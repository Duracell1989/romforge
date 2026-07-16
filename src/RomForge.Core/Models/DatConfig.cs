using System.Collections.Generic;

namespace RomForge.Core.Models;

public sealed record DatConfig
{
    public string? RomFolderPath { get; init; }
    public IReadOnlyList<LanguageBit> LanguageBits { get; init; } = [];
}
