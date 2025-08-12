using System;
using System.Collections.Generic;

namespace OpenshiftOPSCenter.App.Models
{
    public class Deployment
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public int Replicas { get; set; }
        public int AvailableReplicas { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreationTimestamp { get; set; }
        public List<Pod> Pods { get; set; } = new List<Pod>();
    }

    public class Pod
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Node { get; set; } = string.Empty;
        public DateTime CreationTimestamp { get; set; }
        public string IP { get; set; } = string.Empty;
    }
} 