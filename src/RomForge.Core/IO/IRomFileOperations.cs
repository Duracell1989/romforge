using System;
using System.IO;
using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO
{
    public interface IRomFileOperations
    {
        Task<Result> RenameAsync(string from, string to);
        Task<Result> DeleteAsync(string path);
        Task<Result> TruncateAsync(string path, long length);
        bool DirectoryExists(string path);
        bool FileExists(string path);

        /// <summary>
        /// Opens <paramref name="path"/> for reading. The caller owns the returned stream and must
        /// dispose it.
        /// </summary>
        Task<Stream> OpenReadAsync(string path);

        /// <summary>
        /// Returns the current on-disk size and last-write time (UTC) of <paramref name="path"/>.
        /// </summary>
        Task<(long Size, DateTime LastWriteTimeUtc)> GetFileInfoAsync(string path);
    }
}
