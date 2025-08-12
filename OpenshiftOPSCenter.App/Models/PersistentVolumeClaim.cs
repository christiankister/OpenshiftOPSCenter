using System;

namespace OpenshiftOPSCenter.App.Models
{
    public class PersistentVolumeClaim
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string StorageClass { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string VolumeName { get; set; } = string.Empty;
        public string Capacity { get; set; } = string.Empty;
        public string UsedCapacity { get; set; } = string.Empty;
        public string AccessModes { get; set; } = string.Empty;
        public DateTime CreationTimestamp { get; set; }
    }
} 