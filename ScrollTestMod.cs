using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Oxide.Plugins
{
    [Info("ScrollTestMod", "NINJA WORKS", "1.0.0")]
    [Description("Scroll test MOD")]
    class ScrollTestMod : RustPlugin
    {
        #region Plugin References
        [PluginReference]
        private Plugin ImageLibrary;
        #endregion

        #region Constants - Test text content (customizable)
        private const string ScrollTitle = "<b>旅の始まり</b>";
        private const string TEST_TEXT = @"<b><size=18>第1章: 目覚めの砂浜</size></b>

海の波が足元を洗う音で、俺は目を覚ました。頭が重く、視界がぼやけている。周囲は見渡す限りの砂浜と、遠くにそびえる森。空は灰色に曇り、風が冷たく肌を刺す。ここはどこだ？ 記憶が曖昧だ。ゲーム？ いや、これは現実か？ いや、待て……これは「RUST」の世界だ。

俺の名前はアレックス。いや、ゲーム内のハンドルネームだ。裸一貫でスポーンした新参者。インベントリを開くと、何もない。ただの岩と松明だけ。RUSTのルールだ。資源を集め、生き延びろ。他のプレイヤーは敵か味方か、わからない。

足を動かし、砂浜を歩く。まずは石を拾う。木を叩いて木材を集める。基本のクラフト。石斧を作り、木を切り倒す。汗が滴る。仮想現実なのに、疲労感がリアルだ。遠くから銃声が響く。誰かが戦っている。俺はまだ無防備だ。

森に入る。鹿が走るのを追い、弓を作って狩る。肉を焼いて腹を満たす。夜が近づく。基地を建てなければ。石と木で小さな小屋を組む。ドアを付け、鍵をかける。ここが俺の最初のシェルター。



<b><size=18>第2章: 出会いと裏切り</size></b>

翌朝、煙が上がるのを見た。近くの丘に、別の基地。煙突から煙が出ている。クランか？ ソロプレイヤーか？ 接近する。声がする。「おい、誰だそこ！」

男が現れる。ハンドルネームは「Scavenger」。AK-47を構えているが、撃たない。「新入りか？ 資源を分けてやるよ。一緒にやろうぜ。」

俺は警戒しながら頷く。RUSTでは同盟は脆い。だが、一人では厳しい。彼の基地は立派だ。金属の壁、自動ドア。ファーミングを手伝う。ヘンプを収穫し、ファブリケーターで弾薬を作る。夜、焚き火を囲んで話す。「このサーバーは荒れている。クラン『Raiders』が支配してる。奴らはすべてを奪う。」

数日後、俺たちはレイドに行く。小さな基地を襲う。爆弾で壁を破壊し、チェストを漁る。金鉱石、ガンスミスベンチ。興奮する。だが、帰り道で異変。Scavengerが俺の背後で銃を構える。「悪いな、全部俺のものだ。」

銃声。俺は倒れる。リスポーン。すべて失った。RUSTの掟。信頼は愚かだ。



<b><size=18>第3章: 復讐の炎</size></b>

裸で再びスポーン。怒りが燃える。Scavengerの基地を探す。森を駆け、丘を登る。見つけた。強化されている。俺は一人。どうする？

周囲を探索。廃墟のモニュメント。放射能が強いが、資源豊富。軍事クレートからボルトアクションライフルを手に入れる。C4の材料を集める。硫黄を掘り、ファーナスで精錬。

夜を待つ。闇に紛れ、基地に近づく。梯子で壁を登る。警報が鳴る。Scavengerが出てくる。「またお前か！」

戦闘。俺の弾が彼の肩を貫く。彼のショットガンが俺を吹き飛ばす。だが、俺は生き延びる。基地を爆破。チェストを空にする。俺の復讐だ。

だが、喜びは短い。遠くからヘリコプターの音。Raidersのクランだ。俺のレイドが彼らを呼んだ。逃げる。森へ。



<b><size=18>第4章: 連合の夜明け</size></b>

森で出会ったのは、少女のプレイヤー。「Nova」だ。彼女もScavengerに裏切られたと言う。「一緒にRaidersを倒そう。」

俺たちは連合を組む。基地を建て直す。タレットを置き、トラップを仕掛ける。ファーミングを分担。彼女はメディカルを担当、俺はウェポン。

Raidersの襲撃が来る。夜中、爆音。壁が崩れる。戦う。俺のスナイパーでリーダーを狙う。Novaのグレネードが敵を吹き飛ばす。勝利。ルートは豊富。ハイクオリティメタル。

サーバーのバランスが変わる。俺たちは新しいクランを立ち上げる。「Survivors」。他の新参者を迎え、ルールを決める。裏切り禁止。共有の資源。



<b><size=18>終章: 永遠のサバイバル</size></b>

RUSTの世界は終わらない。ワイプが来るたび、リセットされる。だが、学んだ。信頼は築くもの。裏切りは教訓。荒野で生きる術。

俺はNovaと並び、朝日を見る。次なる脅威が来るまで、俺たちは準備する。ここは俺たちの遺産だ。";

        // Text display settings (adjustable)
        private const int MAX_LINE_WIDTH = 90;  // Maximum character width per line (English standard)
        private const int LINE_HEIGHT = 25;     // Line height in pixels
        private const int TEXT_FONT_SIZE = 15;  // Font size
        private const int IMAGE_SIZE = 350;     // Close button image size in pixels
        #endregion

        #region Image URLs
        private const string BACKGROUND_IMAGE_URL = "https://i.imgur.com/RY3DrU6.png";
        private const string CLOSE_BUTTON_IMAGE_URL = "https://i.imgur.com/xLlCqEv.png";
        private const string BACKGROUND_IMAGE_NAME = "scrolltest_background";
        private const string CLOSE_BUTTON_IMAGE_NAME = "scrolltest_closebutton";
        #endregion

        #region Hooks
        void OnServerInitialized()
        {
            if (ImageLibrary == null)
            {
                PrintError("ImageLibrary plugin not found. This plugin is required.");
                return;
            }

            // Register images with ImageLibrary
            LoadImages();
        }

        void Unload()
        {
            // Close UI for all players
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyScrollTestUI(player);
            }
        }
        #endregion

        #region Commands

        [ChatCommand("test")]
        void TestScrollCommand(BasePlayer player, string command, string[] args)
        {
            ShowScrollTestUI(player);
        }

        #endregion

        #region Image Loading
        void LoadImages()
        {
            ImageLibrary?.Call("AddImage", BACKGROUND_IMAGE_URL, BACKGROUND_IMAGE_NAME);
            ImageLibrary?.Call("AddImage", CLOSE_BUTTON_IMAGE_URL, CLOSE_BUTTON_IMAGE_NAME);
            
            Puts("Started loading images.");
        }

        bool IsImageLoaded(string imageName)
        {
            return (bool)(ImageLibrary?.Call("HasImage", imageName, 0UL) ?? false);
        }
        #endregion

        #region Text Processing
        /// <summary>
        /// Determines if a character is Japanese
        /// </summary>
        private bool IsJapaneseChar(char c)
        {
            // Ranges for Hiragana, Katakana, Kanji, and full-width symbols
            return (c >= 0x3040 && c <= 0x309F) ||  // Hiragana
                   (c >= 0x30A0 && c <= 0x30FF) ||  // Katakana
                   (c >= 0x4E00 && c <= 0x9FAF) ||  // Kanji
                   (c >= 0xFF00 && c <= 0xFFEF);    // Full-width symbols and alphanumeric
        }

        /// <summary>
        /// Calculates the display width of a string (Japanese characters count as 2, English characters as 1)
        /// </summary>
        private int CalculateDisplayWidth(string text)
        {
            int width = 0;
            foreach (char c in text)
            {
                if (IsJapaneseChar(c))
                    width += 2; // Japanese characters count as 2
                else
                    width += 1; // English characters count as 1
            }
            return width;
        }

        /// <summary>
        /// Splits a string at the specified display width
        /// </summary>
        private string SubstringByDisplayWidth(string text, int startIndex, int maxDisplayWidth)
        {
            int currentWidth = 0;
            int endIndex = startIndex;
            
            for (int i = startIndex; i < text.Length; i++)
            {
                int charWidth = IsJapaneseChar(text[i]) ? 2 : 1;
                
                if (currentWidth + charWidth > maxDisplayWidth)
                    break;
                    
                currentWidth += charWidth;
                endIndex = i + 1;
            }
            
            return text.Substring(startIndex, endIndex - startIndex);
        }

        private List<string> WrapText(string text, int maxLineWidth = MAX_LINE_WIDTH)
        {
            var lines = new List<string>();
            var paragraphs = text.Split(new string[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            
            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    lines.Add(""); // Preserve empty lines
                    continue;
                }
                
                // Split based on display width
                int currentIndex = 0;
                while (currentIndex < paragraph.Length)
                {
                    string line = SubstringByDisplayWidth(paragraph, currentIndex, maxLineWidth);
                    
                    if (string.IsNullOrEmpty(line))
                    {
                        // If not even one character fits, force advance by one character
                        line = paragraph.Substring(currentIndex, 1);
                        currentIndex += 1;
                    }
                    else
                    {
                        currentIndex += line.Length;
                    }
                    
                    lines.Add(line);
                }
            }
            
            return lines;
        }
        #endregion

        #region UI Creation
        void ShowScrollTestUI(BasePlayer player)
        {
            if (ImageLibrary == null)
            {
                SendReply(player, "ImageLibrary plugin is not available.");
                return;
            }

            // Check if images are loaded
            if (!IsImageLoaded(BACKGROUND_IMAGE_NAME) || !IsImageLoaded(CLOSE_BUTTON_IMAGE_NAME))
            {
                SendReply(player, "Images are not loaded yet. Please wait and try again.");
                // Reload images
                LoadImages();
                return;
            }

            // Remove existing UI
            DestroyScrollTestUI(player);

            var container = new CuiElementContainer();

            // Main panel (full screen overlay)
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.7" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                CursorEnabled = true
            }, "Overlay", "ScrollTestMain");

            // Background image panel - adjusted size for better balance
            var backgroundImageId = (string)ImageLibrary.Call("GetImage", BACKGROUND_IMAGE_NAME);
            container.Add(new CuiElement
            {
                Name = "ScrollTestBackground",
                Parent = "ScrollTestMain",
                Components =
                {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png = backgroundImageId },
                    new CuiRectTransformComponent { AnchorMin = "0.15 0.08", AnchorMax = "0.85 0.92", OffsetMin = "0 0", OffsetMax = "0 0" }
                }
            });

            // Main content area - properly positioned within background image
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.6" },
                RectTransform = { AnchorMin = "0.18 0.12", AnchorMax = "0.82 0.88", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, "ScrollTestMain", "ScrollTestContent");

            // Title area - adjusted height for improved balance
            container.Add(new CuiLabel
            {
                Text = { Text = ScrollTitle, FontSize = 22, Align = TextAnchor.MiddleCenter, Color = "1 0.9 0.7 1" },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-10 0" }
            }, "ScrollTestContent", "ScrollTestTitle");

            // Decorative line below title
            container.Add(new CuiPanel
            {
                Image = { Color = "0.7 0.5 0.2 0.8" },
                RectTransform = { AnchorMin = "0.1 0.91", AnchorMax = "0.9 0.915", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, "ScrollTestContent", "ScrollTestTitleLine");

            // Scrollable area - adjusted spacing from title
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.03 0.08", AnchorMax = "0.97 0.88", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, "ScrollTestContent", "ScrollTestScrollArea");

            // Wrap text before splitting
            var wrappedLines = WrapText(TEST_TEXT);
            var lineHeight = LINE_HEIGHT;
            var padding = 25;
            var imageSize = IMAGE_SIZE; // Use constant
            var totalHeight = wrappedLines.Count * lineHeight + padding * 2 + imageSize + 60;

            // Scroll view
            container.Add(new CuiElement
            {
                Name = "ScrollTestScrollView",
                Parent = "ScrollTestScrollArea",
                Components =
                {
                    new CuiScrollViewComponent
                    {
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Elastic,
                        Vertical = true,
                        Inertia = true,
                        Horizontal = false,
                        Elasticity = 0.1f,
                        DecelerationRate = 0.135f,
                        ScrollSensitivity = 30f,
                        ContentTransform = new CuiRectTransform
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = $"0 -{totalHeight}",
                            OffsetMax = "0 0"
                        },
                        VerticalScrollbar = new CuiScrollbar() { Size = 15f, AutoHide = false }
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                    new CuiNeedsCursorComponent()
                }
            });

            // Scroll content area
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, "ScrollTestScrollView", "ScrollTestScrollContent");

            // Add wrapped text line by line
            for (int i = 0; i < wrappedLines.Count; i++)
            {
                var line = wrappedLines[i];
                var yPos = -(i * lineHeight + padding);
                
                // Reserve space even for empty lines
                var displayText = string.IsNullOrEmpty(line) ? " " : line;
                
                container.Add(new CuiLabel
                {
                    Text = { 
                        Text = displayText, 
                        FontSize = TEXT_FONT_SIZE, 
                        Align = TextAnchor.UpperLeft, 
                        Color = "0.95 0.95 0.9 1" 
                    },
                    RectTransform = { 
                        AnchorMin = "0 1", 
                        AnchorMax = "0.97 1", // Adjust margin for scrollbar
                        OffsetMin = $"20 {yPos - lineHeight}", 
                        OffsetMax = $"-10 {yPos}" 
                    }
                }, "ScrollTestScrollContent", $"ScrollTestLine_{i}");
            }

            // Close button position calculation - placed in a more appropriate position
            var closeButtonImageId = (string)ImageLibrary.Call("GetImage", CLOSE_BUTTON_IMAGE_NAME);
            var closeButtonY = -(wrappedLines.Count * lineHeight + padding + 40);

            // Close button image (non-clickable)
            container.Add(new CuiElement
            {
                Name = "ScrollTestCloseButton",
                Parent = "ScrollTestScrollContent",
                Components =
                {
                    new CuiRawImageComponent { Color = "1 1 1 0.9", Png = closeButtonImageId },
                    new CuiRectTransformComponent { 
                        AnchorMin = "0.5 1", 
                        AnchorMax = "0.5 1", 
                        OffsetMin = $"-{imageSize/2} {closeButtonY - imageSize}", 
                        OffsetMax = $"{imageSize/2} {closeButtonY}" 
                    }
                }
            });

            // Small X button in top right corner for convenience
            container.Add(new CuiButton
            {
                Button = { Color = "0.8 0.2 0.2 0.8", Command = "scrolltest.close" },
                Text = { Text = "×", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.94 0.94", AnchorMax = "0.99 0.99", OffsetMin = "0 0", OffsetMax = "0 0" }
            }, "ScrollTestContent", "ScrollTestQuickClose");

            CuiHelper.AddUi(player, container);

            // Output debug information to log
            Puts($"Improved scroll UI creation complete: lines={wrappedLines.Count}, total height={totalHeight}");
        }

        void DestroyScrollTestUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "ScrollTestMain");
        }
        #endregion

        #region Console Commands
        [ConsoleCommand("scrolltest.close")]
        void CloseScrollTestUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            DestroyScrollTestUI(player);
        }
        #endregion
    }
}