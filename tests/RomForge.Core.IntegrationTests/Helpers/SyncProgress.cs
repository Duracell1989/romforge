using System;

namespace RomForge.Core.IntegrationTests.Helpers
{
    internal sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        internal SyncProgress(Action<T> callback)
        {
            _callback = callback;
        }

        public void Report(T value) => _callback(value);
    }
}
