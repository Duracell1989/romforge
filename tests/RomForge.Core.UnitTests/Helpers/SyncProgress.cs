using System;

namespace RomForge.Core.UnitTests.Helpers;

/// <summary>
/// <see cref="IProgress{T}"/> that invokes the callback synchronously on the calling thread,
/// so progress reports are observed deterministically in tests.
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _callback;

    internal SyncProgress(Action<T> callback)
    {
        _callback = callback;
    }

    public void Report(T value) => _callback(value);
}
