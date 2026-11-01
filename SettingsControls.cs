// 設定視窗用的自繪控制項：色票、快捷鍵錄製框、核取方塊 / 選項按鈕
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DropPocket
{
    // 圓形色票；IsCustom 時是「色點＋文字」的膠囊（沒選取時色點畫成色環）
    class Swatch : PaintBox
    {
        Theme palette = Theme.Default;
        Color color = Color.Gray;
        bool selected, hover;
        public bool IsCustom;

        public Swatch() { Cursor = Cursors.Hand; }

        public Theme Palette
        {
            get { return palette; }
            set { palette = value ?? Theme.Default; Invalidate(); }
        }

        public Color Color
        {
            get { return color; }
            set { color = value; Invalidate(); }
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { Draw(e.Graphics); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        void Draw(Graphics g)
        {
            Theme t = palette;
            g.Clear(t.Background);
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 8 || h < 8) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sc = Gfx.Scale;
            float ring = Math.Max(1.5f, 2f * sc);
            float inset = ring + Math.Max(2f, 2.5f * sc);
            var dot = new RectangleF(inset, inset, h - 2 * inset, h - 2 * inset);
            if (hover && !selected) dot.Inflate(sc, sc);

            if (IsCustom)
            {
                var pill = new RectangleF(ring / 2f, ring / 2f, w - ring, h - ring);
                using (var path = Gfx.RoundedRect(pill, pill.Height / 2f))
                {
                    using (var b = new SolidBrush(hover ? t.ButtonHover : t.Header)) g.FillPath(b, path);
                    using (var pen = new Pen(selected ? t.Text : t.Border, selected ? ring : Math.Max(1f, sc))) g.DrawPath(pen, path);
                }
            }

            bool wheel = IsCustom && !selected;
            if (wheel) DrawHueWheel(g, dot);
            else using (var b = new SolidBrush(color)) g.FillEllipse(b, dot);
            // 和底色很接近的顏色（例如白底上的白色）要有一圈細框才看得出來
            using (var pen = new Pen(Color.FromArgb(70, t.Text), Math.Max(1f, sc)))
                g.DrawEllipse(pen, dot);

            if (selected)
            {
                if (!IsCustom)
                    using (var pen = new Pen(t.Text, ring))
                        g.DrawEllipse(pen, ring / 2f, ring / 2f, h - ring, h - ring);
                float cx = dot.X + dot.Width / 2f, cy = dot.Y + dot.Height / 2f, r = dot.Width / 2f;
                using (var pen = Gfx.RoundPen(Theme.ContrastOn(color), Math.Max(1.5f, 2f * sc)))
                    Gfx.DrawCheck(g, pen,
                        new PointF(cx - 0.42f * r, cy + 0.02f * r),
                        new PointF(cx - 0.12f * r, cy + 0.32f * r),
                        new PointF(cx + 0.44f * r, cy - 0.30f * r), 1f);
            }

            if (IsCustom)
            {
                int tx = h;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Math.Max(1, w - tx), h),
                    selected || hover ? t.Text : t.Sub,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        static void DrawHueWheel(Graphics g, RectangleF r)
        {
            if (r.Width < 2 || r.Height < 2) return;
            for (int i = 0; i < 12; i++)
                using (var b = new SolidBrush(Hsv(i * 30, 0.62, 0.96)))
                    g.FillPie(b, r.X, r.Y, r.Width, r.Height, i * 30 - 90, 31);
        }

        static Color Hsv(double hue, double sat, double val)
        {
            double c = val * sat, x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1)), m = val - c;
            double r = 0, gr = 0, b = 0;
            if (hue < 60) { r = c; gr = x; }
            else if (hue < 120) { r = x; gr = c; }
            else if (hue < 180) { gr = c; b = x; }
            else if (hue < 240) { gr = x; b = c; }
            else if (hue < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb(ToByte(r + m), ToByte(gr + m), ToByte(b + m));
        }

        static int ToByte(double v) { return Math.Max(0, Math.Min(255, (int)Math.Round(v * 255))); }
    }

    // 快捷鍵錄製框：點一下開始錄，按下組合鍵就送出；Esc 取消、Backspace / Delete 清除
    class HotkeyBox : Control
    {
        Theme palette = Theme.Default;
        HotkeyCombo combo;
        bool recording, hover;
        string note; // 組合不合規則時的提示，按下修飾鍵就清掉

        public event EventHandler RecordingChanged;
        public event Action<HotkeyCombo> Committed;

        public HotkeyBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            // 注音等輸入法開著時，按鍵才不會變成 ProcessKey
            ImeMode = ImeMode.Disable;
        }

        public Theme Palette
        {
            get { return palette; }
            set { palette = value ?? Theme.Default; Invalidate(); }
        }

        public HotkeyCombo Value
        {
            get { return combo; }
            set { combo = value; Invalidate(); }
        }

        public bool Recording { get { return recording; } }

        public void Cancel() { StopRecording(); }

        void StartRecording()
        {
            if (recording) return;
            recording = true;
            note = null;
            Invalidate();
            var h = RecordingChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        void StopRecording()
        {
            if (!recording) return;
            recording = false;
            note = null;
            Invalidate();
            var h = RecordingChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        void Commit(HotkeyCombo k)
        {
            // 先結束錄製（原本的快捷鍵註冊回去），再換成新的
            StopRecording();
            var h = Committed;
            if (h != null) h(k);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (!Focused) Focus();
            // 拿不到焦點就收不到按鍵，也就不開始錄
            if (Focused) StartRecording();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            StopRecording();
            Invalidate();
        }

        // 錄製中 Tab、方向鍵、Enter 也要當成一般按鍵收進來
        protected override bool IsInputKey(Keys keyData)
        {
            return recording || base.IsInputKey(keyData);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!recording)
            {
                Keys code = keyData & Keys.KeyCode;
                if ((keyData & Keys.Modifiers) == 0 && (code == Keys.Enter || code == Keys.Space))
                {
                    StartRecording();
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            try { HandleKey(keyData); }
            catch (Exception ex) { Diag.Log(ex); }
            return true;
        }

        void HandleKey(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Escape) { StopRecording(); return; }
            if (code == Keys.Back || code == Keys.Delete) { Commit(HotkeyCombo.None); return; }
            if (HotkeyCombo.IsModifierKey(code)) { note = null; Invalidate(); return; }
            var k = new HotkeyCombo((keyData & Keys.Control) != 0, (keyData & Keys.Alt) != 0, (keyData & Keys.Shift) != 0, code);
            if (!k.IsValid) { note = "要搭配 Ctrl 或 Alt"; Invalidate(); return; }
            Commit(k);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_KEYUP || m.Msg == Native.WM_SYSKEYUP)
            {
                if (recording) Invalidate(); // 放開修飾鍵時更新顯示
                // 吃掉 WM_SYSKEYUP：放開 Alt / F10 時不要進入系統選單模式
                if (m.Msg == Native.WM_SYSKEYUP) return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { Draw(e.Graphics); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        void Draw(Graphics g)
        {
            Theme t = palette;
            g.Clear(t.Background);
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 8 || h < 8) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sc = Gfx.Scale;
            float bw = recording ? Math.Max(1.5f, 2f * sc) : Math.Max(1f, sc);
            var r = new RectangleF(bw / 2f, bw / 2f, w - bw, h - bw);
            Color border = recording ? t.Accent : (Focused || hover ? Ease.Lerp(t.Border, t.Text, 0.35f) : t.Border);
            using (var path = Gfx.RoundedRect(r, 6f * sc))
            {
                using (var b = new SolidBrush(t.Header)) g.FillPath(b, path);
                using (var pen = new Pen(border, bw)) g.DrawPath(pen, path);
            }

            string text;
            Color fore;
            if (recording)
            {
                string held = HeldModifiers();
                if (held.Length > 0) { text = held + " + …"; fore = t.Text; }
                else if (note != null) { text = note; fore = t.Danger; }
                else { text = "請按下組合鍵…"; fore = t.Sub; }
            }
            else if (combo.IsNone) { text = "無"; fore = t.Sub; }
            else { text = combo.Display; fore = t.Text; }
            int pad = (int)Math.Round(10 * sc);
            TextRenderer.DrawText(g, text, Font, new Rectangle(pad, 0, Math.Max(1, w - 2 * pad), h), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        static string HeldModifiers()
        {
            Keys m = Control.ModifierKeys;
            var parts = new List<string>();
            if ((m & Keys.Control) != 0) parts.Add("Ctrl");
            if ((m & Keys.Alt) != 0) parts.Add("Alt");
            if ((m & Keys.Shift) != 0) parts.Add("Shift");
            return string.Join(" + ", parts);
        }
    }

    // 自繪的核取方塊 / 選項按鈕（系統內建的在深色底上很難看清楚）。
    // 點擊只發出 UserToggled，由設定視窗決定新狀態再寫回 Checked
    class OptionBox : Control
    {
        Theme palette = Theme.Default;
        bool isChecked, hover;
        public bool Radio;
        public event EventHandler UserToggled;

        public OptionBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public Theme Palette
        {
            get { return palette; }
            set { palette = value ?? Theme.Default; Invalidate(); }
        }

        public bool Checked
        {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; Invalidate(); } }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            Toggle();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None) { Toggle(); e.Handled = true; }
        }

        void Toggle()
        {
            if (Radio && isChecked) return;
            var h = UserToggled;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { Draw(e.Graphics); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        void Draw(Graphics g)
        {
            Theme t = palette;
            g.Clear(t.Background);
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 8 || h < 8) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sc = Gfx.Scale;
            float box = 16f * sc;
            var r = new RectangleF(4f * sc, (h - box) / 2f, box, box);
            Color edge = Ease.Lerp(t.Border, t.Text, hover || Focused ? 0.45f : 0.15f);
            float pw = Math.Max(1f, 1.3f * sc);

            if (Radio)
            {
                if (isChecked)
                {
                    using (var b = new SolidBrush(t.Accent)) g.FillEllipse(b, r);
                    float d = box * 0.4f;
                    using (var b = new SolidBrush(t.OnAccent)) g.FillEllipse(b, r.X + (box - d) / 2f, r.Y + (box - d) / 2f, d, d);
                }
                else
                {
                    using (var b = new SolidBrush(t.Header)) g.FillEllipse(b, r);
                    using (var pen = new Pen(edge, pw)) g.DrawEllipse(pen, r);
                }
            }
            else
            {
                using (var path = Gfx.RoundedRect(r, 4f * sc))
                {
                    if (isChecked)
                    {
                        using (var b = new SolidBrush(t.Accent)) g.FillPath(b, path);
                        using (var pen = Gfx.RoundPen(t.OnAccent, Math.Max(1.5f, 2f * sc)))
                            Gfx.DrawCheck(g, pen,
                                new PointF(r.X + box * 0.24f, r.Y + box * 0.52f),
                                new PointF(r.X + box * 0.43f, r.Y + box * 0.71f),
                                new PointF(r.X + box * 0.77f, r.Y + box * 0.31f), 1f);
                    }
                    else
                    {
                        using (var b = new SolidBrush(t.Header)) g.FillPath(b, path);
                        using (var pen = new Pen(edge, pw)) g.DrawPath(pen, path);
                    }
                }
            }

            if (Focused && ShowFocusCues)
            {
                RectangleF fr = r;
                fr.Inflate(2.5f * sc, 2.5f * sc);
                using (var pen = new Pen(Color.FromArgb(160, t.Accent), Math.Max(1f, 1.5f * sc)))
                {
                    if (Radio) g.DrawEllipse(pen, fr);
                    else using (var path = Gfx.RoundedRect(fr, 6f * sc)) g.DrawPath(pen, path);
                }
            }

            int tx = (int)Math.Ceiling(r.Right + 8f * sc);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Math.Max(1, w - tx), h), t.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
