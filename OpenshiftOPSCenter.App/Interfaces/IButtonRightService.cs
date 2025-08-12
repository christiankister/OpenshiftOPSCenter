using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenshiftOPSCenter.App.Interfaces
{
    public interface IButtonRightService
    {
        Task<bool> HasRightAsync(string buttonName, string role);
        Task<List<string>> GetRightsForRoleAsync(string role);
        Task SetRightForRoleAsync(string buttonName, string role, bool isAssigned);
        Task<Dictionary<string, bool>> GetAllRightsForRoleAsync(string role);
        Task ResetToDefaultRightsAsync(string role);
        Task SaveRightsAsync();
    }
} 