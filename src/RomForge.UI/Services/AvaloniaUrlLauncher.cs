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
        // Only ever hand the OS launcher a well-formed http(s) URL. The address originates
        // from external release JSON, so a malformed or non-web scheme is rejected here rather
        // than thrown into Uri/LaunchUriAsync.
        if (!IsLaunchableHttpUrl(url))
            return false;

        var window = _getWindow();
        if (window is null)
            return false;

        return await window.Launcher.LaunchUriAsync(new Uri(url));
    }

    internal static bool IsLaunchableHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
