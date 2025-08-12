using System;

namespace OpenshiftOPSCenter.App.Models
{
    public class ButtonRight
    {
        public int Id { get; set; }
        public string ButtonName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsAssigned { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ButtonRight()
        {
            CreatedAt = DateTime.Now;
        }

        public ButtonRight(string buttonName, string description, string role, bool isAssigned)
        {
            ButtonName = buttonName;
            Description = description;
            Role = role;
            IsAssigned = isAssigned;
            CreatedAt = DateTime.Now;
        }
    }
} 