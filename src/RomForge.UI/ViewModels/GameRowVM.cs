using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Scanning;
using RomForge.UI.Converters;

namespace RomForge.UI.ViewModels;

public sealed partial class GameRowVM : ObservableObject, IDisposable
{
    private readonly MatchResult _result;
    private readonly string _namingMask;
    private readonly IReadOnlyList<LanguageBit> _languageBits;
    private readonly string? _im1Path;
    private readonly string? _im2Path;
    private bool _disposed;

    [ObservableProperty]
    public partial Bitmap? Im1Bitmap { get; private set; }

    [ObservableProperty]
    public partial Bitmap? Im2Bitmap { get; private set; }

    public GameRowVM(
        MatchResult result,
        string imgsBasePath,
        DatHeader header,
        IReadOnlyList<LanguageBit> languageBits
    )
    {
        _result = result;
        _namingMask = header.RomTitle;
        _languageBits = languageBits;
        ScreenshotsWidth = header.ScreenshotsWidth > 0 ? header.ScreenshotsWidth : 240;
        ScreenshotsHeight = header.ScreenshotsHeight > 0 ? header.ScreenshotsHeight : 160;

        int imgNum = result.Game.ImageNumber;
        if (imgNum > 0)
        {
            _im1Path = ImagePathResolver.ResolveIm1Path(imgsBasePath, header, imgNum);
            _im2Path = ImagePathResolver.ResolveIm2Path(imgsBasePath, header, imgNum);
            _ = LoadImagesAsync();
        }
    }

    public Game Game => _result.Game;
    public MatchStatus Status => _result.Status;
    public bool IsIncorrectlyNamed => _result.IsIncorrectlyNamed;
    public bool IsWrongArchiveType => _result.IsWrongArchiveType;
    public bool IsUntrimmed => _result.IsUntrimmed;
    public bool IsReArchived => _result.IsReArchived;
    public bool IsGood => _result.IsGood;
    public ScannedRom? ScannedRom => _result.ScannedRom;

    public int ReleaseNumber => _result.Game.ReleaseNumber;
    public string Title => _result.Game.Title;
    public string? Publisher => _result.Game.Publisher;
    public string? SaveType => _result.Game.SaveType;
    public string Location => DecodeLocation(_result.Game.Location);
    public string Language => DecodeLanguage(_result.Game.Language, _languageBits);
    public string RomSize => FormatSize(_result.Game.RomSize);
    public string? FilePath => _result.ScannedRom?.FilePath;
    public int ScreenshotsWidth { get; }
    public int ScreenshotsHeight { get; }

    public string StatusText
    {
        get
        {
            if (_result.Status == MatchStatus.Missing) return "Missing";
            if (_result.IsUntrimmed) return "Untrimmed";
            if (_result.IsWrongArchiveType) return "Wrong Archive";
            if (_result.IsIncorrectlyNamed) return "Incorrectly Named";
            if (_result.IsReArchived) return "Good";
            return "Verified";
        }
    }

    public IBrush StatusBrush
    {
        get
        {
            if (_result.Status == MatchStatus.Missing) return StatusColors.Missing;
            if (_result.IsUntrimmed) return StatusColors.Untrimmed;
            if (_result.IsWrongArchiveType) return StatusColors.WrongArchiveType;
            if (_result.IsIncorrectlyNamed) return StatusColors.IncorrectlyNamed;
            if (_result.IsReArchived) return StatusColors.Good;
            return StatusColors.Verified;
        }
    }

    /// <summary>
    /// Numeric key for status-column sorting. Lower = higher priority issue.
    /// </summary>
    internal int StatusSortKey
    {
        get
        {
            if (_result.Status == MatchStatus.Missing) return 0;
            if (_result.IsUntrimmed) return 1;
            if (_result.IsWrongArchiveType) return 2;
            if (_result.IsIncorrectlyNamed) return 3;
            if (_result.IsReArchived) return 5;
            return 4;
        }
    }

    public string ReArchivedText => _result.IsReArchived ? "✓" : "–";

    public string? ExpectedFileName =>
        _result.IsIncorrectlyNamed && !string.IsNullOrEmpty(_namingMask)
            ? NamingMask.Expand(_namingMask, _result.Game)
            : null;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Im1Bitmap?.Dispose();
        Im2Bitmap?.Dispose();
    }

    private async Task LoadImagesAsync()
    {
        if (_im1Path is not null)
            Im1Bitmap = await Task.Run(() => LoadBitmapSafe(_im1Path));
        if (_im2Path is not null)
            Im2Bitmap = await Task.Run(() => LoadBitmapSafe(_im2Path));
    }

    private static Bitmap? LoadBitmapSafe(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeLanguage(int bitmask, IReadOnlyList<LanguageBit> bits)
    {
        if (bitmask == 0)
            return string.Empty;
        if (bits.Count == 0)
            return bitmask.ToString();
        List<string> labels = bits
            .Where(b => (bitmask & (1 << b.BitIndex)) != 0)
            .Select(b => b.Label)
            .ToList();
        return labels.Count > 0 ? string.Join(" ", labels) : bitmask.ToString();
    }

    private static string DecodeLocation(int location) =>
        location switch
        {
            0 => "(Unknown)",
            1 => "(EU)",
            2 => "(US)",
            3 => "(DE)",
            4 => "(Others)",
            5 => "(ES)",
            6 => "(FR)",
            7 => "(JP)",
            8 => "(AU)",
            9 => "(IT)",
            10 => "(HK)",
            11 => "(NL)",
            12 => "(KR)",
            13 => "(BR)",
            16 => "(CN)",
            18 => "(SE)",
            19 => "(CA)",
            22 => "(PT)",
            _ => location.ToString(),
        };

    private static string FormatSize(long bytes) =>
        bytes switch
        {
            0 => string.Empty,
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / 1024.0:F1} KB",
        };
}
