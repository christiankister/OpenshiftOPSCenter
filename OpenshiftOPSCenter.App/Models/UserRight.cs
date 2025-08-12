using System.Collections.Generic;

namespace OpenshiftOPSCenter.App.Models
{
    /// <summary>
    /// Klasse für Benutzerrechte basierend auf Buttons in der Anwendung
    /// </summary>
    public static class UserRight
    {
        // Main Sidebar Buttons
        public const string UserManagement = "UserManagementButton";
        public const string Settings = "SettingsButton";
        
        // Applications Sidebar Buttons
        public const string DeploymentsPods = "AppButton1";
        public const string Volumes = "AppButton2";
        public const string DeleteUnboundPVs = "DeleteUnboundPVsButton";
        public const string MultiProject = "AppButton3";
        public const string CreateCertificate = "AppButton4";
        public const string AutomationManager = "AutomationManagerButton";
        public const string OISFilesVolume = "OISFilesVolumeButton";
        public const string OISFilesOptIot = "OISFilesOptIotVolumeButton";
        public const string FirmwareVolume = "FirmwareVolumeButton";
        public const string EDHRVolume = "EDHRVolumeButton";
        public const string SAPConnectivity = "SAPConnectivityVolumeButton";
        public const string ServiceRouteRolebinding = "AppButton5";

        // Standard-Rechtesets nach Rolle
        public static Dictionary<string, List<string>> DefaultRightsByRole = new Dictionary<string, List<string>>
        {
            { 
                "Admin", new List<string> 
                { 
                    UserManagement, Settings, DeploymentsPods, Volumes, DeleteUnboundPVs, MultiProject,
                    CreateCertificate, AutomationManager, OISFilesVolume, OISFilesOptIot, FirmwareVolume,
                    EDHRVolume, SAPConnectivity, ServiceRouteRolebinding
                }
            },
            { 
                "PowerUser", new List<string> 
                { 
                    Settings, DeploymentsPods, Volumes, DeleteUnboundPVs, MultiProject,
                    CreateCertificate, AutomationManager, OISFilesVolume, OISFilesOptIot, FirmwareVolume,
                    EDHRVolume, SAPConnectivity, ServiceRouteRolebinding
                }
            },
            { 
                "User", new List<string> 
                { 
                    DeploymentsPods, Volumes, MultiProject
                }
            },
            { 
                "OPCUA", new List<string> 
                {
                    // OPCUA hat standardmäßig keine Rechte auf Application Buttons
                }
            }
        };

        // Alle verfügbaren Rechte
        public static readonly List<string> AllRights = new List<string>
        {
            UserManagement, Settings, DeploymentsPods, Volumes, DeleteUnboundPVs, MultiProject,
            CreateCertificate, AutomationManager, OISFilesVolume, OISFilesOptIot, FirmwareVolume,
            EDHRVolume, SAPConnectivity, ServiceRouteRolebinding
        };

        // Beschreibungen für die Rechte (für die Benutzeroberfläche)
        public static readonly Dictionary<string, string> RightDescriptions = new Dictionary<string, string>
        {
            { UserManagement, "Benutzerverwaltung" },
            { Settings, "Einstellungen" },
            { DeploymentsPods, "Deployments und Pods verwalten" },
            { Volumes, "Volumes verwalten" },
            { DeleteUnboundPVs, "Ungebundene PVs löschen" },
            { MultiProject, "Multi-Projekt Aktionen" },
            { CreateCertificate, "Zertifikate erstellen" },
            { AutomationManager, "Automation Manager erstellen" },
            { OISFilesVolume, "OISFiles Volumes erstellen" },
            { OISFilesOptIot, "OISFiles OPT/IOT Volumes erstellen" },
            { FirmwareVolume, "Firmware Volumes erstellen" },
            { EDHRVolume, "EDHR Volumes erstellen" },
            { SAPConnectivity, "SAP Connectivity Volumes erstellen" },
            { ServiceRouteRolebinding, "Service, Route und Rolebinding erstellen" }
        };

        // Konvertiere einen Button-Namen in einen benutzerfreundlichen Namen
        public static string GetRightDisplayName(string rightKey)
        {
            if (RightDescriptions.TryGetValue(rightKey, out string description))
            {
                return description;
            }
            return rightKey;
        }
    }
} 