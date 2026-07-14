using System.Threading.Tasks;

namespace RomForge.UI.Services;

/// <summary>
/// Opens external URLs in the user's default browser.
/// </summary>
public interface IUrlLauncher
{
    /// <summary>
    /// Opens the given URL in the default browser. Returns <c>false</c> if it could not be launched.
    /// </summary>
    Task<bool> OpenUrlAsync(string url);
}
