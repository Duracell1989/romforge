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

    public Task<Result<string>> ExtractToTempFileAsync(
        string archivePath,
        CancellationToken cancellationToken = default
    ) =>
        Task.Run(
            () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using IArchive archive = ArchiveFactory.OpenArchive(archivePath);
                    IArchiveEntry? entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory);
                    if (entry is null)
                        return Result.Fail($"Archive contains no entries: {archivePath}");

                    string ext = Path.GetExtension(entry.Key ?? string.Empty);
                    string tempFile = Path.Combine(
                        _tempDirectory,
                        Path.GetRandomFileName() + ext
                    );

                    using FileStream dest = File.Create(tempFile);
                    using Stream src = entry.OpenEntryStream();
                    src.CopyTo(dest);
                    return Result.Ok(tempFile);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                    when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    return Result.Fail(new ExceptionalError(ex));
                }
            },
            cancellationToken
        );
}
