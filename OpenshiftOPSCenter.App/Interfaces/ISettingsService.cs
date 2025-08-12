using System.Threading.Tasks;

namespace OpenshiftOPSCenter.App.Interfaces
{
    public interface ISettingsService
    {
        Task<string> GetOcToolFolderAsync();
        Task SetOcToolFolderAsync(string folderPath);
    }
}


