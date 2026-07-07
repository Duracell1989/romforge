using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RomForge.Core.Models;
using RomForge.Core.Services;
using RomForge.UI.Services;

namespace RomForge.UI.ViewModels;

/// <summary>
/// Backs the settings dialog. Edits a working copy of the application preferences and
/// persists them on save; the host is notified to close the window via <see cref="RequestClose"/>.
/// </summary>
public sealed partial class SettingsVM : VMBase
{
    private readonly AppPreferencesService _preferencesService;
    private readonly IFileDialogService _fileDialogs;

    [ObservableProperty]
    public partial string ArchiveFormat { get; set; }

    [ObservableProperty]
    public partial string? UnverifiedFolder { get; set; }

    public IReadOnlyList<string> ArchiveFormats { get; } = ["7z", "zip"];

    /// <summary>
    /// Invoked when the dialog should close. The argument is <c>true</c> when the user saved,
    /// <c>false</c> when they cancelled.
    /// </summary>
    public Action<bool>? RequestClose { get; set; }

    public SettingsVM(
        AppPreferencesService preferencesService,
        IFileDialogService fileDialogs,
        AppPreferences current
    )
    {
        _preferencesService = preferencesService;
        _fileDialogs = fileDialogs;
        ArchiveFormat = current.DefaultArchiveFormat;
        UnverifiedFolder = current.UnverifiedFolder;
    }

    [RelayCommand]
    private async Task BrowseUnverifiedFolderAsync()
    {
        string? picked = await _fileDialogs.PickUnverifiedDestinationAsync();
        if (picked is not null)
            UnverifiedFolder = picked;
    }

    [RelayCommand]
    private void ClearUnverifiedFolder() => UnverifiedFolder = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _preferencesService.UpdateSettingsAsync(ArchiveFormat, UnverifiedFolder);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
