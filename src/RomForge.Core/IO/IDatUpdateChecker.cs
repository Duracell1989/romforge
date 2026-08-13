using System.Threading;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO
{
    /// <summary>
    /// Checks whether a newer version of a DAT file is available.
    /// </summary>
    public interface IDatUpdateChecker
    {
        /// <summary>
        /// Fetches the latest version string from the given URL.
        /// </summary>
        Task<Result<string>> FetchLatestVersionAsync(string versionUrl, CancellationToken ct = default);
    }
}
