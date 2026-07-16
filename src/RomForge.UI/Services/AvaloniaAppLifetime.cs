using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace RomForge.UI.Services;

internal sealed class AvaloniaAppLifetime : IAppLifetime
{
    public void Shutdown()
    {
        if (
            Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime lifetime
        )
        {
            lifetime.Shutdown();
        }
    }
}
