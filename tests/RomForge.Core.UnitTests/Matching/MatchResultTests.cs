using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;

namespace RomForge.Core.UnitTests.Matching;

[TestOf(typeof(MatchResult))]
public sealed class MatchResultTests
{
    [Test]
    public void IsGood_VerifiedAndReArchived_ReturnsTrue()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsReArchived = true,
        };

        result.IsGood.Should().BeTrue();
    }

    [Test]
    public void IsGood_VerifiedNotReArchived_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
        };

        result.IsGood.Should().BeFalse();
    }

    [Test]
    public void IsGood_Missing_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Missing,
            IsReArchived = true,
        };

        result.IsGood.Should().BeFalse();
    }

    [Test]
    public void IsGood_VerifiedReArchivedAndUntrimmed_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsUntrimmed = true,
            IsReArchived = true,
        };

        result.IsGood.Should().BeFalse();
    }

    [Test]
    public void IsGood_VerifiedReArchivedAndWrongArchiveType_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsWrongArchiveType = true,
            IsReArchived = true,
        };

        result.IsGood.Should().BeFalse();
    }

    [Test]
    public void IsGood_VerifiedReArchivedAndIncorrectlyNamed_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsIncorrectlyNamed = true,
            IsReArchived = true,
        };

        result.IsGood.Should().BeFalse();
    }
}
