using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RomForge.Core.IO;
using SevenZipSharper;

namespace RomForge.Core.IntegrationTests.IO
{
    [TestOf(typeof(SevenZipSharperCompressor))]
    [NonParallelizable]
    public sealed class SevenZipSharperCompressorIntegrationTests
    {
        private string _tempDir = string.Empty;
        private SevenZipSharperCompressor _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            // A generous budget so AffordableDictionaryCeiling never binds here — these tests
            // exercise the size-only MaxDictionarySize cap, not machine-adaptive sizing.
            _sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                new WorkingSetBudgetGate(1_000_000_000_000L)
            );
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_tempDir, recursive: true);

        private async Task<string> CreateSourceFileAsync(int sizeBytes = 2048)
        {
            string path = Path.Combine(_tempDir, "source.bin");
            await File.WriteAllBytesAsync(path, new byte[sizeBytes]);
            return path;
        }

        // Writes incompressible random data of the given size. The buffer stays local so it becomes
        // collectable on return — the memory-multiplier test relies on that to isolate the native
        // encoder's working set from this transient managed allocation.
        private static async Task WriteIncompressibleFileAsync(string path, int sizeBytes)
        {
            byte[] payload = new byte[sizeBytes];
            RandomNumberGenerator.Fill(payload);
            await File.WriteAllBytesAsync(path, payload);
        }

        private static async Task<IReadOnlyList<string>> ListEntryPathsAsync(
            string archivePath,
            ArchiveFormat format
        )
        {
            await using FileStream input = File.OpenRead(archivePath);
            using SevenZipExtractor extractor = new SevenZipExtractor(
                input,
                format,
                NullLogger<SevenZipExtractor>.Instance
            );
            await extractor.OpenAsync();
            Result<IReadOnlyList<ArchiveEntry>> entries = await extractor.ListEntriesAsync();
            entries.IsSuccess.Should().BeTrue();
            return entries.Value.Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        }

        [Test]
        public void IsAvailable_WithRealNativeLibrary_ReturnsTrue()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            _sut.IsAvailable.Should().BeTrue();
        }

        [Test]
        public async Task CompressAsync_ValidSource_CreatesSevenZipArchive()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            string source = await CreateSourceFileAsync();
            string dest = Path.Combine(_tempDir, "out.7z");

            Result result = await _sut.CompressAsync(source, dest, "source.bin", 2048);

            result.IsSuccess.Should().BeTrue();
            File.Exists(dest).Should().BeTrue();
            (await ListEntryPathsAsync(dest, ArchiveFormat.SevenZip))
                .Should()
                .BeEquivalentTo("source.bin");
        }

        [Test]
        public async Task CompressAsync_ZipFormat_CreatesZipArchive()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            string source = await CreateSourceFileAsync();
            string dest = Path.Combine(_tempDir, "out.zip");

            Result result = await _sut.CompressAsync(
                source,
                dest,
                "source.bin",
                2048,
                format: "zip"
            );

            result.IsSuccess.Should().BeTrue();
            File.Exists(dest).Should().BeTrue();
            (await ListEntryPathsAsync(dest, ArchiveFormat.Zip))
                .Should()
                .BeEquivalentTo("source.bin");
        }

        [Test]
        public async Task CompressAsync_WithEntryName_NamesInternalEntryAccordingly()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            // The temp file extracted before a re-archive/trim has a random GUID-style name, not
            // the game's real name. entryName lets the caller give the internal archive entry the
            // proper name even though the source file on disk doesn't have it.
            string source = await CreateSourceFileAsync();
            string dest = Path.Combine(_tempDir, "out.7z");

            Result result = await _sut.CompressAsync(source, dest, "0001 - Super Game.bin", 2048);

            result.IsSuccess.Should().BeTrue();
            (await ListEntryPathsAsync(dest, ArchiveFormat.SevenZip))
                .Should()
                .BeEquivalentTo("0001 - Super Game.bin");
        }

        [Test]
        public async Task CompressAsync_WhenDestinationAlreadyExists_ReplacesInsteadOfAppending()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            // A stale archive left at the destination (e.g. from a crash mid-compress) must be
            // replaced, not appended to — an in-place update would leave two entries and silently
            // corrupt the ROM once the original is deleted.
            string stale = Path.Combine(_tempDir, "stale.bin");
            await File.WriteAllBytesAsync(stale, new byte[512]);
            string dest = Path.Combine(_tempDir, "out.7z");
            (await _sut.CompressAsync(stale, dest, "stale.bin", 512)).IsSuccess.Should().BeTrue();

            string source = Path.Combine(_tempDir, "source.bin");
            await File.WriteAllBytesAsync(source, new byte[2048]);

            Result result = await _sut.CompressAsync(source, dest, "source.bin", 2048);

            result.IsSuccess.Should().BeTrue();
            (await ListEntryPathsAsync(dest, ArchiveFormat.SevenZip))
                .Should()
                .BeEquivalentTo("source.bin");
        }

        [Test]
        public async Task CompressAsync_MissingSource_ReturnsFail()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            string dest = Path.Combine(_tempDir, "out.7z");

            Result result = await _sut.CompressAsync(
                Path.Combine(_tempDir, "nonexistent.bin"),
                dest,
                "nonexistent.bin",
                0
            );

            result.IsFailed.Should().BeTrue();
        }

        [Test]
        public async Task CompressAsync_WhenCancelledMidRun_ThrowsOperationCanceled()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            // A large incompressible payload keeps compression busy long enough to cancel while
            // it's still running, exercising the mid-operation cancellation path.
            string source = Path.Combine(_tempDir, "big.bin");
            byte[] payload = new byte[32 * 1024 * 1024];
            RandomNumberGenerator.Fill(payload);
            await File.WriteAllBytesAsync(source, payload);
            string dest = Path.Combine(_tempDir, "out.7z");

            using CancellationTokenSource cts = new CancellationTokenSource();
            Task<Result> compressTask = _sut.CompressAsync(
                source,
                dest,
                "big.bin",
                payload.Length,
                cancellationToken: cts.Token
            );

            await Task.Delay(50);
            await cts.CancelAsync();

            Func<Task> act = async () => await compressTask;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // Empirical check of the Lzma2EncoderMemoryMultiplier constant (11) that the re-archive
        // memory gate sizes concurrency from. Writes an incompressible payload of exactly
        // dictionaryBytes so DictionarySizeFor returns that size without touching internals, then
        // samples process working set across the compress call to measure the encoder's real
        // memory overhead.
        //
        // Run each [Explicit] test below individually, not batched in the same `dotnet test`
        // invocation: back-to-back heavy native LZMA2 allocations in one process can leave the
        // next test's GC.Collect()-sampled baseline already elevated from the previous encoder's
        // allocator pool, which understates the delta (observed: a correct standalone 10.6x read
        // on the ratio-4 test collapsed to ~0x when run immediately after the other two). This is
        // measurement noise from process-level memory carryover, not a defect in the fix.
        private async Task<double> MeasureLzma2EncoderMemoryMultiplierAsync(
            int dictionaryBytes,
            TimeSpan maxSampleDuration
        )
        {
            string source = Path.Combine(_tempDir, "big.bin");
            await WriteIncompressibleFileAsync(source, dictionaryBytes);
            string dest = Path.Combine(_tempDir, "out.7z");

            // Reclaim the transient write buffer before measuring so it isn't charged to the native
            // encoder's working set; compression streams from the file on disk from here on.
#pragma warning disable S1215 // Deliberate: stabilise the baseline before sampling process RSS.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#pragma warning restore S1215

            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            long baseline = process.WorkingSet64;

            using CancellationTokenSource cts = new CancellationTokenSource();
            Task<Result> compressTask = _sut.CompressAsync(
                source,
                dest,
                "big.bin",
                dictionaryBytes,
                cancellationToken: cts.Token
            );

            // Sample the process working set while the encoder allocates its match-finder buffers.
            // The peak is reached shortly after the encoder starts, so once it stops growing for a
            // while we cancel the (now pointless) rest of the compression to keep the test short.
            long peak = baseline;
            Stopwatch sw = Stopwatch.StartNew();
            long lastGrowthMs = 0;
            while (!compressTask.IsCompleted && sw.Elapsed < maxSampleDuration)
            {
                process.Refresh();
                long current = process.WorkingSet64;
                if (current > peak)
                {
                    peak = current;
                    lastGrowthMs = sw.ElapsedMilliseconds;
                }

                if (peak > baseline && sw.ElapsedMilliseconds - lastGrowthMs > 2000)
                    break;

                await Task.Delay(50);
            }

            await cts.CancelAsync();
            try
            {
                await compressTask;
            }
            catch (OperationCanceledException)
            {
                // Expected once we cancel after capturing the peak.
            }

            long encoderWorkingSet = peak - baseline;
            double measuredMultiplier = (double)encoderWorkingSet / dictionaryBytes;
            TestContext.WriteLine(
                $"Measured encoder working set: {encoderWorkingSet / (1024 * 1024)} MB over a "
                    + $"{dictionaryBytes / (1024 * 1024)} MB dictionary -> multiplier "
                    + $"{measuredMultiplier:F1} (constant is 11)."
            );
            return measuredMultiplier;
        }

        [Test]
        [Explicit(
            "Heavy/slow: allocates a large LZMA2 encoder and samples process memory. Run on demand."
        )]
        public async Task CompressAsync_SevenZip_PeakWorkingSetIsAboutElevenTimesTheDictionary()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            // A power-of-two payload makes DictionarySizeFor return exactly the payload size.
            const int dictionaryBytes = 64 * 1024 * 1024; // 2^26 -> 64 MB dictionary.
            double measuredMultiplier = await MeasureLzma2EncoderMemoryMultiplierAsync(
                dictionaryBytes,
                TimeSpan.FromSeconds(30)
            );

            // Process-RSS sampling is noisy (file cache, runtime, GC), so this proves the multiplier
            // is of the right order (~11), ruling out both "no multiplier" (~1x) and a wild
            // over-estimate. Tighten the band if it proves stable on the target hardware.
            measuredMultiplier.Should().BeGreaterThan(4.0).And.BeLessThan(20.0);
        }

        [Test]
        [Explicit(
            "Very heavy/slow: writes a 256 MB payload and allocates the max-size LZMA2 encoder. Run on demand."
        )]
        public async Task CompressAsync_SevenZipAtMaxDictionarySize_PeakWorkingSetIsAboutElevenTimesTheDictionary()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            // Matches SevenZipSharperCompressor.MaxDictionarySize, the ceiling DictionarySizeFor
            // clamps to for large ROMs (e.g. 3DS). The 64 MB test above only proves the multiplier
            // holds at a small dictionary; this proves it doesn't drift at the size the re-archive
            // memory gate actually budgets concurrency from.
            const int dictionaryBytes = 268_435_456; // 256 MB.
            double measuredMultiplier = await MeasureLzma2EncoderMemoryMultiplierAsync(
                dictionaryBytes,
                TimeSpan.FromSeconds(60)
            );

            measuredMultiplier.Should().BeGreaterThan(4.0).And.BeLessThan(20.0);
        }

        // Every test above writes a payload exactly the size of the dictionary it requests
        // (ratio 1) — the one case where native 7-Zip's block-splitting cannot occur, since there
        // is only one block to split. That blind spot is exactly why three releases shipped the
        // panic undetected: DictionarySizeFor clamps at MaxDictionarySize regardless of ROM size,
        // so a real 3DS cart above the clamp compresses at ratio ROM/dictionary > 1. This measures
        // peak working set for a ROM 4x the dictionary clamp — with ThreadCount pinned, memory
        // must track the dictionary (not the ROM), so this must land in the same 4-20x-of-dictionary
        // band as the ratio-1 tests above. Before the ThreadCount pin, this would have measured
        // ~ROM x 10.5 (~42x the dictionary at this ratio) and failed the upper bound.
        private async Task<long> MeasureLzma2EncoderPeakWorkingSetAsync(
            long romSizeBytes,
            TimeSpan maxSampleDuration
        )
        {
            string source = Path.Combine(_tempDir, "big.bin");
            await WriteIncompressibleFileAsync(source, checked((int)romSizeBytes));
            string dest = Path.Combine(_tempDir, "out.7z");

#pragma warning disable S1215 // Deliberate: stabilise the baseline before sampling process RSS.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#pragma warning restore S1215

            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            long baseline = process.WorkingSet64;

            using CancellationTokenSource cts = new CancellationTokenSource();
            Task<Result> compressTask = _sut.CompressAsync(
                source,
                dest,
                "big.bin",
                romSizeBytes,
                cancellationToken: cts.Token
            );

            long peak = baseline;
            Stopwatch sw = Stopwatch.StartNew();
            long lastGrowthMs = 0;
            while (!compressTask.IsCompleted && sw.Elapsed < maxSampleDuration)
            {
                process.Refresh();
                long current = process.WorkingSet64;
                if (current > peak)
                {
                    peak = current;
                    lastGrowthMs = sw.ElapsedMilliseconds;
                }

                if (peak > baseline && sw.ElapsedMilliseconds - lastGrowthMs > 2000)
                    break;

                await Task.Delay(50);
            }

            await cts.CancelAsync();
            try
            {
                await compressTask;
            }
            catch (OperationCanceledException)
            {
                // Expected once we cancel after capturing the peak.
            }

            return peak - baseline;
        }

        [Test]
        [Explicit(
            "Very heavy/slow: writes a 1 GB payload to exercise a 4x dictionary-clamp ratio. Run on demand."
        )]
        public async Task CompressAsync_SevenZipRomFourTimesTheDictionaryClamp_PeakWorkingSetTracksDictionaryNotRom()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            const long romSizeBytes = 1_073_741_824; // 1 GB -> clamps to the 256 MB dictionary (ratio 4).
            uint dictionaryBytes = SevenZipSharperCompressor.DictionarySizeFor(romSizeBytes);
            dictionaryBytes
                .Should()
                .Be(268_435_456u, "the test only proves what it claims at ratio 4");

            long encoderWorkingSet = await MeasureLzma2EncoderPeakWorkingSetAsync(
                romSizeBytes,
                TimeSpan.FromSeconds(90)
            );
            double measuredMultiplier = (double)encoderWorkingSet / dictionaryBytes;
            TestContext.WriteLine(
                $"Measured encoder working set: {encoderWorkingSet / (1024 * 1024)} MB over a "
                    + $"{dictionaryBytes / (1024 * 1024)} MB dictionary ({romSizeBytes / dictionaryBytes}x "
                    + $"ratio) -> multiplier {measuredMultiplier:F1} (constant is 11)."
            );

            // Same band as the ratio-1 tests: proves memory tracks the dictionary, not the ROM.
            // Unpinned ThreadCount would measure ~42x here (ROM x 10.5 / 256 MB dictionary).
            measuredMultiplier.Should().BeGreaterThan(4.0).And.BeLessThan(20.0);
        }

        [Test]
        public async Task CompressAsync_WithProgressCallback_Succeeds()
        {
            Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

            string source = await CreateSourceFileAsync();
            string dest = Path.Combine(_tempDir, "out.7z");
            List<int> reported = [];

            Result result = await _sut.CompressAsync(
                source,
                dest,
                "source.bin",
                2048,
                new Progress<int>(p => reported.Add(p))
            );

            result.IsSuccess.Should().BeTrue();
        }
    }
}
