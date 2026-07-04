using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Models;
using RomForge.Core.Parsers;

namespace RomForge.Core.UnitTests.Parsers;

[TestOf(typeof(DatParser))]
public class DatParserTests
{
    private const string MinimalDat = """
        <?xml version="1.0" encoding="UTF-8"?>
        <dat>
          <configuration>
            <datName>Game Boy Advance</datName>
            <datVersion>20210227</datVersion>
            <system>GBA</system>
            <screenshotsWidth>240</screenshotsWidth>
            <screenshotsHeight>160</screenshotsHeight>
            <romTitle>%u - %n</romTitle>
            <infos/>
            <canOpen><extension>gba</extension></canOpen>
            <search/>
            <newDat>
              <datVersionURL>http://example.com/version</datVersionURL>
              <datURL fileName="gba.zip">http://example.com/gba.zip</datURL>
              <imURL>http://example.com/images.zip</imURL>
            </newDat>
          </configuration>
          <games>
            <game>
              <imageNumber>1</imageNumber>
              <releaseNumber>1234</releaseNumber>
              <title>Some Game</title>
              <saveType>Flash 512Kbit</saveType>
              <romSize>16777216</romSize>
              <publisher>Some Publisher</publisher>
              <location>2</location>
              <language>16</language>
              <sourceRom>GBA</sourceRom>
              <comment>A comment</comment>
              <duplicateID>0</duplicateID>
              <files>
                <romCRC extension="gba">ABCDEF01</romCRC>
              </files>
              <im1CRC>12345678</im1CRC>
              <im2CRC>87654321</im2CRC>
            </game>
            <game>
              <imageNumber>2</imageNumber>
              <title>Another Game</title>
              <comment></comment>
              <files>
                <romCRC extension="zip">DEADBEEF</romCRC>
              </files>
              <im1CRC></im1CRC>
              <im2CRC></im2CRC>
            </game>
          </games>
        </dat>
        """;

    private static MemoryStream ToStream(string xml) =>
        new MemoryStream(Encoding.UTF8.GetBytes(xml));

    [Test]
    public async Task ParseAsync_ReturnsCorrectHeader()
    {
        DatFile result = await DatParser.ParseAsync(ToStream(MinimalDat));
        DatHeader header = result.Header;

        header.DatName.Should().Be("Game Boy Advance");
        header.System.Should().Be("GBA");
        header.DatVersion.Should().Be(20210227);
        header.ScreenshotsWidth.Should().Be(240);
        header.ScreenshotsHeight.Should().Be(160);
        header.RomTitle.Should().Be("%u - %n");
        header.NewDatVersionUrl.Should().Be("http://example.com/version");
        header.NewDatUrl.Should().Be("http://example.com/gba.zip");
        header.NewDatFileName.Should().Be("gba.zip");
        header.NewImUrl.Should().Be("http://example.com/images.zip");
    }

    [Test]
    public async Task ParseAsync_ReturnsCorrectGameCount()
    {
        DatFile result = await DatParser.ParseAsync(ToStream(MinimalDat));

        result.Games.Should().HaveCount(2);
    }

    [Test]
    public async Task ParseAsync_ParsesFirstGameCorrectly()
    {
        DatFile result = await DatParser.ParseAsync(ToStream(MinimalDat));
        Game game = result.Games[0];

        game.ImageNumber.Should().Be(1);
        game.ReleaseNumber.Should().Be(1234);
        game.Title.Should().Be("Some Game");
        game.SaveType.Should().Be("Flash 512Kbit");
        game.RomSize.Should().Be(16777216);
        game.Publisher.Should().Be("Some Publisher");
        game.Location.Should().Be(2);
        game.Language.Should().Be(16);
        game.SourceRom.Should().Be("GBA");
        game.Comment.Should().Be("A comment");
        game.DuplicateId.Should().Be(0);
    }

    [Test]
    public async Task ParseAsync_ParsesRomCrcAndExtension()
    {
        DatFile result = await DatParser.ParseAsync(ToStream(MinimalDat));
        GameFiles files = result.Games[0].Files;

        files.RomCrc.Should().Be(0xABCDEF01u);
        files.RomExtension.Should().Be("gba");
    }

    [Test]
    public async Task ParseAsync_ParsesImageCrcs()
    {
        DatFile result = await DatParser.ParseAsync(ToStream(MinimalDat));
        Game game = result.Games[0];

        game.Im1Crc.Should().Be(0x12345678u);
        game.Im2Crc.Should().Be(0x87654321u);
    }

    [Test]
    public async Task ParseAsync_EmptyImageCrcsAreNull()
    {
        DatFile result = await DatParser.ParseAsync(ToStream(MinimalDat));
        Game game = result.Games[1];

        game.Im1Crc.Should().BeNull();
        game.Im2Crc.Should().BeNull();
    }

    [Test]
    public async Task ParseAsync_EmptyOptionalStringFieldsAreNull()
    {
        DatFile result = await DatParser.ParseAsync(ToStream(MinimalDat));
        Game game = result.Games[1];

        game.Comment.Should().BeNull();
        game.SaveType.Should().BeNull();
        game.Publisher.Should().BeNull();
        game.SourceRom.Should().BeNull();
    }

    [Test]
    public async Task ParseAsync_MissingOptionalIntFieldsDefaultToZero()
    {
        DatFile result = await DatParser.ParseAsync(ToStream(MinimalDat));
        Game game = result.Games[1];

        game.ReleaseNumber.Should().Be(0);
        game.Location.Should().Be(0);
        game.Language.Should().Be(0);
        game.DuplicateId.Should().Be(0);
    }
}
