// =================================================================================================
//  DragDropDemo  (with /danime integration)
//  -----------------------------------------------------------------------------------------------
//  Carbon October 2025 Update / Rust.Community PR #55 (Draggables) + PR #69 (RectTransform Rotation)
//  で追加された CUI Draggable / Slot 関連の機能を、ひとつの画面で確認できるデモプラグイン。
//
//  /ddemo            : デモUIを開く / 閉じる (トグル)
//  /danime           : 画像クルクル回転アニメ単体ウィンドウを開く / 閉じる (トグル)
//
//  画像取得は ImageLibrary 非依存。UnityWebRequest で取得 → FileStorage.server.Store で
//  サーバ側に直接格納し、CRC値を CuiRawImageComponent.Png に渡す方式。
//
//  Author : NINJA WORKS
//  License: MIT
// =================================================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.Networking;

namespace Oxide.Plugins
{
    [Info("DragDropDemo", "NINJA WORKS", "1.5.0")]
    [Description("CUI Draggable / Slot / Rotation 機能の総合デモ + 画像回転アニメ /danime")]
    public class DragDropDemo : RustPlugin
    {
        // =============================================================================================
        // 定数
        // =============================================================================================
        private const string Root              = "ddemo.root";       // /ddemo の親
        private const string AnimeRoot         = "danime.root";      // /danime の親
        private const string AnimeImg          = "danime.img";       // /danime の回転画像 (毎フレ作り直し)
        private const string DemoAnimeImg      = "ddemo.anime.img";    // /ddemo の Demo⑥ 内 回転画像 (毎フレ作り直し)
        private const string DemoAnimeHandle   = "ddemo.anime.handle"; // /ddemo の Demo⑥ 内 Draggableハンドル (1回だけ作成)
        private const string DemoAnimeFrameImg = "ddemo.anime.frame";  // ⑥のキャンバス内コンテナ

        private const string CmdReset = "ddemo.reset";
        private const string CmdClose = "ddemo.close";

        // フィルタ識別子
        private const string FilterRed  = "ddemo_red";
        private const string FilterBlue = "ddemo_blue";
        private const string FilterAny  = "ddemo_any";

        // 色プリセット
        private const string ClrBgDark  = "0.05 0.05 0.06 0.96";
        private const string ClrPanel   = "0.12 0.12 0.14 0.92";
        private const string ClrSlot    = "0.22 0.22 0.25 0.85";
        private const string ClrAccent  = "0.30 0.55 0.90 1";
        private const string ClrRed     = "0.85 0.30 0.30 1";
        private const string ClrBlue    = "0.30 0.55 0.90 1";
        private const string ClrGreen   = "0.30 0.80 0.40 1";
        private const string ClrYellow  = "0.95 0.85 0.30 1";
        private const string ClrText    = "0.95 0.95 0.95 1";
        private const string ClrSubText = "0.65 0.65 0.70 1";
        private const string ClrLine    = "1 1 1 0.10";

        // アニメ
        private const string ImageUrl     = "https://i.imgur.com/FM1iLA9.png";
        private const float  TickInterval = 0.03f;   // 20 FPS
        private const float  DegPerSecond = 120f;    // 1秒で半回転

        // ⑥のアイコンサイズ (半径px)。ハンドルと画像両方に適用される。
        // ここを変えるだけで Demo⑥ の回転画像サイズが一括で変わる。
        private const float  DemoAnimeRadius = 35f;
        // 画像をハンドルより内側に少し縮めたい場合のマージン (片側px)。0なら全域。
        private const float  DemoAnimeImgInset = 0f;
        // /danime 単体ウィンドウの画像サイズ (半径px)
        private const float  StandaloneAnimeRadius = 120f;

        // ----- アイテムショートネーム (Rust標準アイテム) -----
        // OnServerInitialized で ItemManager.FindItemDefinition から itemid を解決してキャッシュ
        private const string ShortnameF1Grenade  = "grenade.f1";       // Demo④ 赤系
        private const string ShortnameMedSyringe = "syringe.medical";  // Demo④ 青系
        private const string ShortnameWood       = "wood";             // Demo⑤ A
        private const string ShortnameStones     = "stones";           // Demo⑤ B
        private const string ShortnameMetalFrag  = "metal.fragments";  // Demo⑤ C

        // ピース識別 (Filter / Drop通知に使う)
        // Draggable.Filter は「ddemo:<pieceKey>」、Slot.Filter は「ddemo:<slotGroup>」のように
        // ddemo: プレフィックスで揃えて、ConsoleCommand 側で識別
        private const string CmdDrop = "ddemo.drop"; // ピースがドロップされたときの通知コマンド (PositionRPC利用)

        /// <summary>
        /// ピース要素名 → ショートネーム のマッピング (ログ用に「何が」を解決するため)
        /// </summary>
        private static readonly Dictionary<string, string> PieceToShortname = new()
        {
            { "ddemo4.piece.red",   ShortnameF1Grenade },
            { "ddemo4.piece.blue1", ShortnameMedSyringe },
            { "ddemo5.piece0",      ShortnameWood },
            { "ddemo5.piece1",      ShortnameStones },
            { "ddemo5.piece2",      ShortnameMetalFrag },
        };

        // =============================================================================================
        // 状態
        // =============================================================================================
        private uint _imagePngId  = 0;     // FileStorage CRC. 0 = 未取得
        private bool _downloading = false;

        private enum AnimeMode { Standalone, InsideDemo }

        private class AnimeState
        {
            public Timer     Tick;
            public float     Angle;
            public AnimeMode Mode;
            public float     RotStartTime;
        }

        private readonly Dictionary<ulong, AnimeState> _animeStates = new();
        // /ddemo を開いているプレイヤー (Unload時の確実な破棄に使用)
        private readonly HashSet<ulong> _demoOpenPlayers = new();

        // ショートネーム → ItemId キャッシュ (OnServerInitialized で構築)
        private readonly Dictionary<string, int> _itemIdCache = new();

        // PositionRPC の enum 値をリフレクションで取得した結果
        // null なら通知機能は無効 (失敗時のフォールバック)
        private object _positionRPCValue = null;

        // プレイヤーごとに生成したCUI要素名 (Unload/Close時の確実な破棄に使用)
        // Draggable で reparent されたピースも UiDict 上は名前で残るので名前で消せばOK
        private readonly Dictionary<ulong, HashSet<string>> _createdElements = new();

        // =============================================================================================
        // 永続化レイアウト
        // =============================================================================================
        // データファイル名 (oxide/data/DragDropDemo.json)
        private const string DataFileName = "DragDropDemo";

        /// <summary>
        /// プレイヤー単位の「ピース要素名 → 着地スロット要素名」マッピング。
        /// JSONとして oxide/data/DragDropDemo.json に保存され、UI再オープン/サーバ再起動を跨いで復元される。
        /// </summary>
        private class LayoutData
        {
            // userID(string) → (pieceName → slotName)
            public Dictionary<string, Dictionary<string, string>> Players { get; set; } = new();
        }
        private LayoutData _layout = new();

        /// <summary>初期配置 (保存データが無いプレイヤー用のフォールバック)。</summary>
        private static readonly Dictionary<string, string> DefaultPieceSlots = new()
        {
            { "ddemo4.piece.red",   "ddemo4.slot.red"   },
            { "ddemo4.piece.blue1", "ddemo4.slot.blue1" },
            { "ddemo5.piece0",      "ddemo5.slot0" },
            { "ddemo5.piece1",      "ddemo5.slot1" },
            { "ddemo5.piece2",      "ddemo5.slot2" },
        };

