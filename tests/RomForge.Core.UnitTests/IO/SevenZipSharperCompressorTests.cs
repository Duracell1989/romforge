using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RomForge.Core.IO;
using SevenZipSharper;
using SevenZipSharper.Compression;

namespace RomForge.Core.UnitTests.IO
{
    [TestOf(typeof(SevenZipSharperCompressor))]
    public sealed class SevenZipSharperCompressorTests
    {
        // A budget large enough that AffordableDictionaryCeiling never binds, so tests that only
        // care about size-based behaviour see the same values as before budget-awareness existed.
        private const long GenerousBudgetBytes = 1_000_000_000_000L; // 1 TB.

        private static WorkingSetBudgetGate GenerousGate() =>
            new WorkingSetBudgetGate(GenerousBudgetBytes);

        [Test]
        public void BuildParameters_ZipFormat_UsesUltraLevelWithoutWordSize()
        {
            CompressionParameters parameters = SevenZipSharperCompressor.BuildParameters(
                "zip",
                romSize: 4 * 1024 * 1024
            );

            parameters.Level.Should().Be(CompressionLevel.Ultra);
            // DictionarySize is deliberately not set for zip — see BuildParameters for why.
            parameters.DictionarySize.Should().BeNull();
            parameters.WordSize.Should().BeNull();
        }

        [Test]
        public void BuildParameters_SevenZipFormat_SetsUltraLevelAndWordSize()
        {
            CompressionParameters parameters = SevenZipSharperCompressor.BuildParameters(
                "7z",
                romSize: 4 * 1024 * 1024
            );

            parameters.Level.Should().Be(CompressionLevel.Ultra);
            parameters.DictionarySize.Should().Be(4 * 1024 * 1024);
            parameters.WordSize.Should().Be(273u);
        }

        [Test]
        public void BuildParameters_SevenZipFormat_PinsThreadCountToEncoderConstant()
        {
            // With ThreadCount unset, native 7-Zip splits the input into ~dict x 4 blocks and runs
            // one full LZMA2 encoder (its own dictionary-sized match finder) per block, so peak
            // memory tracks ROM size rather than dictionary size (measured: ROM x 10.5). Pinning
            // ThreadCount is what makes EstimateWorkingSetBytes's single-encoder formula true.
            //
            // The literal 2 is deliberate — do NOT replace it with Lzma2EncoderThreads. Asserting
            // against the constant the production code assigns from is tautological: it compares
            // the constant to itself and stays green if someone sets it to 8, which reinstates
            // block-splitting and the ROM x 10.5 working set behind three kernel panics. The only
            // test that measures the real behaviour is [Explicit] and never runs in CI, so this
            // literal is the sole automated guard on the value.
            CompressionParameters parameters = SevenZipSharperCompressor.BuildParameters(
                "7z",
                romSize: 4 * 1024 * 1024
            );

            parameters.ThreadCount.Should().Be(2);
        }

        [TestCase(0L, 1024u)]
        [TestCase(1024L, 1024u)]
        [TestCase(1025L, 2048u)]
        [TestCase(3 * 1024 * 1024L, 4 * 1024 * 1024u)]
        [TestCase(4 * 1024 * 1024L, 4 * 1024 * 1024u)]
        [TestCase(268_435_456L, 268_435_456u)]
        [TestCase(1_073_741_824L, 268_435_456u)]
        [TestCase(2_000_000_000L, 268_435_456u)]
        public void DictionarySizeFor_ReturnsSmallestPowerOfTwoCoveringRomSize_ClampedToRange(
            long romSize,
            uint expected
        )
        {
            uint actual = SevenZipSharperCompressor.DictionarySizeFor(romSize);

            actual.Should().Be(expected);
        }

        [TestCase(1_000_000_000_000L, 268_435_456u)] // Generous budget -> the size cap wins.
        [TestCase(3_019_898_880L, 268_435_456u)] // 256 MB dict's exact cost -> affords the full cap.
        [TestCase(3_019_898_869L, 134_217_728u)] // One byte under -> drops to the next power of 2.
        [TestCase(67_108_864L, 1024u)] // Budget only covers the fixed overhead -> floors at Min.
        public void AffordableDictionaryCeiling_ReturnsLargestPowerOfTwoWithinBudget(
            long budgetBytes,
            uint expected
        )
        {
            uint actual = SevenZipSharperCompressor.AffordableDictionaryCeiling(budgetBytes);

            actual.Should().Be(expected);
        }

        [Test]
        public void DictionarySizeFor_WithBudget_NeverExceedsSizeCapOrAffordability()
        {
            // A 3DS-sized ROM would otherwise clamp to the 256 MB MaxDictionarySize cap, but this
            // budget can only afford a 64 MB dictionary (see AffordableDictionaryCeiling_* above
            // for how 805,306,368 -> exactly 64 MB was derived).
            uint actual = SevenZipSharperCompressor.DictionarySizeFor(
                romSize: 4L * 1024 * 1024 * 1024,
                memoryBudgetBytes: 805_306_368L
            );

            actual.Should().Be(64 * 1024 * 1024u);
        }

        [Test]
        public void BuildParameters_WithBudget_SevenZipFormat_ClampsDictionaryToAffordability()
        {
            CompressionParameters parameters = SevenZipSharperCompressor.BuildParameters(
                "7z",
                romSize: 4L * 1024 * 1024 * 1024,
                memoryBudgetBytes: 805_306_368L
            );

            parameters.DictionarySize.Should().Be(64 * 1024 * 1024u);
            // Literal, not the constant — see BuildParameters_SevenZipFormat_PinsThreadCountToEncoderConstant.
            parameters.ThreadCount.Should().Be(2);
        }

