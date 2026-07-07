using System.Threading.Tasks;
using FluentResults;

namespace RomForge.Core.IO;

public interface IRomFileOperations
{
    Task<Result> RenameAsync(string from, string to);
    Task<Result> DeleteAsync(string path);
    Task<Result> TruncateAsync(string path, long length);
    bool DirectoryExists(string path);
}
