using Oxide.Core;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("IPBan", "NINJA WORKS", "1.0.1")]
    [Description("Extended BAN system with IP address and SteamID")]
    class IPBan : RustPlugin
    {
        #region Fields
        private PluginData pluginData;
        #endregion

        #region Data Structure
        class PluginData
        {
            public Dictionary<ulong, BanInfo> BannedPlayers = new Dictionary<ulong, BanInfo>();
        }

        class BanInfo
        {
            public ulong SteamID { get; set; }
            public string IPAddress { get; set; }
            public string PlayerName { get; set; }
            public string BannedBy { get; set; }
            public string Reason { get; set; }
            public string BanDate { get; set; }
            public bool IsOfflineBan { get; set; }

            public BanInfo() { }

            public BanInfo(ulong steamId, string ip, string name, string bannedBy, string reason, bool isOffline = false)
            {
                SteamID = steamId;
                IPAddress = ip;
                PlayerName = name;
                BannedBy = bannedBy;
                Reason = reason;
                BanDate = System.DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                IsOfflineBan = isOffline;
            }
        }
        #endregion

        #region Initialization
        void Init()
        {
            LoadData();
        }

        void Unload()
        {
            SaveData();
        }
        #endregion

        #region Data Management
        void LoadData()
        {
            try
            {
                pluginData = Interface.Oxide.DataFileSystem.ReadObject<PluginData>(Name);
            }
            catch
            {
                pluginData = new PluginData();
            }

            if (pluginData == null)
                pluginData = new PluginData();
        }

        void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, pluginData);
        }
        #endregion

        #region Connection Check
        object CanUserLogin(string name, string id, string ipAddress)
        {
            ulong steamId;
            if (!ulong.TryParse(id, out steamId))
                return null;

            if (pluginData.BannedPlayers.ContainsKey(steamId))
            {
                var banInfo = pluginData.BannedPlayers[steamId];
                
                if (banInfo.IsOfflineBan && string.IsNullOrEmpty(banInfo.IPAddress))
                {
                    banInfo.IPAddress = ipAddress;
                    banInfo.PlayerName = name;
                    banInfo.IsOfflineBan = false;
                    SaveData();
                    Puts($"Offline banned player {name} (SteamID: {steamId}, IP: {ipAddress}) connection detected and information recorded.");
                }
                
                return $"You are banned from this server.\nReason: {banInfo.Reason}\nBan Date: {banInfo.BanDate}";
            }

            var bannedByIP = pluginData.BannedPlayers.Values.FirstOrDefault(b => 
                !string.IsNullOrEmpty(b.IPAddress) && b.IPAddress == ipAddress);
            if (bannedByIP != null)
            {
                return $"This IP address is banned from this server.\nReason: {bannedByIP.Reason}\nBan Date: {bannedByIP.BanDate}";
            }

            return null;
        }
        #endregion

        #region Commands
        [ConsoleCommand("ipban")]
        void CmdIPBan(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                return;
            }

            if (!arg.HasArgs(1))
            {
                arg.ReplyWith("Usage: ipban <player name or SteamID or IP address> [reason]");
                arg.ReplyWith("Example: ipban 76561198012345678 Cheating");
                arg.ReplyWith("Example: ipban 192.168.1.100 VPN Usage");
                arg.ReplyWith("Note: Works for both online and offline players");
                return;
            }

            string targetIdentifier = arg.Args[0];
            string reason = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : "Banned by administrator";
            string adminName = "Server Console";

            // Check if input is an IP address
            if (IsValidIP(targetIdentifier))
            {
                // Ban by IP address
                var existingBan = pluginData.BannedPlayers.Values.FirstOrDefault(b => b.IPAddress == targetIdentifier);
                if (existingBan != null)
                {
                    arg.ReplyWith($"IP address {targetIdentifier} is already banned.");
                    return;
                }

                // Check if any online player has this IP
                BasePlayer targetByIP = BasePlayer.activePlayerList.FirstOrDefault(p => 
                    p.net.connection.ipaddress.Split(':')[0] == targetIdentifier);

                if (targetByIP != null)
                {
                    // Online player with this IP
                    var banInfo = new BanInfo(
                        targetByIP.userID,
                        targetIdentifier,
                        targetByIP.displayName,
                        adminName,
                        reason,
                        false
                    );

                    pluginData.BannedPlayers[targetByIP.userID] = banInfo;
                    SaveData();

                    targetByIP.Kick($"You have been banned from this server.\nReason: {reason}");

                    arg.ReplyWith($"[IP Ban] Player {targetByIP.displayName} (SteamID: {targetByIP.userID}, IP: {targetIdentifier}) has been banned.");
                    Puts($"{adminName} banned {targetByIP.displayName} by IP. Reason: {reason}");
                }
                else
                {
                    // Offline IP ban - create entry with temporary SteamID
                    ulong tempSteamID = GenerateTempSteamID();
                    var banInfo = new BanInfo(
                        tempSteamID,
                        targetIdentifier,
                        $"IP Banned User ({targetIdentifier})",
                        adminName,
                        reason,
                        true
                    );

                    pluginData.BannedPlayers[tempSteamID] = banInfo;
                    SaveData();

                    arg.ReplyWith($"[IP Ban] IP address {targetIdentifier} has been added to the ban list.");
                    arg.ReplyWith("Note: Any player connecting from this IP will be banned.");
                    Puts($"{adminName} banned IP address {targetIdentifier}. Reason: {reason}");
                }
                return;
            }

            // Try to find online player
            BasePlayer target = FindPlayer(targetIdentifier);

            if (target != null)
            {
                string ipAddress = target.net.connection.ipaddress.Split(':')[0];

                var banInfo = new BanInfo(
                    target.userID,
                    ipAddress,
                    target.displayName,
                    adminName,
                    reason,
                    false
                );

                pluginData.BannedPlayers[target.userID] = banInfo;
                SaveData();

                target.Kick($"You have been banned from this server.\nReason: {reason}");

                arg.ReplyWith($"[Online] Player {target.displayName} (SteamID: {target.userID}, IP: {ipAddress}) has been banned.");
                Puts($"{adminName} banned {target.displayName}. Reason: {reason}");
            }
            else
            {
                // Try to parse as SteamID
                ulong steamId;
                if (!ulong.TryParse(targetIdentifier, out steamId))
                {
                    arg.ReplyWith($"Player '{targetIdentifier}' not found.");
                    arg.ReplyWith("To ban an offline player, specify their SteamID or IP address.");
                    return;
                }

                if (pluginData.BannedPlayers.ContainsKey(steamId))
                {
                    arg.ReplyWith($"SteamID {steamId} is already banned.");
                    return;
                }

                var banInfo = new BanInfo(
                    steamId,
                    "",
                    $"Offline Player ({steamId})",
                    adminName,
                    reason,
                    true
                );

                pluginData.BannedPlayers[steamId] = banInfo;
                SaveData();

                arg.ReplyWith($"[Offline] SteamID {steamId} has been added to the ban list.");
                arg.ReplyWith("Note: When this player connects, their IP address and name will be automatically recorded and they will be banned.");
                Puts($"{adminName} banned offline player (SteamID: {steamId}). Reason: {reason}");
            }
        }

        [ConsoleCommand("ipunban")]
        void CmdIPUnban(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                return;
            }

            if (!arg.HasArgs(1))
            {
                arg.ReplyWith("Usage: ipunban <SteamID or IP address>");
                arg.ReplyWith("Example: ipunban 76561198012345678");
                arg.ReplyWith("Example: ipunban 192.168.1.100");
                return;
            }

            string identifier = arg.Args[0];
            int removedCount = 0;
            List<string> removedPlayers = new List<string>();

            // Check if input is an IP address
            if (IsValidIP(identifier))
            {
                // Unban all players with this IP address
                var toRemove = pluginData.BannedPlayers.Where(kvp => kvp.Value.IPAddress == identifier).ToList();
                
                if (toRemove.Count == 0)
                {
                    arg.ReplyWith($"IP address {identifier} is not banned.");
                    return;
                }

                foreach (var entry in toRemove)
                {
                    removedPlayers.Add($"{entry.Value.PlayerName} (SteamID: {entry.Key})");
                    pluginData.BannedPlayers.Remove(entry.Key);
                    removedCount++;
                }

                SaveData();

                arg.ReplyWith($"Removed {removedCount} player(s) with IP address {identifier}:");
                foreach (var playerInfo in removedPlayers)
                {
                    arg.ReplyWith($"  - {playerInfo}");
                }
                Puts($"IP address {identifier} has been unbanned. {removedCount} player(s) removed.");
                return;
            }

            // Try to parse as SteamID
            ulong steamId;
            if (!ulong.TryParse(identifier, out steamId))
            {
                arg.ReplyWith("Invalid SteamID or IP address format.");
                return;
            }

            if (!pluginData.BannedPlayers.ContainsKey(steamId))
            {
                arg.ReplyWith($"SteamID {steamId} is not banned.");
                return;
            }

            var banInfo = pluginData.BannedPlayers[steamId];
            string bannedIP = banInfo.IPAddress;
            
            // Remove the specified SteamID
            pluginData.BannedPlayers.Remove(steamId);
            removedPlayers.Add($"{banInfo.PlayerName} (SteamID: {steamId})");
            removedCount++;

            // Also remove all other players with the same IP address
            if (!string.IsNullOrEmpty(bannedIP))
            {
                var toRemove = pluginData.BannedPlayers.Where(kvp => kvp.Value.IPAddress == bannedIP).ToList();
                
                foreach (var entry in toRemove)
                {
                    removedPlayers.Add($"{entry.Value.PlayerName} (SteamID: {entry.Key})");
                    pluginData.BannedPlayers.Remove(entry.Key);
                    removedCount++;
                }
            }

            SaveData();

            if (removedCount == 1)
            {
                arg.ReplyWith($"Player {banInfo.PlayerName} (SteamID: {steamId}) has been unbanned.");
                Puts($"SteamID {steamId} has been unbanned.");
            }
            else
            {
                arg.ReplyWith($"Removed {removedCount} player(s) with SteamID {steamId} and associated IP {bannedIP}:");
                foreach (var playerInfo in removedPlayers)
                {
                    arg.ReplyWith($"  - {playerInfo}");
                }
                Puts($"SteamID {steamId} and all players with IP {bannedIP} have been unbanned. {removedCount} player(s) removed.");
            }
        }

        [ConsoleCommand("ipbanlist")]
        void CmdIPBanList(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                return;
            }

            if (pluginData.BannedPlayers.Count == 0)
            {
                arg.ReplyWith("No banned players.");
                return;
            }

            arg.ReplyWith($"===== Ban List ({pluginData.BannedPlayers.Count} entries) =====");
            foreach (var kvp in pluginData.BannedPlayers)
            {
                var ban = kvp.Value;
                string status = ban.IsOfflineBan ? "[Offline Ban - Not Connected]" : "[Recorded]";
                
                arg.ReplyWith($"{status} Name: {ban.PlayerName}");
                arg.ReplyWith($"  SteamID: {ban.SteamID}");
                arg.ReplyWith($"  IP: {(string.IsNullOrEmpty(ban.IPAddress) ? "Not Recorded" : ban.IPAddress)}");
                arg.ReplyWith($"  Reason: {ban.Reason}");
                arg.ReplyWith($"  Banned By: {ban.BannedBy}");
                arg.ReplyWith($"  Ban Date: {ban.BanDate}");
                arg.ReplyWith("---");
            }
        }
        #endregion

        #region Helper Methods
        bool IsValidIP(string ip)
        {
            string[] parts = ip.Split('.');
            if (parts.Length != 4)
                return false;

            foreach (string part in parts)
            {
                int num;
                if (!int.TryParse(part, out num) || num < 0 || num > 255)
                    return false;
            }
            return true;
        }

        ulong GenerateTempSteamID()
        {
            System.Random random = new System.Random();
            ulong tempID;
            do
            {
                tempID = (ulong)(90000000000000000L + (long)(random.NextDouble() * 9999999999999999L));
            } while (pluginData.BannedPlayers.ContainsKey(tempID));
            
            return tempID;
        }
        BasePlayer FindPlayer(string nameOrId)
        {
            ulong steamId;
            if (ulong.TryParse(nameOrId, out steamId))
            {
                return BasePlayer.FindByID(steamId);
            }

            var players = BasePlayer.activePlayerList.Where(p => 
                p.displayName.ToLower().Contains(nameOrId.ToLower())).ToList();

            if (players.Count == 1)
                return players[0];

            return null;
        }
        #endregion
    }
}