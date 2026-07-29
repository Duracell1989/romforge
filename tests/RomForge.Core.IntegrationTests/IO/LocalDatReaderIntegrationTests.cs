using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Models;

namespace RomForge.Core.IntegrationTests.IO;

[TestOf(typeof(LocalDatReader))]
public sealed class LocalDatReaderIntegrationTests
{
    private string _tempDir = string.Empty;

    private const string MinimalDatXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <dat>
          <configuration>
            <datName>Integration Test DAT</datName>
            <datVersion>1</datVersion>
            <system>GBA</system>
            <screenshotsWidth>240</screenshotsWidth>
            <screenshotsHeight>160</screenshotsHeight>
            <romTitle>%u - %n</romTitle>
            <infos/>
            <canOpen><extension>gba</extension></canOpen>
            <search/>
            <newDat>
              <datVersionURL>http://example.com/v</datVersionURL>
              <datURL fileName="test.zip">http://example.com/test.zip</datURL>
              <imURL>http://example.com/imgs/</imURL>
            </newDat>
          </configuration>
          <games>
            <game>
              <imageNumber>1</imageNumber>
              <releaseNumber>1</releaseNumber>
              <title>Real Game</title>
              <files><romCRC extension="gba">AABBCCDD</romCRC></files>
              <im1CRC></im1CRC>
              <im2CRC></im2CRC>
            </game>
          </games>
        </dat>
        """;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    [Test]
    public async Task ReadAsync_RealDatFile_ParsesHeaderAndGames()
    {
        string datPath = Path.Combine(_tempDir, "test.dat");
        await File.WriteAllTextAsync(datPath, MinimalDatXml, Encoding.UTF8);

        LocalDatReader reader = new LocalDatReader(datPath);
        FluentResults.Result<DatFile> result = await reader.ReadAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Header.DatName.Should().Be("Integration Test DAT");
        result.Value.Header.System.Should().Be("GBA");
        result.Value.Games.Should().HaveCount(1);
        result.Value.Games[0].Title.Should().Be("Real Game");
        result.Value.Games[0].Files.RomCrc.Should().Be(0xAABBCCDDu);
    }

    [Test]
    public async Task ReadAsync_MissingFile_ReturnsFailed()
    {
        LocalDatReader reader = new LocalDatReader(Path.Combine(_tempDir, "nonexistent.dat"));

        FluentResults.Result<DatFile> result = await reader.ReadAsync();

        result.IsFailed.Should().BeTrue();
    }
}
