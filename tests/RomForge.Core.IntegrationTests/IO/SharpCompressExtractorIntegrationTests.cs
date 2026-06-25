using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using NUnit.Framework;
using RomForge.Core.IO;

namespace RomForge.Core.IntegrationTests.IO;

[TestOf(typeof(SharpCompressExtractor))]
public sealed class SharpCompressExtractorIntegrationTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    private string CreateZip(string entryName, byte[] content)
    {
        string path = Path.Combine(_tempDir, "test.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using Stream stream = entry.Open();
        stream.Write(content);
        return path;
    }

    private string CreateEmptyZip()
    {
        var path = Path.Combine(_tempDir, "empty.zip");
        using ZipArchive _ = ZipFile.Open(path, ZipArchiveMode.Create);
        return path;
    }

    [Test]
    public async Task ExtractToTempFileAsync_ValidZip_ExtractsFileContents()
    {
        byte[] expected = [0x01, 0x02, 0x03, 0x04, 0x05];
        string archivePath = CreateZip("game.gba", expected);
        SharpCompressExtractor sut = new SharpCompressExtractor(_tempDir);

        Result<string> result = await sut.ExtractToTempFileAsync(archivePath);

        result.IsSuccess.Should().BeTrue();
        File.Exists(result.Value).Should().BeTrue();
        (await File.ReadAllBytesAsync(result.Value)).Should().BeEquivalentTo(expected);
    }

    [Test]
    public async Task ExtractToTempFileAsync_ValidZip_PreservesEntryExtension()
    {
        string archivePath = CreateZip("game.nes", [0xAB, 0xCD]);
        SharpCompressExtractor sut = new SharpCompressExtractor(_tempDir);

        Result<string> result = await sut.ExtractToTempFileAsync(archivePath);

        result.IsSuccess.Should().BeTrue();
        Path.GetExtension(result.Value).Should().Be(".nes");
    }

    [Test]
    public async Task ExtractToTempFileAsync_EmptyArchive_ReturnsFail()
    {
        string archivePath = CreateEmptyZip();
        SharpCompressExtractor sut = new SharpCompressExtractor(_tempDir);

        Result<string> result = await sut.ExtractToTempFileAsync(archivePath);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("no entries");
    }

    [Test]
    public async Task ExtractToTempFileAsync_CustomTempDirectory_WritesFileThere()
    {
        string customTemp = Path.Combine(_tempDir, "custom");
        Directory.CreateDirectory(customTemp);
        string archivePath = CreateZip("game.sfc", [0x01, 0x02, 0x03]);
        SharpCompressExtractor sut = new SharpCompressExtractor(customTemp);

        Result<string> result = await sut.ExtractToTempFileAsync(archivePath);

        result.IsSuccess.Should().BeTrue();
        Path.GetDirectoryName(result.Value).Should().Be(customTemp);
    }

    [Test]
    public async Task ExtractToTempFileAsync_DefaultConstructor_UsesSystemTemp()
    {
        string archivePath = CreateZip("game.gba", [0x01, 0x02, 0x03]);
        SharpCompressExtractor sut = new SharpCompressExtractor();
        string? extracted = null;

        try
        {
            Result<string> result = await sut.ExtractToTempFileAsync(archivePath);

            result.IsSuccess.Should().BeTrue();
            extracted = result.Value;
            File.Exists(extracted).Should().BeTrue();
        }
        finally
        {
            if (extracted is not null)
                File.Delete(extracted);
        }
    }
}
