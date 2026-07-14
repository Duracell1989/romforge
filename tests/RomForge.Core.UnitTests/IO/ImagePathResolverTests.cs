using System;
using System.IO;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Models;

namespace RomForge.Core.UnitTests.IO;

[TestOf(typeof(ImagePathResolver))]
public sealed class ImagePathResolverTests
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

    [TestCase(1, "1-500")]
    [TestCase(500, "1-500")]
    [TestCase(501, "501-1000")]
    [TestCase(1000, "501-1000")]
    [TestCase(1001, "1001-1500")]
    [TestCase(2500, "2001-2500")]
    public void GetSubfolder_ReturnsCorrectRange(int imageNumber, string expected)
    {
        ImagePathResolver.GetSubfolder(imageNumber).Should().Be(expected);
    }

    [Test]
    public void ResolveIm1Path_ImageNumberZero_ReturnsNull()
    {
        DatHeader header = new() { DatName = "TestDat" };

        string? result = ImagePathResolver.ResolveIm1Path(_tempDir, header, 0);

        result.Should().BeNull();
    }

    [Test]
    public void ResolveIm1Path_FileExists_ReturnsFullPath()
    {
        DatHeader header = new() { DatName = "TestDat" };
        string expectedPath = CreateImageFile("TestDat", "1-500", "1a.png");

        string? result = ImagePathResolver.ResolveIm1Path(_tempDir, header, 1);

        result.Should().Be(expectedPath);
    }

    [Test]
    public void ResolveIm2Path_FileExists_ReturnsFullPath()
    {
        DatHeader header = new() { DatName = "TestDat" };
        string expectedPath = CreateImageFile("TestDat", "1-500", "1b.png");

        string? result = ImagePathResolver.ResolveIm2Path(_tempDir, header, 1);

        result.Should().Be(expectedPath);
    }

    [Test]
    public void ResolveIm1Path_FileMissing_ReturnsNull()
    {
        DatHeader header = new() { DatName = "TestDat" };

        string? result = ImagePathResolver.ResolveIm1Path(_tempDir, header, 1);

        result.Should().BeNull();
    }

    [Test]
    public void ResolveIm1Path_ImFolderOverridesDatName()
    {
        DatHeader header = new() { DatName = "WrongFolder", ImFolder = "RightFolder" };
        string expectedPath = CreateImageFile("RightFolder", "1-500", "1a.png");

        string? result = ImagePathResolver.ResolveIm1Path(_tempDir, header, 1);

        result.Should().Be(expectedPath);
    }

    [Test]
    public void BuildRelativeLocalPath_UsesDatNameFolderSubfolderAndSuffix()
    {
        DatHeader header = new() { DatName = "TestDat" };

        string result = ImagePathResolver.BuildRelativeLocalPath(header, 501, "b");

        result.Should().Be(Path.Combine("TestDat", "501-1000", "501b.png"));
    }

    [Test]
    public void BuildRelativeLocalPath_ImFolderOverridesDatName()
    {
        DatHeader header = new() { DatName = "WrongFolder", ImFolder = "RightFolder" };

        string result = ImagePathResolver.BuildRelativeLocalPath(header, 1, "a");

        result.Should().Be(Path.Combine("RightFolder", "1-500", "1a.png"));
    }

    [Test]
    public void BuildRelativeUrlPath_OmitsFolderAndUsesForwardSlashes()
    {
        string result = ImagePathResolver.BuildRelativeUrlPath(501, "a");

        result.Should().Be("501-1000/501a.png");
    }

    private string CreateImageFile(string folderName, string subFolder, string fileName)
    {
        string dir = Path.Combine(_tempDir, folderName, subFolder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]); // PNG magic
        return path;
    }
}
