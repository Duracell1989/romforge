using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Operations;
using RomForge.Core.Parsers;
using RomForge.Core.Scanning;

namespace RomForge.Core.UnitTests.Matching
{
    [TestOf(typeof(RomMatcher))]
    public class RomMatcherTests
    {
        private static Game GameWith(
            uint crc,
            string romExt = "gba",
            int release = 0,
            string title = ""
        ) =>
            new()
            {
                Files = new GameFiles { RomCrc = crc, RomExtension = romExt },
                ReleaseNumber = release,
                Title = title,
            };

        private static ScannedRom RomWith(
            uint crc,
            string fileExt = "7z",
            string romExt = "gba",
            string path = "/roms/game.7z",
            uint? trimmedCrc = null,
            string? entryName = null
        ) =>
            new()
            {
                Crc = crc,
                RomExtension = romExt,
                FileExtension = fileExt,
                FilePath = path,
                TrimmedCrc = trimmedCrc,
                EntryName = entryName ?? System.IO.Path.GetFileNameWithoutExtension(path),
            };

        private static DatFile DatWith(params Game[] games) => new() { Games = [.. games] };

        [Test]
        public void Match_CrcAndArchiveExtensionMatch_ReturnsVerified()
        {
            DatFile dat = DatWith(GameWith(0x12345678));
            List<ScannedRom> roms = [RomWith(0x12345678)];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results.Should().HaveCount(1);
            results[0].Status.Should().Be(MatchStatus.Verified);
            results[0].ScannedRom.Should().NotBeNull();
        }

        [Test]
        public void Match_CrcNotFound_ReturnsMissing()
        {
            DatFile dat = DatWith(GameWith(0x12345678));
            List<ScannedRom> roms = [RomWith(0xDEADBEEF)];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Missing);
            results[0].ScannedRom.Should().BeNull();
        }

        [Test]
        public void Match_CrcMatchArchiveExtensionMismatch_ReturnsVerifiedWithWrongArchiveTypeFlag()
        {
            DatFile dat = DatWith(GameWith(0x12345678));
            List<ScannedRom> roms = [RomWith(0x12345678, fileExt: "zip")];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
            results[0].IsWrongArchiveType.Should().BeTrue();
            results[0].ScannedRom.Should().NotBeNull();
        }

        [Test]
        public void Match_EmptyRomList_AllMissing()
        {
            DatFile dat = DatWith(GameWith(0x11111111), GameWith(0x22222222));

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, []).Results;

