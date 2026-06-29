using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Scanning;

namespace RomForge.Core.UnitTests.Scanning;

[TestOf(typeof(RomScanner))]
public class RomScannerTests
{
    [Test]
    public async Task ScanAsync_SingleFile_ReturnsCrcAndExtension()
    {
        byte[] content = [0xAA, 0xBB, 0xCC, 0xDD];
        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(content)),
            },
        ]);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(source, "/roms");

        results.Should().HaveCount(1);
        ScannedRom rom = results[0];
        rom.FilePath.Should().Be("/roms/game.gba");
        rom.FileExtension.Should().Be("gba");
        rom.RomExtension.Should().Be("gba");
        rom.Crc.Should().Be(Crc32Of(content));
    }

    [Test]
    public async Task ScanAsync_ZippedFile_UsesRomExtensionAndContentCrc()
    {
        byte[] romContent = [0x01, 0x02, 0x03, 0x04];
        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.zip",
                FileExtension = "zip",
                RomExtension = "gba",
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(romContent)),
            },
        ]);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(source, "/roms");

        results.Should().HaveCount(1);
        ScannedRom rom = results[0];
        rom.FileExtension.Should().Be("zip");
        rom.RomExtension.Should().Be("gba");
        rom.Crc.Should().Be(Crc32Of(romContent));
    }

    [Test]
    public async Task ScanAsync_MultipleFiles_ScansAll()
    {
        byte[] gba = [0x11, 0x22];
        byte[] nds = [0x33, 0x44, 0x55];
        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/a.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(gba)),
            },
            new RomContent
            {
                FilePath = "/roms/b.nds",
                FileExtension = "nds",
                RomExtension = "nds",
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(nds)),
            },
        ]);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(source, "/roms");

        results.Should().HaveCount(2);
        results.Select(r => r.RomExtension).Should().BeEquivalentTo("gba", "nds");
        uint[] expectedCrcs = [Crc32Of(gba), Crc32Of(nds)];
        results.Select(r => r.Crc).Should().BeEquivalentTo(expectedCrcs);
    }

    [Test]
    public async Task ScanAsync_EmptySource_ReturnsNoResults()
    {
        IRomSource source = StubSource([]);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(source, "/roms");

        results.Should().BeEmpty();
    }

    [Test]
    public async Task ScanAsync_DisposesStreamAfterReading()
    {
        TrackingStream trackingStream = new([0xDE, 0xAD]);
        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                OpenStreamAsync = _ => new ValueTask<Stream>(trackingStream),
            },
        ]);

        await RomScanner.ScanAsync(source, "/roms");

        trackingStream.WasDisposed.Should().BeTrue();
    }

    [Test]
    public async Task ScanAsync_CacheHit_SkipsStreamOpen()
    {
        bool streamOpened = false;
        long size = 4L;
        DateTime lastModified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        uint expectedCrc = 0xDEADBEEF;

        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                FileSize = size,
                LastModified = lastModified,
                OpenStreamAsync = _ =>
                {
                    streamOpened = true;
                    return new ValueTask<Stream>(new MemoryStream([0x01]));
                },
            },
        ]);

        Mock<IRomScanCache> cacheMock = new Mock<IRomScanCache>();
        cacheMock.Setup(c => c.GetCrc("/roms/game.gba", size, lastModified)).Returns(expectedCrc);
        cacheMock
            .Setup(c => c.GetTrimmedCrc("/roms/game.gba", size, lastModified))
            .Returns((uint?)null);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(
            source, "/roms", cacheMock.Object
        );

        streamOpened.Should().BeFalse();
        results.Should().HaveCount(1);
        results[0].Crc.Should().Be(expectedCrc);
        cacheMock.Verify(
            c => c.Set(
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<DateTime>(),
                It.IsAny<uint>(),
                It.IsAny<uint?>()
            ),
            Times.Never
        );
    }

    [Test]
    public async Task ScanAsync_CacheMiss_ComputesCrcAndPopulatesCache()
    {
        byte[] content = [0xAA, 0xBB, 0xCC, 0xDD];
        long size = 4L;
        DateTime lastModified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                FileSize = size,
                LastModified = lastModified,
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(content)),
            },
        ]);

        Mock<IRomScanCache> cacheMock = new Mock<IRomScanCache>();
        cacheMock
            .Setup(c => c.GetCrc(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTime>()))
            .Returns((uint?)null);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(
            source, "/roms", cacheMock.Object
        );

        uint expectedCrc = Crc32Of(content);
        results[0].Crc.Should().Be(expectedCrc);
        cacheMock.Verify(c => c.Set("/roms/game.gba", size, lastModified, expectedCrc, null), Times.Once);
    }

    [Test]
    public async Task ScanAsync_TrailingZeroFF_EmitsTrimmedCrc()
    {
        byte[] data = [0x01, 0x02, 0x03, 0xFF, 0xFF];
        byte[] trimmedData = [0x01, 0x02, 0x03];
        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                FileSize = data.Length,
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(data)),
            },
        ]);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(source, "/roms");

        results[0].Crc.Should().Be(Crc32Of(data));
        results[0].TrimmedCrc.Should().Be(Crc32Of(trimmedData));
        results[0].TrimmedCrc.Should().NotBe(results[0].Crc);
    }

    [Test]
    public async Task ScanAsync_NoTrailingZeroFF_TrimmedCrcIsNull()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                FileSize = data.Length,
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(data)),
            },
        ]);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(source, "/roms");

        results[0].TrimmedCrc.Should().BeNull();
    }

    [Test]
    public async Task ScanAsync_LargeFile_TrimmedCrcIsNull()
    {
        byte[] content = [0x01, 0x02, 0xFF, 0xFF];
        long largeSize = 256L * 1024 * 1024 + 1;
        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.nds",
                FileExtension = "nds",
                RomExtension = "nds",
                FileSize = largeSize,
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(content)),
            },
        ]);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(source, "/roms");

        results[0].TrimmedCrc.Should().BeNull();
    }

    [Test]
    public async Task ScanAsync_CacheHit_RestoresTrimmedCrcFromCache()
    {
        long size = 8L;
        DateTime lastModified = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        uint cachedCrc = 0x11111111;
        uint cachedTrimmedCrc = 0x22222222;

        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                FileSize = size,
                LastModified = lastModified,
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream()),
            },
        ]);

        Mock<IRomScanCache> cacheMock = new Mock<IRomScanCache>();
        cacheMock.Setup(c => c.GetCrc("/roms/game.gba", size, lastModified)).Returns(cachedCrc);
        cacheMock
            .Setup(c => c.GetTrimmedCrc("/roms/game.gba", size, lastModified))
            .Returns(cachedTrimmedCrc);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(
            source, "/roms", cacheMock.Object
        );

        results[0].Crc.Should().Be(cachedCrc);
        results[0].TrimmedCrc.Should().Be(cachedTrimmedCrc);
    }

    [Test]
    public async Task ScanAsync_CacheMiss_StoresTrimmedCrcInCache()
    {
        byte[] data = [0x01, 0x02, 0x03, 0xFF, 0xFF];
        long size = data.Length;
        DateTime lastModified = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        IRomSource source = StubSource([
            new RomContent
            {
                FilePath = "/roms/game.gba",
                FileExtension = "gba",
                RomExtension = "gba",
                FileSize = size,
                LastModified = lastModified,
                OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(data)),
            },
        ]);

        Mock<IRomScanCache> cacheMock = new Mock<IRomScanCache>();
        cacheMock
            .Setup(c => c.GetCrc(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTime>()))
            .Returns((uint?)null);

        await RomScanner.ScanAsync(source, "/roms", cacheMock.Object);

        uint expectedFullCrc = Crc32Of(data);
        uint expectedTrimmedCrc = Crc32Of([0x01, 0x02, 0x03]);
        cacheMock.Verify(
            c => c.Set("/roms/game.gba", size, lastModified, expectedFullCrc, expectedTrimmedCrc),
            Times.Once
        );
    }

    [Test]
    public async Task ScanAsync_MultipleFiles_PreservesInputOrder()
    {
        byte[] a = [0x01];
        byte[] b = [0x02];
        byte[] c = [0x03];
        byte[] d = [0x04];
        byte[] e = [0x05];

        IRomSource source = StubSource([
            new RomContent { FilePath = "/roms/a.7z", FileExtension = "7z", RomExtension = "gba", OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(a)) },
            new RomContent { FilePath = "/roms/b.7z", FileExtension = "7z", RomExtension = "gba", OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(b)) },
            new RomContent { FilePath = "/roms/c.7z", FileExtension = "7z", RomExtension = "gba", OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(c)) },
            new RomContent { FilePath = "/roms/d.7z", FileExtension = "7z", RomExtension = "gba", OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(d)) },
            new RomContent { FilePath = "/roms/e.7z", FileExtension = "7z", RomExtension = "gba", OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(e)) },
        ]);

        IReadOnlyList<ScannedRom> results = await RomScanner.ScanAsync(source, "/roms");

        results.Select(r => r.FilePath)
            .Should()
            .ContainInOrder("/roms/a.7z", "/roms/b.7z", "/roms/c.7z", "/roms/d.7z", "/roms/e.7z");
        results.Select(r => r.Crc)
            .Should()
            .ContainInOrder(Crc32Of(a), Crc32Of(b), Crc32Of(c), Crc32Of(d), Crc32Of(e));
    }

    [Test]
    public async Task ScanAsync_MultipleFiles_ReportsEnumerationProgressWithPreCountedTotal()
    {
        byte[] content = [0x01, 0x02];
        IRomSource source = StubSource([
            new RomContent { FilePath = "/roms/a.gba", FileExtension = "gba", RomExtension = "gba", OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(content)) },
            new RomContent { FilePath = "/roms/b.gba", FileExtension = "gba", RomExtension = "gba", OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(content)) },
        ]);

        List<ScanProgress> reports = [];
        IProgress<ScanProgress> progress = new SyncProgress<ScanProgress>(p => reports.Add(p));

        await RomScanner.ScanAsync(source, "/roms", progress: progress);

        // Enumeration reports use the pre-counted total (2) from CountAsync, so Total > 0 from the first report
        reports.Should().Contain(p => p.Total == 2 && p.Completed == 1, "first enumeration report must show 1 of pre-counted 2");
        // Processing reports also have Total == 2
        reports.Should().Contain(p => p.Total == 2 && p.Completed == 2, "all files must be reported processed");
    }

    [Test]
    public async Task ScanAsync_PartialCacheHits_CrcProgressTotalReflectsMissCountOnly()
    {
        byte[] content = [0x01, 0x02];
        long size = content.Length;
        DateTime lastModified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        uint cachedCrc = 0xDEADBEEF;

        IRomSource source = StubSource([
            new RomContent { FilePath = "/roms/a.gba", FileExtension = "gba", RomExtension = "gba", FileSize = size, LastModified = lastModified, OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(content)) },
            new RomContent { FilePath = "/roms/b.gba", FileExtension = "gba", RomExtension = "gba", FileSize = size, LastModified = lastModified, OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(content)) },
            new RomContent { FilePath = "/roms/c.gba", FileExtension = "gba", RomExtension = "gba", FileSize = size, LastModified = lastModified, OpenStreamAsync = _ => new ValueTask<Stream>(new MemoryStream(content)) },
        ]);

        Mock<IRomScanCache> cacheMock = new Mock<IRomScanCache>();
        cacheMock.Setup(c => c.GetCrc("/roms/a.gba", size, lastModified)).Returns(cachedCrc);
        cacheMock.Setup(c => c.GetCrc("/roms/b.gba", size, lastModified)).Returns(cachedCrc);
        cacheMock.Setup(c => c.GetCrc("/roms/c.gba", size, lastModified)).Returns((uint?)null);
        cacheMock.Setup(c => c.GetTrimmedCrc(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTime>())).Returns((uint?)null);

        List<ScanProgress> reports = [];
        IProgress<ScanProgress> progress = new SyncProgress<ScanProgress>(p => reports.Add(p));

        await RomScanner.ScanAsync(source, "/roms", cacheMock.Object, progress);

        List<ScanProgress> crcReports = reports.Where(p => p.Phase == "Computing CRCs...").ToList();
        crcReports.Should().NotBeEmpty();
        crcReports.Should().AllSatisfy(p => p.Total.Should().Be(1), "only 1 of 3 files is a cache miss");
        crcReports.Should().Contain(p => p.Completed == 1, "the single miss must be reported as completed");
    }

    [Test]
    public void ComputeCrcs_TrailingZeroFF_ReturnsDifferentTrimmedCrc()
    {
        byte[] data = [0xDE, 0xAD, 0xBE, 0xFF, 0xFF];
        byte[] trimmed = [0xDE, 0xAD, 0xBE];

        (uint fullCrc, uint? trimmedCrc) = RomScanner.ComputeCrcs(data);

        fullCrc.Should().Be(Crc32Of(data));
        trimmedCrc.Should().Be(Crc32Of(trimmed));
        trimmedCrc.Should().NotBe(fullCrc);
    }

    [Test]
    public void ComputeCrcs_NoTrailingZeroFF_TrimmedCrcIsNull()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];

        (_, uint? trimmedCrc) = RomScanner.ComputeCrcs(data);

        trimmedCrc.Should().BeNull();
    }

    [Test]
    public void ComputeCrcs_AllZeroFF_TrimmedCrcIsNull()
    {
        byte[] data = [0xFF, 0xFF, 0xFF];

        (_, uint? trimmedCrc) = RomScanner.ComputeCrcs(data);

        trimmedCrc.Should().BeNull();
    }

    private static IRomSource StubSource(IReadOnlyList<RomContent> items) =>
        new StubRomSource(items);

    private sealed class StubRomSource : IRomSource
    {
        private readonly IReadOnlyList<RomContent> _items;

        public StubRomSource(IReadOnlyList<RomContent> items)
        {
            _items = items;
        }

        public Task<int> CountAsync(string folderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Count);

        public async IAsyncEnumerable<RomContent> EnumerateAsync(
            string folderPath,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            foreach (RomContent item in _items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.CompletedTask;
            }
        }
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        internal SyncProgress(Action<T> callback) => _callback = callback;
        public void Report(T value) => _callback(value);
    }

    private sealed class TrackingStream : MemoryStream
    {
        public TrackingStream(byte[] data)
            : base(data) { }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private static uint Crc32Of(ReadOnlySpan<byte> data)
    {
        Crc32 hasher = new();
        hasher.Append(data);
        return hasher.GetCurrentHashAsUInt32();
    }
}
