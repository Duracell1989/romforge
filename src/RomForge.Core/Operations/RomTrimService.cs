using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Scanning;

namespace RomForge.Core.Operations;

/// <summary>
/// Default <see cref="IRomTrimService"/>: extract → truncate to the DAT's ROM size → recompress to a
/// working archive → place it over the original. Temp files are always cleaned up, and the working
/// archive is only handed off once placement has taken ownership of it.
/// </summary>
public sealed class RomTrimService : IRomTrimService
{
    private readonly IArchiveExtractor _extractor;
    private readonly IArchiveCompressor _compressor;
    private readonly IRomFileOperations _fileOperations;
    private readonly ArchiveWorkspace _workspace;

    public RomTrimService(
        IArchiveExtractor extractor,
        IArchiveCompressor compressor,
        IRomFileOperations fileOperations,
        ArchiveWorkspace workspace
    )
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(compressor);
        ArgumentNullException.ThrowIfNull(fileOperations);
        ArgumentNullException.ThrowIfNull(workspace);
        _extractor = extractor;
        _compressor = compressor;
        _fileOperations = fileOperations;
        _workspace = workspace;
    }

    public async Task<Result<MatchResult>> TrimAsync(
        MatchResult match,
        (string From, string To) target,
        string archiveFormat,
        CancellationToken cancellationToken,
        IProgress<int>? compressionProgress = null
    )
    {
        ArgumentNullException.ThrowIfNull(match);

        string? tempRom = null;
        string? tempArchive = null;
        try
        {
            Result<string> extractResult = await _extractor.ExtractToTempFileAsync(
                target.From,
                cancellationToken
            );
            if (extractResult.IsFailed)
                return Result.Fail(
                    $"{Path.GetFileName(target.From)}: {extractResult.Errors[0].Message}"
                );

            tempRom = extractResult.Value;

            Result truncateResult = await _fileOperations.TruncateAsync(
                tempRom,
                match.Game.RomSize
            );
            if (truncateResult.IsFailed)
                return Result.Fail(
                    $"{Path.GetFileName(target.From)}: {truncateResult.Errors[0].Message}"
                );

            string archiveDest = _workspace.NewWorkingArchivePath(archiveFormat);
            tempArchive = archiveDest;

            string entryName = ArchiveWorkspace.BuildEntryName(
                target.To,
                match.Game.Files.RomExtension
            );
            Result compressResult = await _compressor.CompressAsync(
                tempRom,
                archiveDest,
                entryName,
                match.Game.RomSize,
                compressionProgress,
                archiveFormat,
                cancellationToken
            );
            if (compressResult.IsFailed)
                return Result.Fail(
                    $"{Path.GetFileName(target.From)}: {compressResult.Errors[0].Message}"
                );

            (string? placeError, bool consumed) = await _workspace.PlaceWorkingArchiveAsync(
                archiveDest,
                target.From,
                target.To
            );
            if (consumed)
                tempArchive = null;
            if (placeError is not null)
                return Result.Fail($"{Path.GetFileName(target.From)}: {placeError}");

            MatchResult updated = new MatchResult
            {
                Game = match.Game,
                Status = MatchStatus.Verified,
                ScannedRom = match.ScannedRom! with
                {
                    FilePath = target.To,
                    FileExtension = archiveFormat,
                    Crc = match.Game.Files.RomCrc,
                    TrimmedCrc = null,
                },
                IsIncorrectlyNamed = false,
                // Trimming repacks the archive with the correct entry name.
                IsEntryMisnamed = false,
                IsWrongArchiveType = false,
                IsUntrimmed = false,
                IsReArchived = match.IsReArchived,
            };
            return Result.Ok(updated);
        }
        finally
        {
            if (tempRom is not null && _fileOperations.FileExists(tempRom))
                await _fileOperations.DeleteAsync(tempRom);
            // Remove a leftover temp archive from a failed or cancelled in-place trim. It is
            // nulled once the original is deleted, so this only runs while the original survives.
            if (tempArchive is not null && _fileOperations.FileExists(tempArchive))
                await _fileOperations.DeleteAsync(tempArchive);
        }
    }
}
