using System;
using System.Collections.Generic;

namespace OpenshiftOPSCenter.App.Models
{
    public class UserInfo
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime? LastLogin { get; set; }
        public List<string> LdapGroups { get; set; } = new List<string>();
        public List<string> Rights { get; set; } = new List<string>();
        public User? User { get; set; }

        public UserInfo()
        {
        }

        public UserInfo(string username, string fullName, string role)
        {
            Username = username;
            FullName = fullName;
            Role = role;
            LastLogin = DateTime.Now;
        }
    }
} 