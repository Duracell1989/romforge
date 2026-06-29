using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using NUnit.Framework;
using RomForge.Core.IO;
using Serilog;

namespace RomForge.Core.UnitTests.IO;

[TestOf(typeof(SevenZipCliCompressor))]
public sealed class SevenZipCliCompressorTests
{
    private const long LargeRam = 64L * 1024 * 1024 * 1024;

    [TestCase(0L, 1)]
    [TestCase(1L, 1)]
    [TestCase(1L * 1024 * 1024, 1)]
    [TestCase(2L * 1024 * 1024, 2)]
    [TestCase(3L * 1024 * 1024, 4)]
    [TestCase(32L * 1024 * 1024, 32)]
    [TestCase(33L * 1024 * 1024, 64)]
    [TestCase(4096L * 1024 * 1024, 3840)]
    public void ComputeDictionaryMb_VariousSizes_ReturnsExpected(long romSize, int expected)
    {
        SevenZipCliCompressor.ComputeDictionaryMb(romSize, LargeRam).Should().Be(expected);
    }

    [Test]
    public void ComputeDictionaryMb_RamCapSmaller_CapsAtQuarterRam()
    {
        // 4 GB ROM wants 3840 MB dict; 4 GB RAM caps at 1024 MB (25%)
        long romSize = 4096L * 1024 * 1024;
        long totalRam = 4L * 1024 * 1024 * 1024;
        SevenZipCliCompressor.ComputeDictionaryMb(romSize, totalRam).Should().Be(1024);
    }

    [Test]
    public void ComputeDictionaryMb_LargeRam_RomSizeCapApplies()
    {
        // 32 MB ROM on 32 GB machine — ROM cap (32 MB) applies, not the RAM cap (8192 MB)
        long romSize = 32L * 1024 * 1024;
        long totalRam = 32L * 1024 * 1024 * 1024;
        SevenZipCliCompressor.ComputeDictionaryMb(romSize, totalRam).Should().Be(32);
    }

    [Test]
    public void BuildArguments_SevenZip_ContainsRequiredParams()
    {
        string args = SevenZipCliCompressor.BuildArguments(
            "/src/game.gba",
            "/out/game.7z",
            32L * 1024 * 1024,
            LargeRam
        );

        args.Should().Contain("-t7z");
        args.Should().Contain("-m0=LZMA");
        args.Should().Contain("-mx=9");
        args.Should().Contain("-mmf=bt5");
        args.Should().Contain("-mfb=273");
        args.Should().Contain("-md=32m");
        args.Should().Contain("-mlc=3");
        args.Should().Contain("-mlp=0");
        args.Should().Contain("-mpb=2");
        args.Should().Contain("-mhc=on");
        args.Should().Contain("-ms=on");
    }

    [Test]
    public void BuildArguments_SevenZip_QuotesBothPaths()
    {
        string args = SevenZipCliCompressor.BuildArguments("/src/game.gba", "/out/game.7z", 0, LargeRam);

        args.Should().Contain("\"/out/game.7z\"");
        args.Should().Contain("\"/src/game.gba\"");
    }

    [Test]
    public void BuildArguments_SevenZip_DictionarySizeScalesWithRomSize()
    {
        string argsSmall = SevenZipCliCompressor.BuildArguments("s", "d", 1L * 1024 * 1024, LargeRam);
        string argsLarge = SevenZipCliCompressor.BuildArguments("s", "d", 512L * 1024 * 1024, LargeRam);

        argsSmall.Should().Contain("-md=1m");
        argsLarge.Should().Contain("-md=512m");
    }

    [Test]
    public void BuildArguments_ZipFormat_UsesZipTypeWithoutLzmaParams()
    {
        string args = SevenZipCliCompressor.BuildArguments(
            "/src/game.gba",
            "/out/game.zip",
            32L * 1024 * 1024,
            LargeRam,
            "zip"
        );

        args.Should().Contain("-tzip");
        args.Should().Contain("-mx=9");
        args.Should().NotContain("-t7z");
        args.Should().NotContain("-m0=LZMA");
        args.Should().NotContain("-md=");
    }

    [Test]
    public void IsAvailable_WhenNoBinaryPath_ReturnsFalse()
    {
        SevenZipCliCompressor sut = new SevenZipCliCompressor(
            new LoggerConfiguration().CreateLogger(),
            null
        );

        sut.IsAvailable.Should().BeFalse();
    }

    [Test]
    public void IsAvailable_WhenBinaryPathProvided_ReturnsTrue()
    {
        SevenZipCliCompressor sut = new SevenZipCliCompressor(
            new LoggerConfiguration().CreateLogger(),
            "/usr/bin/7zz"
        );

        sut.IsAvailable.Should().BeTrue();
    }

    [Test]
    public async Task CompressAsync_WhenUnavailable_ReturnsFail()
    {
        SevenZipCliCompressor sut = new SevenZipCliCompressor(
            new LoggerConfiguration().CreateLogger(),
            null
        );

        Result result = await sut.CompressAsync("/src/game.gba", "/out/game.7z", 0);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("7-Zip binary not found");
    }
}
