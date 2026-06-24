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
        var folderName = string.IsNullOrEmpty(header.ImFolder)
            ? header.DatName
            : header.ImFolder;
        var subFolder = GetSubfolder(imageNumber);
        var path = Path.Combine(
            imgsBasePath,
            folderName,
            subFolder,
            $"{imageNumber}{suffix}.png"
        );
        return File.Exists(path) ? path : null;
    }

    internal static string GetSubfolder(int imageNumber)
    {
        var start = ((imageNumber - 1) / 500) * 500 + 1;
        return $"{start}-{start + 499}";
    }
}
