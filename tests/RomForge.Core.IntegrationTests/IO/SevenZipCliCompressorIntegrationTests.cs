using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using NUnit.Framework;
using RomForge.Core.IO;
using Serilog;

namespace RomForge.Core.IntegrationTests.IO;

[TestOf(typeof(SevenZipCliCompressor))]
[NonParallelizable]
public sealed class SevenZipCliCompressorIntegrationTests
{
    private string _tempDir = string.Empty;
    private SevenZipCliCompressor _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new SevenZipCliCompressor(new LoggerConfiguration().CreateLogger());
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    private async Task<string> CreateSourceFileAsync(int sizeBytes = 2048)
    {
        string path = Path.Combine(_tempDir, "source.bin");
        await File.WriteAllBytesAsync(path, new byte[sizeBytes]);
        return path;
    }

    [Test]
    public void IsAvailable_WithRealBinary_ReturnsTrue()
    {
        Assume.That(_sut.IsAvailable, Is.True, "7-Zip binary not found; skipping.");

        _sut.IsAvailable.Should().BeTrue();
    }

    [Test]
    public async Task CompressAsync_ValidSource_CreatesSevenZipArchive()
    {
        Assume.That(_sut.IsAvailable, Is.True, "7-Zip binary not found; skipping.");

        string source = await CreateSourceFileAsync();
        string dest = Path.Combine(_tempDir, "out.7z");

        Result result = await _sut.CompressAsync(source, dest, 2048);

        result.IsSuccess.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }

    [Test]
    public async Task CompressAsync_ZipFormat_CreatesZipArchive()
    {
        Assume.That(_sut.IsAvailable, Is.True, "7-Zip binary not found; skipping.");

        string source = await CreateSourceFileAsync();
        string dest = Path.Combine(_tempDir, "out.zip");

        Result result = await _sut.CompressAsync(source, dest, 2048, format: "zip");

        result.IsSuccess.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }

    [Test]
    public async Task CompressAsync_MissingSource_ReturnsFail()
    {
        Assume.That(_sut.IsAvailable, Is.True, "7-Zip binary not found; skipping.");

        string dest = Path.Combine(_tempDir, "out.7z");

        Result result = await _sut.CompressAsync(
            Path.Combine(_tempDir, "nonexistent.bin"),
            dest,
            0
        );

        result.IsFailed.Should().BeTrue();
    }

    [Test]
    public async Task CompressAsync_WithProgressCallback_Succeeds()
    {
        Assume.That(_sut.IsAvailable, Is.True, "7-Zip binary not found; skipping.");

        string source = await CreateSourceFileAsync();
        string dest = Path.Combine(_tempDir, "out.7z");
        List<int> reported = new List<int>();

        Result result = await _sut.CompressAsync(
            source,
            dest,
            2048,
            new Progress<int>(p => reported.Add(p))
        );

        result.IsSuccess.Should().BeTrue();
    }
}
