using System;
using System.IO;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IO;

namespace RomForge.Core.UnitTests.IO;

[TestOf(typeof(OfflineListConfigReader))]
public sealed class OfflineListConfigReaderTests
{
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
    public void Read_MissingFile_ReturnsFailed()
    {
        string result = IniPath("nonexistent.ini");

        OfflineListConfigReader.Read(result).IsFailed.Should().BeTrue();
    }

    [Test]
    public void Read_EmptyIni_ReturnsEmptyLanguageBits()
    {
        WriteIni("[Option]\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.IsSuccess.Should().BeTrue();
        result.Value.LanguageBits.Should().BeEmpty();
    }

    [Test]
    public void Read_SingleLanguageBit_ReturnsBitWithCorrectIndex()
    {
        WriteIni("[Option]\nl3=\"DE\"\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.IsSuccess.Should().BeTrue();
        result.Value.LanguageBits.Should().HaveCount(1);
        result.Value.LanguageBits[0].BitIndex.Should().Be(3);
        result.Value.LanguageBits[0].Label.Should().Be("DE");
    }

    [Test]
    public void Read_MultipleLanguageBits_ReturnsBitsInOrder()
    {
        WriteIni("[Option]\nl1=\"En\"\nl8=\"JP\"\nl2=\"Fr\"\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.IsSuccess.Should().BeTrue();
        result.Value.LanguageBits.Should().HaveCount(3);
        result.Value.LanguageBits[0].BitIndex.Should().Be(1);
        result.Value.LanguageBits[1].BitIndex.Should().Be(2);
        result.Value.LanguageBits[2].BitIndex.Should().Be(8);
    }

    [Test]
    public void Read_LanguageBitIndex_IsOneIndexed()
    {
        // l8 = JP is confirmed from F-Zero JP DAT (language=256 = 2^8)
        WriteIni("[Option]\nl8=\"JP\"\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.Value.LanguageBits[0].BitIndex.Should().Be(8);
    }

    [Test]
    public void Read_IgnoresDNKeys_NotParsedAsLanguageBits()
    {
        WriteIni("[Option]\nd1=1\nd2=2\nl1=\"En\"\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.Value.LanguageBits.Should().HaveCount(1);
        result.Value.LanguageBits[0].Label.Should().Be("En");
    }

    [Test]
    public void Read_StripsQuotesAndWhitespace_FromLabels()
    {
        WriteIni("[Option]\nl1=\" English \"\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.Value.LanguageBits[0].Label.Should().Be("English");
    }

    [Test]
    public void Read_ParsesRomFolder_WhenPresent()
    {
        WriteIni("[Option]\nRomFolder=C:\\ROMs\\GBA\\\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.Value.RomFolderPath.Should().Be("C:\\ROMs\\GBA\\");
    }

    [Test]
    public void Read_IgnoresKeysOutsideOptionSection()
    {
        WriteIni("[Other]\nl1=\"En\"\n[Option]\nl2=\"Fr\"\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.Value.LanguageBits.Should().HaveCount(1);
        result.Value.LanguageBits[0].BitIndex.Should().Be(2);
    }

    [Test]
    public void Read_EmptyLabelValue_SkipsEntry()
    {
        WriteIni("[Option]\nl1=\"\"\nl2=\"Fr\"\n");

        var result = OfflineListConfigReader.Read(IniPath());

        result.Value.LanguageBits.Should().HaveCount(1);
        result.Value.LanguageBits[0].BitIndex.Should().Be(2);
    }

    private static void WriteIni(string path, string content) => File.WriteAllText(path, content);

    private void WriteIni(string content) => WriteIni(IniPath(), content);

    private string IniPath(string name = "test.ini") => Path.Combine(_tempDir, name);
}
