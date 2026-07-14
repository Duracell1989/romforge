using System.Threading;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO;

/// <summary>
/// Downloads a single image file from a URL to a local path.
/// </summary>
public interface IImageDownloader
{
    /// <summary>
    /// Downloads the image at <paramref name="imageUrl"/> to <paramref name="destPath"/>,
    /// creating the destination directory if needed. A non-success HTTP status (e.g. a
    /// missing image) is returned as a failed <see cref="Result"/>, not thrown.
    /// </summary>
    Task<Result> DownloadImageAsync(
        string imageUrl,
        string destPath,
        CancellationToken ct = default
    );
}
