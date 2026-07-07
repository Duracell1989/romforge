namespace RomForge.Core.Models;

public sealed record AppPreferences
{
    public string? LastActiveDatName { get; init; }
    public string DefaultArchiveFormat { get; init; } = "7z";
    public string? UnverifiedFolder { get; init; }
}
