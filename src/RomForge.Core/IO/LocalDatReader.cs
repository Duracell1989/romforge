using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Models;
using RomForge.Core.Parsers;
using SharpCompress.Archives;

namespace RomForge.Core.IO;

public sealed class LocalDatReader : IDatReader
{
    private readonly string _filePath;

    public LocalDatReader(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<Result<DatFile>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return Result.Fail($"DAT file not found: {_filePath}");

        try
        {
            var xmlStream = IsZip(_filePath)
                ? await ExtractXmlFromZipAsync(_filePath, cancellationToken).ConfigureAwait(false)
                : File.OpenRead(_filePath);

            await using (xmlStream)
            {
                var datFile = await DatParser
                    .ParseAsync(xmlStream, cancellationToken)
                    .ConfigureAwait(false);
                return Result.Ok(datFile);
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Result.Fail(new ExceptionalError(ex));
        }
    }

    private static bool IsZip(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var fs = File.OpenRead(path);
        var read = fs.Read(header);
        // PK signature: 0x50 0x4B 0x03 0x04
        return read >= 4
            && header[0] == 0x50
            && header[1] == 0x4B
            && header[2] == 0x03
            && header[3] == 0x04;
    }

    private static async Task<Stream> ExtractXmlFromZipAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        using IArchive archive = ArchiveFactory.OpenArchive(path);
        IArchiveEntry? entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory);
        if (entry is null)
            throw new InvalidDataException($"ZIP archive contains no entries: {path}");

        MemoryStream ms = new();
        await using Stream entryStream = await entry
            .OpenEntryStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await entryStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
}