            results.Should().HaveCount(2);
            results.Should().AllSatisfy(r => r.Status.Should().Be(MatchStatus.Missing));
        }

        [Test]
        public void Match_EmptyDat_ReturnsEmpty()
        {
            DatFile dat = DatWith();

            IReadOnlyList<MatchResult> results = RomMatcher
                .Match(dat, [RomWith(0x12345678)])
                .Results;

            results.Should().BeEmpty();
        }

        [Test]
        public void Match_DuplicateCrcInCollection_FirstWins()
        {
            DatFile dat = DatWith(GameWith(0x12345678));
            List<ScannedRom> roms =
            [
                RomWith(0x12345678, path: "/roms/first.7z"),
                RomWith(0x12345678, path: "/roms/second.7z"),
            ];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
            results[0].ScannedRom!.FilePath.Should().Be("/roms/first.7z");
        }

        [Test]
        public void Match_MixedCollection_ReturnsCorrectStatuses()
        {
            DatFile dat = DatWith(GameWith(0x11111111), GameWith(0x22222222), GameWith(0x33333333));
            List<ScannedRom> roms = [RomWith(0x11111111), RomWith(0x33333333, fileExt: "zip")];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
            results[0].IsWrongArchiveType.Should().BeFalse();
            results[1].Status.Should().Be(MatchStatus.Missing);
            results[2].Status.Should().Be(MatchStatus.Verified);
            results[2].IsWrongArchiveType.Should().BeTrue();
        }

        [Test]
        public void Match_CorrectFilename_ReturnsVerified()
        {
            DatFile dat = DatWith(GameWith(0x12345678, release: 1, title: "Test Game"));
            List<ScannedRom> roms = [RomWith(0x12345678, path: "/roms/0001 - Test Game.7z")];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
            results[0].IsIncorrectlyNamed.Should().BeFalse();
        }

        [Test]
        public void Match_IncorrectFilename_ReturnsVerifiedWithIncorrectlyNamedFlag()
        {
            DatFile dat = DatWith(GameWith(0x12345678, release: 1, title: "Test Game"));
            List<ScannedRom> roms = [RomWith(0x12345678, path: "/roms/Wrong Name.7z")];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
            results[0].IsIncorrectlyNamed.Should().BeTrue();
            results[0].ScannedRom.Should().NotBeNull();
        }

        [Test]
        public void Match_CorrectFilenameButWrongEntryName_FlagsEntryMisnamedNotIncorrectlyNamed()
        {
            // Archives re-archived before the entry-naming fix: the outer archive filename matches
            // the mask, but its internal entry still carries a leftover name (e.g. from the
            // extractor's random temp file). This is NOT an IncorrectlyNamed case — a plain rename
            // (File.Move on the outer file) cannot fix it, and would silently no-op. It is flagged
            // IsEntryMisnamed instead, which keeps the ROM Verified (not Good) so a re-archive fixes it.
            DatFile dat = DatWith(GameWith(0x12345678, release: 1, title: "Test Game"));
            List<ScannedRom> roms =
            [
                RomWith(0x12345678, path: "/roms/0001 - Test Game.7z", entryName: "tmpAB12CD.tmp"),
            ];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
            results[0].IsIncorrectlyNamed.Should().BeFalse();
            results[0].IsEntryMisnamed.Should().BeTrue();
            results[0].DisplayStatus.Should().Be(RomStatus.Verified);
        }

        [Test]
        public void Match_CorrectFilenameAndEntryName_FlagsNeitherNamingIssue()
        {
            DatFile dat = DatWith(GameWith(0x12345678, release: 1, title: "Test Game"));
            List<ScannedRom> roms =
            [
                RomWith(
                    0x12345678,
                    path: "/roms/0001 - Test Game.7z",
                    entryName: "0001 - Test Game"
                ),
            ];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].IsIncorrectlyNamed.Should().BeFalse();
            results[0].IsEntryMisnamed.Should().BeFalse();
        }

        [Test]
        public void Match_CustomExpectedArchiveExtension_UsesProvidedExtension()
        {
            DatFile dat = DatWith(GameWith(0x12345678));
            List<ScannedRom> roms = [RomWith(0x12345678, fileExt: "zip")];

            IReadOnlyList<MatchResult> results = RomMatcher
                .Match(dat, roms, expectedArchiveExtension: "zip")
                .Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
        }

        [Test]
        public void Match_TrimmedCrcMatchesGame_ReturnsVerifiedWithUntrimmedFlag()
        {
            DatFile dat = DatWith(GameWith(0xABCDABCD));
            List<ScannedRom> roms = [RomWith(0xDEADDEAD, trimmedCrc: 0xABCDABCD)];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
            results[0].IsUntrimmed.Should().BeTrue();
            results[0].ScannedRom.Should().NotBeNull();
        }

        [Test]
        public void Match_FullCrcMatchTakesPriorityOverTrimmedCrc()
        {
            DatFile dat = DatWith(GameWith(0x11111111), GameWith(0x22222222));
            List<ScannedRom> roms =
            [
                RomWith(0x11111111),
                RomWith(0xDEADBEEF, trimmedCrc: 0x11111111),
            ];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Verified);
            results[1].Status.Should().Be(MatchStatus.Missing);
        }

        [Test]
        public void Match_TrimmedCrcPresentButNoGameMatchesTrimmedCrc_ReturnsMissing()
        {
            DatFile dat = DatWith(GameWith(0x99999999));
            List<ScannedRom> roms = [RomWith(0xAAAAAAAA, trimmedCrc: 0xBBBBBBBB)];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].Status.Should().Be(MatchStatus.Missing);
        }

        [Test]
        public void Match_ScannedRomCrcNotInDat_IsInUnmatchedRoms()
        {
            DatFile dat = DatWith(GameWith(0x11111111));
            List<ScannedRom> roms = [RomWith(0x11111111), RomWith(0xDEADBEEF)];

            MatchSummary summary = RomMatcher.Match(dat, roms);

            summary.UnmatchedRoms.Should().HaveCount(1);
            summary.UnmatchedRoms[0].Crc.Should().Be(0xDEADBEEF);
        }

        [Test]
        public void Match_AllRomsMatchDat_UnmatchedIsEmpty()
        {
            DatFile dat = DatWith(GameWith(0x11111111));
            List<ScannedRom> roms = [RomWith(0x11111111)];

            MatchSummary summary = RomMatcher.Match(dat, roms);

            summary.UnmatchedRoms.Should().BeEmpty();
        }

        [Test]
        public void Match_EmptyDat_AllRomsAreUnmatched()
        {
            DatFile dat = DatWith();
            List<ScannedRom> roms = [RomWith(0x11111111), RomWith(0x22222222)];

            MatchSummary summary = RomMatcher.Match(dat, roms);

            summary.UnmatchedRoms.Should().HaveCount(2);
        }

        [Test]
        public void Match_DuplicateOfKnownRom_IsNotUnmatched()
        {
            DatFile dat = DatWith(GameWith(0x12345678));
            List<ScannedRom> roms =
            [
                RomWith(0x12345678, path: "/roms/game.7z"),
                RomWith(0x12345678, path: "/roms/copy.7z"),
            ];

            MatchSummary summary = RomMatcher.Match(dat, roms);

            summary.UnmatchedRoms.Should().BeEmpty();
        }

        [Test]
        public void Match_UntrimmedVersionOfKnownRom_IsNotUnmatched()
        {
            DatFile dat = DatWith(GameWith(0xABCDABCD));
            List<ScannedRom> roms = [RomWith(0xDEADDEAD, trimmedCrc: 0xABCDABCD)];

            MatchSummary summary = RomMatcher.Match(dat, roms);

            summary.UnmatchedRoms.Should().BeEmpty();
        }

        [Test]
        public void Match_ArchiveExtensionUppercase_TreatedCaseInsensitive()
        {
            DatFile dat = DatWith(GameWith(0x12345678));
            List<ScannedRom> roms = [RomWith(0x12345678, fileExt: "7Z")];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].IsWrongArchiveType.Should().BeFalse();
        }

        [Test]
        public async Task Match_EntryNameFromDatWithLeadingDotExtension_RoundTripsWithoutEntryMisnamedFlag()
        {
            // Regression for the 3DS re-archive-forever loop: ADVANsCEne DATs declare
            // extension=".3ds" (leading dot). ArchiveWorkspace.BuildEntryName also prepends a dot,
            // so a raw pass-through of the DAT value produced "Name..3ds" on disk. Rescanning that
            // entry (Path.GetFileNameWithoutExtension strips only the outer ".3ds", leaving a
            // trailing dot) never matched the expected stem, so IsEntryMisnamed stayed true forever
            // and Re-Archive All never converged. This chains DatParser -> BuildEntryName -> the
            // rescan's own name-stripping -> RomMatcher, the exact path that broke.
            const string dat = """
                <?xml version="1.0" encoding="UTF-8"?>
                <dat>
                  <configuration>
                    <datName>Nintendo 3DS</datName>
                    <datVersion>1</datVersion>
                    <system>3DS</system>
                    <screenshotsWidth>0</screenshotsWidth>
                    <screenshotsHeight>0</screenshotsHeight>
                    <romTitle>%n</romTitle>
                    <infos/>
                    <canOpen><extension>3ds</extension></canOpen>
                    <search/>
                    <newDat>
                      <datVersionURL>http://example.com/version</datVersionURL>
                      <datURL fileName="3ds.zip">http://example.com/3ds.zip</datURL>
                      <imURL>http://example.com/images.zip</imURL>
                    </newDat>
                  </configuration>
                  <games>
                    <game>
                      <releaseNumber>1</releaseNumber>
                      <title>Test Game</title>
                      <files>
                        <romCRC extension=".3ds">ABCDEF01</romCRC>
                      </files>
                    </game>
                  </games>
                </dat>
                """;
            DatFile parsed = await DatParser.ParseAsync(
                new MemoryStream(Encoding.UTF8.GetBytes(dat))
            );
            Game game = parsed.Games[0] with
            {
                Files = parsed.Games[0].Files with { RomCrc = 0x12345678 },
            };

            const string archivePath = "/roms/0001 - Test Game.7z";
            string entryOnDisk = ArchiveWorkspace.BuildEntryName(
                archivePath,
                game.Files.RomExtension
            );
            string entryAsRescanned = Path.GetFileNameWithoutExtension(entryOnDisk);

            DatFile dataFile = DatWith(game);
            List<ScannedRom> roms =
            [
                RomWith(0x12345678, path: archivePath, entryName: entryAsRescanned),
            ];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dataFile, roms).Results;

            results[0].IsEntryMisnamed.Should().BeFalse();
        }

        [Test]
        public void Match_BothWrongArchiveAndWrongName_SetsBothFlags()
        {
            DatFile dat = DatWith(GameWith(0x12345678, release: 1, title: "Correct Name"));
            List<ScannedRom> roms =
            [
                RomWith(0x12345678, fileExt: "zip", path: "/roms/Wrong Name.zip"),
            ];

            IReadOnlyList<MatchResult> results = RomMatcher.Match(dat, roms).Results;

            results[0].IsWrongArchiveType.Should().BeTrue();
            results[0].IsIncorrectlyNamed.Should().BeTrue();
        }
    }
}
