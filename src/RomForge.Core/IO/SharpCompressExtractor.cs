using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using SharpCompress.Archives;

namespace RomForge.Core.IO;

public sealed class SharpCompressExtractor : IArchiveExtractor
{
    private readonly string _tempDirectory;

    public SharpCompressExtractor(string tempDirectory = "")
    {
        _tempDirectory = string.IsNullOrEmpty(tempDirectory)
            ? Path.GetTempPath()
            : tempDirectory;
    }

    public async Task<Result<string>> ExtractToTempFileAsync(
        string archivePath,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory);
            if (entry is null)
                return Result.Fail($"Archive contains no entries: {archivePath}");

            var ext = Path.GetExtension(entry.Key ?? string.Empty);
            var tempFile = Path.Combine(_tempDirectory, Path.GetRandomFileName() + ext);

            await using var dest = File.Create(tempFile);
            await using var src = await entry
                .OpenEntryStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await src.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);

            return Result.Ok(tempFile);
        }
        catch (Exception ex)
            when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Result.Fail(new ExceptionalError(ex));
        }
    }
}
