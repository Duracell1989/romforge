using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;

namespace RomForge.Core.UnitTests.Matching
{
    [TestOf(typeof(NamingMask))]
    public sealed class NamingMaskTests
    {
        [Test]
        public void Expand_ReleaseAndTitle_ReturnsFormattedName()
        {
            Game game = new() { ReleaseNumber = 1, Title = "Test Game" };
            NamingMask.Expand("%u - %n", game).Should().Be("0001 - Test Game");
        }

        [TestCase(1, "0001")]
        [TestCase(42, "0042")]
        [TestCase(999, "0999")]
        [TestCase(9999, "9999")]
        public void Expand_ReleaseNumber_ZeroPadsToFourDigits(int number, string expected)
        {
            Game game = new() { ReleaseNumber = number };
            NamingMask.Expand("%u", game).Should().Be(expected);
        }

        [Test]
        public void Expand_SourceRomToken_SubstitutesValue()
        {
            Game game = new() { SourceRom = "Original Title" };
            NamingMask.Expand("%s", game).Should().Be("Original Title");
        }

        [Test]
        public void Expand_SourceRomToken_NullSourceRom_ReturnsEmpty()
        {
            Game game = new() { SourceRom = null };
            NamingMask.Expand("%s", game).Should().Be(string.Empty);
        }

        [Test]
        public void Expand_CommentToken_SubstitutesValue()
        {
            Game game = new() { Comment = "A note" };
            NamingMask.Expand("%o", game).Should().Be("A note");
        }

        [Test]
        public void Expand_CommentToken_NullComment_ReturnsEmpty()
        {
            Game game = new() { Comment = null };
            NamingMask.Expand("%o", game).Should().Be(string.Empty);
        }

        [TestCase(0b000, "")]
        [TestCase(0b001, "")]
        [TestCase(0b011, "(M2)")]
        [TestCase(0b111, "(M3)")]
        public void Expand_MultiLangToken_ReturnsCorrectMarker(int language, string expected)
        {
            Game game = new() { Language = language };
            NamingMask.Expand("%m", game).Should().Be(expected);
        }

        [Test]
        public void Expand_EmptyMask_ReturnsEmptyString()
        {
            Game game = new() { ReleaseNumber = 1, Title = "Test Game" };
            NamingMask.Expand(string.Empty, game).Should().Be(string.Empty);
        }
    }
}
