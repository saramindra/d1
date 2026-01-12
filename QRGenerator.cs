using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("QRGenerator", "NINJA WORKS", "1.0.0")]
    [Description("Displays URL with QR code using /qr command")]
    public class QRGenerator : RustPlugin
    {
        #region Constants

        private const string UI_OVERLAY = "QRGenerator_Overlay";
        private const string UI_PANEL = "QRGenerator_Panel";
        private const string UI_QR = "QRGenerator_QR";

        private const string COMMAND_NAME = "qr";

        private const string COLOR_OVERLAY = "0 0 0 0.85";
        private const string COLOR_BACKGROUND = "0.1 0.1 0.1 0.95";
        private const string COLOR_TITLE = "1 1 1 1";
        private const string COLOR_URL = "0.3 0.8 1 1";
        private const string COLOR_HINT = "0.6 0.6 0.6 1";
        private const string COLOR_BUTTON = "0.6 0.2 0.2 1";
        private const string COLOR_QR_MODULE = "0 0 0 1";
        private const string COLOR_QR_BACKGROUND = "1 1 1 1";

        private const float QR_HEIGHT = 0.45f;
        private const float QR_ASPECT_RATIO = 1f;
        private const float QR_BOTTOM = 0.28f;

        private const string MSG_USAGE = "<color=#ff6666>Usage:</color> /qr <URL>";
        private const string MSG_INVALID = "<color=#ff6666>Error:</color> Please enter a valid URL (http:// or https://)";
        private const string MSG_TITLE = "Open URL with QR Code";
        private const string MSG_INSTRUCTION = "Scan the QR code with your smartphone";
        private const string MSG_HINT = "* To copy the URL, press F1 to open the console";
        private const string MSG_CLOSE = "Close";

        #endregion

        #region Hooks

        private void Init()
        {
            cmd.AddChatCommand(COMMAND_NAME, this, nameof(CmdTestURL));
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(player, UI_OVERLAY);
        }

        #endregion

        #region Commands

        private void CmdTestURL(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                player.ChatMessage(MSG_USAGE);
                return;
            }

            string url = args[0];

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                player.ChatMessage(MSG_INVALID);
                return;
            }

            ShowUI(player, url);
        }

        [ConsoleCommand("qr.close")]
        private void CmdClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null)
                CuiHelper.DestroyUi(player, UI_OVERLAY);
        }

        #endregion

        #region UI

        private void ShowUI(BasePlayer player, string url)
        {
            CuiHelper.DestroyUi(player, UI_OVERLAY);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = COLOR_OVERLAY },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UI_OVERLAY);

            container.Add(new CuiPanel
            {
                Image = { Color = COLOR_BACKGROUND },
                RectTransform = { AnchorMin = "0.3 0.15", AnchorMax = "0.7 0.85" }
            }, UI_OVERLAY, UI_PANEL);

            container.Add(new CuiLabel
            {
                Text = { Text = MSG_TITLE, FontSize = 20, Align = TextAnchor.MiddleCenter, Color = COLOR_TITLE },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 0.98" }
            }, UI_PANEL);

            container.Add(new CuiLabel
            {
                Text = { Text = MSG_INSTRUCTION, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = COLOR_HINT },
                RectTransform = { AnchorMin = "0 0.83", AnchorMax = "1 0.9" }
            }, UI_PANEL);

            float qrHeight = QR_HEIGHT;
            float qrWidth = qrHeight * QR_ASPECT_RATIO;
            float qrLeft = 0.5f - (qrWidth / 2f);
            float qrRight = 0.5f + (qrWidth / 2f);
            float qrBottom = QR_BOTTOM;
            float qrTop = qrBottom + qrHeight;

            container.Add(new CuiPanel
            {
                Image = { Color = COLOR_QR_BACKGROUND },
                RectTransform = { AnchorMin = $"{qrLeft} {qrBottom}", AnchorMax = $"{qrRight} {qrTop}" }
            }, UI_PANEL, UI_QR);

            var qrMatrix = QRCodeGen.Generate(url);
            DrawQR(container, qrMatrix);

            container.Add(new CuiLabel
            {
                Text = { Text = url, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = COLOR_URL },
                RectTransform = { AnchorMin = "0.02 0.18", AnchorMax = "0.98 0.26" }
            }, UI_PANEL);

            container.Add(new CuiLabel
            {
                Text = { Text = MSG_HINT, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = COLOR_HINT },
                RectTransform = { AnchorMin = "0 0.1", AnchorMax = "1 0.17" }
            }, UI_PANEL);

            container.Add(new CuiButton
            {
                Button = { Command = "qr.close", Color = COLOR_BUTTON },
                RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.65 0.09" },
                Text = { Text = MSG_CLOSE, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_PANEL);

            CuiHelper.AddUi(player, container);
        }

        private void DrawQR(CuiElementContainer container, bool[,] matrix)
        {
            int size = matrix.GetLength(0);
            float moduleSize = 1f / (size + 2);
            float margin = moduleSize;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (matrix[x, y])
                    {
                        float xMin = margin + (x * moduleSize);
                        float yMin = margin + ((size - 1 - y) * moduleSize);

                        container.Add(new CuiPanel
                        {
                            Image = { Color = COLOR_QR_MODULE },
                            RectTransform = {
                                AnchorMin = $"{xMin} {yMin}",
                                AnchorMax = $"{xMin + moduleSize} {yMin + moduleSize}"
                            }
                        }, UI_QR);
                    }
                }
            }
        }

        #endregion

        #region QR Code Generator

        private static class QRCodeGen
        {
            private static readonly int[] EXP = new int[256];
            private static readonly int[] LOG = new int[256];

            static QRCodeGen()
            {
                int v = 1;
                for (int i = 0; i < 256; i++)
                {
                    EXP[i] = v;
                    LOG[v] = i;
                    v <<= 1;
                    if (v >= 256) v ^= 0x11D;
                }
            }

            public static bool[,] Generate(string text)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                int ver = GetVersion(bytes.Length);
                byte[] data = Encode(text);
                int size = 17 + ver * 4;

                var matrix = new bool[size, size];
                var reserved = new bool[size, size];

                AddFinders(matrix, reserved, size);
                AddTiming(matrix, reserved, size);
                
                int[] alignPos = GetAlignPositions(ver);
                if (alignPos.Length > 0)
                    AddAlignments(matrix, reserved, alignPos, size);
                
                if (ver >= 7)
                    ReserveVersionArea(reserved, size);
                
                ReserveFormat(reserved, size);
                PlaceData(matrix, reserved, data, size);
                ApplyMask(matrix, reserved, size);
                AddFormat(matrix, size);
                
                if (ver >= 7)
                    AddVersionInfo(matrix, ver, size);

                return matrix;
            }

            private static byte[] Encode(string text)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                int ver = GetVersion(bytes.Length);
                int charCountBits = ver <= 9 ? 8 : 16;
                
                var bits = new List<bool>();

                AddBits(bits, 4, 4);
                AddBits(bits, bytes.Length, charCountBits);
                foreach (byte b in bytes) AddBits(bits, b, 8);
                int cap = GetCapacity(ver);
                int remaining = cap * 8 - bits.Count;
                AddBits(bits, 0, Math.Min(4, remaining));
                
                while (bits.Count % 8 != 0) bits.Add(false);

                bool tog = true;
                while (bits.Count < cap * 8)
                {
                    AddBits(bits, tog ? 236 : 17, 8);
                    tog = !tog;
                }

                byte[] result = new byte[bits.Count / 8];
                for (int i = 0; i < result.Length; i++)
                    for (int j = 0; j < 8; j++)
                        if (bits[i * 8 + j]) result[i] |= (byte)(128 >> j);

                return AddEC(result, ver);
            }

            private static void AddBits(List<bool> bits, int val, int cnt)
            {
                for (int i = cnt - 1; i >= 0; i--)
                    bits.Add(((val >> i) & 1) == 1);
            }

            private static int GetVersion(int dataLen)
            {
                if (dataLen <= 17) return 1;
                if (dataLen <= 32) return 2;
                if (dataLen <= 53) return 3;
                if (dataLen <= 78) return 4;
                if (dataLen <= 106) return 5;
                if (dataLen <= 134) return 6;
                if (dataLen <= 154) return 7;
                if (dataLen <= 192) return 8;
                if (dataLen <= 230) return 9;
                if (dataLen <= 271) return 10;
                if (dataLen <= 321) return 11;
                if (dataLen <= 367) return 12;
                if (dataLen <= 425) return 13;
                if (dataLen <= 458) return 14;
                if (dataLen <= 520) return 15;
                if (dataLen <= 586) return 16;
                if (dataLen <= 644) return 17;
                if (dataLen <= 718) return 18;
                if (dataLen <= 792) return 19;
                if (dataLen <= 858) return 20;
                return 20;
            }

            private static int GetCapacity(int ver)
            {
                int[] caps = { 0, 19, 34, 55, 80, 108, 136, 156, 194, 232, 274, 324, 370, 428, 461, 523, 589, 647, 721, 795, 861 };
                return ver < caps.Length ? caps[ver] : caps[caps.Length - 1];
            }

            private static int[] GetAlignPositions(int ver)
            {
                if (ver == 1) return new int[0];
                
                int[][] positions = {
                    null,
                    new int[0],
                    new int[] { 6, 18 },
                    new int[] { 6, 22 },
                    new int[] { 6, 26 },
                    new int[] { 6, 30 },
                    new int[] { 6, 34 },
                    new int[] { 6, 22, 38 },
                    new int[] { 6, 24, 42 },
                    new int[] { 6, 26, 46 },
                    new int[] { 6, 28, 50 },
                    new int[] { 6, 30, 54 },
                    new int[] { 6, 32, 58 },
                    new int[] { 6, 34, 62 },
                    new int[] { 6, 26, 46, 66 },
                    new int[] { 6, 26, 48, 70 },
                    new int[] { 6, 26, 50, 74 },
                    new int[] { 6, 30, 54, 78 },
                    new int[] { 6, 30, 56, 82 },
                    new int[] { 6, 30, 58, 86 },
                    new int[] { 6, 34, 62, 90 },
                };
                
                return ver < positions.Length ? positions[ver] : positions[positions.Length - 1];
            }

            private static byte[] AddEC(byte[] data, int ver)
            {
                int cap = GetCapacity(ver);
                byte[] padded = new byte[cap];
                Array.Copy(data, padded, Math.Min(data.Length, cap));

                int[] blockInfo = GetBlockInfo(ver);
                int numBlocks1 = blockInfo[0];
                int dataPerBlock1 = blockInfo[1];
                int numBlocks2 = blockInfo[2];
                int dataPerBlock2 = blockInfo[3];
                int ecPerBlock = blockInfo[4];

                var dataBlocks = new List<byte[]>();
                var ecBlocks = new List<byte[]>();
                int offset = 0;

                for (int i = 0; i < numBlocks1; i++)
                {
                    byte[] block = new byte[dataPerBlock1];
                    Array.Copy(padded, offset, block, 0, dataPerBlock1);
                    offset += dataPerBlock1;
                    dataBlocks.Add(block);
                    ecBlocks.Add(CalculateEC(block, ecPerBlock));
                }

                for (int i = 0; i < numBlocks2; i++)
                {
                    byte[] block = new byte[dataPerBlock2];
                    Array.Copy(padded, offset, block, 0, dataPerBlock2);
                    offset += dataPerBlock2;
                    dataBlocks.Add(block);
                    ecBlocks.Add(CalculateEC(block, ecPerBlock));
                }

                var result = new List<byte>();
                int maxDataLen = Math.Max(dataPerBlock1, dataPerBlock2);
                
                for (int i = 0; i < maxDataLen; i++)
                {
                    foreach (var block in dataBlocks)
                    {
                        if (i < block.Length)
                            result.Add(block[i]);
                    }
                }

                for (int i = 0; i < ecPerBlock; i++)
                {
                    foreach (var block in ecBlocks)
                    {
                        result.Add(block[i]);
                    }
                }

                return result.ToArray();
            }

            private static int[] GetBlockInfo(int ver)
            {
                int[][] info = {
                    null,
                    new[] { 1, 19, 0, 0, 7 },
                    new[] { 1, 34, 0, 0, 10 },
                    new[] { 1, 55, 0, 0, 15 },
                    new[] { 1, 80, 0, 0, 20 },
                    new[] { 1, 108, 0, 0, 26 },
                    new[] { 2, 68, 0, 0, 18 },
                    new[] { 2, 78, 0, 0, 20 },
                    new[] { 2, 97, 0, 0, 24 },
                    new[] { 2, 116, 0, 0, 30 },
                    new[] { 2, 68, 2, 69, 18 },
                    new[] { 4, 81, 0, 0, 20 },
                    new[] { 2, 92, 2, 93, 24 },
                    new[] { 4, 107, 0, 0, 26 },
                    new[] { 3, 115, 1, 116, 30 },
                    new[] { 5, 87, 1, 88, 22 },
                    new[] { 5, 98, 1, 99, 24 },
                    new[] { 1, 107, 5, 108, 28 },
                    new[] { 5, 120, 1, 121, 30 },
                    new[] { 3, 113, 4, 114, 28 },
                    new[] { 3, 107, 5, 108, 28 },
                };
                return ver < info.Length ? info[ver] : info[info.Length - 1];
            }

            private static byte[] CalculateEC(byte[] data, int ecCount)
            {
                int[] gen = GetGenerator(ecCount);
                int[] msg = new int[data.Length + ecCount];
                for (int i = 0; i < data.Length; i++) msg[i] = data[i];

                for (int i = 0; i < data.Length; i++)
                {
                    int c = msg[i];
                    if (c != 0)
                        for (int j = 0; j < gen.Length; j++)
                            msg[i + j] ^= Mul(gen[j], c);
                }

                byte[] result = new byte[ecCount];
                for (int i = 0; i < ecCount; i++)
                    result[i] = (byte)msg[data.Length + i];

                return result;
            }

            private static int[] GetGenerator(int deg)
            {
                int[] r = { 1 };
                for (int i = 0; i < deg; i++)
                {
                    int[] t = new int[r.Length + 1];
                    for (int j = 0; j < r.Length; j++)
                    {
                        t[j] ^= r[j];
                        t[j + 1] ^= Mul(r[j], EXP[i]);
                    }
                    r = t;
                }
                return r;
            }

            private static int Mul(int a, int b)
            {
                if (a == 0 || b == 0) return 0;
                return EXP[(LOG[a] + LOG[b]) % 255];
            }

            private static void AddFinders(bool[,] m, bool[,] r, int s)
            {
                int[] offsets = { 0, s - 7 };
                foreach (int ox in new[] { 0, s - 7 })
                {
                    foreach (int oy in new[] { 0, s - 7 })
                    {
                        if (ox != 0 && oy != 0) continue;
                        for (int dy = -1; dy <= 7; dy++)
                        {
                            for (int dx = -1; dx <= 7; dx++)
                            {
                                int x = ox + dx, y = oy + dy;
                                if (x < 0 || y < 0 || x >= s || y >= s) continue;

                                bool blk = dx >= 0 && dx <= 6 && dy >= 0 && dy <= 6 &&
                                    (dx == 0 || dx == 6 || dy == 0 || dy == 6 ||
                                    (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4));

                                m[x, y] = blk;
                                r[x, y] = true;
                            }
                        }
                    }
                }
            }

            private static void AddTiming(bool[,] m, bool[,] r, int s)
            {
                for (int i = 8; i < s - 8; i++)
                {
                    bool blk = i % 2 == 0;
                    m[i, 6] = blk; m[6, i] = blk;
                    r[i, 6] = true; r[6, i] = true;
                }
            }

            private static void AddAlignments(bool[,] m, bool[,] r, int[] positions, int size)
            {
                if (positions.Length == 0) return;
                
                foreach (int cy in positions)
                {
                    foreach (int cx in positions)
                    {
                        if ((cx <= 8 && cy <= 8) ||
                            (cx <= 8 && cy >= size - 9) ||
                            (cx >= size - 9 && cy <= 8))
                            continue;
                        
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int x = cx + dx;
                                int y = cy + dy;
                                if (x < 0 || y < 0 || x >= size || y >= size) continue;
                                
                                bool blk = dx == -2 || dx == 2 || dy == -2 || dy == 2 || (dx == 0 && dy == 0);
                                m[x, y] = blk;
                                r[x, y] = true;
                            }
                        }
                    }
                }
            }

            private static void ReserveFormat(bool[,] r, int s)
            {
                for (int i = 0; i < 9; i++) { r[i, 8] = true; r[8, i] = true; }
                for (int i = 0; i < 8; i++) { r[s - 1 - i, 8] = true; r[8, s - 1 - i] = true; }
                r[8, s - 8] = true;
            }

            private static void ReserveVersionArea(bool[,] r, int s)
            {
                for (int i = 0; i < 6; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        r[s - 11 + j, i] = true;
                        r[i, s - 11 + j] = true;
                    }
                }
            }

            private static void AddVersionInfo(bool[,] m, int ver, int s)
            {
                int[] versionBits = {
                    0, 0, 0, 0, 0, 0, 0,
                    0x07C94, 0x085BC, 0x09A99, 0x0A4D3, 0x0BBF6, 0x0C762, 0x0D847, 0x0E60D,
                    0x0F928, 0x10B78, 0x1145D, 0x12A17, 0x13532, 0x149A6
                };
                
                if (ver < 7 || ver >= versionBits.Length) return;
                
                int bits = versionBits[ver];
                
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) == 1;
                    int row = i / 3;
                    int col = i % 3;
                    
                    m[s - 11 + col, row] = bit;
                    m[row, s - 11 + col] = bit;
                }
            }

            private static void PlaceData(bool[,] m, bool[,] r, byte[] d, int s)
            {
                int bi = 0, total = d.Length * 8;
                bool upward = true;
                
                for (int col = s - 1; col >= 1; col -= 2)
                {
                    if (col == 6) col = 5;
                    
                    for (int i = 0; i < s; i++)
                    {
                        int row = upward ? (s - 1 - i) : i;
                        
                        for (int c = 0; c < 2; c++)
                        {
                            int x = col - c;
                            int y = row;
                            
                            if (!r[x, y] && bi < total)
                            {
                                m[x, y] = ((d[bi / 8] >> (7 - bi % 8)) & 1) == 1;
                                bi++;
                            }
                        }
                    }
                    upward = !upward;
                }
            }

            private static void ApplyMask(bool[,] m, bool[,] r, int s)
            {
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                        if (!r[x, y] && (x + y) % 2 == 0)
                            m[x, y] = !m[x, y];
            }

            private static void AddFormat(bool[,] m, int s)
            {
                int fmt = 0x77C4;
                int[] hp = { 0, 1, 2, 3, 4, 5, 7, 8 };
                int[] vp = { 8, 7, 5, 4, 3, 2, 1, 0 };

                for (int i = 0; i < 8; i++)
                {
                    bool b = ((fmt >> (14 - i)) & 1) == 1;
                    m[hp[i], 8] = b;
                    m[8, s - 1 - i] = b;
                }
                for (int i = 0; i < 7; i++)
                {
                    bool b = ((fmt >> (6 - i)) & 1) == 1;
                    m[8, vp[i]] = b;
                    m[s - 8 + i, 8] = b;
                }
                m[8, s - 8] = true;
            }
        }

        #endregion
    }
}