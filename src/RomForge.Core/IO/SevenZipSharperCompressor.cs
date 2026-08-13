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
        private readonly WorkingSetBudgetGate _memoryGate;

        public SevenZipSharperCompressor(ILogger<SevenZipCompressor> logger, WorkingSetBudgetGate memoryGate)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(memoryGate);
            _logger = logger;
            _memoryGate = memoryGate;
            IsAvailable = ProbeNativeLibrary(logger);
        }

        // For unit testing — bypasses the native library probe.
        internal SevenZipSharperCompressor(ILogger<SevenZipCompressor> logger, WorkingSetBudgetGate memoryGate, bool isAvailable)
        {
            ArgumentNullException.ThrowIfNull(memoryGate);
            _logger = logger;
            _memoryGate = memoryGate;
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
                    return Result.Fail($"Could not replace existing archive {Path.GetFileName(destArchive)}: {ex.Message}");
                }
            }

            ArchiveFormat archiveFormat = format == ZipFormatName ? ArchiveFormat.Zip : ArchiveFormat.SevenZip;
            CompressionParameters parameters = BuildParameters(format, romSize, _memoryGate.BudgetBytes);

            try
            {
                using SevenZipCompressor compressor = new SevenZipCompressor(archiveFormat, parameters, _logger);
                await using FileStream source = File.OpenRead(sourceFile);
                await using FileStream dest = File.Create(destArchive);

                var entries = new (string EntryPath, Stream Data)[] { (entryName, source) };
                Progress<CompressionProgress>? mapped = MapProgress(progress);

                return await compressor.CompressAsync(entries, dest, mapped, cancellationToken).ConfigureAwait(false);
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

        // 256 MB (2^28). Was 1 GB until 2026-08-09: with ThreadCount unset, native 7-Zip splits
        // the input into ~dictionary x 4 blocks and runs one full encoder per block, so a 1 GB
        // dictionary on a 4 GB 3DS cart (ratio 4) cost ~42 GB resident and triggered a kernel
        // panic (three times: 07-25, 08-01, 08-08). Measured across 6 ROMs / 3 platforms that a
        // smaller dictionary costs at most 0.0044% archive size for 3DS/NDS carts (they're
        // largely encrypted/pre-compressed, so a wider window finds no long-range redundancy) —
        // see Codex/Claude/Plans/romforge-3ds-archive-panic.md. This cap only does anything
        // together with pinning ThreadCount below; alone, it is a measured no-op.
        private const uint MaxDictionarySize = 268_435_456;

        // Pins the LZMA2 encoder to exactly one match-finder instance instead of native 7-Zip's
        // per-core default. Unpinned, peak memory tracks ROM size (measured ~ROM x 10.5) because
        // the encoder is split into ~dictionary x 4 blocks, each with its own dictionary-sized
        // allocation; pinned, it tracks dictionary size only (dict x 10.5-11), which is what
        // EstimateWorkingSetBytes assumes. mt=1 -> 2 is also a free 6.3x speedup at identical
        // memory and byte-identical output (hash + BT match-finder threads pipeline within one
        // encoder); mt >= 4 is where block-splitting kicks in, so 2 is the ceiling that keeps
        // exactly one encoder alive. Do not raise this without re-measuring — see the panic plan.
        internal const int Lzma2EncoderThreads = 2;

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
                    ThreadCount = Lzma2EncoderThreads,
                };

        // Also clamps the dictionary to what this machine's memory budget can afford (see
        // AffordableDictionaryCeiling), on top of the fixed MaxDictionarySize ceiling above. This
        // is what makes a single compress job's estimated working set never exceed the budget it
        // was admitted under — the WorkingSetBudgetGate.AcquireAsync "runs alone" fallback for an
        // over-budget job exists only as a safety net for a near-zero budget, not normal operation.
        internal static CompressionParameters BuildParameters(string format, long romSize, long memoryBudgetBytes) =>
            format == ZipFormatName
                ? BuildParameters(format, romSize)
                : CompressionParameters.Default with
                {
                    Level = CompressionLevel.Ultra,
                    WordSize = 273,
                    DictionarySize = DictionarySizeFor(romSize, memoryBudgetBytes),
                    ThreadCount = Lzma2EncoderThreads,
                };

        // The LZMA2 BT4 match finder needs roughly this many times the dictionary size as working
        // memory while encoding, so a 1 GB dictionary costs ~11 GB resident per encoder. This is
        // the dominant cost that limits how many large re-archives can run at once. Only holds
        // when exactly one encoder is alive, i.e. ThreadCount <= 2 (see Lzma2EncoderThreads).
        private const int Lzma2EncoderMemoryMultiplier = 11;

        // Measured 2026-08-09: the pure dict x 11 multiplier under-predicts by up to 30% below a
        // 32 MB dictionary (14.31x at 4 MB), caused by a fixed encoder overhead the multiplier
        // model omits. Adding this flat term covers every measured dictionary size with headroom.
        private const long Lzma2EncoderFixedOverheadBytes = 64L * 1024 * 1024;

        // Deflate (the zip format) uses a fixed 32 KB window, so its encoder memory is negligible
        // next to LZMA2. A small flat estimate keeps zip jobs from ever throttling the memory gate.
        private const long ZipEncoderWorkingSetBytes = 64L * 1024 * 1024;

        public long EstimateWorkingSetBytes(long romSize, string format) =>
            format == ZipFormatName ? ZipEncoderWorkingSetBytes : CostFor(DictionarySizeFor(romSize, _memoryGate.BudgetBytes));

        private static long CostFor(uint dictionaryBytes) => ((long)dictionaryBytes * Lzma2EncoderMemoryMultiplier) + Lzma2EncoderFixedOverheadBytes;

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

        // Also clamps to whatever a memory budget can afford (see AffordableDictionaryCeiling),
        // so a single job's estimated cost is never charged more than the whole budget it was
        // admitted under. Without this, DictionarySizeFor alone lets any ROM at or above
        // MaxDictionarySize request the full 256 MB dictionary regardless of how little memory is
        // actually available — fine on a 48 GB dev Mac, a real shortfall on a low-RAM shipped Mac.
        internal static uint DictionarySizeFor(long romSize, long memoryBudgetBytes) =>
            Math.Min(DictionarySizeFor(romSize), AffordableDictionaryCeiling(memoryBudgetBytes));

        // Largest power-of-2 dictionary whose CostFor(...) fits within budgetBytes. Floors at
        // MinDictionarySize — a dictionary cannot shrink below the library's own minimum, so an
        // extremely small budget still costs CostFor(MinDictionarySize); that residual case is the
        // WorkingSetBudgetGate "runs alone" fallback's actual job, not something this method can
        // avoid. In practice unreachable on real hardware: it would need well under 64 MB of
        // available memory, far below what .NET's GC.GetGCMemoryInfo() reports even under pressure.
        internal static uint AffordableDictionaryCeiling(long memoryBudgetBytes)
        {
            long affordable = (memoryBudgetBytes - Lzma2EncoderFixedOverheadBytes) / Lzma2EncoderMemoryMultiplier;

            if (affordable <= MinDictionarySize)
                return MinDictionarySize;

            if (affordable >= MaxDictionarySize)
                return MaxDictionarySize;

            // Largest power of 2 <= affordable (round down, unlike DictionarySizeFor's round-up —
            // this is a ceiling on cost, not a floor on coverage).
            return 1u << BitOperations.Log2((uint)affordable);
        }

        private static bool ProbeNativeLibrary(ILogger<SevenZipCompressor> logger)
        {
            Result<SevenZipCompressor> probe = SevenZipCompressor.Create(ArchiveFormat.SevenZip, CompressionParameters.Default, logger);
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
