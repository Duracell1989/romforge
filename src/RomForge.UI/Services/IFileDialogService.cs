using System.Threading.Tasks;

namespace RomForge.UI.Services;

public interface IFileDialogService
{
    Task<string?> PickDatFileAsync();
    Task<string?> PickRomFolderAsync();
    Task<string?> PickUnverifiedDestinationAsync();
}
