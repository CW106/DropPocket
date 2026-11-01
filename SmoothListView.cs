// 自繪清單：滑過時柔和的高亮、新項目滑入並閃一下
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DropPocket
{
    class SmoothListView : ListView
    {
        class ItemFx
        {
            public float Hover, HoverTarget;
            public double FlashStart = -1, SlideStart = -1;
        }

        const double HoverMs = 120, FlashMs = 1200, SlideMs = 350;
        const int SlideDist = 28;
        // TextRenderer 預設不理會 g.SetClip；不加 PreserveGraphicsClipping，滑入時文字會畫到磚塊外留下殘影
        const TextFormatFlags LineFlags = TextFormatFlags.Left | TextFormatFlags.NoPrefix
                                          | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
                                          | TextFormatFlags.PreserveGraphicsClipping;

        Theme palette = Theme.Default;
        public bool SlideFromRight = true;
        public event EventHandler NeedsAnimation;

        // 以項目的 Tag（PocketItem）為鍵，清單重建後動畫狀態不會遺失
        readonly Dictionary<object, ItemFx> fx = new Dictionary<object, ItemFx>();
        readonly List<object> settled = new List<object>();
        object hoverKey;
        double now;
        Font detailFont;

        public SmoothListView()
        {
            DoubleBuffered = true;
            OwnerDraw = true;
        }

        public Theme Palette
        {
            get { return palette; }
            set { palette = value ?? Theme.Default; Invalidate(); }
        }

        Font DetailFont
        {
            get
            {
                if (detailFont == null) detailFont = new Font(Font.FontFamily, 8f);
                return detailFont;
            }
        }

        ItemFx Fx(object key)
        {
            ItemFx f;
            if (!fx.TryGetValue(key, out f)) { f = new ItemFx(); fx[key] = f; }
            return f;
        }

        void RaiseNeedsAnimation()
        {
            var h = NeedsAnimation;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---------- 滑過高亮 ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ListViewItem it = HitItem(e.Location);
            SetHover(it == null ? null : it.Tag);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHover(null);
        }

        public void ClearHover() { SetHover(null); }

        void SetHover(object key)
        {
            if (key == hoverKey) return;
            if (hoverKey != null) Fx(hoverKey).HoverTarget = 0f;
            if (key != null) Fx(key).HoverTarget = 1f;
            hoverKey = key;
            RaiseNeedsAnimation();
        }

        // 磚塊檢視的 GetItemAt 只認圖示和文字，空白處改用整塊範圍判斷
        ListViewItem HitItem(Point p)
        {
            ListViewItem it = GetItemAt(p.X, p.Y);
            if (it != null) return it;
            foreach (ListViewItem lvi in Items)
                if (lvi.Bounds.Contains(p)) return lvi;
            return null;
        }

        // ---------- 新項目 ----------

        public void AnimateNew(IEnumerable<object> keys, double t)
        {
            now = t;
            var alive = AliveKeys();
            foreach (object k in keys)
            {
                if (k == null || !alive.Contains(k)) continue;
                ItemFx f = Fx(k);
                f.FlashStart = t;
                f.SlideStart = t;
            }
            Invalidate();
            RaiseNeedsAnimation();
        }

        // 清單重建後，移除已經不在清單裡的動畫狀態
        public void Sync()
        {
            var alive = AliveKeys();
            var dead = new List<object>();
            foreach (object k in fx.Keys) if (!alive.Contains(k)) dead.Add(k);
            foreach (object k in dead) fx.Remove(k);
            if (hoverKey != null && !alive.Contains(hoverKey)) hoverKey = null;
        }

        HashSet<object> AliveKeys()
        {
            var alive = new HashSet<object>();
            foreach (ListViewItem lvi in Items) if (lvi.Tag != null) alive.Add(lvi.Tag);
            return alive;
        }

        // 每一幀由口袋的動畫迴圈呼叫；只重畫有變化的項目
        public bool Step(double t, double dt)
        {
            now = t;
            if (fx.Count == 0) return false;
            bool busy = false;
            foreach (ListViewItem lvi in Items)
            {
                ItemFx f;
                if (lvi.Tag == null || !fx.TryGetValue(lvi.Tag, out f)) continue;
                bool dirty = false, sliding = false;
                if (f.Hover != f.HoverTarget)
                {
                    float d = (float)(dt / HoverMs);
                    f.Hover = f.HoverTarget > f.Hover ? Math.Min(f.HoverTarget, f.Hover + d) : Math.Max(f.HoverTarget, f.Hover - d);
                    dirty = true;
                    if (f.Hover != f.HoverTarget) busy = true;
                }
                if (f.FlashStart >= 0)
                {
                    dirty = true;
                    if (t - f.FlashStart >= FlashMs) f.FlashStart = -1; else busy = true;
                }
                if (f.SlideStart >= 0)
                {
                    dirty = sliding = true;
                    if (t - f.SlideStart >= SlideMs) f.SlideStart = -1; else busy = true;
                }
                // 滑動中（含最後一幀）連兩側一起重畫：ImageList.Draw 不受 SetClip 限制，畫出磚塊外的部分才會被清掉
                if (dirty && IsHandleCreated)
                    Invalidate(sliding ? Rectangle.Inflate(lvi.Bounds, Gfx.S(SlideDist), 0) : lvi.Bounds);
                if (f.Hover == 0f && f.HoverTarget == 0f && f.FlashStart < 0 && f.SlideStart < 0) settled.Add(lvi.Tag);
            }
            // 已經完全靜止的項目不用再追蹤
            foreach (object k in settled) fx.Remove(k);
            settled.Clear();
            return busy;
        }

        // ---------- 繪製 ----------

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            try { PaintItem(e.Graphics, e.Item, e.Bounds); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        void PaintItem(Graphics g, ListViewItem lvi, Rectangle b)
        {
            if (b.Width <= 0 || b.Height <= 0) return;
            using (var bg = new SolidBrush(palette.Background)) g.FillRectangle(bg, b);

            ItemFx f = null;
            if (lvi.Tag != null) fx.TryGetValue(lvi.Tag, out f);
            float hover = 0f, flash = 0f, slide = 0f;
            if (f != null)
            {
                hover = (float)Ease.InOutCubic(f.Hover);
                if (f.FlashStart >= 0)
                {
                    float p = 1f - (float)Ease.Clamp01((now - f.FlashStart) / FlashMs);
                    flash = p * p;
                }
                if (f.SlideStart >= 0) slide = 1f - (float)Ease.OutCubic((now - f.SlideStart) / SlideMs);
            }
            int dx = (int)Math.Round(slide * Gfx.S(SlideDist)) * (SlideFromRight ? 1 : -1);

            g.SetClip(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var hl = new Rectangle(b.X + Gfx.S(4) + dx, b.Y + Gfx.S(2), b.Width - Gfx.S(8), b.Height - Gfx.S(4));
            if (hl.Width > 0 && hl.Height > 0 && (lvi.Selected || hover > 0.001f || flash > 0.001f))
            {
                using (var path = Gfx.RoundedRect(hl, Gfx.S(8)))
                {
                    if (lvi.Selected)
                        using (var br = new SolidBrush(Ease.Lerp(palette.Select, palette.SelectHover, hover))) g.FillPath(br, path);
                    else if (hover > 0.001f)
                        using (var br = new SolidBrush(Color.FromArgb((int)(255 * hover), palette.Hover))) g.FillPath(br, path);
                    if (flash > 0.001f)
                        using (var br = new SolidBrush(Color.FromArgb((int)(140 * flash), palette.Accent))) g.FillPath(br, path);
                }
            }

            ImageList il = LargeImageList;
            int iconSize = il != null ? il.ImageSize.Width : Gfx.S(32);
            int ix = b.X + Gfx.S(12) + dx, iy = b.Y + (b.Height - iconSize) / 2;
            if (il != null)
            {
                // 用 ImageList.Draw 直接畫；Images[i] 每次都會複製一張點陣圖
                int idx = !string.IsNullOrEmpty(lvi.ImageKey) ? il.Images.IndexOfKey(lvi.ImageKey) : lvi.ImageIndex;
                if (idx >= 0 && idx < il.Images.Count) il.Draw(g, ix, iy, idx);
            }

            int tx = ix + iconSize + Gfx.S(10), tw = b.Right - Gfx.S(10) + dx - tx;
            if (tw > 0)
            {
                Font nf = Font, df = DetailFont;
                int nh = nf.Height, dh = df.Height, gap = Gfx.S(1);
                int ty = b.Y + (b.Height - nh - gap - dh) / 2;
                string detail = lvi.SubItems.Count > 1 ? lvi.SubItems[1].Text : "";
                TextRenderer.DrawText(g, lvi.Text, nf, new Rectangle(tx, ty, tw, nh), lvi.ForeColor,
                    LineFlags | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, detail, df, new Rectangle(tx, ty + nh + gap, tw, dh), palette.Sub,
                    LineFlags | TextFormatFlags.PathEllipsis);
            }
            g.ResetClip();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            if (detailFont != null) { detailFont.Dispose(); detailFont = null; }
            base.OnFontChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && detailFont != null) { detailFont.Dispose(); detailFont = null; }
            base.Dispose(disposing);
        }
    }
}
