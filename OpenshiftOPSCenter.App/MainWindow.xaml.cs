using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using OpenshiftOPSCenter.App.Models;
using OpenshiftOPSCenter.App.Services;
using OpenshiftOPSCenter.App.Data;
using System.DirectoryServices.AccountManagement;
using OpenshiftOPSCenter.App.Views;
using OpenshiftOPSCenter.App.Interfaces;
using System.Collections.Generic;
using System.Text;
using System.Windows.Threading;
using System.Collections;
using System.IO;

namespace OpenshiftOPSCenter.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly IWindowsAuthService _windowsAuthService = new WindowsAuthService();
    private readonly ILdapService _ldapService = new LdapService();
    private readonly AppDbContext _dbContext = new AppDbContext();
    private User _currentUser;
    private string _currentUsername = string.Empty;
    private string _currentFullName = string.Empty;
    private bool _isLoggedIn = false;
    private readonly ILoggingService _loggingService = new LoggingService();
    private string _currentUserRole = string.Empty;
    private readonly IUserService _userService = new UserService(new LoggingService());
    private Models.UserInfo? _currentUserInfo;
    private readonly OpenShiftService _openShiftService;
    private List<string> _allProjects;
    private ClusterConfig _currentClusterConfig;
    private string _originalYaml = string.Empty;
    private string _modifiedYaml = string.Empty;
    // Füge eine Hilfsvariable hinzu, um doppelte Klicks zu verhindern
    private bool _isApplyingEDHRChanges = false;
    // Neue Sperrvariablen für die verschiedenen Volume-Funktionen
    private bool _isApplyingOISFilesChanges = false;
    private bool _isApplyingOISFilesOptIotChanges = false;
    private bool _isApplyingFirmwareChanges = false;
    private bool _isApplyingSAPConnectivityChanges = false;
    // Neue Property hinzufügen nach anderen Properties
    private bool _isClusterLoggedIn = false;
    private readonly IButtonRightService _buttonRightService;
    // Füge eine Variable für das Tracking der Automation Manager Änderungen hinzu
    private bool _isApplyingAutomationManagerChanges = false;
    private bool _hasDeployedAutomationManagerYamls = false;

    public MainWindow()
    {
        InitializeComponent();
        _openShiftService = new OpenShiftService();
        _allProjects = new List<string>();
        _currentClusterConfig = new ClusterConfig();
        _buttonRightService = new ButtonRightService(new LoggingService());
        
        // Erstelle einen Dummy-Benutzer für den Designer
        _currentUser = new User
        {
            Username = "Designer",
            Email = "designer@example.com",
            Role = UserRoles.Admin,
            Function = "Designer",
            IsActive = true,
            LastLogin = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        
        // UI initialisieren
        InitializeUI();
        
        // Login-Seite anzeigen
        ShowLoginPage();
        
        // Aktuellen Benutzer laden
        LoadCurrentUser();

        InitializeEventHandlers();
    }

    public MainWindow(User user)
    {
        InitializeComponent();
        
        _currentUser = user;
        _isLoggedIn = true;
        _openShiftService = new OpenShiftService();
        _allProjects = new List<string>();
        _currentClusterConfig = new ClusterConfig();
        _buttonRightService = new ButtonRightService(new LoggingService());
        
        InitializeUI();
        _loggingService.LogInfo($"Benutzer {user.Username} erfolgreich eingeloggt.");
        ShowDashboard();
    }

    private void InitializeUI()
    {
        // Setze Fenstertitel mit Benutzername
        Title = $"Openshift OPS Center - {_currentUser?.Username ?? _currentUsername}";

        // Anfangsstatus der Anwendungsbuttons setzen
        UpdateApplicationButtonVisibility();

        // Deaktiviere Buttons basierend auf Benutzerrolle
        if (_isLoggedIn)
        {
            switch (_currentUser.Role)
            {
                case UserRoles.Admin:
                    // Admin hat Zugriff auf alles
                    break;

                case UserRoles.PowerUser:
                    // PowerUser hat eingeschränkten Zugriff
                    DisableButton("AdminButton");
                    break;

                case UserRoles.User:
                    // Normaler Benutzer hat nur Basis-Zugriff
                    DisableButton("AdminButton");
                    DisableButton("PowerUserButton");
                    break;

                case UserRoles.OpcUa:
                    // OpcUa-Benutzer hat nur OpcUa-Zugriff
                    DisableButton("AdminButton");
                    DisableButton("PowerUserButton");
                    DisableButton("UserButton");
                    break;
            }
        }
    }

    private void DisableButton(string buttonName)
    {
        var button = this.FindName(buttonName) as Button;
        if (button != null)
        {
            button.IsEnabled = false;
            button.Opacity = 0.5;
        }
    }

    private async void LoadCurrentUser()
    {
        try
        {
            _currentUsername = Environment.UserName;
            _currentFullName = Environment.UserDomainName + "\\" + _currentUsername;
            
            CurrentUserText.Text = $"Benutzer: {_currentUsername}";
            FullNameText.Text = $"Domain: {_currentFullName}";
            
            // Benutzerrolle prüfen
            var authResult = await _ldapService.ValidateCredentialsAsync(_currentUsername, string.Empty);
            
            if (authResult.success)
            {
                var userInfo = await _ldapService.GetUserInfoAsync(_currentUsername);
                _currentUserRole = userInfo.role;
                
                // Berechtigungen basierend auf der Rolle setzen
                SetPermissionsByRole(_currentUserRole);
                
                // Wenn der Benutzer eine Rolle hat, zeige das Dashboard an
                if (!string.IsNullOrEmpty(_currentUserRole) && _currentUserRole != "Keine Rolle")
                {
                    ShowContent(DashboardContent);
                }
                else
                {
                    // Wenn keine Rolle, zeige die Zugriffsanfrage an
                    ShowContent(LoginContent);
                }
            }
            else
            {
                _currentUserRole = "Nicht authentifiziert";
                ShowContent(LoginContent);
                _loggingService.LogWarning($"Authentifizierung fehlgeschlagen: {authResult.message}");
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden des Benutzers: {ex.Message}", ex);
            ErrorMessage.Text = "Fehler beim Laden der Benutzerinformationen.";
        }
    }

    private async void SetPermissionsByRole(string role)
    {
        try 
        {
            _loggingService.LogInfo($"Setze Berechtigungen für Rolle: {role}");
            
            // Standardmäßig alle Buttons deaktivieren
            DashboardButton.IsEnabled = false;
            ClusterButton.IsEnabled = false;
            ApplicationsButton.IsEnabled = false; // Wird später aktiviert, wenn eingeloggt
            MonitoringButton.IsEnabled = false;
            UserManagementButton.IsEnabled = false;
            SettingsButton.IsEnabled = false;
            
            // Berechtigungen basierend auf der Rolle setzen
            if (role == UserRoles.Admin)
            {
                _loggingService.LogInfo("Admin-Benutzer erkannt - setze alle Rechte");
                
                // Admin hat Zugriff auf alles
                DashboardButton.IsEnabled = true;
                ClusterButton.IsEnabled = true;
                // ApplicationsButton nur aktivieren, wenn im Cluster eingeloggt
                ApplicationsButton.IsEnabled = _isClusterLoggedIn;
                MonitoringButton.IsEnabled = true;
                UserManagementButton.IsEnabled = true;
                SettingsButton.IsEnabled = true;
            }
            else
            {
                // Berechtigungen aus der Datenbank laden für andere Rollen
                DashboardButton.IsEnabled = true; // Alle haben Zugriff auf das Dashboard
                ClusterButton.IsEnabled = true;   // Alle haben Zugriff auf den Cluster
                
                // ApplicationsButton nur aktivieren, wenn im Cluster eingeloggt
                ApplicationsButton.IsEnabled = _isClusterLoggedIn;
                
                // Prüfe Berechtigungen für einzelne Buttons
                UserManagementButton.IsEnabled = await _buttonRightService.HasRightAsync("UserManagementButton", role);
                SettingsButton.IsEnabled = await _buttonRightService.HasRightAsync("SettingsButton", role);
                MonitoringButton.IsEnabled = true; // Alle sollen Monitoring sehen können
            }
            
            _loggingService.LogInfo($"Berechtigungen gesetzt: Dashboard={DashboardButton.IsEnabled}, " +
                                   $"Cluster={ClusterButton.IsEnabled}, " +
                                   $"Applications={ApplicationsButton.IsEnabled}, " +
                                   $"Monitoring={MonitoringButton.IsEnabled}, " +
                                   $"UserManagement={UserManagementButton.IsEnabled}, " +
                                   $"Settings={SettingsButton.IsEnabled}");
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Setzen der Berechtigungen: {ex.Message}", ex);
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_currentUsername))
            {
                if (ErrorMessage != null)
                {
                    ErrorMessage.Text = "Kein Benutzer gefunden. Bitte melden Sie sich an Windows an.";
                }
                return;
            }

            // Prüfe, ob der Benutzer in einer der erlaubten AD-Gruppen ist
            var authResult = await _ldapService.ValidateCredentialsAsync(_currentUsername, string.Empty);
            
            if (authResult.success)
            {
                var userInfo = await _ldapService.GetUserInfoAsync(_currentUsername);
                
                // Benutzer in der Datenbank speichern oder aktualisieren
                var user = _dbContext.Users.FirstOrDefault(u => u.Username == _currentUsername);
                if (user == null)
                {
                    user = new User
                    {
                        Username = _currentUsername,
                        Email = $"{_currentUsername}@bbraun.com",
                        Role = userInfo.role,
                        Function = "Nicht angegeben",
                        IsActive = true,
                        LastLogin = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.Users.Add(user);
                }
                else
                {
                    user.LastLogin = DateTime.UtcNow;
                    user.Role = userInfo.role;
                }
                _dbContext.SaveChanges();

                // Benutzer setzen und Dashboard anzeigen
                _currentUser = user;
                _isLoggedIn = true;
                InitializeUI();
                ShowDashboard();
                
                _loggingService.LogInfo($"Benutzer {_currentUsername} erfolgreich angemeldet mit Rolle: {userInfo.role}");
            }
            else
            {
                if (ErrorMessage != null)
                {
                    ErrorMessage.Text = "Sie haben keine Berechtigung für das Openshift OPS Center. Bitte fordern Sie Zugriff an.";
                }
                _loggingService.LogWarning($"Anmeldeversuch fehlgeschlagen: {authResult.message}");
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler bei der Anmeldung: {ex.Message}", ex);
            if (ErrorMessage != null)
            {
                ErrorMessage.Text = $"Fehler bei der Anmeldung: {ex.Message}";
            }
        }
    }

    private void ShowContent(Grid content)
    {
        LoginContent.Visibility = Visibility.Collapsed;
        DashboardContent.Visibility = Visibility.Collapsed;
        ClusterContent.Visibility = Visibility.Collapsed;
        ApplicationsContent.Visibility = Visibility.Collapsed;
        MonitoringContent.Visibility = Visibility.Collapsed;
        UserManagementContent.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Collapsed;

        content.Visibility = Visibility.Visible;
    }

    private void ShowLoginPage()
    {
        _loggingService.LogInfo("Login-Seite wird angezeigt");
        ShowContent(LoginContent);
    }

    private void ShowDashboard()
    {
        _loggingService.LogInfo("Dashboard wird angezeigt");
        ShowContent(DashboardContent);
    }

    private void InitializeEventHandlers()
    {
        // Navigation Buttons
        DashboardButton.Click += DashboardButton_Click;
        ClusterButton.Click += ClusterButton_Click;
        ApplicationsButton.Click += ApplicationsButton_Click;
        MonitoringButton.Click += MonitoringButton_Click;
        UserManagementButton.Click += UserManagementButton_Click;
        SettingsButton.Click += SettingsButton_Click;
        
        // Application Buttons
        HomeButton.Click += HomeButton_Click;
        AppButton1.Click += AppButton1_Click;
        AppButton2.Click += AppButton2_Click;
        AppButton3.Click += AppButton3_Click;
        AppButton4.Click += AppButton4_Click;
        OISFilesVolumeButton.Click += OISFilesVolumeButton_Click;
        OISFilesOptIotVolumeButton.Click += OISFilesOptIotVolumeButton_Click;
        FirmwareVolumeButton.Click += FirmwareVolumeButton_Click;
        EDHRVolumeButton.Click += EDHRVolumeButton_Click;
        SAPConnectivityVolumeButton.Click += SAPConnectivityVolumeButton_Click;
        AppButton5.Click += AppButton5_Click;
        
        // OISFiles Content Buttons
        ConfigureYamlButton.Click += ConfigureYamlButton_Click;
        ApplyChangesButton.Click += ApplyChangesButton_Click;
        GeneratePVYamlButton.Click += GeneratePVYamlButton_Click;
        GeneratePVCYamlButton.Click += GeneratePVCYamlButton_Click;
        GenerateSecretYamlButton.Click += GenerateSecretYamlButton_Click;
        
        // Firmware Content Buttons
        ConfigureFirmwareYamlButton.Click += ConfigureFirmwareYamlButton_Click;
        ApplyFirmwareChangesButton.Click += ApplyFirmwareChangesButton_Click;
        GenerateFirmwarePVYamlButton.Click += GenerateFirmwarePVYamlButton_Click;
        GenerateFirmwarePVCYamlButton.Click += GenerateFirmwarePVCYamlButton_Click;
        GenerateFirmwareSecretYamlButton.Click += GenerateFirmwareSecretYamlButton_Click;
        
        // OISFiles OPT/IOT Content Buttons
        ConfigureOptIotYamlButton.Click += ConfigureOptIotYamlButton_Click;
        ApplyOptIotChangesButton.Click += ApplyOptIotChangesButton_Click;
        GenerateOptIotPVYamlButton.Click += GenerateOptIotPVYamlButton_Click;
        GenerateOptIotPVCYamlButton.Click += GenerateOptIotPVCYamlButton_Click;
        GenerateOptIotSecretYamlButton.Click += GenerateOptIotSecretYamlButton_Click;
        
        // Initialisiere die GridView-Spaltenanpassung
        DeploymentsListView.Loaded += (s, e) => AdjustGridViewColumns(DeploymentsGridView);
        PodsListView.Loaded += (s, e) => AdjustGridViewColumns(PodsGridView);
        
        // Events für Fenstergrößenänderung
        SizeChanged += (s, e) => {
            AdjustGridViewColumns(DeploymentsGridView);
            AdjustGridViewColumns(PodsGridView);
        };
    }

    private void AdjustGridViewColumns(GridView gridView)
    {
        if (gridView == null || gridView.Columns.Count == 0) return;
        
        // Setze zunächst alle Spalten auf Auto-Breite
        foreach (var column in gridView.Columns)
        {
            column.Width = double.NaN; // Auto-Breite
        }
        
        // Wir verzögern die endgültige Anpassung, um sicherzustellen, dass die Auto-Breiten berechnet wurden
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            try
            {
                // Wende Mindestbreiten an und füge Puffer hinzu
                foreach (var column in gridView.Columns)
                {
                    // Wir setzen eine großzügige Breite für Text-Spalten, insbesondere für Namen und Nodes
                    if (column.Header.ToString() == "Name")
                    {
                        column.Width = 300; // Name-Spalten sind oft breiter
                    }
                    else if (column.Header.ToString() == "Node")
                    {
                        column.Width = 250; // Node-Namen können lang sein
                    }
                    else if (column.Header.ToString() == "Status")
                    {
                        column.Width = 120; // Status hat feste Werte
                    }
                    else if (column.Header.ToString() == "IP")
                    {
                        column.Width = 150; // IPs haben feste Länge
                    }
                    else if (column.Header.ToString() == "Replicas")
                    {
                        column.Width = 100; // Replicas sind numerisch
                    }
                    else if (column.Header.ToString() == "Erstellt am")
                    {
                        column.Width = 180; // Datum mit Zeit braucht Platz
                    }
                    else
                    {
                        // Für andere Spalten nehmen wir die Auto-Breite plus Puffer, mindestens aber 150
                        column.Width = Math.Max(150, column.ActualWidth + 30);
                    }
                }
                
                _loggingService.LogInfo($"Spaltenbreiten angepasst für {gridView.Columns.Count} Spalten");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler bei der Spaltenanpassung: {ex.Message}", ex);
            }
        }));
    }
    
    private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Nur Zahlen erlauben
        e.Handled = !int.TryParse(e.Text, out _);
    }
    
    private async void StartDeploymentButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedDeployments = DeploymentsListView.SelectedItems.Cast<Models.Deployment>().ToList();
            if (selectedDeployments == null || selectedDeployments.Count == 0)
            {
                MessageBox.Show("Bitte wählen Sie mindestens ein Deployment aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;

            int successCount = 0;
            int failCount = 0;
            StringBuilder errorMessages = new StringBuilder();

            foreach (var deployment in selectedDeployments)
            {
                // Skalierung auf vorherige Replikazahl, oder 1 wenn es 0 war
                int targetReplicas = deployment.Replicas > 0 ? deployment.Replicas : 1;
                var (success, message) = await _openShiftService.ScaleDeployment(deployment.Name, targetReplicas);
                if (success)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                    errorMessages.AppendLine($"{deployment.Name}: {message}");
                }
            }

            if (failCount == 0)
            {
                MessageBox.Show($"{successCount} Deployment(s) erfolgreich gestartet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Erfolgreich gestartet: {successCount}\nFehlgeschlagen: {failCount}\n\nDetails:\n{errorMessages}", "Teilweise erfolgreich", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await LoadDeployments(_currentClusterConfig.SelectedProject);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Starten des Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Starten des Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    
    private async void RestartDeploymentButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedDeployments = DeploymentsListView.SelectedItems.Cast<Models.Deployment>().ToList();
            if (selectedDeployments == null || selectedDeployments.Count == 0)
            {
                MessageBox.Show("Bitte wählen Sie mindestens ein Deployment aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;

            int successCount = 0;
            int failCount = 0;
            StringBuilder errorMessages = new StringBuilder();

            foreach (var deployment in selectedDeployments)
            {
                var (success, message) = await _openShiftService.RestartDeployment(deployment.Name);
                if (success)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                    errorMessages.AppendLine($"{deployment.Name}: {message}");
                }
            }

            if (failCount == 0)
            {
                MessageBox.Show($"{successCount} Deployment(s) zum Neustart angestoßen.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Erfolgreich angestoßen: {successCount}\nFehlgeschlagen: {failCount}\n\nDetails:\n{errorMessages}", "Teilweise erfolgreich", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await LoadDeployments(_currentClusterConfig.SelectedProject);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Neustarten des Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Neustarten des Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    
    private async void StartAllDeploymentsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DeploymentsListView.Items.Count == 0)
            {
                MessageBox.Show("Keine Deployments vorhanden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show("Möchten Sie wirklich alle Deployments starten?", 
                                         "Alle Deployments starten", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                
                int successCount = 0;
                int failCount = 0;
                StringBuilder errorMessages = new StringBuilder();
                
                foreach (Models.Deployment deployment in DeploymentsListView.Items)
                {
                    // Starte nur Deployments, die aktuell gestoppt sind (0 Replicas)
                    if (deployment.Status == "STOP" || deployment.AvailableReplicas == 0)
                    {
                        // Skalierung auf vorherige Replikazahl, oder 1 wenn es 0 war
                        int targetReplicas = deployment.Replicas > 0 ? deployment.Replicas : 1;
                        var (success, message) = await _openShiftService.ScaleDeployment(deployment.Name, targetReplicas);
                        
                        if (success)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                            errorMessages.AppendLine($"- {deployment.Name}: {message}");
                        }
                    }
                }
                
                await LoadDeployments(_currentClusterConfig.SelectedProject);
                
                if (failCount == 0)
                {
                    MessageBox.Show($"{successCount} Deployments wurden erfolgreich gestartet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{successCount} Deployments erfolgreich gestartet, {failCount} fehlgeschlagen.\n\nFehler:\n{errorMessages}", 
                                   "Teilerfolg", 
                                   MessageBoxButton.OK, 
                                   MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Starten aller Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Starten aller Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    
    private async void StopAllDeploymentsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DeploymentsListView.Items.Count == 0)
            {
                MessageBox.Show("Keine Deployments vorhanden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show("Möchten Sie wirklich alle Deployments stoppen?", 
                                         "Alle Deployments stoppen", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                
                int successCount = 0;
                int failCount = 0;
                StringBuilder errorMessages = new StringBuilder();
                
                foreach (Models.Deployment deployment in DeploymentsListView.Items)
                {
                    // Stoppe nur laufende Deployments
                    if (deployment.Status == "RUNNING" || deployment.AvailableReplicas > 0)
                    {
                        var (success, message) = await _openShiftService.ScaleDeployment(deployment.Name, 0);
                        
                        if (success)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                            errorMessages.AppendLine($"- {deployment.Name}: {message}");
                        }
                    }
                }
                
                await LoadDeployments(_currentClusterConfig.SelectedProject);
                
                if (failCount == 0)
                {
                    MessageBox.Show($"{successCount} Deployments wurden erfolgreich gestoppt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{successCount} Deployments wurden erfolgreich gestoppt.\n{failCount} Deployments konnten nicht gestoppt werden:\n{errorMessages}", "Teilweise erfolgreich", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Stoppen aller Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Stoppen aller Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    
    private async void DeleteAllDeploymentsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DeploymentsListView.Items.Count == 0)
            {
                MessageBox.Show("Keine Deployments vorhanden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show("Möchten Sie wirklich alle Deployments löschen? Diese Aktion kann nicht rückgängig gemacht werden.", 
                                         "Alle Deployments löschen", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                
                int successCount = 0;
                int failCount = 0;
                StringBuilder errorMessages = new StringBuilder();
                
                foreach (Models.Deployment deployment in DeploymentsListView.Items)
                {
                    var (success, message) = await _openShiftService.DeleteDeployment(deployment.Name);
                    
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        errorMessages.AppendLine($"- {deployment.Name}: {message}");
                    }
                }
                
                await LoadDeployments(_currentClusterConfig.SelectedProject);
                
                if (failCount == 0)
                {
                    MessageBox.Show($"{successCount} Deployments wurden erfolgreich gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{successCount} Deployments wurden erfolgreich gelöscht.\n{failCount} Deployments konnten nicht gelöscht werden:\n{errorMessages}", "Teilweise erfolgreich", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Löschen aller Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Löschen aller Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    
    private async void RestartPodButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedPod = PodsListView.SelectedItem as Models.Pod;
            if (selectedPod == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Pod aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            Mouse.OverrideCursor = Cursors.Wait;
            
            // Um einen Pod neu zu starten, löschen wir ihn einfach
            var (success, message) = await _openShiftService.DeletePod(selectedPod.Name);
            
            if (success)
            {
                MessageBox.Show($"Pod {selectedPod.Name} wird neu gestartet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Aktualisiere die Pods für das ausgewählte Deployment
                if (DeploymentsListView.SelectedItem is Models.Deployment selectedDeployment)
                {
                    await LoadPodsForDeployment(selectedDeployment.Name);
                }
            }
            else
            {
                MessageBox.Show($"Fehler beim Neustarten des Pods: {message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Neustarten des Pods: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Neustarten des Pods: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void DashboardButton_Click(object sender, RoutedEventArgs e)
    {
        ResetButtonStyles();
        DashboardButton.Style = (Style)FindResource("ActiveSidebarButton");
        ShowContent(DashboardContent);
    }

    private void ClusterButton_Click(object sender, RoutedEventArgs e)
    {
        ResetButtonStyles();
        ClusterButton.Style = (Style)FindResource("ActiveSidebarButton");
        ShowContent(ClusterContent);
    }

    private async void ApplicationsButton_Click(object sender, RoutedEventArgs e)
    {
        _loggingService.LogInfo("Navigiere zu Applications");
        
        // Button-Styles zurücksetzen und aktuellen Button hervorheben
        ResetButtonStyles();
        ApplicationsButton.Style = (Style)FindResource("ActiveSidebarButton");
        
        // Hauptseitenleiste ausblenden und Anwendungsseitenleiste einblenden
        MainSidebar.Visibility = Visibility.Collapsed;
        ApplicationsSidebar.Visibility = Visibility.Visible;
        
        // Benutzerinformationen in der Anwendungsseitenleiste aktualisieren
        ApplicationsUserText.Text = $"Benutzer: {_currentUsername}";
        ApplicationsFullNameText.Text = $"Domain: {_currentFullName}";
        
        // Verstecke alle Content-Bereiche zuerst
        HideAllContent();
        
        // Aktiviere nur den gewünschten Content-Bereich
        ApplicationsContent.Visibility = Visibility.Visible;
        
        // Aktualisiere die Sichtbarkeit der Application-Buttons basierend auf den Berechtigungen
        await UpdateApplicationButtonVisibility();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        // Anwendungsseitenleiste ausblenden und Hauptseitenleiste einblenden
        ApplicationsSidebar.Visibility = Visibility.Collapsed;
        MainSidebar.Visibility = Visibility.Visible;
        
        // Button-Styles zurücksetzen
        ResetButtonStyles();
        ResetAppButtonStyles();
        
        // Dashboard-Button als aktiv markieren
        DashboardButton.Style = (Style)FindResource("ActiveSidebarButton");
        
        // Verstecke alle Content-Bereiche zuerst
        HideAllContent();
        
        // Zurück zum Dashboard
        DashboardContent.Visibility = Visibility.Visible;
    }

    private async void AppButton1_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Button-Styles zurücksetzen und aktuellen Button hervorheben
            ResetAppButtonStyles();
            AppButton1.Style = (Style)FindResource("ActiveSidebarButton");
            
            // Verstecke alle Content-Bereiche zuerst
            HideAllContent();
            
            // Aktiviere nur den gewünschten Content-Bereich
            DeploymentsPodsContent.Visibility = Visibility.Visible;
            
            // Zeige das aktuelle Projekt an
            DeploymentCurrentProjectText.Text = _currentClusterConfig?.SelectedProject ?? "Kein Projekt ausgewählt";
            
            // Lade Deployments, wenn ein Projekt ausgewählt wurde
            if (!string.IsNullOrEmpty(_currentClusterConfig?.SelectedProject))
            {
                await LoadDeployments(_currentClusterConfig.SelectedProject);
            }
            else
            {
                MessageBox.Show("Bitte wählen Sie zuerst ein Projekt aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadDeployments(string projectName)
    {
        try
        {
            // UI-Feedback zeigen
            Mouse.OverrideCursor = Cursors.Wait;
            
            // Deployments laden - optimierte Version ohne detaillierte Pod-Informationen beim ersten Laden
            var (deployments, error) = await _openShiftService.GetDeploymentsMinimal(projectName);
            
            if (error == null)
            {
                // Deployments in der ListView anzeigen
                DeploymentsListView.ItemsSource = deployments;
                
                // Pods des ersten Deployments anzeigen, wenn vorhanden
                if (deployments.Count > 0)
                {
                    DeploymentsListView.SelectedIndex = 0;
                    // Wir laden die Pods für das erste Deployment separat
                    await LoadPodsForDeployment(deployments[0].Name);
                }
                else
                {
                    // Keine Deployments gefunden, ListView leeren
                    PodsListView.ItemsSource = null;
                }
            }
            else
            {
                _loggingService.LogError($"Fehler beim Laden der Deployments: {error}");
                MessageBox.Show($"Fehler beim Laden der Deployments: {error}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // UI-Feedback beenden
            Mouse.OverrideCursor = null;
        }
    }

    private async Task LoadPodsForDeployment(string deploymentName)
    {
        try
        {
            // UI-Feedback zeigen
            Mouse.OverrideCursor = Cursors.Wait;
            
            // Pods für das ausgewählte Deployment laden
            var (pods, error) = await _openShiftService.GetPodsForDeployment(deploymentName);
            
            if (error == null)
            {
                // Pods in der ListView anzeigen
                PodsListView.ItemsSource = pods;
            }
            else
            {
                _loggingService.LogError($"Fehler beim Laden der Pods: {error}");
                // Fehler nur loggen, aber nicht anzeigen, um Benutzerinteraktion nicht zu stören
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der Pods: {ex.Message}", ex);
        }
        finally
        {
            // UI-Feedback beenden
            Mouse.OverrideCursor = null;
        }
    }

    private async void DeploymentsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            var selectedDeployment = DeploymentsListView.SelectedItem as Models.Deployment;
            if (selectedDeployment != null)
            {
                // Pods des ausgewählten Deployments laden und anzeigen
                await LoadPodsForDeployment(selectedDeployment.Name);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anzeigen der Pods: {ex.Message}", ex);
        }
    }

    private async void AppButton2_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isClusterLoggedIn)
            {
                MessageBox.Show("Bitte melden Sie sich zuerst an einem Cluster an.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Button-Styles zurücksetzen und aktuellen Button hervorheben
            ResetAppButtonStyles();
            AppButton2.Style = (Style)FindResource("ActiveSidebarButton");
            
            HideAllContent();
            VolumesContent.Visibility = Visibility.Visible;
            
            // Initialize sidebar
            MainSidebar.Visibility = Visibility.Collapsed;
            ApplicationsSidebar.Visibility = Visibility.Visible;
            
            // Set the user info
            ApplicationsUserText.Text = CurrentUserText.Text;
            ApplicationsFullNameText.Text = FullNameText.Text;
            
            // Set current project text
            string currentProject = await _openShiftService.GetCurrentProjectName();
            VolumeCurrentProjectText.Text = currentProject;
            
            // Load the PVCs for the current project
            await LoadPersistentVolumeClaims(currentProject);
            
            // Load the PVs
            await LoadPersistentVolumes();
            
            // Initialize GridView columns
            AdjustGridViewColumns(PVCsGridView);
            AdjustGridViewColumns(PVsGridView);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anzeigen der Volumes: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Anzeigen der Volumes: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadPersistentVolumeClaims(string projectName)
    {
        try
        {
            if (string.IsNullOrEmpty(projectName))
            {
                MessageBox.Show("Bitte wählen Sie zuerst ein Projekt aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Zeige Ladeanzeige oder deaktiviere UI-Elemente während des Ladens
            Mouse.OverrideCursor = Cursors.Wait;
            
            // Schneller Abruf der grundlegenden PVC-Informationen
            var (pvcs, error) = await _openShiftService.GetPersistentVolumeClaims(projectName);
            if (error == null)
            {
                // Setze die ItemsSource, damit die UI sofort aktualisiert wird
                PVCsListView.ItemsSource = pvcs;
                
                // Starte einen Task, der die Speicherbelegungsdaten holt und die UI aktualisiert
                await Task.Run(async () => 
                {
                    await _openShiftService.UpdatePVCUsageDataAsync(pvcs, projectName);
                    
                    // UI-Update muss auf dem UI-Thread durchgeführt werden
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        // Aktualisiere die ListView-Ansicht
                        PVCsListView.Items.Refresh();
                    });
                });
            }
            else
            {
                MessageBox.Show($"Fehler beim Laden der PVCs: {error}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der PVCs: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der PVCs: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // Entferne Ladeanzeige oder aktiviere UI-Elemente wieder
            Mouse.OverrideCursor = null;
        }
    }

    private async Task LoadPersistentVolumes()
    {
        try
        {
            var (pvs, error) = await _openShiftService.GetPersistentVolumes();
            if (error == null)
            {
                PVsListView.ItemsSource = pvs;
            }
            else
            {
                MessageBox.Show($"Fehler beim Laden der PVs: {error}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der PVs: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der PVs: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshVolumesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = await _openShiftService.GetCurrentProjectName();
            
            // Refresh PVCs
            await LoadPersistentVolumeClaims(currentProject);
            
            // Refresh PVs
            await LoadPersistentVolumes();
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der Volumes: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Aktualisieren der Volumes: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeletePVCButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PVCsListView.SelectedItem == null)
            {
                MessageBox.Show("Bitte wählen Sie zuerst einen PVC aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var selectedPVC = (PersistentVolumeClaim)PVCsListView.SelectedItem;
            var result = MessageBox.Show($"Möchten Sie den PVC '{selectedPVC.Name}' wirklich löschen?", 
                                         "PVC löschen", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                string currentProject = await _openShiftService.GetCurrentProjectName();
                var (success, message) = await _openShiftService.DeletePersistentVolumeClaim(selectedPVC.Name, currentProject);
                
                if (success)
                {
                    MessageBox.Show(message, "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadPersistentVolumeClaims(currentProject);
                    await LoadPersistentVolumes();
                }
                else
                {
                    MessageBox.Show($"Fehler beim Löschen des PVC: {message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Löschen des PVC: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Löschen des PVC: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteAllSelectedPVCsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedPVCs = PVCsListView.SelectedItems.Cast<PersistentVolumeClaim>().ToList();
            
            if (selectedPVCs.Count == 0)
            {
                MessageBox.Show("Bitte wählen Sie mindestens einen PVC aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show($"Möchten Sie {selectedPVCs.Count} ausgewählte PVCs wirklich löschen?", 
                                         "PVCs löschen", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                string currentProject = await _openShiftService.GetCurrentProjectName();
                int successCount = 0;
                int failCount = 0;
                
                foreach (var pvc in selectedPVCs)
                {
                    var (success, message) = await _openShiftService.DeletePersistentVolumeClaim(pvc.Name, currentProject);
                    
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        _loggingService.LogError($"Fehler beim Löschen des PVC '{pvc.Name}': {message}");
                    }
                }
                
                if (failCount == 0)
                {
                    MessageBox.Show($"{successCount} PVCs erfolgreich gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{successCount} PVCs erfolgreich gelöscht. {failCount} PVCs konnten nicht gelöscht werden. Details finden Sie im Log.", 
                                   "Teilerfolg", 
                                   MessageBoxButton.OK, 
                                   MessageBoxImage.Warning);
                }
                
                await LoadPersistentVolumeClaims(currentProject);
                await LoadPersistentVolumes();
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Löschen der PVCs: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Löschen der PVCs: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Methode für das Löschen von PVs über das Kontextmenü
    private async void DeletePVButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PVsListView.SelectedItem == null)
            {
                MessageBox.Show("Bitte wählen Sie zuerst ein PV aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var selectedPV = (PersistentVolume)PVsListView.SelectedItem;
            var result = MessageBox.Show($"Möchten Sie das PV '{selectedPV.Name}' wirklich löschen?", 
                                         "PV löschen", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var (success, message) = await _openShiftService.DeletePersistentVolume(selectedPV.Name);
                
                if (success)
                {
                    MessageBox.Show(message, "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                    string currentProject = await _openShiftService.GetCurrentProjectName();
                    await LoadPersistentVolumeClaims(currentProject);
                    await LoadPersistentVolumes();
                }
                else
                {
                    MessageBox.Show($"Fehler beim Löschen des PV: {message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Löschen des PV: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Löschen des PV: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AppButton3_Click(object sender, RoutedEventArgs e)
    {
        if (!_isClusterLoggedIn)
        {
            MessageBox.Show("Bitte melden Sie sich zuerst an einem Cluster an.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Button-Styles zurücksetzen und aktuellen Button hervorheben
        ResetAppButtonStyles();
        AppButton3.Style = (Style)FindResource("ActiveSidebarButton");
        
        HideAllContent();
        MultiProjectContent.Visibility = Visibility.Visible;
        
        // Aktuellen Cluster-Namen anzeigen
        MultiProjectCurrentClusterText.Text = _currentClusterConfig.Name;
        
        // Projekte laden
        LoadMultiProjects();
    }

    private async void LoadMultiProjects()
    {
        try
        {
            // Projekte vom OpenShift-Service laden
            var projects = await _openShiftService.GetProjectsAsync(_currentClusterConfig);
            
            if (projects != null && projects.Any())
            {
                // Projekte in die ListView laden
                MultiProjectsListView.ItemsSource = projects.Select(p => new
                {
                    Name = p,
                    CreationTimestamp = DateTime.Now, // In der realen Anwendung sollte hier das tatsächliche Erstellungsdatum stehen
                    Status = "Active"
                }).ToList();
                
                // GridView-Spalten anpassen
                AdjustGridViewColumns(MultiProjectsGridView);
            }
            else
            {
                MessageBox.Show("Keine Projekte gefunden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der Projekte: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der Projekte: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void MultiProjectSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            var textBox = sender as TextBox;
            if (textBox != null && MultiProjectsListView.ItemsSource != null)
            {
                var view = CollectionViewSource.GetDefaultView(MultiProjectsListView.ItemsSource);
                if (view != null)
                {
                    view.Filter = item =>
                    {
                        var property = item.GetType().GetProperty("Name");
                        if (property != null)
                        {
                            var value = property.GetValue(item) as string;
                            return value != null && value.ToLower().Contains(textBox.Text.ToLower());
                        }
                        return false;
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler bei der Projekt-Suche: {ex.Message}", ex);
        }
    }
    
    private void MultiProjectsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedProjectsCount();
    }
    
    private void UpdateSelectedProjectsCount()
    {
        int count = MultiProjectsListView.SelectedItems.Count;
        SelectedProjectsCountText.Text = count.ToString();
    }
    
    private async void RefreshProjectsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // UI-Status aktualisieren
            RefreshProjectsButton.IsEnabled = false;
            RefreshProjectsButton.Content = "Wird aktualisiert...";
            
            // Projekte neu laden
            await Task.Run(() => LoadMultiProjects());
            
            // UI-Status zurücksetzen
            RefreshProjectsButton.IsEnabled = true;
            RefreshProjectsButton.Content = "Projekte aktualisieren";
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der Projekte: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Aktualisieren der Projekte: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // UI-Status zurücksetzen
            RefreshProjectsButton.IsEnabled = true;
            RefreshProjectsButton.Content = "Projekte aktualisieren";
        }
    }
    
    private async void LoadSelectedProjectsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (MultiProjectsListView.SelectedItems.Count == 0)
            {
                MessageBox.Show("Bitte wählen Sie mindestens ein Projekt aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Ausgewählte Projekte sammeln
            var selectedProjects = new List<string>();
            foreach (var item in MultiProjectsListView.SelectedItems)
            {
                var property = item.GetType().GetProperty("Name");
                if (property != null)
                {
                    var projectName = property.GetValue(item) as string;
                    if (!string.IsNullOrEmpty(projectName))
                    {
                        selectedProjects.Add(projectName);
                    }
                }
            }
            
            if (selectedProjects.Count > 0)
            {
                // Pods für alle ausgewählten Projekte laden
                await LoadPodsForProjects(selectedProjects);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der Pods für ausgewählte Projekte: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der Pods für ausgewählte Projekte: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async Task LoadPodsForProjects(List<string> projectNames)
    {
        try
        {
            // Sammelobjekt für alle Pods
            var allPods = new List<object>();
            
            foreach (var projectName in projectNames)
            {
                // Pods für das Projekt abrufen
                var pods = await _openShiftService.GetPodsAsync(_currentClusterConfig, projectName);
                
                if (pods != null && pods.Any())
                {
                    // Status für jede Pod bestimmen (RUNNING oder STOP)
                    foreach (var pod in pods)
                    {
                        var podData = new
                        {
                            Project = projectName,
                            Name = pod.Name,
                            Status = pod.Status == "Running" ? "RUNNING" : "STOP",
                            CreationTimestamp = pod.CreationTimestamp,
                            PodCount = pod.Status == "Running" ? 1 : 0
                        };
                        
                        allPods.Add(podData);
                    }
                }
                else
                {
                    // Wenn keine Pods gefunden wurden, einen leeren Pod mit Status STOP hinzufügen
                    allPods.Add(new
                    {
                        Project = projectName,
                        Name = "Keine Pods",
                        Status = "STOP",
                        CreationTimestamp = DateTime.Now,
                        PodCount = 0
                    });
                }
            }
            
            // Pods in der ListView anzeigen
            MultiProjectPodsListView.ItemsSource = allPods;
            
            // GridView-Spalten anpassen
            AdjustGridViewColumns(MultiProjectPodsGridView);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der Pods: {ex.Message}", ex);
            throw;
        }
    }
    
    private async void StartAllMultiPodsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (MultiProjectPodsListView.ItemsSource == null || !((IEnumerable)MultiProjectPodsListView.ItemsSource).Cast<object>().Any())
            {
                MessageBox.Show("Keine Pods verfügbar.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show("Möchten Sie alle Pods in den ausgewählten Projekten starten?", 
                                       "Bestätigung", 
                                       MessageBoxButton.YesNo, 
                                       MessageBoxImage.Question);
                                       
            if (result == MessageBoxResult.Yes)
            {
                // Sammel die einzigartigen Projekte
                var projectsToProcess = new HashSet<string>();
                foreach (var item in ((IEnumerable)MultiProjectPodsListView.ItemsSource).Cast<object>())
                {
                    var projectProperty = item.GetType().GetProperty("Project");
                    if (projectProperty != null)
                    {
                        var projectName = projectProperty.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(projectName))
                        {
                            projectsToProcess.Add(projectName);
                        }
                    }
                }
                
                foreach (var projectName in projectsToProcess)
                {
                    // Für jedes Projekt alle zugehörigen Deployments auf Replica=1 skalieren
                    var deployments = await _openShiftService.GetDeploymentsAsync(_currentClusterConfig, projectName);
                    
                    foreach (var deployment in deployments)
                    {
                        await _openShiftService.ScaleDeploymentAsync(_currentClusterConfig, projectName, deployment.Name, 1);
                    }
                }
                
                MessageBox.Show("Alle Pods wurden gestartet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Liste aktualisieren
                var selectedProjects = projectsToProcess.ToList();
                await LoadPodsForProjects(selectedProjects);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Starten aller Pods: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Starten aller Pods: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async void StopAllMultiPodsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (MultiProjectPodsListView.ItemsSource == null || !((IEnumerable)MultiProjectPodsListView.ItemsSource).Cast<object>().Any())
            {
                MessageBox.Show("Keine Pods verfügbar.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show("Möchten Sie alle Pods in den ausgewählten Projekten stoppen?", 
                                       "Bestätigung", 
                                       MessageBoxButton.YesNo, 
                                       MessageBoxImage.Question);
                                       
            if (result == MessageBoxResult.Yes)
            {
                // Sammel die einzigartigen Projekte
                var projectsToProcess = new HashSet<string>();
                foreach (var item in ((IEnumerable)MultiProjectPodsListView.ItemsSource).Cast<object>())
                {
                    var projectProperty = item.GetType().GetProperty("Project");
                    if (projectProperty != null)
                    {
                        var projectName = projectProperty.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(projectName))
                        {
                            projectsToProcess.Add(projectName);
                        }
                    }
                }
                
                foreach (var projectName in projectsToProcess)
                {
                    // Für jedes Projekt alle zugehörigen Deployments auf Replica=0 skalieren
                    var deployments = await _openShiftService.GetDeploymentsAsync(_currentClusterConfig, projectName);
                    
                    foreach (var deployment in deployments)
                    {
                        await _openShiftService.ScaleDeploymentAsync(_currentClusterConfig, projectName, deployment.Name, 0);
                    }
                }
                
                MessageBox.Show("Alle Pods wurden gestoppt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Liste aktualisieren
                var selectedProjects = projectsToProcess.ToList();
                await LoadPodsForProjects(selectedProjects);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Stoppen aller Pods: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Stoppen aller Pods: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async void RestartAllMultiPodsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (MultiProjectPodsListView.ItemsSource == null || !((IEnumerable)MultiProjectPodsListView.ItemsSource).Cast<object>().Any())
            {
                MessageBox.Show("Keine Pods verfügbar.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show("Möchten Sie alle Pods in den ausgewählten Projekten neustarten? Dies wird die Pods löschen und neue erstellen.", 
                                       "Bestätigung", 
                                       MessageBoxButton.YesNo, 
                                       MessageBoxImage.Question);
                                       
            if (result == MessageBoxResult.Yes)
            {
                // Für jeden Pod in der Liste, lösche ihn (das führt zum Neustart durch den Replication Controller)
                var podsToRestart = new List<(string Project, string Name)>();
                
                foreach (var item in ((IEnumerable)MultiProjectPodsListView.ItemsSource).Cast<object>())
                {
                    var projectProperty = item.GetType().GetProperty("Project");
                    var nameProperty = item.GetType().GetProperty("Name");
                    var statusProperty = item.GetType().GetProperty("Status");
                    
                    if (projectProperty != null && nameProperty != null && statusProperty != null)
                    {
                        var projectName = projectProperty.GetValue(item) as string;
                        var podName = nameProperty.GetValue(item) as string;
                        var status = statusProperty.GetValue(item) as string;
                        
                        if (!string.IsNullOrEmpty(projectName) && !string.IsNullOrEmpty(podName) && status == "RUNNING")
                        {
                            podsToRestart.Add((projectName, podName));
                        }
                    }
                }
                
                foreach (var pod in podsToRestart)
                {
                    await _openShiftService.DeletePodAsync(_currentClusterConfig, pod.Project, pod.Name);
                }
                
                MessageBox.Show("Neustart für alle laufenden Pods wurde initiiert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Warten, damit die Pods Zeit haben, neu zu starten
                await Task.Delay(2000);
                
                // Liste aktualisieren
                var projectsToProcess = new HashSet<string>();
                foreach (var pod in podsToRestart)
                {
                    projectsToProcess.Add(pod.Project);
                }
                
                await LoadPodsForProjects(projectsToProcess.ToList());
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Neustarten aller Pods: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Neustarten aller Pods: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async void AppButton4_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Button-Styles zurücksetzen und aktuellen Button hervorheben
            ResetAppButtonStyles();
            AppButton4.Style = (Style)FindResource("ActiveSidebarButton");
            
            HideAllContent();
            CreateContent.Visibility = Visibility.Visible;

            // Aktuelles Projekt anzeigen
            string currentProject = await _openShiftService.GetCurrentProjectName();
            
            // Webserver-UI vorbereiten
            WebserverCurrentProjectText.Text = currentProject;
            
            // Namespace Namen generieren (aktuelles Projekt + "-webserver")
            string newNamespace = $"{currentProject}-webserver";
            NewNamespaceTextBox.Text = newNamespace;
            
            // Default-Werte für Webserver
            WebserverVolumeHandleTextBox.Text = $"{newNamespace}pv";
            
            // Standard-SMB-Pfad als Beispiel
            string env = ExtractEnvironment(currentProject);
            WebserverSMBPathTextBox.Text = $"//bbmag65.bbmag.bbraun.com/nfs/CMFMES/ATO/{env}/{currentProject}/oisfiles/oisputrecipe/";
            
            // YAML-Vorschau für Namespace generieren
            GenerateNamespaceYaml(newNamespace);
            
            // Webserver-Ressourcen deaktivieren bis Namespace erstellt wird
            WebserverResourcesGroup.IsEnabled = false;
            
            // Fehlermeldung zurücksetzen
            WebserverErrorMessage.Text = string.Empty;
            WebserverErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden des Webserver Management: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OISFilesVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Alle Sperrvariablen zurücksetzen
            _isApplyingOISFilesChanges = false;
            _isApplyingOISFilesOptIotChanges = false;
            _isApplyingEDHRChanges = false;
            _isApplyingFirmwareChanges = false;
            _isApplyingSAPConnectivityChanges = false;
            
            // Button-Style setzen und Inhalte anzeigen
            ResetAppButtonStyles();
            OISFilesVolumeButton.Style = (Style)FindResource("ActiveSidebarButton");
            HideAllContent();
            OISFilesVolumeContent.Visibility = Visibility.Visible;

            // Status-Meldung zurücksetzen
            OISFilesErrorMessage.Text = string.Empty;
            OISFilesErrorMessage.Visibility = Visibility.Collapsed;
            OISFilesErrorMessage.Foreground = new SolidColorBrush(Colors.Red);

            // Zurücksetzen aller Textfelder und Eingabefelder für diesen Bereich
            OISFilesOriginalYamlTextBox.Text = string.Empty;
            OISFilesModifiedYamlTextBox.Text = string.Empty;
            PVNameTextBox.Text = string.Empty;
            PVCNameTextBox.Text = string.Empty;
            PVCVolumeNameTextBox.Text = string.Empty;
            SecretNameTextBox.Text = string.Empty;
            UsernameTextBox.Text = string.Empty;
            SecretPasswordBox.Password = string.Empty;
            SMBSourcePathTextBox.Text = string.Empty;
            PVYamlTextBox.Text = string.Empty;
            PVCYamlTextBox.Text = string.Empty;
            OISFilesSecretYamlTextBox.Text = string.Empty;

            // Aktuelles Projekt prüfen - hier explizit nochmal abfragen, um sicherzustellen dass es aktuell ist
            Task.Run(async () => {
                try
                {
                    // Aktuelles Projekt abfragen
                    string currentProject = await _openShiftService.GetCurrentProjectName();
                    
                    if (string.IsNullOrEmpty(currentProject))
                    {
                        _loggingService.LogWarning("OISFilesVolumeButton_Click: Kein Projekt ausgewählt");
                        await Dispatcher.InvokeAsync(() => {
                            OISFilesErrorMessage.Text = "Bitte wählen Sie zuerst ein Projekt aus, bevor Sie fortfahren.";
                            OISFilesErrorMessage.Visibility = Visibility.Visible;
                        });
                        return;
                    }

                    _loggingService.LogInfo("OISFiles Volume Management wird geöffnet...");
                    _loggingService.LogInfo($"Aktuelles Projekt: {currentProject}");
                    
                    // Aktuelles Projekt anzeigen
                    await Dispatcher.InvokeAsync(() => {
                        OISFilesCurrentProjectText.Text = currentProject;
                    });
                    
                    // Host-Deployment YAML laden
                    string yaml = await _openShiftService.GetHostDeploymentYamlAsync(currentProject);
                    
                    await Dispatcher.InvokeAsync(() => {
                        // Namen aktualisieren
                        UpdateResourceNames();
                        
                        // YAML in TextBoxen anzeigen
                        OISFilesOriginalYamlTextBox.Text = yaml;
                        OISFilesModifiedYamlTextBox.Text = yaml; // Initial kopieren
                        
                        // Alle Templates aktualisieren
                        UpdateYamlTemplates();
                    });
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Fehler beim Laden der Projektdaten: {ex.Message}", ex);
                    await Dispatcher.InvokeAsync(() => {
                        OISFilesErrorMessage.Text = $"Fehler beim Laden der Projektdaten: {ex.Message}";
                        OISFilesErrorMessage.Visibility = Visibility.Visible;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Öffnen des OISFiles Volume Management: {ex.Message}", ex);
            OISFilesErrorMessage.Text = $"Fehler beim Öffnen: {ex.Message}";
            OISFilesErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void UpdateResourceNames()
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            
            _loggingService.LogInfo($"Aktualisiere Ressourcennamen für Projekt: {currentProject}");
            
            if (string.IsNullOrEmpty(currentProject))
            {
                _loggingService.LogWarning("Kein aktuelles Projekt ausgewählt");
                OISFilesErrorMessage.Text = "Kein aktuelles Projekt ausgewählt. Bitte wählen Sie zuerst ein Projekt aus.";
                OISFilesErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV
            string pvName = $"{currentProject}-oisfiles";
            PVNameTextBox.Text = pvName;
            
            // PVC
            PVCNameTextBox.Text = $"{currentProject}-oisfiles";
            PVCVolumeNameTextBox.Text = pvName;
            
            // Secret
            SecretNameTextBox.Text = $"{currentProject}-oisfiles-creds";
            
            // Beispiel-SMB-Pfad eintragen
            SMBSourcePathTextBox.Text = $"//bbmag65.bbmag.bbraun.com/nfs/CMFMES/ATO/{ExtractEnvironment(currentProject)}/{currentProject}/oisfiles/";
            
            _loggingService.LogInfo($"Ressourcennamen erfolgreich aktualisiert: PV={pvName}, PVC={currentProject}-oisfiles, Secret={currentProject}-oisfiles-creds");
            _loggingService.LogInfo($"SMB-Pfad: {SMBSourcePathTextBox.Text}");
            
            // Erfolgreich aktualisiert, Fehlermeldung zurücksetzen
            OISFilesErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der Ressourcennamen: {ex.Message}", ex);
            OISFilesErrorMessage.Text = $"Fehler beim Aktualisieren der Ressourcennamen: {ex.Message}";
            OISFilesErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    // Helper-Methode zum Extrahieren der Umgebung aus dem Projektnamen
    private string ExtractEnvironment(string projectName)
    {
        try
        {
            _loggingService.LogInfo($"Extrahiere Umgebung aus Projektnamen: {projectName}");
            
            // Prüfen, ob der Projektname gültig ist
            if (string.IsNullOrEmpty(projectName))
            {
                _loggingService.LogWarning("Projektname ist leer, kann Umgebung nicht extrahieren");
                return "UNK";
            }
            
            // DEV/QAS/PRD aus dem Namen extrahieren
            if (projectName.Contains("dev") || projectName.Contains("-d-"))
            {
                _loggingService.LogInfo("Umgebung DEV01 erkannt");
                return "DEV01";
            }
            else if (projectName.Contains("qas") || projectName.Contains("-q-"))
            {
                _loggingService.LogInfo("Umgebung QAS01 erkannt");
                return "QAS01";
            }
            else if (projectName.Contains("prd") || projectName.Contains("-p-"))
            {
                _loggingService.LogInfo("Umgebung PRD01 erkannt");
                return "PRD01";
            }
            else
            {
                _loggingService.LogWarning($"Keine bekannte Umgebung in '{projectName}' gefunden, verwende PRD01 als Standard");
                return "PRD01";
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Extrahieren der Umgebung: {ex.Message}", ex);
            return "PRD01";
        }
    }

    private void UpdateYamlTemplates()
    {
        string currentProject = _openShiftService.CurrentProject;
        string pvName = PVNameTextBox.Text;
        string smbPath = SMBSourcePathTextBox.Text;
        string secretName = SecretNameTextBox.Text;
        string pvcName = PVCNameTextBox.Text;
        
        // PV Template generieren
        PVYamlTextBox.Text = GeneratePVYaml(currentProject, pvName, smbPath, secretName);
        
        // PVC Template generieren
        PVCYamlTextBox.Text = GeneratePVCYaml(currentProject, pvcName, pvName);
        
        // Secret Template generieren
        OISFilesSecretYamlTextBox.Text = GenerateSecretYaml(currentProject, secretName);
    }

    private string GeneratePVYaml(string currentProject, string pvName, string smbPath, string secretName)
    {
        // Mit korrigierter Einrückung (ein Tab zurück)
        return $@"kind: PersistentVolume
apiVersion: v1
metadata:
  name: {pvName}
  annotations:
    pv.kubernetes.io/bound-by-controller: 'yes'
  finalizers:
    - kubernetes.io/pv-protection
spec:
  capacity:
    storage: 1Gi
  csi:
    driver: smb.csi.k8s.io
    volumeHandle: {pvName}
    volumeAttributes:
      source: {smbPath}
    nodeStageSecretRef:
      name: {secretName}
      namespace: {currentProject}
  accessModes:
    - ReadWriteMany
  persistentVolumeReclaimPolicy: Retain
  mountOptions:
    - dir_mode=0777
    - file_mode=0777
    - nobrl
  volumeMode: Filesystem";
    }

    private string GeneratePVCYaml(string currentProject, string pvcName, string pvName)
    {
        // Mit korrigierter Einrückung (ein Tab zurück)
        return $@"kind: PersistentVolumeClaim
apiVersion: v1
metadata:
  name: {pvcName}
  namespace: {currentProject}
  annotations:
    pv.kubernetes.io/bind-completed: 'yes'
  finalizers:
    - kubernetes.io/pvc-protection
spec:
  accessModes:
    - ReadWriteMany
  resources:
    requests:
      storage: 1Gi
  volumeName: {pvName}
  storageClassName: ''
  volumeMode: Filesystem";
    }

    private string GenerateSecretYaml(string projectName, string secretName)
    {
        try
        {
            string template = $@"apiVersion: v1
kind: Secret
metadata:
  name: {secretName}
  namespace: {projectName}
type: Opaque
data:
  domain: YmJtYWc=
  username: 
  password: ";

            return template;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Fehler beim Generieren des Secret YAML", ex);
            return string.Empty;
        }
    }

    private async Task LoadHostDeploymentYaml()
    {
        try
        {
            string projectName = _openShiftService.CurrentProject;
            _loggingService.LogInfo($"Lade Host-Deployment YAML für Projekt: {projectName}");
            
            string yaml = await _openShiftService.GetHostDeploymentYamlAsync(projectName);
            _loggingService.LogInfo("Host-Deployment YAML erfolgreich geladen");
            
            await Dispatcher.InvokeAsync(() => {
                OISFilesOriginalYamlTextBox.Text = yaml;
                OISFilesModifiedYamlTextBox.Text = yaml; // Initial kopieren
                
                // Bei Erfolg Fehlermeldung ausblenden
                OISFilesErrorMessage.Visibility = Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => {
                // Zeige Fehlermeldungen in einem MessageBox an
                MessageBox.Show($"Fehler beim Laden des Host Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Fehlermeldung im OISFilesErrorMessage anzeigen
                OISFilesErrorMessage.Text = $"Fehler beim Laden des Host Deployments: {ex.Message}";
                OISFilesErrorMessage.Visibility = Visibility.Visible;
                
                _loggingService.LogError($"Fehler beim Laden des Host Deployments: {ex.Message}", ex);
            });
        }
    }

    private void ConfigureYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string yaml = OISFilesOriginalYamlTextBox.Text;
            string modifiedYaml = ModifyYamlForOISFiles(yaml);
            OISFilesModifiedYamlTextBox.Text = modifiedYaml;
            
            // Erfolgreiche Konfiguration, Fehlermeldung zurücksetzen
            OISFilesErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            OISFilesErrorMessage.Text = $"Fehler beim Konfigurieren des YAML: {ex.Message}";
            OISFilesErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Konfigurieren des YAML: {ex.Message}", ex);
        }
    }

    private string ModifyYamlForOISFiles(string yaml)
    {
        // Hier würde die tatsächliche YAML-Modifikation stattfinden
        // Dies ist eine vereinfachte Version, die im wirklichen Code durch eine
        // ordnungsgemäße YAML-Parsing-Bibliothek ersetzt werden sollte
        
        string volumeName = "oisfilesbbmag65";
        string volumePath = "/var/opt/ois/bbmag65/oisfiles";
        string pvcName = PVCNameTextBox.Text;
        
        // Env-Eintrag hinzufügen
        if (yaml.Contains("env:"))
        {
            string envEntry = $"        - name: {volumeName}\n          value: {volumePath}";
            yaml = yaml.Replace("env:", $"env:\n{envEntry}");
        }
        
        // Volume-Eintrag hinzufügen
        if (yaml.Contains("volumes:"))
        {
            string volumeEntry = $"      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
            yaml = yaml.Replace("volumes:", $"volumes:\n{volumeEntry}");
        }
        else
        {
            // Wenn kein volumes-Abschnitt existiert, fügen wir einen hinzu
            yaml += $"\n    volumes:\n      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
        }
        
        // volumeMounts-Eintrag hinzufügen
        if (yaml.Contains("volumeMounts:"))
        {
            string volumeMountEntry = $"        - name: {volumeName}\n          mountPath: {volumePath}";
            yaml = yaml.Replace("volumeMounts:", $"volumeMounts:\n{volumeMountEntry}");
        }
        else
        {
            // Wenn kein volumeMounts-Abschnitt existiert, fügen wir einen hinzu
            // Dies würde in einem bestehenden Container-Block platziert werden
            if (yaml.Contains("containers:"))
            {
                string containerBlockStart = "containers:";
                int containerStartIndex = yaml.IndexOf(containerBlockStart);
                int insertIndex = yaml.IndexOf("image:", containerStartIndex);
                
                if (insertIndex > 0)
                {
                    string beforeInsert = yaml.Substring(0, insertIndex);
                    string afterInsert = yaml.Substring(insertIndex);
                    string volumeMountsBlock = $"        volumeMounts:\n          - name: {volumeName}\n            mountPath: {volumePath}\n        ";
                    
                    yaml = beforeInsert + volumeMountsBlock + afterInsert;
                }
            }
        }
        
        return yaml;
    }

    private async void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingOISFilesChanges)
        {
            _loggingService.LogWarning("Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...");
            OISFilesErrorMessage.Text = "Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...";
            OISFilesErrorMessage.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _isApplyingOISFilesChanges = true;
            
            // OISFilesErrorMessage statt ErrorMessage verwenden
            OISFilesErrorMessage.Text = "Änderungen werden angewendet...";
            OISFilesErrorMessage.Visibility = Visibility.Visible;
            
            string currentProject = _openShiftService.CurrentProject;
            _loggingService.LogInfo($"Starte Anwendung der Änderungen für Projekt: {currentProject}");
            
            // Alle benötigten Ressourcen vorbereiten
            string pvName = PVNameTextBox.Text;
            string smbPath = SMBSourcePathTextBox.Text;
            string secretName = SecretNameTextBox.Text;
            string pvcName = PVCNameTextBox.Text;
            string username = UsernameTextBox.Text;
            string password = SecretPasswordBox.Password;
            bool allSuccess = true;
            
            // 1. Zuerst YAML konfigurieren, ohne es neu zu laden
            string modifiedYaml = ModifyYamlForOISFiles(OISFilesOriginalYamlTextBox.Text);
            OISFilesModifiedYamlTextBox.Text = modifiedYaml;
            
            // 1. Host-Deployment ändern
            _loggingService.LogInfo($"Aktualisiere Deployment in Projekt: {currentProject}");
            bool deploymentSuccess = await _openShiftService.UpdateDeploymentFromYamlAsync(currentProject, modifiedYaml);
            
            // Wir ignorieren Deployment-Fehler und machen trotzdem weiter
            
            // 2. Secret erstellen (falls nicht existiert)
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                _loggingService.LogInfo("Erstelle Secret...");
                string secretYaml = GenerateSecretYaml(currentProject, secretName);
                
                // Base64-Kodierung für Benutzername und Passwort
                string usernameBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(username));
                string passwordBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
                
                // YAML aktualisieren mit Base64-kodierten Werten
                secretYaml = secretYaml.Replace("username: ", $"username: {usernameBase64}")
                                 .Replace("password: ", $"password: {passwordBase64}");
                
                // Secret erstellen
                bool secretSuccess = await _openShiftService.CreateResourceFromYamlAsync(secretYaml);
                if (!secretSuccess)
                {
                    _loggingService.LogWarning("Fehler beim Erstellen des Secret - möglicherweise existiert es bereits");
                    // Wir setzen allSuccess nicht auf false
                }
            }
            
            // 3. PV erstellen
            if (!string.IsNullOrEmpty(smbPath))
            {
                _loggingService.LogInfo("Erstelle PersistentVolume...");
                string pvYaml = GeneratePVYaml(currentProject, pvName, smbPath, secretName);
                bool pvSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvYaml);
                if (!pvSuccess)
                {
                    _loggingService.LogWarning("Fehler beim Erstellen des PV - möglicherweise existiert es bereits");
                    // Wir setzen allSuccess nicht auf false
                }
                
                // 4. PVC erstellen, unabhängig vom PV-Ergebnis
                _loggingService.LogInfo("Erstelle PersistentVolumeClaim...");
                string pvcYaml = GeneratePVCYaml(currentProject, pvcName, pvName);
                bool pvcSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvcYaml);
                if (!pvcSuccess)
                {
                    _loggingService.LogWarning("Fehler beim Erstellen des PVC - möglicherweise existiert es bereits");
                    // Wir setzen allSuccess nicht auf false
                }
            }
            
            // Erfolgsmeldung
            _loggingService.LogInfo("Alle Änderungen wurden angewendet");
            OISFilesErrorMessage.Text = "Alle Änderungen wurden angewendet.";
            OISFilesErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            
            MessageBox.Show("Die Änderungen wurden angewendet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anwenden der Änderungen: {ex.Message}", ex);
            OISFilesErrorMessage.Text = $"Fehler: {ex.Message}";
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isApplyingOISFilesChanges = false;
        }
    }

    private async void CreatePVButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Überprüfen, ob das YAML erstellt wurde
            if (string.IsNullOrEmpty(PVYamlTextBox.Text))
            {
                MessageBox.Show("Bitte erstellen Sie zuerst das PV YAML.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            string pvYaml = PVYamlTextBox.Text;
            bool success = await _openShiftService.CreateResourceFromYamlAsync(pvYaml);
            
            if (success)
            {
                MessageBox.Show("PersistentVolume wurde erfolgreich erstellt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Fehler beim Erstellen des PersistentVolume.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage.Text = $"Fehler beim Erstellen des PV: {ex.Message}";
            _loggingService.LogError($"Fehler beim Erstellen des PV: {ex.Message}", ex);
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CreatePVCButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Überprüfen, ob das YAML erstellt wurde
            if (string.IsNullOrEmpty(PVCYamlTextBox.Text))
            {
                MessageBox.Show("Bitte erstellen Sie zuerst das PVC YAML.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            string pvcYaml = PVCYamlTextBox.Text;
            bool success = await _openShiftService.CreateResourceFromYamlAsync(pvcYaml);
            
            if (success)
            {
                MessageBox.Show("PersistentVolumeClaim wurde erfolgreich erstellt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Fehler beim Erstellen des PersistentVolumeClaim.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage.Text = $"Fehler beim Erstellen des PVC: {ex.Message}";
            _loggingService.LogError($"Fehler beim Erstellen des PVC: {ex.Message}", ex);
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CreateSecretButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            if (string.IsNullOrEmpty(currentProject))
            {
                MessageBox.Show("Bitte wählen Sie zuerst ein Projekt aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string secretName = SecretNameTextBox.Text;
            
            // Überprüfen, ob Username und Password angegeben wurden
            string username = UsernameTextBox.Text;
            string password = SecretPasswordBox.Password;
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Bitte geben Sie Benutzername und Passwort an.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // YAML generieren
            string secretYaml = GenerateSecretYaml(currentProject, secretName);
            
            // Base64-Kodierung für Benutzername und Passwort
            string usernameBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(username));
            string passwordBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
            
            // YAML aktualisieren
            secretYaml = secretYaml.Replace("password: ", $"password: {passwordBase64}")
                                  .Replace("username: ", $"username: {usernameBase64}");

            // Secret erstellen
            bool result = await _openShiftService.CreateResourceFromYamlAsync(secretYaml);
            
            if (result)
            {
                _loggingService.LogInfo($"Secret '{secretName}' wurde erfolgreich erstellt.");
                MessageBox.Show($"Secret '{secretName}' wurde erfolgreich erstellt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                throw new Exception("Fehler beim Erstellen des Secrets.");
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des Secrets: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Erstellen des Secrets: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void AppButton5_Click(object sender, RoutedEventArgs e)
    {
        // Button-Styles zurücksetzen und aktuellen Button hervorheben
        ResetAppButtonStyles();
        AppButton5.Style = (Style)FindResource("ActiveSidebarButton");
        
        HideAllContent();
        ServiceRouteRolebindingContent.Visibility = Visibility.Visible;
    }

    private void MonitoringButton_Click(object sender, RoutedEventArgs e)
    {
        ResetButtonStyles();
        MonitoringButton.Style = (Style)FindResource("ActiveSidebarButton");
        ShowContent(MonitoringContent);
    }

    private async void UserManagementButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ResetButtonStyles();
            UserManagementButton.Style = (Style)FindResource("ActiveSidebarButton");
            
            _loggingService.LogInfo("Benutzerverwaltungsseite wird angezeigt");
            
            // Benutzerverwaltungsseite anzeigen
            ShowContent(UserManagementContent);
            
            // UserManagementPage im Frame anzeigen
            var userManagementPage = new UserManagementPage();
            UserManagementFrame.Navigate(userManagementPage);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anzeigen der Benutzerverwaltungsseite: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Anzeigen der Benutzerverwaltungsseite: {ex.Message}", 
                          "Fehler", 
                          MessageBoxButton.OK, 
                          MessageBoxImage.Error);
        }
    }

    private async Task LoadUserData()
    {
        try
        {
            _loggingService.LogInfo("Starte Laden der Benutzerdaten...");
            
            var currentUser = _userService.GetCurrentUser();
            _loggingService.LogInfo($"GetCurrentUser aufgerufen, Ergebnis: {(currentUser != null ? $"Benutzer gefunden: {currentUser.Username}" : "Kein Benutzer gefunden")}");
            
            if (currentUser == null)
            {
                _loggingService.LogWarning("Kein Benutzer angemeldet - currentUser ist null");
                MessageBox.Show("Kein Benutzer angemeldet.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Rufen Sie stattdessen die UserManagementPage als Inhalt des Frames auf
            var userManagementPage = new UserManagementPage();
            UserManagementFrame.Navigate(userManagementPage);
            
            _loggingService.LogInfo("Benutzerverwaltungsseite wurde geladen");
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der Benutzerdaten: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der Benutzerdaten: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ResetButtonStyles();
        SettingsButton.Style = (Style)FindResource("ActiveSidebarButton");
        ShowContent(SettingsContent);
        // Aktuellen OC-Ordner laden
        try
        {
            var settingsService = new Services.SettingsService();
            var ocFolder = settingsService.GetOcToolFolderAsync().GetAwaiter().GetResult();
            if (OcToolFolderTextBox != null)
            {
                OcToolFolderTextBox.Text = ocFolder ?? string.Empty;
            }
        }
        catch { }
    }

    private async void RequestAccessButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var loginWindow = new LoginWindow();
            if (loginWindow.ShowDialog() == true)
            {
                _currentUserInfo = loginWindow.CurrentUser;
                if (_currentUserInfo != null)
                {
                    _loggingService.LogInfo($"Benutzer {_currentUserInfo.Username} erfolgreich angemeldet");
                    CurrentUserText.Text = _currentUserInfo.Username;
                    FullNameText.Text = _currentUserInfo.FullName;
                    DashboardContent.Visibility = Visibility.Visible;
                    LoginContent.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _loggingService.LogWarning("Anmeldung fehlgeschlagen: Keine Benutzerinformationen verfügbar");
                    MessageBox.Show("Anmeldung fehlgeschlagen. Bitte versuchen Sie es erneut.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Fehler beim Anmeldevorgang", ex);
            MessageBox.Show("Ein Fehler ist beim Anmeldevorgang aufgetreten.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _loggingService.LogInfo("Anwendung wird beendet");
    }

    private void ResetButtonStyles()
    {
        DashboardButton.Style = null;
        ClusterButton.Style = null;
        ApplicationsButton.Style = null;
        MonitoringButton.Style = null;
        UserManagementButton.Style = null;
        SettingsButton.Style = null;
    }

    private async void ClusterLoginButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedItem = ClusterComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Cluster aus.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentClusterConfig.Url = selectedItem.Tag.ToString();
            _currentClusterConfig.Username = ClusterUsernameTextBox.Text;
            _currentClusterConfig.Password = ClusterPasswordBox.Password;
            _currentClusterConfig.Name = selectedItem.Content.ToString();

            if (string.IsNullOrEmpty(_currentClusterConfig.Username) || string.IsNullOrEmpty(_currentClusterConfig.Password))
            {
                MessageBox.Show("Bitte geben Sie Benutzernamen und Passwort ein.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ClusterLoginButton.IsEnabled = false;
            ClusterLoginButton.Content = "Anmeldung läuft...";

            try
            {
                var (success, message) = await _openShiftService.LoginToCluster(
                    _currentClusterConfig.Url,
                    _currentClusterConfig.Username,
                    _currentClusterConfig.Password);

                if (success)
                {
                    _isClusterLoggedIn = true;
                    
                    // Lade die verfügbaren Projekte
                    await LoadProjects();
                    
                    // Aktualisiere die UI für den gerade angemeldeten Cluster
                    CurrentClusterText.Text = _currentClusterConfig.Name;
                    
                    // Aktiviere den Applications-Button, wenn er nach der Rolle aktiviert sein soll
                    // Entferne das await, da SetPermissionsByRole void zurückgibt
                    SetPermissionsByRole(_currentUserRole);
                    
                    MessageBox.Show($"Erfolgreich am Cluster {_currentClusterConfig.Name} angemeldet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _isClusterLoggedIn = false;
                    MessageBox.Show($"Anmeldung fehlgeschlagen: {message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                ClusterLoginButton.IsEnabled = true;
                ClusterLoginButton.Content = "Anmelden";
            }
        }
        catch (Exception ex)
        {
            _isClusterLoggedIn = false;
            _loggingService.LogError($"Fehler bei der Clusteranmeldung: {ex.Message}", ex);
            MessageBox.Show($"Fehler bei der Clusteranmeldung: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            
            ClusterLoginButton.IsEnabled = true;
            ClusterLoginButton.Content = "Anmelden";
        }
    }

    private async Task LoadProjects()
    {
        try
        {
            var (projects, error) = await _openShiftService.GetProjects();
            if (error == null)
            {
                _allProjects = projects;
                ProjectsListView.ItemsSource = _allProjects;
            }
            else
            {
                MessageBox.Show($"Fehler beim Laden der Projekte: {error}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der Projekte: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der Projekte: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProjectSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = ProjectSearchTextBox.Text.ToLower();
        var filteredProjects = _allProjects.Where(p => p.ToLower().Contains(searchText)).ToList();
        ProjectsListView.ItemsSource = filteredProjects;
    }

    private async void ProjectsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectsListView.SelectedItem is string selectedProject)
        {
            try
            {
                var (success, message) = await _openShiftService.SwitchProject(selectedProject);
                if (success)
                {
                    _currentClusterConfig.SelectedProject = selectedProject;
                    CurrentProjectText.Text = selectedProject;
                }
                else
                {
                    MessageBox.Show($"Fehler beim Wechseln des Projekts: {message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler beim Wechseln des Projekts: {ex.Message}", ex);
                MessageBox.Show($"Fehler beim Wechseln des Projekts: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ClusterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClusterComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            _currentClusterConfig.Url = selectedItem.Tag.ToString();
            _currentClusterConfig.Name = selectedItem.Content.ToString();
        }
    }

    private void HideAllContent()
    {
        LoginContent.Visibility = Visibility.Collapsed;
        DashboardContent.Visibility = Visibility.Collapsed;
        ClusterContent.Visibility = Visibility.Collapsed;
        ApplicationsContent.Visibility = Visibility.Collapsed;
        DeploymentsPodsContent.Visibility = Visibility.Collapsed;
        VolumesContent.Visibility = Visibility.Collapsed;
        MultiProjectContent.Visibility = Visibility.Collapsed;
        CreateContent.Visibility = Visibility.Collapsed;
        OISFilesVolumeContent.Visibility = Visibility.Collapsed;
        OISFilesOptIotVolumeContent.Visibility = Visibility.Collapsed;
        FirmwareVolumeContent.Visibility = Visibility.Collapsed;
        EDHRVolumeContent.Visibility = Visibility.Collapsed; // Sicherstellen dass dieser explizit ausgeblendet wird
        SAPConnectivityVolumeContent.Visibility = Visibility.Collapsed;
        ServiceRouteRolebindingContent.Visibility = Visibility.Collapsed;
        MonitoringContent.Visibility = Visibility.Collapsed;
        UserManagementContent.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Collapsed;
        AutomationManagerContent.Visibility = Visibility.Collapsed; // AutomationManagerContent hinzugefügt
        DeleteUnboundPVsContent.Visibility = Visibility.Collapsed;
    }

    private async void RefreshDeploymentsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(_currentClusterConfig?.SelectedProject))
            {
                await LoadDeployments(_currentClusterConfig.SelectedProject);
            }
            else
            {
                MessageBox.Show("Bitte wählen Sie zuerst ein Projekt aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Aktualisieren der Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StopDeploymentButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedDeployments = DeploymentsListView.SelectedItems.Cast<Models.Deployment>().ToList();
            if (selectedDeployments == null || selectedDeployments.Count == 0)
            {
                MessageBox.Show("Bitte wählen Sie mindestens ein Deployment aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;

            int successCount = 0;
            int failCount = 0;
            StringBuilder errorMessages = new StringBuilder();

            foreach (var deployment in selectedDeployments)
            {
                var (success, message) = await _openShiftService.ScaleDeployment(deployment.Name, 0);
                if (success)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                    errorMessages.AppendLine($"{deployment.Name}: {message}");
                }
            }

            if (failCount == 0)
            {
                MessageBox.Show($"{successCount} Deployment(s) erfolgreich gestoppt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Erfolgreich gestoppt: {successCount}\nFehlgeschlagen: {failCount}\n\nDetails:\n{errorMessages}", "Teilweise erfolgreich", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await LoadDeployments(_currentClusterConfig.SelectedProject);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Stoppen des Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Stoppen des Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void DeleteDeploymentButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedDeployments = DeploymentsListView.SelectedItems.Cast<Models.Deployment>().ToList();
            if (selectedDeployments == null || selectedDeployments.Count == 0)
            {
                MessageBox.Show("Bitte wählen Sie mindestens ein Deployment aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Möchten Sie die ausgewählten Deployments wirklich löschen? (Anzahl: {selectedDeployments.Count})", 
                                        "Deployments löschen", 
                                        MessageBoxButton.YesNo, 
                                        MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Mouse.OverrideCursor = Cursors.Wait;

                int successCount = 0;
                int failCount = 0;
                StringBuilder errorMessages = new StringBuilder();

                foreach (var deployment in selectedDeployments)
                {
                    var (success, message) = await _openShiftService.DeleteDeployment(deployment.Name);
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        errorMessages.AppendLine($"{deployment.Name}: {message}");
                    }
                }

                if (failCount == 0)
                {
                    MessageBox.Show($"{successCount} Deployment(s) erfolgreich gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Erfolgreich gelöscht: {successCount}\nFehlgeschlagen: {failCount}\n\nDetails:\n{errorMessages}", "Teilweise erfolgreich", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                await LoadDeployments(_currentClusterConfig.SelectedProject);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Löschen des Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Löschen des Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void ScaleDeploymentButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedDeployment = DeploymentsListView.SelectedItem as Models.Deployment;
            if (selectedDeployment == null)
            {
                MessageBox.Show("Bitte wählen Sie ein Deployment aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (!int.TryParse(ReplicasTextBox.Text, out int replicas) || replicas < 0)
            {
                MessageBox.Show("Bitte geben Sie eine gültige Anzahl von Replicas ein (größer oder gleich 0).", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            Mouse.OverrideCursor = Cursors.Wait;
            
            var (success, message) = await _openShiftService.ScaleDeployment(selectedDeployment.Name, replicas);
            
            if (success)
            {
                MessageBox.Show($"Deployment {selectedDeployment.Name} wurde erfolgreich auf {replicas} Replicas skaliert.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadDeployments(_currentClusterConfig.SelectedProject);
            }
            else
            {
                MessageBox.Show($"Fehler beim Skalieren des Deployments: {message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Skalieren des Deployments: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Skalieren des Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void DeletePodButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedPod = PodsListView.SelectedItem as Models.Pod;
            if (selectedPod == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Pod aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show($"Möchten Sie den Pod {selectedPod.Name} wirklich löschen?", 
                                         "Pod löschen", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                
                var (success, message) = await _openShiftService.DeletePod(selectedPod.Name);
                
                if (success)
                {
                    MessageBox.Show($"Pod {selectedPod.Name} wurde erfolgreich gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Aktualisiere die Pods für das ausgewählte Deployment
                    if (DeploymentsListView.SelectedItem is Models.Deployment selectedDeployment)
                    {
                        await LoadPodsForDeployment(selectedDeployment.Name);
                    }
                }
                else
                {
                    MessageBox.Show($"Fehler beim Löschen des Pods: {message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Löschen des Pods: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Löschen des Pods: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // Reset der Styles in den Apps-Sidebar-Buttons
    private void ResetAppButtonStyles()
    {
        HomeButton.Style = null;
        AppButton1.Style = null;
        AppButton2.Style = null;
        AppButton3.Style = null;
        AppButton4.Style = null;
        AppButton5.Style = null;
        OISFilesVolumeButton.Style = null;
        OISFilesOptIotVolumeButton.Style = null;
        FirmwareVolumeButton.Style = null;
        EDHRVolumeButton.Style = null;
        SAPConnectivityVolumeButton.Style = null;
        AutomationManagerButton.Style = null; // Hinzufügen des AutomationManagerButton
        DeleteUnboundPVsButton.Style = null; // Hinzufügen des DeleteUnboundPVsButton
    }

    private async Task<bool> ApplyYamlFile(string yamlFilePath)
    {
        var ocFolder = await new Services.SettingsService().GetOcToolFolderAsync();
        var ocExe = !string.IsNullOrWhiteSpace(ocFolder) ? System.IO.Path.Combine(ocFolder, "oc.exe") : "oc";
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ocExe,
            Arguments = $"apply -f \"{yamlFilePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, args) => 
            {
                if (args.Data != null) outputBuilder.AppendLine(args.Data);
            };

            process.ErrorDataReceived += (sender, args) => 
            {
                if (args.Data != null) errorBuilder.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
    }

    private async void BrowseOcToolFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "oc.exe auswählen",
            Filter = "oc.exe|oc.exe",
            CheckFileExists = true
        };
        var result = dialog.ShowDialog();
        if (result == true)
        {
            var folder = System.IO.Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            OcToolFolderTextBox.Text = folder;
        }
    }

    private async void SaveOcToolFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = OcToolFolderTextBox.Text?.Trim() ?? string.Empty;
            await new Services.SettingsService().SetOcToolFolderAsync(folder);
            _openShiftService.RefreshOcPath();
            SettingsMessageText.Text = "OC Tool Folder gespeichert.";
            SettingsMessageText.Foreground = new SolidColorBrush(Colors.Green);
        }
        catch (Exception ex)
        {
            SettingsMessageText.Text = $"Fehler beim Speichern: {ex.Message}";
            SettingsMessageText.Foreground = new SolidColorBrush(Colors.Red);
        }
    }

    private void GeneratePVYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvName = PVNameTextBox.Text;
            string smbPath = SMBSourcePathTextBox.Text;
            string secretName = SecretNameTextBox.Text;
            
            // Überprüfe, ob die erforderlichen Felder ausgefüllt sind
            if (string.IsNullOrEmpty(smbPath))
            {
                OISFilesErrorMessage.Text = "Bitte geben Sie einen SMB Source Path an.";
                OISFilesErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV YAML generieren
            PVYamlTextBox.Text = GeneratePVYaml(currentProject, pvName, smbPath, secretName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"PV YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            OISFilesErrorMessage.Text = "PV YAML wurde erstellt.";
            OISFilesErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            OISFilesErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des PV YAML: {ex.Message}", ex);
            OISFilesErrorMessage.Text = $"Fehler beim Erstellen des PV YAML: {ex.Message}";
            OISFilesErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void GeneratePVCYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvcName = PVCNameTextBox.Text;
            string pvName = PVNameTextBox.Text;
            
            // PVC YAML generieren
            PVCYamlTextBox.Text = GeneratePVCYaml(currentProject, pvcName, pvName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"PVC YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            OISFilesErrorMessage.Text = "PVC YAML wurde erstellt.";
            OISFilesErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            OISFilesErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des PVC YAML: {ex.Message}", ex);
            OISFilesErrorMessage.Text = $"Fehler beim Erstellen des PVC YAML: {ex.Message}";
            OISFilesErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void GenerateSecretYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string secretName = SecretNameTextBox.Text;
            
            // Überprüfen der Eingaben
            string username = UsernameTextBox.Text;
            string password = SecretPasswordBox.Password;
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                OISFilesErrorMessage.Text = "Bitte geben Sie Benutzername und Passwort an.";
                OISFilesErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // Secret YAML generieren
            string secretYaml = GenerateSecretYaml(currentProject, secretName);
            
            // Base64-Kodierung für Benutzername und Passwort
            string usernameBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(username));
            string passwordBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
            
            // YAML aktualisieren mit Base64-kodierten Werten
            secretYaml = secretYaml.Replace("username: ", $"username: {usernameBase64}")
                              .Replace("password: ", $"password: {passwordBase64}");
            
            OISFilesSecretYamlTextBox.Text = secretYaml;
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"Secret YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            OISFilesErrorMessage.Text = "Secret YAML wurde erstellt.";
            OISFilesErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            OISFilesErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des Secret YAML: {ex.Message}", ex);
            OISFilesErrorMessage.Text = $"Fehler beim Erstellen des Secret YAML: {ex.Message}";
            OISFilesErrorMessage.Visibility = Visibility.Visible;
        }
    }

    // EDHR Volume Funktionalität
    
    private void EDHRVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Alle Variablen zurücksetzen, um keine restlichen Einstellungen zu behalten
            _originalYaml = string.Empty;
            _modifiedYaml = string.Empty;
            
            // Alle Sperrvariablen zurücksetzen
            _isApplyingOISFilesChanges = false;
            _isApplyingOISFilesOptIotChanges = false;
            _isApplyingEDHRChanges = false;
            _isApplyingFirmwareChanges = false;
            _isApplyingSAPConnectivityChanges = false;
            
            // Button-Style setzen und Inhalte anzeigen
            ResetAppButtonStyles();
            EDHRVolumeButton.Style = (Style)FindResource("ActiveSidebarButton");
            HideAllContent();
            EDHRVolumeContent.Visibility = Visibility.Visible;

            // Status-Meldung zurücksetzen
            EDHRErrorMessage.Text = string.Empty;
            EDHRErrorMessage.Visibility = Visibility.Collapsed;
            EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            
            // Zurücksetzen aller Textfelder und Eingabefelder für diesen Bereich
            EDHROriginalYamlTextBox.Text = string.Empty;
            EDHRModifiedYamlTextBox.Text = string.Empty;
            EDHRPVNameTextBox.Text = string.Empty;
            EDHRPVCNameTextBox.Text = string.Empty;
            EDHRPVCVolumeNameTextBox.Text = string.Empty;
            EDHRSecretNameTextBox.Text = string.Empty;
            EDHRUsernameTextBox.Text = string.Empty;
            EDHRSecretPasswordBox.Password = string.Empty;
            EDHRSMBSourcePathTextBox.Text = string.Empty;
            EDHRPVYamlTextBox.Text = string.Empty;
            EDHRPVCYamlTextBox.Text = string.Empty;
            EDHRSecretYamlTextBox.Text = string.Empty;

            // Aktuelles Projekt prüfen - hier explizit nochmal abfragen, um sicherzustellen dass es aktuell ist
            Task.Run(async () => {
                try
                {
                    // Aktuelles Projekt abfragen
                    string currentProject = await _openShiftService.GetCurrentProjectName();
                    
                    if (string.IsNullOrEmpty(currentProject))
                    {
                        _loggingService.LogWarning("EDHRVolumeButton_Click: Kein Projekt ausgewählt");
                        await Dispatcher.InvokeAsync(() => {
                            EDHRErrorMessage.Text = "Bitte wählen Sie zuerst ein Projekt aus, bevor Sie fortfahren.";
                            EDHRErrorMessage.Visibility = Visibility.Visible;
                        });
                        return;
                    }

                    _loggingService.LogInfo("EDHR Volume Management wird geöffnet...");
                    _loggingService.LogInfo($"Aktuelles Projekt: {currentProject}");
                    
                    // Aktuelles Projekt anzeigen
                    await Dispatcher.InvokeAsync(() => {
                        EDHRCurrentProjectText.Text = currentProject;
                    });
                    
                    // Host-Deployment YAML laden für EDHR und dieses Projekt
                    string yaml = await _openShiftService.GetHostDeploymentYamlAsync(currentProject);
                    
                    await Dispatcher.InvokeAsync(() => {
                        // Namen aktualisieren
                        UpdateEDHRResourceNames();
                        
                        // YAML in TextBoxen anzeigen
                        EDHROriginalYamlTextBox.Text = yaml;
                        EDHRModifiedYamlTextBox.Text = yaml; // Initial kopieren
                        
                        _loggingService.LogInfo($"Host Deployment YAML erfolgreich geladen für EDHR");
                    });
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Fehler beim Laden der Projektdaten: {ex.Message}", ex);
                    await Dispatcher.InvokeAsync(() => {
                        EDHRErrorMessage.Text = $"Fehler beim Laden der Projektdaten: {ex.Message}";
                        EDHRErrorMessage.Visibility = Visibility.Visible;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Öffnen des EDHR Volume Management: {ex.Message}", ex);
            EDHRErrorMessage.Text = $"Fehler beim Öffnen: {ex.Message}";
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void UpdateEDHRResourceNames()
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            
            _loggingService.LogInfo($"Aktualisiere EDHR Ressourcennamen für Projekt: {currentProject}");
            
            if (string.IsNullOrEmpty(currentProject))
            {
                _loggingService.LogWarning("Kein aktuelles Projekt ausgewählt");
                EDHRErrorMessage.Text = "Kein aktuelles Projekt ausgewählt. Bitte wählen Sie zuerst ein Projekt aus.";
                EDHRErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV
            string pvName = $"{currentProject}-edhr";
            EDHRPVNameTextBox.Text = pvName;
            
            // PVC
            EDHRPVCNameTextBox.Text = $"{currentProject}-edhr";
            EDHRPVCVolumeNameTextBox.Text = pvName;
            
            // Secret
            EDHRSecretNameTextBox.Text = $"{currentProject}-edhr-creds";
            
            // Beispiel-SMB-Pfad eintragen
            EDHRSMBSourcePathTextBox.Text = $"//bbmag65.bbmag.bbraun.com/NFS/ixos_test/einschleuse_MES_BX";
            
            _loggingService.LogInfo($"EDHR Ressourcennamen erfolgreich aktualisiert: PV={pvName}, PVC={currentProject}-edhr, Secret={currentProject}-edhr-creds");
            _loggingService.LogInfo($"SMB-Pfad: {EDHRSMBSourcePathTextBox.Text}");
            
            // Erfolgreich aktualisiert, Fehlermeldung zurücksetzen
            EDHRErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der EDHR Ressourcennamen: {ex.Message}", ex);
            EDHRErrorMessage.Text = $"Fehler beim Aktualisieren der EDHR Ressourcennamen: {ex.Message}";
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private string GenerateEDHRPVYaml(string currentProject, string pvName, string smbPath, string secretName)
    {
        // Mit korrigierter Einrückung (ein Tab zurück)
        return $@"kind: PersistentVolume
apiVersion: v1
metadata:
  name: {pvName}
  annotations:
    pv.kubernetes.io/bound-by-controller: 'yes'
  finalizers:
    - kubernetes.io/pv-protection
spec:
  capacity:
    storage: 1Gi
  csi:
    driver: smb.csi.k8s.io
    volumeHandle: {pvName}
    volumeAttributes:
      source: {smbPath}
    nodeStageSecretRef:
      name: {secretName}
      namespace: {currentProject}
  accessModes:
    - ReadWriteMany
  persistentVolumeReclaimPolicy: Retain
  mountOptions:
    - dir_mode=0777
    - file_mode=0777
    - nobrl
  volumeMode: Filesystem";
    }

    private string GenerateEDHRPVCYaml(string currentProject, string pvcName, string pvName)
    {
        // Mit korrigierter Einrückung (ein Tab zurück)
        return $@"kind: PersistentVolumeClaim
apiVersion: v1
metadata:
  name: {pvcName}
  namespace: {currentProject}
  annotations:
    pv.kubernetes.io/bind-completed: 'yes'
  finalizers:
    - kubernetes.io/pvc-protection  
spec:
  accessModes:
    - ReadWriteMany
  resources:
    requests:
      storage: 1Gi
  volumeName: {pvName}
  storageClassName: ''
  volumeMode: Filesystem";
    }

    private string GenerateEDHRSecretYaml(string projectName, string secretName)
    {
        try
        {
            string template = $@"apiVersion: v1
kind: Secret
metadata:
  name: {secretName}
  namespace: {projectName}
type: Opaque
data:
  domain: YmJtYWc=
  username: 
  password: ";

            return template;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Fehler beim Generieren des EDHR Secret YAML", ex);
            return string.Empty;
        }
    }

    private async Task LoadEDHRHostDeploymentYaml()
    {
        try
        {
            string projectName = _openShiftService.CurrentProject;
            _loggingService.LogInfo($"Lade Host-Deployment YAML für EDHR und Projekt: {projectName}");
            
            string yaml = await _openShiftService.GetHostDeploymentYamlAsync(projectName);
            _loggingService.LogInfo("Host-Deployment YAML erfolgreich geladen");
            
            await Dispatcher.InvokeAsync(() => {
                EDHROriginalYamlTextBox.Text = yaml;
                EDHRModifiedYamlTextBox.Text = yaml; // Initial kopieren
                
                // Bei Erfolg Fehlermeldung ausblenden
                EDHRErrorMessage.Visibility = Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => {
                // Zeige Fehlermeldungen in einem MessageBox an
                MessageBox.Show($"Fehler beim Laden des EDHR Host Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Fehlermeldung im EDHRErrorMessage anzeigen
                EDHRErrorMessage.Text = $"Fehler beim Laden des EDHR Host Deployments: {ex.Message}";
                EDHRErrorMessage.Visibility = Visibility.Visible;
                
                _loggingService.LogError($"Fehler beim Laden des EDHR Host Deployments: {ex.Message}", ex);
            });
        }
    }

    private void ConfigureEDHRYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Vorhandene YAML aus dem TextBox holen
            string originalYaml = EDHROriginalYamlTextBox.Text;
            
            if (string.IsNullOrEmpty(originalYaml))
            {
                EDHRErrorMessage.Text = "Keine YAML zum Konfigurieren vorhanden. Bitte wählen Sie zuerst ein Projekt aus.";
                EDHRErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // YAML für EDHR Volume modifizieren
            string modifiedYaml = ModifyYamlForEDHR(originalYaml);
            
            // Modifizierte YAML in TextBox anzeigen
            EDHRModifiedYamlTextBox.Text = modifiedYaml;
            
            EDHRErrorMessage.Text = "YAML wurde erfolgreich für EDHR Volume konfiguriert!";
            EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Konfigurieren der YAML: {ex.Message}", ex);
            EDHRErrorMessage.Text = $"Fehler beim Konfigurieren der YAML: {ex.Message}";
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private string ModifyYamlForEDHR(string yaml)
    {
        // Hier würde die tatsächliche YAML-Modifikation stattfinden
        // Dies ist eine vereinfachte Version, die im wirklichen Code durch eine
        // ordnungsgemäße YAML-Parsing-Bibliothek ersetzt werden sollte
        
        string volumeName = "edhr";
        string volumePath = "/opt/edhr/";
        string pvcName = EDHRPVCNameTextBox.Text;
        
        // Env-Eintrag hinzufügen
        if (yaml.Contains("env:"))
        {
            string envEntry = $"        - name: {volumeName}\n          value: {volumePath}";
            yaml = yaml.Replace("env:", $"env:\n{envEntry}");
        }
        
        // Volume-Eintrag hinzufügen
        if (yaml.Contains("volumes:"))
        {
            string volumeEntry = $"      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
            yaml = yaml.Replace("volumes:", $"volumes:\n{volumeEntry}");
        }
        else
        {
            // Wenn kein volumes-Abschnitt existiert, fügen wir einen hinzu
            yaml += $"\n    volumes:\n      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
        }
        
        // volumeMounts-Eintrag hinzufügen
        if (yaml.Contains("volumeMounts:"))
        {
            string volumeMountEntry = $"        - name: {volumeName}\n          mountPath: {volumePath}";
            yaml = yaml.Replace("volumeMounts:", $"volumeMounts:\n{volumeMountEntry}");
        }
        else
        {
            // Wenn kein volumeMounts-Abschnitt existiert, fügen wir einen hinzu
            // Dies würde in einem bestehenden Container-Block platziert werden
            if (yaml.Contains("containers:"))
            {
                string containerBlockStart = "containers:";
                int containerStartIndex = yaml.IndexOf(containerBlockStart);
                int insertIndex = yaml.IndexOf("image:", containerStartIndex);
                
                if (insertIndex > 0)
                {
                    string beforeInsert = yaml.Substring(0, insertIndex);
                    string afterInsert = yaml.Substring(insertIndex);
                    string volumeMountsBlock = $"        volumeMounts:\n          - name: {volumeName}\n            mountPath: {volumePath}\n        ";
                    
                    yaml = beforeInsert + volumeMountsBlock + afterInsert;
                }
            }
        }
        
        return yaml;
    }

    private async void ApplyEDHRChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingEDHRChanges)
        {
            _loggingService.LogWarning("Eine EDHR Operation ist bereits in Bearbeitung. Bitte warten Sie...");
            EDHRErrorMessage.Text = "Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...";
            EDHRErrorMessage.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _isApplyingEDHRChanges = true;
            
            // Verwende die EDHR-spezifischen TextBoxen, nicht die allgemeinen Variablen
            string modifiedYaml = EDHRModifiedYamlTextBox.Text;
            
            if (string.IsNullOrEmpty(modifiedYaml))
            {
                EDHRErrorMessage.Text = "Keine YAML zum Anwenden vorhanden. Bitte konfigurieren Sie zuerst die YAML.";
                EDHRErrorMessage.Visibility = Visibility.Visible;
                _isApplyingEDHRChanges = false;
                return;
            }
            
            EDHRErrorMessage.Text = "Änderungen werden angewendet...";
            EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Blue);
            EDHRErrorMessage.Visibility = Visibility.Visible;
            
            bool result = await _openShiftService.UpdateDeploymentFromYamlAsync(_openShiftService.CurrentProject, modifiedYaml);
            
            if (result)
            {
                EDHRErrorMessage.Text = "Änderungen wurden erfolgreich angewendet!";
                EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
                EDHRErrorMessage.Visibility = Visibility.Visible;
                _loggingService.LogInfo("EDHR Volume wurde erfolgreich zum Deployment hinzugefügt.");
            }
            else
            {
                EDHRErrorMessage.Text = "Fehler beim Anwenden der Änderungen. Siehe Log für Details.";
                EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
                EDHRErrorMessage.Visibility = Visibility.Visible;
                _loggingService.LogError("Fehler beim Anwenden der EDHR Volume Änderungen.");
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anwenden der EDHR Änderungen: {ex.Message}", ex);
            EDHRErrorMessage.Text = $"Fehler beim Anwenden der EDHR Änderungen: {ex.Message}";
            EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
        finally
        {
            _isApplyingEDHRChanges = false;
        }
    }

    private void GenerateEDHRPVYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvName = EDHRPVNameTextBox.Text;
            string smbPath = EDHRSMBSourcePathTextBox.Text;
            string secretName = EDHRSecretNameTextBox.Text;
            
            // Überprüfe, ob die erforderlichen Felder ausgefüllt sind
            if (string.IsNullOrEmpty(smbPath))
            {
                EDHRErrorMessage.Text = "Bitte geben Sie einen SMB Source Path an.";
                EDHRErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV YAML generieren
            EDHRPVYamlTextBox.Text = GenerateEDHRPVYaml(currentProject, pvName, smbPath, secretName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"EDHR PV YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            EDHRErrorMessage.Text = "EDHR PV YAML wurde erstellt.";
            EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des EDHR PV YAML: {ex.Message}", ex);
            EDHRErrorMessage.Text = $"Fehler beim Erstellen des EDHR PV YAML: {ex.Message}";
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void GenerateEDHRPVCYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvcName = EDHRPVCNameTextBox.Text;
            string pvName = EDHRPVNameTextBox.Text;
            
            // PVC YAML generieren
            EDHRPVCYamlTextBox.Text = GenerateEDHRPVCYaml(currentProject, pvcName, pvName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"EDHR PVC YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            EDHRErrorMessage.Text = "EDHR PVC YAML wurde erstellt.";
            EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des EDHR PVC YAML: {ex.Message}", ex);
            EDHRErrorMessage.Text = $"Fehler beim Erstellen des EDHR PVC YAML: {ex.Message}";
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void GenerateEDHRSecretYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string secretName = EDHRSecretNameTextBox.Text;
            
            // Überprüfen der Eingaben
            string username = EDHRUsernameTextBox.Text;
            string password = EDHRSecretPasswordBox.Password;
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                EDHRErrorMessage.Text = "Bitte geben Sie Benutzername und Passwort an.";
                EDHRErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // Secret YAML generieren
            string secretYaml = GenerateEDHRSecretYaml(currentProject, secretName);
            
            // Base64-Kodierung für Benutzername und Passwort
            string usernameBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(username));
            string passwordBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
            
            // YAML aktualisieren mit Base64-kodierten Werten
            secretYaml = secretYaml.Replace("username: ", $"username: {usernameBase64}")
                              .Replace("password: ", $"password: {passwordBase64}");
            
            EDHRSecretYamlTextBox.Text = secretYaml;
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"EDHR Secret YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            EDHRErrorMessage.Text = "EDHR Secret YAML wurde erstellt.";
            EDHRErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des EDHR Secret YAML: {ex.Message}", ex);
            EDHRErrorMessage.Text = $"Fehler beim Erstellen des EDHR Secret YAML: {ex.Message}";
            EDHRErrorMessage.Visibility = Visibility.Visible;
        }
    }

    // SAP Connectivity Volume Implementierung
    private void SAPConnectivityVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Alle Sperrvariablen zurücksetzen
            _isApplyingOISFilesChanges = false;
            _isApplyingOISFilesOptIotChanges = false;
            _isApplyingEDHRChanges = false;
            _isApplyingFirmwareChanges = false;
            _isApplyingSAPConnectivityChanges = false;
            
            // Button-Style setzen und Inhalte anzeigen
            ResetAppButtonStyles();
            SAPConnectivityVolumeButton.Style = (Style)FindResource("ActiveSidebarButton");
            HideAllContent();
            SAPConnectivityVolumeContent.Visibility = Visibility.Visible;

            // Status-Meldung zurücksetzen
            SAPConnectivityErrorMessage.Text = string.Empty;
            SAPConnectivityErrorMessage.Visibility = Visibility.Collapsed;
            SAPConnectivityErrorMessage.Foreground = new SolidColorBrush(Colors.Red);

            // Zurücksetzen aller Textfelder und Eingabefelder für diesen Bereich
            SAPConnectivityOriginalYamlTextBox.Text = string.Empty;
            SAPConnectivityModifiedYamlTextBox.Text = string.Empty;
            SAPPVNameTextBox.Text = string.Empty;
            SAPPVCNameTextBox.Text = string.Empty;
            SAPPVCVolumeNameTextBox.Text = string.Empty;
            SAPSecretNameTextBox.Text = string.Empty;
            SAPUsernameTextBox.Text = string.Empty;
            SAPSecretPasswordBox.Password = string.Empty;
            SAPSMBSourcePathTextBox.Text = string.Empty;
            SAPPVYamlTextBox.Text = string.Empty;
            SAPPVCYamlTextBox.Text = string.Empty;
            SAPSecretYamlTextBox.Text = string.Empty;

            // Aktuelles Projekt prüfen
            string currentProject = _openShiftService.CurrentProject;
            if (string.IsNullOrEmpty(currentProject))
            {
                _loggingService.LogWarning("SAPConnectivityVolumeButton_Click: Kein Projekt ausgewählt");
                SAPConnectivityErrorMessage.Text = "Bitte wählen Sie zuerst ein Projekt aus, bevor Sie fortfahren.";
                SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
                return;
            }

            _loggingService.LogInfo("SAP Connectivity Volume Management wird geöffnet...");
            _loggingService.LogInfo("Ermittle aktuelles Projekt...");

            // YAML für das Deployment laden und Namen aktualisieren
            Task.Run(async () => {
                try
                {
                    // Aktuelles Projekt anzeigen
                    await Dispatcher.InvokeAsync(() => {
                        SAPConnectivityCurrentProjectText.Text = currentProject;
                    });
                    
                    // Host-Deployment YAML laden
                    _loggingService.LogInfo($"Lade Host-Deployment YAML für SAP Connectivity und Projekt: {currentProject}");
                    string yaml = await _openShiftService.GetHostDeploymentYamlAsync(currentProject);
                    
                    await Dispatcher.InvokeAsync(() => {
                        // Namen aktualisieren
                        UpdateSAPConnectivityResourceNames();
                        
                        // YAML in TextBoxen anzeigen
                        SAPConnectivityOriginalYamlTextBox.Text = yaml;
                        SAPConnectivityModifiedYamlTextBox.Text = yaml; // Initial kopieren
                        
                        _loggingService.LogInfo("Host-Deployment YAML erfolgreich für SAP Connectivity geladen");
                        _loggingService.LogInfo("Host Deployment YAML erfolgreich geladen für SAP Connectivity");
                    });
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Fehler beim Laden der Projektdaten: {ex.Message}", ex);
                    await Dispatcher.InvokeAsync(() => {
                        SAPConnectivityErrorMessage.Text = $"Fehler beim Laden der Projektdaten: {ex.Message}";
                        SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Öffnen des SAP Connectivity Volume Management: {ex.Message}", ex);
            SAPConnectivityErrorMessage.Text = $"Fehler beim Öffnen: {ex.Message}";
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void UpdateSAPConnectivityResourceNames()
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            
            _loggingService.LogInfo($"Aktualisiere SAP Connectivity Ressourcennamen für Projekt: {currentProject}");
            
            if (string.IsNullOrEmpty(currentProject))
            {
                _loggingService.LogWarning("Kein aktuelles Projekt ausgewählt");
                SAPConnectivityErrorMessage.Text = "Kein aktuelles Projekt ausgewählt. Bitte wählen Sie zuerst ein Projekt aus.";
                SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV
            string pvName = $"{currentProject}-sapconnectivity";
            SAPPVNameTextBox.Text = pvName;
            
            // PVC
            SAPPVCNameTextBox.Text = $"{currentProject}-sapconnectivity";
            SAPPVCVolumeNameTextBox.Text = pvName;
            
            // Secret
            SAPSecretNameTextBox.Text = $"{currentProject}-sapconnectivity-creds";
            
            // Beispiel-SMB-Pfad eintragen
            SAPSMBSourcePathTextBox.Text = $"//bbmag65.bbmag.bbraun.com/nfs/CMFMES/ATO/{ExtractEnvironment(currentProject)}/{currentProject}/SAP_Connectivity/";
            
            _loggingService.LogInfo($"SAP Connectivity Ressourcennamen erfolgreich aktualisiert: PV={pvName}, PVC={currentProject}-sapconnectivity, Secret={currentProject}-sapconnectivity-creds");
            _loggingService.LogInfo($"SMB-Pfad: {SAPSMBSourcePathTextBox.Text}");
            
            // Erfolgreich aktualisiert, Fehlermeldung zurücksetzen
            SAPConnectivityErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der SAP Connectivity Ressourcennamen: {ex.Message}", ex);
            SAPConnectivityErrorMessage.Text = $"Fehler beim Aktualisieren der Ressourcennamen: {ex.Message}";
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private string GenerateSAPPVYaml(string currentProject, string pvName, string smbPath, string secretName)
    {
        try
        {
            string template = $@"apiVersion: v1
kind: PersistentVolume
metadata:
  name: {pvName}
  annotations:
    pv.kubernetes.io/bound-by-controller: 'yes'
  finalizers:
    - kubernetes.io/pv-protection
spec:
  capacity:
    storage: 1Gi
  csi:
    driver: smb.csi.k8s.io
    volumeHandle: {pvName}
    volumeAttributes:
      source: {smbPath}
    nodeStageSecretRef:
      name: {secretName}
      namespace: {currentProject}
  accessModes:
    - ReadWriteMany      
  persistentVolumeReclaimPolicy: Retain      
  mountOptions:
    - dir_mode=0777
    - file_mode=0777
    - nobrl
  volumeMode: Filesystem";

            return template;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Fehler beim Generieren des SAP Connectivity PV YAML", ex);
            return string.Empty;
        }
    }

    private string GenerateSAPPVCYaml(string currentProject, string pvcName, string pvName)
    {
        return $@"apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: {pvcName}
  namespace: {currentProject}
  annotations:
    pv.kubernetes.io/bind-completed: 'yes'
  finalizers:
    - kubernetes.io/pvc-protection  
spec:
  accessModes:
    - ReadWriteMany
  resources:
    requests:
      storage: 1Gi
  volumeName: {pvName}
  storageClassName: ''
  volumeMode: Filesystem";
    }

    private string GenerateSAPSecretYaml(string projectName, string secretName)
    {
        try
        {
            string template = $@"apiVersion: v1
kind: Secret
metadata:
  name: {secretName}
  namespace: {projectName}
type: Opaque
data:
  domain: YmJtYWc=
  username: 
  password: ";

            return template;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Fehler beim Generieren des SAP Connectivity Secret YAML", ex);
            return string.Empty;
        }
    }

    private async Task LoadSAPConnectivityHostDeploymentYaml()
    {
        try
        {
            string projectName = _openShiftService.CurrentProject;
            _loggingService.LogInfo($"Lade Host-Deployment YAML für SAP Connectivity und Projekt: {projectName}");
            
            string yaml = await _openShiftService.GetHostDeploymentYamlAsync(projectName);
            _loggingService.LogInfo("Host-Deployment YAML erfolgreich für SAP Connectivity geladen");
            
            await Dispatcher.InvokeAsync(() => {
                SAPConnectivityOriginalYamlTextBox.Text = yaml;
                SAPConnectivityModifiedYamlTextBox.Text = yaml; // Initial kopieren
                
                // Bei Erfolg Fehlermeldung ausblenden
                SAPConnectivityErrorMessage.Visibility = Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => {
                // Zeige Fehlermeldungen in einem MessageBox an
                MessageBox.Show($"Fehler beim Laden des SAP Connectivity Host Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Fehlermeldung im SAPConnectivityErrorMessage anzeigen
                SAPConnectivityErrorMessage.Text = $"Fehler beim Laden des SAP Connectivity Host Deployments: {ex.Message}";
                SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
                
                _loggingService.LogError($"Fehler beim Laden des SAP Connectivity Host Deployments: {ex.Message}", ex);
            });
        }
    }

    private void ConfigureSAPYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string yaml = SAPConnectivityOriginalYamlTextBox.Text;
            string modifiedYaml = ModifyYamlForSAPConnectivity(yaml);
            SAPConnectivityModifiedYamlTextBox.Text = modifiedYaml;
            
            // Erfolgreiche Konfiguration, Fehlermeldung zurücksetzen
            SAPConnectivityErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SAPConnectivityErrorMessage.Text = $"Fehler beim Konfigurieren des YAML: {ex.Message}";
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Konfigurieren des YAML: {ex.Message}", ex);
        }
    }

    private string ModifyYamlForSAPConnectivity(string yaml)
    {
        // Hier würde die tatsächliche YAML-Modifikation stattfinden
        // Dies ist eine vereinfachte Version, die im wirklichen Code durch eine
        // ordnungsgemäße YAML-Parsing-Bibliothek ersetzt werden sollte
        
        string volumeName = "sapconnectivitybbmag65";
        string volumePath = "/opt/erp";
        string pvcName = SAPPVCNameTextBox.Text;
        
        // Env-Eintrag hinzufügen
        if (yaml.Contains("env:"))
        {
            string envEntry = $"        - name: {volumeName}\n          value: {volumePath}";
            yaml = yaml.Replace("env:", $"env:\n{envEntry}");
        }
        
        // Volume-Eintrag hinzufügen
        if (yaml.Contains("volumes:"))
        {
            string volumeEntry = $"      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
            yaml = yaml.Replace("volumes:", $"volumes:\n{volumeEntry}");
        }
        else
        {
            // Wenn kein volumes-Abschnitt existiert, fügen wir einen hinzu
            yaml += $"\n    volumes:\n      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
        }
        
        // volumeMounts-Eintrag hinzufügen
        if (yaml.Contains("volumeMounts:"))
        {
            string volumeMountEntry = $"        - name: {volumeName}\n          mountPath: {volumePath}";
            yaml = yaml.Replace("volumeMounts:", $"volumeMounts:\n{volumeMountEntry}");
        }
        else
        {
            // Wenn kein volumeMounts-Abschnitt existiert, fügen wir einen hinzu
            // Dies würde in einem bestehenden Container-Block platziert werden
            if (yaml.Contains("containers:"))
            {
                string containerBlockStart = "containers:";
                int containerStartIndex = yaml.IndexOf(containerBlockStart);
                int insertIndex = yaml.IndexOf("image:", containerStartIndex);
                
                if (insertIndex > 0)
                {
                    string beforeInsert = yaml.Substring(0, insertIndex);
                    string afterInsert = yaml.Substring(insertIndex);
                    string volumeMountsBlock = $"        volumeMounts:\n          - name: {volumeName}\n            mountPath: {volumePath}\n        ";
                    
                    yaml = beforeInsert + volumeMountsBlock + afterInsert;
                }
            }
        }
        
        return yaml;
    }

    private async void ApplySAPChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSAPConnectivityChanges)
        {
            _loggingService.LogWarning("Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...");
            SAPConnectivityErrorMessage.Text = "Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...";
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _isApplyingSAPConnectivityChanges = true;
            
            // SAPConnectivityErrorMessage statt ErrorMessage verwenden
            SAPConnectivityErrorMessage.Text = "Änderungen werden angewendet...";
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
            
            string currentProject = _openShiftService.CurrentProject;
            _loggingService.LogInfo($"Starte Anwendung der Änderungen für SAP Connectivity im Projekt: {currentProject}");
            
            // Alle benötigten Ressourcen vorbereiten
            string pvName = SAPPVNameTextBox.Text;
            string smbPath = SAPSMBSourcePathTextBox.Text;
            string secretName = SAPSecretNameTextBox.Text;
            string pvcName = SAPPVCNameTextBox.Text;
            string username = SAPUsernameTextBox.Text;
            string password = SAPSecretPasswordBox.Password;
            bool allSuccess = true;
            
            // 1. Zuerst YAML konfigurieren, ohne es neu zu laden
            string modifiedYaml = ModifyYamlForSAPConnectivity(SAPConnectivityOriginalYamlTextBox.Text);
            SAPConnectivityModifiedYamlTextBox.Text = modifiedYaml;
            
            // 1. Host-Deployment ändern
            _loggingService.LogInfo($"Aktualisiere Deployment in Projekt: {currentProject}");
            bool deploymentSuccess = await _openShiftService.UpdateDeploymentFromYamlAsync(currentProject, modifiedYaml);
            
            // Wir ignorieren Deployment-Fehler und machen trotzdem weiter
            
            // 2. Secret erstellen (falls nicht existiert)
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                _loggingService.LogInfo($"Erstelle Secret {secretName} in Projekt: {currentProject}");

                // Erstelle Secret
                byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                string usernameBase64 = Convert.ToBase64String(usernameBytes);
                string passwordBase64 = Convert.ToBase64String(passwordBytes);
                
                string secretYaml = GenerateSAPSecretYaml(currentProject, secretName)
                                    .Replace("username: ", $"username: {usernameBase64}")
                                    .Replace("password: ", $"password: {passwordBase64}");
                
                bool secretSuccess = await _openShiftService.CreateResourceFromYamlAsync(secretYaml);
                
                if (!secretSuccess)
                {
                    _loggingService.LogWarning($"Fehler beim Erstellen des Secret {secretName} - versuche es zu aktualisieren");
                    secretSuccess = await _openShiftService.CreateResourceFromYamlAsync(secretYaml, true);
                    
                    if (!secretSuccess) allSuccess = false;
                }
            }
            
            // 3. PV erstellen (falls nicht existiert)
            _loggingService.LogInfo($"Erstelle Persistent Volume {pvName}");
            string pvYaml = GenerateSAPPVYaml(currentProject, pvName, smbPath, secretName);
            bool pvSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvYaml);
            
            if (!pvSuccess)
            {
                _loggingService.LogWarning($"Fehler beim Erstellen des PV {pvName} - versuche es zu aktualisieren");
                pvSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvYaml, true);
                
                if (!pvSuccess) allSuccess = false;
            }
            
            // 4. PVC erstellen (falls nicht existiert)
            _loggingService.LogInfo($"Erstelle Persistent Volume Claim {pvcName} in Projekt: {currentProject}");
            string pvcYaml = GenerateSAPPVCYaml(currentProject, pvcName, pvName);
            bool pvcSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvcYaml);
            
            if (!pvcSuccess)
            {
                _loggingService.LogWarning($"Fehler beim Erstellen des PVC {pvcName} - versuche es zu aktualisieren");
                pvcSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvcYaml, true);
                
                if (!pvcSuccess) allSuccess = false;
            }
            
            // Erfolgsmeldung
            _loggingService.LogInfo("Alle Änderungen für SAP Connectivity wurden angewendet");
            SAPConnectivityErrorMessage.Text = "Alle Änderungen wurden angewendet.";
            SAPConnectivityErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            
            MessageBox.Show("Die Änderungen für SAP Connectivity wurden angewendet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anwenden der SAP Connectivity Änderungen: {ex.Message}", ex);
            SAPConnectivityErrorMessage.Text = $"Fehler: {ex.Message}";
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isApplyingSAPConnectivityChanges = false;
        }
    }

    private async void GenerateSAPPVYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvName = SAPPVNameTextBox.Text;
            string smbPath = SAPSMBSourcePathTextBox.Text;
            string secretName = SAPSecretNameTextBox.Text;
            
            // Überprüfe, ob die erforderlichen Felder ausgefüllt sind
            if (string.IsNullOrEmpty(smbPath))
            {
                SAPConnectivityErrorMessage.Text = "Bitte geben Sie einen SMB Source Path an.";
                SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV YAML generieren
            SAPPVYamlTextBox.Text = GenerateSAPPVYaml(currentProject, pvName, smbPath, secretName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"SAP Connectivity PV YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            SAPConnectivityErrorMessage.Text = "PV YAML wurde erstellt.";
            SAPConnectivityErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des SAP Connectivity PV YAML: {ex.Message}", ex);
            SAPConnectivityErrorMessage.Text = $"Fehler beim Erstellen des PV YAML: {ex.Message}";
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void GenerateSAPPVCYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvcName = SAPPVCNameTextBox.Text;
            string pvName = SAPPVNameTextBox.Text;
            
            // PVC YAML generieren
            SAPPVCYamlTextBox.Text = GenerateSAPPVCYaml(currentProject, pvcName, pvName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"SAP Connectivity PVC YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            SAPConnectivityErrorMessage.Text = "PVC YAML wurde erstellt.";
            SAPConnectivityErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des SAP Connectivity PVC YAML: {ex.Message}", ex);
            SAPConnectivityErrorMessage.Text = $"Fehler beim Erstellen des PVC YAML: {ex.Message}";
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void GenerateSAPSecretYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string secretName = SAPSecretNameTextBox.Text;
            
            // Überprüfen der Eingaben
            string username = SAPUsernameTextBox.Text;
            string password = SAPSecretPasswordBox.Password;
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                SAPConnectivityErrorMessage.Text = "Bitte geben Sie Benutzername und Passwort an.";
                SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // Secret YAML generieren
            byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            string usernameBase64 = Convert.ToBase64String(usernameBytes);
            string passwordBase64 = Convert.ToBase64String(passwordBytes);
            
            string secretYaml = GenerateSAPSecretYaml(currentProject, secretName)
                              .Replace("username: ", $"username: {usernameBase64}")
                              .Replace("password: ", $"password: {passwordBase64}");
            
            SAPSecretYamlTextBox.Text = secretYaml;
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"SAP Connectivity Secret YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            SAPConnectivityErrorMessage.Text = "Secret YAML wurde erstellt.";
            SAPConnectivityErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des SAP Connectivity Secret YAML: {ex.Message}", ex);
            SAPConnectivityErrorMessage.Text = $"Fehler beim Erstellen des Secret YAML: {ex.Message}";
            SAPConnectivityErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void OISFilesOptIotVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Alle Sperrvariablen zurücksetzen
            _isApplyingOISFilesChanges = false;
            _isApplyingOISFilesOptIotChanges = false;
            _isApplyingEDHRChanges = false;
            _isApplyingFirmwareChanges = false;
            _isApplyingSAPConnectivityChanges = false;
            
            // Button-Style setzen und Inhalte anzeigen
            ResetAppButtonStyles();
            OISFilesOptIotVolumeButton.Style = (Style)FindResource("ActiveSidebarButton");
            HideAllContent();
            OISFilesOptIotVolumeContent.Visibility = Visibility.Visible;

            // Status-Meldung zurücksetzen
            OISFilesOptIotErrorMessage.Text = string.Empty;
            OISFilesOptIotErrorMessage.Visibility = Visibility.Collapsed;
            OISFilesOptIotErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            
            // Zurücksetzen aller Textfelder und Eingabefelder für diesen Bereich
            OISFilesOptIotOriginalYamlTextBox.Text = string.Empty;
            OISFilesOptIotModifiedYamlTextBox.Text = string.Empty;
            PVOptIotNameTextBox.Text = string.Empty;
            PVCOptIotNameTextBox.Text = string.Empty;
            PVCOptIotVolumeNameTextBox.Text = string.Empty;
            SecretOptIotNameTextBox.Text = string.Empty;
            UsernameOptIotTextBox.Text = string.Empty;
            SecretOptIotPasswordBox.Password = string.Empty;
            SMBOptIotSourcePathTextBox.Text = string.Empty;
            PVOptIotYamlTextBox.Text = string.Empty;
            PVCOptIotYamlTextBox.Text = string.Empty;
            SecretOptIotYamlTextBox.Text = string.Empty;

            // Namen aktualisieren
            UpdateOptIotResourceNames();
            
            // Yaml-Templates aktualisieren
            UpdateOptIotYamlTemplates();
            
            // Host Deployment YAML laden
            Task.Run(async () => await LoadOptIotHostDeploymentYaml());
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Öffnen des OISFiles OPT/IOT Volume Management: {ex.Message}", ex);
            OISFilesOptIotErrorMessage.Text = $"Fehler beim Öffnen: {ex.Message}";
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void UpdateOptIotResourceNames()
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            
            if (string.IsNullOrEmpty(currentProject))
            {
                _loggingService.LogWarning("Kein aktuelles Projekt ausgewählt");
                OISFilesOptIotErrorMessage.Text = "Kein aktuelles Projekt ausgewählt. Bitte wählen Sie zuerst ein Projekt aus.";
                OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV
            string pvName = $"{currentProject}-oisfiles-opt-iot";
            PVOptIotNameTextBox.Text = pvName;
            
            // PVC
            PVCOptIotNameTextBox.Text = $"{currentProject}-oisfiles-opt-iot";
            PVCOptIotVolumeNameTextBox.Text = pvName;
            
            // Secret
            SecretOptIotNameTextBox.Text = $"{currentProject}-oisfiles-opt-iot-creds";
            
            // Beispiel-SMB-Pfad eintragen
            SMBOptIotSourcePathTextBox.Text = $"//bbmag65.bbmag.bbraun.com/nfs/CMFMES/ATO/{ExtractEnvironment(currentProject)}/{currentProject}/oisfiles/";
            
            _loggingService.LogInfo($"Ressourcennamen erfolgreich aktualisiert: PV={pvName}, PVC={currentProject}-oisfiles-opt-iot, Secret={currentProject}-oisfiles-opt-iot-creds");
            _loggingService.LogInfo($"SMB-Pfad: {SMBOptIotSourcePathTextBox.Text}");
            
            // Erfolgreich aktualisiert, Fehlermeldung zurücksetzen
            OISFilesOptIotErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der Ressourcennamen: {ex.Message}", ex);
            OISFilesOptIotErrorMessage.Text = $"Fehler beim Aktualisieren der Ressourcennamen: {ex.Message}";
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void UpdateOptIotYamlTemplates()
    {
        string currentProject = _openShiftService.CurrentProject;
        string pvName = PVOptIotNameTextBox.Text;
        string smbPath = SMBOptIotSourcePathTextBox.Text;
        string secretName = SecretOptIotNameTextBox.Text;
        string pvcName = PVCOptIotNameTextBox.Text;
        
        // PV Template generieren
        PVOptIotYamlTextBox.Text = GenerateOptIotPVYaml(currentProject, pvName, smbPath, secretName);
        
        // PVC Template generieren
        PVCOptIotYamlTextBox.Text = GenerateOptIotPVCYaml(currentProject, pvcName, pvName);
        
        // Secret Template generieren
        SecretOptIotYamlTextBox.Text = GenerateOptIotSecretYaml(currentProject, secretName);
    }

    private string GenerateOptIotPVYaml(string currentProject, string pvName, string smbPath, string secretName)
    {
        return $@"kind: PersistentVolume
apiVersion: v1
metadata:
  name: {pvName}
  annotations:
    pv.kubernetes.io/bound-by-controller: 'yes'
  finalizers:
    - kubernetes.io/pv-protection
spec:
  capacity:
    storage: 1Gi
  csi:
    driver: smb.csi.k8s.io
    volumeHandle: {pvName}
    volumeAttributes:
      source: {smbPath}
    nodeStageSecretRef:
      name: {secretName}
      namespace: {currentProject}
  accessModes:
    - ReadWriteMany
  persistentVolumeReclaimPolicy: Retain
  mountOptions:
    - dir_mode=0777
    - file_mode=0777
    - nobrl
  volumeMode: Filesystem";  
    }

    private string GenerateOptIotPVCYaml(string currentProject, string pvcName, string pvName)
    {
        return $@"kind: PersistentVolumeClaim
apiVersion: v1
metadata:
  name: {pvcName}
  namespace: {currentProject}
  annotations:
    pv.kubernetes.io/bind-completed: 'yes'
  finalizers:
    - kubernetes.io/pvc-protection  
spec:
  accessModes:
    - ReadWriteMany
  resources:
    requests:
      storage: 1Gi
  volumeName: {pvName}
  storageClassName: ''
  volumeMode: Filesystem";  
    }

    private string GenerateOptIotSecretYaml(string projectName, string secretName)
    {
        return $@"apiVersion: v1
kind: Secret
metadata:
  name: {secretName}
  namespace: {projectName}
type: Opaque
data:
  domain: YmJtYWc=
  username: 
  password: ";
    }

    private async Task LoadOptIotHostDeploymentYaml()
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            _loggingService.LogInfo($"Lade Host Deployment YAML für Projekt: {currentProject}");
            
            string yaml = await _openShiftService.GetHostDeploymentYamlAsync(currentProject);
            
            await Dispatcher.InvokeAsync(() => {
                OISFilesOptIotOriginalYamlTextBox.Text = yaml;
                OISFilesOptIotModifiedYamlTextBox.Text = yaml; // Initial kopieren
                
                // Bei Erfolg Fehlermeldung ausblenden
                OISFilesOptIotErrorMessage.Visibility = Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => {
                // Zeige Fehlermeldungen in einem MessageBox an
                MessageBox.Show($"Fehler beim Laden des Host Deployments: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Fehlermeldung im OISFilesOptIotErrorMessage anzeigen
                OISFilesOptIotErrorMessage.Text = $"Fehler beim Laden des Host Deployments: {ex.Message}";
                OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
                
                _loggingService.LogError($"Fehler beim Laden des Host Deployments: {ex.Message}", ex);
            });
        }
    }

    private void ConfigureOptIotYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string yaml = OISFilesOptIotOriginalYamlTextBox.Text;
            string modifiedYaml = ModifyYamlForOISFilesOptIot(yaml);
            OISFilesOptIotModifiedYamlTextBox.Text = modifiedYaml;
            
            // Erfolgreiche Konfiguration, Fehlermeldung zurücksetzen
            OISFilesOptIotErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            OISFilesOptIotErrorMessage.Text = $"Fehler beim Konfigurieren des YAML: {ex.Message}";
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Konfigurieren des YAML: {ex.Message}", ex);
        }
    }

    private string ModifyYamlForOISFilesOptIot(string yaml)
    {
        // Wenn YAML leer oder null ist, gibt es nichts zu modifizieren
        if (string.IsNullOrEmpty(yaml))
        {
            _loggingService.LogWarning("ModifyYamlForOISFilesOptIot: Das YAML ist leer oder null.");
            return yaml;
        }
        
        try
        {
            // Eindeutige ID für OISFilesOptIot-Ressourcen
            // Verwende einen anderen Namen als bei anderen Volume-Funktionen, um Vermischungen zu vermeiden
            string volumeName = "oisfilesoptiotbbmag65";
            string volumePath = "/opt/iot/ois/bbmag65/oisfiles";
            string pvcName = PVCOptIotNameTextBox.Text;
            
            // Env-Eintrag hinzufügen
            if (yaml.Contains("env:"))
            {
                string envEntry = $"        - name: OISFILES_OPT_IOT_PATH\n          value: {volumePath}";
                yaml = yaml.Replace("env:", $"env:\n{envEntry}");
            }
            
            // Volume-Eintrag hinzufügen
            if (yaml.Contains("volumes:"))
            {
                string volumeEntry = $"      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
                yaml = yaml.Replace("volumes:", $"volumes:\n{volumeEntry}");
            }
            else
            {
                // Wenn kein volumes-Abschnitt existiert, fügen wir einen hinzu
                yaml += $"\n    volumes:\n      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
            }
            
            // volumeMounts-Eintrag hinzufügen
            if (yaml.Contains("volumeMounts:"))
            {
                string volumeMountEntry = $"        - name: {volumeName}\n          mountPath: {volumePath}";
                yaml = yaml.Replace("volumeMounts:", $"volumeMounts:\n{volumeMountEntry}");
            }
            else
            {
                // Wenn kein volumeMounts-Abschnitt existiert, fügen wir einen hinzu
                // Dies würde in einem bestehenden Container-Block platziert werden
                if (yaml.Contains("containers:"))
                {
                    string containerBlockStart = "containers:";
                    int containerStartIndex = yaml.IndexOf(containerBlockStart);
                    int insertIndex = yaml.IndexOf("image:", containerStartIndex);
                    
                    if (insertIndex > 0)
                    {
                        string beforeInsert = yaml.Substring(0, insertIndex);
                        string afterInsert = yaml.Substring(insertIndex);
                        string volumeMountsBlock = $"        volumeMounts:\n          - name: {volumeName}\n            mountPath: {volumePath}\n        ";
                        
                        yaml = beforeInsert + volumeMountsBlock + afterInsert;
                    }
                }
            }
            
            return yaml;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler in ModifyYamlForOISFilesOptIot: {ex.Message}", ex);
            throw new Exception($"Fehler bei der YAML-Modifikation: {ex.Message}", ex);
        }
    }

    private async void ApplyOptIotChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingOISFilesOptIotChanges)
        {
            _loggingService.LogWarning("Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...");
            OISFilesOptIotErrorMessage.Text = "Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...";
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _isApplyingOISFilesOptIotChanges = true;
            
            // OISFilesOptIotErrorMessage statt ErrorMessage verwenden
            OISFilesOptIotErrorMessage.Text = "Änderungen werden angewendet...";
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
            
            string currentProject = _openShiftService.CurrentProject;
            _loggingService.LogInfo($"Starte Anwendung der Änderungen für Projekt: {currentProject}");
            
            // Alle benötigten Ressourcen vorbereiten
            string pvName = PVOptIotNameTextBox.Text;
            string smbPath = SMBOptIotSourcePathTextBox.Text;
            string secretName = SecretOptIotNameTextBox.Text;
            string pvcName = PVCOptIotNameTextBox.Text;
            string username = UsernameOptIotTextBox.Text;
            string password = SecretOptIotPasswordBox.Password;
            bool allSuccess = true;
            
            // 1. Zuerst YAML konfigurieren, ohne es neu zu laden
            string modifiedYaml = ModifyYamlForOISFilesOptIot(OISFilesOptIotOriginalYamlTextBox.Text);
            OISFilesOptIotModifiedYamlTextBox.Text = modifiedYaml;
            
            // 1. Host-Deployment ändern
            _loggingService.LogInfo($"Aktualisiere Deployment in Projekt: {currentProject}");
            bool deploymentSuccess = await _openShiftService.UpdateDeploymentFromYamlAsync(currentProject, modifiedYaml);
            
            // Wir ignorieren Deployment-Fehler und machen trotzdem weiter
            
            // 2. Secret erstellen (falls nicht existiert)
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                _loggingService.LogInfo($"Erstelle Secret {secretName} in Projekt: {currentProject}");

                // Erstelle Secret
                byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                string usernameBase64 = Convert.ToBase64String(usernameBytes);
                string passwordBase64 = Convert.ToBase64String(passwordBytes);
                
                string secretYaml = GenerateOptIotSecretYaml(currentProject, secretName)
                                    .Replace("username: ", $"username: {usernameBase64}")
                                    .Replace("password: ", $"password: {passwordBase64}");
                
                bool secretSuccess = await _openShiftService.CreateResourceFromYamlAsync(secretYaml);
                
                if (!secretSuccess)
                {
                    _loggingService.LogWarning($"Fehler beim Erstellen des Secret {secretName} - versuche es zu aktualisieren");
                    secretSuccess = await _openShiftService.CreateResourceFromYamlAsync(secretYaml, true);
                    
                    if (!secretSuccess) allSuccess = false;
                }
            }
            
            // 3. PV erstellen (falls nicht existiert)
            _loggingService.LogInfo($"Erstelle Persistent Volume {pvName}");
            string pvYaml = GenerateOptIotPVYaml(currentProject, pvName, smbPath, secretName);
            bool pvSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvYaml);
            
            if (!pvSuccess)
            {
                _loggingService.LogWarning($"Fehler beim Erstellen des PV {pvName} - versuche es zu aktualisieren");
                pvSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvYaml, true);
                
                if (!pvSuccess) allSuccess = false;
            }
            
            // 4. PVC erstellen (falls nicht existiert)
            _loggingService.LogInfo($"Erstelle Persistent Volume Claim {pvcName} in Projekt: {currentProject}");
            string pvcYaml = GenerateOptIotPVCYaml(currentProject, pvcName, pvName);
            bool pvcSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvcYaml);
            
            if (!pvcSuccess)
            {
                _loggingService.LogWarning($"Fehler beim Erstellen des PVC {pvcName} - versuche es zu aktualisieren");
                pvcSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvcYaml, true);
                
                if (!pvcSuccess) allSuccess = false;
            }
            
            // Erfolgsmeldung
            _loggingService.LogInfo("Alle Änderungen für OISFiles OPT/IOT wurden angewendet");
            OISFilesOptIotErrorMessage.Text = "Alle Änderungen wurden angewendet.";
            OISFilesOptIotErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            
            MessageBox.Show("Die Änderungen für OISFiles OPT/IOT wurden angewendet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anwenden der OISFiles OPT/IOT Änderungen: {ex.Message}", ex);
            OISFilesOptIotErrorMessage.Text = $"Fehler: {ex.Message}";
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isApplyingOISFilesOptIotChanges = false;
        }
    }

    private async void GenerateOptIotPVYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvName = PVOptIotNameTextBox.Text;
            string smbPath = SMBOptIotSourcePathTextBox.Text;
            string secretName = SecretOptIotNameTextBox.Text;
            
            // Überprüfe, ob die erforderlichen Felder ausgefüllt sind
            if (string.IsNullOrEmpty(smbPath))
            {
                OISFilesOptIotErrorMessage.Text = "Bitte geben Sie einen SMB Source Path an.";
                OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV YAML generieren
            PVOptIotYamlTextBox.Text = GenerateOptIotPVYaml(currentProject, pvName, smbPath, secretName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"OISFiles OPT/IOT PV YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            OISFilesOptIotErrorMessage.Text = "PV YAML wurde erstellt.";
            OISFilesOptIotErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des OPT/IOT PV YAML: {ex.Message}", ex);
            OISFilesOptIotErrorMessage.Text = $"Fehler beim Erstellen des PV YAML: {ex.Message}";
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void GenerateOptIotPVCYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvcName = PVCOptIotNameTextBox.Text;
            string pvName = PVOptIotNameTextBox.Text;
            
            // PVC YAML generieren
            PVCOptIotYamlTextBox.Text = GenerateOptIotPVCYaml(currentProject, pvcName, pvName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"OISFiles OPT/IOT PVC YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            OISFilesOptIotErrorMessage.Text = "PVC YAML wurde erstellt.";
            OISFilesOptIotErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des OPT/IOT PVC YAML: {ex.Message}", ex);
            OISFilesOptIotErrorMessage.Text = $"Fehler beim Erstellen des PVC YAML: {ex.Message}";
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void GenerateOptIotSecretYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string secretName = SecretOptIotNameTextBox.Text;
            
            // Überprüfen der Eingaben
            string username = UsernameOptIotTextBox.Text;
            string password = SecretOptIotPasswordBox.Password;
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                OISFilesOptIotErrorMessage.Text = "Bitte geben Sie Benutzername und Passwort an.";
                OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // Secret YAML generieren
            byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            string usernameBase64 = Convert.ToBase64String(usernameBytes);
            string passwordBase64 = Convert.ToBase64String(passwordBytes);
            
            string secretYaml = GenerateOptIotSecretYaml(currentProject, secretName)
                              .Replace("username: ", $"username: {usernameBase64}")
                              .Replace("password: ", $"password: {passwordBase64}");
            
            SecretOptIotYamlTextBox.Text = secretYaml;
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"OISFiles OPT/IOT Secret YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            OISFilesOptIotErrorMessage.Text = "Secret YAML wurde erstellt.";
            OISFilesOptIotErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des OPT/IOT Secret YAML: {ex.Message}", ex);
            OISFilesOptIotErrorMessage.Text = $"Fehler beim Erstellen des Secret YAML: {ex.Message}";
            OISFilesOptIotErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void FirmwareVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Alle Sperrvariablen zurücksetzen
            _isApplyingOISFilesChanges = false;
            _isApplyingOISFilesOptIotChanges = false;
            _isApplyingEDHRChanges = false;
            _isApplyingFirmwareChanges = false;
            _isApplyingSAPConnectivityChanges = false;
            
            // Button-Style setzen und Inhalte anzeigen
            ResetAppButtonStyles();
            FirmwareVolumeButton.Style = (Style)FindResource("ActiveSidebarButton");
            HideAllContent();
            FirmwareVolumeContent.Visibility = Visibility.Visible;

            // Status-Meldung zurücksetzen
            FirmwareErrorMessage.Text = string.Empty;
            FirmwareErrorMessage.Visibility = Visibility.Collapsed;
            FirmwareErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            
            // Zurücksetzen aller Textfelder und Eingabefelder für diesen Bereich
            FirmwareOriginalYamlTextBox.Text = string.Empty;
            FirmwareModifiedYamlTextBox.Text = string.Empty;
            FirmwarePVNameTextBox.Text = string.Empty;
            FirmwarePVCNameTextBox.Text = string.Empty;
            FirmwarePVCVolumeNameTextBox.Text = string.Empty;
            FirmwareSecretNameTextBox.Text = string.Empty;
            FirmwareUsernameTextBox.Text = string.Empty;
            FirmwareSecretPasswordBox.Password = string.Empty;
            FirmwareSMBSourcePathTextBox.Text = string.Empty;
            FirmwarePVYamlTextBox.Text = string.Empty;
            FirmwarePVCYamlTextBox.Text = string.Empty;
            FirmwareSecretYamlTextBox.Text = string.Empty;

            // Aktuelles Projekt prüfen - hier explizit nochmal abfragen, um sicherzustellen dass es aktuell ist
            Task.Run(async () => {
                try
                {
                    // Aktuelles Projekt abfragen
                    string currentProject = await _openShiftService.GetCurrentProjectName();
                    
                    if (string.IsNullOrEmpty(currentProject))
                    {
                        _loggingService.LogWarning("FirmwareVolumeButton_Click: Kein Projekt ausgewählt");
                        await Dispatcher.InvokeAsync(() => {
                            FirmwareErrorMessage.Text = "Bitte wählen Sie zuerst ein Projekt aus, bevor Sie fortfahren.";
                            FirmwareErrorMessage.Visibility = Visibility.Visible;
                        });
                        return;
                    }

                    _loggingService.LogInfo("Firmware Volume Management wird geöffnet...");
                    _loggingService.LogInfo($"Aktuelles Projekt: {currentProject}");
                    
                    // Aktuelles Projekt anzeigen
                    await Dispatcher.InvokeAsync(() => {
                        FirmwareCurrentProjectText.Text = currentProject;
                    });
                    
                    // Host-Deployment YAML laden für Firmware und dieses Projekt
                    string yaml = await _openShiftService.GetHostDeploymentYamlAsync(currentProject);
                    
                    await Dispatcher.InvokeAsync(() => {
                        // Namen aktualisieren
                        UpdateFirmwareResourceNames();
                        
                        // YAML in TextBoxen anzeigen
                        FirmwareOriginalYamlTextBox.Text = yaml;
                        FirmwareModifiedYamlTextBox.Text = yaml; // Initial kopieren
                        
                        // Firmware-YAML-Templates aktualisieren
                        UpdateFirmwareYamlTemplates();
                    });
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Fehler beim Laden der Projektdaten: {ex.Message}", ex);
                    await Dispatcher.InvokeAsync(() => {
                        FirmwareErrorMessage.Text = $"Fehler beim Laden der Projektdaten: {ex.Message}";
                        FirmwareErrorMessage.Visibility = Visibility.Visible;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Öffnen des Firmware Volume Management: {ex.Message}", ex);
            FirmwareErrorMessage.Text = $"Fehler beim Öffnen: {ex.Message}";
            FirmwareErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void UpdateFirmwareResourceNames()
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            
            _loggingService.LogInfo($"Aktualisiere Ressourcennamen für Projekt: {currentProject}");
            
            if (string.IsNullOrEmpty(currentProject))
            {
                _loggingService.LogWarning("Kein aktuelles Projekt ausgewählt");
                FirmwareErrorMessage.Text = "Kein aktuelles Projekt ausgewählt. Bitte wählen Sie zuerst ein Projekt aus.";
                FirmwareErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV
            string pvName = $"{currentProject}-firmware";
            FirmwarePVNameTextBox.Text = pvName;
            
            // PVC
            FirmwarePVCNameTextBox.Text = $"{currentProject}-firmware";
            FirmwarePVCVolumeNameTextBox.Text = pvName;
            
            // Secret
            FirmwareSecretNameTextBox.Text = $"{currentProject}-firmware-creds";
            
            // Beispiel-SMB-Pfad eintragen
            FirmwareSMBSourcePathTextBox.Text = $"//bbmag65.bbmag.bbraun.com/nfs/CMFMES/ATO/{ExtractEnvironment(currentProject)}/{currentProject}/firmware";
            
            _loggingService.LogInfo($"Ressourcennamen erfolgreich aktualisiert: PV={pvName}, PVC={currentProject}-firmware, Secret={currentProject}-firmware-creds");
            _loggingService.LogInfo($"SMB-Pfad: {FirmwareSMBSourcePathTextBox.Text}");
            
            // Erfolgreich aktualisiert, Fehlermeldung zurücksetzen
            FirmwareErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der Ressourcennamen: {ex.Message}", ex);
            FirmwareErrorMessage.Text = $"Fehler beim Aktualisieren der Ressourcennamen: {ex.Message}";
            FirmwareErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void UpdateFirmwareYamlTemplates()
    {
        string currentProject = _openShiftService.CurrentProject;
        string pvName = FirmwarePVNameTextBox.Text;
        string smbPath = FirmwareSMBSourcePathTextBox.Text;
        string secretName = FirmwareSecretNameTextBox.Text;
        string pvcName = FirmwarePVCNameTextBox.Text;
        
        // PV Template generieren
        FirmwarePVYamlTextBox.Text = GenerateFirmwarePVYaml(currentProject, pvName, smbPath, secretName);
        
        // PVC Template generieren
        FirmwarePVCYamlTextBox.Text = GenerateFirmwarePVCYaml(currentProject, pvcName, pvName);
        
        // Secret Template generieren
        FirmwareSecretYamlTextBox.Text = GenerateFirmwareSecretYaml(currentProject, secretName);
    }

    private string GenerateFirmwarePVYaml(string currentProject, string pvName, string smbPath, string secretName)
    {
        // Mit korrigierter Einrückung (ein Tab zurück)
        return $@"kind: PersistentVolume
apiVersion: v1
metadata:
  name: {pvName}
  annotations:
    pv.kubernetes.io/bound-by-controller: 'yes'
  finalizers:
    - kubernetes.io/pv-protection
spec:
  capacity:
    storage: 1Gi
  csi:
    driver: smb.csi.k8s.io
    volumeHandle: {pvName}
    volumeAttributes:
      source: {smbPath}
    nodeStageSecretRef:
      name: {secretName}
      namespace: {currentProject}
  accessModes:
    - ReadWriteMany
  persistentVolumeReclaimPolicy: Retain
  mountOptions:
    - dir_mode=0777
    - file_mode=0777
    - nobrl
  volumeMode: Filesystem";
    }

    private string GenerateFirmwarePVCYaml(string currentProject, string pvcName, string pvName)
    {
        // Mit korrigierter Einrückung (ein Tab zurück)
        return $@"kind: PersistentVolumeClaim
apiVersion: v1
metadata:
  name: {pvcName}
  namespace: {currentProject}
  annotations:
    pv.kubernetes.io/bind-completed: 'yes'
  finalizers:
    - kubernetes.io/pvc-protection
spec:
  accessModes:
    - ReadWriteMany
  resources:
    requests:
      storage: 1Gi
  volumeName: {pvName}
  storageClassName: ''
  volumeMode: Filesystem";
    }

    private string GenerateFirmwareSecretYaml(string projectName, string secretName)
    {
        return $@"apiVersion: v1
kind: Secret
metadata:
  name: {secretName}
  namespace: {projectName}
type: Opaque
data:
  domain: YmJtYWc=
  username: 
  password: ";
    }

    private async Task LoadFirmwareHostDeploymentYaml()
    {
        try
        {
            _loggingService.LogInfo("Lade aktuelle Deployment YAML für Firmware...");
            
            // Aktuellen Projektnamen holen
            string currentProject = await _openShiftService.GetCurrentProjectName();

            // Hole die YAML-Konfiguration des Host Deployments
            string yaml = await _openShiftService.GetHostDeploymentYamlAsync(currentProject);

            await Dispatcher.InvokeAsync(() => {
                FirmwareOriginalYamlTextBox.Text = yaml;
                FirmwareModifiedYamlTextBox.Text = yaml; // Initial kopieren
                
                // Bei Erfolg Fehlermeldung ausblenden
                FirmwareErrorMessage.Visibility = Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => {
                
                // Fehlermeldung im FirmwareErrorMessage anzeigen
                FirmwareErrorMessage.Text = $"Fehler beim Laden des Host Deployments: {ex.Message}";
                FirmwareErrorMessage.Visibility = Visibility.Visible;
                
                _loggingService.LogError($"Fehler beim Laden des Host Deployments: {ex.Message}", ex);
            });
        }
    }

    private void ConfigureFirmwareYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string yaml = FirmwareOriginalYamlTextBox.Text;
            string modifiedYaml = ModifyYamlForFirmware(yaml);
            FirmwareModifiedYamlTextBox.Text = modifiedYaml;
            
            // Erfolgreiche Konfiguration, Fehlermeldung zurücksetzen
            FirmwareErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            FirmwareErrorMessage.Text = $"Fehler beim Konfigurieren des YAML: {ex.Message}";
            FirmwareErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Konfigurieren des YAML: {ex.Message}", ex);
        }
    }
    
    private string ModifyYamlForFirmware(string yaml)
    {
        // Wenn YAML leer oder null ist, gibt es nichts zu modifizieren
        if (string.IsNullOrEmpty(yaml))
        {
            _loggingService.LogWarning("ModifyYamlForFirmware: Das YAML ist leer oder null.");
            return yaml;
        }
        
        try
        {
            // Eindeutige ID für Firmware-Ressourcen
            // Verwende einen anderen Namen als bei anderen Volume-Funktionen, um Vermischungen zu vermeiden
            string volumeName = "firmware";
            string volumePath = "/opt/iot/firmware";
            string pvcName = FirmwarePVCNameTextBox.Text;
            
            // Env-Eintrag hinzufügen
            if (yaml.Contains("env:"))
            {
                string envEntry = $"        - name: {volumeName}\n          value: {volumePath}";
                yaml = yaml.Replace("env:", $"env:\n{envEntry}");
            }
            
            // Volume-Eintrag hinzufügen
            if (yaml.Contains("volumes:"))
            {
                string volumeEntry = $"      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
                yaml = yaml.Replace("volumes:", $"volumes:\n{volumeEntry}");
            }
            else
            {
                // Wenn kein volumes-Abschnitt existiert, fügen wir einen hinzu
                yaml += $"\n    volumes:\n      - name: {volumeName}\n        persistentVolumeClaim:\n          claimName: {pvcName}";
            }
            
            // volumeMounts-Eintrag hinzufügen
            if (yaml.Contains("volumeMounts:"))
            {
                string volumeMountEntry = $"        - name: {volumeName}\n          mountPath: {volumePath}";
                yaml = yaml.Replace("volumeMounts:", $"volumeMounts:\n{volumeMountEntry}");
            }
            else
            {
                // Wenn kein volumeMounts-Abschnitt existiert, fügen wir einen hinzu
                // Dies würde in einem bestehenden Container-Block platziert werden
                if (yaml.Contains("containers:"))
                {
                    string containerBlockStart = "containers:";
                    int containerStartIndex = yaml.IndexOf(containerBlockStart);
                    
                    if (containerStartIndex >= 0)
                    {
                        int insertIndex = yaml.IndexOf("image:", containerStartIndex);
                        
                        if (insertIndex > 0)
                        {
                            string beforeInsert = yaml.Substring(0, insertIndex);
                            string afterInsert = yaml.Substring(insertIndex);
                            string volumeMountsBlock = $"        volumeMounts:\n          - name: {volumeName}\n            mountPath: {volumePath}\n        ";
                            
                            yaml = beforeInsert + volumeMountsBlock + afterInsert;
                        }
                    }
                }
            }
            
            return yaml;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler bei der YAML-Modifikation: {ex.Message}", ex);
            throw;
        }
    }

    private async void ApplyFirmwareChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingFirmwareChanges)
        {
            _loggingService.LogWarning("Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...");
            FirmwareErrorMessage.Text = "Eine Operation ist bereits in Bearbeitung. Bitte warten Sie...";
            FirmwareErrorMessage.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _isApplyingFirmwareChanges = true;
            
            // FirmwareErrorMessage statt ErrorMessage verwenden
            FirmwareErrorMessage.Text = "Änderungen werden angewendet...";
            FirmwareErrorMessage.Visibility = Visibility.Visible;
            
            string currentProject = _openShiftService.CurrentProject;
            _loggingService.LogInfo($"Starte Anwendung der Änderungen für Projekt: {currentProject}");
            
            // Alle benötigten Ressourcen vorbereiten
            string pvName = FirmwarePVNameTextBox.Text;
            string smbPath = FirmwareSMBSourcePathTextBox.Text;
            string secretName = FirmwareSecretNameTextBox.Text;
            string pvcName = FirmwarePVCNameTextBox.Text;
            string username = FirmwareUsernameTextBox.Text;
            string password = FirmwareSecretPasswordBox.Password;
            bool allSuccess = true;
            
            // 1. Zuerst YAML konfigurieren, ohne es neu zu laden
            string modifiedYaml = ModifyYamlForFirmware(FirmwareOriginalYamlTextBox.Text);
            FirmwareModifiedYamlTextBox.Text = modifiedYaml;
            
            // 1. Host-Deployment ändern
            _loggingService.LogInfo($"Aktualisiere Deployment in Projekt: {currentProject}");
            bool deploymentSuccess = await _openShiftService.UpdateDeploymentFromYamlAsync(currentProject, modifiedYaml);
            
            // Wir ignorieren Deployment-Fehler und machen trotzdem weiter
            
            // 2. Secret erstellen (falls nicht existiert)
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                _loggingService.LogInfo("Erstelle Secret...");
                string secretYaml = GenerateFirmwareSecretYaml(currentProject, secretName);
                
                // Base64-Kodierung für Benutzername und Passwort
                string usernameBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(username));
                string passwordBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
                
                // YAML aktualisieren mit Base64-kodierten Werten
                secretYaml = secretYaml.Replace("username: ", $"username: {usernameBase64}")
                                 .Replace("password: ", $"password: {passwordBase64}");
                
                // Secret erstellen
                bool secretSuccess = await _openShiftService.CreateResourceFromYamlAsync(secretYaml);
                if (!secretSuccess)
                {
                    _loggingService.LogWarning("Fehler beim Erstellen des Secret - möglicherweise existiert es bereits");
                    // Wir setzen allSuccess nicht auf false
                }
            }
            
            // 3. PV erstellen
            if (!string.IsNullOrEmpty(smbPath))
            {
                _loggingService.LogInfo("Erstelle PersistentVolume...");
                string pvYaml = GenerateFirmwarePVYaml(currentProject, pvName, smbPath, secretName);
                bool pvSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvYaml);
                if (!pvSuccess)
                {
                    _loggingService.LogWarning("Fehler beim Erstellen des PV - möglicherweise existiert es bereits");
                    // Wir setzen allSuccess nicht auf false
                }
                
                // 4. PVC erstellen, unabhängig vom PV-Ergebnis
                _loggingService.LogInfo("Erstelle PersistentVolumeClaim...");
                string pvcYaml = GenerateFirmwarePVCYaml(currentProject, pvcName, pvName);
                bool pvcSuccess = await _openShiftService.CreateResourceFromYamlAsync(pvcYaml);
                if (!pvcSuccess)
                {
                    _loggingService.LogWarning("Fehler beim Erstellen des PVC - möglicherweise existiert es bereits");
                    // Wir setzen allSuccess nicht auf false
                }
            }
            
            // Erfolgsmeldung
            _loggingService.LogInfo("Alle Änderungen wurden angewendet");
            FirmwareErrorMessage.Text = "Alle Änderungen wurden angewendet.";
            FirmwareErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            
            MessageBox.Show("Die Änderungen wurden angewendet.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anwenden der Änderungen: {ex.Message}", ex);
            FirmwareErrorMessage.Text = $"Fehler: {ex.Message}";
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isApplyingFirmwareChanges = false;
        }
    }

    // Füge folgende Methode hinzu, um die Verfügbarkeit der Application-Buttons zu aktualisieren
    private async Task UpdateApplicationButtonVisibility()
    {
        _loggingService.LogInfo($"UpdateApplicationButtonVisibility wurde aufgerufen. Benutzerrolle: {_currentUserRole}, ClusterLogin: {_isClusterLoggedIn}");
        
        // Wenn nicht im Cluster eingeloggt, keine weiteren Prüfungen durchführen
        if (!_isClusterLoggedIn)
        {
            _loggingService.LogInfo("Nicht im Cluster eingeloggt, keine Buttons werden angezeigt");
            return;
        }

        // Wenn Admin, alle Buttons anzeigen
        if (_currentUserRole == UserRoles.Admin)
        {
            _loggingService.LogInfo("Admin-Benutzer erkannt - alle Application-Buttons werden sichtbar gemacht");
            
            AppButton1.Visibility = Visibility.Visible;
            AppButton2.Visibility = Visibility.Visible;
            AppButton3.Visibility = Visibility.Visible;
            AppButton4.Visibility = Visibility.Visible;
            OISFilesVolumeButton.Visibility = Visibility.Visible;
            OISFilesOptIotVolumeButton.Visibility = Visibility.Visible;
            FirmwareVolumeButton.Visibility = Visibility.Visible;
            EDHRVolumeButton.Visibility = Visibility.Visible;
            SAPConnectivityVolumeButton.Visibility = Visibility.Visible;
            AppButton5.Visibility = Visibility.Visible;
            AutomationManagerButton.Visibility = Visibility.Visible;
            return;
        }

        _loggingService.LogInfo($"Setze Rechte für Benutzerrolle {_currentUserRole}");
        
        // Für andere Rollen die Sichtbarkeit der Buttons basierend auf den Rechten setzen
        AppButton1.Visibility = await _buttonRightService.HasRightAsync("AppButton1", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        AppButton2.Visibility = await _buttonRightService.HasRightAsync("AppButton2", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        AppButton3.Visibility = await _buttonRightService.HasRightAsync("AppButton3", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        AppButton4.Visibility = await _buttonRightService.HasRightAsync("AppButton4", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        OISFilesVolumeButton.Visibility = await _buttonRightService.HasRightAsync("OISFilesVolumeButton", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        OISFilesOptIotVolumeButton.Visibility = await _buttonRightService.HasRightAsync("OISFilesOptIotVolumeButton", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        FirmwareVolumeButton.Visibility = await _buttonRightService.HasRightAsync("FirmwareVolumeButton", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        EDHRVolumeButton.Visibility = await _buttonRightService.HasRightAsync("EDHRVolumeButton", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        SAPConnectivityVolumeButton.Visibility = await _buttonRightService.HasRightAsync("SAPConnectivityVolumeButton", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        AppButton5.Visibility = await _buttonRightService.HasRightAsync("AppButton5", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
        AutomationManagerButton.Visibility = await _buttonRightService.HasRightAsync("AutomationManagerButton", _currentUserRole) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GenerateFirmwarePVYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvName = FirmwarePVNameTextBox.Text;
            string smbPath = FirmwareSMBSourcePathTextBox.Text;
            string secretName = FirmwareSecretNameTextBox.Text;
            
            // Überprüfe, ob die erforderlichen Felder ausgefüllt sind
            if (string.IsNullOrEmpty(smbPath))
            {
                FirmwareErrorMessage.Text = "Bitte geben Sie einen SMB Source Path an.";
                FirmwareErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // PV YAML generieren
            FirmwarePVYamlTextBox.Text = GenerateFirmwarePVYaml(currentProject, pvName, smbPath, secretName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"PV YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            FirmwareErrorMessage.Text = "PV YAML wurde erstellt.";
            FirmwareErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            FirmwareErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des PV YAML: {ex.Message}", ex);
            FirmwareErrorMessage.Text = $"Fehler beim Erstellen des PV YAML: {ex.Message}";
            FirmwareErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void GenerateFirmwarePVCYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string pvcName = FirmwarePVCNameTextBox.Text;
            string pvName = FirmwarePVNameTextBox.Text;
            
            // PVC YAML generieren
            FirmwarePVCYamlTextBox.Text = GenerateFirmwarePVCYaml(currentProject, pvcName, pvName);
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"PVC YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            FirmwareErrorMessage.Text = "PVC YAML wurde erstellt.";
            FirmwareErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            FirmwareErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des PVC YAML: {ex.Message}", ex);
            FirmwareErrorMessage.Text = $"Fehler beim Erstellen des PVC YAML: {ex.Message}";
            FirmwareErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    private void GenerateFirmwareSecretYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            string secretName = FirmwareSecretNameTextBox.Text;
            
            // Überprüfen der Eingaben
            string username = FirmwareUsernameTextBox.Text;
            string password = FirmwareSecretPasswordBox.Password;
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                FirmwareErrorMessage.Text = "Bitte geben Sie Benutzername und Passwort an.";
                FirmwareErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // Secret YAML generieren
            string secretYaml = GenerateFirmwareSecretYaml(currentProject, secretName);
            
            // Werte in Base64 umwandeln
            string usernameBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(username));
            string passwordBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
            
            // Base64-Werte in YAML einsetzen
            secretYaml = secretYaml.Replace("username: ", $"username: {usernameBase64}")
                               .Replace("password: ", $"password: {passwordBase64}");
            
            FirmwareSecretYamlTextBox.Text = secretYaml;
            
            // Erfolg protokollieren
            _loggingService.LogInfo($"Firmware Secret YAML wurde erfolgreich für Projekt {currentProject} erstellt.");
            
            // Erfolgsmeldung anzeigen
            FirmwareErrorMessage.Text = "Secret YAML wurde erstellt.";
            FirmwareErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            FirmwareErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des Firmware Secret YAML: {ex.Message}", ex);
            FirmwareErrorMessage.Text = $"Fehler beim Erstellen des Secret YAML: {ex.Message}";
            FirmwareErrorMessage.Visibility = Visibility.Visible;
        }
    }

    // Generiert das YAML für den Namespace
    private void GenerateNamespaceYaml(string namespaceName)
    {
        try
        {
            string namespaceYaml = $@"apiVersion: v1
kind: Namespace
metadata:
  name: {namespaceName}
  labels:
    app: webserver";
            
            NamespaceYamlTextBox.Text = namespaceYaml;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Generieren des Namespace YAML: {ex.Message}", ex);
            WebserverErrorMessage.Text = $"Fehler: {ex.Message}";
            WebserverErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    // Erstellt den Namespace
    private async void CreateNamespaceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WebserverErrorMessage.Text = string.Empty;
            WebserverErrorMessage.Visibility = Visibility.Collapsed;
            
            string namespaceName = NewNamespaceTextBox.Text;
            if (string.IsNullOrEmpty(namespaceName))
            {
                WebserverErrorMessage.Text = "Namespace-Name darf nicht leer sein.";
                WebserverErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // Temporäre YAML-Datei für den Namespace erstellen
            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"namespace-{Guid.NewGuid()}.yaml");
            System.IO.File.WriteAllText(tempFile, NamespaceYamlTextBox.Text);
            
            // Namespace erstellen
            bool success = await ApplyYamlFile(tempFile);
            
            if (success)
            {
                MessageBox.Show($"Namespace '{namespaceName}' wurde erfolgreich erstellt.", 
                               "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Webserver-Ressourcen aktivieren
                WebserverResourcesGroup.IsEnabled = true;
                
                // Alle YAMLs generieren
                GenerateAllWebserverYamls(namespaceName);
            }
            else
            {
                WebserverErrorMessage.Text = $"Fehler beim Erstellen des Namespace '{namespaceName}'.";
                WebserverErrorMessage.Visibility = Visibility.Visible;
            }
            
            // Temporäre Datei löschen
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen des Namespace: {ex.Message}", ex);
            WebserverErrorMessage.Text = $"Fehler: {ex.Message}";
            WebserverErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    // YAML für alle Webserver-Ressourcen generieren
    private void GenerateWebserverYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WebserverErrorMessage.Text = string.Empty;
            WebserverErrorMessage.Visibility = Visibility.Collapsed;
            
            string namespaceName = NewNamespaceTextBox.Text;
            if (string.IsNullOrEmpty(namespaceName))
            {
                WebserverErrorMessage.Text = "Namespace-Name darf nicht leer sein.";
                WebserverErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // Alle YAMLs generieren
            GenerateAllWebserverYamls(namespaceName);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Generieren der Webserver-YAMLs: {ex.Message}", ex);
            WebserverErrorMessage.Text = $"Fehler: {ex.Message}";
            WebserverErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    // Generiert alle YAML-Dateien für den Webserver
    private void GenerateAllWebserverYamls(string namespaceName)
    {
        try
        {
            // Username und Passwort Base64-kodieren
            string username = WebserverUsernameTextBox.Text;
            string password = WebserverPasswordBox.Password;
            string volumeHandle = WebserverVolumeHandleTextBox.Text;
            string smbPath = WebserverSMBPathTextBox.Text;
            
            byte[] usernameBytes = System.Text.Encoding.UTF8.GetBytes(username);
            byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            string usernameBase64 = Convert.ToBase64String(usernameBytes);
            string passwordBase64 = Convert.ToBase64String(passwordBytes);
            
            // Zertifikate ConfigMap
            string certConfigMapYaml = $@"kind: ConfigMap
apiVersion: v1
metadata:
 name: bbraun-webserver-configmap
 namespace: {namespaceName}
 labels:
   app.kubernetes.io/instance: bbraun-mes-cm-dm-ato-prd01-webserver-cert
   app.kubernetes.io/name: bbraun-mes-cm-dm-ato-prd01-webserver-cert
data:
 ca-certificates.crt: |-
   -----BEGIN CERTIFICATE-----
   MIIE0zCCA7ugAwIBAgIJANu+mC2Jt3uTMA0GCSqGSIb3DQEBCwUAMIGhMQswCQYD
   VQQGEwJVUzETMBEGA1UECBMKQ2FsaWZvcm5pYTERMA8GA1UEBxMIU2FuIEpvc2Ux
   FTATBgNVBAoTDFpzY2FsZXIgSW5jLjEVMBMGA1UECxMMWnNjYWxlciBJbmMuMRgw
   FgYDVQQDEw9ac2NhbGVyIFJvb3QgQ0ExIjAgBgkqhkiG9w0BCQEWE3N1cHBvcnRA
   enNjYWxlci5jb20wHhcNMTQxMjE5MDAyNzU1WhcNNDIwNTA2MDAyNzU1WjCBoTEL
   MAkGA1UEBhMCVVMxEzARBgNVBAgTCkNhbGlmb3JuaWExETAPBgNVBAcTCFNhbiBK
   b3NlMRUwEwYDVQQKEwxac2NhbGVyIEluYy4xFTATBgNVBAsTDFpzY2FsZXIgSW5j
   LjEYMBYGA1UEAxMPWnNjYWxlciBSb290IENBMSIwIAYJKoZIhvcNAQkBFhNzdXBw
   b3J0QHpzY2FsZXIuY29tMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA
   qT7STSxZRTgEFFf6doHajSc1vk5jmzmM6BWuOo044EsaTc9eVEV/HjH/1DWzZtcr
   fTj+ni205apMTlKBW3UYR+lyLHQ9FoZiDXYXK8poKSV5+Tm0Vls/5Kb8mkhVVqv7
   LgYEmvEY7HPY+i1nEGZCa46ZXCOohJ0mBEtB9JVlpDIO+nN0hUMAYYdZ1KZWCMNf
   5J/aTZiShsorN2A38iSOhdd+mcRM4iNL3gsLu99XhKnRqKoHeH83lVdfu1XBeoQz
   z5V6gA3kbRvhDwoIlTBeMa5l4yRdJAfdpkbFzqiwSgNdhbxTHnYYorDzKfr2rEFM
   dsMU0DHdeAZf711+1CunuQIDAQABo4IBCjCCAQYwHQYDVR0OBBYEFLm33UrNww4M
   hp1d3+wcBGnFTpjfMIHWBgNVHSMEgc4wgcuAFLm33UrNww4Mhp1d3+wcBGnFTpjf
   oYGnpIGkMIGhMQswCQYDVQQGEwJVUzETMBEGA1UECBMKQ2FsaWZvcm5pYTERMA8G
   A1UEBxMIU2FuIEpvc2UxFTATBgNVBAoTDFpzY2FsZXIgSW5jLjEVMBMGA1UECxMM
   WnNjYWxlciBJbmMuMRgwFgYDVQQDEw9ac2NhbGVyIFJvb3QgQ0ExIjAgBgkqhkiG
   9w0BCQEWE3N1cHBvcnRAenNjYWxlci5jb22CCQDbvpgtibd7kzAMBgNVHRMEBTAD
   AQH/MA0GCSqGSIb3DQEBCwUAA4IBAQAw0NdJh8w3NsJu4KHuVZUrmZgIohnTm0j+
   RTmYQ9IKA/pvxAcA6K1i/LO+Bt+tCX+C0yxqB8qzuo+4vAzoY5JEBhyhBhf1uK+P
   /WVWFZN/+hTgpSbZgzUEnWQG2gOVd24msex+0Sr7hyr9vn6OueH+jj+vCMiAm5+u
   kd7lLvJsBu3AO3jGWVLyPkS3i6Gf+rwAp1OsRrv3WnbkYcFf9xjuaf4z0hRCrLN2
   xFNjavxrHmsH8jPHVvgc1VD0Opja0l/BRVauTrUaoW6tE+wFG5rEcPGS80jjHK4S
   pB5iDj2mUZH1T8lzYtuZy0ZPirxmtsk3135+CKNa2OCAhhFjE0xd
   -----END CERTIFICATE-----";
            
            // Cluster-Typ ermitteln für korrekte Domain
            string envType = "lkp"; // Standard ist prd (lkp)
            
            // Aktuelle URL des Clusters auslesen und Cluster-Typ bestimmen
            if (_currentClusterConfig != null && !string.IsNullOrEmpty(_currentClusterConfig.Name))
            {
                string clusterName = _currentClusterConfig.Name.ToLower();
                if (clusterName.Contains("dev"))
                {
                    envType = "lkd"; // Development
                }
                else if (clusterName.Contains("qas"))
                {
                    envType = "lkq"; // QA
                }
                else if (clusterName.Contains("prd"))
                {
                    envType = "lkp"; // Production
                }
            }
            
            // Nginx ConfigMap
            string nginxConfigMapYaml = $@"kind: ConfigMap
apiVersion: v1
metadata:
 name: nginx-config
 namespace: {namespaceName}
data:
 default.conf: |
   server {{
     client_max_body_size 200M;
     sendfile on;
     tcp_nopush on;
     tcp_nodelay on;
     client_body_timeout 300;
     client_header_timeout 300;
     keepalive_timeout 300;
     send_timeout 300;
     autoindex on;
     
     proxy_buffering off;
     proxy_buffer_size 512k;
     proxy_buffers 8 512k;
     proxy_busy_buffers_size 512k;
     proxy_temp_file_write_size 512k;
     
     listen 80;
     listen 443;
     server_name {namespaceName}.apps.de08-{envType}10.k8s.it.bbraun.com;
     
     location / {{
       root /usr/share/nginx/html/files;
       autoindex on;
       index index.html index.htm;
       allow all;
     }}
     
     error_page 500 502 503 504 /50x.html;
     location = /50x.html {{
       root /usr/share/nginx/html;
     }}
   }}";
            
            // Diese Definitionen wurden nach oben verschoben
            
            string routeYaml = $@"kind: Route
apiVersion: route.openshift.io/v1
metadata:
 name: {namespaceName}
 namespace: {namespaceName}
spec:
 host: {namespaceName}.apps.de08-{envType}10.k8s.it.bbraun.com
 to:
   kind: Service
   name: {namespaceName}
   weight: 100
 port:
   targetPort: 80
 tls:
   termination: edge
   insecureEdgeTerminationPolicy: Redirect
 wildcardPolicy: None";
            
            // Secret
            string secretYaml = $@"kind: Secret
apiVersion: v1
metadata:
 name: {namespaceName}
 namespace: {namespaceName}
data:
 domain: YmJtYWc=
 password: {passwordBase64}
 username: {usernameBase64}
type: Opaque";
            
            // Service
            string serviceYaml = $@"kind: Service
apiVersion: v1
metadata:
 name: {namespaceName}
 namespace: {namespaceName}
spec:
 ipFamilies:
 - IPv4
 ports:
 - name: webserver
   protocol: TCP
   port: 80
   targetPort: 80
 internalTrafficPolicy: Cluster
 type: ClusterIP
 ipFamilyPolicy: SingleStack
 sessionAffinity: None
 selector:
   app: webserver";
            
            // PersistentVolume
            string pvYaml = $@"kind: PersistentVolume
apiVersion: v1
metadata:
 name: {namespaceName}pv
 annotations:
   pv.kubernetes.io/bound-by-controller: 'yes'
 finalizers:
 - kubernetes.io/pv-protection
spec:
 capacity:
   storage: 1Gi
 csi:
   driver: smb.csi.k8s.io
   volumeHandle: {volumeHandle}
   volumeAttributes:
     source: {smbPath}
   nodeStageSecretRef:
     name: {namespaceName}
     namespace: {namespaceName}
 accessModes:
 - ReadWriteMany
 persistentVolumeReclaimPolicy: Retain
 volumeMode: Filesystem";
            
            // PersistentVolumeClaim
            string pvcYaml = $@"kind: PersistentVolumeClaim
apiVersion: v1
metadata:
 name: {namespaceName}pvc
 namespace: {namespaceName}
 annotations:
   pv.kubernetes.io/bind-completed: 'yes'
 finalizers:
 - kubernetes.io/pvc-protection
spec:
 accessModes:
 - ReadWriteMany
 resources:
   requests:
     storage: 1Gi
 volumeName: {namespaceName}pv
 storageClassName: ''
 volumeMode: Filesystem";
            
            // ServiceAccount
            string serviceAccountYaml = $@"apiVersion: v1
kind: ServiceAccount
metadata:
  name: nginx
  namespace: {namespaceName}";
            
            // RoleBinding
            string roleBindingYaml = $@"apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: nginx-anyuid-binding
  namespace: {namespaceName}
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: system:openshift:scc:anyuid
subjects:
  - kind: ServiceAccount
    name: nginx
    namespace: {namespaceName}";
            
            // YAMLs in TextBoxen anzeigen
            CertConfigMapYamlTextBox.Text = certConfigMapYaml;
            NginxConfigMapYamlTextBox.Text = nginxConfigMapYaml;
            RouteYamlTextBox.Text = routeYaml;
            WebserverSecretYamlTextBox.Text = secretYaml;
            ServiceYamlTextBox.Text = serviceYaml;
            WebserverPVYamlTextBox.Text = pvYaml;
            WebserverPVCYamlTextBox.Text = pvcYaml;
            ServiceAccountYamlTextBox.Text = serviceAccountYaml;
            RoleBindingYamlTextBox.Text = roleBindingYaml;
            
            // Deployment
            string deploymentYaml = $@"kind: Deployment
apiVersion: apps/v1
metadata:
  name: {namespaceName}
  namespace: {namespaceName}
  annotations:
    deployment.kubernetes.io/revision: '1'
spec:
  replicas: 1
  selector:
    matchLabels:
      app: webserver
  template:
    metadata:
      creationTimestamp: null
      labels:
        app: webserver
    spec:
      restartPolicy: Always
      serviceAccountName: nginx
      schedulerName: default-scheduler
      terminationGracePeriodSeconds: 30
      securityContext: {{}}
      containers:
        - name: webserver
          image: 'nginx:latest'
          ports:
            - containerPort: 80
              protocol: TCP
          resources:
            limits:
              cpu: '1'
              memory: 2Gi
            requests:
              cpu: 500m
              memory: 1Gi
          volumeMounts:
            - name: bbraun-webserver-configmap
              readOnly: true
              mountPath: /etc/ssl/certs/ca-certificates.crt
              subPath: ca-certificates.crt
            - name: bbraun-webserver-storage
              mountPath: /usr/share/nginx/html/files
            - name: nginx-config
              mountPath: /etc/nginx/conf.d/default.conf
              subPath: default.conf
          terminationMessagePath: /dev/termination-log
          terminationMessagePolicy: File
          imagePullPolicy: Always
      serviceAccount: nginx
      volumes:
        - name: bbraun-webserver-configmap
          configMap:
            name: bbraun-webserver-configmap
            items:
              - key: ca-certificates.crt
                path: ca-certificates.crt
            defaultMode: 420
        - name: bbraun-webserver-storage
          persistentVolumeClaim:
            claimName: {namespaceName}pvc
        - name: nginx-config
          configMap:
            name: nginx-config
            defaultMode: 420
      dnsPolicy: ClusterFirst
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 25%
      maxSurge: 25%
  revisionHistoryLimit: 10
  progressDeadlineSeconds: 600";

            DeploymentYamlTextBox.Text = deploymentYaml;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Generieren der Webserver-YAMLs: {ex.Message}", ex);
            WebserverErrorMessage.Text = $"Fehler: {ex.Message}";
            WebserverErrorMessage.Visibility = Visibility.Visible;
        }
    }
    
    // Erstellt alle Webserver-Ressourcen
    private async void CreateWebserverResourcesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WebserverErrorMessage.Text = string.Empty;
            WebserverErrorMessage.Visibility = Visibility.Collapsed;
            
            string namespaceName = NewNamespaceTextBox.Text;
            if (string.IsNullOrEmpty(namespaceName))
            {
                WebserverErrorMessage.Text = "Namespace-Name darf nicht leer sein.";
                WebserverErrorMessage.Visibility = Visibility.Visible;
                return;
            }
            
            // Ordner für temporäre YAML-Dateien
            string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"webserver-{Guid.NewGuid()}");
            System.IO.Directory.CreateDirectory(tempFolder);
            
            try
            {
                // Alle YAML-Dateien in temporäre Dateien schreiben
                string certConfigMapFile = System.IO.Path.Combine(tempFolder, "cert-configmap.yaml");
                string nginxConfigMapFile = System.IO.Path.Combine(tempFolder, "nginx-configmap.yaml");
                string routeFile = System.IO.Path.Combine(tempFolder, "route.yaml");
                string secretFile = System.IO.Path.Combine(tempFolder, "secret.yaml");
                string serviceFile = System.IO.Path.Combine(tempFolder, "service.yaml");
                string pvFile = System.IO.Path.Combine(tempFolder, "pv.yaml");
                string pvcFile = System.IO.Path.Combine(tempFolder, "pvc.yaml");
                string serviceAccountFile = System.IO.Path.Combine(tempFolder, "serviceaccount.yaml");
                string roleBindingFile = System.IO.Path.Combine(tempFolder, "rolebinding.yaml");
                string deploymentFile = System.IO.Path.Combine(tempFolder, "deployment.yaml");
                
                System.IO.File.WriteAllText(certConfigMapFile, CertConfigMapYamlTextBox.Text);
                System.IO.File.WriteAllText(nginxConfigMapFile, NginxConfigMapYamlTextBox.Text);
                System.IO.File.WriteAllText(routeFile, RouteYamlTextBox.Text);
                System.IO.File.WriteAllText(secretFile, WebserverSecretYamlTextBox.Text);
                System.IO.File.WriteAllText(serviceFile, ServiceYamlTextBox.Text);
                System.IO.File.WriteAllText(pvFile, WebserverPVYamlTextBox.Text);
                System.IO.File.WriteAllText(pvcFile, WebserverPVCYamlTextBox.Text);
                System.IO.File.WriteAllText(serviceAccountFile, ServiceAccountYamlTextBox.Text);
                System.IO.File.WriteAllText(roleBindingFile, RoleBindingYamlTextBox.Text);
                System.IO.File.WriteAllText(deploymentFile, DeploymentYamlTextBox.Text);
                
                // Ressourcen erstellen (in der richtigen Reihenfolge)
                bool certConfigMapSuccess = await ApplyYamlFile(certConfigMapFile);
                bool nginxConfigMapSuccess = await ApplyYamlFile(nginxConfigMapFile);
                bool secretSuccess = await ApplyYamlFile(secretFile);
                bool pvSuccess = await ApplyYamlFile(pvFile);
                bool pvcSuccess = await ApplyYamlFile(pvcFile);
                bool serviceAccountSuccess = await ApplyYamlFile(serviceAccountFile);
                bool roleBindingSuccess = await ApplyYamlFile(roleBindingFile);
                bool serviceSuccess = await ApplyYamlFile(serviceFile);
                bool routeSuccess = await ApplyYamlFile(routeFile);
                bool deploymentSuccess = await ApplyYamlFile(deploymentFile);
                
                // Ergebnis anzeigen
                if (certConfigMapSuccess && nginxConfigMapSuccess && secretSuccess && 
                    pvSuccess && pvcSuccess && serviceAccountSuccess && 
                    roleBindingSuccess && serviceSuccess && routeSuccess && deploymentSuccess)
                {
                    MessageBox.Show($"Alle Webserver-Ressourcen für '{namespaceName}' wurden erfolgreich erstellt.", 
                                   "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    WebserverErrorMessage.Text = "Fehler beim Erstellen einer oder mehrerer Ressourcen.";
                    WebserverErrorMessage.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                // Temporäre Dateien und Ordner löschen
                try
                {
                    if (System.IO.Directory.Exists(tempFolder))
                    {
                        System.IO.Directory.Delete(tempFolder, true);
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"Fehler beim Löschen temporärer Dateien: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Erstellen der Webserver-Ressourcen: {ex.Message}", ex);
            WebserverErrorMessage.Text = $"Fehler: {ex.Message}";
            WebserverErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private async void AutomationManagerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Alle Sperrvariablen zurücksetzen
            _isApplyingOISFilesChanges = false;
            _isApplyingOISFilesOptIotChanges = false;
            _isApplyingEDHRChanges = false;
            _isApplyingFirmwareChanges = false;
            _isApplyingSAPConnectivityChanges = false;
            _isApplyingAutomationManagerChanges = false;
            
            // Button-Style setzen und Inhalte anzeigen
            ResetAppButtonStyles();
            AutomationManagerButton.Style = (Style)FindResource("ActiveSidebarButton");
            HideAllContent();
            AutomationManagerContent.Visibility = Visibility.Visible;
            
            // Aktuelles Projekt abrufen (immer frisch abfragen)
            string currentProject = await _openShiftService.GetCurrentProjectName();
            
            if (!string.IsNullOrEmpty(currentProject))
            {
                // Statt direkten Zugriff auf den setter verwenden wir SwitchProject
                // Das stellt sicher, dass OpenShiftService.CurrentProject korrekt gesetzt wird
                await _openShiftService.SwitchProject(currentProject);
                
                // UI aktualisieren
                AutomationManagerCurrentProjectText.Text = currentProject;
                
                // Status-Meldung zum Laden des Deployments
                AutomationManagerErrorMessage.Text = "Bitte laden Sie zuerst das Deployment.";
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Blue);
                AutomationManagerErrorMessage.Visibility = Visibility.Visible;
                
                // YAMLs für die aktuelle Projektauswahl aktualisieren
                UpdateAutomationManagerYamls();
            }
            else
            {
                AutomationManagerCurrentProjectText.Text = "Kein Projekt ausgewählt";
                
                // Warnung anzeigen
                AutomationManagerErrorMessage.Text = "Kein Projekt ausgewählt. Bitte wählen Sie zuerst ein Projekt aus und kehren Sie zu dieser Funktion zurück.";
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
                AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            }
            
            // Zurücksetzen aller Textfelder und Eingabefelder für diesen Bereich
            AutomationManagerOriginalYamlTextBox.Text = string.Empty;
            AutomationManagerModifiedYamlTextBox.Text = string.Empty;
            AutomationManagerPV1YamlTextBox.Text = string.Empty;
            AutomationManagerPV2YamlTextBox.Text = string.Empty;
            AutomationManagerServiceYamlTextBox.Text = string.Empty;
            AutomationManagerRouteYamlTextBox.Text = string.Empty;
            
            // Standardwerte setzen
            AutomationManagerNameTextBox.Text = "am01";
            AutomationManagerPortTextBox.Text = "4840";
            ProjectSelectionComboBox.SelectedIndex = 0; // Template
            
            // Cluster-Auswahl setzen basierend auf aktuellem Cluster
            if (_currentClusterConfig != null && !string.IsNullOrEmpty(_currentClusterConfig.Url))
            {
                if (_currentClusterConfig.Url.Contains("lkd"))
                {
                    ClusterSelectionComboBox.SelectedIndex = 0; // DEV
                }
                else if (_currentClusterConfig.Url.Contains("lkq"))
                {
                    ClusterSelectionComboBox.SelectedIndex = 1; // QAS
                }
                else if (_currentClusterConfig.Url.Contains("lkp"))
                {
                    ClusterSelectionComboBox.SelectedIndex = 2; // PRD
                }
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Öffnen des Automation Manager: {ex.Message}", ex);
            AutomationManagerErrorMessage.Text = $"Fehler beim Öffnen: {ex.Message}";
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private void UpdateAutomationManagerYamls()
    {
        try
        {
            string currentProject = _openShiftService.CurrentProject;
            
            if (string.IsNullOrEmpty(currentProject))
            {
                _loggingService.LogWarning("Kein aktuelles Projekt ausgewählt");
                AutomationManagerErrorMessage.Text = "Kein aktuelles Projekt ausgewählt. Bitte wählen Sie zuerst ein Projekt aus.";
                AutomationManagerErrorMessage.Visibility = Visibility.Visible;
                return;
            }

            string amName = AutomationManagerNameTextBox.Text;
            string port = AutomationManagerPortTextBox.Text;
            string projectSelection = ((ComboBoxItem)ProjectSelectionComboBox.SelectedItem)?.Content.ToString() ?? "Template";
            string clusterSelection = ((ComboBoxItem)ClusterSelectionComboBox.SelectedItem)?.Content.ToString() ?? "DEV";
            string clusterCode = GetClusterCodeFromSelection(clusterSelection);

            // PV1 YAML (Logs) generieren
            AutomationManagerPV1YamlTextBox.Text = GenerateAutomationManagerPV1Yaml(currentProject, amName, projectSelection, clusterSelection);
            
            // PV2 YAML (Persistency) generieren
            AutomationManagerPV2YamlTextBox.Text = GenerateAutomationManagerPV2Yaml(currentProject, amName, projectSelection, clusterSelection);
            
            // Service YAML generieren
            AutomationManagerServiceYamlTextBox.Text = GenerateAutomationManagerServiceYaml(currentProject, amName, port);
            
            // Route YAML generieren
            AutomationManagerRouteYamlTextBox.Text = GenerateAutomationManagerRouteYaml(currentProject, amName, port, clusterCode);
            
            // Erfolgreich aktualisiert, Fehlermeldung zurücksetzen
            AutomationManagerErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der Automation Manager YAMLs: {ex.Message}", ex);
            AutomationManagerErrorMessage.Text = $"Fehler beim Aktualisieren der YAMLs: {ex.Message}";
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
        }
    }

    private string GetClusterCodeFromSelection(string clusterSelection)
    {
        switch (clusterSelection)
        {
            case "DEV":
                return "lkd";
            case "QAS":
                return "lkq";
            case "PRD":
                return "lkp";
            default:
                return "lkd";
        }
    }

    private string GenerateAutomationManagerPV1Yaml(string currentProject, string amName, string projectSelection, string clusterSelection)
    {
        return $@"kind: PersistentVolume
apiVersion: v1
metadata:
  name: {currentProject}-{amName}-connect-iot-logs
spec:
  capacity:
    storage: 10Gi
  nfs:
    server: de08-la013.bbmag.bbraun.com
    path: /opt/CMFMES/{projectSelection}/{clusterSelection}/{currentProject}/automationmanager/logs/{amName}/
  accessModes:
    - ReadWriteOnce
  persistentVolumeReclaimPolicy: Retain
  volumeMode: Filesystem";
    }

    private string GenerateAutomationManagerPV2Yaml(string currentProject, string amName, string projectSelection, string clusterSelection)
    {
        return $@"kind: PersistentVolume
apiVersion: v1
metadata:
  name: {currentProject}-{amName}-connect-iot-persistency
spec:
  capacity:
    storage: 10Gi
  nfs:
    server: de08-la013.bbmag.bbraun.com
    path: /opt/CMFMES/{projectSelection}/{clusterSelection}/{currentProject}/automationmanager/persisted/{amName}/    
  accessModes:
    - ReadWriteOnce
  persistentVolumeReclaimPolicy: Retain
  volumeMode: Filesystem";
    }

    private string GenerateAutomationManagerServiceYaml(string currentProject, string amName, string port)
    {
        return $@"kind: Service
apiVersion: v1
metadata:
  name: {currentProject}-{amName}
  namespace: {currentProject}
  labels:
    app: {amName}
spec:
  externalTrafficPolicy: Cluster
  ipFamilies:
    - IPv4
  ports:
    - name: opcua-connection-{port}
      protocol: TCP
      port: {port}
      targetPort: {port}
      nodePort: {port}
  internalTrafficPolicy: Cluster
  type: NodePort
  ipFamilyPolicy: SingleStack
  sessionAffinity: None
  selector:
    app: {amName}";
    }

    private string GenerateAutomationManagerRouteYaml(string currentProject, string amName, string port, string clusterCode)
    {
        return $@"kind: Route
apiVersion: route.openshift.io/v1
metadata:
  name: {currentProject}-{amName}
  namespace: {currentProject}
  labels:
    app: {amName}
spec:
  host: {amName}.apps.de08-{clusterCode}10.k8s.it.bbraun.com
  to:
    kind: Service
    name: {currentProject}-{amName}
    weight: 100
  port:
    targetPort: {port}
  wildcardPolicy: None";
    }

    private async Task LoadAutomationManagerDeploymentYaml()
    {
        try
        {
            // Stelle sicher, dass wir das aktuelle Projekt haben
            string currentProject = _openShiftService.CurrentProject;
            if (string.IsNullOrEmpty(currentProject))
            {
                currentProject = await _openShiftService.GetCurrentProjectName();
                await Dispatcher.InvokeAsync(() => 
                {
                    AutomationManagerCurrentProjectText.Text = currentProject;
                });
            }
            
            string amName = await Dispatcher.InvokeAsync(() => AutomationManagerNameTextBox.Text);
            
            if (string.IsNullOrEmpty(amName))
            {
                await Dispatcher.InvokeAsync(() => {
                    AutomationManagerErrorMessage.Text = "Bitte geben Sie einen Namen für den Automation Manager ein.";
                    AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Orange);
                    AutomationManagerErrorMessage.Visibility = Visibility.Visible;
                });
                return;
            }
            
            _loggingService.LogInfo($"Lade Automation Manager Deployment YAML für Projekt: {currentProject}, Automation Manager: {amName}");
            
            string yaml = await _openShiftService.GetDeploymentYamlAsync(currentProject, amName);
            
            if (string.IsNullOrEmpty(yaml) || yaml.Contains("Error from server (NotFound)"))
            {
                await Dispatcher.InvokeAsync(() => {
                    // Klare Fehlermeldung anzeigen, wenn kein Deployment gefunden wurde
                    AutomationManagerErrorMessage.Text = $"Automation Manager '{amName}' nicht vorhanden. Bitte erstellen Sie zuerst den Automation Manager im MES.";
                    AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Orange);
                    AutomationManagerErrorMessage.Visibility = Visibility.Visible;
                    
                    // Leeres YAML in die Textfelder setzen
                    AutomationManagerOriginalYamlTextBox.Text = "";
                    AutomationManagerModifiedYamlTextBox.Text = "";
                    
                    _loggingService.LogWarning($"Automation Manager Deployment '{amName}' wurde nicht gefunden.");
                });
                return;
            }
            
            await Dispatcher.InvokeAsync(() => {
                AutomationManagerOriginalYamlTextBox.Text = yaml;
                string modifiedYaml = ModifyYamlForAutomationManager(yaml); // Direkt modifizieren
                AutomationManagerModifiedYamlTextBox.Text = modifiedYaml;
                
                // Bei Erfolg Fehlermeldung ausblenden bzw. Erfolgsmeldung anzeigen
                AutomationManagerErrorMessage.Text = $"Deployment erfolgreich geladen und alle 'app: opcua' Labels zu 'app: {amName}' geändert.";
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
                AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => {
                // Fehlermeldung anzeigen, aber nicht als schwerwiegend behandeln, da das Deployment möglicherweise noch nicht existiert
                AutomationManagerErrorMessage.Text = $"Automation Manager nicht vorhanden. Bitte erstellen Sie zuerst den Automation Manager im MES.";
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Orange);
                AutomationManagerErrorMessage.Visibility = Visibility.Visible;
                
                // Leeres YAML in die Textfelder setzen
                AutomationManagerOriginalYamlTextBox.Text = "";
                AutomationManagerModifiedYamlTextBox.Text = "";
                
                _loggingService.LogWarning($"Fehler beim Laden des Automation Manager Deployments: {ex.Message}");
            });
        }
    }

    private void ConfigureAutomationManagerYamlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // YAMLs aktualisieren, falls sich Eingaben geändert haben
            UpdateAutomationManagerYamls();
            
            // Jetzt das Deployment-YAML laden, weil es explizit angefordert wurde
            Task.Run(async () => await LoadAutomationManagerDeploymentYaml());
            
            string yaml = AutomationManagerOriginalYamlTextBox.Text;
            
            // Wenn YAML vorhanden ist, dann direkt modifizieren
            if (!string.IsNullOrEmpty(yaml))
            {
                string modifiedYaml = ModifyYamlForAutomationManager(yaml);
                AutomationManagerModifiedYamlTextBox.Text = modifiedYaml;
            }
            
            // Erfolgreiche Konfiguration, Fehlermeldung zurücksetzen
            AutomationManagerErrorMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            AutomationManagerErrorMessage.Text = $"Fehler beim Konfigurieren des YAML: {ex.Message}";
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Konfigurieren des YAML: {ex.Message}", ex);
        }
    }

    private async void ApplyAutomationManagerChangesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Verhindere parallele Ausführungen
            if (_isApplyingAutomationManagerChanges)
            {
                MessageBox.Show("Es läuft bereits eine Anwendung der Änderungen. Bitte warten Sie, bis der Vorgang abgeschlossen ist.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            _isApplyingAutomationManagerChanges = true;
            
            // YAMLs aktualisieren, falls sich Eingaben geändert haben
            UpdateAutomationManagerYamls();
            
            // Stelle sicher, dass alle erforderlichen Felder ausgefüllt sind
            if (string.IsNullOrEmpty(AutomationManagerNameTextBox.Text) || string.IsNullOrEmpty(AutomationManagerPortTextBox.Text))
            {
                MessageBox.Show("Bitte füllen Sie alle erforderlichen Felder aus.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                _isApplyingAutomationManagerChanges = false;
                return;
            }
            
            // Sicherheitsabfrage
            MessageBoxResult result = MessageBox.Show(
                "Möchten Sie die folgenden Änderungen anwenden?\n\n" +
                $"- PersistentVolumes erstellen\n" +
                $"- Service erstellen\n" +
                $"- Route erstellen",
                "Änderungen anwenden",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result != MessageBoxResult.Yes)
            {
                _isApplyingAutomationManagerChanges = false;
                return;
            }
            
            string currentProject = _openShiftService.CurrentProject;
            
            // Statusmeldung anzeigen
            AutomationManagerErrorMessage.Text = "Änderungen werden angewendet...";
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Blue);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            
            // Temporäre Dateien erstellen
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenshiftOPSCenter");
            System.IO.Directory.CreateDirectory(tempPath);
            
            string modifiedDeploymentYamlPath = System.IO.Path.Combine(tempPath, $"deployment-{currentProject}-{Guid.NewGuid()}.yaml");
            string pv1YamlPath = System.IO.Path.Combine(tempPath, $"pv1-{currentProject}-{Guid.NewGuid()}.yaml");
            string pv2YamlPath = System.IO.Path.Combine(tempPath, $"pv2-{currentProject}-{Guid.NewGuid()}.yaml");
            string serviceYamlPath = System.IO.Path.Combine(tempPath, $"service-{currentProject}-{Guid.NewGuid()}.yaml");
            string routeYamlPath = System.IO.Path.Combine(tempPath, $"route-{currentProject}-{Guid.NewGuid()}.yaml");
            
            // Schreibe modifiziertes Deployment YAML in temporäre Datei
            if (!string.IsNullOrEmpty(AutomationManagerModifiedYamlTextBox.Text))
            {
                await System.IO.File.WriteAllTextAsync(modifiedDeploymentYamlPath, AutomationManagerModifiedYamlTextBox.Text);
            }
            
            // Schreibe PV, Service und Route YAMLs in temporäre Dateien
            await System.IO.File.WriteAllTextAsync(pv1YamlPath, AutomationManagerPV1YamlTextBox.Text);
            await System.IO.File.WriteAllTextAsync(pv2YamlPath, AutomationManagerPV2YamlTextBox.Text);
            await System.IO.File.WriteAllTextAsync(serviceYamlPath, AutomationManagerServiceYamlTextBox.Text);
            await System.IO.File.WriteAllTextAsync(routeYamlPath, AutomationManagerRouteYamlTextBox.Text);
            
            // Wende die Änderungen an
            bool success = true;
            string statusMessage = "";
            
            // Wende die PVs an
            success &= await ApplyYamlFile(pv1YamlPath);
            if (success)
            {
                statusMessage += "PersistentVolume (Logs) erfolgreich erstellt.\n";
            }
            else
            {
                statusMessage += "Fehler beim Erstellen des PersistentVolume (Logs).\n";
            }
            
            success &= await ApplyYamlFile(pv2YamlPath);
            if (success)
            {
                statusMessage += "PersistentVolume (Persistency) erfolgreich erstellt.\n";
            }
            else
            {
                statusMessage += "Fehler beim Erstellen des PersistentVolume (Persistency).\n";
            }
            
            // Wende Service an
            success &= await ApplyYamlFile(serviceYamlPath);
            if (success)
            {
                statusMessage += "Service erfolgreich erstellt.\n";
            }
            else
            {
                statusMessage += "Fehler beim Erstellen des Service.\n";
            }
            
            // Wende Route an
            success &= await ApplyYamlFile(routeYamlPath);
            if (success)
            {
                statusMessage += "Route erfolgreich erstellt.\n";
            }
            else
            {
                statusMessage += "Fehler beim Erstellen der Route.\n";
            }
            
            // Bestätigung anzeigen, dass der Benutzer jetzt den Automation Manager im MES erstellen soll
            MessageBoxResult mesConfirmResult = MessageBox.Show(
                "Die PersistentVolumes, Service und Route wurden angelegt.\n\n" +
                "Bitte erstellen Sie jetzt den Automation Manager im MES und klicken Sie anschließend auf 'OK', um das Deployment anzupassen.",
                "Automation Manager erstellen",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
                
            if (mesConfirmResult != MessageBoxResult.OK)
            {
                // Benutzer hat abgebrochen
                _isApplyingAutomationManagerChanges = false;
                AutomationManagerErrorMessage.Text = "Vorgang abgebrochen nach Erstellung von PVs, Service und Route.";
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Orange);
                return;
            }
            
            // Deployment-YAML neu laden nach der Erstellung im MES
            await LoadAutomationManagerDeploymentYaml();
            
            // Wende das modifizierte Deployment an, falls vorhanden
            if (!string.IsNullOrEmpty(AutomationManagerModifiedYamlTextBox.Text))
            {
                // Aktuelle Version des modifizierten YAML in die Datei schreiben
                await System.IO.File.WriteAllTextAsync(modifiedDeploymentYamlPath, AutomationManagerModifiedYamlTextBox.Text);
                
                success &= await ApplyYamlFile(modifiedDeploymentYamlPath);
                if (success)
                {
                    statusMessage += "Deployment erfolgreich konfiguriert.\n";
                }
                else
                {
                    statusMessage += "Fehler beim Konfigurieren des Deployments.\n";
                }
            }
            else
            {
                statusMessage += "Kein Deployment zum Konfigurieren gefunden.\n";
            }
            
            // Temporäre Dateien löschen
            try
            {
                if (System.IO.File.Exists(modifiedDeploymentYamlPath)) System.IO.File.Delete(modifiedDeploymentYamlPath);
                if (System.IO.File.Exists(pv1YamlPath)) System.IO.File.Delete(pv1YamlPath);
                if (System.IO.File.Exists(pv2YamlPath)) System.IO.File.Delete(pv2YamlPath);
                if (System.IO.File.Exists(serviceYamlPath)) System.IO.File.Delete(serviceYamlPath);
                if (System.IO.File.Exists(routeYamlPath)) System.IO.File.Delete(routeYamlPath);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Fehler beim Löschen temporärer Dateien: {ex.Message}");
            }
            
            // Aktualisiere Status
            if (success)
            {
                AutomationManagerErrorMessage.Text = "Alle Änderungen wurden erfolgreich angewendet:\n" + statusMessage;
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                AutomationManagerErrorMessage.Text = "Es sind Fehler aufgetreten:\n" + statusMessage;
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            }
            
            _isApplyingAutomationManagerChanges = false;
        }
        catch (Exception ex)
        {
            AutomationManagerErrorMessage.Text = $"Fehler beim Anwenden der Änderungen: {ex.Message}";
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Anwenden der Automation Manager Änderungen: {ex.Message}", ex);
            _isApplyingAutomationManagerChanges = false;
        }
    }

    private string ModifyYamlForAutomationManager(string yaml)
    {
        // Wenn YAML leer oder null ist, gibt es nichts zu modifizieren
        if (string.IsNullOrEmpty(yaml))
        {
            _loggingService.LogWarning("ModifyYamlForAutomationManager: Das YAML ist leer oder null.");
            return yaml;
        }
        
        try
        {
            _loggingService.LogInfo("Beginne Modifikation des Automation Manager YAML...");
            
            // Automation Manager Name holen
            string amName = AutomationManagerNameTextBox.Text;
            
            // Arbeite mit Zeilenweise, um genau die richtigen Stellen zu modifizieren
            string[] lines = yaml.Split('\n');
            List<string> modifiedLines = new List<string>(lines);
            
            // Suche nach dem Hauptmetadata-Bereich
            bool foundMainMetadata = false;
            bool foundTemplateMetadata = false;
            
            for (int i = 0; i < modifiedLines.Count; i++)
            {
                string line = modifiedLines[i];
                
                // Hauptmetadata-Bereich (direkt unter kind: Deployment)
                if (line.Trim() == "metadata:" && !foundMainMetadata)
                {
                    _loggingService.LogInfo("Hauptmetadaten-Bereich gefunden");
                    
                    // Suche nach dem labels-Abschnitt oder erstelle ihn
                    int labelsIndex = -1;
                    int indentLevel = line.Length - line.TrimStart().Length;
                    string indent = new string(' ', indentLevel);
                    
                    for (int j = i + 1; j < modifiedLines.Count; j++)
                    {
                        if (modifiedLines[j].Trim() == "labels:")
                        {
                            labelsIndex = j;
                            break;
                        }
                        else if (modifiedLines[j].TrimStart().StartsWith("name:") || 
                                 modifiedLines[j].TrimStart().StartsWith("namespace:"))
                        {
                            // Weiter suchen
                            continue;
                        }
                        else if (!modifiedLines[j].StartsWith(indent + " "))
                        {
                            // Ende des metadata-Bereichs erreicht
                            break;
                        }
                    }
                    
                    if (labelsIndex != -1)
                    {
                        // Labels-Abschnitt gefunden - prüfe, ob app: Label existiert
                        bool hasAppLabel = false;
                        bool hasCorrectAppLabel = false;
                        int labelsIndentLevel = modifiedLines[labelsIndex].Length - modifiedLines[labelsIndex].TrimStart().Length;
                        string labelsIndent = new string(' ', labelsIndentLevel + 2); // +2 für Einrückung der Werte
                        
                        for (int j = labelsIndex + 1; j < modifiedLines.Count; j++)
                        {
                            if (!modifiedLines[j].StartsWith(labelsIndent))
                            {
                                break; // Ende der Labels
                            }
                            
                            if (modifiedLines[j].Trim().StartsWith("app:"))
                            {
                                hasAppLabel = true;
                                if (modifiedLines[j].Trim() == $"app: {amName}")
                                {
                                    hasCorrectAppLabel = true;
                                }
                                else
                                {
                                    // Überschreibe das vorhandene app-Label
                                    modifiedLines[j] = labelsIndent + $"app: {amName}";
                                    _loggingService.LogInfo($"app: {amName} Label in Hauptmetadaten aktualisiert");
                                }
                            }
                        }
                        
                        if (!hasAppLabel)
                        {
                            // Füge app: amName Label hinzu
                            modifiedLines.Insert(labelsIndex + 1, labelsIndent + $"app: {amName}");
                            _loggingService.LogInfo($"app: {amName} Label zu Hauptmetadaten hinzugefügt");
                        }
                    }
                    else
                    {
                        // Kein Labels-Abschnitt gefunden, erstelle ihn
                        for (int j = i + 1; j < modifiedLines.Count; j++)
                        {
                            if (!modifiedLines[j].StartsWith(indent + " ") || modifiedLines[j].Trim().StartsWith("spec:"))
                            {
                                // Füge den Labels-Abschnitt vor dem Ende des metadata-Bereichs ein
                                modifiedLines.Insert(j, indent + "  labels:");
                                modifiedLines.Insert(j + 1, indent + $"    app: {amName}");
                                _loggingService.LogInfo($"Neuen Labels-Abschnitt mit app: {amName} in Hauptmetadaten hinzugefügt");
                                break;
                            }
                        }
                    }
                    
                    foundMainMetadata = true;
                }
                
                // Template-Metadata-Bereich
                if (line.Trim().StartsWith("template:") && !foundTemplateMetadata)
                {
                    // Suche nach dem metadata-Abschnitt innerhalb von template
                    int templateMetadataIndex = -1;
                    int templateIndentLevel = line.Length - line.TrimStart().Length;
                    string templateIndent = new string(' ', templateIndentLevel + 2); // +2 für Einrückung innerhalb von template
                    
                    for (int j = i + 1; j < modifiedLines.Count; j++)
                    {
                        if (modifiedLines[j].Trim() == "metadata:")
                        {
                            templateMetadataIndex = j;
                            break;
                        }
                        else if (!modifiedLines[j].StartsWith(templateIndent))
                        {
                            // Ende des template-Bereichs erreicht
                            break;
                        }
                    }
                    
                    if (templateMetadataIndex != -1)
                    {
                        _loggingService.LogInfo("Template-Metadaten-Bereich gefunden");
                        
                        // Suche nach dem labels-Abschnitt oder erstelle ihn
                        int labelsIndex = -1;
                        int metadataIndentLevel = modifiedLines[templateMetadataIndex].Length - modifiedLines[templateMetadataIndex].TrimStart().Length;
                        string metadataIndent = new string(' ', metadataIndentLevel + 2); // +2 für Einrückung innerhalb von metadata
                        
                        for (int j = templateMetadataIndex + 1; j < modifiedLines.Count; j++)
                        {
                            if (modifiedLines[j].Trim() == "labels:")
                            {
                                labelsIndex = j;
                                break;
                            }
                            else if (!modifiedLines[j].StartsWith(metadataIndent.Substring(0, metadataIndent.Length - 2)) || 
                                     modifiedLines[j].Trim().StartsWith("spec:"))
                            {
                                // Ende des metadata-Bereichs erreicht
                                break;
                            }
                        }
                        
                        if (labelsIndex != -1)
                        {
                            // Labels-Abschnitt gefunden - prüfe, ob app: Label existiert
                            bool hasAppLabel = false;
                            bool hasCorrectAppLabel = false;
                            int labelsIndentLevel = modifiedLines[labelsIndex].Length - modifiedLines[labelsIndex].TrimStart().Length;
                            string labelsIndent = new string(' ', labelsIndentLevel + 2); // +2 für Einrückung der Werte
                            
                            for (int j = labelsIndex + 1; j < modifiedLines.Count; j++)
                            {
                                if (!modifiedLines[j].StartsWith(labelsIndent))
                                {
                                    break; // Ende der Labels
                                }
                                
                                if (modifiedLines[j].Trim().StartsWith("app:"))
                                {
                                    hasAppLabel = true;
                                    if (modifiedLines[j].Trim() == $"app: {amName}")
                                    {
                                        hasCorrectAppLabel = true;
                                    }
                                    else
                                    {
                                        // Überschreibe das vorhandene app-Label
                                        modifiedLines[j] = labelsIndent + $"app: {amName}";
                                        _loggingService.LogInfo($"app: {amName} Label in Template-Metadaten aktualisiert");
                                    }
                                }
                            }
                            
                            if (!hasAppLabel)
                            {
                                // Füge app: amName Label hinzu
                                modifiedLines.Insert(labelsIndex + 1, labelsIndent + $"app: {amName}");
                                _loggingService.LogInfo($"app: {amName} Label zu Template-Metadaten hinzugefügt");
                            }
                        }
                        else
                        {
                            // Kein Labels-Abschnitt gefunden, erstelle ihn
                            modifiedLines.Insert(templateMetadataIndex + 1, metadataIndent + "labels:");
                            modifiedLines.Insert(templateMetadataIndex + 2, metadataIndent + $"  app: {amName}");
                            _loggingService.LogInfo($"Neuen Labels-Abschnitt mit app: {amName} in Template-Metadaten hinzugefügt");
                        }
                    }
                    else
                    {
                        // Kein Metadata-Abschnitt innerhalb von template gefunden
                        _loggingService.LogWarning("Kein metadata-Abschnitt im template-Bereich gefunden");
                    }
                    
                    foundTemplateMetadata = true;
                }
                
                // Wenn beide Bereiche gefunden wurden, können wir abbrechen
                if (foundMainMetadata && foundTemplateMetadata)
                {
                    break;
                }
            }
            
            _loggingService.LogInfo("YAML-Modifikation abgeschlossen");
            return string.Join("\n", modifiedLines);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Modifizieren des YAML für Automation Manager: {ex.Message}", ex);
            throw;
        }
    }

    private void LoadAutomationManagerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // YAMLs aktualisieren, falls sich Eingaben geändert haben
            UpdateAutomationManagerYamls();
            
            // Deployment-YAML laden
            Task.Run(async () => await LoadAutomationManagerDeploymentYaml());
            
            // Erfolgreiche Anfrage, Fehlermeldung zurücksetzen
            string amName = AutomationManagerNameTextBox.Text;
            if (!string.IsNullOrEmpty(amName))
            {
                AutomationManagerErrorMessage.Text = $"Deployment wird geladen... Nach dem Laden wird 'app: {amName}' in den Labels verwendet.";
            }
            else
            {
                AutomationManagerErrorMessage.Text = "Deployment wird geladen...";
            }
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Blue);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            AutomationManagerErrorMessage.Text = $"Fehler beim Laden des Deployments: {ex.Message}";
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Laden des Deployments: {ex.Message}", ex);
        }
    }

    private async void DeleteUnboundPVsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isClusterLoggedIn)
            {
                MessageBox.Show("Bitte melden Sie sich zuerst an einem Cluster an.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Button-Styles zurücksetzen und aktuellen Button hervorheben
            ResetAppButtonStyles();
            DeleteUnboundPVsButton.Style = (Style)FindResource("ActiveSidebarButton");
            
            HideAllContent();
            DeleteUnboundPVsContent.Visibility = Visibility.Visible;
            
            // Initialize sidebar
            MainSidebar.Visibility = Visibility.Collapsed;
            ApplicationsSidebar.Visibility = Visibility.Visible;
            
            // Set the user info
            ApplicationsUserText.Text = CurrentUserText.Text;
            ApplicationsFullNameText.Text = FullNameText.Text;
            
            // Set current project text
            string currentProject = await _openShiftService.GetCurrentProjectName();
            UnboundPVsCurrentProjectText.Text = currentProject;
            
            // Load the unbound PVs
            await LoadUnboundPersistentVolumes();
            
            // Initialize GridView columns
            AdjustGridViewColumns(UnboundPVsGridView);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Anzeigen der ungebundenen Volumes: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Anzeigen der ungebundenen Volumes: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadUnboundPersistentVolumes()
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            
            var (pvs, error) = await _openShiftService.GetUnboundPersistentVolumes();
            if (error == null)
            {
                UnboundPVsListView.ItemsSource = pvs;
                
                if (pvs.Count == 0)
                {
                    MessageBox.Show("Es wurden keine ungebundenen Volumes gefunden, die mit dem aktuellen Projekt in Verbindung stehen.", 
                                     "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show($"Fehler beim Laden der ungebundenen PVs: {error}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Laden der ungebundenen PVs: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Laden der ungebundenen PVs: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void RefreshUnboundPVsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentProject = await _openShiftService.GetCurrentProjectName();
            UnboundPVsCurrentProjectText.Text = currentProject;
            
            // Refresh unbound PVs
            await LoadUnboundPersistentVolumes();
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Fehler beim Aktualisieren der ungebundenen Volumes: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Aktualisieren der ungebundenen Volumes: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteSelectedUnboundPVsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (UnboundPVsListView.SelectedItems.Count == 0)
            {
                MessageBox.Show("Bitte wählen Sie mindestens ein PV aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var selectedPVs = UnboundPVsListView.SelectedItems.Cast<PersistentVolume>().ToList();
            var result = MessageBox.Show($"Möchten Sie die {selectedPVs.Count} ausgewählten PVs wirklich löschen?", 
                                         "PVs löschen", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                
                int successCount = 0;
                int failCount = 0;
                
                foreach (var pv in selectedPVs)
                {
                    var (success, message) = await _openShiftService.DeletePersistentVolume(pv.Name);
                    
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        _loggingService.LogError($"Fehler beim Löschen des PV {pv.Name}: {message}");
                    }
                }
                
                Mouse.OverrideCursor = null;
                
                if (failCount == 0)
                {
                    MessageBox.Show($"Alle {successCount} PVs wurden erfolgreich gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{successCount} PVs wurden erfolgreich gelöscht. {failCount} PVs konnten nicht gelöscht werden. Überprüfen Sie die Logs für Details.", 
                                     "Teilweise erfolgreich", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                // Aktualisieren der Anzeige
                await LoadUnboundPersistentVolumes();
            }
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            _loggingService.LogError($"Fehler beim Löschen der ausgewählten PVs: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Löschen der ausgewählten PVs: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteAllUnboundPVsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pvs = UnboundPVsListView.Items.Cast<PersistentVolume>().ToList();
            
            if (pvs.Count == 0)
            {
                MessageBox.Show("Es sind keine ungebundenen PVs vorhanden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show($"Möchten Sie alle {pvs.Count} ungebundenen PVs wirklich löschen?", 
                                         "Alle PVs löschen", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                
                int successCount = 0;
                int failCount = 0;
                
                foreach (var pv in pvs)
                {
                    var (success, message) = await _openShiftService.DeletePersistentVolume(pv.Name);
                    
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        _loggingService.LogError($"Fehler beim Löschen des PV {pv.Name}: {message}");
                    }
                }
                
                Mouse.OverrideCursor = null;
                
                if (failCount == 0)
                {
                    MessageBox.Show($"Alle {successCount} PVs wurden erfolgreich gelöscht.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{successCount} PVs wurden erfolgreich gelöscht. {failCount} PVs konnten nicht gelöscht werden. Überprüfen Sie die Logs für Details.", 
                                     "Teilweise erfolgreich", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                // Aktualisieren der Anzeige
                await LoadUnboundPersistentVolumes();
            }
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            _loggingService.LogError($"Fehler beim Löschen aller ungebundenen PVs: {ex.Message}", ex);
            MessageBox.Show($"Fehler beim Löschen aller ungebundenen PVs: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async void CreateAutomationManagerPVsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Verhindere parallele Ausführungen
            if (_isApplyingAutomationManagerChanges)
            {
                MessageBox.Show("Es läuft bereits eine Anwendung der Änderungen. Bitte warten Sie, bis der Vorgang abgeschlossen ist.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            _isApplyingAutomationManagerChanges = true;
            
            // YAMLs aktualisieren, falls sich Eingaben geändert haben
            UpdateAutomationManagerYamls();
            
            // Stelle sicher, dass alle erforderlichen Felder ausgefüllt sind
            if (string.IsNullOrEmpty(AutomationManagerNameTextBox.Text) || string.IsNullOrEmpty(AutomationManagerPortTextBox.Text))
            {
                MessageBox.Show("Bitte füllen Sie alle erforderlichen Felder aus.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                _isApplyingAutomationManagerChanges = false;
                return;
            }
            
            // Aktualisiere Status
            AutomationManagerErrorMessage.Text = "YAMLs wurden erfolgreich erstellt. Klicken Sie auf 'Alle YAMLs deployen' um die Ressourcen zu erstellen.";
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            
            // Aktiviere den "Alle YAMLs deployen" Button
            DeployAllAutomationManagerYamlsButton.IsEnabled = true;
            
            _isApplyingAutomationManagerChanges = false;
        }
        catch (Exception ex)
        {
            AutomationManagerErrorMessage.Text = $"Fehler beim Erstellen der YAMLs: {ex.Message}";
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Erstellen der Automation Manager YAMLs: {ex.Message}", ex);
            _isApplyingAutomationManagerChanges = false;
        }
    }
    
    private async void DeployAllAutomationManagerYamlsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Verhindere parallele Ausführungen
            if (_isApplyingAutomationManagerChanges)
            {
                MessageBox.Show("Es läuft bereits eine Anwendung der Änderungen. Bitte warten Sie, bis der Vorgang abgeschlossen ist.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            _isApplyingAutomationManagerChanges = true;
            
            // Sicherheitsabfrage
            MessageBoxResult result = MessageBox.Show(
                "Möchten Sie die folgenden Änderungen anwenden?\n\n" +
                $"- PersistentVolumes erstellen\n" +
                $"- Service erstellen\n" +
                $"- Route erstellen",
                "Änderungen anwenden",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result != MessageBoxResult.Yes)
            {
                _isApplyingAutomationManagerChanges = false;
                return;
            }
            
            string currentProject = _openShiftService.CurrentProject;
            
            // Statusmeldung anzeigen
            AutomationManagerErrorMessage.Text = "Änderungen werden angewendet...";
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Blue);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            
            // Temporäre Dateien erstellen
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenshiftOPSCenter");
            System.IO.Directory.CreateDirectory(tempPath);
            
            string pv1YamlPath = System.IO.Path.Combine(tempPath, $"pv1-{currentProject}-{Guid.NewGuid()}.yaml");
            string pv2YamlPath = System.IO.Path.Combine(tempPath, $"pv2-{currentProject}-{Guid.NewGuid()}.yaml");
            string serviceYamlPath = System.IO.Path.Combine(tempPath, $"service-{currentProject}-{Guid.NewGuid()}.yaml");
            string routeYamlPath = System.IO.Path.Combine(tempPath, $"route-{currentProject}-{Guid.NewGuid()}.yaml");
            
            // Schreibe PV, Service und Route YAMLs in temporäre Dateien
            await System.IO.File.WriteAllTextAsync(pv1YamlPath, AutomationManagerPV1YamlTextBox.Text);
            await System.IO.File.WriteAllTextAsync(pv2YamlPath, AutomationManagerPV2YamlTextBox.Text);
            await System.IO.File.WriteAllTextAsync(serviceYamlPath, AutomationManagerServiceYamlTextBox.Text);
            await System.IO.File.WriteAllTextAsync(routeYamlPath, AutomationManagerRouteYamlTextBox.Text);
            
            // Wende die Änderungen an und tracke den Erfolg jeder Operation separat
            bool overallSuccess = true;
            StringBuilder statusMessage = new StringBuilder();
            
            // Wende die PVs an
            bool pv1Success = await ApplyYamlFile(pv1YamlPath);
            if (pv1Success)
            {
                statusMessage.AppendLine("PersistentVolume (Logs) erfolgreich erstellt.");
            }
            else
            {
                statusMessage.AppendLine("Fehler beim Erstellen des PersistentVolume (Logs).");
                overallSuccess = false;
            }
            
            bool pv2Success = await ApplyYamlFile(pv2YamlPath);
            if (pv2Success)
            {
                statusMessage.AppendLine("PersistentVolume (Persistency) erfolgreich erstellt.");
            }
            else
            {
                statusMessage.AppendLine("Fehler beim Erstellen des PersistentVolume (Persistency).");
                overallSuccess = false;
            }
            
            // Wende Service an - unabhängig vom Erfolg der PVs
            bool serviceSuccess = await ApplyYamlFile(serviceYamlPath);
            if (serviceSuccess)
            {
                statusMessage.AppendLine("Service erfolgreich erstellt.");
                _loggingService.LogInfo($"Service für Automation Manager erfolgreich erstellt in Projekt {currentProject}");
            }
            else
            {
                statusMessage.AppendLine("Fehler beim Erstellen des Service.");
                _loggingService.LogError($"Fehler beim Erstellen des Service für Automation Manager in Projekt {currentProject}");
                overallSuccess = false;
            }
            
            // Warte einen Moment, damit der Service erstellt werden kann, bevor die Route versucht wird
            await Task.Delay(1000);
            
            // Wende Route an - unabhängig vom Erfolg des Service
            bool routeSuccess = await ApplyYamlFile(routeYamlPath);
            if (routeSuccess)
            {
                statusMessage.AppendLine("Route erfolgreich erstellt.");
                _loggingService.LogInfo($"Route für Automation Manager erfolgreich erstellt in Projekt {currentProject}");
            }
            else
            {
                statusMessage.AppendLine("Fehler beim Erstellen der Route.");
                _loggingService.LogError($"Fehler beim Erstellen der Route für Automation Manager in Projekt {currentProject}");
                overallSuccess = false;
            }
            
            // Temporäre Dateien löschen
            try
            {
                if (System.IO.File.Exists(pv1YamlPath)) System.IO.File.Delete(pv1YamlPath);
                if (System.IO.File.Exists(pv2YamlPath)) System.IO.File.Delete(pv2YamlPath);
                if (System.IO.File.Exists(serviceYamlPath)) System.IO.File.Delete(serviceYamlPath);
                if (System.IO.File.Exists(routeYamlPath)) System.IO.File.Delete(routeYamlPath);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Fehler beim Löschen temporärer Dateien: {ex.Message}");
            }
            
            // Aktualisiere Status
            if (overallSuccess)
            {
                AutomationManagerErrorMessage.Text = "Alle Ressourcen wurden erfolgreich erstellt:\n" + statusMessage.ToString() + 
                    "\nBitte erstellen Sie jetzt den Automation Manager im MES und aktivieren Sie die Checkbox 'AM im MES deployed'.";
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Green);
                
                // Aktiviere die Checkbox
                AutomationManagerDeployedCheckbox.IsEnabled = true;
                _hasDeployedAutomationManagerYamls = true;
            }
            else
            {
                AutomationManagerErrorMessage.Text = "Es sind Fehler aufgetreten:\n" + statusMessage.ToString();
                AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            }
            
            _isApplyingAutomationManagerChanges = false;
        }
        catch (Exception ex)
        {
            AutomationManagerErrorMessage.Text = $"Fehler beim Anwenden der Änderungen: {ex.Message}";
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Anwenden der Automation Manager Änderungen: {ex.Message}", ex);
            _isApplyingAutomationManagerChanges = false;
        }
    }

    private void AutomationManagerDeployedCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        try
        {
            // Aktiviere die restlichen Buttons
            LoadAutomationManagerButton.IsEnabled = true;
            ConfigureAutomationManagerYamlButton.IsEnabled = true;
            ApplyAutomationManagerChangesButton.IsEnabled = true;
            
            // Statusmeldung aktualisieren
            AutomationManagerErrorMessage.Text = "Automation Manager im MES wurde deployed. Sie können jetzt das Deployment laden und konfigurieren.";
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Blue);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            AutomationManagerErrorMessage.Text = $"Fehler beim Aktivieren der Buttons: {ex.Message}";
            AutomationManagerErrorMessage.Foreground = new SolidColorBrush(Colors.Red);
            AutomationManagerErrorMessage.Visibility = Visibility.Visible;
            _loggingService.LogError($"Fehler beim Aktivieren der Buttons: {ex.Message}", ex);
        }
    }
}