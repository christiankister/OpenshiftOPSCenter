using System.Threading.Tasks;
using System.Collections.Generic;

namespace OpenshiftOPSCenter.App.Interfaces
{
    public interface ILdapService
    {
        Task<(bool success, string message)> ValidateCredentialsAsync(string username, string password);
        Task<(string fullName, string role, List<string> groups, List<string> rights)> GetUserInfoAsync(string username);
    }
} 