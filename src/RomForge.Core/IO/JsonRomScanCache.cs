using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RomForge.Core.IO;

public sealed class JsonRomScanCache : IRomScanCache
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public JsonRomScanCache(string filePath)
    {
        _filePath = filePath;
        _entries = new ConcurrentDictionary<string, CacheEntry>(Load(filePath));
    }

    public uint? GetCrc(string filePath, long fileSize, DateTime lastModified)
    {
        if (!_entries.TryGetValue(filePath, out CacheEntry? entry))
            return null;
        if (entry.Size != fileSize || entry.LastModified != lastModified)
            return null;
        return entry.Crc;
    }

    public uint? GetTrimmedCrc(string filePath, long fileSize, DateTime lastModified)
    {
        if (!_entries.TryGetValue(filePath, out CacheEntry? entry))
            return null;
        if (entry.Size != fileSize || entry.LastModified != lastModified)
            return null;
        return entry.TrimmedCrc;
    }

    public void Set(
        string filePath,
        long fileSize,
        DateTime lastModified,
        uint crc,
        uint? trimmedCrc = null
    )
    {
        _entries[filePath] = new CacheEntry
        {
            Size = fileSize,
            LastModified = lastModified,
            Crc = crc,
            TrimmedCrc = trimmedCrc,
        };
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var fs = File.Create(_filePath);
        await JsonSerializer
            .SerializeAsync(fs, _entries, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, CacheEntry> Load(string filePath)
    {
        if (!File.Exists(filePath))
            return new Dictionary<string, CacheEntry>();

        try
        {
            using var fs = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(fs, JsonOptions)
                ?? new Dictionary<string, CacheEntry>();
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, CacheEntry>();
        }
    }

    private sealed record CacheEntry
    {
        public long Size { get; init; }
        public DateTime LastModified { get; init; }
        public uint Crc { get; init; }
        public uint? TrimmedCrc { get; init; }
    }
}
