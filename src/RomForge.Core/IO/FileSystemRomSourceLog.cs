using System;
using Microsoft.Extensions.Logging;

namespace RomForge.Core.IO
{
    internal static class FileSystemRomSourceLog
    {
        private static readonly Action<ILogger, string, string, Exception?> ExtractionFailedMessage = LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(110, nameof(ExtractionFailed)),
            "Could not extract {FilePath}: {Error}"
        );

        public static void ExtractionFailed(ILogger logger, string filePath, string error) => ExtractionFailedMessage(logger, filePath, error, null);
    }
}
