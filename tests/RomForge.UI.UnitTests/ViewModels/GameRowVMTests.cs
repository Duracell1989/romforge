using System.Collections.Generic;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests.ViewModels;

[TestOf(typeof(GameRowVM))]
public sealed class GameRowVMTests
{
    private static GameRowVM MakeRow(
        MatchResult result,
        DatHeader? header = null,
        IReadOnlyList<LanguageBit>? bits = null
    ) =>
        new GameRowVM(result, string.Empty, header ?? new DatHeader(), bits ?? []);

    private static MatchResult MakeVerified(
        bool incorrectlyNamed = false,
        bool wrongArchiveType = false,
        bool untrimmed = false,
        bool reArchived = false,
        int release = 1,
        string title = "Test",
        long romSize = 0,
        int location = 0,
        int language = 0
    ) =>
        new MatchResult
        {
            Game = new Game
            {
                ReleaseNumber = release,
                Title = title,
                RomSize = romSize,
                Location = location,
                Language = language,
            },
            Status = MatchStatus.Verified,
            IsIncorrectlyNamed = incorrectlyNamed,
            IsWrongArchiveType = wrongArchiveType,
            IsUntrimmed = untrimmed,
            IsReArchived = reArchived,
        };

    // --- StatusText ---

    [Test]
    public void StatusText_Missing_ReturnsMissing()
    {
        GameRowVM vm = MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Missing });

        vm.StatusText.Should().Be("Missing");
    }

    [Test]
    public void StatusText_Untrimmed_ReturnsUntrimmed()
    {
        GameRowVM vm = MakeRow(MakeVerified(untrimmed: true));

        vm.StatusText.Should().Be("Untrimmed");
    }

    [Test]
    public void StatusText_WrongArchiveType_ReturnsWrongArchive()
    {
        GameRowVM vm = MakeRow(MakeVerified(wrongArchiveType: true));

        vm.StatusText.Should().Be("Wrong Archive");
    }

    [Test]
    public void StatusText_IncorrectlyNamed_ReturnsIncorrectlyNamed()
    {
        GameRowVM vm = MakeRow(MakeVerified(incorrectlyNamed: true));

        vm.StatusText.Should().Be("Incorrectly Named");
    }

    [Test]
    public void StatusText_ReArchived_ReturnsGood()
    {
        GameRowVM vm = MakeRow(MakeVerified(reArchived: true));

        vm.StatusText.Should().Be("Good");
    }

    [Test]
    public void StatusText_PlainVerified_ReturnsVerified()
    {
        GameRowVM vm = MakeRow(MakeVerified());

        vm.StatusText.Should().Be("Verified");
    }

    // --- StatusSortKey ---

    [Test]
    public void StatusSortKey_Missing_Returns0()
    {
        GameRowVM vm = MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Missing });

        vm.StatusSortKey.Should().Be(0);
    }

    [Test]
    public void StatusSortKey_Untrimmed_Returns1()
    {
        GameRowVM vm = MakeRow(MakeVerified(untrimmed: true));

        vm.StatusSortKey.Should().Be(1);
    }

    [Test]
    public void StatusSortKey_WrongArchiveType_Returns2()
    {
        GameRowVM vm = MakeRow(MakeVerified(wrongArchiveType: true));

        vm.StatusSortKey.Should().Be(2);
    }

    [Test]
    public void StatusSortKey_IncorrectlyNamed_Returns3()
    {
        GameRowVM vm = MakeRow(MakeVerified(incorrectlyNamed: true));

        vm.StatusSortKey.Should().Be(3);
    }

    [Test]
    public void StatusSortKey_PlainVerified_Returns4()
    {
        GameRowVM vm = MakeRow(MakeVerified());

        vm.StatusSortKey.Should().Be(4);
    }

    [Test]
    public void StatusSortKey_ReArchived_Returns5()
    {
        GameRowVM vm = MakeRow(MakeVerified(reArchived: true));

        vm.StatusSortKey.Should().Be(5);
    }

    // --- Language ---

    [Test]
    public void Language_ZeroBitmask_ReturnsEmpty()
    {
        IReadOnlyList<LanguageBit> bits = [new LanguageBit(0, "EN")];
        GameRowVM vm = MakeRow(MakeVerified(language: 0), bits: bits);

        vm.Language.Should().Be(string.Empty);
    }

    [Test]
    public void Language_NoBitsProvided_ReturnsBitmaskAsString()
    {
        GameRowVM vm = MakeRow(MakeVerified(language: 3), bits: []);

        vm.Language.Should().Be("3");
    }

    [Test]
    public void Language_SingleBitSet_ReturnsLabel()
    {
        IReadOnlyList<LanguageBit> bits = [new LanguageBit(0, "EN")];
        GameRowVM vm = MakeRow(MakeVerified(language: 1), bits: bits);

        vm.Language.Should().Be("EN");
    }

    [Test]
    public void Language_MultipleBitsSet_ReturnsJoinedLabels()
    {
        IReadOnlyList<LanguageBit> bits =
        [
            new LanguageBit(0, "EN"),
            new LanguageBit(1, "FR"),
        ];
        GameRowVM vm = MakeRow(MakeVerified(language: 3), bits: bits);

        vm.Language.Should().Be("EN FR");
    }

    [Test]
    public void Language_BitmaskWithNoMatchingBits_ReturnsBitmaskAsString()
    {
        // Bits 0 and 1 are set in the bitmask, but only bit 2 is defined in the language list.
        IReadOnlyList<LanguageBit> bits = [new LanguageBit(2, "DE")];
        GameRowVM vm = MakeRow(MakeVerified(language: 3), bits: bits);

        vm.Language.Should().Be("3");
    }

    // --- Location ---

    [TestCase(1, "(EU)")]
    [TestCase(2, "(US)")]
    [TestCase(7, "(JP)")]
    [TestCase(16, "(CN)")]
    [TestCase(22, "(PT)")]
    public void Location_KnownCode_ReturnsExpectedString(int code, string expected)
    {
        GameRowVM vm = MakeRow(MakeVerified(location: code));

        vm.Location.Should().Be(expected);
    }

    [Test]
    public void Location_UnknownCode_ReturnsNumericString()
    {
        GameRowVM vm = MakeRow(MakeVerified(location: 99));

        vm.Location.Should().Be("99");
    }

    // --- RomSize ---

    [Test]
    public void RomSize_ZeroBytes_ReturnsEmpty()
    {
        GameRowVM vm = MakeRow(MakeVerified(romSize: 0));

        vm.RomSize.Should().Be(string.Empty);
    }

    [Test]
    public void RomSize_SmallFile_ReturnsKB()
    {
        GameRowVM vm = MakeRow(MakeVerified(romSize: 512));

        // F1 formatting is culture-sensitive; compute expected the same way the method does.
        vm.RomSize.Should().Be($"{512 / 1024.0:F1} KB");
    }

    [Test]
    public void RomSize_LargeFile_ReturnsMB()
    {
        GameRowVM vm = MakeRow(MakeVerified(romSize: 2 * 1024 * 1024));

        vm.RomSize.Should().Be($"{2 * 1024 * 1024 / (1024.0 * 1024.0):F1} MB");
    }

    // --- ExpectedFileName ---

    [Test]
    public void ExpectedFileName_NotIncorrectlyNamed_ReturnsNull()
    {
        GameRowVM vm = MakeRow(MakeVerified(incorrectlyNamed: false));

        vm.ExpectedFileName.Should().BeNull();
    }

    [Test]
    public void ExpectedFileName_EmptyMask_ReturnsNull()
    {
        GameRowVM vm = MakeRow(
            MakeVerified(incorrectlyNamed: true),
            header: new DatHeader { RomTitle = string.Empty }
        );

        vm.ExpectedFileName.Should().BeNull();
    }

    [Test]
    public void ExpectedFileName_WithMask_ReturnsExpandedName()
    {
        GameRowVM vm = MakeRow(
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 1, Title = "Mario" },
                Status = MatchStatus.Verified,
                IsIncorrectlyNamed = true,
            },
            header: new DatHeader { RomTitle = "%u %n" }
        );

        vm.ExpectedFileName.Should().Be("0001 Mario");
    }
}
