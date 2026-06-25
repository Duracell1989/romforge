using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Scanning;

namespace RomForge.Core.UnitTests.Scanning;

[TestOf(typeof(ScanProgress))]
public sealed class ScanProgressTests
{
    [Test]
    public void Constructor_SetsAllProperties()
    {
        ScanProgress progress = new ScanProgress(5, 10, "game.rom");

        progress.Completed.Should().Be(5);
        progress.Total.Should().Be(10);
        progress.CurrentFile.Should().Be("game.rom");
    }

    [Test]
    public void Equality_SameValues_AreEqual()
    {
        ScanProgress a = new ScanProgress(1, 5, "a.rom");
        ScanProgress b = new ScanProgress(1, 5, "a.rom");

        a.Should().Be(b);
    }

    [Test]
    public void Equality_DifferentCompleted_AreNotEqual()
    {
        ScanProgress a = new ScanProgress(1, 5, "a.rom");
        ScanProgress b = new ScanProgress(2, 5, "a.rom");

        a.Should().NotBe(b);
    }

    [Test]
    public void Equality_DifferentFile_AreNotEqual()
    {
        ScanProgress a = new ScanProgress(1, 5, "a.rom");
        ScanProgress b = new ScanProgress(1, 5, "b.rom");

        a.Should().NotBe(b);
    }

    [Test]
    public void ToString_ContainsAllPropertyValues()
    {
        ScanProgress progress = new ScanProgress(3, 10, "game.rom");

        string str = progress.ToString();

        str.Should().Contain("3");
        str.Should().Contain("10");
        str.Should().Contain("game.rom");
    }
}
