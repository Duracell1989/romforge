using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IO;

namespace RomForge.Core.UnitTests.IO;

[TestOf(typeof(FileSystemRomSource))]
public sealed class FileSystemRomSourceTests
{
    private string _tempDir = string.Empty;
    private FileSystemRomSource _source = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _source = new FileSystemRomSource();
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    [Test]
    public async Task EnumerateAsync_RawFile_YieldsCorrectMetadataAndContent()
    {
        byte[] content = [0xAA, 0xBB, 0xCC, 0xDD];
        string path = WriteFile("game.gba", content);

        List<RomContent> results = await _source.EnumerateAsync(_tempDir).ToListAsync();

        results.Should().HaveCount(1);
        RomContent rom = results[0];
        rom.FilePath.Should().Be(path);
        rom.FileExtension.Should().Be("gba");
        rom.RomExtension.Should().Be("gba");

        await using Stream stream = await rom.OpenStreamAsync(default);
        uint crc = await Crc32OfStreamAsync(stream);
        crc.Should().Be(Crc32Of(content));
    }

    [Test]
    public async Task EnumerateAsync_ZipFile_YieldsArchiveMetadataAndRomContent()
    {
        byte[] romContent = [0x01, 0x02, 0x03, 0x04];
        string zipPath = Path.Combine(_tempDir, "game.zip");
        CreateZip(zipPath, "game.gba", romContent);

        List<RomContent> results = await _source.EnumerateAsync(_tempDir).ToListAsync();

        results.Should().HaveCount(1);
        RomContent rom = results[0];
        rom.FilePath.Should().Be(zipPath);
        rom.FileExtension.Should().Be("zip");
        rom.RomExtension.Should().Be("gba");

        await using Stream stream = await rom.OpenStreamAsync(default);
        uint crc = await Crc32OfStreamAsync(stream);
        crc.Should().Be(Crc32Of(romContent));
    }

    [Test]
    public async Task EnumerateAsync_EmptyZip_YieldsNoResult()
    {
        string zipPath = Path.Combine(_tempDir, "empty.zip");
        CreateZip(zipPath);

        List<RomContent> results = await _source.EnumerateAsync(_tempDir).ToListAsync();

        results.Should().BeEmpty();
    }

    [Test]
    public async Task EnumerateAsync_FileWithNoExtension_IsSkipped()
    {
        WriteFile("noextension", [0xFF]);

        List<RomContent> results = await _source.EnumerateAsync(_tempDir).ToListAsync();

        results.Should().BeEmpty();
    }

    [Test]
    public async Task EnumerateAsync_MultipleFiles_YieldsAll()
    {
        WriteFile("a.gba", [0x11]);
        WriteFile("b.nds", [0x22]);

        List<RomContent> results = await _source.EnumerateAsync(_tempDir).ToListAsync();

        results.Should().HaveCount(2);
        results.Select(r => r.RomExtension).Should().BeEquivalentTo("gba", "nds");
    }

    [Test]
    public async Task EnumerateAsync_FileInSubfolder_IsIncluded()
    {
        string sub = Path.Combine(_tempDir, "region-eu");
        Directory.CreateDirectory(sub);
        await File.WriteAllBytesAsync(Path.Combine(sub, "game.gba"), [0xAA, 0xBB]);

        List<RomContent> results = await _source.EnumerateAsync(_tempDir).ToListAsync();

        results.Should().HaveCount(1);
        results[0].RomExtension.Should().Be("gba");
    }

    [Test]
    public async Task EnumerateAsync_FilesAtMultipleDepths_YieldsAll()
    {
        WriteFile("root.gba", [0x01]);
        string sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        await File.WriteAllBytesAsync(Path.Combine(sub, "nested.nds"), [0x02]);

        List<RomContent> results = await _source.EnumerateAsync(_tempDir).ToListAsync();

        results.Should().HaveCount(2);
        results.Select(r => r.RomExtension).Should().BeEquivalentTo("gba", "nds");
    }

    [Test]
    public async Task CountAsync_EmptyFolder_ReturnsZero()
    {
        int count = await _source.CountAsync(_tempDir);

        count.Should().Be(0);
    }

    [Test]
    public async Task CountAsync_MultipleFiles_ReturnsTotalCount()
    {
        WriteFile("a.gba", [0x01]);
        WriteFile("b.nds", [0x02]);
        WriteFile("c.7z", [0x03]);

        int count = await _source.CountAsync(_tempDir);

        count.Should().Be(3);
    }

    [Test]
    public async Task CountAsync_FileWithNoExtension_IsExcluded()
    {
        WriteFile("noextension", [0xFF]);
        WriteFile("game.gba", [0x01]);

        int count = await _source.CountAsync(_tempDir);

        count.Should().Be(1);
    }

    [Test]
    public async Task CountAsync_FilesInSubfolders_AreIncluded()
    {
        WriteFile("root.gba", [0x01]);
        string sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        await File.WriteAllBytesAsync(Path.Combine(sub, "nested.nds"), [0x02]);

        int count = await _source.CountAsync(_tempDir);

        count.Should().Be(2);
    }

    private string WriteFile(string name, byte[] content)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static void CreateZip(string zipPath, string? entryName = null, byte[]? content = null)
    {
        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        if (entryName is not null && content is not null)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName);
            using Stream s = entry.Open();
            s.Write(content);
        }
    }

    private static async Task<uint> Crc32OfStreamAsync(Stream stream)
    {
        Crc32 hasher = new();
        byte[] buffer = new byte[4096];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            hasher.Append(buffer.AsSpan(0, bytesRead));
        return hasher.GetCurrentHashAsUInt32();
    }

    private static uint Crc32Of(ReadOnlySpan<byte> data)
    {
        Crc32 hasher = new();
        hasher.Append(data);
        return hasher.GetCurrentHashAsUInt32();
    }
}
