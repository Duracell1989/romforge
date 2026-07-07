using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using NUnit.Framework;
using RomForge.Core.IO;

namespace RomForge.Core.IntegrationTests.IO;

[TestOf(typeof(LocalRomFileOperations))]
public sealed class LocalRomFileOperationsIntegrationTests
{
    private string _tempDir = string.Empty;
    private LocalRomFileOperations _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new LocalRomFileOperations();
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    private string CreateFile(string name, string contents = "content")
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, contents);
        return path;
    }

    [Test]
    public void DirectoryExists_WhenDirectoryPresent_ReturnsTrue() =>
        _sut.DirectoryExists(_tempDir).Should().BeTrue();

    [Test]
    public void DirectoryExists_WhenDirectoryMissing_ReturnsFalse() =>
        _sut.DirectoryExists(Path.Combine(_tempDir, "no-such-folder")).Should().BeFalse();

    [Test]
    public async Task RenameAsync_ExistingFile_MovesFileToNewPath()
    {
        string from = CreateFile("old.rom");
        string to = Path.Combine(_tempDir, "new.rom");

        Result result = await _sut.RenameAsync(from, to);

        result.IsSuccess.Should().BeTrue();
        File.Exists(from).Should().BeFalse();
        File.Exists(to).Should().BeTrue();
    }

    [Test]
    public async Task RenameAsync_SourceDoesNotExist_ReturnsFail()
    {
        string from = Path.Combine(_tempDir, "missing.rom");
        string to = Path.Combine(_tempDir, "new.rom");

        Result result = await _sut.RenameAsync(from, to);

        result.IsFailed.Should().BeTrue();
    }

    [Test]
    public async Task RenameAsync_DestinationAlreadyExists_ReturnsFail()
    {
        string from = CreateFile("source.rom");
        string to = CreateFile("existing.rom");

        Result result = await _sut.RenameAsync(from, to);

        result.IsFailed.Should().BeTrue();
    }

    [Test]
    public async Task DeleteAsync_ExistingFile_DeletesFile()
    {
        string path = CreateFile("delete.rom");

        Result result = await _sut.DeleteAsync(path);

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
    }

    [Test]
    public async Task DeleteAsync_NonExistentFile_ReturnsOk()
    {
        string path = Path.Combine(_tempDir, "missing.rom");

        Result result = await _sut.DeleteAsync(path);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task TruncateAsync_ExistingFile_TruncatesToSpecifiedLength()
    {
        string path = CreateFile("game.rom", "Hello World!!");
        long newLength = 5;

        Result result = await _sut.TruncateAsync(path, newLength);

        result.IsSuccess.Should().BeTrue();
        new FileInfo(path).Length.Should().Be(newLength);
    }

    [Test]
    public async Task TruncateAsync_NonExistentFile_ReturnsFail()
    {
        string path = Path.Combine(_tempDir, "missing.rom");

        Result result = await _sut.TruncateAsync(path, 0);

        result.IsFailed.Should().BeTrue();
    }
}
