using System;
using OpenshiftOPSCenter.App.Interfaces;
using OpenshiftOPSCenter.App.Models;
using System.Linq;
using OpenshiftOPSCenter.App.Data;

namespace OpenshiftOPSCenter.App.Services
{
    public class UserService : IUserService
    {
        private UserInfo? _currentUser;
        private readonly ILoggingService _loggingService;
        private readonly AppDbContext _dbContext;

        public UserService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _dbContext = new AppDbContext();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                _loggingService.LogInfo("Initialisiere Datenbank...");
                _dbContext.InitializeDatabase();
                
                // Prüfe, ob bereits ein Benutzer existiert
                var username = Environment.UserName;
                var user = _dbContext.Users.FirstOrDefault(u => u.Username == username);
                
                if (user == null)
                {
                    _loggingService.LogInfo($"Erstelle neuen Benutzer für {username}");
                    user = new User
                    {
                        Username = username,
                        Email = $"{username}@bbraun.com",
                        Role = "User", // Standardrolle
                        Function = "Nicht angegeben",
                        IsActive = true,
                        LastLogin = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.Users.Add(user);
                    _dbContext.SaveChanges();
                }
                
                _loggingService.LogInfo("Datenbank erfolgreich initialisiert");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler bei der Datenbankinitialisierung: {ex.Message}", ex);
            }
        }

        public User? GetCurrentUser()
        {
            try
            {
                _loggingService.LogInfo("GetCurrentUser wird aufgerufen...");
                
                var username = Environment.UserName;
                _loggingService.LogInfo($"Aktueller Windows-Benutzername: {username}");

                var user = _dbContext.Users.FirstOrDefault(u => u.Username == username);
                
                if (user != null)
                {
                    _loggingService.LogInfo($"Benutzer in Datenbank gefunden: {user.Username}, Rolle: {user.Role}, Letzter Login: {user.LastLogin}");
                }
                else
                {
                    _loggingService.LogWarning($"Kein Benutzer in der Datenbank für Windows-Benutzer {username} gefunden");
                }

                return user;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler in GetCurrentUser: {ex.Message}", ex);
                _loggingService.LogError($"Stack Trace: {ex.StackTrace}");
                return null;
            }
        }

        public void SetCurrentUser(UserInfo user)
        {
            _currentUser = user;
            
            // Aktualisiere das LastLogin-Datum in der Datenbank
            var dbUser = _dbContext.Users.FirstOrDefault(u => u.Username == user.Username);
            if (dbUser != null)
            {
                dbUser.LastLogin = DateTime.Now;
                _dbContext.SaveChanges();
                _loggingService.LogInfo($"LastLogin für Benutzer {user.Username} aktualisiert: {dbUser.LastLogin}");
            }
            
            _loggingService.LogInfo($"Benutzer angemeldet: {user.Username}");
        }

        public void ClearCurrentUser()
        {
            if (_currentUser != null)
            {
                _loggingService.LogInfo($"Benutzer abgemeldet: {_currentUser.Username}");
                _currentUser = null;
            }
        }
    }
} 