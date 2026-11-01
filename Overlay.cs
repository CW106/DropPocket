// 拖曳提示層與空口袋提示
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DropPocket
{
    // 別的程式把東西拖到展開的口袋上方時，蓋在清單上的提示層
    class DropOverlay : PaintBox
    {
        enum Mode { Hidden, Hover, Leaving, Success }

        const double SuccessMs = 550;
        const TextFormatFlags CenterLine = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix
                                           | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        Theme palette = Theme.Default;
        public event EventHandler SuccessDone;

        public Theme Palette
        {
            get { return palette; }
            set { palette = value ?? Theme.Default; Invalidate(); }
        }

        Mode mode = Mode.Hidden;
        double now, modeStart;
        float fade;
        string subLine = "";
        int addedCount;
        readonly Font mainFont, subFont;
        readonly Pen dashPen;

        public DropOverlay(Font baseFont)
        {
            mainFont = new Font(baseFont.FontFamily, 11f, FontStyle.Bold);
            subFont = new Font(baseFont.FontFamily, 8.5f);
            dashPen = new Pen(Theme.Default.Accent, Math.Max(1.5f, 2f * Gfx.Scale));
            dashPen.DashPattern = new float[] { 4f, 3f };
            Visible = false;
        }

        public bool Active { get { return mode != Mode.Hidden; } }

        public void ShowHover(string sub, double t)
        {
            subLine = sub ?? "";
            if (mode == Mode.Hidden) fade = 0f;
            mode = Mode.Hover;
            modeStart = t;
            now = t;
            Reveal();
        }

        public void ShowSuccess(int count, double t)
        {
            addedCount = count;
            mode = Mode.Success;
            modeStart = t;
            now = t;
            Reveal();
        }

        // 拖曳離開或放下失敗：淡出（成功動畫進行中就讓它跑完）
        public void Dismiss()
        {
            if (mode == Mode.Hover) mode = Mode.Leaving;
        }

        void Reveal()
        {
            if (!Visible) Visible = true;
            BringToFront();
            Invalidate();
        }

        void HideNow()
        {
            mode = Mode.Hidden;
            fade = 0f;
            Visible = false;
        }

        // 每一幀由口袋的動畫迴圈呼叫；回傳 true 表示還在動
        public bool Step(double t, double dt)
        {
            now = t;
            switch (mode)
            {
                case Mode.Hover:
                    fade = Ease.Approach(fade, 1f, dt, 45);
                    Invalidate();
                    return true;
                case Mode.Leaving:
                    fade -= (float)(dt / 110.0);
                    if (fade <= 0f) { HideNow(); return false; }
                    Invalidate();
                    return true;
                case Mode.Success:
                    if (t - modeStart >= SuccessMs)
                    {
                        // 先讓清單開始新項目動畫，再把提示層拿掉
                        var h = SuccessDone;
                        if (h != null) h(this, EventArgs.Empty);
                        HideNow();
                        return false;
                    }
                    fade = Ease.Approach(fade, 1f, dt, 45);
                    Invalidate();
                    return true;
            }
            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { Draw(e.Graphics); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        void Draw(Graphics g)
        {
            g.Clear(palette.Background);
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 8 || h < 8) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            double ts = mode == Mode.Success ? now - modeStart : 0;
            float a = fade;
            if (mode == Mode.Success) a *= 1f - (float)Ease.Clamp01(ts / 140.0);
            float cx = w / 2f, cy = h / 2f;

            if (a > 0.01f)
            {
                float inset = Gfx.S(12);
                var box = new RectangleF(inset, inset, w - 2 * inset, h - 2 * inset);
                if (box.Width > 4 && box.Height > 4)
                {
                    double pulse = 0.5 + 0.5 * Math.Sin(now * 2 * Math.PI / 1400.0);
                    using (var path = Gfx.RoundedRect(box, Gfx.S(12)))
                    {
                        using (var b = new SolidBrush(Color.FromArgb((int)((18 + 20 * pulse) * a), palette.Accent)))
                            g.FillPath(b, path);
                        dashPen.Color = Color.FromArgb((int)(230 * a), palette.Accent);
                        dashPen.DashOffset = (float)(now / 70.0 % 7.0);
                        g.DrawPath(dashPen, path);
                    }
                }

                float trayY = cy - Gfx.S(20);
                using (var pen = Gfx.RoundPen(Color.FromArgb((int)(255 * a), palette.Accent), Math.Max(2f, 3f * Gfx.Scale)))
                {
                    Gfx.DrawTray(g, pen, cx, trayY, Gfx.S(60));
                    float bob = (float)(0.5 - 0.5 * Math.Cos(now * 2 * Math.PI / 1000.0)) * Gfx.S(7);
                    Gfx.DrawDownArrow(g, pen, cx, trayY - Gfx.S(10) + bob, Gfx.S(20), Gfx.S(7));
                }

                int mh = mainFont.Height, sh = subFont.Height;
                int ty = (int)(trayY + Gfx.S(34));
                TextRenderer.DrawText(g, "放開即可放進口袋", mainFont, new Rectangle(0, ty, w, mh),
                    Ease.Lerp(palette.Background, palette.Text, a), CenterLine);
                if (subLine.Length > 0)
                    TextRenderer.DrawText(g, subLine, subFont, new Rectangle(Gfx.S(24), ty + mh + Gfx.S(4), Math.Max(1, w - Gfx.S(48)), sh),
                        Ease.Lerp(palette.Background, palette.Sub, a), CenterLine | TextFormatFlags.EndEllipsis);
            }

            if (mode == Mode.Success) DrawSuccess(g, ts, w, cx, cy);
        }

        void DrawSuccess(Graphics g, double ts, int w, float cx, float cy)
        {
            float end = 1f - (float)Ease.Clamp01((ts - 420) / 130.0);
            if (end <= 0f) return;
            float ccy = cy - Gfx.S(12);
            float r = Gfx.S(26) * (float)Ease.OutBack(ts / 300.0);
            if (r > 1f)
            {
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * end), palette.Accent)))
                    g.FillEllipse(b, cx - r, ccy - r, r * 2, r * 2);
                float prog = (float)Ease.InOutCubic((ts - 110) / 230.0);
                if (prog > 0f)
                    using (var pen = Gfx.RoundPen(Color.FromArgb((int)(255 * end), palette.OnAccent), Math.Max(2f, 3.2f * Gfx.Scale)))
                        Gfx.DrawCheck(g, pen,
                            new PointF(cx - 0.40f * r, ccy + 0.02f * r),
                            new PointF(cx - 0.10f * r, ccy + 0.31f * r),
                            new PointF(cx + 0.42f * r, ccy - 0.28f * r), prog);
            }
            float ta = (float)Ease.Clamp01((ts - 90) / 150.0) * end;
            if (ta > 0.01f)
                TextRenderer.DrawText(g, "已放進 " + addedCount + " 項", mainFont,
                    new Rectangle(0, (int)(ccy + Gfx.S(38)), w, mainFont.Height), Ease.Lerp(palette.Background, palette.Text, ta), CenterLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                mainFont.Dispose();
                subFont.Dispose();
                dashPen.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // 口袋是空的時候顯示的提示（靜態）
    class EmptyState : PaintBox
    {
        static readonly string[] Lines = { "把檔案拖到這裡", "", "切換到目標視窗後", "再從這裡拖出去" };

        Theme palette = Theme.Default;

        public Theme Palette
        {
            get { return palette; }
            set { palette = value ?? Theme.Default; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { Draw(e.Graphics); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        void Draw(Graphics g)
        {
            g.Clear(palette.Background);
            int w = ClientSize.Width, h = ClientSize.Height;
            float inset = Gfx.S(12);
            var box = new RectangleF(inset, inset, w - 2 * inset, h - 2 * inset);
            if (box.Width < 8 || box.Height < 8) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var path = Gfx.RoundedRect(box, Gfx.S(12)))
            using (var pen = new Pen(Color.FromArgb(70, palette.Sub), Math.Max(1f, 1.2f * Gfx.Scale)))
            {
                pen.DashPattern = new float[] { 4f, 4f };
                g.DrawPath(pen, path);
            }

            int lineH = Font.Height;
            float glyph = Gfx.S(48), gap = Gfx.S(12);
            float top = (h - (glyph + gap + lineH * Lines.Length)) / 2f;
            using (var pen = Gfx.RoundPen(Color.FromArgb(150, palette.Sub), Math.Max(1.5f, 2.2f * Gfx.Scale)))
                Gfx.DrawTray(g, pen, w / 2f, top + glyph / 2f, glyph);

            int y = (int)(top + glyph + gap);
            for (int i = 0; i < Lines.Length; i++)
            {
                if (Lines[i].Length > 0)
                    TextRenderer.DrawText(g, Lines[i], Font, new Rectangle(0, y, w, lineH),
                        i == 0 ? Ease.Lerp(palette.Sub, palette.Text, 0.45f) : palette.Sub,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                y += lineH;
            }
        }
    }
}
