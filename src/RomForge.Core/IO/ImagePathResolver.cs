using System.IO;
using RomForge.Core.Models;

namespace RomForge.Core.IO;

public static class ImagePathResolver
{
    public static string? ResolveIm1Path(string imgsBasePath, DatHeader header, int imageNumber) =>
        imageNumber > 0 ? Resolve(imgsBasePath, header, imageNumber, "a") : null;

    public static string? ResolveIm2Path(string imgsBasePath, DatHeader header, int imageNumber) =>
        imageNumber > 0 ? Resolve(imgsBasePath, header, imageNumber, "b") : null;

    private static string? Resolve(
        string imgsBasePath,
        DatHeader header,
        int imageNumber,
        string suffix
    )
    {
        var path = Path.Combine(imgsBasePath, BuildRelativeLocalPath(header, imageNumber, suffix));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The image's path relative to the imgs base directory:
    /// <c>&lt;folder&gt;/&lt;subfolder&gt;/&lt;n&gt;&lt;suffix&gt;.png</c>. Shared by the
    /// local resolver and the downloader so both agree on the on-disk layout.
    /// </summary>
    internal static string BuildRelativeLocalPath(DatHeader header, int imageNumber, string suffix)
    {
        var folderName = string.IsNullOrEmpty(header.ImFolder) ? header.DatName : header.ImFolder;
        return Path.Combine(folderName, GetSubfolder(imageNumber), $"{imageNumber}{suffix}.png");
    }

    /// <summary>
    /// The image's path relative to the OfflineList <c>imURL</c> base:
    /// <c>&lt;subfolder&gt;/&lt;n&gt;&lt;suffix&gt;.png</c>. Unlike the local path, the DAT
    /// folder name is not part of the URL.
    /// </summary>
    internal static string BuildRelativeUrlPath(int imageNumber, string suffix) =>
        $"{GetSubfolder(imageNumber)}/{imageNumber}{suffix}.png";

    internal static string GetSubfolder(int imageNumber)
    {
        var start = ((imageNumber - 1) / 500) * 500 + 1;
        return $"{start}-{start + 499}";
    }
}
