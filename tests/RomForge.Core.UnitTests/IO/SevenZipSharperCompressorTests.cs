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

        [TestCase(0L, 1024u)]
        [TestCase(1024L, 1024u)]
        [TestCase(1025L, 2048u)]
        [TestCase(3 * 1024 * 1024L, 4 * 1024 * 1024u)]
        [TestCase(4 * 1024 * 1024L, 4 * 1024 * 1024u)]
        [TestCase(1_073_741_824L, 1_073_741_824u)]
        [TestCase(2_000_000_000L, 1_073_741_824u)]
        public void DictionarySizeFor_ReturnsSmallestPowerOfTwoCoveringRomSize_ClampedToRange(
            long romSize,
            uint expected
        )
        {
            uint actual = SevenZipSharperCompressor.DictionarySizeFor(romSize);

            actual.Should().Be(expected);
        }

        [Test]
        public void EstimateWorkingSetBytes_SevenZipLargeRom_ClampsDictionaryThenAppliesMultiplier()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                isAvailable: true
            );

            long estimate = sut.EstimateWorkingSetBytes(2_000_000_000L, "7z");

            // A ~2 GB (up to 4 GB) 3DS ROM clamps to the 1 GB dictionary; the LZMA2 encoder needs
            // ~11x the dictionary as working memory.
            estimate.Should().Be(1_073_741_824L * 11);
        }

        [Test]
        public void EstimateWorkingSetBytes_SevenZipSmallRom_UsesRoundedDictionary()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                isAvailable: true
            );

            long estimate = sut.EstimateWorkingSetBytes(3 * 1024 * 1024L, "7z");

            // 3 MB rounds up to a 4 MB dictionary, then x11 for the encoder working set.
            estimate.Should().Be(4 * 1024 * 1024L * 11);
        }

        [TestCase(3 * 1024 * 1024L)]
        [TestCase(256 * 1024 * 1024L)]
        [TestCase(1_073_741_824L)]
        [TestCase(2_000_000_000L)]
        public void EstimateWorkingSetBytes_SevenZip_AppliesElevenTimesTheDictionary(long romSize)
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                isAvailable: true
            );

            long estimate = sut.EstimateWorkingSetBytes(romSize, "7z");
            uint dictionary = SevenZipSharperCompressor.DictionarySizeFor(romSize);

            // The estimate must be exactly the LZMA2 encoder memory multiplier (~11x the dictionary
            // per general 7-Zip BT4 match-finder sizing) applied to the dictionary — no more, no
            // less. Pinning the ratio guards the value the concurrency budget depends on.
            (estimate / dictionary)
                .Should()
                .Be(11);
            (estimate % dictionary).Should().Be(0);
        }

        [Test]
        public void EstimateWorkingSetBytes_ZipFormat_ReturnsFlatEstimateBelowLzmaScale()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
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
                isAvailable: true
            );

            sut.IsAvailable.Should().BeTrue();
        }

        [Test]
        public void IsAvailable_ProbeFailed_ReturnsFalse()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                isAvailable: false
            );

            sut.IsAvailable.Should().BeFalse();
        }

        [Test]
        public async Task CompressAsync_WhenUnavailable_ReturnsFail()
        {
            SevenZipSharperCompressor sut = new SevenZipSharperCompressor(
                NullLogger<SevenZipCompressor>.Instance,
                isAvailable: false
            );

            Result result = await sut.CompressAsync("/src/game.gba", "/out/game.7z", "game.gba", 0);

            result.IsFailed.Should().BeTrue();
            result.Errors[0].Message.Should().Contain("native library");
        }
    }
}
