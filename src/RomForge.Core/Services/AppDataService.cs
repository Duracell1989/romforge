using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;

namespace RomForge.Core.Services;

/// <summary>
/// Manages the application's owned data directory under LocalApplicationData/RomForge/.
/// </summary>
public sealed class AppDataService
{
    public string RootPath { get; }
    public string DatsPath { get; }
    public string ImgsPath { get; }
    public string ConfigPath { get; }
    public string CachesPath { get; }
    public string TempPath { get; }
    public string StatusDbPath { get; }

    public AppDataService()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RomForge"
            )
        ) { }

    internal AppDataService(string rootPath)
    {
        RootPath = rootPath;
        DatsPath = Path.Combine(RootPath, "dats");
        ImgsPath = Path.Combine(RootPath, "imgs");
        ConfigPath = Path.Combine(RootPath, "config");
        CachesPath = Path.Combine(RootPath, "caches");
        TempPath = Path.Combine(RootPath, "temp");
        StatusDbPath = Path.Combine(RootPath, "status.db");

        Directory.CreateDirectory(DatsPath);
        Directory.CreateDirectory(ImgsPath);
        Directory.CreateDirectory(ConfigPath);
        Directory.CreateDirectory(CachesPath);
        Directory.CreateDirectory(TempPath);

        CleanTemp();
    }

    /// <summary>
    /// Returns the path to the scan cache file for a given ROM folder.
    /// The filename is derived from a CRC32 hash of the folder path so it is
    /// stable and unique per folder without encoding the full path on disk.
    /// </summary>
    public string GetScanCachePath(string romFolderPath)
    {
        uint hash = Crc32.HashToUInt32(Encoding.UTF8.GetBytes(romFolderPath));
        return Path.Combine(CachesPath, $"{hash:X8}.json");
    }

    public IReadOnlyList<string> GetImportedDatPaths() =>
        Directory
            .GetFiles(DatsPath, "*.zip")
            .Concat(Directory.GetFiles(DatsPath, "*.xml"))
            .OrderBy(p => p)
            .ToList<string>();

    private void CleanTemp()
    {
        foreach (string file in Directory.GetFiles(TempPath))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // orphan from a previous crash — skip
            }
        }
    }
}
