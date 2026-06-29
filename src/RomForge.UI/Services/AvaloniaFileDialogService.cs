using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace RomForge.UI.Services;

internal sealed class AvaloniaFileDialogService : IFileDialogService
{
    private readonly Func<Window?> _getWindow;

    private static readonly FilePickerFileType DatFileType = new("OfflineList DAT")
    {
        Patterns = ["*.zip", "*.xml"],
    };

    public AvaloniaFileDialogService(Func<Window?> getWindow)
    {
        _getWindow = getWindow;
    }

    public async Task<string?> PickDatFileAsync()
    {
        Window? topLevel = _getWindow();
        if (topLevel is null)
            return null;

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open DAT File",
                AllowMultiple = false,
                FileTypeFilter = [DatFileType],
            }
        );

        return files.Count == 1 ? files[0].Path.LocalPath : null;
    }

    public Task<string?> PickRomFolderAsync() => PickFolderAsync("Select ROM Folder");

    public Task<string?> PickUnverifiedDestinationAsync() =>
        PickFolderAsync("Move Unverified Files To…");

    private async Task<string?> PickFolderAsync(string title)
    {
        Window? topLevel = _getWindow();
        if (topLevel is null)
            return null;

        IReadOnlyList<IStorageFolder> folders =
            await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = title, AllowMultiple = false }
            );

        return folders.Count == 1 ? folders[0].Path.LocalPath : null;
    }
}
