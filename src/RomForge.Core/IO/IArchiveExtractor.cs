using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO
{
    public interface IArchiveExtractor
    {
        /// <summary>
        /// Extracts the single ROM entry from <paramref name="archivePath"/> to a temporary file
        /// and returns its path. The caller owns the extracted file and is responsible for deleting it.
        /// </summary>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        Task<Result<string>> ExtractToTempFileAsync(string archivePath, CancellationToken cancellationToken = default);
    }
}
