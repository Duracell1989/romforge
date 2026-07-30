using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RomForge.Core.IO;

public sealed record RomContent
{
    public string FilePath { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public string RomExtension { get; init; } = string.Empty;
    public string EntryName { get; init; } = string.Empty;
    public long? FileSize { get; init; }
    public DateTime? LastModified { get; init; }
    public required Func<CancellationToken, ValueTask<Stream>> OpenStreamAsync { get; init; }
}
