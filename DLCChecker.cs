using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("DLC Checker", "YourName", "1.0.0")]
    [Description("Check player's owned DLCs")]
    public class DLCChecker : RustPlugin
    {
        // Rustの主要DLC一覧
        private readonly Dictionary<uint, string> knownDLCs = new Dictionary<uint, string>
        {
            { 1213160, "Instruments Pack" },
            { 1282100, "Cobalt Pack" },
            { 1491040, "Desert Raid Pack" },
            { 1491050, "Artic Pack" },
            { 1670430, "Voice Props Pack" },
            { 1670440, "Staging Branch" },
            { 2026710, "Spacesuit Pack" },
            { 2077550, "Public Test Branch" },
            { 2174850, "Big Grin Pack" },
            { 2524750, "Blackout Pack" }
        };

        [ChatCommand("checkdlc")]
        private void CheckDLCCommand(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                // 自分のDLCをチェック
                CheckPlayerDLC(player, player);
                return;
            }

            // 管理者権限チェック
            if (!player.IsAdmin)
            {
                SendReply(player, "このコマンドを使用する権限がありません。");
                return;
            }

            // 他のプレイヤーのDLCをチェック
            var targetPlayer = FindPlayer(args[0]);
            if (targetPlayer == null)
            {
                SendReply(player, $"プレイヤー '{args[0]}' が見つかりません。");
                return;
            }

            CheckPlayerDLC(player, targetPlayer);
        }

        [ConsoleCommand("checkdlc")]
        private void CheckDLCConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length == 0)
            {
                arg.ReplyWith("使用方法: checkdlc <プレイヤー名>");
                return;
            }

            var targetPlayer = FindPlayer(arg.Args[0]);
            if (targetPlayer == null)
            {
                arg.ReplyWith($"プレイヤー '{arg.Args[0]}' が見つかりません。");
                return;
            }

            var dlcInfo = GetPlayerDLCInfo(targetPlayer);
            arg.ReplyWith($"{targetPlayer.displayName} のDLC情報:\n{dlcInfo}");
        }

        private void CheckPlayerDLC(BasePlayer requester, BasePlayer target)
        {
            var dlcInfo = GetPlayerDLCInfo(target);
            
            if (requester == target)
            {
                SendReply(requester, $"あなたの所有DLC:\n{dlcInfo}");
            }
            else
            {
                SendReply(requester, $"{target.displayName} の所有DLC:\n{dlcInfo}");
            }
        }

        private string GetPlayerDLCInfo(BasePlayer player)
        {
            var result = $"プレイヤー: {player.displayName} (Steam ID: {player.userID})\n";
            result += $"接続時間: {TimeSpan.FromSeconds(player.secondsConnected):hh\\:mm\\:ss}\n\n";
            
            result += "DLC所有状況の確認:\n";
            
            foreach (var dlc in knownDLCs)
            {
                bool hasDLC = CheckPlayerDLCOwnership(player, dlc.Key);
                string status = hasDLC ? "✓ 所有済み" : "✗ 未所有";
                result += $"{dlc.Value}: {status}\n";
            }

            result += "\n注意: DLC検出は推測ベースです。完全に正確ではない場合があります。";
            return result;
        }

        private bool CheckPlayerDLCOwnership(BasePlayer player, uint dlcId)
        {
            try
            {
                // 基本的なDLCチェック方法
                switch (dlcId)
                {
                    case 1213160: // Instruments Pack
                        return HasInstrumentItems(player);
                    case 1282100: // Cobalt Pack
                        return HasCobaltItems(player);
                    case 1491040: // Desert Raid Pack
                        return HasDesertRaidItems(player);
                    case 1491050: // Arctic Pack
                        return HasArcticItems(player);
                    case 1670430: // Voice Props Pack
                        return HasVoicePropsItems(player);
                    case 2026710: // Spacesuit Pack
                        return HasSpacesuitItems(player);
                    case 2174850: // Big Grin Pack
                        return HasBigGrinItems(player);
                    case 2524750: // Blackout Pack
                        return HasBlackoutItems(player);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                PrintError($"DLCチェック中にエラー (DLC ID: {dlcId}): {ex.Message}");
                return false;
            }
        }

        private bool HasInstrumentItems(BasePlayer player)
        {
            var instrumentItems = new[] { "piano", "drumkit", "trumpet", "tuba", "cowbell", "xylophone" };
            return HasAnyBlueprints(player, instrumentItems);
        }

        private bool HasCobaltItems(BasePlayer player)
        {
            // Cobalt Pack特有のアイテムをチェック
            var cobaltItems = new[] { "metal.facemask", "roadsign.jacket", "roadsign.kilt" };
            return HasSpecialSkins(player, cobaltItems);
        }

        private bool HasDesertRaidItems(BasePlayer player)
        {
            // Desert Raid Pack特有のアイテムをチェック
            return HasSpecialSkins(player, new[] { "attire.hide.vest", "burlap.shirt" });
        }

        private bool HasArcticItems(BasePlayer player)
        {
            // Arctic Pack特有のアイテムをチェック
            return HasSpecialSkins(player, new[] { "hoodie", "pants" });
        }

        private bool HasVoicePropsItems(BasePlayer player)
        {
            // Voice Props Pack特有のアイテムをチェック
            var voiceItems = new[] { "cassette", "cassette.recorder" };
            return HasAnyBlueprints(player, voiceItems);
        }

        private bool HasSpacesuitItems(BasePlayer player)
        {
            // Spacesuit Pack特有のアイテムをチェック
            return HasSpecialSkins(player, new[] { "hazmatsuit" });
        }

        private bool HasBigGrinItems(BasePlayer player)
        {
            // Big Grin Pack特有のアイテムをチェック
            return HasSpecialSkins(player, new[] { "mask.balaclava" });
        }

        private bool HasBlackoutItems(BasePlayer player)
        {
            // Blackout Pack特有のアイテムをチェック
            return HasSpecialSkins(player, new[] { "metal.plate.torso", "pants.shorts" });
        }

        private bool HasAnyBlueprints(BasePlayer player, string[] itemNames)
        {
            if (player.blueprints == null) return false;

            foreach (var itemName in itemNames)
            {
                var itemDef = ItemManager.FindItemDefinition(itemName);
                if (itemDef != null && player.blueprints.HasUnlocked(itemDef))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasSpecialSkins(BasePlayer player, string[] itemNames)
        {
            if (player.inventory == null) return false;

            // プレイヤーのインベントリ内でスキン付きアイテムをチェック
            var containers = new[] { 
                player.inventory.containerMain, 
                player.inventory.containerBelt, 
                player.inventory.containerWear 
            };

            foreach (var container in containers)
            {
                if (container?.itemList == null) continue;

                foreach (var item in container.itemList)
                {
                    if (item?.skin != 0 && itemNames.Contains(item.info?.shortname))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private BasePlayer FindPlayer(string nameOrId)
        {
            // Steam IDで検索
            if (ulong.TryParse(nameOrId, out ulong steamId))
            {
                return BasePlayer.FindByID(steamId);
            }

            // 名前で検索
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.displayName.ToLower().Contains(nameOrId.ToLower()))
                {
                    return player;
                }
            }

            return null;
        }

        // プラグインロード時の処理
        private void Init()
        {
            Puts("DLC Checker プラグインが読み込まれました。");
        }

        // プラグインアンロード時の処理
        private void Unload()
        {
            Puts("DLC Checker プラグインがアンロードされました。");
        }
    }
}