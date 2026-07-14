using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO;

/// <summary>
/// Downloads updated DAT files.
/// </summary>
public interface IDatDownloader
{
    /// <summary>
    /// Downloads a DAT file to <paramref name="destDir"/>.
    /// </summary>
    /// <returns>The full path of the downloaded file on success.</returns>
    Task<Result<string>> DownloadDatAsync(
        string url,
        string destDir,
        string? fileName,
        IProgress<int>? progress,
        CancellationToken ct = default
    );
}
