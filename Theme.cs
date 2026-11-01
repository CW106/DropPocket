// 配色與全域快捷鍵的資料型別
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DropPocket
{
    // 使用者只挑主題色和面板底色，其他顏色都從這兩個推算
    class Theme
    {
        public static readonly Color DefaultAccent = Color.FromArgb(0x4C, 0x8B, 0xF5);
        public static readonly Color DefaultBackground = Color.FromArgb(0x1E, 0x1F, 0x24);
        public static readonly Theme Default = new Theme(DefaultAccent, DefaultBackground);

        public readonly Color Accent, Background;
        public readonly bool IsLight;
        public readonly Color Header, Text, Sub, IdleTab, Border;
        public readonly Color Hover, Select, SelectHover;
        public readonly Color ButtonHover, ButtonPress, AccentHover, AccentPress;
        public readonly Color OnAccent, BadgeShade, TabDots, Glow, Danger;

        public Theme(Color accent, Color background)
        {
            Accent = Color.FromArgb(255, accent.R, accent.G, accent.B);
            Background = Color.FromArgb(255, background.R, background.G, background.B);
            IsLight = Luminance(Background) > 0.5;
            bool defBg = SameRgb(Background, DefaultBackground);
            bool defAll = defBg && SameRgb(Accent, DefaultAccent);

            // 預設底色沿用原本手調的數值（公式算出來會稍微偏掉）
            if (defBg)
            {
                Header = Color.FromArgb(40, 42, 50);
                Text = Color.FromArgb(232, 232, 234);
                Sub = Color.FromArgb(150, 156, 166);
                IdleTab = Color.FromArgb(84, 88, 100);
                ButtonHover = Color.FromArgb(58, 61, 72);
                ButtonPress = Color.FromArgb(70, 74, 88);
            }
            else
            {
                Header = Ease.Lerp(Background, IsLight ? Color.Black : Color.White, 0.06f);
                Text = IsLight ? Color.FromArgb(0x1F, 0x23, 0x28) : Color.FromArgb(0xE8, 0xE8, 0xEA);
                Sub = Ease.Lerp(Text, Background, 0.4f);
                IdleTab = IsLight ? Color.FromArgb(0xA3, 0xA7, 0xB0) : Color.FromArgb(0x54, 0x58, 0x64);
                ButtonHover = Ease.Lerp(Header, Text, 0.08f);
                ButtonPress = Ease.Lerp(Header, Text, 0.14f);
            }
            if (defAll)
            {
                Hover = Color.FromArgb(44, 48, 60);
                Select = Color.FromArgb(46, 72, 120);
                SelectHover = Color.FromArgb(56, 86, 140);
            }
            else
            {
                Hover = Ease.Lerp(Ease.Lerp(Background, Text, 0.06f), Accent, 0.07f);
                Select = Ease.Lerp(Background, Accent, 0.35f);
                SelectHover = Ease.Lerp(Background, Accent, 0.47f);
            }
            Border = Ease.Lerp(Background, Text, 0.2f);
            AccentHover = Ease.Lerp(Accent, Color.White, 0.12f);
            AccentPress = Ease.Lerp(Accent, Color.Black, 0.12f);
            OnAccent = ContrastOn(Accent);
            bool darkInk = OnAccent.R < 128;
            BadgeShade = darkInk ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(48, 10, 20, 50);
            TabDots = Color.FromArgb(190, 255, 255, 255);
            Glow = Color.White;
            Danger = IsLight ? Color.FromArgb(0xC6, 0x28, 0x28) : Color.FromArgb(0xFF, 0x6B, 0x6B);
        }

        // WCAG 相對亮度
        public static double Luminance(Color c)
        {
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        static double Linear(int v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        // 畫在這個顏色上的字和勾勾：一般用白色，顏色太亮就改用近黑色
        // （門檻刻意偏高，讓預設的藍色維持白字）
        public static Color ContrastOn(Color c)
        {
            return Luminance(c) > 0.33 ? Color.FromArgb(0x11, 0x11, 0x11) : Color.White;
        }

        public static bool SameRgb(Color a, Color b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B;
        }

        public static string ToHex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public static bool TryParseHex(string text, out Color c)
        {
            c = Color.Empty;
            if (text == null) return false;
            text = text.Trim();
            if (text.StartsWith("#")) text = text.Substring(1);
            if (text.Length != 6) return false;
            int v;
            if (!int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out v)) return false;
            c = Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            return true;
        }
    }

    enum HotkeyResult { Ok, Occupied, Duplicate }

    // 全域快捷鍵：Ctrl / Alt / Shift 加一個按鍵；Key 是 None 表示沒有設定
    struct HotkeyCombo : IEquatable<HotkeyCombo>
    {
        public static readonly HotkeyCombo None = new HotkeyCombo();
        public static readonly HotkeyCombo DefaultToggle = new HotkeyCombo(true, true, false, Keys.P);

        public readonly bool Ctrl, Alt, Shift;
        public readonly Keys Key;

        public HotkeyCombo(bool ctrl, bool alt, bool shift, Keys key)
        {
            Key = key & Keys.KeyCode;
            bool any = Key != Keys.None;
            Ctrl = ctrl && any;
            Alt = alt && any;
            Shift = shift && any;
        }

        public bool IsNone { get { return Key == Keys.None; } }

        public uint Modifiers
        {
            get
            {
                uint m = 0;
                if (Ctrl) m |= Native.MOD_CONTROL;
                if (Alt) m |= Native.MOD_ALT;
                if (Shift) m |= Native.MOD_SHIFT;
                return m;
            }
        }

        // 要有 Ctrl 或 Alt（Shift 可加可不加）；F1–F24 可以單獨使用
        public bool IsValid
        {
            get
            {
                if (IsNone || IsModifierKey(Key) || Key == Keys.ProcessKey || Key == Keys.Packet) return false;
                if (!Enum.IsDefined(typeof(Keys), Key)) return false;
                return Ctrl || Alt || IsFunctionKey(Key);
            }
        }

        public static bool IsFunctionKey(Keys k) { return k >= Keys.F1 && k <= Keys.F24; }

        public static bool IsModifierKey(Keys k)
        {
            switch (k)
            {
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
                case Keys.LWin: case Keys.RWin:
                    return true;
            }
            return false;
        }

        // 顯示用：「Ctrl + Alt + P」；沒有設定時是空字串
        public string Display
        {
            get
            {
                if (IsNone) return "";
                var parts = new List<string>();
                if (Ctrl) parts.Add("Ctrl");
                if (Alt) parts.Add("Alt");
                if (Shift) parts.Add("Shift");
                parts.Add(KeyName(Key));
                return string.Join(" + ", parts);
            }
        }

        // 存檔用：「Ctrl+Alt+P」，按鍵用 Keys 列舉名稱才能原樣讀回來
        public string Serialize()
        {
            if (IsNone) return "";
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            parts.Add(Key.ToString());
            return string.Join("+", parts);
        }

        // 空字串 = 沒有設定；格式不對或不能當快捷鍵就用 fallback
        public static HotkeyCombo Parse(string text, HotkeyCombo fallback)
        {
            if (text == null) return fallback;
            text = text.Trim();
            if (text.Length == 0) return None;
            bool ctrl = false, alt = false, shift = false;
            Keys key = Keys.None;
            foreach (string raw in text.Split('+'))
            {
                string p = raw.Trim();
                if (p.Length == 0) return fallback;
                if (Is(p, "Ctrl") || Is(p, "Control")) ctrl = true;
                else if (Is(p, "Alt")) alt = true;
                else if (Is(p, "Shift")) shift = true;
                else
                {
                    Keys k;
                    if (key != Keys.None || !Enum.TryParse(p, true, out k)) return fallback;
                    if ((k & ~Keys.KeyCode) != 0 || k == Keys.None) return fallback;
                    key = k;
                }
            }
            var r = new HotkeyCombo(ctrl, alt, shift, key);
            return r.IsValid ? r : fallback;
        }

        static bool Is(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        public static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (int)(k - Keys.NumPad0);
            switch (k)
            {
                case Keys.Multiply: return "Num *";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Divide: return "Num /";
                case Keys.Decimal: return "Num .";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Oem1: return ";";
                case Keys.Oem7: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.Oem6: return "]";
                case Keys.Oem5: return "\\";
                case Keys.OemBackslash: return "\\";
                case Keys.Oemtilde: return "`";
                case Keys.Space: return "Space";
                case Keys.Enter: return "Enter";
                case Keys.Tab: return "Tab";
                case Keys.Escape: return "Esc";
                case Keys.Back: return "Backspace";
                case Keys.Delete: return "Delete";
                case Keys.Insert: return "Insert";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.PrintScreen: return "PrintScreen";
                case Keys.Pause: return "Pause";
                case Keys.Scroll: return "ScrollLock";
                case Keys.CapsLock: return "CapsLock";
                case Keys.NumLock: return "NumLock";
                case Keys.Apps: return "Menu";
            }
            return k.ToString();
        }

        public bool Equals(HotkeyCombo o)
        {
            return Key == o.Key && Ctrl == o.Ctrl && Alt == o.Alt && Shift == o.Shift;
        }

        public override bool Equals(object obj)
        {
            return obj is HotkeyCombo && Equals((HotkeyCombo)obj);
        }

        public override int GetHashCode()
        {
            return ((int)Key << 3) | (Ctrl ? 1 : 0) | (Alt ? 2 : 0) | (Shift ? 4 : 0);
        }
    }

    static partial class Native
    {
        public const int WM_KEYUP = 0x101, WM_SYSKEYUP = 0x105;
        const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_FRAME = 0x400;

        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        // 換配色後連捲軸、標題列這些非工作區也重畫
        public static void RedrawAll(IntPtr h)
        {
            try { RedrawWindow(h, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN); }
            catch { }
        }

        public static void SetDarkTitleBar(IntPtr h, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE；較舊的 Windows 10 用 19
                if (DwmSetWindowAttribute(h, 20, ref v, 4) != 0) DwmSetWindowAttribute(h, 19, ref v, 4);
            }
            catch { }
        }

        // Icon.FromHandle 建的圖示不擁有 HICON，Dispose 不會釋放，要自己 DestroyIcon
        public static void FreeIcon(Icon ic)
        {
            if (ic == null) return;
            IntPtr h = IntPtr.Zero;
            try { h = ic.Handle; } catch { }
            ic.Dispose();
            if (h != IntPtr.Zero) DestroyIcon(h);
        }
    }
}
