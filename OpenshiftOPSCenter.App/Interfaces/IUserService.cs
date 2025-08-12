using OpenshiftOPSCenter.App.Models;

namespace OpenshiftOPSCenter.App.Interfaces
{
    public interface IUserService
    {
        User? GetCurrentUser();
        void SetCurrentUser(UserInfo user);
        void ClearCurrentUser();
    }
} 