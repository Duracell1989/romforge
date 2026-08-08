using System;
using System.IO;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Services;

namespace RomForge.Core.UnitTests.Services
{
    [TestOf(typeof(AppDataService))]
    public sealed class AppDataServiceTests
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
        public void Constructor_CreatesAllSubdirectories()
        {
            AppDataService svc = new AppDataService(_tempDir);

            Directory.Exists(svc.DatsPath).Should().BeTrue();
            Directory.Exists(svc.ImgsPath).Should().BeTrue();
            Directory.Exists(svc.ConfigPath).Should().BeTrue();
            Directory.Exists(svc.CachesPath).Should().BeTrue();
            Directory.Exists(svc.TempPath).Should().BeTrue();
            Directory.Exists(svc.RecoveredPath).Should().BeTrue();
        }

        [Test]
        public void Constructor_CleansExistingTempFiles()
        {
            AppDataService first = new AppDataService(_tempDir);
            string orphan = Path.Combine(first.TempPath, "leftover.tmp");
            File.WriteAllText(orphan, "stale");

            _ = new AppDataService(_tempDir);

            File.Exists(orphan).Should().BeFalse();
        }

        [Test]
        public void Constructor_DoesNotSweepRecoveredFiles()
        {
            // Unlike TempPath, RecoveredPath holds working archives a user still needs to manually
            // rescue after a failed placement — CleanTemp() must never touch it, otherwise a "kept"
            // recovery message becomes a lie the moment the app is relaunched.
            AppDataService first = new AppDataService(_tempDir);
            string recovered = Path.Combine(first.RecoveredPath, "rearchive-leftover.7z");
            File.WriteAllText(recovered, "kept");

            _ = new AppDataService(_tempDir);

            File.Exists(recovered).Should().BeTrue();
        }

        [Test]
        public void GetScanCachePath_SameInput_ReturnsSamePath()
        {
            AppDataService svc = new AppDataService(_tempDir);

            string path1 = svc.GetScanCachePath("/roms/GBA");
            string path2 = svc.GetScanCachePath("/roms/GBA");

            path1.Should().Be(path2);
        }

        [Test]
        public void GetScanCachePath_DifferentFolders_ReturnDifferentPaths()
        {
            AppDataService svc = new AppDataService(_tempDir);

            string path1 = svc.GetScanCachePath("/roms/GBA");
            string path2 = svc.GetScanCachePath("/roms/NDS");

            path1.Should().NotBe(path2);
        }

        [Test]
        public void GetScanCachePath_ReturnsJsonFileInCachesDir()
        {
            AppDataService svc = new AppDataService(_tempDir);

            string path = svc.GetScanCachePath("/roms/GBA");

            Path.GetDirectoryName(path).Should().Be(svc.CachesPath);
            Path.GetExtension(path).Should().Be(".json");
        }

        [Test]
        public void GetScanCachePath_FilenameIsEightHexChars()
        {
            AppDataService svc = new AppDataService(_tempDir);

            string name = Path.GetFileNameWithoutExtension(svc.GetScanCachePath("/roms/GBA"));

            name.Should().HaveLength(8);
            name.Should().MatchRegex("^[0-9A-F]{8}$");
        }

        [Test]
        public void GetImportedDatPaths_EmptyDatsDir_ReturnsEmpty()
        {
            AppDataService svc = new AppDataService(_tempDir);

            svc.GetImportedDatPaths().Should().BeEmpty();
        }

        [Test]
        public void GetImportedDatPaths_IncludesZipAndXml_ExcludesOtherExtensions()
        {
            AppDataService svc = new AppDataService(_tempDir);
            File.WriteAllText(Path.Combine(svc.DatsPath, "a.zip"), string.Empty);
            File.WriteAllText(Path.Combine(svc.DatsPath, "b.xml"), string.Empty);
            File.WriteAllText(Path.Combine(svc.DatsPath, "c.txt"), string.Empty);

            var paths = svc.GetImportedDatPaths();

            paths.Should().HaveCount(2);
            paths.Should().Contain(p => p.EndsWith("a.zip"));
            paths.Should().Contain(p => p.EndsWith("b.xml"));
        }

        [Test]
        public void GetImportedDatPaths_ReturnsSortedAlphabetically()
        {
            AppDataService svc = new AppDataService(_tempDir);
            File.WriteAllText(Path.Combine(svc.DatsPath, "z.zip"), string.Empty);
            File.WriteAllText(Path.Combine(svc.DatsPath, "a.xml"), string.Empty);
            File.WriteAllText(Path.Combine(svc.DatsPath, "m.zip"), string.Empty);

            var paths = svc.GetImportedDatPaths();

            paths[0].Should().EndWith("a.xml");
            paths[1].Should().EndWith("m.zip");
            paths[2].Should().EndWith("z.zip");
        }

        [Test]
        public void StatusDbPath_IsInsideRootPath()
        {
            AppDataService svc = new AppDataService(_tempDir);

            svc.StatusDbPath.Should().StartWith(svc.RootPath);
            svc.StatusDbPath.Should().EndWith(".db");
        }
    }
}
