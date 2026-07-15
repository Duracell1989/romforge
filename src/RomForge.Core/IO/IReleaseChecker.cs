using System.Threading;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO;

/// <summary>
/// Fetches the latest published application release.
/// </summary>
public interface IReleaseChecker
{
    /// <summary>
    /// Fetches the latest published release. A network or parse failure is returned as a failed
    /// <see cref="Result{T}"/> rather than thrown.
    /// </summary>
    Task<Result<ReleaseInfo>> FetchLatestReleaseAsync(CancellationToken ct = default);
}
