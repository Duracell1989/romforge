using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Models;

namespace RomForge.Core.IO
{
    public interface IDatReader
    {
        Task<Result<DatFile>> ReadAsync(CancellationToken cancellationToken = default);
    }
}
