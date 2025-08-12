using System;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.DirectoryServices.AccountManagement;
using OpenshiftOPSCenter.App.Models;
using System.Threading.Tasks;
using OpenshiftOPSCenter.App.Interfaces;
using System.IO;
using System.Text;

namespace OpenshiftOPSCenter.App.Services
{
    public class LdapService : ILdapService
    {
        private readonly ILoggingService _loggingService;
        private readonly string _server;
        private readonly int _port;
        private readonly string _baseDn;
        private readonly string _domain;
        private readonly X509Certificate2 _rootCert;
        private readonly X509Certificate2 _serverCert;

        // LDAP-Gruppen für die verschiedenen Rollen
        private const string ADMIN_GROUP = "LDE08_K8s_OOC_Admin";
        private const string POWERUSER_GROUP = "LDE08_K8s_OOC_Poweruser";
        private const string USER_GROUP = "LDE08_K8s_OOC_User";
        private const string OPCUA_GROUP = "LDE08_K8s_OOC_OPCUA";

        public LdapService(ILoggingService? loggingService = null)
        {
            _loggingService = loggingService ?? new LoggingService();
            _server = "de08-wdc03.bbmag.bbraun.com";
            _port = 636;
            _baseDn = "DC=BBMAG,DC=BBRAUN,DC=COM";
            _domain = "BBMAG";

            try
            {
                _loggingService.LogInfo("Lade Zertifikate für LDAP-Verbindung");
                
                // Pfad zum Zertifikatsverzeichnis
                string certPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cert");
                _loggingService.LogDebug($"Zertifikatsverzeichnis: {certPath}");
                
                // Überprüfen, ob das Verzeichnis existiert
                if (!Directory.Exists(certPath))
                {
                    _loggingService.LogWarning($"Zertifikatsverzeichnis existiert nicht: {certPath}");
                    throw new DirectoryNotFoundException($"Zertifikatsverzeichnis nicht gefunden: {certPath}");
                }
                
                // Root-Zertifikat laden
                string rootCertPath = Path.Combine(certPath, "Root.cer");
                _loggingService.LogDebug($"Root-Zertifikatspfad: {rootCertPath}");
                
                if (!File.Exists(rootCertPath))
                {
                    _loggingService.LogError($"Root-Zertifikat nicht gefunden: {rootCertPath}");
                    throw new FileNotFoundException($"Root-Zertifikat nicht gefunden: {rootCertPath}");
                }
                
                _rootCert = new X509Certificate2(rootCertPath);
                _loggingService.LogInfo($"Root-Zertifikat geladen: {_rootCert.Subject}");

                // Server-Zertifikat laden
                string serverCertPath = Path.Combine(certPath, "Server.cer");
                _loggingService.LogDebug($"Server-Zertifikatspfad: {serverCertPath}");
                
                if (!File.Exists(serverCertPath))
                {
                    _loggingService.LogError($"Server-Zertifikat nicht gefunden: {serverCertPath}");
                    throw new FileNotFoundException($"Server-Zertifikat nicht gefunden: {serverCertPath}");
                }
                
                _serverCert = new X509Certificate2(serverCertPath);
                _loggingService.LogInfo($"Server-Zertifikat geladen: {_serverCert.Subject}");

                // Zertifikate zum Store hinzufügen
                var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                store.Add(_rootCert);
                store.Add(_serverCert);
                store.Close();
                _loggingService.LogInfo("Zertifikate zum Store hinzugefügt");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Fehler beim Laden der Zertifikate", ex);
                throw;
            }
        }

        public async Task<(bool success, string message)> ValidateCredentialsAsync(string username, string password)
        {
            try
            {
                _loggingService.LogInfo($"Starte Authentifizierung für Benutzer: {username}");
                _loggingService.LogDebug($"Verwende LDAP-Server: {_server}:{_port}");
                _loggingService.LogDebug($"Base DN: {_baseDn}");

                var identifier = new LdapDirectoryIdentifier(_server, _port);
                var ldapConnection = new LdapConnection(identifier)
                {
                    AuthType = AuthType.Basic,
                    SessionOptions = { ProtocolVersion = 3 },
                    ClientCertificates = { _serverCert }
                };

                _loggingService.LogDebug("LDAP-Verbindung konfiguriert mit Server-Zertifikat");
                
                var networkCredential = new NetworkCredential(username, password);
                ldapConnection.Bind(networkCredential);
                
                _loggingService.LogInfo($"Benutzer {username} erfolgreich authentifiziert");
                return (true, "Authentifizierung erfolgreich");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler bei der Authentifizierung für Benutzer {username}", ex);
                return (false, $"Authentifizierungsfehler: {ex.Message}");
            }
        }

