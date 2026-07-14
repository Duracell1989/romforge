using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace RomForge.UI.Services;

internal sealed class AvaloniaUrlLauncher : IUrlLauncher
{
    private readonly Func<Window?> _getWindow;

    public AvaloniaUrlLauncher(Func<Window?> getWindow)
    {
        _getWindow = getWindow;
    }

    public async Task<bool> OpenUrlAsync(string url)
    {
        Window? window = _getWindow();
        if (window is null)
            return false;

        return await window.Launcher.LaunchUriAsync(new Uri(url));
    }
}