        private void LoadLayout()
        {
            try
            {
                _layout = Interface.Oxide.DataFileSystem.ReadObject<LayoutData>(DataFileName) ?? new LayoutData();
                if (_layout.Players == null) _layout.Players = new();
                Puts($"[DragDropDemo] レイアウト読込: {_layout.Players.Count} 名分");
            }
            catch (Exception ex)
            {
                PrintError($"[DragDropDemo] レイアウト読込失敗: {ex.Message}");
                _layout = new LayoutData();
            }
        }

        private void SaveLayout()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(DataFileName, _layout);
            }
            catch (Exception ex)
            {
                PrintError($"[DragDropDemo] レイアウト保存失敗: {ex.Message}");
            }
        }

        /// <summary>プレイヤーの「ピース → スロット」辞書を取得 (無ければ作る)。</summary>
        private Dictionary<string, string> GetPlayerLayout(ulong userID)
        {
            string key = userID.ToString();
            if (!_layout.Players.TryGetValue(key, out var dict))
            {
                dict = new Dictionary<string, string>(DefaultPieceSlots);
                _layout.Players[key] = dict;
            }
            return dict;
        }

        /// <summary>そのプレイヤーが特定ピースを「今どのスロットに置いているか」を返す。未登録なら初期値。</summary>
        private string GetSavedSlot(BasePlayer player, string pieceName)
        {
            if (player == null) return DefaultPieceSlots.TryGetValue(pieceName, out var d) ? d : null;
            var dict = GetPlayerLayout(player.userID);
            if (dict.TryGetValue(pieceName, out var slot) && !string.IsNullOrEmpty(slot)) return slot;
            return DefaultPieceSlots.TryGetValue(pieceName, out var def) ? def : null;
        }

        /// <summary>ピースの配置を更新して保存。</summary>
        private void SetSavedSlot(BasePlayer player, string pieceName, string slot)
        {
            if (player == null || string.IsNullOrEmpty(pieceName)) return;
            var dict = GetPlayerLayout(player.userID);
            dict[pieceName] = slot ?? "";
            SaveLayout();
        }

        // プレイヤーごとの「現在どのスロットに何が入っているか」の記憶
        // PositionRPCコマンドが届いた時に「変化したか」を判定するため
        // key   = $"{userID}:{slotName}"
        // value = piece の shortname (空 = 何も入っていない)
        private readonly Dictionary<string, string> _slotContents = new();

        // ★調査モード★ true の間、OnServerCommand に来た全コマンドをログ出力
        // /ddemo を開いているプレイヤーがいる間 ON / 全員閉じたら OFF
        private bool _commandSpyMode = false;

        // 観察を絞るため、無視するコマンド名のプレフィックス (頻繁すぎるもの)
        private static readonly string[] SpyIgnorePrefixes =
        {
            "client.",      // Rust標準クライアント同期 (頻発)
            "input.",       // 入力イベント (頻発)
            "rpcq",         // RPC queue (頻発)
            "tick",         // tick系
            "global.tick",
            "physics.",
            "ai.",
            "weather.",
            "demo.",        // demo記録系 (デモ録画と紛らわしい)
            "auth.",
        };

        // =============================================================================================
        // フック
        // =============================================================================================
        private Harmony _harmony;
        private static DragDropDemo _instance;

        private void Init()
        {
            cmd.AddChatCommand("ddemo",  this, nameof(CmdChatDemo));
            cmd.AddChatCommand("danime", this, nameof(CmdChatAnime));

            _instance = this;

            // 永続化レイアウトを読込
            LoadLayout();

            // Hook_DropRPC は private 空メソッドで Oxide/Carbon の自動フックには載らないため
            // Harmony で直接 Postfix パッチを当て、ドロップ通知を取得する
            try
            {
                _harmony = new Harmony("ninjaworks.dragdropdemo");
                var target = typeof(CommunityEntity).GetMethod(
                    "Hook_DropRPC",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (target == null)
                {
                    PrintWarning("[DragDropDemo] Hook_DropRPC が見つからない (古いビルド?)");
                }
                else
                {
                    var postfix = typeof(DragDropDemo).GetMethod(
                        nameof(HarmonyPostfix_DropRPC),
                        BindingFlags.Static | BindingFlags.NonPublic);
                    _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                    Puts("[DragDropDemo] Hook_DropRPC に Harmony パッチを適用しました");
                }

                var dragTarget = typeof(CommunityEntity).GetMethod(
                    "Hook_DragRPC",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (dragTarget != null)
                {
                    var dragPostfix = typeof(DragDropDemo).GetMethod(
                        nameof(HarmonyPostfix_DragRPC),
                        BindingFlags.Static | BindingFlags.NonPublic);
                    _harmony.Patch(dragTarget, postfix: new HarmonyMethod(dragPostfix));
                }
            }
            catch (Exception ex)
            {
                PrintError($"[DragDropDemo] Harmonyパッチ適用失敗: {ex}");
            }
        }

        private static void HarmonyPostfix_DropRPC(BasePlayer player, string draggedName,
            string draggedSlot, string swappedName, string swappedSlot)
        {
            try { _instance?.OnDropRPC(player, draggedName, draggedSlot, swappedName, swappedSlot); }
            catch (Exception ex) { _instance?.PrintError($"[DragDropDemo] DropRPC処理例外: {ex}"); }
        }

        private static void HarmonyPostfix_DragRPC(BasePlayer player, string name,
            Vector3 position, CommunityEntity.DraggablePositionSendType type)
        {
            // 必要なら有効化。デフォは無効 (頻発するため)。
            // _instance?.Puts($"[DragDropDemo] DragRPC <{player?.displayName}> name='{name}' pos={position} type={type}");
        }

        private void OnServerInitialized()
        {
            // ① ショートネーム → itemid 解決 (キャッシュ)
            ResolveItemIds();

            // ② PositionRPC enum 値をリフレクションで取得試行
            //    バージョンによって "Update" / "OnRelease" / "OnDrop" 等の名前が変わるので、
            //    候補を順に試して、見つかったら採用する
            ResolvePositionRPCValue();

            // ③ 起動時に画像を先取りしておく (失敗しても /danime / /ddemo 実行時に再試行)
            DownloadImage();
        }

        /// <summary>
        /// 必要なアイテムショートネームを itemid に解決してキャッシュする。
        /// </summary>
        private void ResolveItemIds()
        {
            string[] shortnames =
            {
                ShortnameF1Grenade, ShortnameMedSyringe,
                ShortnameWood, ShortnameStones, ShortnameMetalFrag
            };

            foreach (var sn in shortnames)
            {
                var def = ItemManager.FindItemDefinition(sn);
                if (def == null)
                {
                    PrintWarning($"[DragDropDemo] ItemDefinition '{sn}' が見つかりません");
                    continue;
                }
                _itemIdCache[sn] = def.itemid;
            }

            Puts($"[DragDropDemo] アイテムID解決: {_itemIdCache.Count}/{shortnames.Length}");
        }

        /// <summary>
        /// CuiDraggableComponent.PositionRPC の enum 型をリフレクションで解決する。
        /// このenumは「ドロップ時に位置をどの座標系で送るか」を指定するもの。
        /// 実環境で利用可能な値: NormalizedScreen / NormalizedParent / Relative / RelativeAnchor
        /// なんでも良いので NormalizedScreen を採用。
        /// </summary>
        private void ResolvePositionRPCValue()
        {
            try
            {
                var draggableType = typeof(CuiDraggableComponent);
                var prop = draggableType.GetProperty("PositionRPC");
                if (prop == null)
                {
                    PrintWarning("[DragDropDemo] CuiDraggableComponent に PositionRPC が無い (古いビルド)");
                    return;
                }

                var enumType = prop.PropertyType;
                if (!enumType.IsEnum)
                {
                    PrintWarning("[DragDropDemo] PositionRPC が enum でない");
                    return;
                }

                // 実際に存在する候補名 (= 座標系) を順に試行
                string[] candidates = { "NormalizedScreen", "NormalizedParent", "Relative", "RelativeAnchor" };
                foreach (var name in candidates)
                {
                    if (Enum.IsDefined(enumType, name))
                    {
                        _positionRPCValue = Enum.Parse(enumType, name);
                        Puts($"[DragDropDemo] PositionRPC enum 値解決: {enumType.Name}.{name}");
                        return;
                    }
                }

                var values = string.Join(", ", Enum.GetNames(enumType));
                PrintWarning($"[DragDropDemo] PositionRPC 候補値が見つかりません。利用可能: {values}");
            }
            catch (Exception ex)
            {
                PrintWarning($"[DragDropDemo] PositionRPC 解決失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// PositionRPC を CuiDraggableComponent にリフレクションで設定 (失敗しても無視)。
        /// </summary>
        private void TrySetPositionRPC(CuiDraggableComponent drag)
        {
            if (_positionRPCValue == null) return;
            try
            {
                var prop = typeof(CuiDraggableComponent).GetProperty("PositionRPC");
                prop?.SetValue(drag, _positionRPCValue);
            }
            catch
            {
                // Failed silently
            }
        }

        /// <summary>
        /// プラグインアンロード時に、UI とタイマーをすべて確実に破棄する。
        ///
        /// 注意: Draggable で動かしたピースは、サーバから見たCUIツリー上の親が
        /// クライアント実装次第で変わることがある。そのため "Root を破壊するだけ"
        /// では取り残しが起きるケースがあるので、既知の要素名を明示的に DestroyUi する。
        /// </summary>
        private void Unload()
        {
            // ① 全アニメタイマーを止める
            foreach (var kv in _animeStates)
            {
                kv.Value?.Tick?.Destroy();
            }
            _animeStates.Clear();

            // ② UIを表示している可能性のある全プレイヤーに対してUIを破壊
            //    activePlayerList と、開閉状態が把握できている userID 群の和集合で破棄を試行
            var targetIds = new HashSet<ulong>(_demoOpenPlayers);
            foreach (var k in _createdElements.Keys) targetIds.Add(k);

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || !p.IsConnected) continue;
                targetIds.Add(p.userID);
            }

            // Unload では NextTick が走らない可能性があるので、各プレイヤーに即時2回送信
            foreach (var uid in targetIds)
            {
                var p = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
                if (p == null || !p.IsConnected) continue;

                var names = new HashSet<string>(AllFixedDemoNames);
                if (_createdElements.TryGetValue(uid, out var s))
                    foreach (var n in s) names.Add(n);

                for (int pass = 0; pass < 2; pass++)
                    foreach (var n in names)
                        try { CuiHelper.DestroyUi(p, n); } catch { }
            }

            _createdElements.Clear();

            // レイアウトを最終保存
            SaveLayout();

            _demoOpenPlayers.Clear();
            _slotContents.Clear();
            _commandSpyMode = false;

            // Harmonyパッチ解除
            try { _harmony?.UnpatchAll("ninjaworks.dragdropdemo"); }
            catch (Exception ex) { PrintError($"[DragDropDemo] Harmony解除失敗: {ex.Message}"); }
            _harmony = null;
            if (_instance == this) _instance = null;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            StopAnime(player);
            ForgetPlayerSlots(player);
            _demoOpenPlayers.Remove(player.userID);

            // 全員いなくなったら調査モードもOFF
            if (_demoOpenPlayers.Count == 0 && _commandSpyMode)
            {
                _commandSpyMode = false;
                Puts("[DragDropDemo][調査] 全員がデモを閉じました。OnServerCommand 観察を停止します。");
            }
        }

        // =============================================================================================
        // /ddemo
        // =============================================================================================
        private void CmdChatDemo(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            // close 引数 or 既に開いている場合は閉じる (トグル)
            bool wantClose = (args.Length > 0 && args[0].ToLower() == "close")
                             || _demoOpenPlayers.Contains(player.userID);
            if (wantClose)
            {
                CloseDemo(player);
                return;
            }

            ShowDemo(player);
        }

        [ConsoleCommand(CmdClose)]
        private void CcClose(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            CloseDemo(p);
        }

        [ConsoleCommand(CmdReset)]
        private void CcReset(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;

            // 永続化レイアウトを初期値に戻して保存 (Demo④⑤)
            string key = p.userID.ToString();
            _layout.Players[key] = new Dictionary<string, string>(DefaultPieceSlots);
            SaveLayout();

            // _slotContents の旧記憶もクリアして初期配置に揃える
            ForgetPlayerSlots(p);

            // CloseDemoは呼ばない (NextTickの遅延破棄が再描画した新UIを消してしまうため)。
            // ShowDemo 自身が冒頭で DestroyUi(Root) → AddUi を行うので
            // ピース位置はすべて初期オフセットで作り直される。
            ShowDemo(p);
            p.ChatMessage("[DragDropDemo] レイアウトをリセットしました (①〜⑤を初期配置に復元)");
        }

        // =============================================================================================
        // ★調査機能①★ OnServerCommand フックで全コマンド観察
        // /ddemo を開いている間、サーバが受信した全コンソールコマンドをログ出力する。
        // ドラッグ中・ドロップ時にどんなコマンド名で何が送られるかを調査するため。
        // =============================================================================================
        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (!_commandSpyMode) return null;
            if (arg?.cmd == null)  return null;

            string fullName = arg.cmd.FullName ?? "";
            if (string.IsNullOrEmpty(fullName)) return null;

            // ノイズ削減: 頻発するシステム系コマンドは無視
            string lower = fullName.ToLowerInvariant();
            for (int i = 0; i < SpyIgnorePrefixes.Length; i++)
            {
                if (lower.StartsWith(SpyIgnorePrefixes[i])) return null;
            }

            string playerName = "?";
            try { playerName = arg.Player()?.displayName ?? "console"; } catch { }
            string args = arg.FullString.ToString() ?? "";
            Puts($"[DragDropDemo][調査] cmd='{fullName}' player='{playerName}' args='{args}'");
            return null; // 元の処理を妨げない
        }

        // =============================================================================================
        // ★ドロップ通知★ CommunityEntity.DropRPC が発火する Oxide/Carbon フック
        //
        // CommunityEntity.UI.Draggable.cs の Hook_DropRPC が空メソッドとして用意されており、
        // Carbon/Oxide はこれを `OnDropRPC` という名前のフックとしてプラグインに配信する。
        // (ConsoleCommandでは届かないので、いくら ddemo.drop / community.drop を待っても無意味)
        //
        // 引数:
        //   player       : ドロップを実行したプレイヤー
        //   draggedName  : ドラッグされたピースの要素名 (例 "ddemo4.piece.red")
        //   draggedSlot  : 着地先スロットの要素名 (スロット外なら null)
        //   swappedName  : スワップで押し出されたピースの要素名 (なければ null)
        //   swappedSlot  : スワップで押し出されたピースの新スロット名 (なければ null)
        // =============================================================================================
        private void OnDropRPC(BasePlayer player, string draggedName, string draggedSlot,
                               string swappedName, string swappedSlot)
        {
            if (player == null) return;

            Puts($"[DragDropDemo] OnDropRPC <{player.displayName}> dragged='{draggedName}' " +
                 $"slot='{draggedSlot}' swappedName='{swappedName}' swappedSlot='{swappedSlot}'");

            if (string.IsNullOrEmpty(draggedName)) return;
            if (!PieceToShortname.TryGetValue(draggedName, out var draggedShort)) return;

            // スロット外にドロップされた場合 (DropAnywhere=true のときだけ起こる)
            if (string.IsNullOrEmpty(draggedSlot))
            {
                Puts($"[DragDropDemo] <{player.displayName}> '{draggedShort}' をスロット外に配置");
                return;
            }

            // スワップ発生時は両方の通知を出す
            if (!string.IsNullOrEmpty(swappedName)
                && PieceToShortname.TryGetValue(swappedName, out var swappedShort))
            {
                Puts($"[DragDropDemo] <{player.displayName}> {draggedSlot} に '{draggedShort}' を入れ、" +
                     $"押し出された '{swappedShort}' は {swappedSlot} へ");
                UpdateSlotMemory(player, draggedName, draggedShort, draggedSlot);
                UpdateSlotMemory(player, swappedName, swappedShort, swappedSlot);

                // 永続化: 両ピースの新しい着地スロットを保存
                SetSavedSlot(player, draggedName, draggedSlot);
                SetSavedSlot(player, swappedName, swappedSlot);
                return;
            }

            // 通常のスロット移動 (空スロットへの移動)
            string oldSlot = FindOldSlot(player, draggedShort);
            if (oldSlot == draggedSlot) return;

            Puts($"[DragDropDemo] <{player.displayName}> '{draggedShort}' を " +
                 $"{oldSlot ?? "?"} → {draggedSlot} へ移動");
            UpdateSlotMemory(player, draggedName, draggedShort, draggedSlot);

            // 永続化: ドラッグしたピースの新しい着地スロットを保存
            SetSavedSlot(player, draggedName, draggedSlot);
        }

        /// <summary>旧スロットを記憶辞書から検索。</summary>
        private string FindOldSlot(BasePlayer player, string shortname)
        {
            string prefix = $"{player.userID}:";
            foreach (var kv in _slotContents)
            {
                if (kv.Key.StartsWith(prefix) && kv.Value == shortname)
                    return kv.Key.Substring(prefix.Length);
            }
            return null;
        }

        /// <summary>スロット記憶を更新 (旧スロットは空に、新スロットに shortname を入れる)。</summary>
        private void UpdateSlotMemory(BasePlayer player, string pieceName, string shortname, string newSlot)
        {
            string oldSlot = FindOldSlot(player, shortname);
            if (oldSlot != null)
                _slotContents[$"{player.userID}:{oldSlot}"] = "";
            if (!string.IsNullOrEmpty(newSlot))
                _slotContents[$"{player.userID}:{newSlot}"] = shortname;
        }

        /// <summary>
        /// ピースがどのスロットに移動したか変化検出して、Putsで通知。
        /// </summary>
        private void DetectSlotChange(BasePlayer player, string pieceName, string shortname, string newSlot)
        {
            // 旧スロットを探す
            string oldSlot = null;
            foreach (var kv in _slotContents)
            {
                if (kv.Key.StartsWith($"{player.userID}:") && kv.Value == shortname)
                {
                    oldSlot = kv.Key.Substring(kv.Key.IndexOf(':') + 1);
                    break;
                }
            }

            if (oldSlot == newSlot) return; // 変化なし

            // スワップ相手 (新スロットに入っていたアイテム) を取得
            string swappedOut = "";
            string newKey = $"{player.userID}:{newSlot}";
            if (_slotContents.TryGetValue(newKey, out var existing) && !string.IsNullOrEmpty(existing))
            {
                swappedOut = existing;
            }

            // 状態更新
            if (oldSlot != null) _slotContents[$"{player.userID}:{oldSlot}"] = swappedOut;
            _slotContents[newKey] = shortname;

            // 通知ログ
            if (!string.IsNullOrEmpty(swappedOut))
            {
                Puts($"[DragDropDemo] <{player.displayName}> {oldSlot ?? "?"} の '{shortname}' と {newSlot} の '{swappedOut}' を入れ替え");
            }
            else
            {
                Puts($"[DragDropDemo] <{player.displayName}> '{shortname}' を {oldSlot ?? "?"} → {newSlot} へ移動");
            }
        }

        /// <summary>
        /// プレイヤー固有のスロット内容を記録する。
        /// </summary>
        private void RememberSlotContent(BasePlayer player, string slot, string shortname)
        {
            if (player == null) return;
            _slotContents[$"{player.userID}:{slot}"] = shortname ?? "";
        }

        /// <summary>
        /// プレイヤーが /ddemo を閉じたときに、そのプレイヤーのスロット記憶をクリア。
        /// </summary>
        private void ForgetPlayerSlots(BasePlayer player)
        {
            if (player == null) return;
            string prefix = $"{player.userID}:";
            var keys = new List<string>();
            foreach (var k in _slotContents.Keys)
            {
                if (k.StartsWith(prefix)) keys.Add(k);
            }
            foreach (var k in keys)
            {
                _slotContents.Remove(k);
            }
        }

        // =============================================================================================
        // /danime  (単体ウィンドウでクルクル回す)
        // =============================================================================================
        private void CmdChatAnime(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            // すでに動いてたら停止 (トグル)
            if (_animeStates.TryGetValue(player.userID, out var existing))
            {
                if (existing.Mode == AnimeMode.Standalone)
                {
                    StopAnime(player);
                    player.ChatMessage("[danime] 停止しました");
                    return;
                }

                // /ddemo 側で動いてるなら止めて単体表示に切り替え
                StopAnime(player);
            }

            // 画像未取得なら今ダウンロード
            if (_imagePngId == 0)
            {
                if (_downloading)
                {
                    player.ChatMessage("[danime] 画像取得中... もう一度コマンドを叩いてください");
                    return;
                }

                player.ChatMessage("[danime] 画像をダウンロード中...");
                DownloadImage(success =>
                {
                    if (success) StartAnimeStandalone(player);
                    else         player.ChatMessage("[danime] 画像のDLに失敗しました");
                });
                return;
            }

            StartAnimeStandalone(player);
        }

        // =============================================================================================
        // 画像ダウンロード (FileStorage 直接格納)
        // =============================================================================================
        private void DownloadImage(Action<bool> onComplete = null)
        {
            if (_imagePngId != 0) { onComplete?.Invoke(true); return; }
            if (_downloading)     { return; }

            _downloading = true;
            ServerMgr.Instance.StartCoroutine(DownloadCoroutine(onComplete));
        }

        private IEnumerator DownloadCoroutine(Action<bool> onComplete)
        {
            using (var req = UnityWebRequest.Get(ImageUrl))
            {
                req.timeout = 30;
                yield return req.SendWebRequest();

                bool ok = false;

#if UNITY_2020_2_OR_NEWER
                bool failed = req.result != UnityWebRequest.Result.Success;
#else
                bool failed = req.isNetworkError || req.isHttpError;
#endif

                if (failed)
                {
                    PrintError($"[DragDropDemo] 画像DL失敗: {req.error} ({ImageUrl})");
                }
                else
                {
                    byte[] bytes = req.downloadHandler?.data;
                    if (bytes == null || bytes.Length == 0)
                    {
                        PrintError("[DragDropDemo] DLしたが空のバイト列");
                    }
                    else
                    {
                        try
                        {
                            uint id = FileStorage.server.Store(
                                bytes,
                                FileStorage.Type.png,
                                CommunityEntity.ServerInstance.net.ID);

                            _imagePngId = id;
                            ok = (id != 0);
                            Puts($"[DragDropDemo] 画像を FileStorage に格納 (id={id}, {bytes.Length} bytes)");
                        }
                        catch (Exception ex)
                        {
                            PrintError($"[DragDropDemo] FileStorage.Store 例外: {ex.Message}");
                        }
                    }
                }

                _downloading = false;
                onComplete?.Invoke(ok);
            }
        }

        // =============================================================================================
        // メイン /ddemo UI 構築
        // =============================================================================================
        private void ShowDemo(BasePlayer player)
        {
            // 既存タイマーを片付ける (⑥のアニメが動いてる可能性)
            StopAnime(player);
            CuiHelper.DestroyUi(player, Root);

            var c = new CuiElementContainer();

            // ------- ルート -------
            c.Add(new CuiElement
            {
                Name      = Root,
                Parent    = "Overlay",
                DestroyUi = Root,
                Components =
                {
                    new CuiImageComponent { Color = ClrBgDark },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                    new CuiNeedsCursorComponent()
                }
            });

            BuildHeader(c);

            // 上段
            BuildDemo1_BasicFreeDrag(c, "0.02 0.55", "0.34 0.92");
            BuildDemo2_MaxDistance  (c, "0.34 0.55", "0.66 0.92");
            BuildDemo3_LimitToParent(c, "0.66 0.55", "0.98 0.92");

            // 下段
            BuildDemo4_SlotFilter   (c, "0.02 0.10", "0.34 0.52", player);
            BuildDemo5_Swap         (c, "0.34 0.10", "0.66 0.52", player);
            BuildDemo6_Rotation     (c, "0.66 0.10", "0.98 0.52");

            BuildFooter(c);

            // 全要素名を抽出して記録 (Unload時の確実破棄に使う)
            TrackCreatedElements(player, c);

            CuiHelper.AddUi(player, c);
            _demoOpenPlayers.Add(player.userID);

            // ★調査モード ON★ /ddemo を開いている間、全コンソールコマンドをログ出力
            _commandSpyMode = true;
            Puts($"[DragDropDemo][調査] {player.displayName} がデモを開きました。OnServerCommand 観察を開始します。");

            // 初期配置を記録 (これと差分でPutsログ通知する)
            RememberSlotContent(player, "ddemo4.slot.red",   ShortnameF1Grenade);
            RememberSlotContent(player, "ddemo4.slot.blue1", ShortnameMedSyringe);
            RememberSlotContent(player, "ddemo4.slot.blue2", "");
            RememberSlotContent(player, "ddemo5.slot0", ShortnameWood);
            RememberSlotContent(player, "ddemo5.slot1", ShortnameStones);
            RememberSlotContent(player, "ddemo5.slot2", ShortnameMetalFrag);
            RememberSlotContent(player, "ddemo5.slot3", "");

            // 画像が用意できていれば、Demo⑥のアニメを起動
            if (_imagePngId != 0)
            {
                StartAnimeInsideDemo(player);
            }
            else if (!_downloading)
            {
                // 起動時のDLが失敗していた場合の再試行
                DownloadImage(success =>
                {
                    if (success && _demoOpenPlayers.Contains(player.userID))
                        StartAnimeInsideDemo(player);
                });
            }
        }

        private void CloseDemo(BasePlayer player)
        {
            if (player == null) return;

            StopAnime(player);
            DestroyAllDemoElements(player);
            // _slotContents は永続化レイアウトと連動するためクリアしない
            _demoOpenPlayers.Remove(player.userID);
            SaveLayout();

            // ★調査モード自動OFF★ 誰も開いていない状態になったらログ出力を止める
            if (_demoOpenPlayers.Count == 0 && _commandSpyMode)
            {
                _commandSpyMode = false;
                Puts("[DragDropDemo][調査] 全員がデモを閉じました。OnServerCommand 観察を停止します。");
            }
        }

        /// <summary>
        /// CUIコンテナ内の全要素の "Name" を抽出して、プレイヤー単位で記録する。
        /// BuildDemoFrame で生成されるGUID名フレームや、各ヘルパで追加した要素も漏らさず拾える。
        /// </summary>
        private void TrackCreatedElements(BasePlayer player, CuiElementContainer container)
        {
            if (player == null || container == null) return;

            if (!_createdElements.TryGetValue(player.userID, out var set))
            {
                set = new HashSet<string>();
                _createdElements[player.userID] = set;
            }

            foreach (var el in container)
            {
                if (!string.IsNullOrEmpty(el.Name))
                    set.Add(el.Name);
            }
        }

        /// <summary>
        /// /ddemo の全UI要素を確実に消す。
        /// Draggable で親が付け替わったピースも UiDict 上は名前で生きているので、
        /// 記録した名前を片っ端から DestroyUi すれば取りこぼしなく消える。
        /// </summary>
        // 既知の固定名 (GUIDフレーム以外の全Demo要素)
        private static readonly string[] AllFixedDemoNames =
        {
            Root,
            "ddemo1.canvas", "ddemo1.box",
            "ddemo2.canvas", "ddemo2.box",
            "ddemo3.canvas", "ddemo3.box",
            "ddemo4.canvas",
            "ddemo4.slot.red", "ddemo4.slot.blue1", "ddemo4.slot.blue2",
            "ddemo4.piece.red", "ddemo4.piece.blue1",
            "ddemo5.canvas",
            "ddemo5.slot0", "ddemo5.slot1", "ddemo5.slot2", "ddemo5.slot3",
            "ddemo5.piece0", "ddemo5.piece1", "ddemo5.piece2",
            DemoAnimeFrameImg, DemoAnimeHandle, DemoAnimeImg,
            AnimeRoot, AnimeImg
        };

        private void DestroyAllDemoElements(BasePlayer player)
        {
            if (player == null) return;

            // 記録済みの動的要素 (フレームGUI含む) と固定名を統合して送信
            var toDestroy = new HashSet<string>(AllFixedDemoNames);
            if (_createdElements.TryGetValue(player.userID, out var set))
            {
                foreach (var n in set)
                    if (!string.IsNullOrEmpty(n)) toDestroy.Add(n);
                _createdElements.Remove(player.userID);
            }

            // 1回目: 即時送信
            foreach (var n in toDestroy)
            {
                try { CuiHelper.DestroyUi(player, n); } catch { }
            }

            // 2回目: 1フレ後に再送 (Draggable reparent 直後の RPC 順序ズレ対策)
            //         この時点で UiDict が確実に更新されており、name lookup が成功する
            var snapshot = new List<string>(toDestroy);
            ulong uid = player.userID;
            NextTick(() =>
            {
                var p = BasePlayer.FindByID(uid);
                if (p == null || !p.IsConnected) return;
                foreach (var n in snapshot)
                {
                    try { CuiHelper.DestroyUi(p, n); } catch { }
                }
            });
        }

        // =============================================================================================
        // ヘッダー / フッター
        // =============================================================================================
        private void BuildHeader(CuiElementContainer c)
        {
            c.Add(new CuiElement
            {
                Parent = Root,
                Components =
                {
                    new CuiImageComponent { Color = ClrPanel },
                    new CuiRectTransformComponent { AnchorMin = "0 0.93", AnchorMax = "1 1" }
                }
            });

            c.Add(new CuiLabel
            {
                Text =
                {
                    Text     = "CUI Draggable & Slot 機能総合デモ",
                    FontSize = 22,
                    Align    = TextAnchor.MiddleLeft,
                    Color    = ClrText
                },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "0.6 1", OffsetMin = "20 0", OffsetMax = "0 0" }
            }, Root);

            c.Add(new CuiLabel
            {
                Text =
                {
                    Text     = "Carbon Oct'25 / Rust.Community PR #55 (Draggable) + #69 (Rotation)",
                    FontSize = 12,
                    Align    = TextAnchor.MiddleLeft,
                    Color    = ClrSubText
                },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "0.6 1", OffsetMin = "20 -25", OffsetMax = "0 -5" }
            }, Root);

            c.Add(new CuiButton
            {
                Button        = { Command = CmdClose, Color = "0.55 0.18 0.18 0.95" },
                RectTransform = { AnchorMin = "1 0.93", AnchorMax = "1 1", OffsetMin = "-110 8", OffsetMax = "-15 -8" },
                Text          = { Text = "閉じる [X]", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ClrText }
            }, Root);

            c.Add(new CuiButton
            {
                Button        = { Command = CmdReset, Color = "0.20 0.40 0.65 0.95" },
                RectTransform = { AnchorMin = "1 0.93", AnchorMax = "1 1", OffsetMin = "-220 8", OffsetMax = "-115 -8" },
                Text          = { Text = "リセット", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ClrText }
            }, Root);
        }

        private void BuildFooter(CuiElementContainer c)
        {
            c.Add(new CuiLabel
            {
                Text =
                {
                    Text     = "ヒント: 各エリアのアイテムをドラッグして挙動を確認してください。⑥は実画像が回転中。",
                    FontSize = 11,
                    Align    = TextAnchor.MiddleCenter,
                    Color    = ClrSubText
                },
                RectTransform = { AnchorMin = "0 0.02", AnchorMax = "1 0.08" }
            }, Root);
        }

        // =============================================================================================
        // 共通: デモエリア外枠
        // =============================================================================================
        private string BuildDemoFrame(CuiElementContainer c, string anchorMin, string anchorMax,
                                      string title, string desc, string accent)
        {
            string id = "ddemo.frame." + CuiHelper.GetGuid();

            c.Add(new CuiElement
            {
                Name   = id,
                Parent = Root,
                Components =
                {
                    new CuiImageComponent { Color = ClrPanel },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin, AnchorMax = anchorMax,
                        OffsetMin = "5 5",     OffsetMax = "-5 -5"
                    },
                    new CuiOutlineComponent { Distance = "1 1", Color = ClrLine }
                }
            });

            c.Add(new CuiElement
            {
                Parent = id,
                Components =
                {
                    new CuiImageComponent { Color = accent },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -28", OffsetMax = "0 0" }
                }
            });

            c.Add(new CuiLabel
            {
                Text          = { Text = title, FontSize = 14, Align = TextAnchor.MiddleLeft, Color = ClrText },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "10 -26", OffsetMax = "-10 -2" }
            }, id);

            c.Add(new CuiLabel
            {
                Text =
                {
                    Text     = desc,
                    FontSize = 10,
                    Align    = TextAnchor.UpperLeft,
                    Color    = ClrSubText,
                    VerticalOverflow = VerticalWrapMode.Overflow
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 8", OffsetMax = "-10 -34" }
            }, id);

            return id;
        }

        // =============================================================================================
        // Demo 1 — 基本: 何の制約もないフリーなドラッグ
        // =============================================================================================
        private void BuildDemo1_BasicFreeDrag(CuiElementContainer c, string aMin, string aMax)
        {
            string frame = BuildDemoFrame(c, aMin, aMax,
                "①  基本ドラッグ (Free Drag)",
                "Draggable のみを付けた最小構成。\n" +
                "DropAnywhere=true なので任意の場所に置ける。",
                ClrAccent);

            string canvas = "ddemo1.canvas";
            c.Add(new CuiElement
            {
                Name   = canvas,
                Parent = frame,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.4" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 30", OffsetMax = "-10 -100" }
                }
            });

            c.Add(new CuiElement
            {
                Name   = "ddemo1.box",
                Parent = canvas,
                Components =
                {
                    new CuiImageComponent { Color = ClrBlue },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "0 0",
                        OffsetMin = "10 10", OffsetMax = "70 70"
                    },
                    new CuiDraggableComponent
                    {
                        DropAnywhere = true,
                        DragAlpha    = 0.7f,
                        // KeepOnTop=true にすると EndDrag 時に Canvas 直下へ取り残されて
                        // Root 破棄時のカスケードで消えなくなるため false にしておく
                        KeepOnTop    = false
                    },
                    new CuiOutlineComponent { Distance = "1 1", Color = "1 1 1 0.4" }
                }
            });
            c.Add(new CuiLabel
            {
                Text = { Text = "Drag", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ClrText },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "ddemo1.box");
        }

        // =============================================================================================
        // Demo 2 — MaxDistance
        // =============================================================================================
        private void BuildDemo2_MaxDistance(CuiElementContainer c, string aMin, string aMax)
        {
            string frame = BuildDemoFrame(c, aMin, aMax,
                "②  MaxDistance",
                "MaxDistance=120 を指定すると、開始位置から半径120px以内\n" +
                "までしか移動できない。点線円が許容範囲。",
                ClrGreen);

            string canvas = "ddemo2.canvas";
            c.Add(new CuiElement
            {
                Name   = canvas,
                Parent = frame,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.4" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 30", OffsetMax = "-10 -100" }
                }
            });

            c.Add(new CuiElement
            {
                Parent = canvas,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color  = "0.3 0.8 0.4 0.18",
                        Sprite = "assets/content/ui/gameui/mlrs/mlrs_dotted_circle.png"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = "-120 -120", OffsetMax = "120 120"
                    }
                }
            });

            c.Add(new CuiElement
            {
                Name   = "ddemo2.box",
                Parent = canvas,
                Components =
                {
                    new CuiImageComponent { Color = ClrGreen, Sprite = "assets/content/ui/gradient-circle.png" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = "-30 -30", OffsetMax = "30 30"
                    },
                    new CuiDraggableComponent
                    {
                        MaxDistance  = 120f,
                        DropAnywhere = true,
                        DragAlpha    = 0.6f,
                        KeepOnTop    = false
                    }
                }
            });
            c.Add(new CuiLabel
            {
                Text = { Text = "120px", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0 0 0 0.85" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "ddemo2.box");
        }

        // =============================================================================================
        // Demo 3 — LimitToParent + ParentPadding
        // =============================================================================================
        private void BuildDemo3_LimitToParent(CuiElementContainer c, string aMin, string aMax)
        {
            string frame = BuildDemoFrame(c, aMin, aMax,
                "③  LimitToParent + ParentPadding",
                "親パネル内に拘束。ParentPadding='10 10 10 10' で、\n" +
                "親の縁から内側10pxまで近寄れない (黄枠 = 拘束領域)。",
                ClrYellow);

            string canvas = "ddemo3.canvas";
            c.Add(new CuiElement
            {
                Name   = canvas,
                Parent = frame,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.4" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 30", OffsetMax = "-10 -100" }
                }
            });

            c.Add(new CuiElement
            {
                Parent = canvas,
                Components =
                {
                    new CuiImageComponent { Color = "0.95 0.85 0.30 0.10" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "10 10", OffsetMax = "-10 -10"
                    },
                    new CuiOutlineComponent { Distance = "1 1", Color = "0.95 0.85 0.30 0.6" }
                }
            });

            c.Add(new CuiElement
            {
                Name   = "ddemo3.box",
                Parent = canvas,
                Components =
                {
                    new CuiImageComponent { Color = ClrYellow },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "0 0",
                        OffsetMin = "20 20", OffsetMax = "75 75"
                    },
                    new CuiDraggableComponent
                    {
                        LimitToParent    = true,
                        ParentPadding    = "10 10 10 10",
                        DropAnywhere     = true,
                        DragAlpha        = 0.7f,
                        KeepOnTop        = false,
                        ParentLimitIndex = 0
                    }
                }
            });
            c.Add(new CuiLabel
            {
                Text = { Text = "Limited", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0 0 0 0.85" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "ddemo3.box");
        }

        // =============================================================================================
        // Demo 4 — Slot + Filter (色マッチング、実アイテムアイコン)
        // =============================================================================================
        private void BuildDemo4_SlotFilter(CuiElementContainer c, string aMin, string aMax, BasePlayer player)
        {
            string frame = BuildDemoFrame(c, aMin, aMax,
                "④  Slot + Filter (アイテム版) [調査モード]",
                "F1グレネード(赤系)は赤スロット、医療シリンジ(青系)は青スロットに。\n" +
                "★調査モード中★ ピースを動かすとサーバコンソールに全コマンドがログ出力。\n" +
                "PositionRPCの実引数を観察してください。",
                ClrRed);

            string canvas = "ddemo4.canvas";
            c.Add(new CuiElement
            {
                Name   = canvas,
                Parent = frame,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.4" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 30", OffsetMax = "-10 -120" }
                }
            });

            CreateSlot(c, canvas, "ddemo4.slot.red",   "0.10 0.55", "0.10 0.55", "0 0", "60 60", ClrRed,  FilterRed,  "RED");
            CreateSlot(c, canvas, "ddemo4.slot.blue1", "0.55 0.55", "0.55 0.55", "0 0", "60 60", ClrBlue, FilterBlue, "BLUE");
            CreateSlot(c, canvas, "ddemo4.slot.blue2", "0.55 0.10", "0.55 0.10", "0 0", "60 60", ClrBlue, FilterBlue, "BLUE");

            // F1グレネード(赤Filter) は赤スロットに、医療シリンジ(青Filter) は青スロットに初期配置
            // 保存されたレイアウトに従ってピースを配置 (初回は DefaultPieceSlots)
            CreateItemPiece(c, "ddemo4.piece.red",
                GetSavedSlot(player, "ddemo4.piece.red"),   ShortnameF1Grenade,  FilterRed);
            CreateItemPiece(c, "ddemo4.piece.blue1",
                GetSavedSlot(player, "ddemo4.piece.blue1"), ShortnameMedSyringe, FilterBlue);
        }

        // =============================================================================================
        // Demo 5 — Swap (実アイテムアイコン: 木材/石/金属)
        // =============================================================================================
        private void BuildDemo5_Swap(CuiElementContainer c, string aMin, string aMax, BasePlayer player)
        {
            string frame = BuildDemoFrame(c, aMin, aMax,
                "⑤  AllowSwapping (アイテム版)",
                "AllowSwapping=true のピースを、他ピースが入っているスロットに\n" +
                "ドロップすると自動で位置を入れ替える。\n" +
                "アイコンは木材/石/金属の3資源 (ItemId 直接描画)。",
                ClrBlue);

            string canvas = "ddemo5.canvas";
            c.Add(new CuiElement
            {
                Name   = canvas,
                Parent = frame,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.4" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 30", OffsetMax = "-10 -120" }
                }
            });

            for (int i = 0; i < 4; i++)
            {
                float xMin = 0.05f + i * 0.23f;
                float xMax = xMin;
                CreateSlot(c, canvas,
                    $"ddemo5.slot{i}",
                    $"{xMin:F2} 0.5", $"{xMax:F2} 0.5",
                    "0 -30", "60 30",
                    "0.4 0.4 0.4 1", FilterAny, $"Slot{i + 1}");
            }

            // Wood / Stones / Metal Fragments — 保存されたスロットへ配置 (初回は slot0/1/2)
            string[] shortnames = { ShortnameWood, ShortnameStones, ShortnameMetalFrag };
            for (int i = 0; i < 3; i++)
            {
                string pieceName = $"ddemo5.piece{i}";
                CreateSwapItemPiece(c, pieceName, GetSavedSlot(player, pieceName), shortnames[i]);
            }
        }

        // =============================================================================================
        // Demo 6 — RectTransform Rotation (PR #69) ★ クルクル回る画像に置き換え
        // =============================================================================================
        private void BuildDemo6_Rotation(CuiElementContainer c, string aMin, string aMax)
        {
            string frame = BuildDemoFrame(c, aMin, aMax,
                "⑥  Rotation + Draggable  (PR #69 + #55)",
                "回転中の画像をドラッグで移動できる。\n" +
                "Draggable ハンドルを親にして、子画像で回転のみ更新する設計。\n" +
                "単体ウィンドウは /danime コマンドへ。",
                ClrGreen);

            // アニメ画像のキャンバス (画像本体は StartAnimeInsideDemo で都度生成)
            string canvas = DemoAnimeFrameImg;
            c.Add(new CuiElement
            {
                Name   = canvas,
                Parent = frame,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.4" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 30", OffsetMax = "-10 -100" }
                }
            });

            // 画像未取得 / DL中 のメッセージ
            if (_imagePngId == 0)
            {
                c.Add(new CuiLabel
                {
                    Text =
                    {
                        Text     = _downloading
                            ? "画像をダウンロード中..."
                            : "画像未取得 (まもなくDL開始)",
                        FontSize = 12,
                        Align    = TextAnchor.MiddleCenter,
                        Color    = ClrSubText
                    },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, canvas);
            }
        }

        // =============================================================================================
        // ヘルパ: スロット生成
        // =============================================================================================
        private void CreateSlot(CuiElementContainer c, string parent, string name,
                                string anchorMin, string anchorMax,
                                string offsetMin, string offsetMax,
                                string accentColor, string filter, string label)
        {
            c.Add(new CuiElement
            {
                Name   = name,
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = ClrSlot },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin, AnchorMax = anchorMax,
                        OffsetMin = offsetMin, OffsetMax = offsetMax
                    },
                    new CuiOutlineComponent { Distance = "2 2", Color = accentColor },
                    new CuiSlotComponent { Filter = filter }
                }
            });

            c.Add(new CuiLabel
            {
                Text          = { Text = label, FontSize = 10, Align = TextAnchor.LowerCenter, Color = "1 1 1 0.4" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 -14", OffsetMax = "0 0" }
            }, name);
        }

        /// <summary>
        /// Demo④用: 実アイテムアイコンのDraggableピース。
        /// CuiImageComponent.ItemId を指定すると、クライアントが直接アイコンを描画する。
        /// </summary>
        private void CreateItemPiece(CuiElementContainer c, string name, string parentSlot,
                                     string shortname, string filter)
        {
            int itemId;
            if (!_itemIdCache.TryGetValue(shortname, out itemId))
            {
                PrintWarning($"[DragDropDemo] アイテム '{shortname}' のIDが未解決");
                return;
            }

            var drag = new CuiDraggableComponent
            {
                Filter        = filter,
                DropAnywhere  = false,
                AllowSwapping = true,
                DragAlpha     = 0.55f,
                KeepOnTop     = false,
                MoveToAnchor  = true,
                RebuildAnchor = true,
                AnchorOffset  = "0 0"
            };
            TrySetPositionRPC(drag);

            c.Add(new CuiElement
            {
                Name   = name,
                Parent = parentSlot,
                Components =
                {
                    new CuiImageComponent
                    {
                        ItemId = itemId,
                        Color  = "1 1 1 1"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "5 5",  OffsetMax = "-5 -5"
                    },
                    drag
                }
            });
        }

        /// <summary>
        /// Demo⑤用: スワップ用の実アイテムアイコンピース。
        /// </summary>
        private void CreateSwapItemPiece(CuiElementContainer c, string name, string parentSlot, string shortname)
        {
            int itemId;
            if (!_itemIdCache.TryGetValue(shortname, out itemId))
            {
                PrintWarning($"[DragDropDemo] アイテム '{shortname}' のIDが未解決");
                return;
            }

            // keepOnTop=true にすると EndDrag 時に realParent への SetParent が抑止され、
            // スワップ後にピースが旧スロット階層に取り残されて消えたように見えるバグを誘発する。
            // ここでは false にしてスワップ後きちんと新スロットに親付け替えされるようにする。
            var drag = new CuiDraggableComponent
            {
                Filter        = FilterAny,
                DropAnywhere  = false,
                AllowSwapping = true,
                DragAlpha     = 0.55f,
                KeepOnTop     = false,
                MoveToAnchor  = true,
                RebuildAnchor = true
            };
            TrySetPositionRPC(drag);

            c.Add(new CuiElement
            {
                Name   = name,
                Parent = parentSlot,
                Components =
                {
                    new CuiImageComponent
                    {
                        ItemId = itemId,
                        Color  = "1 1 1 1"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "4 4", OffsetMax = "-4 -4"
                    },
                    drag
                }
            });
        }

        // =============================================================================================
        // /danime 単体ウィンドウ起動
        // =============================================================================================
        private void StartAnimeStandalone(BasePlayer player)
        {
            if (player == null) return;

            CuiHelper.DestroyUi(player, AnimeRoot);

            // 土台
            var c = new CuiElementContainer();
            c.Add(new CuiElement
            {
                Name      = AnimeRoot,
                Parent    = "Overlay",
                DestroyUi = AnimeRoot,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.65" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = "-180 -180", OffsetMax = "180 200"
                    }
                }
            });
            c.Add(new CuiLabel
            {
                Text =
                {
                    Text = "RectTransform.Rotation Demo",
                    FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.9"
                },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -28", OffsetMax = "0 -4" }
            }, AnimeRoot);
            c.Add(new CuiLabel
            {
                Text =
                {
                    Text = "もう一度 /danime で停止",
                    FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.7 0.7 0.7 0.9"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 6", OffsetMax = "0 24" }
            }, AnimeRoot);
            CuiHelper.AddUi(player, c);

            // タイマー始動
            var state = new AnimeState { Angle = 0f, Mode = AnimeMode.Standalone, RotStartTime = Time.realtimeSinceStartup };
            state.Tick = timer.Repeat(TickInterval, 0, () =>
            {
                if (player == null || !player.IsConnected)
                {
                    StopAnime(player);
                    return;
                }
                state.Angle = (DegPerSecond * (Time.realtimeSinceStartup - state.RotStartTime)) % 360f;
                UpdateAnimeImage(player, state.Mode, state.Angle);
            });
            _animeStates[player.userID] = state;
        }

        // =============================================================================================
        // /ddemo の Demo⑥ 内アニメ起動
        //
        // 設計:
        //   DragHandle (透明・Draggable, 1回だけ作成)
        //     └ RotatingImage (毎フレ destroy/add で Rotation を更新)
        //
        // ハンドル側で位置を持つので、毎フレーム作り直す画像はハンドルの中で常に (0,0)
        // → ユーザーがドラッグした位置はクライアント側で保持され、回転だけが更新される
        // =============================================================================================
        private void StartAnimeInsideDemo(BasePlayer player)
        {
            if (player == null || _imagePngId == 0) return;

            // 既存タイマー破棄
            if (_animeStates.TryGetValue(player.userID, out var old))
                old.Tick?.Destroy();

            // ★ Draggableハンドル を1回だけ作成 (キャンバス中央スタート、画像と同サイズ)
            CuiHelper.DestroyUi(player, DemoAnimeHandle);
            CuiHelper.DestroyUi(player, DemoAnimeImg);

            var c = new CuiElementContainer();
            c.Add(new CuiElement
            {
                Name   = DemoAnimeHandle,
                Parent = DemoAnimeFrameImg,
                Components =
                {
                    // 当たり判定確保のため極微透明な画像を敷く (完全透明だとクリックが拾われない実装もある)
                    new CuiImageComponent { Color = "1 1 1 0.01" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = $"{-DemoAnimeRadius} {-DemoAnimeRadius}",
                        OffsetMax = $"{DemoAnimeRadius} {DemoAnimeRadius}"
                    },
                    new CuiDraggableComponent
                    {
                        DropAnywhere  = true,    // 任意の場所に置ける
                        LimitToParent = true,    // ⑥のフレーム内に拘束
                        DragAlpha     = 0.7f,
                        KeepOnTop     = true
                    }
                }
            });
            CuiHelper.AddUi(player, c);

            var state = new AnimeState { Angle = 0f, Mode = AnimeMode.InsideDemo, RotStartTime = Time.realtimeSinceStartup };
            state.Tick = timer.Repeat(TickInterval, 0, () =>
            {
                if (player == null || !player.IsConnected || !_demoOpenPlayers.Contains(player.userID))
                {
                    StopAnime(player);
                    return;
                }
                state.Angle = (DegPerSecond * (Time.realtimeSinceStartup - state.RotStartTime)) % 360f;
                UpdateAnimeImage(player, state.Mode, state.Angle);
            });
            _animeStates[player.userID] = state;
        }

        // =============================================================================================
        // 回転画像の毎フレーム更新 (destroy → add)
        // =============================================================================================
        private void UpdateAnimeImage(BasePlayer player, AnimeMode mode, float angle)
        {
            if (player == null || _imagePngId == 0) return;

            // Standalone モード: AnimeRoot 直下に -120..120 で配置
            // InsideDemo モード:  DemoAnimeHandle (Draggable) の中で 0..1 全域に貼る
            //                    → ハンドルが動けば一緒に動く＆回転だけが毎フレ更新される
            string parent  = (mode == AnimeMode.Standalone) ? AnimeRoot : DemoAnimeHandle;
            string imgName = (mode == AnimeMode.Standalone) ? AnimeImg  : DemoAnimeImg;

            string anchorMin, anchorMax, offMin, offMax;
            if (mode == AnimeMode.Standalone)
            {
                anchorMin = "0.5 0.5";
                anchorMax = "0.5 0.5";
                offMin    = $"{-StandaloneAnimeRadius} {-StandaloneAnimeRadius}";
                offMax    = $"{StandaloneAnimeRadius} {StandaloneAnimeRadius}";
            }
            else
            {
                // ハンドル全域に貼り付ける (位置はハンドルが持つ)
                // DemoAnimeImgInset > 0 ならハンドルより内側にマージンを取って描画
                anchorMin = "0 0";
                anchorMax = "1 1";
                offMin    = $"{DemoAnimeImgInset} {DemoAnimeImgInset}";
                offMax    = $"{-DemoAnimeImgInset} {-DemoAnimeImgInset}";
            }

            CuiHelper.DestroyUi(player, imgName);

            var c = new CuiElementContainer();
            c.Add(new CuiElement
            {
                Name   = imgName,
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png   = _imagePngId.ToString(),
                        Color = "1 1 1 1"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offMin,
                        OffsetMax = offMax,
                        Rotation  = angle
                    }
                }
            });
            CuiHelper.AddUi(player, c);
        }

        // =============================================================================================
        // アニメ停止 (タイマー + UI)
        // =============================================================================================
        private void StopAnime(BasePlayer player)
        {
            if (player == null) return;

            if (_animeStates.TryGetValue(player.userID, out var st))
            {
                st.Tick?.Destroy();
                _animeStates.Remove(player.userID);
            }

            // 単体ウィンドウは丸ごと消す。InsideDemo はハンドル + 画像を消す (親⑥フレームは残す)
            CuiHelper.DestroyUi(player, AnimeRoot);
            CuiHelper.DestroyUi(player, AnimeImg);
            CuiHelper.DestroyUi(player, DemoAnimeImg);
            CuiHelper.DestroyUi(player, DemoAnimeHandle);
        }
    }
}