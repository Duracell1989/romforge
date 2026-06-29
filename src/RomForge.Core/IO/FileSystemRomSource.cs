using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace RomForge.Core.IO;

public sealed class FileSystemRomSource : IRomSource
{
    private static readonly HashSet<string> ArchiveExtensions = [".zip", ".7z"];

    public Task<int> CountAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            EnumerationOptions enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            };
            int count = 0;
            foreach (string f in Directory.EnumerateFiles(folderPath, "*", enumOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(Path.GetExtension(f)))
                    count++;
            }
            return count;
        }, cancellationToken);
    }

    public async IAsyncEnumerable<RomContent> EnumerateAsync(
        string folderPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        EnumerationOptions enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };
        foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", enumOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileExt = Path.GetExtension(filePath).ToLowerInvariant();
            if (string.IsNullOrEmpty(fileExt))
                continue;

            var fileInfo = new FileInfo(filePath);
            var content = ArchiveExtensions.Contains(fileExt)
                ? await Task.Run(() => BuildArchiveContent(filePath, fileExt, fileInfo), cancellationToken)
                : BuildRawContent(filePath, fileExt, fileInfo);

            if (content is not null)
                yield return content;
        }
    }

    private static RomContent BuildRawContent(string filePath, string fileExt, FileInfo fileInfo)
    {
        var ext = fileExt.TrimStart('.');
        return new RomContent
        {
            FilePath = filePath,
            FileExtension = ext,
            RomExtension = ext,
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            OpenStreamAsync = ct => new ValueTask<Stream>(File.OpenRead(filePath)),
        };
    }

    private static RomContent? BuildArchiveContent(
        string filePath,
        string fileExt,
        FileInfo fileInfo
    )
    {
        // Open briefly to read the entry name, then close; re-open when the stream is requested.
        string romExt;
        using (var probe = ArchiveFactory.OpenArchive(filePath))
        {
            var entry = probe.Entries.FirstOrDefault(e => !e.IsDirectory);
            if (entry is null)
                return null;

            romExt = Path.GetExtension(entry.Key ?? string.Empty).TrimStart('.').ToLowerInvariant();
        }

        return new RomContent
        {
            FilePath = filePath,
            FileExtension = fileExt.TrimStart('.'),
            RomExtension = romExt,
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            OpenStreamAsync = ct => OpenArchiveEntryStreamAsync(filePath, ct),
        };
    }

    private static async ValueTask<Stream> OpenArchiveEntryStreamAsync(
        string filePath,
        CancellationToken ct
    )
    {
        var archive = ArchiveFactory.OpenArchive(filePath);
        try
        {
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory);
            if (entry is null)
            {
                archive.Dispose();
                return Stream.Null;
            }

            var entryStream = await entry.OpenEntryStreamAsync(ct).ConfigureAwait(false);
            return new ArchiveOwningStream(archive, entryStream);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    // Wraps an archive entry stream and disposes the owning IArchive when closed.
    private sealed class ArchiveOwningStream : Stream
    {
        private readonly IArchive _archive;
        private readonly Stream _inner;

        public ArchiveOwningStream(IArchive archive, Stream inner)
        {
            _archive = archive;
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _archive.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _archive.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
