using Microsoft.EntityFrameworkCore;
using OpenshiftOPSCenter.App.Models;
using System;
using System.Linq;

namespace OpenshiftOPSCenter.App.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<ButtonRight> ButtonRights { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=app.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<AppSetting>()
                .HasIndex(s => s.Key)
                .IsUnique();

            // Erstelle Standard-Admin-Benutzer
            modelBuilder.Entity<User>().HasData(
                new User
                {
                    Id = 1,
                    Username = "kistchde",
                    Email = "christian.kister@bbraun.com",
                    Role = UserRoles.Admin,
                    IsActive = true,
                    CreatedAt = System.DateTime.UtcNow
                },
                new User
                {
                    Id = 2,
                    Username = "kistchde_aa",
                    Email = "christian.kister@bbraun.com",
                    Role = UserRoles.Admin,
                    IsActive = true,
                    CreatedAt = System.DateTime.UtcNow
                }
            );

            // Erstelle Standard-Rechte für verschiedene Rollen
            SeedDefaultButtonRights(modelBuilder);

            // Default App Settings
            modelBuilder.Entity<AppSetting>().HasData(
                new AppSetting
                {
                    Id = 1,
                    Key = "OcToolFolder",
                    Value = string.Empty,
                    CreatedAt = System.DateTime.UtcNow,
                    UpdatedAt = System.DateTime.UtcNow
                }
            );
        }

        private void SeedDefaultButtonRights(ModelBuilder modelBuilder)
        {
            int id = 1;

            // Main Sidebar Buttons
            AddButtonRight(modelBuilder, ref id, "UserManagementButton", "Benutzerverwaltung", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "UserManagementButton", "Benutzerverwaltung", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "UserManagementButton", "Benutzerverwaltung", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "SettingsButton", "Einstellungen", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "SettingsButton", "Einstellungen", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "SettingsButton", "Einstellungen", UserRoles.OpcUa, false);

            // Applications Sidebar Buttons
            AddButtonRight(modelBuilder, ref id, "AppButton1", "Deployments/Pods", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "AppButton1", "Deployments/Pods", UserRoles.User, true);
            AddButtonRight(modelBuilder, ref id, "AppButton1", "Deployments/Pods", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "AppButton2", "Volumes", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "AppButton2", "Volumes", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "AppButton2", "Volumes", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "DeleteUnboundPVsButton", "Delete Unbound PV's", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "DeleteUnboundPVsButton", "Delete Unbound PV's", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "DeleteUnboundPVsButton", "Delete Unbound PV's", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "AppButton3", "Multi Project", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "AppButton3", "Multi Project", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "AppButton3", "Multi Project", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "AppButton4", "Create Webserver", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "AppButton4", "Create Webserver", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "AppButton4", "Create Webserver", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "AutomationManagerButton", "Create Automation Manager", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "AutomationManagerButton", "Create Automation Manager", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "AutomationManagerButton", "Create Automation Manager", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "OISFilesVolumeButton", "Create OISFiles Volume", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "OISFilesVolumeButton", "Create OISFiles Volume", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "OISFilesVolumeButton", "Create OISFiles Volume", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "OISFilesOptIotVolumeButton", "Create OISFiles OPT/IOT", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "OISFilesOptIotVolumeButton", "Create OISFiles OPT/IOT", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "OISFilesOptIotVolumeButton", "Create OISFiles OPT/IOT", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "FirmwareVolumeButton", "Create Firmware Volume", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "FirmwareVolumeButton", "Create Firmware Volume", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "FirmwareVolumeButton", "Create Firmware Volume", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "EDHRVolumeButton", "Create EDHR Volume", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "EDHRVolumeButton", "Create EDHR Volume", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "EDHRVolumeButton", "Create EDHR Volume", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "SAPConnectivityVolumeButton", "SAP Connectivity Volume", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "SAPConnectivityVolumeButton", "SAP Connectivity Volume", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "SAPConnectivityVolumeButton", "SAP Connectivity Volume", UserRoles.OpcUa, false);

            AddButtonRight(modelBuilder, ref id, "AppButton5", "Service, Route, Rolebinding", UserRoles.PowerUser, true);
            AddButtonRight(modelBuilder, ref id, "AppButton5", "Service, Route, Rolebinding", UserRoles.User, false);
            AddButtonRight(modelBuilder, ref id, "AppButton5", "Service, Route, Rolebinding", UserRoles.OpcUa, false);
        }

        private void AddButtonRight(ModelBuilder modelBuilder, ref int id, string buttonName, string description, string role, bool isAssigned)
        {
            modelBuilder.Entity<ButtonRight>().HasData(
                new ButtonRight
                {
                    Id = id++,
                    ButtonName = buttonName,
                    Description = description,
                    Role = role,
                    IsAssigned = isAssigned,
                    CreatedAt = System.DateTime.UtcNow
                }
            );
        }

        public void InitializeDatabase()
        {
            try
            {
                // Stelle sicher, dass die Datenbank erstellt wird
                Database.EnsureCreated();
                
                // Logge die Anzahl der vorhandenen Einträge in ButtonRights
                int buttonRightsCount = ButtonRights.Count();
                Console.WriteLine($"ButtonRights-Tabelle enthält {buttonRightsCount} Einträge");
                
                if (buttonRightsCount == 0)
                {
                    // Wenn keine ButtonRights existieren, erstelle die Standard-Rechte manuell
                    Console.WriteLine("Keine ButtonRights vorhanden, initialisiere mit Standardwerten");
                    SeedButtonRightsManually();
                    
                    // Überprüfe, ob das Seeding erfolgreich war
                    int newCount = ButtonRights.Count();
                    Console.WriteLine($"Nach dem Seeding enthält die ButtonRights-Tabelle {newCount} Einträge");
                }
            }
            catch (Exception ex)
            {
                // Detaillierte Fehlerinformationen protokollieren
                Console.WriteLine($"FEHLER bei der Datenbankinitialisierung: {ex.Message}");
                Console.WriteLine($"Stacktrace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
            }
        }

        private void SeedButtonRightsManually()
        {
            // Erstelle Standard-Rechte für verschiedene Rollen
            
            // PowerUser-Rechte
            AddButtonRightManually("UserManagementButton", "Benutzerverwaltung", UserRoles.PowerUser, true);
            AddButtonRightManually("SettingsButton", "Einstellungen", UserRoles.PowerUser, true);
            AddButtonRightManually("AppButton1", "Deployments/Pods", UserRoles.PowerUser, true);
            AddButtonRightManually("AppButton2", "Volumes", UserRoles.PowerUser, true);
            AddButtonRightManually("DeleteUnboundPVsButton", "Delete Unbound PV's", UserRoles.PowerUser, true);
            AddButtonRightManually("AppButton3", "Multi Project", UserRoles.PowerUser, true);
            AddButtonRightManually("AppButton4", "Create Webserver", UserRoles.PowerUser, true);
            AddButtonRightManually("AutomationManagerButton", "Create Automation Manager", UserRoles.PowerUser, true);
            AddButtonRightManually("OISFilesVolumeButton", "Create OISFiles Volume", UserRoles.PowerUser, true);
            AddButtonRightManually("OISFilesOptIotVolumeButton", "Create OISFiles OPT/IOT", UserRoles.PowerUser, true);
            AddButtonRightManually("FirmwareVolumeButton", "Create Firmware Volume", UserRoles.PowerUser, true);
            AddButtonRightManually("EDHRVolumeButton", "Create EDHR Volume", UserRoles.PowerUser, true);
            AddButtonRightManually("SAPConnectivityVolumeButton", "SAP Connectivity Volume", UserRoles.PowerUser, true);
            AddButtonRightManually("AppButton5", "Service, Route, Rolebinding", UserRoles.PowerUser, true);
            
            // User-Rechte
            AddButtonRightManually("UserManagementButton", "Benutzerverwaltung", UserRoles.User, false);
            AddButtonRightManually("SettingsButton", "Einstellungen", UserRoles.User, false);
            AddButtonRightManually("AppButton1", "Deployments/Pods", UserRoles.User, true);
            AddButtonRightManually("AppButton2", "Volumes", UserRoles.User, false);
            AddButtonRightManually("DeleteUnboundPVsButton", "Delete Unbound PV's", UserRoles.User, false);
            AddButtonRightManually("AppButton3", "Multi Project", UserRoles.User, false);
            AddButtonRightManually("AppButton4", "Create Webserver", UserRoles.User, false);
            AddButtonRightManually("AutomationManagerButton", "Create Automation Manager", UserRoles.User, false);
            AddButtonRightManually("OISFilesVolumeButton", "Create OISFiles Volume", UserRoles.User, false);
            AddButtonRightManually("OISFilesOptIotVolumeButton", "Create OISFiles OPT/IOT", UserRoles.User, false);
            AddButtonRightManually("FirmwareVolumeButton", "Create Firmware Volume", UserRoles.User, false);
            AddButtonRightManually("EDHRVolumeButton", "Create EDHR Volume", UserRoles.User, false);
            AddButtonRightManually("SAPConnectivityVolumeButton", "SAP Connectivity Volume", UserRoles.User, false);
            AddButtonRightManually("AppButton5", "Service, Route, Rolebinding", UserRoles.User, false);
            
            // OpcUa-Rechte
            AddButtonRightManually("UserManagementButton", "Benutzerverwaltung", UserRoles.OpcUa, false);
            AddButtonRightManually("SettingsButton", "Einstellungen", UserRoles.OpcUa, false);
            AddButtonRightManually("AppButton1", "Deployments/Pods", UserRoles.OpcUa, false);
            AddButtonRightManually("AppButton2", "Volumes", UserRoles.OpcUa, false);
            AddButtonRightManually("DeleteUnboundPVsButton", "Delete Unbound PV's", UserRoles.OpcUa, false);
            AddButtonRightManually("AppButton3", "Multi Project", UserRoles.OpcUa, false);
            AddButtonRightManually("AppButton4", "Create Webserver", UserRoles.OpcUa, false);
            AddButtonRightManually("AutomationManagerButton", "Create Automation Manager", UserRoles.OpcUa, false);
            AddButtonRightManually("OISFilesVolumeButton", "Create OISFiles Volume", UserRoles.OpcUa, false);
            AddButtonRightManually("OISFilesOptIotVolumeButton", "Create OISFiles OPT/IOT", UserRoles.OpcUa, false);
            AddButtonRightManually("FirmwareVolumeButton", "Create Firmware Volume", UserRoles.OpcUa, false);
            AddButtonRightManually("EDHRVolumeButton", "Create EDHR Volume", UserRoles.OpcUa, false);
            AddButtonRightManually("SAPConnectivityVolumeButton", "SAP Connectivity Volume", UserRoles.OpcUa, false);
            AddButtonRightManually("AppButton5", "Service, Route, Rolebinding", UserRoles.OpcUa, false);
            
            SaveChanges();
        }

        private void AddButtonRightManually(string buttonName, string description, string role, bool isAssigned)
        {
            ButtonRights.Add(new ButtonRight
            {
                ButtonName = buttonName,
                Description = description,
                Role = role,
                IsAssigned = isAssigned,
                CreatedAt = System.DateTime.UtcNow
            });
        }
    }
} 