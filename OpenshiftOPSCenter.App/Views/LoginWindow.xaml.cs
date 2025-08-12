using System;
using System.Windows;
using System.Windows.Controls;
using OpenshiftOPSCenter.App.Interfaces;
using OpenshiftOPSCenter.App.Services;
using OpenshiftOPSCenter.App.Data;
using OpenshiftOPSCenter.App.Models;

namespace OpenshiftOPSCenter.App.Views
{
    public partial class LoginWindow : Window
    {
        private readonly IWindowsAuthService _windowsAuthService = new WindowsAuthService();
        private readonly ILdapService _ldapService = new LdapService();
        private readonly AppDbContext _dbContext = new AppDbContext();
        private readonly ILoggingService _loggingService = new LoggingService();
        private string _currentUsername = string.Empty;
        private string _currentFullName = string.Empty;
        public string Username { get; private set; } = string.Empty;
        public UserInfo? CurrentUser { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();
            _loggingService.LogInfo("LoginWindow wurde initialisiert");
            LoadCurrentUser();
        }

        private void LoadCurrentUser()
        {
            try
            {
                _currentUsername = Environment.UserName;
                _currentFullName = Environment.UserDomainName + "\\" + _currentUsername;

                CurrentUserText.Text = $"Aktueller Windows-Benutzer: {_currentUsername}";
                FullNameText.Text = $"Vollständiger Name: {_currentFullName}";
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Fehler beim Laden des Benutzers", ex);
                ErrorMessage.Text = "Fehler beim Laden der Benutzerinformationen.";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string username = UsernameTextBox.Text.Trim();
                string password = PasswordBox.Password.Trim();

                if (string.IsNullOrWhiteSpace(username))
                {
                    MessageBox.Show("Bitte geben Sie einen Benutzernamen ein.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show("Bitte geben Sie ein Passwort ein.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = await _ldapService.ValidateCredentialsAsync(username, password);
                
                if (result.success)
                {
                    _loggingService.LogInfo($"Benutzer {username} erfolgreich angemeldet");
                    var userInfo = await _ldapService.GetUserInfoAsync(username);
                    CurrentUser = new UserInfo
                    {
                        Username = username,
                        FullName = userInfo.fullName,
                        Role = userInfo.role
                    };
                    DialogResult = true;
                    Close();
                }
                else
                {
                    _loggingService.LogWarning($"Anmeldeversuch fehlgeschlagen für Benutzer {username}");
                    MessageBox.Show("Anmeldung fehlgeschlagen. Bitte überprüfen Sie Ihre Anmeldedaten.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Fehler bei der Anmeldung", ex);
                MessageBox.Show($"Ein Fehler ist aufgetreten: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 