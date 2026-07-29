using System;
using System.Collections.Generic;
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

namespace RomForge.Core.IntegrationTests.IO;

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
        _sut = new SevenZipSharperCompressor(NullLogger<SevenZipCompressor>.Instance);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    private async Task<string> CreateSourceFileAsync(int sizeBytes = 2048)
    {
        string path = Path.Combine(_tempDir, "source.bin");
        await File.WriteAllBytesAsync(path, new byte[sizeBytes]);
        return path;
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

        Result result = await _sut.CompressAsync(source, dest, "source.bin", 2048, format: "zip");

        result.IsSuccess.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        (await ListEntryPathsAsync(dest, ArchiveFormat.Zip)).Should().BeEquivalentTo("source.bin");
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

    [Test]
    public async Task CompressAsync_WithProgressCallback_Succeeds()
    {
        Assume.That(_sut.IsAvailable, Is.True, "7-Zip native library not available; skipping.");

        string source = await CreateSourceFileAsync();
        string dest = Path.Combine(_tempDir, "out.7z");
        List<int> reported = new List<int>();

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
