using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO;

public interface IArchiveExtractor
{
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    Task<Result<string>> ExtractToTempFileAsync(
        string archivePath,
        CancellationToken cancellationToken = default
    );
}
