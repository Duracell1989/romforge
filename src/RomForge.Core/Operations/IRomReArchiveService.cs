using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Matching;

namespace RomForge.Core.Operations
{
    /// <summary>
    /// Re-archives a single ROM: extracts it, recompresses it to the target format with the correct
    /// entry name, places the result over the original, and records the re-archive.
    /// </summary>
    public interface IRomReArchiveService
    {
        /// <summary>
        /// Re-archives the ROM described by <paramref name="match"/> to <paramref name="target"/>.
        /// </summary>
        /// <returns>
        /// A success carrying the updated <see cref="MatchResult"/>, or a failure whose message is
        /// already prefixed with the source file name.
        /// </returns>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        Task<Result<MatchResult>> ReArchiveAsync(
            MatchResult match,
            (string From, string To) target,
            string archiveFormat,
            string datName,
            CancellationToken cancellationToken,
            IProgress<int>? compressionProgress = null
        );
    }
}
