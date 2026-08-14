using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Matching;

namespace RomForge.Core.Operations
{
    /// <summary>
    /// Re-archives a single ROM: extracts it, recompresses it to the target format with the correct
    /// entry name, places the result over the original, verifies the placed archive's CRC, and
    /// records the re-archive.
    /// </summary>
    public interface IRomReArchiveService
    {
        /// <summary>
        /// Re-archives the ROM described by <paramref name="match"/> to <paramref name="target"/>.
        /// After placement, the newly-written archive is read back and its CRC verified against
        /// <see cref="Matching.MatchResult.Game"/>'s expected CRC before the re-archive is recorded.
        /// </summary>
        /// <param name="scanCache">
        /// When provided, the verified CRC is written into the cache under the placed archive's
        /// current size and last-write time, so the next full scan does not need to recompute it.
        /// </param>
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
            IRomScanCache? scanCache = null,
            IProgress<int>? compressionProgress = null
        );
    }
}
