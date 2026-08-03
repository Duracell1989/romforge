namespace RomForge.Core.Models
{
    public sealed record Game
    {
        public int ImageNumber { get; init; }
        public int ReleaseNumber { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? SaveType { get; init; }
        public long RomSize { get; init; }
        public string? Publisher { get; init; }
        public int Location { get; init; }
        public int Language { get; init; }
        public string? SourceRom { get; init; }
        public string? Comment { get; init; }
        public int DuplicateId { get; init; }
        public GameFiles Files { get; init; } = new();
        public uint? Im1Crc { get; init; }
        public uint? Im2Crc { get; init; }
    }
}
