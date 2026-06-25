using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RomForge.Core.IO;

public interface IRomSource
{
    IAsyncEnumerable<RomContent> EnumerateAsync(
        string folderPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns a fast estimate of the number of files in the folder without opening archives.
    /// Used to seed progress reporting before enumeration begins.
    /// </summary>
    Task<int> CountAsync(string folderPath, CancellationToken cancellationToken = default);
}
