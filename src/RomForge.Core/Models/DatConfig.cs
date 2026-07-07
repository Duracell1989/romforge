using System.Collections.Generic;

namespace RomForge.Core.Models;

public sealed record DatConfig
{
    public string? RomFolderPath { get; init; }
    public List<LanguageBit> LanguageBits { get; init; } = [];
}
