using System;
using System.IO;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.Operations
{
    /// <summary>
    /// Shared working-archive plumbing for the compress-based operations (re-archive, trim): allocates
    /// a temp path to compress into, names the ROM entry, and moves the finished archive into its final
    /// location with crash-safe recovery. Both operations compress to a temp file first and only touch
    /// the user's ROM folder through <see cref="PlaceWorkingArchiveAsync"/>.
    /// </summary>
    public sealed class ArchiveWorkspace
    {
        // The original is renamed aside to this suffix (rather than deleted up front) for an in-place
        // operation, so a crash between the rename-aside and the final move cannot destroy the ROM.
        private const string OriginalAsideSuffix = ".bak";

        private readonly AppDataService _appData;
        private readonly IRomFileOperations _fileOperations;
        private readonly ILogger _logger;

        public ArchiveWorkspace(AppDataService appData, IRomFileOperations fileOperations, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(appData);
            ArgumentNullException.ThrowIfNull(fileOperations);
            ArgumentNullException.ThrowIfNull(logger);
            _appData = appData;
            _fileOperations = fileOperations;
            _logger = logger.ForContext<ArchiveWorkspace>();
        }

        /// <summary>
        /// A fresh path for a working archive in the app temp directory. Compression always writes
        /// here rather than into a ROM folder, so a failed or cancelled operation never leaves a
        /// partial file next to the user's ROMs. The temp directory is swept on the next launch.
        /// </summary>
        public string NewWorkingArchivePath(string format) => Path.Combine(_appData.TempPath, "rearchive-" + Guid.NewGuid().ToString("N") + "." + format);

        /// <summary>
        /// The name to give the ROM entry inside a freshly-compressed archive: the expected file
        /// stem with the DAT's declared ROM extension. The extracted temp file's own name can't be
        /// used for this — it's a <see cref="Path.GetRandomFileName"/>-derived name that always
        /// carries its own random extension, so deriving the extension from it would fabricate one
        /// whenever the DAT's real ROM extension is empty.
        /// </summary>
        public static string BuildEntryName(string targetPath, string romExtension)
        {
            string stem = Path.GetFileNameWithoutExtension(targetPath);
            return string.IsNullOrEmpty(romExtension) ? stem : stem + "." + romExtension;
        }

        /// <summary>
        /// Moves a freshly-compressed working archive into its final location, replacing the original
        /// for an in-place operation. Returns an error message on failure, or <see langword="null"/> on
        /// success. The returned <c>Consumed</c> flag is <see langword="true"/> whenever the working
        /// archive was moved somewhere (final destination or the recovery folder) and the caller no
        /// longer owns it; it is <see langword="false"/> only when the working archive is still
        /// sitting untouched at its original path (the original could not be renamed aside, so nothing
        /// was attempted), so the caller's own cleanup is still responsible for it. If placement fails
        /// after the original has already been renamed aside, that rename is undone so the original is
        /// restored to <paramref name="fromPath"/>; the compressed archive itself is moved to
        /// <see cref="AppDataService.RecoveredPath"/> — never the destination directory again, since
        /// that is the directory placement just failed against — so the ROM is never lost to the
        /// auto-swept <see cref="AppDataService.TempPath"/>.
        /// </summary>
        /// <remarks>
        /// For the in-place case (<paramref name="fromPath"/> equals <paramref name="toPath"/>), the
        /// original is renamed aside rather than deleted up front. A crash between the rename-aside and
        /// the final move would otherwise destroy the ROM outright — the aside copy is only deleted
        /// once the working archive has been placed successfully.
        /// </remarks>
        public async Task<(string? Error, bool Consumed)> PlaceWorkingArchiveAsync(string workingArchive, string fromPath, string toPath)
        {
            ArgumentNullException.ThrowIfNull(workingArchive);
            ArgumentNullException.ThrowIfNull(fromPath);
            ArgumentNullException.ThrowIfNull(toPath);

            bool sameFile = fromPath.Equals(toPath, StringComparison.OrdinalIgnoreCase);
            string? asidePath = null;

            if (sameFile)
            {
                asidePath = fromPath + OriginalAsideSuffix;
                Result renameAside = await _fileOperations.RenameAsync(fromPath, asidePath);
                if (renameAside.IsFailed)
                {
                    return ($"Could not replace original: {Path.GetFileName(fromPath)}: {renameAside.Errors[0].Message}", false);
                }
            }

            Result move = await _fileOperations.RenameAsync(workingArchive, toPath);
            if (move.IsFailed)
            {
                return await RecoverFromFailedMoveAsync(workingArchive, fromPath, toPath, asidePath, move.Errors[0].Message);
            }

            if (!sameFile)
            {
                Result deleteOriginal = await _fileOperations.DeleteAsync(fromPath);
                if (deleteOriginal.IsFailed)
                {
                    return ($"Archived but could not delete original: {Path.GetFileName(fromPath)}: {deleteOriginal.Errors[0].Message}", true);
                }
            }
            else
            {
                Result deleteAside = await _fileOperations.DeleteAsync(asidePath!);
                if (deleteAside.IsFailed)
                {
                    _logger.Warning("Could not delete original backup at {Aside}: {Error}", asidePath, deleteAside.Errors[0].Message);
                }
            }

            return (null, true);
        }

        // Extracted from PlaceWorkingArchiveAsync only to keep that method's cognitive complexity
        // under the limit — the recovery sequence itself is unchanged: put the aside copy back
        // first, then move the compressed archive somewhere that does not depend on the
        // destination which just failed.
        private async Task<(string? Error, bool Consumed)> RecoverFromFailedMoveAsync(
            string workingArchive,
            string fromPath,
            string toPath,
            string? asidePath,
            string moveError
        )
        {
            var restoreNote = string.Empty;
            if (asidePath is not null)
            {
                Result restoreOriginal = await _fileOperations.RenameAsync(asidePath, fromPath);
                if (restoreOriginal.IsFailed)
                    restoreNote = $" The original is still safe at:\n{asidePath}";
            }

            // The primary move failed, so the destination directory itself is the likely
            // problem (offline volume, permissions, full disk). Recovering to a sibling path
            // in that same directory would fail for the same reason, so recover into the app's
            // own recovered/ folder instead — a location whose availability doesn't depend on
            // the destination that just failed.
            string recovery = Path.Combine(_appData.RecoveredPath, Path.GetFileName(workingArchive));
            Result fallback = await _fileOperations.RenameAsync(workingArchive, recovery);
            string kept = fallback.IsFailed ? workingArchive : recovery;
            _logger.Error("Could not place archive at {To}; kept the compressed copy at {Kept}", toPath, kept);
            return ($"Archived but could not place it at {Path.GetFileName(toPath)} ({moveError}). A copy was kept at:\n{kept}{restoreNote}", true);
        }
    }
}
