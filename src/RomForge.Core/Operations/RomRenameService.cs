using System;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Scanning;

namespace RomForge.Core.Operations
{
    /// <summary>
    /// Default <see cref="IRomRenameService"/>: computes the rename target with <see cref="RomRenamer"/>,
    /// moves the outer file through <see cref="IRomFileOperations"/>, and produces the updated match.
    /// </summary>
    public sealed class RomRenameService : IRomRenameService
    {
        private readonly IRomFileOperations _fileOperations;

        public RomRenameService(IRomFileOperations fileOperations)
        {
            ArgumentNullException.ThrowIfNull(fileOperations);
            _fileOperations = fileOperations;
        }

        public async Task<Result<MatchResult?>> RenameAsync(MatchResult match, string namingMask)
        {
            ArgumentNullException.ThrowIfNull(match);

            (string From, string To)? target = RomRenamer.GetRenameTarget(match, namingMask);
            if (target is null)
                return Result.Ok<MatchResult?>(null);

            Result renameResult = await _fileOperations.RenameAsync(
                target.Value.From,
                target.Value.To
            );
            if (renameResult.IsFailed)
                return Result.Fail<MatchResult?>(renameResult.Errors);

            // GetRenameTarget only returns a target when ScannedRom is present.
            ScannedRom updatedRom = match.ScannedRom! with
            {
                FilePath = target.Value.To,
            };
            MatchResult updated = new MatchResult
            {
                Game = match.Game,
                Status = MatchStatus.Verified,
                ScannedRom = updatedRom,
                IsIncorrectlyNamed = false,
                // A rename only moves the outer file; a misnamed inner entry is untouched.
                IsEntryMisnamed = match.IsEntryMisnamed,
                IsWrongArchiveType = match.IsWrongArchiveType,
                IsUntrimmed = match.IsUntrimmed,
                IsReArchived = match.IsReArchived,
            };
            return Result.Ok<MatchResult?>(updated);
        }
    }
}
