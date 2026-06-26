using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Operations;
using RomForge.Core.Scanning;

namespace RomForge.Core.UnitTests.Operations;

[TestOf(typeof(RomRenamer))]
public sealed class RomRenamerTests
{
    private static MatchResult MakeResult(
        bool isIncorrectlyNamed,
        string? filePath = "/roms/Wrong Name.7z",
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
                    FileExtension = "7z",
                    RomExtension = "gba",
                },
            IsIncorrectlyNamed = isIncorrectlyNamed,
        };

    [Test]
    public void GetRenameTarget_IncorrectlyNamed_ReturnsFromAndToPath()
    {
        MatchResult result = MakeResult(isIncorrectlyNamed: true);

        (string From, string To)? target = RomRenamer.GetRenameTarget(result, "%u - %n");

        target.Should().NotBeNull();
        target!.Value.From.Should().Be("/roms/Wrong Name.7z");
        target.Value.To.Should().Be("/roms/0001 - Correct Title.7z");
    }

    [Test]
    public void GetRenameTarget_NotIncorrectlyNamed_ReturnsNull()
    {
        MatchResult result = MakeResult(isIncorrectlyNamed: false);

        RomRenamer.GetRenameTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetRenameTarget_Missing_ReturnsNull()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game { ReleaseNumber = 1, Title = "Correct Title" },
            Status = MatchStatus.Missing,
        };

        RomRenamer.GetRenameTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetRenameTarget_EmptyMask_ReturnsNull()
    {
        MatchResult result = MakeResult(isIncorrectlyNamed: true);

        RomRenamer.GetRenameTarget(result, string.Empty).Should().BeNull();
    }

    [Test]
    public void GetRenameTarget_NoScannedRom_ReturnsNull()
    {
        MatchResult result = MakeResult(isIncorrectlyNamed: true, filePath: null);

        RomRenamer.GetRenameTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetRenameTarget_PreservesArchiveExtension()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game { ReleaseNumber = 1, Title = "Game" },
            Status = MatchStatus.Verified,
            ScannedRom = new ScannedRom
            {
                FilePath = "/roms/old.zip",
                FileExtension = "zip",
                RomExtension = "gba",
            },
            IsIncorrectlyNamed = true,
        };

        (string From, string To)? target = RomRenamer.GetRenameTarget(result, "%u - %n");

        target!.Value.To.Should().EndWith(".zip");
    }
}
