using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenshiftOPSCenter.App.Models
{
    public class OpenShiftResourceList
    {
        public string apiVersion { get; set; } = string.Empty;
        public string kind { get; set; } = string.Empty;
        public List<OpenShiftResource> items { get; set; } = new List<OpenShiftResource>();
    }

    public class OpenShiftResource
    {
        public OpenShiftMetadata metadata { get; set; } = new OpenShiftMetadata();
        public OpenShiftSpec spec { get; set; } = new OpenShiftSpec();
        public OpenShiftStatus status { get; set; } = new OpenShiftStatus();
    }

    public class OpenShiftMetadata
    {
        public string name { get; set; } = string.Empty;
        [JsonPropertyName("namespace")]
        public string namespaceName { get; set; } = string.Empty;
        public Dictionary<string, string> annotations { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> labels { get; set; } = new Dictionary<string, string>();
        public System.DateTime creationTimestamp { get; set; }
    }

    public class OpenShiftSpec
    {
        public string volumeName { get; set; } = string.Empty;
        public List<string> accessModes { get; set; } = new List<string>();
        public Dictionary<string, string> capacity { get; set; } = new Dictionary<string, string>();
        public string storageClassName { get; set; } = string.Empty;
        public string persistentVolumeReclaimPolicy { get; set; } = string.Empty;
        public ClaimReference claimRef { get; set; } = new ClaimReference();
    }

    public class ClaimReference
    {
        public string name { get; set; } = string.Empty;
        [JsonPropertyName("namespace")]
        public string namespaceName { get; set; } = string.Empty;
    }

    public class OpenShiftStatus
    {
        public string phase { get; set; } = string.Empty;
        public Dictionary<string, string> capacity { get; set; } = new Dictionary<string, string>();
    }
} 