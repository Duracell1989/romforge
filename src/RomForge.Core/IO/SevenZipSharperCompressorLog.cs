using System;
using Microsoft.Extensions.Logging;

namespace RomForge.Core.IO
{
    internal static class SevenZipSharperCompressorLog
    {
        private static readonly Action<ILogger, string, Exception?> _existingArchiveDeleteFailed =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(100, nameof(ExistingArchiveDeleteFailed)),
                "Could not remove existing archive {Dest}"
            );

        private static readonly Action<ILogger, string, Exception?> _compressionFailed =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(101, nameof(CompressionFailed)),
                "Compression failed for {Source}"
            );

        private static readonly Action<ILogger, string, Exception?> _nativeLibraryUnavailable =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(102, nameof(NativeLibraryUnavailable)),
                "7-Zip native library not available; re-archive operations will be unavailable: {Error}"
            );

        public static void ExistingArchiveDeleteFailed(ILogger logger, string dest, Exception ex) =>
            _existingArchiveDeleteFailed(logger, dest, ex);

        public static void CompressionFailed(ILogger logger, string source, Exception ex) =>
            _compressionFailed(logger, source, ex);

        public static void NativeLibraryUnavailable(ILogger logger, string error) =>
            _nativeLibraryUnavailable(logger, error, null);
    }
}
