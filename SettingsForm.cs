// 設定視窗：外觀（主題色、面板底色）、快捷鍵、一般選項
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DropPocket
{
    // 一般可以啟用的視窗（要拿得到鍵盤焦點才能錄快捷鍵），不像口袋本身是 NOACTIVATE
    class SettingsForm : Form
    {
        const string OccupiedText = "這組快捷鍵已被其他程式佔用，請換一組";
        const string DuplicateText = "和另一個快捷鍵重複了";

        static readonly Color[] AccentPresets =
        {
            Rgb(0x4C8BF5), Rgb(0x8B5CF6), Rgb(0xEC4899), Rgb(0xEF4444),
            Rgb(0xF59E0B), Rgb(0x22C55E), Rgb(0x14B8A6), Rgb(0x94A3B8),
        };
        static readonly string[] AccentNames = { "藍", "紫", "粉", "紅", "橘", "綠", "青", "灰" };
        static readonly Color[] BgPresets = { Rgb(0x1E1F24), Rgb(0x0B0B0D), Rgb(0x161C2C), Rgb(0xF3F4F6), Rgb(0xFFFFFF) };
        static readonly string[] BgNames = { "深灰", "純黑", "深藍", "淺灰", "白" };
        static int[] customColors; // 調色盤裡的自訂色，程式結束前都記得

        class HotkeyRow
        {
            public bool Hide, Error;
            public Label Caption, Status;
            public HotkeyBox Box;
            public Button Clear;
        }

        readonly PocketForm pocket;
        readonly float s;
        readonly Font baseFont, boldFont, smallFont;
        readonly ToolTip tips = new ToolTip();
        readonly List<Label> sections = new List<Label>(), captions = new List<Label>();
        readonly List<Swatch> accentSwatches = new List<Swatch>(), bgSwatches = new List<Swatch>();
        Swatch accentCustom, bgCustom;
        HotkeyRow rowToggle, rowHide;
        Label hint;
        OptionBox optRight, optLeft, optKeep, optAuto;
        Button btnReset, btnDone;
        Panel divider;
        Theme theme;

        static Color Rgb(int v) { return Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF); }

        int S(int v) { return (int)Math.Round(v * s); }

        static void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        public SettingsForm(PocketForm owner)
        {
            pocket = owner;
            s = Gfx.Scale;
            theme = owner.CurrentTheme;
            Text = "暫存口袋 設定";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            baseFont = new Font("Microsoft JhengHei UI", 9f);
            boldFont = new Font(baseFont, FontStyle.Bold);
            smallFont = new Font(baseFont.FontFamily, 8.5f);
            Font = baseFont;
            Icon = owner.AppIcon;

            BuildLayout();
            ApplyTheme(theme);
            SyncFromPocket();
            InitHotkeyRow(rowToggle);
            InitHotkeyRow(rowHide);
            // 一開始把焦點放在「完成」，不要落在錄製框上
            ActiveControl = btnDone;
        }

        // ---------- 版面 ----------

        void BuildLayout()
        {
            int pad = S(20), width = S(440), right = width - pad, y = S(12);

            AddSection("外觀", pad, ref y);
            AddCaption("主題色", pad, ref y);
            accentCustom = AddSwatchRow(AccentPresets, AccentNames, accentSwatches, true, pad, ref y);
            AddCaption("面板底色", pad, ref y);
            bgCustom = AddSwatchRow(BgPresets, BgNames, bgSwatches, false, pad, ref y);

            y += S(4);
            AddSection("快捷鍵", pad, ref y);
            rowToggle = AddHotkeyRow("顯示 / 隱藏口袋", false, pad, right, ref y);
            rowHide = AddHotkeyRow("完全隱藏邊緣小條", true, pad, right, ref y);
            hint = new Label { Text = "點一下方框，再按下新的組合鍵", AutoSize = true, Font = smallFont, Location = new Point(pad, y) };
            Controls.Add(hint);
            y += S(30);

            AddSection("一般", pad, ref y);
            var dock = new Label
            {
                Text = "停靠位置",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(pad, y, S(100), S(26)),
            };
            Controls.Add(dock);
            captions.Add(dock);
            optRight = AddOption("右側", true, pad + S(104), y);
            optLeft = AddOption("左側", true, optRight.Right + S(16), y);
            y += S(32);
            optKeep = AddOption("拖出後保留項目", false, pad, y);
            y += S(30);
            optAuto = AddOption("開機時自動啟動", false, pad, y);
            y += S(38);

            divider = new Panel { Bounds = new Rectangle(0, y, width, Math.Max(1, S(1))) };
            Controls.Add(divider);
            y += S(14);
            btnReset = AddButton("恢復預設", new Rectangle(pad, y, S(100), S(32)));
            btnDone = AddButton("完成", new Rectangle(right - S(92), y, S(92), S(32)));
            tips.SetToolTip(btnReset, "把主題色、面板底色和兩組快捷鍵恢復成預設值");
            y += S(32) + S(14);

            optRight.UserToggled += (o, e) => Safe(() => pocket.SetDockRight(true));
            optLeft.UserToggled += (o, e) => Safe(() => pocket.SetDockRight(false));
            optKeep.UserToggled += (o, e) => Safe(() => pocket.SetKeepAfterDrag(!pocket.KeepAfterDrag));
            optAuto.UserToggled += (o, e) => Safe(() => pocket.SetAutostart(!pocket.AutostartEnabled));
            btnReset.Click += (o, e) => Safe(ResetDefaults);
            btnDone.Click += (o, e) => Close();
            CancelButton = btnDone;
            ClientSize = new Size(width, y);
        }

        void AddSection(string text, int x, ref int y)
        {
            var l = new Label { Text = text, AutoSize = true, Font = boldFont, Location = new Point(x, y) };
            Controls.Add(l);
            sections.Add(l);
            y += S(28);
        }

        void AddCaption(string text, int x, ref int y)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
            Controls.Add(l);
            captions.Add(l);
            y += S(24);
        }

        Swatch AddSwatchRow(Color[] colors, string[] names, List<Swatch> into, bool accent, int x, ref int y)
        {
            int d = S(30), gap = S(6);
            for (int i = 0; i < colors.Length; i++)
            {
                Color c = colors[i];
                var sw = new Swatch { Color = c, Bounds = new Rectangle(x + i * (d + gap), y, d, d) };
                sw.Click += (o, e) => Safe(() => PickColor(accent, c));
                tips.SetToolTip(sw, names[i]);
                Controls.Add(sw);
                into.Add(sw);
            }
            int cx = x + colors.Length * (d + gap) + S(4);
            int cw = d + TextRenderer.MeasureText("自訂…", Font).Width + S(10);
            var custom = new Swatch { IsCustom = true, Text = "自訂…", Bounds = new Rectangle(cx, y, cw, d) };
            custom.Click += (o, e) => Safe(() => PickCustom(accent));
            tips.SetToolTip(custom, accent ? "自訂主題色" : "自訂面板底色");
            Controls.Add(custom);
            y += d + S(14);
            return custom;
        }

        HotkeyRow AddHotkeyRow(string caption, bool hide, int x, int right, ref int y)
        {
            int h = S(30);
            var r = new HotkeyRow { Hide = hide };
            r.Caption = new Label
            {
                Text = caption,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(x, y, S(124), h),
            };
            r.Box = new HotkeyBox { Bounds = new Rectangle(x + S(128), y, S(186), h) };
            Controls.Add(r.Caption);
            Controls.Add(r.Box);
            int cx = r.Box.Right + S(8);
            r.Clear = AddButton("清除", new Rectangle(cx, y, Math.Max(S(48), right - cx), h));
            r.Status = new Label
            {
                AutoSize = false,
                Font = smallFont,
                TextAlign = ContentAlignment.TopLeft,
                Bounds = new Rectangle(r.Box.Left, y + h + S(3), right - r.Box.Left, S(20)),
            };
            Controls.Add(r.Status);
            captions.Add(r.Caption);
            tips.SetToolTip(r.Box, "Esc 取消 · Backspace / Delete 清除");
            tips.SetToolTip(r.Clear, "不使用快捷鍵");

            r.Box.RecordingChanged += (o, e) => Safe(UpdateHotkeySuspend);
            r.Box.Committed += k => Safe(() => ApplyHotkey(r, k));
            r.Clear.Click += (o, e) => Safe(() => { r.Box.Cancel(); ApplyHotkey(r, HotkeyCombo.None); });
            y += h + S(26);
            return r;
        }

        OptionBox AddOption(string text, bool radio, int x, int y)
        {
            int w = S(30) + TextRenderer.MeasureText(text, Font).Width + S(6);
            var o = new OptionBox { Text = text, Radio = radio, Bounds = new Rectangle(x, y, w, S(26)) };
            Controls.Add(o);
            return o;
        }

        Button AddButton(string text, Rectangle bounds)
        {
            var b = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
            Controls.Add(b);
            return b;
        }

        // ---------- 配色 ----------

        public void ApplyTheme(Theme t)
        {
            theme = t;
            BackColor = t.Background;
            ForeColor = t.Text;
            foreach (Label l in sections) l.ForeColor = t.Text;
            foreach (Label l in captions) l.ForeColor = t.Text;
            hint.ForeColor = t.Sub;
            foreach (Swatch sw in accentSwatches) sw.Palette = t;
            foreach (Swatch sw in bgSwatches) sw.Palette = t;
            accentCustom.Palette = t;
            bgCustom.Palette = t;
            foreach (HotkeyRow r in new[] { rowToggle, rowHide })
            {
                r.Box.Palette = t;
                r.Status.ForeColor = r.Error ? t.Danger : t.Sub;
                StyleButton(r.Clear, false);
            }
            foreach (OptionBox ob in new[] { optRight, optLeft, optKeep, optAuto }) ob.Palette = t;
            StyleButton(btnReset, false);
            StyleButton(btnDone, true);
            divider.BackColor = t.Border;
            SyncSwatches();
            if (IsHandleCreated)
            {
                Native.SetDarkTitleBar(Handle, !t.IsLight);
                Native.RedrawAll(Handle);
            }
            Invalidate(true);
        }

        void StyleButton(Button b, bool primary)
        {
            Theme t = theme;
            b.BackColor = primary ? t.Accent : t.Header;
            b.ForeColor = primary ? t.OnAccent : t.Text;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = primary ? t.Accent : t.Border;
            b.FlatAppearance.MouseOverBackColor = primary ? t.AccentHover : t.ButtonHover;
            b.FlatAppearance.MouseDownBackColor = primary ? t.AccentPress : t.ButtonPress;
        }

        void SyncSwatches()
        {
            MarkSelected(accentSwatches, accentCustom, theme.Accent);
            MarkSelected(bgSwatches, bgCustom, theme.Background);
        }

        // 對不上任何預設色時，由「自訂」格顯示成已選取
        static void MarkSelected(List<Swatch> presets, Swatch custom, Color current)
        {
            bool found = false;
            foreach (Swatch sw in presets)
            {
                bool sel = Theme.SameRgb(sw.Color, current);
                sw.Selected = sel;
                if (sel) found = true;
            }
            custom.Color = current;
            custom.Selected = !found;
        }

        void PickColor(bool accent, Color c)
        {
            Theme t = pocket.CurrentTheme;
            if (accent) pocket.SetColors(c, t.Background);
            else pocket.SetColors(t.Accent, c);
        }

        void PickCustom(bool accent)
        {
            using (var dlg = new ColorDialog())
            {
                Theme t = pocket.CurrentTheme;
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                dlg.Color = accent ? t.Accent : t.Background;
                if (customColors != null) dlg.CustomColors = customColors;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                customColors = dlg.CustomColors;
                PickColor(accent, dlg.Color);
            }
        }

        // ---------- 快捷鍵 ----------

        void InitHotkeyRow(HotkeyRow r)
        {
            r.Box.Value = pocket.GetHotkey(r.Hide);
            if (pocket.HotkeyFailed(r.Hide)) SetStatus(r, OccupiedText, true);
            else SetStatus(r, "", false);
        }

        // 任何一個框在錄製時，兩組全域快捷鍵都先放掉
        void UpdateHotkeySuspend()
        {
            bool any = (rowToggle != null && rowToggle.Box.Recording) || (rowHide != null && rowHide.Box.Recording);
            if (any) pocket.SuspendHotkeys();
            else pocket.ResumeHotkeys();
        }

        void ApplyHotkey(HotkeyRow r, HotkeyCombo k)
        {
            ShowResult(r, pocket.SetHotkey(r.Hide, k));
        }

        void ShowResult(HotkeyRow r, HotkeyResult res)
        {
            r.Box.Value = pocket.GetHotkey(r.Hide);
            if (res == HotkeyResult.Ok) SetStatus(r, "已套用", false);
            else SetStatus(r, res == HotkeyResult.Occupied ? OccupiedText : DuplicateText, true);
        }

        void SetStatus(HotkeyRow r, string text, bool error)
        {
            r.Error = error;
            r.Status.Text = text;
            r.Status.ForeColor = error ? theme.Danger : theme.Sub;
        }

        void StopRecordings()
        {
            if (rowToggle != null) rowToggle.Box.Cancel();
            if (rowHide != null) rowHide.Box.Cancel();
            pocket.ResumeHotkeys();
        }

        // 只恢復兩個顏色和兩組快捷鍵；先清掉隱藏小條的，預設的 Ctrl+Alt+P 才不會被判成重複
        void ResetDefaults()
        {
            StopRecordings();
            HotkeyResult hideRes = pocket.SetHotkey(true, HotkeyCombo.None);
            HotkeyResult toggleRes = pocket.SetHotkey(false, HotkeyCombo.DefaultToggle);
            pocket.SetColors(Theme.DefaultAccent, Theme.DefaultBackground);
            ShowResult(rowHide, hideRes);
            ShowResult(rowToggle, toggleRes);
        }

        // ---------- 和口袋 / 系統匣同步 ----------

        public void SyncFromPocket()
        {
            bool right = pocket.RightEdge;
            optRight.Checked = right;
            optLeft.Checked = !right;
            optKeep.Checked = pocket.KeepAfterDrag;
            optAuto.Checked = pocket.AutostartEnabled;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkTitleBar(Handle, !theme.IsLight);
        }

        // 切到別的視窗、關閉視窗時，正在錄的一律取消，快捷鍵也一定要註冊回去
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Safe(StopRecordings);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Safe(StopRecordings);
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Safe(pocket.ResumeHotkeys);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                tips.Dispose();
                smallFont.Dispose();
                boldFont.Dispose();
                baseFont.Dispose();
            }
        }
    }
}
