using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Parsers;

namespace RomForge.Core.UnitTests.Parsers;

[TestOf(typeof(XElementExtensions))]
public sealed class XElementExtensionsTests
{
    // --- ElementCI ---

    [Test]
    public void ElementCI_ExactMatch_ReturnsElement()
    {
        XElement parent = new XElement("root", new XElement("game", "value"));

        XElement? result = parent.ElementCI("game");

        result.Should().NotBeNull();
        result!.Value.Should().Be("value");
    }

    [Test]
    public void ElementCI_UppercaseName_ReturnsElement()
    {
        XElement parent = new XElement("root", new XElement("game", "value"));

        XElement? result = parent.ElementCI("GAME");

        result.Should().NotBeNull();
    }

    [Test]
    public void ElementCI_MixedCaseName_ReturnsElement()
    {
        XElement parent = new XElement("root", new XElement("game", "value"));

        XElement? result = parent.ElementCI("GaMe");

        result.Should().NotBeNull();
    }

    [Test]
    public void ElementCI_NoMatch_ReturnsNull()
    {
        XElement parent = new XElement("root", new XElement("game", "value"));

        XElement? result = parent.ElementCI("rom");

        result.Should().BeNull();
    }

    [Test]
    public void ElementCI_MultipleChildren_ReturnsFirst()
    {
        XElement parent = new XElement(
            "root",
            new XElement("game", "first"),
            new XElement("game", "second")
        );

        XElement? result = parent.ElementCI("game");

        result.Should().NotBeNull();
        result!.Value.Should().Be("first");
    }

    // --- ElementsCI ---

    [Test]
    public void ElementsCI_ReturnsAllMatches()
    {
        XElement parent = new XElement(
            "root",
            new XElement("game", "a"),
            new XElement("GAME", "b"),
            new XElement("Game", "c"),
            new XElement("other", "d")
        );

        List<XElement> result = parent.ElementsCI("game").ToList();

        result.Should().HaveCount(3);
        result.Select(e => e.Value).Should().ContainInOrder("a", "b", "c");
    }

    [Test]
    public void ElementsCI_NoMatch_ReturnsEmpty()
    {
        XElement parent = new XElement("root", new XElement("rom", "value"));

        List<XElement> result = parent.ElementsCI("game").ToList();

        result.Should().BeEmpty();
    }
}
