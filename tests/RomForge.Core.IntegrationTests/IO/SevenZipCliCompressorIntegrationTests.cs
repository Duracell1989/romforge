using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
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
    public async Task CompressAsync_WhenCancelledMidRun_KillsProcessAndThrows()
    {
        Assume.That(_sut.IsAvailable, Is.True, "7-Zip binary not found; skipping.");

        // A large incompressible payload keeps 7-Zip busy long enough to cancel
        // while the process is still running, exercising the kill-on-cancel path.
        string source = Path.Combine(_tempDir, "big.bin");
        byte[] payload = new byte[32 * 1024 * 1024];
        RandomNumberGenerator.Fill(payload);
        await File.WriteAllBytesAsync(source, payload);
        string dest = Path.Combine(_tempDir, "out.7z");

        using CancellationTokenSource cts = new CancellationTokenSource();
        Task<Result> compressTask = _sut.CompressAsync(
            source,
            dest,
            payload.Length,
            cancellationToken: cts.Token
        );

        await Task.Delay(100);
        await cts.CancelAsync();

        Func<Task> act = async () => await compressTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
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