        public async Task<(string fullName, string role, List<string> groups, List<string> rights)> GetUserInfoAsync(string username)
        {
            try
            {
                _loggingService.LogInfo($"Starte Benutzerinformationsabruf für: {username}");
                
                // Verwende den LDAP-Server direkt statt der Domain
                using (var context = new PrincipalContext(ContextType.Domain, _server, _baseDn))
                {
                    _loggingService.LogDebug($"Suche nach Benutzer {username} auf Server {_server}");
                    var user = UserPrincipal.FindByIdentity(context, username);
                    
                    if (user != null)
                    {
                        _loggingService.LogDebug($"Benutzer gefunden: {user.DisplayName} (SAM: {user.SamAccountName})");
                        string fullName = !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : username;
                        
                        _loggingService.LogDebug("Prüfe Gruppenmitgliedschaften:");
                        var allGroups = new List<string>();
                        var userGroups = user.GetGroups();
                        foreach (var group in userGroups)
                        {
                            _loggingService.LogDebug($"- Mitglied von Gruppe: {group.Name} (SID: {group.Sid})");
                            allGroups.Add(group.Name);
                        }
                        
                        string role = await DetermineUserRoleAsync(user);
                        _loggingService.LogInfo($"Benutzerrolle für {username} bestimmt: {role}");

                        // Filtere nur die relevanten LDAP-Gruppen
                        var relevantGroups = new List<string>
                        {
                            ADMIN_GROUP,
                            POWERUSER_GROUP,
                            USER_GROUP,
                            OPCUA_GROUP
                        };
                        
                        var filteredGroups = allGroups.Where(g => relevantGroups.Contains(g)).ToList();
                        _loggingService.LogInfo($"Relevante Gruppen für {username}: {string.Join(", ", filteredGroups)}");

                        // Rechte basierend auf der Rolle bestimmen
                        var rights = DetermineUserRights(role);
                        _loggingService.LogInfo($"Benutzerrechte für {username} bestimmt: {string.Join(", ", rights)}");
                        
                        return (fullName, role, filteredGroups, rights);
                    }
                    
                    _loggingService.LogWarning($"Benutzer {username} nicht gefunden");
                    throw new Exception($"Benutzer {username} nicht gefunden");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler beim Abrufen der Benutzerinformationen für {username}", ex);
                throw;
            }
        }

        private async Task<string> DetermineUserRoleAsync(UserPrincipal user)
        {
            if (user == null) return string.Empty;

            try
            {
                _loggingService.LogDebug($"Starte Rollenbestimmung für Benutzer: {user.SamAccountName}");
                
                // Verwende den LDAP-Server direkt statt der Domain
                using (var context = new PrincipalContext(ContextType.Domain, _server, _baseDn))
                {
                    var adminGroup = GroupPrincipal.FindByIdentity(context, ADMIN_GROUP);
                    var powerUserGroup = GroupPrincipal.FindByIdentity(context, POWERUSER_GROUP);
                    var userGroup = GroupPrincipal.FindByIdentity(context, USER_GROUP);
                    var opcUaGroup = GroupPrincipal.FindByIdentity(context, OPCUA_GROUP);

                    _loggingService.LogDebug("Prüfe Gruppenmitgliedschaften für Rollenbestimmung:");
                    
                    if (adminGroup != null)
                    {
                        _loggingService.LogDebug($"- Prüfe Admin-Gruppe: {adminGroup.Name}");
                        if (user.IsMemberOf(adminGroup))
                        {
                            _loggingService.LogInfo($"Benutzer {user.SamAccountName} ist Mitglied der Admin-Gruppe");
                            return UserRoles.Admin;
                        }
                    }
                    
                    if (powerUserGroup != null)
                    {
                        _loggingService.LogDebug($"- Prüfe PowerUser-Gruppe: {powerUserGroup.Name}");
                        if (user.IsMemberOf(powerUserGroup))
                        {
                            _loggingService.LogInfo($"Benutzer {user.SamAccountName} ist Mitglied der PowerUser-Gruppe");
                            return UserRoles.PowerUser;
                        }
                    }
                    
                    if (userGroup != null)
                    {
                        _loggingService.LogDebug($"- Prüfe User-Gruppe: {userGroup.Name}");
                        if (user.IsMemberOf(userGroup))
                        {
                            _loggingService.LogInfo($"Benutzer {user.SamAccountName} ist Mitglied der User-Gruppe");
                            return UserRoles.User;
                        }
                    }
                    
                    if (opcUaGroup != null)
                    {
                        _loggingService.LogDebug($"- Prüfe OPCUA-Gruppe: {opcUaGroup.Name}");
                        if (user.IsMemberOf(opcUaGroup))
                        {
                            _loggingService.LogInfo($"Benutzer {user.SamAccountName} ist Mitglied der OPCUA-Gruppe");
                            return UserRoles.OpcUa;
                        }
                    }

                    _loggingService.LogWarning($"Keine Rolle für Benutzer {user.SamAccountName} gefunden");
                    return "Keine Rolle";
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Fehler bei der Rollenbestimmung für Benutzer {user.SamAccountName}", ex);
                return "Keine Rolle";
            }
        }

        private List<string> DetermineUserRights(string role)
        {
            // Falls die Rolle Admin ist, werden automatisch alle Rechte zugewiesen
            if (role == UserRoles.Admin)
            {
                return UserRight.AllRights;
            }
            
            // Für andere Rollen verwenden wir die vordefinierten Rechte aus dem UserRight-Modell
            if (UserRight.DefaultRightsByRole.TryGetValue(role, out List<string> rights))
            {
                return rights;
            }
            
            // Wenn keine Rechte definiert sind, geben wir eine leere Liste zurück
            return new List<string>();
        }

        private async Task<string> GetUserRoleAsync(LdapConnection connection, string username)
        {
            try
            {
                // Suchfilter für den Benutzer
                string userFilter = $"(&(objectClass=user)(sAMAccountName={username}))";
                
                // Suchanfrage erstellen
                var searchRequest = new SearchRequest(
                    _baseDn,
                    userFilter,
                    SearchScope.Subtree,
                    new string[] { "distinguishedName" }
                );
                
                // Suchanfrage ausführen
                var searchResponse = (SearchResponse)await Task.Run(() => connection.SendRequest(searchRequest));
                
                if (searchResponse.Entries.Count > 0)
                {
                    string userDn = searchResponse.Entries[0].DistinguishedName;
                    
                    // Gruppenmitgliedschaft prüfen
                    if (await IsUserInGroupAsync(connection, userDn, ADMIN_GROUP))
                        return UserRoles.Admin;
                    if (await IsUserInGroupAsync(connection, userDn, POWERUSER_GROUP))
                        return UserRoles.PowerUser;
                    if (await IsUserInGroupAsync(connection, userDn, USER_GROUP))
                        return UserRoles.User;
                    if (await IsUserInGroupAsync(connection, userDn, OPCUA_GROUP))
                        return UserRoles.OpcUa;
                }
                
                return "Keine Rolle";
            }
            catch (Exception)
            {
                return "Fehler bei der Rollenprüfung";
            }
        }

        private async Task<bool> IsUserInGroupAsync(LdapConnection connection, string userDn, string groupName)
        {
            try
            {
                // Suchfilter für die Gruppe
                string groupFilter = $"(&(objectClass=group)(sAMAccountName={groupName}))";
                
                // Suchanfrage erstellen
                var searchRequest = new SearchRequest(
                    _baseDn,
                    groupFilter,
                    SearchScope.Subtree,
                    new string[] { "member" }
                );
                
                // Suchanfrage ausführen
                var searchResponse = (SearchResponse)await Task.Run(() => connection.SendRequest(searchRequest));
                
                if (searchResponse.Entries.Count > 0)
                {
                    // Prüfen, ob der Benutzer in der Gruppe ist
                    var memberAttribute = searchResponse.Entries[0].Attributes["member"];
                    if (memberAttribute != null)
                    {
                        foreach (var member in memberAttribute)
                        {
                            if (member.ToString().Equals(userDn, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
                
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
} 