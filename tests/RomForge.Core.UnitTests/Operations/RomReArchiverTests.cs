using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Operations;
using RomForge.Core.Scanning;

namespace RomForge.Core.UnitTests.Operations;

[TestOf(typeof(RomReArchiver))]
public sealed class RomReArchiverTests
{
    private static MatchResult MakeVerifiedResult(
        string? filePath = "/roms/game.zip",
        bool isUntrimmed = false,
        bool isWrongArchiveType = false,
        int release = 1,
        string title = "Correct Title"
    ) =>
        new MatchResult
        {
            Game = new Game { ReleaseNumber = release, Title = title },
            Status = MatchStatus.Verified,
            ScannedRom = filePath is null
                ? null
                : new ScannedRom
                {
                    FilePath = filePath,
                    FileExtension = "zip",
                    RomExtension = "gba",
                },
            IsUntrimmed = isUntrimmed,
            IsWrongArchiveType = isWrongArchiveType,
        };

    [Test]
    public void GetReArchiveTarget_VerifiedNotUntrimmed_ReturnsTarget()
    {
        MatchResult result = MakeVerifiedResult();

        (string From, string To)? target = RomReArchiver.GetReArchiveTarget(result, "%u - %n");

        target.Should().NotBeNull();
        target!.Value.From.Should().Be("/roms/game.zip");
        target.Value.To.Should().Be("/roms/0001 - Correct Title.7z");
    }

    [Test]
    public void GetReArchiveTarget_VerifiedWithWrongArchiveType_ReturnsTarget()
    {
        MatchResult result = MakeVerifiedResult(isWrongArchiveType: true);

        (string From, string To)? target = RomReArchiver.GetReArchiveTarget(result, "%u - %n");

        target.Should().NotBeNull();
    }

    [Test]
    public void GetReArchiveTarget_Missing_ReturnsNull()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game { ReleaseNumber = 1, Title = "Game" },
            Status = MatchStatus.Missing,
        };

        RomReArchiver.GetReArchiveTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetReArchiveTarget_Untrimmed_ReturnsNull()
    {
        MatchResult result = MakeVerifiedResult(isUntrimmed: true);

        RomReArchiver.GetReArchiveTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetReArchiveTarget_NoScannedRom_ReturnsNull()
    {
        MatchResult result = MakeVerifiedResult(filePath: null);

        RomReArchiver.GetReArchiveTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetReArchiveTarget_EmptyMask_UsesExistingFileName()
    {
        MatchResult result = MakeVerifiedResult(filePath: "/roms/My Game.zip");

        (string From, string To)? target = RomReArchiver.GetReArchiveTarget(result, string.Empty);

        target.Should().NotBeNull();
        target!.Value.To.Should().Be("/roms/My Game.7z");
    }

    [Test]
    public void GetReArchiveTarget_DefaultExtension_OutputsSevenZ()
    {
        MatchResult result = MakeVerifiedResult(filePath: "/roms/game.rar");

        (string From, string To)? target = RomReArchiver.GetReArchiveTarget(result, "%u - %n");

        target!.Value.To.Should().EndWith(".7z");
    }

    [Test]
    public void GetReArchiveTarget_ZipExtension_OutputsZip()
    {
        MatchResult result = MakeVerifiedResult(filePath: "/roms/game.7z");

        (string From, string To)? target = RomReArchiver.GetReArchiveTarget(result, "%u - %n", "zip");

        target!.Value.To.Should().EndWith(".zip");
    }

    [Test]
    public void GetReArchiveTarget_PathAlreadyCorrect_ReturnsTarget()
    {
        // A correctly-named 7z should still return a target — re-archiving in place
        // gives RomForge control over compression quality even when name/format are already right.
        MatchResult result = MakeVerifiedResult(filePath: "/roms/0001 - Correct Title.7z");

        (string From, string To)? target = RomReArchiver.GetReArchiveTarget(result, "%u - %n", "7z");

        target.Should().NotBeNull();
        target!.Value.From.Should().Be("/roms/0001 - Correct Title.7z");
        target.Value.To.Should().Be("/roms/0001 - Correct Title.7z");
    }
}
