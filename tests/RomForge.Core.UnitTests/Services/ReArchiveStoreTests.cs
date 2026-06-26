using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.UnitTests.Services;

[TestOf(typeof(ReArchiveStore))]
public sealed class ReArchiveStoreTests
{
    private string _tempDir = string.Empty;
    private AppDataService _appData = null!;
    private ReArchiveStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _appData = new AppDataService(_tempDir);
        _store = new ReArchiveStore(_appData, new LoggerConfiguration().CreateLogger());
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    [Test]
    public async Task InitializeAsync_Succeeds_WithoutError()
    {
        await _store.Invoking(s => s.InitializeAsync()).Should().NotThrowAsync();
    }

    [Test]
    public async Task GetReArchivedReleasesAsync_BeforeInit_ReturnsEmpty()
    {
        HashSet<int> result = await _store.GetReArchivedReleasesAsync("TestDat");

        result.Should().BeEmpty();
    }

    [Test]
    public async Task GetReArchivedReleasesAsync_AfterInit_NoData_ReturnsEmpty()
    {
        await _store.InitializeAsync();

        HashSet<int> result = await _store.GetReArchivedReleasesAsync("TestDat");

        result.Should().BeEmpty();
    }

    [Test]
    public async Task MarkAndGet_RoundTrips()
    {
        await _store.InitializeAsync();

        await _store.MarkAsync("TestDat", 5);
        HashSet<int> result = await _store.GetReArchivedReleasesAsync("TestDat");

        result.Should().Contain(5);
    }

    [Test]
    public async Task MarkAsync_DuplicateKey_InsertOrReplace_ReturnsSingleEntry()
    {
        await _store.InitializeAsync();

        await _store.MarkAsync("TestDat", 5);
        await _store.MarkAsync("TestDat", 5);
        HashSet<int> result = await _store.GetReArchivedReleasesAsync("TestDat");

        result.Should().HaveCount(1);
        result.Should().Contain(5);
    }

    [Test]
    public async Task GetReArchivedReleasesAsync_MultipleDats_IsolatedPerDat()
    {
        await _store.InitializeAsync();

        await _store.MarkAsync("DatA", 1);
        await _store.MarkAsync("DatB", 2);

        HashSet<int> resultA = await _store.GetReArchivedReleasesAsync("DatA");
        HashSet<int> resultB = await _store.GetReArchivedReleasesAsync("DatB");

        resultA.Should().Contain(1);
        resultA.Should().NotContain(2);
        resultB.Should().Contain(2);
        resultB.Should().NotContain(1);
    }

    [Test]
    public async Task MarkAndGet_MultipleReleases_ReturnsAll()
    {
        await _store.InitializeAsync();

        await _store.MarkAsync("TestDat", 1);
        await _store.MarkAsync("TestDat", 7);
        await _store.MarkAsync("TestDat", 42);

        HashSet<int> result = await _store.GetReArchivedReleasesAsync("TestDat");

        result.Should().HaveCount(3);
        result.Should().Contain(1);
        result.Should().Contain(7);
        result.Should().Contain(42);
    }
}
