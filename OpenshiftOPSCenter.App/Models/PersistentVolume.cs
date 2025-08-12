using System;

namespace OpenshiftOPSCenter.App.Models
{
    public class PersistentVolume
    {
        public string Name { get; set; } = string.Empty;
        public string StorageClass { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Capacity { get; set; } = string.Empty;
        public string AccessModes { get; set; } = string.Empty;
        public string ReclaimPolicy { get; set; } = string.Empty;
        public string ClaimRef { get; set; } = string.Empty;
        public DateTime CreationTimestamp { get; set; }
    }
} 