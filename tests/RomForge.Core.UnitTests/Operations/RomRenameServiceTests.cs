using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using Moq;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Operations;
using RomForge.Core.Scanning;

namespace RomForge.Core.UnitTests.Operations;

[TestOf(typeof(RomRenameService))]
public sealed class RomRenameServiceTests
{
    private const string Mask = "%u - %n";

    private Mock<IRomFileOperations> _fileOps = null!;
    private RomRenameService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _fileOps = new Mock<IRomFileOperations>();
        _service = new RomRenameService(_fileOps.Object);
    }

    private static MatchResult IncorrectlyNamed(
        string filePath = "/roms/Wrong Name.7z",
        bool entryMisnamed = false,
        bool wrongArchiveType = false,
        bool untrimmed = false,
        bool reArchived = false
    ) =>
        new MatchResult
        {
            Game = new Game { ReleaseNumber = 1, Title = "Correct Title" },
            Status = MatchStatus.Verified,
            ScannedRom = new ScannedRom
            {
                FilePath = filePath,
                FileExtension = "7z",
                RomExtension = "gba",
            },
            IsIncorrectlyNamed = true,
            IsEntryMisnamed = entryMisnamed,
            IsWrongArchiveType = wrongArchiveType,
            IsUntrimmed = untrimmed,
            IsReArchived = reArchived,
        };

    [Test]
    public async Task RenameAsync_NoRenameNeeded_ReturnsSuccessWithNullAndDoesNotMove()
    {
        MatchResult match = IncorrectlyNamed() with { IsIncorrectlyNamed = false };

        Result<MatchResult?> result = await _service.RenameAsync(match, Mask);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        _fileOps.Verify(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task RenameAsync_IncorrectlyNamed_MovesFileToExpectedNameAndReturnsUpdatedMatch()
    {
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        Result<MatchResult?> result = await _service.RenameAsync(IncorrectlyNamed(), Mask);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.IsIncorrectlyNamed.Should().BeFalse();
        result.Value.Status.Should().Be(MatchStatus.Verified);
        result.Value.ScannedRom!.FilePath.Should().Be("/roms/0001 - Correct Title.7z");
        _fileOps.Verify(
            f => f.RenameAsync("/roms/Wrong Name.7z", "/roms/0001 - Correct Title.7z"),
            Times.Once
        );
    }

    [Test]
    public async Task RenameAsync_PreservesUnrelatedFlags()
    {
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());
        MatchResult match = IncorrectlyNamed(
            entryMisnamed: true,
            wrongArchiveType: true,
            untrimmed: true,
            reArchived: true
        );

        Result<MatchResult?> result = await _service.RenameAsync(match, Mask);

        result.Value!.IsEntryMisnamed.Should().BeTrue();
        result.Value.IsWrongArchiveType.Should().BeTrue();
        result.Value.IsUntrimmed.Should().BeTrue();
        result.Value.IsReArchived.Should().BeTrue();
    }

    [Test]
    public async Task RenameAsync_FileMoveFails_ReturnsFailureCarryingTheError()
    {
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Fail("disk full"));

        Result<MatchResult?> result = await _service.RenameAsync(IncorrectlyNamed(), Mask);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Be("disk full");
    }
}
