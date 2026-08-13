using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;

namespace RomForge.Core.UnitTests.Matching
{
    [TestOf(typeof(MatchResult))]
    public sealed class MatchResultTests
    {
        private static MatchResult Verified(
            bool incorrectlyNamed = false,
            bool entryMisnamed = false,
            bool wrongArchiveType = false,
            bool untrimmed = false,
            bool reArchived = false
        ) =>
            new MatchResult
            {
                Game = new Game(),
                Status = MatchStatus.Verified,
                IsIncorrectlyNamed = incorrectlyNamed,
                IsEntryMisnamed = entryMisnamed,
                IsWrongArchiveType = wrongArchiveType,
                IsUntrimmed = untrimmed,
                IsReArchived = reArchived,
            };

        private static MatchResult Missing() =>
            new MatchResult { Game = new Game(), Status = MatchStatus.Missing };

        // --- DisplayStatus: one bucket per single flag ---

        [Test]
        public void DisplayStatus_Missing_ReturnsMissing() =>
            Missing().DisplayStatus.Should().Be(RomStatus.Missing);

        [Test]
        public void DisplayStatus_VerifiedNotReArchived_ReturnsVerified() =>
            Verified().DisplayStatus.Should().Be(RomStatus.Verified);

        [Test]
        public void DisplayStatus_VerifiedAndReArchived_ReturnsGood() =>
            Verified(reArchived: true).DisplayStatus.Should().Be(RomStatus.Good);

        [Test]
        public void DisplayStatus_Untrimmed_ReturnsUntrimmed() =>
            Verified(untrimmed: true).DisplayStatus.Should().Be(RomStatus.Untrimmed);

        [Test]
        public void DisplayStatus_WrongArchiveType_ReturnsWrongArchive() =>
            Verified(wrongArchiveType: true).DisplayStatus.Should().Be(RomStatus.WrongArchive);

        [Test]
        public void DisplayStatus_IncorrectlyNamed_ReturnsIncorrectlyNamed() =>
            Verified(incorrectlyNamed: true).DisplayStatus.Should().Be(RomStatus.IncorrectlyNamed);

        // --- DisplayStatus: priority ordering (most severe issue wins) ---

        [Test]
        public void DisplayStatus_MissingWithOtherFlags_MissingWins()
        {
            MatchResult result = new MatchResult
            {
                Game = new Game(),
                Status = MatchStatus.Missing,
                IsUntrimmed = true,
                IsWrongArchiveType = true,
            };

            result.DisplayStatus.Should().Be(RomStatus.Missing);
        }

        [Test]
        public void DisplayStatus_UntrimmedAndWrongArchive_UntrimmedWins() =>
            Verified(wrongArchiveType: true, untrimmed: true)
                .DisplayStatus.Should()
                .Be(RomStatus.Untrimmed);

        [Test]
        public void DisplayStatus_WrongArchiveAndIncorrectlyNamed_WrongArchiveWins() =>
            Verified(incorrectlyNamed: true, wrongArchiveType: true)
                .DisplayStatus.Should()
                .Be(RomStatus.WrongArchive);

        [Test]
        public void DisplayStatus_ReArchivedButWrongArchive_WrongArchiveWins() =>
            // A stale re-archived mark never hides an outstanding issue.
            Verified(wrongArchiveType: true, reArchived: true)
                .DisplayStatus.Should()
                .Be(RomStatus.WrongArchive);

        [Test]
        public void DisplayStatus_EntryMisnamedNotReArchived_ReturnsVerified() =>
            // A wrong entry name inside the archive is not a distinct status; the ROM is simply not
            // yet Good. It stays Verified so a re-archive is offered — unlike IncorrectlyNamed, it is
            // never fixable by a plain rename.
            Verified(entryMisnamed: true).DisplayStatus.Should().Be(RomStatus.Verified);

        [Test]
        public void DisplayStatus_ReArchivedButEntryMisnamed_ReturnsVerified() =>
            // A misnamed inner entry blocks Good even when the re-archived mark is set: the archive
            // still needs repacking to rewrite the entry.
            Verified(entryMisnamed: true, reArchived: true)
                .DisplayStatus.Should()
                .Be(RomStatus.Verified);

        // --- IsGood is derived from DisplayStatus ---

        [Test]
        public void IsGood_VerifiedNotReArchived_ReturnsFalse() =>
            // The reported bug: a freshly scanned, coincidentally-correct ROM is NOT good
            // until RomForge has re-archived it.
            Verified().IsGood.Should().BeFalse();

        [Test]
        public void IsGood_VerifiedAndReArchived_ReturnsTrue() =>
            Verified(reArchived: true).IsGood.Should().BeTrue();

        [Test]
        public void IsGood_ReArchivedButEntryMisnamed_ReturnsFalse() =>
            Verified(entryMisnamed: true, reArchived: true).IsGood.Should().BeFalse();

        [Test]
        public void IsGood_Missing_ReturnsFalse() => Missing().IsGood.Should().BeFalse();

        [Test]
        public void IsGood_ReArchivedButUntrimmed_ReturnsFalse() =>
            Verified(untrimmed: true, reArchived: true).IsGood.Should().BeFalse();

        [Test]
        public void IsGood_ReArchivedButWrongArchiveType_ReturnsFalse() =>
            Verified(wrongArchiveType: true, reArchived: true).IsGood.Should().BeFalse();

        [Test]
        public void IsGood_ReArchivedButIncorrectlyNamed_ReturnsFalse() =>
            Verified(incorrectlyNamed: true, reArchived: true).IsGood.Should().BeFalse();
    }
}
