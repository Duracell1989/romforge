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

namespace RomForge.Core.IO
{
    // S6672: the logger here is forwarded verbatim to SevenZipExtractor's constructor, which
    // requires ILogger<SevenZipExtractor> specifically - it is not this class's own diagnostic logger.
#pragma warning disable S6672
    public sealed class SevenZipSharperExtractor : IArchiveExtractor
    {
        private readonly ILogger<SevenZipExtractor> _logger;
        private readonly string _tempDirectory;

        public SevenZipSharperExtractor(
            ILogger<SevenZipExtractor> logger,
            string tempDirectory = ""
        )
        {
            _logger = logger;
            _tempDirectory = string.IsNullOrEmpty(tempDirectory)
                ? Path.GetTempPath()
                : tempDirectory;
        }

        public async Task<Result<string>> ExtractToTempFileAsync(
            string archivePath,
            CancellationToken cancellationToken = default
        )
        {
            ArchiveFormat? format = ArchiveFormatDetector.FromExtension(archivePath);
            if (format is null)
                return Result.Fail($"Unrecognized archive format: {archivePath}");

            await using FileStream input = File.OpenRead(archivePath);
            using SevenZipExtractor extractor = new SevenZipExtractor(input, format.Value, _logger);

            Result<ArchiveInfo> openResult = await extractor
                .OpenAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (openResult.IsFailed)
                return Result.Fail<string>(openResult.Errors);

            Result<IReadOnlyList<ArchiveEntry>> entriesResult = await extractor
                .ListEntriesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (entriesResult.IsFailed)
                return Result.Fail<string>(entriesResult.Errors);

            ArchiveEntry? entry = entriesResult.Value.FirstOrDefault(e => !e.IsDirectory);
            if (entry is null)
                return Result.Fail($"Archive contains no entries: {archivePath}");

            var ext = Path.GetExtension(entry.Path);
            var tempFile = Path.Combine(_tempDirectory, Path.GetRandomFileName() + ext);

            Result extractResult;
            await using (FileStream dest = File.Create(tempFile))
            {
                extractResult = await extractor
                    .ExtractEntryAsync(entry, dest, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (extractResult.IsFailed)
            {
                File.Delete(tempFile);
                return Result.Fail<string>(extractResult.Errors);
            }

            return Result.Ok(tempFile);
        }
    }
#pragma warning restore S6672
}
