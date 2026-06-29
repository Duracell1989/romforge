using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Scanning;

namespace RomForge.UI.ViewModels;

/// <summary>
/// Per-DAT state: games, ROM folder, filter criteria, and computed display strings.
/// Operations stay in MainWindowVM; this is a pure state container.
/// </summary>
public partial class LoadedDatVM : VMBase
{
    private enum SortColumn { None, ReleaseNumber, Title, Publisher, Location, Language, ReArchived, Status }

    private readonly DatFile _datFile;
    private readonly string _imgsBasePath;
    private readonly DatConfig? _config;
    private readonly ObservableCollection<GameRowVM> _filteredGames = new ObservableCollection<GameRowVM>();
    private SortColumn _sortColumn = SortColumn.None;
    private bool _sortDescending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameCount))]
    [NotifyPropertyChangedFor(nameof(DisplaySubtitle))]
    public partial ObservableCollection<GameRowVM> Games { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySubtitle))]
    public partial string? RomFolder { get; set; }

    [ObservableProperty]
    public partial bool ShowVerified { get; set; }

    [ObservableProperty]
    public partial bool ShowMissing { get; set; }

    [ObservableProperty]
    public partial bool ShowIncorrectlyNamed { get; set; }

    [ObservableProperty]
    public partial bool ShowWrongArchiveType { get; set; }

    [ObservableProperty]
    public partial bool ShowUntrimmed { get; set; }

    [ObservableProperty]
    public partial bool ShowGood { get; set; }

    [ObservableProperty]
    public partial string TitleFilter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnmatchedCount))]
    public partial IReadOnlyList<ScannedRom> UnmatchedRoms { get; set; }

    public int UnmatchedCount => UnmatchedRoms.Count;

    public LoadedDatVM(DatFile datFile, string datFilePath, DatConfig? config = null)
    {
        _datFile = datFile;
        DatFilePath = datFilePath;
        _config = config;
        _imgsBasePath = ResolveImgsBasePath(datFilePath);
        Games = new ObservableCollection<GameRowVM>();
        UnmatchedRoms = [];
        ShowVerified = true;
        ShowMissing = true;
        ShowIncorrectlyNamed = true;
        ShowWrongArchiveType = true;
        ShowUntrimmed = true;
        ShowGood = true;
        TitleFilter = string.Empty;
    }

    public string DatFilePath { get; }
    public DatFile DatFile => _datFile;
    public string ImgsBasePath => _imgsBasePath;
    public string SystemName => _datFile.Header.System;
    public string DatName => _datFile.Header.DatName;
    public int GameCount => Games.Count;
    public string DisplayTitle => string.IsNullOrEmpty(SystemName) ? DatName : SystemName;
    public ObservableCollection<GameRowVM> FilteredGames => _filteredGames;
    public int FilteredCount => _filteredGames.Count;

    public string DisplaySubtitle =>
        $"{GameCount} games  •  {(RomFolder is null ? "No folder" : Path.GetFileName(RomFolder))}";

    public string StatusSummary
    {
        get
        {
            if (Games.Count == 0)
                return "No scan yet";

            int good = Games.Count(g => g.IsGood);
            int untrimmed = Games.Count(g => g.IsUntrimmed);
            int wrongArchiveType = Games.Count(g => !g.IsUntrimmed && g.IsWrongArchiveType);
            int incorrectlyNamed = Games.Count(g => !g.IsUntrimmed && !g.IsWrongArchiveType && g.IsIncorrectlyNamed);
            int missing = Games.Count(g => g.Status == MatchStatus.Missing);

            List<string> segments = new List<string> { $"{Games.Count} games" };

            if (good > 0)
                segments.Add($"{good} good");
            if (incorrectlyNamed > 0)
                segments.Add($"{incorrectlyNamed} incorrectly named");
            if (wrongArchiveType > 0)
                segments.Add($"{wrongArchiveType} wrong archive");
            if (untrimmed > 0)
                segments.Add($"{untrimmed} untrimmed");
            segments.Add($"{missing} missing");

            string summary = string.Join("  •  ", segments);

            return IsFilterActive ? $"Showing {FilteredCount} of {Games.Count}  •  {summary}" : summary;
        }
    }

    private bool IsFilterActive =>
        !ShowVerified
        || !ShowMissing
        || !ShowIncorrectlyNamed
        || !ShowWrongArchiveType
        || !ShowUntrimmed
        || !ShowGood
        || !string.IsNullOrEmpty(TitleFilter);

    private static string ResolveImgsBasePath(string datFilePath)
    {
        string datDir = Path.GetDirectoryName(datFilePath) ?? string.Empty;
        string parentDir = Path.GetDirectoryName(datDir) ?? string.Empty;
        string parentImgs = Path.Combine(parentDir, "imgs");
        return Directory.Exists(parentImgs) ? parentImgs : Path.Combine(datDir, "imgs");
    }

    public string ReleaseNumberSortIndicator => GetSortIndicator(SortColumn.ReleaseNumber);
    public string TitleSortIndicator => GetSortIndicator(SortColumn.Title);
    public string PublisherSortIndicator => GetSortIndicator(SortColumn.Publisher);
    public string LocationSortIndicator => GetSortIndicator(SortColumn.Location);
    public string LanguageSortIndicator => GetSortIndicator(SortColumn.Language);
    public string ReArchivedSortIndicator => GetSortIndicator(SortColumn.ReArchived);
    public string StatusSortIndicator => GetSortIndicator(SortColumn.Status);

    [RelayCommand]
    private void SortBy(string? column)
    {
        SortColumn parsed = column switch
        {
            "ReleaseNumber" => SortColumn.ReleaseNumber,
            "Title" => SortColumn.Title,
            "Publisher" => SortColumn.Publisher,
            "Location" => SortColumn.Location,
            "Language" => SortColumn.Language,
            "ReArchived" => SortColumn.ReArchived,
            "Status" => SortColumn.Status,
            _ => SortColumn.None,
        };

        if (_sortColumn == parsed)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = parsed;
            _sortDescending = false;
        }

        RefreshFilter();
        OnPropertyChanged(nameof(ReleaseNumberSortIndicator));
        OnPropertyChanged(nameof(TitleSortIndicator));
        OnPropertyChanged(nameof(PublisherSortIndicator));
        OnPropertyChanged(nameof(LocationSortIndicator));
        OnPropertyChanged(nameof(LanguageSortIndicator));
        OnPropertyChanged(nameof(ReArchivedSortIndicator));
        OnPropertyChanged(nameof(StatusSortIndicator));
    }

    private string GetSortIndicator(SortColumn column)
    {
        if (_sortColumn != column)
            return string.Empty;
        return _sortDescending ? " ▼" : " ▲";
    }

    private IEnumerable<GameRowVM> ApplySort(IEnumerable<GameRowVM> items) =>
        _sortColumn switch
        {
            SortColumn.ReleaseNumber => _sortDescending
                ? items.OrderByDescending(g => g.ReleaseNumber)
                : items.OrderBy(g => g.ReleaseNumber),
            SortColumn.Title => _sortDescending
                ? items.OrderByDescending(g => g.Title, System.StringComparer.CurrentCultureIgnoreCase)
                : items.OrderBy(g => g.Title, System.StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Publisher => _sortDescending
                ? items.OrderByDescending(
                    g => g.Publisher ?? string.Empty,
                    System.StringComparer.CurrentCultureIgnoreCase
                )
                : items.OrderBy(
                    g => g.Publisher ?? string.Empty,
                    System.StringComparer.CurrentCultureIgnoreCase
                ),
            SortColumn.Location => _sortDescending
                ? items.OrderByDescending(g => g.Location, System.StringComparer.CurrentCultureIgnoreCase)
                : items.OrderBy(g => g.Location, System.StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Language => _sortDescending
                ? items.OrderByDescending(g => g.Language, System.StringComparer.CurrentCultureIgnoreCase)
                : items.OrderBy(g => g.Language, System.StringComparer.CurrentCultureIgnoreCase),
            SortColumn.ReArchived => _sortDescending
                ? items.OrderByDescending(g => g.IsReArchived)
                : items.OrderBy(g => g.IsReArchived),
            SortColumn.Status => _sortDescending
                ? items.OrderByDescending(g => g.StatusSortKey)
                : items.OrderBy(g => g.StatusSortKey),
            _ => items,
        };

    internal GameRowVM BuildGameRow(MatchResult result) =>
        new GameRowVM(result, _imgsBasePath, _datFile.Header, _config?.LanguageBits ?? []);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (
            e.PropertyName is nameof(TitleFilter)
                or nameof(ShowVerified)
                or nameof(ShowMissing)
                or nameof(ShowIncorrectlyNamed)
                or nameof(ShowWrongArchiveType)
                or nameof(ShowUntrimmed)
                or nameof(ShowGood)
        )
            RefreshFilter();
    }

    private void RefreshFilter()
    {
        _filteredGames.Clear();
        foreach (GameRowVM g in ApplySort(Games.Where(MatchesFilter)))
            _filteredGames.Add(g);
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(FilteredCount));
    }

    private bool MatchesFilter(GameRowVM vm)
    {
        if (vm.Status == MatchStatus.Missing && !ShowMissing)
            return false;
        if (vm.IsUntrimmed && !ShowUntrimmed)
            return false;
        if (!vm.IsUntrimmed && vm.IsWrongArchiveType && !ShowWrongArchiveType)
            return false;
        if (!vm.IsUntrimmed && !vm.IsWrongArchiveType && vm.IsIncorrectlyNamed && !ShowIncorrectlyNamed)
            return false;
        if (vm.IsGood && !ShowGood)
            return false;
        if (vm.IsGood && !vm.IsReArchived && !ShowVerified)
            return false;
        if (
            !string.IsNullOrEmpty(TitleFilter)
            && !vm.Title.Contains(TitleFilter, System.StringComparison.OrdinalIgnoreCase)
        )
            return false;
        return true;
    }

    partial void OnGamesChanged(
        ObservableCollection<GameRowVM> oldValue,
        ObservableCollection<GameRowVM> newValue
    )
    {
#pragma warning disable CS8625 // generator declares oldValue non-nullable but backing field starts null
        if (oldValue is not null)
        {
#pragma warning restore CS8625
            oldValue.CollectionChanged -= OnGamesCollectionChanged;
            foreach (GameRowVM vm in oldValue)
                vm.Dispose();
        }
        newValue.CollectionChanged += OnGamesCollectionChanged;
        RefreshFilter();
    }

    private void OnGamesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(GameCount));
        OnPropertyChanged(nameof(DisplaySubtitle));
        RefreshFilter();
    }
}
