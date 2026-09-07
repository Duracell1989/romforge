using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace RomForge.UI.ViewModels
{
    public sealed partial class GameRowVM : ObservableObject, IDisposable
    {
        private readonly IReadOnlyList<LanguageBit> _languageBits;
        private readonly string? _im1Path;
        private readonly string? _im2Path;
        private bool _disposed;

        [ObservableProperty]
        public partial Bitmap? Im1Bitmap { get; private set; }

        [ObservableProperty]
        public partial Bitmap? Im2Bitmap { get; private set; }

        public GameRowVM(MatchResult result, string imagesBasePath, DatHeader header, IReadOnlyList<LanguageBit> languageBits)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(header);

            Result = result;
            _languageBits = languageBits;
            ScreenshotsWidth = header.ScreenshotsWidth > 0 ? header.ScreenshotsWidth : 240;
            ScreenshotsHeight = header.ScreenshotsHeight > 0 ? header.ScreenshotsHeight : 160;

            var imgNum = result.Game.ImageNumber;
            if (imgNum <= 0)
                return;
            _im1Path = ImagePathResolver.ResolveIm1Path(imagesBasePath, header, imgNum);
            _im2Path = ImagePathResolver.ResolveIm2Path(imagesBasePath, header, imgNum);
            _ = LoadImagesAsync();
        }

        /// <summary>
        /// The underlying match this row wraps, for handing to Core operation services.
        /// </summary>
        internal MatchResult Result { get; }

        public Game Game => Result.Game;
        public MatchStatus Status => Result.Status;
        public bool IsIncorrectlyNamed => Result.IsIncorrectlyNamed;
        public bool IsEntryMisnamed => Result.IsEntryMisnamed;
        public bool IsWrongArchiveType => Result.IsWrongArchiveType;
        public bool IsUntrimmed => Result.IsUntrimmed;
        public bool IsReArchived => Result.IsReArchived;
        public bool IsGood => Result.IsGood;
        public RomStatus DisplayStatus => Result.DisplayStatus;
        public ScannedRom? ScannedRom => Result.ScannedRom;

        public int ReleaseNumber => Result.Game.ReleaseNumber;
        public string Title => Result.Game.Title;
        public string? Publisher => Result.Game.Publisher;
        public string? SaveType => Result.Game.SaveType;
        public string Location => DecodeLocation(Result.Game.Location);
        public string Language => DecodeLanguage(Result.Game.Language, _languageBits);
        public string RomSize => FormatSize(Result.Game.RomSize);
        public string? FilePath => Result.ScannedRom?.FilePath;
        public int ScreenshotsWidth { get; }
        public int ScreenshotsHeight { get; }

        public string StatusText =>
            DisplayStatus switch
            {
                RomStatus.Missing => "Missing",
                RomStatus.Untrimmed => "Untrimmed",
                RomStatus.WrongArchive => "Wrong Archive",
                RomStatus.IncorrectlyNamed => "Incorrectly Named",
                RomStatus.Good => "Good",
                RomStatus.Verified => "Verified",
                _ => "Verified",
            };

        public IBrush StatusBrush =>
            DisplayStatus switch
            {
                RomStatus.Missing => StatusColors.Missing,
                RomStatus.Untrimmed => StatusColors.Untrimmed,
                RomStatus.WrongArchive => StatusColors.WrongArchiveType,
                RomStatus.IncorrectlyNamed => StatusColors.IncorrectlyNamed,
                RomStatus.Good => StatusColors.Good,
                RomStatus.Verified => StatusColors.Verified,
                _ => StatusColors.Verified,
            };

        /// <summary>
        /// Numeric key for status-column sorting. Lower = higher priority issue.
        /// </summary>
        internal int StatusSortKey =>
            DisplayStatus switch
            {
                RomStatus.Missing => 0,
                RomStatus.Untrimmed => 1,
                RomStatus.WrongArchive => 2,
                RomStatus.IncorrectlyNamed => 3,
                RomStatus.Verified => 4,
                RomStatus.Good => 5,
                _ => 4,
            };

        public string ReArchivedText => Result.IsReArchived ? "✓" : "–";

        public string? ExpectedFileName => Result.IsIncorrectlyNamed ? NamingMask.Expand(NamingMask.DefaultMask, Result.Game) : null;

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
            // CA1031: a bad/locked/corrupt image file must never crash the row — the decoder can
            // surface a variety of IO and native exception types, so any failure means "no image".
#pragma warning disable CA1031
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }

        private static string DecodeLanguage(int bitmask, IReadOnlyList<LanguageBit> bits)
        {
            if (bitmask == 0)
                return string.Empty;
            if (bits.Count == 0)
                return bitmask.ToString(CultureInfo.InvariantCulture);
            var labels = bits.Where(b => (bitmask & (1 << b.BitIndex)) != 0).Select(b => b.Label).ToList();
            return labels.Count > 0 ? string.Join(" ", labels) : bitmask.ToString(CultureInfo.InvariantCulture);
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
                _ => location.ToString(CultureInfo.InvariantCulture),
            };

        private static string FormatSize(long bytes) =>
            bytes switch
            {
                0 => string.Empty,
                >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
                _ => $"{bytes / 1024.0:F1} KB",
            };
    }
}
