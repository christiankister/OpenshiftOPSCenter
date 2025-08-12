using Microsoft.EntityFrameworkCore;
using OpenshiftOPSCenter.App.Data;
using OpenshiftOPSCenter.App.Interfaces;
using OpenshiftOPSCenter.App.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenshiftOPSCenter.App.Services
{
    public class ButtonRightService : IButtonRightService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILoggingService _loggingService;

        public ButtonRightService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _dbContext = new AppDbContext();
            _dbContext.InitializeDatabase();
        }

        public async Task<bool> HasRightAsync(string buttonName, string role)
        {
            // Admin hat immer alle Rechte
            if (role == UserRoles.Admin)
                return true;

            var right = await _dbContext.ButtonRights
                .FirstOrDefaultAsync(r => r.ButtonName == buttonName && r.Role == role);

            return right != null && right.IsAssigned;
        }

        public async Task<List<string>> GetRightsForRoleAsync(string role)
        {
            // Admin hat immer alle Rechte
            if (role == UserRoles.Admin)
            {
                return await _dbContext.ButtonRights
                    .Select(r => r.ButtonName)
                    .Distinct()
                    .ToListAsync();
            }

            return await _dbContext.ButtonRights
                .Where(r => r.Role == role && r.IsAssigned)
                .Select(r => r.ButtonName)
                .ToListAsync();
        }

        public async Task<Dictionary<string, bool>> GetAllRightsForRoleAsync(string role)
        {
            var result = new Dictionary<string, bool>();

            // Wenn Admin, dann alle Rechte auf true setzen
            if (role == UserRoles.Admin)
            {
                var allButtonNames = await _dbContext.ButtonRights
                    .Select(r => r.ButtonName)
                    .Distinct()
                    .ToListAsync();

                foreach (var buttonName in allButtonNames)
                {
                    result[buttonName] = true;
                }
                
                return result;
            }

            // Für andere Rollen: hole die tatsächlichen Rechte
            var rights = await _dbContext.ButtonRights
                .Where(r => r.Role == role)
                .ToListAsync();

            foreach (var right in rights)
            {
                result[right.ButtonName] = right.IsAssigned;
            }

            return result;
        }

        public async Task SetRightForRoleAsync(string buttonName, string role, bool isAssigned)
        {
            if (role == UserRoles.Admin)
            {
                _loggingService.LogWarning("Versuch, Rechte für Admin zu ändern. Admin hat immer alle Rechte.");
                return;
            }

            var right = await _dbContext.ButtonRights
                .FirstOrDefaultAsync(r => r.ButtonName == buttonName && r.Role == role);

            if (right != null)
            {
                right.IsAssigned = isAssigned;
                right.UpdatedAt = DateTime.Now;
                await _dbContext.SaveChangesAsync();
                _loggingService.LogInfo($"Recht für Button {buttonName} für Rolle {role} auf {isAssigned} gesetzt.");
            }
            else
            {
                // Wenn das Recht nicht existiert, erstellen wir es automatisch
                _loggingService.LogInfo($"Recht für Button {buttonName} und Rolle {role} nicht gefunden. Erstelle neu.");
                var newRight = new ButtonRight
                {
                    ButtonName = buttonName,
                    Description = GetButtonDescription(buttonName),
                    Role = role,
                    IsAssigned = isAssigned,
                    CreatedAt = DateTime.Now
                };
                
                _dbContext.ButtonRights.Add(newRight);
                await _dbContext.SaveChangesAsync();
                _loggingService.LogInfo($"Neues Recht für Button {buttonName} für Rolle {role} auf {isAssigned} erstellt.");
            }
        }

        // Hilfsmethode, um eine Beschreibung für einen Button zu bekommen
        private string GetButtonDescription(string buttonName)
        {
            switch (buttonName)
            {
                case "UserManagementButton":
                    return "Benutzerverwaltung";
                case "SettingsButton":
                    return "Einstellungen";
                case "AppButton1":
                    return "Deployments/Pods";
                case "AppButton2":
                    return "Volumes";
                case "AppButton3":
                    return "Multi Project";
                case "AppButton4":
                    return "Create Webserver";
                case "OISFilesVolumeButton":
                    return "Create OISFiles Volume";
                case "OISFilesOptIotVolumeButton":
                    return "Create OISFiles OPT/IOT";
                case "FirmwareVolumeButton":
                    return "Create Firmware Volume";
                case "EDHRVolumeButton":
                    return "Create EDHR Volume";
                case "SAPConnectivityVolumeButton":
                    return "SAP Connectivity Volume";
                case "AppButton5":
                    return "Service, Route, Rolebinding";
                default:
                    return buttonName;
            }
        }

        public async Task ResetToDefaultRightsAsync(string role)
        {
            if (role == UserRoles.Admin)
            {
                _loggingService.LogWarning("Versuch, Standard-Rechte für Admin zu setzen. Admin hat immer alle Rechte.");
                return;
            }

            try
            {
                _loggingService.LogInfo($"Lösche Rechte für Rolle {role}");
                
                // Nur die ButtonRights für die angegebene Rolle löschen
                var rightsToDelete = await _dbContext.ButtonRights
                    .Where(br => br.Role == role)
                    .ToListAsync();
                
                if (rightsToDelete.Any())
                {
                    _dbContext.ButtonRights.RemoveRange(rightsToDelete);
                    await _dbContext.SaveChangesAsync();
                }
                
                _loggingService.LogInfo($"Erstelle Standard-Rechte für Rolle {role}");
                
                // Standard-Rechte erstellen
                await CreateDefaultRightsForRole(role);
                
                _loggingService.LogInfo($"Standard-Rechte für Rolle {role} wurden wiederhergestellt");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler beim Zurücksetzen der Rechte für Rolle {role}: {ex.Message}", ex);
                throw;
            }
        }

        private async Task CreateDefaultRightsForRole(string role)
        {
            // Main Sidebar Buttons
            await AddButtonRightAsync("UserManagementButton", "Benutzerverwaltung", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("SettingsButton", "Einstellungen", role, role == UserRoles.PowerUser);
            
            // Applications Sidebar Buttons
            await AddButtonRightAsync("AppButton1", "Deployments/Pods", role, role == UserRoles.PowerUser || role == UserRoles.User);
            await AddButtonRightAsync("AppButton2", "Volumes", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("AppButton3", "Multi Project", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("AppButton4", "Create Webserver", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("OISFilesVolumeButton", "Create OISFiles Volume", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("OISFilesOptIotVolumeButton", "Create OISFiles OPT/IOT", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("FirmwareVolumeButton", "Create Firmware Volume", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("EDHRVolumeButton", "Create EDHR Volume", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("SAPConnectivityVolumeButton", "SAP Connectivity Volume", role, role == UserRoles.PowerUser);
            await AddButtonRightAsync("AppButton5", "Service, Route, Rolebinding", role, role == UserRoles.PowerUser);
        }

        private async Task AddButtonRightAsync(string buttonName, string description, string role, bool isAssigned)
        {
            var right = new ButtonRight
            {
                ButtonName = buttonName,
                Description = description,
                Role = role,
                IsAssigned = isAssigned,
                CreatedAt = DateTime.Now
            };
            
            _dbContext.ButtonRights.Add(right);
            await _dbContext.SaveChangesAsync();
        }

        public async Task SaveRightsAsync()
        {
            await _dbContext.SaveChangesAsync();
            _loggingService.LogInfo("Alle Rechte gespeichert.");
        }
    }
} 