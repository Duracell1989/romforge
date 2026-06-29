using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;

namespace RomForge.Core.UnitTests.Matching;

[TestOf(typeof(MatchResult))]
public sealed class MatchResultTests
{
    [Test]
    public void IsGood_VerifiedWithNoFlags_ReturnsTrue()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
        };

        result.IsGood.Should().BeTrue();
    }

    [Test]
    public void IsGood_VerifiedWithNoFlags_NotReArchived_ReturnsTrue()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsReArchived = false,
        };

        result.IsGood.Should().BeTrue();
    }

    [Test]
    public void IsGood_Missing_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Missing,
        };

        result.IsGood.Should().BeFalse();
    }

    [Test]
    public void IsGood_VerifiedAndUntrimmed_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsUntrimmed = true,
        };

        result.IsGood.Should().BeFalse();
    }

    [Test]
    public void IsGood_VerifiedAndWrongArchiveType_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsWrongArchiveType = true,
        };

        result.IsGood.Should().BeFalse();
    }

    [Test]
    public void IsGood_VerifiedAndIncorrectlyNamed_ReturnsFalse()
    {
        MatchResult result = new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsIncorrectlyNamed = true,
        };

        result.IsGood.Should().BeFalse();
    }
}
