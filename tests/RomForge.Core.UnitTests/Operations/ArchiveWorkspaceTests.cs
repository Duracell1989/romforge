using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Operations;

namespace RomForge.Core.UnitTests.Operations;

[TestOf(typeof(ArchiveWorkspace))]
public sealed class ArchiveWorkspaceTests
{
    [Test]
    public void BuildEntryName_WithRomExtension_AppendsIt()
    {
        string result = ArchiveWorkspace.BuildEntryName("/roms/0001 - Mario.7z", "gba");

        result.Should().Be("0001 - Mario.gba");
    }

    [Test]
    public void BuildEntryName_WithEmptyRomExtension_ReturnsStemUnchanged()
    {
        // A DAT entry can legitimately declare no extension for its ROM. The entry name must
        // not fabricate one — this guards against deriving the extension from the extracted
        // temp file's own name instead, which always carries a random extension of its own
        // (Path.GetRandomFileName()) and would corrupt the result for exactly this case.
        string result = ArchiveWorkspace.BuildEntryName("/roms/0001 - Mario.7z", string.Empty);

        result.Should().Be("0001 - Mario");
    }
}
