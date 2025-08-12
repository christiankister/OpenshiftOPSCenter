using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenshiftOPSCenter.App.Services
{
    public interface ILoggingService
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? ex = null);
        void LogDebug(string message);
    }

    public class LoggingService : ILoggingService
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();

        public LoggingService(string logFilePath = "logs/app.log")
        {
            _logFilePath = logFilePath;
            EnsureLogDirectoryExists();
            
            // Logge Startup-Information
            LogInfo($"Logging-Service initialisiert mit Log-Pfad: {_logFilePath}");
        }

        private void EnsureLogDirectoryExists()
        {
            try
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Console.WriteLine($"Log-Verzeichnis erstellt: {directory}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Erstellen des Log-Verzeichnisses: {ex.Message}");
            }
        }

        public void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        public void LogWarning(string message)
        {
            WriteLog("WARNING", message);
        }

        public void LogError(string message, Exception? ex = null)
        {
            WriteLog("ERROR", message, ex);
        }

        public void LogDebug(string message)
        {
            WriteLog("DEBUG", message);
        }

        private void WriteLog(string level, string message, Exception? exception = null)
        {
            try
            {
                var logMessage = new StringBuilder();
                logMessage.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
                
                if (exception != null)
                {
                    logMessage.AppendLine($"Exception: {exception.Message}");
                    logMessage.AppendLine($"StackTrace: {exception.StackTrace}");
                }

                lock (_lockObject)
                {
                    File.AppendAllText(_logFilePath, logMessage.ToString());
                }
                
                // Debug-Ausgabe auch in die Konsole
                Console.WriteLine($"[{level}] {message}");
            }
            catch (Exception ex)
            {
                // Fehler beim Logging ausgeben, aber keine Exception werfen
                Console.WriteLine($"Fehler beim Schreiben des Logs: {ex.Message}");
            }
        }
    }
} 