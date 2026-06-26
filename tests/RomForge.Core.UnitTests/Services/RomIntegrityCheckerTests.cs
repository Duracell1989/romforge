using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Scanning;
using RomForge.Core.Services;

namespace RomForge.Core.UnitTests.Services;

[TestOf(typeof(RomIntegrityChecker))]
public sealed class RomIntegrityCheckerTests
{
    [Test]
    public void FindStaleResults_FileExists_ReturnsEmpty()
    {
        string existingPath = typeof(RomIntegrityCheckerTests).Assembly.Location;
        IReadOnlyList<MatchResult> results =
        [
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 1 },
                Status = MatchStatus.Verified,
                ScannedRom = new ScannedRom { FilePath = existingPath },
            },
        ];

        IReadOnlyList<MatchResult> stale = RomIntegrityChecker.FindStaleResults(results);

        stale.Should().BeEmpty();
    }

    [Test]
    public void FindStaleResults_FileMissing_ReturnsStaleResult()
    {
        IReadOnlyList<MatchResult> results =
        [
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 1 },
                Status = MatchStatus.Verified,
                ScannedRom = new ScannedRom { FilePath = Path.Combine(Path.GetTempPath(), "romforge_nonexistent_test.zip") },
            },
        ];

        IReadOnlyList<MatchResult> stale = RomIntegrityChecker.FindStaleResults(results);

        stale.Should().HaveCount(1);
        stale[0].Game.ReleaseNumber.Should().Be(1);
    }

    [Test]
    public void FindStaleResults_MissingStatus_NotChecked()
    {
        IReadOnlyList<MatchResult> results =
        [
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 1 },
                Status = MatchStatus.Missing,
            },
        ];

        IReadOnlyList<MatchResult> stale = RomIntegrityChecker.FindStaleResults(results);

        stale.Should().BeEmpty();
    }

    [Test]
    public void FindStaleResults_VerifiedWithNullScannedRom_NotChecked()
    {
        IReadOnlyList<MatchResult> results =
        [
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 1 },
                Status = MatchStatus.Verified,
                ScannedRom = null,
            },
        ];

        IReadOnlyList<MatchResult> stale = RomIntegrityChecker.FindStaleResults(results);

        stale.Should().BeEmpty();
    }

    [Test]
    public void FindStaleResults_MixedResults_ReturnsOnlyStale()
    {
        string existingPath = typeof(RomIntegrityCheckerTests).Assembly.Location;
        string missingPath = Path.Combine(Path.GetTempPath(), "romforge_nonexistent_test_2.zip");

        IReadOnlyList<MatchResult> results =
        [
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 1 },
                Status = MatchStatus.Verified,
                ScannedRom = new ScannedRom { FilePath = existingPath },
            },
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 2 },
                Status = MatchStatus.Verified,
                ScannedRom = new ScannedRom { FilePath = missingPath },
            },
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 3 },
                Status = MatchStatus.Missing,
            },
        ];

        IReadOnlyList<MatchResult> stale = RomIntegrityChecker.FindStaleResults(results);

        stale.Should().HaveCount(1);
        stale[0].Game.ReleaseNumber.Should().Be(2);
    }
}
