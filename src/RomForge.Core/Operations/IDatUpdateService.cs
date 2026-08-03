using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Models;

namespace RomForge.Core.Operations
{
    /// <summary>
    /// Checks whether a newer DAT version is available and downloads the update. UI concerns
    /// (confirmation, progress display, reloading the DAT) stay with the caller.
    /// </summary>
    public interface IDatUpdateService
    {
        /// <summary>
        /// Fetches the latest published version for <paramref name="header"/> and compares it against
        /// the currently loaded version.
        /// </summary>
        /// <returns>
        /// A success carrying the comparison outcome, or a failure if no version URL is configured or
        /// the fetch failed.
        /// </returns>
        Task<Result<DatUpdateCheck>> CheckForUpdateAsync(
            DatHeader header,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Downloads the updated DAT file described by <paramref name="header"/> into the managed DATs
        /// directory.
        /// </summary>
        /// <returns>A success, or a failure if no download URL is configured or the download failed.</returns>
        Task<Result> DownloadUpdateAsync(
            DatHeader header,
            IProgress<int>? progress,
            CancellationToken cancellationToken = default
        );
    }
}
