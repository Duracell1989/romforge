using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IO;

namespace RomForge.Core.UnitTests.IO
{
    [TestOf(typeof(JsonRomScanCache))]
    public sealed class JsonRomScanCacheTests
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
        public void GetCrc_NoEntry_ReturnsNull()
        {
            JsonRomScanCache cache = new JsonRomScanCache(CachePath());

            uint? result = cache.GetCrc("/roms/game.gba", 100L, Utc(2024, 1, 1));

            result.Should().BeNull();
        }

        [Test]
        public void GetCrc_SizeChanged_ReturnsNull()
        {
            JsonRomScanCache cache = new JsonRomScanCache(CachePath());
            cache.Set("/roms/game.gba", 100L, Utc(2024, 1, 1), 0xDEADBEEF);

            uint? result = cache.GetCrc("/roms/game.gba", 999L, Utc(2024, 1, 1));

            result.Should().BeNull();
        }

        [Test]
        public void GetCrc_LastModifiedChanged_ReturnsNull()
        {
            JsonRomScanCache cache = new JsonRomScanCache(CachePath());
            cache.Set("/roms/game.gba", 100L, Utc(2024, 1, 1), 0xDEADBEEF);

            uint? result = cache.GetCrc("/roms/game.gba", 100L, Utc(2025, 6, 1));

            result.Should().BeNull();
        }

        [Test]
        public void GetCrc_ExactMatch_ReturnsCrc()
        {
            JsonRomScanCache cache = new JsonRomScanCache(CachePath());
            cache.Set("/roms/game.gba", 100L, Utc(2024, 1, 1), 0xDEADBEEF);

            uint? result = cache.GetCrc("/roms/game.gba", 100L, Utc(2024, 1, 1));

            result.Should().Be(0xDEADBEEF);
        }

        [Test]
        public async Task SaveAsync_PersistsEntries_RestoredOnNextConstruction()
        {
            string path = CachePath();
            JsonRomScanCache cache = new JsonRomScanCache(path);
            cache.Set("/roms/a.gba", 100L, Utc(2024, 1, 1), 0xAABBCCDD);
            cache.Set("/roms/b.nds", 200L, Utc(2024, 6, 1), 0x11223344);

            await cache.SaveAsync();

            JsonRomScanCache reloaded = new JsonRomScanCache(path);
            reloaded.GetCrc("/roms/a.gba", 100L, Utc(2024, 1, 1)).Should().Be(0xAABBCCDD);
            reloaded.GetCrc("/roms/b.nds", 200L, Utc(2024, 6, 1)).Should().Be(0x11223344);
        }

        [Test]
        public void Construction_CorruptFile_StartsEmpty()
        {
            string path = CachePath();
            File.WriteAllText(path, "not valid json {{{{");

            JsonRomScanCache cache = new JsonRomScanCache(path);

            cache.GetCrc("/roms/game.gba", 100L, Utc(2024, 1, 1)).Should().BeNull();
        }

        [Test]
        public async Task SaveAsync_RealFileTickPrecisionMtime_RoundTripsAndHits()
        {
            string romPath = Path.Combine(_tempDir, "game.gba");
            await File.WriteAllBytesAsync(romPath, [0x01, 0x02, 0x03, 0x04]);
            FileInfo info = new FileInfo(romPath);
            long size = info.Length;
            DateTime mtime = info.LastWriteTimeUtc;

            string path = CachePath();
            JsonRomScanCache cache = new JsonRomScanCache(path);
            cache.Set(romPath, size, mtime, 0xCAFEF00D);
            await cache.SaveAsync();

            JsonRomScanCache reloaded = new JsonRomScanCache(path);
            DateTime reread = new FileInfo(romPath).LastWriteTimeUtc;
            reloaded.GetCrc(romPath, size, reread).Should().Be(0xCAFEF00D);
        }

        private string CachePath() => Path.Combine(_tempDir, "cache.json");

        private static DateTime Utc(int year, int month, int day) =>
            new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }
}
