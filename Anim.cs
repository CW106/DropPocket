// 動畫與繪圖的小工具
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DropPocket
{
    static class Ease
    {
        public static double Clamp01(double t) { return t < 0 ? 0 : (t > 1 ? 1 : t); }

        public static double OutCubic(double t) { t = Clamp01(t); double u = 1 - t; return 1 - u * u * u; }

        public static double InCubic(double t) { t = Clamp01(t); return t * t * t; }

        public static double InOutCubic(double t)
        {
            t = Clamp01(t);
            if (t < 0.5) return 4 * t * t * t;
            double u = -2 * t + 2;
            return 1 - u * u * u / 2;
        }

        public static double OutBack(double t)
        {
            t = Clamp01(t);
            const double c1 = 1.70158, c3 = c1 + 1;
            double u = t - 1;
            return 1 + c3 * u * u * u + c1 * u * u;
        }

        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        public static int Lerp(int a, int b, float t) { return (int)Math.Round(a + (b - a) * (double)t); }

        public static RectangleF Lerp(RectangleF a, RectangleF b, float t)
        {
            return new RectangleF(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Width, b.Width, t), Lerp(a.Height, b.Height, t));
        }

        public static Rectangle Lerp(Rectangle a, Rectangle b, float t)
        {
            return new Rectangle(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Width, b.Width, t), Lerp(a.Height, b.Height, t));
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(Lerp(a.A, b.A, t), Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
        }

        // 指數平滑（與幀率無關）；差距夠小就直接對齊，動畫才會真的停下來
        public static float Approach(float v, float target, double dt, double tau)
        {
            float diff = target - v;
            if (Math.Abs(diff) < 0.001f || tau <= 0) return target;
            return v + diff * (float)(1 - Math.Exp(-dt / tau));
        }

        public static double Approach(double v, double target, double dt, double tau)
        {
            double diff = target - v;
            if (Math.Abs(diff) < 0.001 || tau <= 0) return target;
            return v + diff * (1 - Math.Exp(-dt / tau));
        }
    }

    static class Gfx
    {
        public static float Scale = 1f;

        public static int S(float v) { return (int)Math.Round(v * Scale); }

        // 圓角矩形；尺寸太小時退化成直角或空路徑，避免 AddArc 丟例外
        public static GraphicsPath RoundedRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
            if (rad < 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Pen RoundPen(Color c, float width)
        {
            return new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        }

        // 口袋圖示：開口朝上的托盤＋內側凹槽線（以 24 為一格的座標設計）
        public static void DrawTray(Graphics g, Pen pen, float cx, float cy, float size)
        {
            if (size < 4) return;
            float u = size / 24f, ox = cx - size / 2f, oy = cy - size / 2f;
            using (var p = new GraphicsPath())
            {
                p.StartFigure();
                p.AddLine(ox + 3 * u, oy + 8 * u, ox + 3 * u, oy + 17 * u);
                p.AddArc(ox + 3 * u, oy + 14 * u, 6 * u, 6 * u, 180, -90);
                p.AddLine(ox + 6 * u, oy + 20 * u, ox + 18 * u, oy + 20 * u);
                p.AddArc(ox + 15 * u, oy + 14 * u, 6 * u, 6 * u, 90, -90);
                p.AddLine(ox + 21 * u, oy + 17 * u, ox + 21 * u, oy + 8 * u);
                p.StartFigure();
                p.AddLines(new[]
                {
                    new PointF(ox + 3 * u, oy + 13 * u), new PointF(ox + 8 * u, oy + 13 * u),
                    new PointF(ox + 9.5f * u, oy + 15.5f * u), new PointF(ox + 14.5f * u, oy + 15.5f * u),
                    new PointF(ox + 16 * u, oy + 13 * u), new PointF(ox + 21 * u, oy + 13 * u),
                });
                g.DrawPath(pen, p);
            }
        }

        public static void DrawDownArrow(Graphics g, Pen pen, float cx, float tipY, float length, float head)
        {
            if (length < 1) return;
            g.DrawLine(pen, cx, tipY - length, cx, tipY);
            g.DrawLines(pen, new[] { new PointF(cx - head, tipY - head), new PointF(cx, tipY), new PointF(cx + head, tipY - head) });
        }

        public static void DrawChevron(Graphics g, Pen pen, float cx, float cy, float half, bool pointLeft)
        {
            if (half < 1) return;
            float w = half * 0.8f;
            float tip = pointLeft ? cx - w / 2 : cx + w / 2, back = pointLeft ? cx + w / 2 : cx - w / 2;
            g.DrawLines(pen, new[] { new PointF(back, cy - half), new PointF(tip, cy), new PointF(back, cy + half) });
        }

        // 依進度（0..1）畫出勾勾，做出一筆畫出來的效果
        public static void DrawCheck(Graphics g, Pen pen, PointF a, PointF b, PointF c, float progress)
        {
            if (progress <= 0) return;
            float l1 = Dist(a, b), l2 = Dist(b, c);
            if (l1 < 0.5f || l2 < 0.5f) return;
            float len = (l1 + l2) * Math.Min(1f, progress);
            if (len <= l1)
            {
                if (len >= 0.5f) g.DrawLine(pen, a, Along(a, b, len / l1));
            }
            else g.DrawLines(pen, new[] { a, b, Along(b, c, (len - l1) / l2) });
        }

        static float Dist(PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static PointF Along(PointF a, PointF b, float t)
        {
            return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }
    }

    static class Diag
    {
        public static string LogFile;
        static int logged;

        // 繪圖 / 計時器裡的例外只記前幾筆，避免每一幀都寫檔
        public static void Log(Exception ex)
        {
            if (LogFile == null || logged >= 5) return;
            logged++;
            try { File.AppendAllText(LogFile, DateTime.Now + " " + ex + Environment.NewLine); } catch { }
        }
    }

    // 自繪控制項的基底：雙緩衝、不能取得焦點（口袋視窗不可以搶走目前視窗的焦點）
    class PaintBox : Control
    {
        public PaintBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }
    }

    // 只畫一行，放不下就用「…」截斷（一般 Label 會自動換行，標題被按鈕擠窄時會變兩行）
    class SingleLineLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var r = new Rectangle(Padding.Left, 0, Math.Max(0, Width - Padding.Horizontal), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, r, ForeColor,
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter
                | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
    }
}
