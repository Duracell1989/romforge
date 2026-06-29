using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Operations;
using RomForge.Core.Scanning;

namespace RomForge.Core.UnitTests.Operations;

[TestOf(typeof(RomTrimmer))]
public sealed class RomTrimmerTests
{
    private static MatchResult MakeResult(
        bool isUntrimmed,
        string? filePath = "/roms/game.7z",
        int release = 1,
        string title = "My Game"
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
            IsUntrimmed = isUntrimmed,
        };

    [Test]
    public void GetTrimTarget_Untrimmed_ReturnsPaths()
    {
        MatchResult result = MakeResult(isUntrimmed: true);

        (string From, string To)? target = RomTrimmer.GetTrimTarget(result, "%u - %n");

        target.Should().NotBeNull();
        target!.Value.From.Should().Be("/roms/game.7z");
        target.Value.To.Should().Be("/roms/0001 - My Game.7z");
    }

    [Test]
    public void GetTrimTarget_SameNameAndFormat_StillReturnsPaths()
    {
        MatchResult result = MakeResult(isUntrimmed: true, filePath: "/roms/0001 - My Game.7z");

        (string From, string To)? target = RomTrimmer.GetTrimTarget(result, "%u - %n");

        target.Should().NotBeNull();
        target!.Value.From.Should().Be("/roms/0001 - My Game.7z");
        target.Value.To.Should().Be("/roms/0001 - My Game.7z");
    }

    [Test]
    public void GetTrimTarget_NotUntrimmed_ReturnsNull()
    {
        MatchResult result = MakeResult(isUntrimmed: false);

        RomTrimmer.GetTrimTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetTrimTarget_Missing_ReturnsNull()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game { ReleaseNumber = 1, Title = "My Game" },
            Status = MatchStatus.Missing,
        };

        RomTrimmer.GetTrimTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetTrimTarget_NoScannedRom_ReturnsNull()
    {
        MatchResult result = MakeResult(isUntrimmed: true, filePath: null);

        RomTrimmer.GetTrimTarget(result, "%u - %n").Should().BeNull();
    }

    [Test]
    public void GetTrimTarget_EmptyMask_UsesExistingFileStem()
    {
        MatchResult result = MakeResult(isUntrimmed: true, filePath: "/roms/My Favourite Game.7z");

        (string From, string To)? target = RomTrimmer.GetTrimTarget(result, string.Empty);

        target.Should().NotBeNull();
        target!.Value.To.Should().Be("/roms/My Favourite Game.7z");
    }

    [Test]
    public void GetTrimTarget_ZipExtension_OutputsZip()
    {
        MatchResult result = MakeResult(isUntrimmed: true, filePath: "/roms/game.7z");

        (string From, string To)? target = RomTrimmer.GetTrimTarget(result, "%u - %n", "zip");

        target!.Value.To.Should().EndWith(".zip");
    }

    [Test]
    public void GetTrimTarget_DefaultExtension_OutputsSevenZ()
    {
        MatchResult result = MakeResult(isUntrimmed: true, filePath: "/roms/game.7z");

        (string From, string To)? target = RomTrimmer.GetTrimTarget(result, "%u - %n");

        target!.Value.To.Should().EndWith(".7z");
    }
}
