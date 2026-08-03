using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Matching;

namespace RomForge.Core.Operations
{
    /// <summary>
    /// Renames a ROM archive's outer file to match a naming mask. This is the outer-file operation
    /// only — a misnamed entry <em>inside</em> the archive is not its concern (that needs a re-archive).
    /// </summary>
    public interface IRomRenameService
    {
        /// <summary>
        /// Renames the outer archive file of <paramref name="match"/> to match
        /// <paramref name="namingMask"/>.
        /// </summary>
        /// <returns>
        /// A success carrying the updated <see cref="MatchResult"/> when the file was renamed; a
        /// success carrying <see langword="null"/> when no rename was needed (the outer name is already
        /// correct, or there is nothing to rename); a failure carrying the IO error otherwise.
        /// </returns>
        Task<Result<MatchResult?>> RenameAsync(MatchResult match, string namingMask);
    }
}
