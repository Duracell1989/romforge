using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomForge.Core.IO;

namespace RomForge.Core.Scanning;

public static class RomScanner
{
    private const long TrimDetectionThresholdBytes = 256L * 1024 * 1024;

    public static async Task<IReadOnlyList<ScannedRom>> ScanAsync(
        IRomSource source,
        string folderPath,
        IRomScanCache? cache = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        int estimatedTotal = await source.CountAsync(folderPath, cancellationToken).ConfigureAwait(false);
        int found = 0;

        List<RomContent> contents = new();
        await foreach (RomContent c in source.EnumerateAsync(folderPath, cancellationToken))
        {
            contents.Add(c);
            found++;
            progress?.Report(new ScanProgress(found, estimatedTotal, Path.GetFileName(c.FilePath)));
        }

        if (contents.Count == 0)
            return [];

        int total = contents.Count;
        int completed = 0;
        ScannedRom[] results = new ScannedRom[total];

        await Parallel.ForEachAsync(
            contents.Select((c, i) => (Content: c, Index: i)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8),
                CancellationToken = cancellationToken,
            },
            async (item, ct) =>
            {
                results[item.Index] = await ProcessContentAsync(item.Content, cache, ct).ConfigureAwait(false);
                int c = Interlocked.Increment(ref completed);
                progress?.Report(new ScanProgress(c, total, Path.GetFileName(item.Content.FilePath)));
            }
        );

        return results;
    }

    private static async Task<ScannedRom> ProcessContentAsync(
        RomContent content,
        IRomScanCache? cache,
        CancellationToken cancellationToken
    )
    {
        long? fileSize = content.FileSize;
        DateTime? lastModified = content.LastModified;
        bool hasMeta = fileSize.HasValue && lastModified.HasValue;

        uint crc;
        uint? trimmedCrc;

        if (
            cache is not null
            && hasMeta
            && cache.GetCrc(content.FilePath, fileSize!.Value, lastModified!.Value) is { } cachedCrc
        )
        {
            crc = cachedCrc;
            trimmedCrc = cache.GetTrimmedCrc(content.FilePath, fileSize!.Value, lastModified!.Value);
        }
        else
        {
            Stream stream = await content.OpenStreamAsync(cancellationToken).ConfigureAwait(false);

            if (fileSize.HasValue && fileSize.Value <= TrimDetectionThresholdBytes)
                (crc, trimmedCrc) = await ComputeCrcsBufferedAsync(stream, cancellationToken).ConfigureAwait(false);
            else
            {
                crc = await ComputeCrc32StreamedAsync(stream, cancellationToken).ConfigureAwait(false);
                trimmedCrc = null;
            }

            if (cache is not null && hasMeta)
                cache.Set(content.FilePath, fileSize!.Value, lastModified!.Value, crc, trimmedCrc);
        }

        return new ScannedRom
        {
            FilePath = content.FilePath,
            FileExtension = content.FileExtension,
            RomExtension = content.RomExtension,
            Crc = crc,
            TrimmedCrc = trimmedCrc,
        };
    }

    internal static (uint FullCrc, uint? TrimmedCrc) ComputeCrcs(byte[] bytes)
    {
        Crc32 hasher = new Crc32();
        hasher.Append(bytes);
        uint fullCrc = hasher.GetCurrentHashAsUInt32();

        int trimEnd = bytes.Length - 1;
        while (trimEnd >= 0 && bytes[trimEnd] == 0xFF)
            trimEnd--;

        if (trimEnd < 0 || trimEnd == bytes.Length - 1)
            return (fullCrc, null);

        Crc32 trimHasher = new Crc32();
        trimHasher.Append(bytes.AsSpan(0, trimEnd + 1));
        return (fullCrc, trimHasher.GetCurrentHashAsUInt32());
    }

    private static async Task<(uint FullCrc, uint? TrimmedCrc)> ComputeCrcsBufferedAsync(
        Stream stream,
        CancellationToken ct
    )
    {
        await using (stream)
        {
            using MemoryStream ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            return ComputeCrcs(ms.ToArray());
        }
    }

    private static async Task<uint> ComputeCrc32StreamedAsync(Stream stream, CancellationToken ct)
    {
        await using (stream)
        {
            Crc32 hasher = new Crc32();
            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                hasher.Append(buffer.AsSpan(0, bytesRead));
            return hasher.GetCurrentHashAsUInt32();
        }
    }
}
