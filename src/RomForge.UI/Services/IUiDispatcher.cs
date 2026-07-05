using System;
using System.Threading.Tasks;

namespace RomForge.UI.Services;

/// <summary>
/// Marshals work onto the UI thread.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Invokes <paramref name="action"/> on the UI thread and awaits its completion.
    /// </summary>
    Task InvokeAsync(Action action);
}