        [Test]
        public void BuildParameters_WithBudget_ZipFormat_IgnoresBudget()
        {
            // Deflate's window is fixed-size and DictionarySize isn't set for zip at all (see the
            // 2-arg overload), so the budget-aware overload must fall through to the same params.
            CompressionParameters parameters = SevenZipSharperCompressor.BuildParameters(
                "zip",
                romSize: 4L * 1024 * 1024 * 1024,
                memoryBudgetBytes: 1024L
            );

            parameters.DictionarySize.Should().BeNull();
        }

        // The regression this whole method exists for: a per-job estimate that structurally cannot
        // exceed the budget it was admitted under, on any machine size RomForge ships to — not just
        // the 48 GB dev Mac the panic was found and fixed on. Deterministic, no real hardware
        // needed (see Codex/Claude/Plans/romforge-3ds-archive-panic.md, "Machine-adaptive sizing").
        [TestCase(4L * 1024 * 1024 * 1024)] // 4 GB — a low-RAM shipped Mac.
        [TestCase(8L * 1024 * 1024 * 1024)]
        [TestCase(16L * 1024 * 1024 * 1024)]
        [TestCase(48L * 1024 * 1024 * 1024)] // The dev machine the panic was measured on.
        public void EstimateWorkingSetBytes_WithBudget_NeverExceedsBudget_AtAnyRomSize(
            long budgetBytes
        )
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                new WorkingSetBudgetGate(budgetBytes),
                isAvailable: true
            );

            foreach (
                long romSize in new[]
                {
                    3 * 1024 * 1024L,
                    32 * 1024 * 1024L,
                    268_435_456L,
                    1_073_741_824L,
                    4L * 1024 * 1024 * 1024,
                }
            )
            {
                long estimate = sut.EstimateWorkingSetBytes(romSize, "7z");
                estimate.Should().BeLessThanOrEqualTo(budgetBytes);
            }
        }

        [Test]
        public void EstimateWorkingSetBytes_SevenZipLargeRom_ClampsDictionaryThenAppliesMultiplier()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                GenerousGate(),
                isAvailable: true
            );

            long estimate = sut.EstimateWorkingSetBytes(2_000_000_000L, "7z");

            // A 3DS-sized ROM clamps to the 256 MB dictionary; the LZMA2 encoder needs ~11x the
            // dictionary as working memory, plus a fixed ~64 MB overhead the pure multiplier omits.
            estimate.Should().Be(268_435_456L * 11 + 64 * 1024 * 1024);
        }

        [Test]
        public void EstimateWorkingSetBytes_SevenZipSmallRom_UsesRoundedDictionary()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                GenerousGate(),
                isAvailable: true
            );

            long estimate = sut.EstimateWorkingSetBytes(3 * 1024 * 1024L, "7z");

            // 3 MB rounds up to a 4 MB dictionary, then x11 plus the fixed encoder overhead.
            estimate.Should().Be(4 * 1024 * 1024L * 11 + 64 * 1024 * 1024);
        }

        [TestCase(3 * 1024 * 1024L)]
        [TestCase(32 * 1024 * 1024L)]
        [TestCase(268_435_456L)]
        [TestCase(1_073_741_824L)]
        [TestCase(2_000_000_000L)]
        public void EstimateWorkingSetBytes_SevenZip_AppliesElevenTimesTheDictionaryPlusFixedOverhead(
            long romSize
        )
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                GenerousGate(),
                isAvailable: true
            );

            long estimate = sut.EstimateWorkingSetBytes(romSize, "7z");
            uint dictionary = SevenZipSharperCompressor.DictionarySizeFor(romSize);

            // Measured 2026-08-09: the pure dict x 11 multiplier under-predicts by up to 30% below
            // a 32 MB dictionary, so a fixed ~64 MB term covers the shortfall at every size. Pinning
            // the exact formula guards the value the concurrency budget depends on.
            estimate.Should().Be((long)dictionary * 11 + 64 * 1024 * 1024);
        }

        [Test]
        public void EstimateWorkingSetBytes_ZipFormat_ReturnsFlatEstimateBelowLzmaScale()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                GenerousGate(),
                isAvailable: true
            );

            long small = sut.EstimateWorkingSetBytes(4 * 1024 * 1024L, "zip");
            long large = sut.EstimateWorkingSetBytes(2_000_000_000L, "zip");

            // Deflate's window is tiny and fixed, so the estimate is flat and must never dominate
            // the memory budget the way an LZMA2 dictionary can.
            small.Should().Be(large);
            large.Should().BeLessThan(1_073_741_824L);
        }

        [Test]
        public void IsAvailable_ProbeSucceeded_ReturnsTrue()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                GenerousGate(),
                isAvailable: true
            );

            sut.IsAvailable.Should().BeTrue();
        }

        [Test]
        public void IsAvailable_ProbeFailed_ReturnsFalse()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                GenerousGate(),
                isAvailable: false
            );

            sut.IsAvailable.Should().BeFalse();
        }

        [Test]
        public async Task CompressAsync_WhenUnavailable_ReturnsFail()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                GenerousGate(),
                isAvailable: false
            );

            Result result = await sut.CompressAsync("/src/game.gba", "/out/game.7z", "game.gba", 0);

            result.IsFailed.Should().BeTrue();
            result.Errors[0].Message.Should().Contain("native library");
        }
    }
}
