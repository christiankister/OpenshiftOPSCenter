using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.DirectoryServices.AccountManagement;
using OpenshiftOPSCenter.App.Services;
using OpenshiftOPSCenter.App.Models;
using OpenshiftOPSCenter.App.Interfaces;
using System.Collections.Generic;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace OpenshiftOPSCenter.App.Views
{
    public partial class UserManagementPage : Page
    {
        private readonly ILdapService _ldapService;
        private readonly IUserService _userService;
        private readonly ILoggingService _loggingService;
        private readonly IButtonRightService _buttonRightService;
        private readonly List<string> _relevantGroups = new List<string>
        {
            UserRoles.Admin,
            UserRoles.PowerUser,
            UserRoles.User,
            UserRoles.OpcUa
        };
        
        private ObservableCollection<ButtonRightViewModel> _currentRoleRights;
        private string _currentSelectedRole;
        
        public UserManagementPage()
        {
            InitializeComponent();
            _loggingService = new LoggingService();
            _ldapService = new LdapService(_loggingService);
            _userService = new UserService(_loggingService);
            _buttonRightService = new ButtonRightService(_loggingService);
            
            InitializeRolesComboBox();
            LoadUserData();
        }
        
        private void InitializeRolesComboBox()
        {
            RoleComboBox.SelectedIndex = 0; // PowerUser als Standardauswahl
        }
        
        private async void LoadUserData()
        {
            try
            {
                _loggingService.LogInfo("Lade Benutzerdaten...");
                
                var currentUser = _userService.GetCurrentUser();
                if (currentUser == null)
                {
                    _loggingService.LogWarning("Kein aktueller Benutzer gefunden");
                    MessageBox.Show("Kein aktueller Benutzer gefunden.", "Warnung", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Benutzerinformationen aus LDAP holen
                var username = currentUser.Username;
                // Zuerst überprüfen, ob die Anmeldedaten gültig sind
                var authResult = await _ldapService.ValidateCredentialsAsync(username, string.Empty);
                
                string fullName = string.Empty;
                string role = currentUser.Role;
                List<string> groups = new List<string>();
                List<string> rights = new List<string>();
                
                if (authResult.success)
                {
                    // Weitere Informationen des Benutzers holen
                    var userInfo = await _ldapService.GetUserInfoAsync(username);
                    fullName = userInfo.fullName;
                    role = userInfo.role;
                    groups = userInfo.groups ?? new List<string>();
                    rights = userInfo.rights ?? new List<string>();
                }
                
                UsernameText.Text = currentUser.Username;
                FullNameText.Text = fullName;
                LastLoginText.Text = currentUser.LastLogin.ToString("dd.MM.yyyy HH:mm");
                RoleText.Text = role;
                
                // Filtere nur die relevanten LDAP-Gruppen
                var relevantGroups = groups.Where(g => _relevantGroups.Contains(g)).ToList();
                
                // Erzeuge ViewModel für LDAP-Gruppen
                var groupViewModels = groups.Select(g => new GroupViewModel
                {
                    Name = g,
                    IsUserGroup = relevantGroups.Contains(g)
                }).ToList();
                
                // Lade Benutzerrechte aus der Datenbank
                if (role == UserRoles.Admin)
                {
                    // Admin hat alle Rechte
                    rights = new List<string>
                    {
                        "Alle Rechte (Admin)"
                    };
                }
                else
                {
                    // Für andere Rollen die spezifischen Rechte laden
                    rights = await _buttonRightService.GetRightsForRoleAsync(role);
                }
                
                LdapGroupsList.ItemsSource = groupViewModels;
                RightsList.ItemsSource = rights;
                
                _loggingService.LogInfo($"Benutzerdaten geladen: Username={currentUser.Username}, FullName={fullName}, Role={role}, LastLogin={currentUser.LastLogin:g}");
                _loggingService.LogInfo($"Relevante Gruppen: {string.Join(", ", relevantGroups)}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler beim Laden der Benutzerdaten: {ex.Message}", ex);
                MessageBox.Show($"Fehler beim Laden der Benutzerdaten: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void RoleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RoleComboBox.SelectedItem != null)
            {
                var selectedItem = (ComboBoxItem)RoleComboBox.SelectedItem;
                _currentSelectedRole = selectedItem.Content.ToString();
                LoadRoleRights(_currentSelectedRole);
            }
        }
        
        private async void LoadRoleRights(string role)
        {
            try
            {
                _loggingService.LogInfo($"Lade Rechte für Rolle {role}");
                
                if (role == UserRoles.Admin)
                {
                    _loggingService.LogInfo("Admin-Rolle hat immer alle Rechte");
                    MessageBox.Show("Die Admin-Rolle hat automatisch alle Rechte und kann nicht bearbeitet werden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // Alle Button-Rechte für die ausgewählte Rolle laden
                var roleRights = await _buttonRightService.GetAllRightsForRoleAsync(role);
                
                // ViewModel erstellen
                _currentRoleRights = new ObservableCollection<ButtonRightViewModel>();
                
                // Main Sidebar Buttons
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "UserManagementButton",
                    Description = "Benutzerverwaltung",
                    IsAssigned = roleRights.ContainsKey("UserManagementButton") ? roleRights["UserManagementButton"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "SettingsButton",
                    Description = "Einstellungen",
                    IsAssigned = roleRights.ContainsKey("SettingsButton") ? roleRights["SettingsButton"] : false
                });
                
                // Applications Sidebar Buttons
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "AppButton1",
                    Description = "Deployments/Pods",
                    IsAssigned = roleRights.ContainsKey("AppButton1") ? roleRights["AppButton1"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "AppButton2",
                    Description = "Volumes",
                    IsAssigned = roleRights.ContainsKey("AppButton2") ? roleRights["AppButton2"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "AppButton3",
                    Description = "Multi Project",
                    IsAssigned = roleRights.ContainsKey("AppButton3") ? roleRights["AppButton3"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "AppButton4",
                    Description = "Create Webserver",
                    IsAssigned = roleRights.ContainsKey("AppButton4") ? roleRights["AppButton4"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "OISFilesVolumeButton",
                    Description = "Create OISFiles Volume",
                    IsAssigned = roleRights.ContainsKey("OISFilesVolumeButton") ? roleRights["OISFilesVolumeButton"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "OISFilesOptIotVolumeButton",
                    Description = "Create OISFiles OPT/IOT",
                    IsAssigned = roleRights.ContainsKey("OISFilesOptIotVolumeButton") ? roleRights["OISFilesOptIotVolumeButton"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "FirmwareVolumeButton",
                    Description = "Create Firmware Volume",
                    IsAssigned = roleRights.ContainsKey("FirmwareVolumeButton") ? roleRights["FirmwareVolumeButton"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "EDHRVolumeButton",
                    Description = "Create EDHR Volume",
                    IsAssigned = roleRights.ContainsKey("EDHRVolumeButton") ? roleRights["EDHRVolumeButton"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "SAPConnectivityVolumeButton",
                    Description = "SAP Connectivity Volume",
                    IsAssigned = roleRights.ContainsKey("SAPConnectivityVolumeButton") ? roleRights["SAPConnectivityVolumeButton"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "AppButton5",
                    Description = "Service, Route, Rolebinding",
                    IsAssigned = roleRights.ContainsKey("AppButton5") ? roleRights["AppButton5"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "DeleteUnboundPVsButton",
                    Description = "Delete Unbound PV's",
                    IsAssigned = roleRights.ContainsKey("DeleteUnboundPVsButton") ? roleRights["DeleteUnboundPVsButton"] : false
                });
                
                _currentRoleRights.Add(new ButtonRightViewModel
                {
                    ButtonName = "AutomationManagerButton",
                    Description = "Create Automation Manager",
                    IsAssigned = roleRights.ContainsKey("AutomationManagerButton") ? roleRights["AutomationManagerButton"] : false
                });
                
                // Setze die Liste als ItemsSource für die ListView
                ButtonRightsList.ItemsSource = _currentRoleRights;
                
                _loggingService.LogInfo($"Rechte für Rolle {role} geladen: {roleRights.Count} zugewiesene Rechte");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler beim Laden der Rollenrechte: {ex.Message}", ex);
                MessageBox.Show($"Fehler beim Laden der Rollenrechte: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void SaveRightsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSelectedRole == UserRoles.Admin)
                {
                    MessageBox.Show("Die Admin-Rolle hat automatisch alle Rechte und kann nicht bearbeitet werden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                _loggingService.LogInfo($"Speichere Rechte für Rolle {_currentSelectedRole}");
                
                foreach (var right in _currentRoleRights)
                {
                    await _buttonRightService.SetRightForRoleAsync(right.ButtonName, _currentSelectedRole, right.IsAssigned);
                }
                
                await _buttonRightService.SaveRightsAsync();
                
                MessageBox.Show($"Rechte für Rolle {_currentSelectedRole} wurden erfolgreich gespeichert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                _loggingService.LogInfo($"Rechte für Rolle {_currentSelectedRole} erfolgreich gespeichert");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler beim Speichern der Rechte: {ex.Message}", ex);
                MessageBox.Show($"Fehler beim Speichern der Rechte: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSelectedRole == UserRoles.Admin)
                {
                    MessageBox.Show("Die Admin-Rolle hat automatisch alle Rechte und kann nicht zurückgesetzt werden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                var result = MessageBox.Show($"Möchten Sie die Standardrechte für die Rolle {_currentSelectedRole} wiederherstellen?", "Standardrechte wiederherstellen", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    _loggingService.LogInfo($"Setze Standardrechte für Rolle {_currentSelectedRole}");
                    
                    await _buttonRightService.ResetToDefaultRightsAsync(_currentSelectedRole);
                    
                    MessageBox.Show($"Standardrechte für Rolle {_currentSelectedRole} wurden wiederhergestellt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                    _loggingService.LogInfo($"Standardrechte für Rolle {_currentSelectedRole} wiederhergestellt");
                    
                    // Lade die aktualisierten Rechte
                    LoadRoleRights(_currentSelectedRole);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler beim Zurücksetzen der Standardrechte: {ex.Message}", ex);
                MessageBox.Show($"Fehler beim Zurücksetzen der Standardrechte: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class GroupViewModel
    {
        public string Name { get; set; }
        public bool IsUserGroup { get; set; }
    }
    
    public class ButtonRightViewModel
    {
        public string ButtonName { get; set; }
        public string Description { get; set; }
        public bool IsAssigned { get; set; }
    }
} 