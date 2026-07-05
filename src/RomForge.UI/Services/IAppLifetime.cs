namespace RomForge.UI.Services;

/// <summary>
/// Controls the application lifetime.
/// </summary>
public interface IAppLifetime
{
    /// <summary>
    /// Requests the application to shut down.
    /// </summary>
    void Shutdown();
}
