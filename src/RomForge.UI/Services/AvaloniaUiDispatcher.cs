using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace RomForge.UI.Services;

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);
}
