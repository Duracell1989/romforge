using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RomForge.Core.IO;
using SevenZipSharper;
using SevenZipSharper.Compression;

namespace RomForge.Core.UnitTests.IO;

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
