namespace OpenshiftOPSCenter.App.Models
{
    public class ClusterConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SelectedProject { get; set; } = string.Empty;
    }
} 