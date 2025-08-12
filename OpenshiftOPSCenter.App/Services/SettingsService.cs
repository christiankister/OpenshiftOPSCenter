using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenshiftOPSCenter.App.Data;
using OpenshiftOPSCenter.App.Interfaces;
using OpenshiftOPSCenter.App.Models;

namespace OpenshiftOPSCenter.App.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly AppDbContext _dbContext;

        public SettingsService()
        {
            _dbContext = new AppDbContext();
            _dbContext.InitializeDatabase();
        }

        public async Task<string> GetOcToolFolderAsync()
        {
            // Versuche aus DB
            try
            {
                var setting = _dbContext.AppSettings.FirstOrDefault(s => s.Key == "OcToolFolder");
                if (setting != null)
                {
                    return setting.Value ?? string.Empty;
                }
            }
            catch
            {
                // ignorieren und auf Datei-Fallback gehen
            }

            // Datei-Fallback
            var (ok, value) = TryReadFromFile();
            return ok ? value : string.Empty;
        }

        public async Task SetOcToolFolderAsync(string folderPath)
        {
            // Versuche in DB zu speichern
            try
            {
                var setting = _dbContext.AppSettings.FirstOrDefault(s => s.Key == "OcToolFolder");
                if (setting == null)
                {
                    setting = new AppSetting
                    {
                        Key = "OcToolFolder",
                        Value = folderPath
                    };
                    _dbContext.AppSettings.Add(setting);
                }
                else
                {
                    setting.Value = folderPath;
                    setting.UpdatedAt = System.DateTime.UtcNow;
                }

                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                // Datei-Fallback
                WriteToFile(folderPath);
            }
        }

        private static (bool ok, string value) TryReadFromFile()
        {
            try
            {
                var (dir, file) = GetConfigPath();
                if (!File.Exists(file)) return (false, string.Empty);
                var json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json)) return (false, string.Empty);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("OcToolFolder", out var val))
                {
                    return (true, val.GetString() ?? string.Empty);
                }
            }
            catch { }
            return (false, string.Empty);
        }

        private static void WriteToFile(string folderPath)
        {
            try
            {
                var (dir, file) = GetConfigPath();
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(new { OcToolFolder = folderPath });
                File.WriteAllText(file, json);
            }
            catch { }
        }

        private static (string dir, string file) GetConfigPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenshiftOPSCenter");
            var file = Path.Combine(dir, "settings.json");
            return (dir, file);
        }
    }
}


