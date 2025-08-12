using System;
using System.Collections.Generic;

namespace OpenshiftOPSCenter.App.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime LastLogin { get; set; }
        public bool IsActive { get; set; }
        public string Function { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? AccessRequestedAt { get; set; }
        public bool IsAdmin { get; set; }

        public User()
        {
        }

        public User(string username, string email, string role, bool isAdmin, string function)
        {
            Username = username;
            Email = email;
            Role = role;
            IsAdmin = isAdmin;
            Function = function;
        }
    }

    public static class UserRoles
    {
        public const string Admin = "LDE08_K8s_OOC_Admin";
        public const string PowerUser = "LDE08_K8s_OOC_Poweruser";
        public const string User = "LDE08_K8s_OOC_User";
        public const string OpcUa = "LDE08_K8s_OOC_OPCUA";

        public static readonly List<string> AllRoles = new List<string>
        {
            Admin,
            PowerUser,
            User,
            OpcUa
        };
    }
} 