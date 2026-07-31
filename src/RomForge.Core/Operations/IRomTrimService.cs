using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Matching;

namespace RomForge.Core.Operations;

/// <summary>
/// Trims the trailing padding off an over-sized ROM: extracts it, truncates it to the DAT's declared
/// size, recompresses it with the correct entry name, and places the result over the original.
/// </summary>
public interface IRomTrimService
{
    /// <summary>
    /// Trims the ROM described by <paramref name="match"/> and writes it to <paramref name="target"/>.
    /// </summary>
    /// <returns>
    /// A success carrying the updated <see cref="MatchResult"/>, or a failure whose message is
    /// already prefixed with the source file name.
    /// </returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    Task<Result<MatchResult>> TrimAsync(
        MatchResult match,
        (string From, string To) target,
        string archiveFormat,
        CancellationToken cancellationToken,
        IProgress<int>? compressionProgress = null
    );
}
