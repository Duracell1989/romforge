using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO;

public interface IArchiveCompressor
{
    bool IsAvailable { get; }

    Task<Result> CompressAsync(
        string sourceFile,
        string destArchive,
        long romSize,
        IProgress<int>? progress = null,
        string format = "7z",
        CancellationToken cancellationToken = default
    );
}
