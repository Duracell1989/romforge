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
        CompressionParameters parameters = SevenZipSharperCompressor.BuildParameters("zip");

        parameters.Level.Should().Be(CompressionLevel.Ultra);
        parameters.DictionarySize.Should().BeNull();
        parameters.WordSize.Should().BeNull();
    }

    [Test]
    public void BuildParameters_SevenZipFormat_SetsUltraLevelAndWordSize()
    {
        CompressionParameters parameters = SevenZipSharperCompressor.BuildParameters("7z");

        parameters.Level.Should().Be(CompressionLevel.Ultra);
        // DictionarySize is deliberately not set — see BuildParameters for why.
        parameters.DictionarySize.Should().BeNull();
        parameters.WordSize.Should().Be(273u);
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

        Result result = await sut.CompressAsync("/src/game.gba", "/out/game.7z", 0);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("native library");
    }
}
