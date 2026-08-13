using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Scanning;
using RomForge.Core.Services;

namespace RomForge.Core.Operations
{
    /// <summary>
    /// Default <see cref="IRomReArchiveService"/>: extract → recompress to a working archive → place it
    /// over the original → mark re-archived and persist the updated match. Temp files are always cleaned
    /// up, and the working archive is only handed off once placement has taken ownership of it.
    /// </summary>
    public sealed class RomReArchiveService : IRomReArchiveService
    {
        private readonly IArchiveExtractor _extractor;
        private readonly IArchiveCompressor _compressor;
        private readonly IRomFileOperations _fileOperations;
        private readonly ArchiveWorkspace _workspace;
        private readonly ReArchiveStore _reArchiveStore;
        private readonly ScanResultStore _scanResultStore;
        private readonly WorkingSetBudgetGate _memoryGate;

        public RomReArchiveService(
            IArchiveExtractor extractor,
            IArchiveCompressor compressor,
            IRomFileOperations fileOperations,
            ArchiveWorkspace workspace,
            ReArchiveStore reArchiveStore,
            ScanResultStore scanResultStore,
            WorkingSetBudgetGate memoryGate
        )
        {
            ArgumentNullException.ThrowIfNull(extractor);
            ArgumentNullException.ThrowIfNull(compressor);
            ArgumentNullException.ThrowIfNull(fileOperations);
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(reArchiveStore);
            ArgumentNullException.ThrowIfNull(scanResultStore);
            ArgumentNullException.ThrowIfNull(memoryGate);
            _extractor = extractor;
            _compressor = compressor;
            _fileOperations = fileOperations;
            _workspace = workspace;
            _reArchiveStore = reArchiveStore;
            _scanResultStore = scanResultStore;
            _memoryGate = memoryGate;
        }

        public async Task<Result<MatchResult>> ReArchiveAsync(
            MatchResult match,
            (string From, string To) target,
            string archiveFormat,
            string datName,
            CancellationToken cancellationToken,
            IProgress<int>? compressionProgress = null
        )
        {
            ArgumentNullException.ThrowIfNull(match);

            string? tempFile = null;
            string? tempArchive = null;
            try
            {
                Result<string> extractResult = await _extractor.ExtractToTempFileAsync(
                    target.From,
                    cancellationToken
                );
                if (extractResult.IsFailed)
                {
                    return Result.Fail(
                        $"{Path.GetFileName(target.From)}: {extractResult.Errors[0].Message}"
                    );
                }

                tempFile = extractResult.Value;

                // Compress to a working archive in the app temp directory, then move it into place.
                // A partial file from a failed or cancelled compress stays in the temp directory
                // (swept on the next launch) rather than landing next to the user's ROMs.
                string compressTarget = _workspace.NewWorkingArchivePath(archiveFormat);
                tempArchive = compressTarget;

                string entryName = ArchiveWorkspace.BuildEntryName(
                    target.To,
                    match.Game.Files.RomExtension
                );

                // Reserve this job's estimated working set before compressing so concurrent
                // re-archive/trim operations never together exceed physical memory — the gate is a
                // single DI instance shared across every compress-based call site (see App.axaml.cs).
                // The lease is scoped to the compress call alone: the encoder's memory is gone once
                // it returns, so holding the reservation across the archive move and the store
                // writes below would block other jobs on I/O that costs no encoder memory at all.
                long cost = _compressor.EstimateWorkingSetBytes(match.Game.RomSize, archiveFormat);
                Result compressResult;
                using (
                    await _memoryGate.AcquireAsync(cost, cancellationToken).ConfigureAwait(false)
                )
                {
                    compressResult = await _compressor.CompressAsync(
                        tempFile,
                        compressTarget,
                        entryName,
                        match.Game.RomSize,
                        compressionProgress,
                        archiveFormat,
                        cancellationToken
                    );
                }

                if (compressResult.IsFailed)
                {
                    return Result.Fail(
                        $"{Path.GetFileName(target.From)}: {compressResult.Errors[0].Message}"
                    );
                }

                (string? placeError, bool consumed) = await _workspace.PlaceWorkingArchiveAsync(
                    compressTarget,
                    target.From,
                    target.To
                );
                // Only clear tempArchive once PlaceWorkingArchiveAsync has actually taken ownership
                // of it (moved to the destination or the recovery folder). If it's still untouched,
                // leave it set so the finally block cleans it up immediately.
                if (consumed)
                    tempArchive = null;
                if (placeError is not null)
                    return Result.Fail($"{Path.GetFileName(target.From)}: {placeError}");

                await _reArchiveStore.MarkAsync(datName, match.Game.ReleaseNumber);

                MatchResult updatedMatch = new MatchResult
                {
                    Game = match.Game,
                    Status = MatchStatus.Verified,
                    ScannedRom = match.ScannedRom! with
                    {
                        FilePath = target.To,
                        FileExtension = archiveFormat,
                    },
                    IsIncorrectlyNamed = false,
                    // Re-archiving repacks the archive with the correct entry name.
                    IsEntryMisnamed = false,
                    IsWrongArchiveType = false,
                    IsUntrimmed = match.IsUntrimmed,
                    IsReArchived = true,
                };

                await _scanResultStore.UpdateResultAsync(datName, updatedMatch);
                return Result.Ok(updatedMatch);
            }
            finally
            {
                if (tempFile is not null && _fileOperations.FileExists(tempFile))
                    await _fileOperations.DeleteAsync(tempFile);
                // Clean up a leftover temp archive from a failed or cancelled in-place
                // compress. It is nulled once the original is deleted, so this only ever
                // runs while the original is still intact.
                if (tempArchive is not null && _fileOperations.FileExists(tempArchive))
                    await _fileOperations.DeleteAsync(tempArchive);
            }
        }
    }
}
