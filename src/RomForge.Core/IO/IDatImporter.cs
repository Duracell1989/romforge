using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Models;

namespace RomForge.Core.IO
{
    public readonly record struct ImportProgress(int Current, int Total, string CurrentFile);

    public interface IDatImporter
    {
        Task<Result<string>> ImportAsync(string sourceDatPath, DatHeader header, IProgress<ImportProgress>? progress, CancellationToken ct);
    }
}
