using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO
{
    public interface IArchiveCompressor
    {
        bool IsAvailable { get; }

        /// <summary>
        /// Estimates the peak working-set memory (in bytes) a single compression of a ROM of the
        /// given size in the given format will hold. Used to bound how many re-archives may run
        /// concurrently without exhausting physical memory.
        /// </summary>
        long EstimateWorkingSetBytes(long romSize, string format);

        Task<Result> CompressAsync(
            string sourceFile,
            string destArchive,
            string entryName,
            long romSize,
            IProgress<int>? progress = null,
            string format = "7z",
            CancellationToken cancellationToken = default
        );
    }
}
