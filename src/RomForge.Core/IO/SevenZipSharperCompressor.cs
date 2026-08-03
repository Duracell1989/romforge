using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using Microsoft.Extensions.Logging;
using SevenZipSharper;
using SevenZipSharper.Compression;
using static RomForge.Core.IO.SevenZipSharperCompressorLog;

namespace RomForge.Core.IO
{
    // S6672: the logger here is forwarded verbatim to SevenZipCompressor's constructor, which
    // requires ILogger<SevenZipCompressor> specifically - it is not this class's own logger.
#pragma warning disable S6672
    public sealed class SevenZipSharperCompressor : IArchiveCompressor
    {
        private const string ZipFormatName = "zip";

        private readonly ILogger<SevenZipCompressor> _logger;

        public SevenZipSharperCompressor(ILogger<SevenZipCompressor> logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            _logger = logger;
            IsAvailable = ProbeNativeLibrary(logger);
        }

        // For unit testing — bypasses the native library probe.
        internal SevenZipSharperCompressor(ILogger<SevenZipCompressor> logger, bool isAvailable)
        {
            _logger = logger;
            IsAvailable = isAvailable;
        }

        public bool IsAvailable { get; }

        public async Task<Result> CompressAsync(
            string sourceFile,
            string destArchive,
            string entryName,
            long romSize,
            IProgress<int>? progress = null,
            string format = "7z",
            CancellationToken cancellationToken = default
        )
        {
            if (!IsAvailable)
                return Result.Fail("7-Zip native library could not be loaded.");

            // SevenZipCompressor always creates a fresh archive stream, but a stale partial file
            // left behind by a killed process would collide with File.Create below.
            if (File.Exists(destArchive))
            {
                try
                {
                    File.Delete(destArchive);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ExistingArchiveDeleteFailed(_logger, destArchive, ex);
                    return Result.Fail(
                        $"Could not replace existing archive {Path.GetFileName(destArchive)}: {ex.Message}"
                    );
                }
            }

            ArchiveFormat archiveFormat =
                format == ZipFormatName ? ArchiveFormat.Zip : ArchiveFormat.SevenZip;
            CompressionParameters parameters = BuildParameters(format, romSize);

            try
            {
                using SevenZipCompressor compressor = new SevenZipCompressor(
                    archiveFormat,
                    parameters,
                    _logger
                );
                await using FileStream source = File.OpenRead(sourceFile);
                await using FileStream dest = File.Create(destArchive);

                var entries = new (string EntryPath, Stream Data)[] { (entryName, source) };
                Progress<CompressionProgress>? mapped = MapProgress(progress);

                return await compressor
                    .CompressAsync(entries, dest, mapped, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CompressionFailed(_logger, sourceFile, ex);
                return Result.Fail(new ExceptionalError(ex));
            }
        }

        private static Progress<CompressionProgress>? MapProgress(IProgress<int>? progress) =>
            progress is null
                ? null
                : new Progress<CompressionProgress>(p =>
                {
                    if (p.TotalBytes > 0)
                        progress.Report((int)(p.BytesProcessed * 100 / p.TotalBytes));
                });

        // 1 KB — the library's minimum LZMA/LZMA2 dictionary size.
        private const uint MinDictionarySize = 1024;

        // 1 GB (2^30) — the largest power-of-2 dictionary that still fits under the library's
        // 1536 MB LZMA/LZMA2 ceiling; the next power up (2^31 = 2048 MB) would exceed it.
        private const uint MaxDictionarySize = 1_073_741_824;

        // DictionarySize is deliberately not set for the zip branch: the Zip format handler
        // rejects it (HRESULT 0x80070057 / E_INVALIDARG) even on SevenZipSharper 2.0.0, where
        // the same property works for the 7z format handler. Matches the existing WordSize
        // split below, which hit the same zip-specific quirk.
        internal static CompressionParameters BuildParameters(string format, long romSize) =>
            format == ZipFormatName
                ? CompressionParameters.Default with
                {
                    Level = CompressionLevel.Ultra,
                }
                : CompressionParameters.Default with
                {
                    Level = CompressionLevel.Ultra,
                    WordSize = 273,
                    DictionarySize = DictionarySizeFor(romSize),
                };

        // A dictionary larger than the data being compressed can't improve the ratio for this
        // single-entry archive — it only costs memory — so use the smallest power-of-2 that still
        // covers the whole ROM, clamped to the library's supported LZMA/LZMA2 range.
        internal static uint DictionarySizeFor(long romSize)
        {
            if (romSize <= MinDictionarySize)
                return MinDictionarySize;

            if (romSize >= MaxDictionarySize)
                return MaxDictionarySize;

            return BitOperations.RoundUpToPowerOf2((uint)romSize);
        }

        private static bool ProbeNativeLibrary(ILogger<SevenZipCompressor> logger)
        {
            Result<SevenZipCompressor> probe = SevenZipCompressor.Create(
                ArchiveFormat.SevenZip,
                CompressionParameters.Default,
                logger
            );
            if (probe.IsFailed)
            {
                NativeLibraryUnavailable(logger, probe.Errors[0].Message);
                return false;
            }

            probe.Value.Dispose();
            return true;
        }
    }
#pragma warning restore S6672
}
