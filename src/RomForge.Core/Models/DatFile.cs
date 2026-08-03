using System.Collections.Generic;

namespace RomForge.Core.Models
{
    public sealed record DatFile
    {
        public DatHeader Header { get; init; } = new();
        public IReadOnlyList<Game> Games { get; init; } = [];
    }
}
