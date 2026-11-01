// 口袋的配色、全域快捷鍵、隱藏邊緣小條，以及給設定視窗用的介面
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DropPocket
{
    partial class PocketForm
    {
        const int HideKeyId = 0xD0D;
        const double HideMs = 380;

        Theme theme = Theme.Default;
        HotkeyCombo toggleKey = HotkeyCombo.DefaultToggle, hideKey = HotkeyCombo.None;
        bool hotkeysSuspended, toggleKeyFailed, hideKeyFailed;
        // tabHidden：整個小條藏起來（不存檔，每次啟動都看得到）；previewHold：設定視窗開著時不自動收合
        bool tabHidden, previewHold;
        // 小條滑出、淡入的進度（0..1）；隱藏時倒著播同一段
        double introP, showP;
        SettingsForm settings;
        Button btnGear;
        ToolStripMenuItem miToggle, miHideTab;

        public Theme CurrentTheme { get { return theme; } }
        public Icon AppIcon { get { return appIcon; } }
        public bool RightEdge { get { return rightEdge; } }
        public bool KeepAfterDrag { get { return keepAfterDrag; } }

        public bool AutostartEnabled
        {
            get
            {
                try { return IsAutostart(); }
                catch (Exception ex) { Diag.Log(ex); return false; }
            }
        }

        static void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        // ---------- 配色 ----------

        public void SetColors(Color accent, Color background)
        {
            theme = new Theme(accent, background);
            ApplyTheme();
            SaveSettings();
        }

        void ApplyTheme()
        {
            Theme t = theme;
            BackColor = t.Background;
            ForeColor = t.Text;
            content.BackColor = t.Background;
            body.BackColor = t.Background;
            header.BackColor = t.Header;
            title.ForeColor = t.Text;
            footer.ForeColor = t.Sub;
            rail.BackColor = t.Header;
            foreach (Button b in new[] { btnAll, btnClear, btnPin, btnGear }) StyleHeaderButton(b);
            UpdatePinButton();

            list.BackColor = t.Background;
            list.ForeColor = t.Text;
            list.Palette = t;
            // 其他項目跟著清單的前景色，只有找不到的項目另外指定了顏色
            foreach (ListViewItem lvi in list.Items)
            {
                var it = lvi.Tag as PocketItem;
                if (it != null && !it.Exists) lvi.ForeColor = t.Sub;
            }
            ApplyListTheme();
            emptyState.Palette = t;
            overlay.Palette = t;

            RebuildIcon();
            Invalidate(true);
            rail.Invalidate();
            if (settings != null && !settings.IsDisposed) settings.ApplyTheme(t);
        }

        void ApplyListTheme()
        {
            if (!list.IsHandleCreated) return;
            // 捲軸跟著深色 / 淺色
            Native.SetWindowTheme(list.Handle, theme.IsLight ? "Explorer" : "DarkMode_Explorer", null);
            Native.RedrawAll(list.Handle);
        }

        void RebuildIcon()
        {
            Icon old = appIcon;
            appIcon = MakeIcon(S(32), theme);
            Icon = appIcon;
            if (tray != null) tray.Icon = appIcon;
            if (settings != null && !settings.IsDisposed) settings.Icon = appIcon;
            // 每個地方都換成新圖示之後才釋放舊的 HICON
            if (old != null && !ReferenceEquals(old, appIcon)) Native.FreeIcon(old);
        }

        void StyleHeaderButton(Button b)
        {
            b.ForeColor = theme.Text;
            b.BackColor = theme.Header;
            b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
            b.FlatAppearance.MouseDownBackColor = theme.ButtonPress;
        }

        Button MakeGearButton()
        {
            Button b = MakeHeaderButton("", null); // 提示文字由 ApplyTexts 設定
            b.Width = S(28);
            b.Paint += PaintGear;
            b.Click += (o, e) => ShowSettings();
            return b;
        }

        void PaintGear(object sender, PaintEventArgs e)
        {
            try
            {
                var c = (Control)sender;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                DrawGear(e.Graphics, theme.Text, c.Width / 2f, c.Height / 2f, 7f * s);
            }
            catch (Exception ex) { Diag.Log(ex); }
        }

        // 齒輪：8 個齒的外框，中間挖一個圓孔
        static void DrawGear(Graphics g, Color color, float cx, float cy, float r)
        {
            if (r < 2) return;
            const int teeth = 8;
            double step = 2 * Math.PI / teeth;
            float ri = r * 0.72f;
            var pts = new PointF[teeth * 4];
            for (int i = 0; i < teeth; i++)
            {
                double a = i * step;
                pts[i * 4] = Polar(cx, cy, ri, a - step * 0.32);
                pts[i * 4 + 1] = Polar(cx, cy, r, a - step * 0.2);
                pts[i * 4 + 2] = Polar(cx, cy, r, a + step * 0.2);
                pts[i * 4 + 3] = Polar(cx, cy, ri, a + step * 0.32);
            }
            using (var path = new GraphicsPath(FillMode.Alternate))
            using (var b = new SolidBrush(color))
            {
                path.AddPolygon(pts);
                float h = r * 0.34f;
                path.AddEllipse(cx - h, cy - h, h * 2, h * 2);
                g.FillPath(b, path);
            }
        }

        static PointF Polar(float cx, float cy, float r, double a)
        {
            return new PointF(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a));
        }

        // ---------- 全域快捷鍵 ----------

        public HotkeyCombo GetHotkey(bool hide) { return hide ? hideKey : toggleKey; }

        public bool HotkeyFailed(bool hide) { return hide ? hideKeyFailed : toggleKeyFailed; }

        bool TryRegister(int id, HotkeyCombo k)
        {
            if (k.IsNone) return true;
            return Native.RegisterHotKey(Handle, id, k.Modifiers | Native.MOD_NOREPEAT, (uint)k.Key);
        }

        // 視窗建立（或重建）時註冊；錄製快捷鍵期間先不註冊
        void RegisterHotkeys()
        {
            if (!IsHandleCreated) return;
            UnregisterHotkeys();
            if (hotkeysSuspended) return;
            toggleKeyFailed = !TryRegister(HotkeyId, toggleKey);
            hideKeyFailed = !TryRegister(HideKeyId, hideKey);
        }

        void UnregisterHotkeys()
        {
            if (!IsHandleCreated) return;
            Native.UnregisterHotKey(Handle, HotkeyId);
            Native.UnregisterHotKey(Handle, HideKeyId);
        }

        // 錄製新的組合鍵時兩組都先放掉，不然按下去會被 WM_HOTKEY 吃掉
        public void SuspendHotkeys()
        {
            if (hotkeysSuspended) return;
            hotkeysSuspended = true;
            UnregisterHotkeys();
        }

        public void ResumeHotkeys()
        {
            if (!hotkeysSuspended) return;
            hotkeysSuspended = false;
            RegisterHotkeys();
        }

        public HotkeyResult SetHotkey(bool hide, HotkeyCombo k)
        {
            HotkeyCombo old = hide ? hideKey : toggleKey, other = hide ? toggleKey : hideKey;
            if (!k.IsNone && k.Equals(other)) return HotkeyResult.Duplicate;
            if (IsHandleCreated && !hotkeysSuspended)
            {
                int id = hide ? HideKeyId : HotkeyId;
                Native.UnregisterHotKey(Handle, id);
                if (!TryRegister(id, k))
                {
                    // 被其他程式佔用：把原本的註冊回去
                    bool oldOk = TryRegister(id, old);
                    if (hide) hideKeyFailed = !oldOk; else toggleKeyFailed = !oldOk;
                    return HotkeyResult.Occupied;
                }
            }
            if (hide) { hideKey = k; hideKeyFailed = false; }
            else { toggleKey = k; toggleKeyFailed = false; }
            SaveSettings();
            UpdateHotkeyTexts();
            return HotkeyResult.Ok;
        }

        bool OnHotkey(int id)
        {
            if (id != HotkeyId && id != HideKeyId) return false;
            if (id == HotkeyId) Safe(OnToggleRequest);
            else Safe(() => SetTabHidden(!tabHidden));
            return true;
        }

        void UpdateHotkeyTexts()
        {
            string t = toggleKey.Display, h = hideKey.Display;
            // 英文比較長，底部提示放不下「右鍵更多」，設了快捷鍵時就只留快捷鍵
            footer.Text = L.En
                ? "Double-click opens" + (t.Length > 0 ? " · " + t + " pins" : " · Right-click for more")
                : "雙擊開啟 · 右鍵更多" + (t.Length > 0 ? " · " + t + " 釘選" : "");
            toolTip.SetToolTip(btnPin, L.T("固定展開口袋", "Keep the pocket open")
                + (t.Length > 0 ? L.T("（" + t + "）", " (" + t + ")") : ""));
            string gap = L.T("　", "    ");
            miToggle.Text = L.T("顯示 / 隱藏口袋", "Show / hide pocket") + (t.Length > 0 ? gap + t : "");
            miHideTab.Text = L.T("隱藏邊緣小條", "Hide edge tab") + (h.Length > 0 ? gap + h : "");
        }

        // ---------- 完全隱藏邊緣小條 ----------

        // 快捷鍵、系統匣左鍵：小條藏起來時先叫出來
        void OnToggleRequest()
        {
            if (tabHidden) SetTabHidden(false);
            TogglePinned();
        }

        void SetTabHidden(bool hide)
        {
            if (hide == tabHidden) return;
            tabHidden = hide;
            if (hide)
            {
                pinned = false;
                UpdatePinButton();
                SetExpanded(false);
                if (dropHover) EndDropHover();
            }
            else
            {
                if (!Visible) Show();
                if (previewHold) SetExpanded(true);
            }
            UpdateMenuChecks();
            EnsureAnimating();
        }

        // 滑回邊緣、淡出都播完才把視窗藏起來，之後點擊和拖曳都不會落在它上面
        void FinishHideIfDone()
        {
            if (tabHidden && introP <= 0 && showP <= 0 && openness <= 0f && Visible) Hide();
        }

        bool StepIntro(double dt)
        {
            double target = tabHidden ? 0 : 1;
            if (introP == target) return false;
            double d = dt / (tabHidden ? HideMs : 500.0);
            introP = tabHidden ? Math.Max(0, introP - d) : Math.Min(1, introP + d);
            return introP != target;
        }

        bool StepShowFade(double dt)
        {
            double target = tabHidden ? 0 : 1;
            if (showP == target) return false;
            double d = dt / (tabHidden ? HideMs : 400.0);
            showP = tabHidden ? Math.Max(0, showP - d) : Math.Min(1, showP + d);
            return showP != target;
        }

        // ---------- 設定視窗 ----------

        void ShowSettings()
        {
            ShowSettings(null);
        }

        // at：重建設定視窗（例如切換語言）時放回原本的位置
        void ShowSettings(Point? at)
        {
            try
            {
                if (tabHidden) SetTabHidden(false);
                if (settings != null && !settings.IsDisposed)
                {
                    if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
                    settings.Show();
                    settings.Activate();
                    return;
                }
                var f = new SettingsForm(this);
                if (at.HasValue)
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = at.Value;
                }
                f.FormClosed += OnSettingsClosed;
                settings = f;
                // 設定視窗開著時口袋保持展開，當作即時預覽
                previewHold = true;
                SetExpanded(true);
                f.Show();
                f.Activate();
            }
            catch (Exception ex)
            {
                Diag.Log(ex);
                if (settings != null && (settings.IsDisposed || !settings.Visible))
                {
                    try { settings.Dispose(); } catch { }
                    settings = null;
                }
                previewHold = settings != null;
                ResumeHotkeys();
            }
        }

        void OnSettingsClosed(object sender, FormClosedEventArgs e)
        {
            var f = sender as SettingsForm;
            if (f != null) f.FormClosed -= OnSettingsClosed;
            if (f == null || ReferenceEquals(f, settings)) settings = null;
            previewHold = false;
            outsideTicks = 0;
            ResumeHotkeys();
        }

        public void SetDockRight(bool right)
        {
            if (right != rightEdge) SetEdge(right);
        }

        public void SetKeepAfterDrag(bool keep)
        {
            keepAfterDrag = keep;
            SaveSettings();
            UpdateMenuChecks();
        }

        public void SetAutostart(bool on)
        {
            try { if (on != IsAutostart()) ToggleAutostart(); }
            catch (Exception ex) { Diag.Log(ex); }
            UpdateMenuChecks();
        }
    }
}
