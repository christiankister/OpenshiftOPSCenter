using System;
using System.Security.Principal;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Threading.Tasks;

namespace OpenshiftOPSCenter.App.Services
{
    public interface IWindowsAuthService
    {
        Task<(string username, string fullName)> GetCurrentWindowsUserAsync();
    }

    public class WindowsAuthService : IWindowsAuthService
    {
        public async Task<(string username, string fullName)> GetCurrentWindowsUserAsync()
        {
            return await Task.Run(() =>
            {
                var identity = WindowsIdentity.GetCurrent();
                if (identity != null)
                {
                    var name = identity.Name;
                    var parts = name.Split('\\');
                    if (parts.Length == 2)
                    {
                        return (parts[1], name);
                    }
                }
                return (string.Empty, string.Empty);
            });
        }

        private string GetUserFullName(string username)
        {
            try
            {
                using (var context = new PrincipalContext(ContextType.Domain, "BBMAG.BBRAUN.COM"))
                {
                    var user = UserPrincipal.FindByIdentity(context, username);
                    if (user != null)
                    {
                        return user.DisplayName ?? $"{user.GivenName} {user.Surname}";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Abrufen des vollständigen Namens: {ex.Message}");
            }

            return username;
        }
    }
} 