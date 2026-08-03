using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IO;

namespace RomForge.Core.UnitTests.IO
{
    [TestOf(typeof(LocalDatReader))]
    public sealed class LocalDatReaderTests
    {
        private const string MinimalDatXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <dat>
              <configuration>
                <datName>Test DAT</datName>
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
                  <title>Test Game</title>
                  <files><romCRC extension="gba">AABBCCDD</romCRC></files>
                  <im1CRC></im1CRC>
                  <im2CRC></im2CRC>
                  <comment></comment>
                </game>
              </games>
            </dat>
            """;

        private string _tempDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_tempDir, recursive: true);

        [Test]
        public async Task ReadAsync_RawXmlFile_ParsesDat()
        {
            string path = WriteXmlFile("test.xml");
            LocalDatReader reader = new(path);

            FluentResults.Result<RomForge.Core.Models.DatFile> result = await reader.ReadAsync();

            result.IsSuccess.Should().BeTrue();
            result.Value.Header.DatName.Should().Be("Test DAT");
            result.Value.Games.Should().HaveCount(1);
        }

        [Test]
        public async Task ReadAsync_ZipWrappedXml_ParsesDat()
        {
            string path = WriteZipFile("test.zip", "test.xml");
            LocalDatReader reader = new(path);

            FluentResults.Result<RomForge.Core.Models.DatFile> result = await reader.ReadAsync();

            result.IsSuccess.Should().BeTrue();
            result.Value.Header.DatName.Should().Be("Test DAT");
            result.Value.Games.Should().HaveCount(1);
        }

        [Test]
        public async Task ReadAsync_MissingFile_ReturnsFailed()
        {
            LocalDatReader reader = new(Path.Combine(_tempDir, "missing.xml"));

            FluentResults.Result<RomForge.Core.Models.DatFile> result = await reader.ReadAsync();

            result.IsFailed.Should().BeTrue();
        }

        private string WriteXmlFile(string name)
        {
            string path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, MinimalDatXml, Encoding.UTF8);
            return path;
        }

        private string WriteZipFile(string zipName, string entryName)
        {
            string path = Path.Combine(_tempDir, zipName);
            using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
            ZipArchiveEntry entry = zip.CreateEntry(entryName);
            using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
            writer.Write(MinimalDatXml);
            return path;
        }
    }
}
