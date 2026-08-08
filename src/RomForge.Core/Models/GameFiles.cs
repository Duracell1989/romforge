namespace RomForge.Core.Models
{
    public sealed record GameFiles
    {
        public uint RomCrc { get; init; }
        public string RomExtension { get; init; } = string.Empty;
    }
}
