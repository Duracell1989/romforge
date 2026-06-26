using System;

namespace RomForge.Core.Scanning;

public sealed record ScannedRom
{
    public string FilePath { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public string RomExtension { get; init; } = string.Empty;
    public uint Crc { get; init; }
    public uint? TrimmedCrc { get; init; }
    public DateTime? LastModified { get; init; }
}
