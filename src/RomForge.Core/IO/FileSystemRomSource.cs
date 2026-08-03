using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using Microsoft.Extensions.Logging;
using SevenZipSharper;
using SevenZipSharper.Detection;
using static RomForge.Core.IO.FileSystemRomSourceLog;

namespace RomForge.Core.IO
{
    // S6672: the logger here is forwarded verbatim to SevenZipExtractor's constructor, which
    // requires ILogger<SevenZipExtractor> specifically - it is not this class's own diagnostic logger.
#pragma warning disable S6672
    public sealed class FileSystemRomSource : IRomSource
    {
        private static readonly HashSet<string> ArchiveExtensions = [".zip", ".7z"];

        private readonly IArchiveExtractor _extractor;
        private readonly ILogger<SevenZipExtractor> _logger;

        public FileSystemRomSource(IArchiveExtractor extractor, ILogger<SevenZipExtractor> logger)
        {
            _extractor = extractor;
            _logger = logger;
        }

        public Task<int> CountAsync(
            string folderPath,
            CancellationToken cancellationToken = default
        )
        {
            return Task.Run(
                () =>
                {
                    EnumerationOptions enumOptions = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                    };
                    int count = 0;
                    foreach (string f in Directory.EnumerateFiles(folderPath, "*", enumOptions))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!string.IsNullOrEmpty(Path.GetExtension(f)))
                            count++;
                    }
                    return count;
                },
                cancellationToken
            );
        }

        public async IAsyncEnumerable<RomContent> EnumerateAsync(
            string folderPath,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            EnumerationOptions enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            };
            foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", enumOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileExt = ToLowerExtension(Path.GetExtension(filePath));
                if (string.IsNullOrEmpty(fileExt))
                    continue;

                var fileInfo = new FileInfo(filePath);
                var content = ArchiveExtensions.Contains(fileExt)
                    ? await BuildArchiveContentAsync(filePath, fileExt, fileInfo, cancellationToken)
                        .ConfigureAwait(false)
                    : BuildRawContent(filePath, fileExt, fileInfo);

                if (content is not null)
                    yield return content;
            }
        }

        // File extensions are normalised to lowercase to match the lowercase archive-extension
        // literals and typical ROM naming; CA1308 (prefer uppercase) does not apply to extensions.
#pragma warning disable CA1308
        private static string ToLowerExtension(string value) => value.ToLowerInvariant();
#pragma warning restore CA1308

        private static RomContent BuildRawContent(
            string filePath,
            string fileExt,
            FileInfo fileInfo
        )
        {
            var ext = fileExt.TrimStart('.');
            return new RomContent
            {
                FilePath = filePath,
                FileExtension = ext,
                RomExtension = ext,
                EntryName = Path.GetFileNameWithoutExtension(filePath),
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc,
                OpenStreamAsync = ct => new ValueTask<Stream>(File.OpenRead(filePath)),
            };
        }

        // Opens the archive briefly to read the entry name, then closes it; the entry is
        // re-extracted lazily when the stream is actually requested (see OpenArchiveEntryStreamAsync).
        private async Task<RomContent?> BuildArchiveContentAsync(
            string filePath,
            string fileExt,
            FileInfo fileInfo,
            CancellationToken cancellationToken
        )
        {
            (string RomExtension, string EntryName)? entryInfo = await PeekEntryAsync(
                    filePath,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (entryInfo is null)
                return null;

            return new RomContent
            {
                FilePath = filePath,
                FileExtension = fileExt.TrimStart('.'),
                RomExtension = entryInfo.Value.RomExtension,
                EntryName = entryInfo.Value.EntryName,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc,
                OpenStreamAsync = ct => OpenArchiveEntryStreamAsync(filePath, ct),
            };
        }

        private async Task<(string RomExtension, string EntryName)?> PeekEntryAsync(
            string filePath,
            CancellationToken cancellationToken
        )
        {
            ArchiveFormat? format = ArchiveFormatDetector.FromExtension(filePath);
            if (format is null)
                return null;

            await using FileStream input = File.OpenRead(filePath);
            using SevenZipExtractor extractor = new SevenZipExtractor(input, format.Value, _logger);

            Result<ArchiveInfo> openResult = await extractor
                .OpenAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (openResult.IsFailed)
                return null;

            Result<IReadOnlyList<ArchiveEntry>> entriesResult = await extractor
                .ListEntriesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (entriesResult.IsFailed)
                return null;

            ArchiveEntry? entry = entriesResult.Value.FirstOrDefault(e => !e.IsDirectory);
            if (entry is null)
                return null;

            string entryFileName = Path.GetFileName(entry.Path);
            return (
                ToLowerExtension(Path.GetExtension(entryFileName).TrimStart('.')),
                Path.GetFileNameWithoutExtension(entryFileName)
            );
        }

        private async ValueTask<Stream> OpenArchiveEntryStreamAsync(
            string filePath,
            CancellationToken ct
        )
        {
            Result<string> extractResult = await _extractor
                .ExtractToTempFileAsync(filePath, ct)
                .ConfigureAwait(false);
            if (extractResult.IsFailed)
            {
                ExtractionFailed(_logger, filePath, extractResult.Errors[0].Message);
                return Stream.Null;
            }

            // FileOptions.DeleteOnClose sweeps the extracted temp file once the caller disposes
            // the stream, so no separate owning wrapper is needed.
            return new FileStream(
                extractResult.Value,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous
            );
        }
    }
#pragma warning restore S6672
}
