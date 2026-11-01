// 暫存口袋 DropPocket
// 拖著檔案時，目標視窗被擋住？先丟進螢幕邊緣的小口袋，切換好視窗再拖出來。
// 編譯：執行同資料夾的 build.bat（使用 Windows 內建的 .NET Framework csc，免安裝）

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using ComStream = System.Runtime.InteropServices.ComTypes.IStream;
using FORMATETC = System.Runtime.InteropServices.ComTypes.FORMATETC;
using STGMEDIUM = System.Runtime.InteropServices.ComTypes.STGMEDIUM;
using TYMED = System.Runtime.InteropServices.ComTypes.TYMED;
using DVASPECT = System.Runtime.InteropServices.ComTypes.DVASPECT;

namespace DropPocket
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "DropPocket.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("暫存口袋已經在執行中（看看螢幕邊緣或系統匣）。", "暫存口袋");
                    return;
                }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PocketForm());
            }
        }
    }

    class PocketItem
    {
        public string FilePath;   // null 表示這是一段文字
        public string Text;

        public bool IsText { get { return FilePath == null; } }
        public bool Exists { get { return IsText || File.Exists(FilePath) || Directory.Exists(FilePath); } }

        public string Name
        {
            get
            {
                if (IsText)
                {
                    string t = Text.Replace("\r", " ").Replace("\n", " ").Trim();
                    return t.Length > 60 ? t.Substring(0, 60) + "…" : t;
                }
                string n = Path.GetFileName(FilePath.TrimEnd('\\'));
                return string.IsNullOrEmpty(n) ? FilePath : n;
            }
        }

        public string Detail
        {
            get
            {
                if (IsText) return "文字";
                return Path.GetDirectoryName(FilePath.TrimEnd('\\')) ?? FilePath;
            }
        }
    }

    static partial class Native
    {
        public const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3, WM_HOTKEY = 0x312;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr h);
        [DllImport("ole32.dll")] public static extern void ReleaseStgMedium(ref STGMEDIUM m);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO psfi, uint cb, uint flags);
        const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;

        public static Bitmap GetIcon(string path, bool byExtensionOnly)
        {
            var info = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_LARGEICON | (byExtensionOnly ? SHGFI_USEFILEATTRIBUTES : 0);
            IntPtr r = SHGetFileInfo(path, 0x80, ref info, (uint)Marshal.SizeOf(info), flags);
            if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try { using (var ic = Icon.FromHandle(info.hIcon)) return ic.ToBitmap(); }
            finally { DestroyIcon(info.hIcon); }
        }
    }

    // 讀取「虛擬檔案」(FileGroupDescriptorW + FileContents)：瀏覽器圖片、壓縮檔內的檔案、郵件附件等
    static class VirtualFiles
    {
        const int DescriptorSize = 592;

        public static List<string> Extract(IDataObject data, string destRoot)
        {
            var topLevel = new List<string>();
            var ms = data.GetData("FileGroupDescriptorW") as MemoryStream;
            var com = data as ComDataObject;
            if (ms == null || com == null) return topLevel;

            byte[] buf = ms.ToArray();
            int count = BitConverter.ToInt32(buf, 0);
            short cf = (short)DataFormats.GetFormat("FileContents").Id;

            for (int i = 0; i < count; i++)
            {
                int off = 4 + i * DescriptorSize;
                if (off + DescriptorSize > buf.Length) break;
                uint flags = BitConverter.ToUInt32(buf, off);
                uint attrs = BitConverter.ToUInt32(buf, off + 36);
                long size = -1;
                if ((flags & 0x40) != 0) // FD_FILESIZE
                    size = ((long)BitConverter.ToUInt32(buf, off + 64) << 32) | BitConverter.ToUInt32(buf, off + 68);
                string raw = Encoding.Unicode.GetString(buf, off + 72, 520);
                int z = raw.IndexOf('\0');
                string name = Sanitize(z >= 0 ? raw.Substring(0, z) : raw);
                if (name.Length == 0) continue;

                string target = Path.Combine(destRoot, name);
                bool isDir = (flags & 0x4) != 0 && (attrs & 0x10) != 0;
                if (isDir)
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    byte[] content = ReadContents(com, cf, i, size);
                    if (content == null) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, content);
                }

                string top = Path.Combine(destRoot, name.Split('\\')[0]);
                if (!topLevel.Contains(top, StringComparer.OrdinalIgnoreCase)) topLevel.Add(top);
            }
            return topLevel;
        }

        // 拖曳預覽用：和 Extract 一樣只算最上層的名稱（不分大小寫去重），資料夾裡的檔案不另外算
        public static List<string> TopLevelNames(byte[] buf, out bool hasFolder)
        {
            hasFolder = false;
            var tops = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (buf == null || buf.Length < 4) return tops;
            int count = BitConverter.ToInt32(buf, 0);
            for (int i = 0; i < count; i++)
            {
                int off = 4 + i * DescriptorSize;
                if (off + DescriptorSize > buf.Length) break;
                uint flags = BitConverter.ToUInt32(buf, off);
                uint attrs = BitConverter.ToUInt32(buf, off + 36);
                string raw = Encoding.Unicode.GetString(buf, off + 72, 520);
                int z = raw.IndexOf('\0');
                string name = Sanitize(z >= 0 ? raw.Substring(0, z) : raw);
                if (name.Length == 0) continue;
                // 有子項目、或本身就是資料夾，它的最上層就是資料夾
                if (name.IndexOf('\\') >= 0 || ((flags & 0x4) != 0 && (attrs & 0x10) != 0)) hasFolder = true;
                string top = name.Split('\\')[0];
                if (seen.Add(top)) tops.Add(top);
            }
            return tops;
        }

        static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var parts = name.Split('\\', '/')
                .Where(p => p.Length > 0 && p != "." && p != "..")
                .Select(p => new string(p.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()));
            return string.Join("\\", parts);
        }

        static byte[] ReadContents(ComDataObject com, short cf, int index, long size)
        {
            var fmt = new FORMATETC();
            fmt.cfFormat = cf;
            fmt.dwAspect = DVASPECT.DVASPECT_CONTENT;
            fmt.lindex = index;
            fmt.ptd = IntPtr.Zero;
            fmt.tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL;

            STGMEDIUM med;
            try { com.GetData(ref fmt, out med); }
            catch { return null; }

            try
            {
                if (med.tymed == TYMED.TYMED_ISTREAM)
                {
                    var st = (ComStream)Marshal.GetObjectForIUnknown(med.unionmember);
                    IntPtr pRead = Marshal.AllocHGlobal(4);
                    try
                    {
                        var output = new MemoryStream();
                        byte[] chunk = new byte[1 << 16];
                        while (true)
                        {
                            st.Read(chunk, chunk.Length, pRead);
                            int n = Marshal.ReadInt32(pRead);
                            if (n <= 0) break;
                            output.Write(chunk, 0, n);
                        }
                        return output.ToArray();
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pRead);
                        Marshal.ReleaseComObject(st);
                    }
                }
                if (med.tymed == TYMED.TYMED_HGLOBAL)
                {
                    IntPtr p = Native.GlobalLock(med.unionmember);
                    try
                    {
                        long len = (long)Native.GlobalSize(med.unionmember).ToUInt64();
                        if (size >= 0 && size < len) len = size;
                        byte[] bytes = new byte[len];
                        Marshal.Copy(p, bytes, 0, (int)len);
                        return bytes;
                    }
                    finally { Native.GlobalUnlock(med.unionmember); }
                }
                return null;
            }
            finally { Native.ReleaseStgMedium(ref med); }
        }
    }

    // 配色、快捷鍵、隱藏小條和設定視窗的部分在 PocketSettings.cs
    partial class PocketForm : Form
    {
        const int HotkeyId = 0xD0C;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly float s;
        readonly List<PocketItem> items = new List<PocketItem>();
        readonly string dataDir, receivedDir, itemsFile, settingsFile;

        bool expanded, pinned, draggingOut, movingHeader, menuOpen, dragArmed;
        bool rightEdge = true, keepAfterDrag;
        double yFrac = 0.5;
        string screenName;
        int hoverTicks, outsideTicks, grabOffsetY;
        Point dragStart;

        Panel header, content, body;
        Label title, footer;
        Button btnAll, btnClear, btnPin;
        SmoothListView list;
        EmptyState emptyState;
        DropOverlay overlay;
        PaintBox rail;
        ImageList images;
        ToolTip toolTip;
        ContextMenuStrip itemMenu;
        NotifyIcon tray;
        ToolStripMenuItem miLeft, miRight, miKeep, miAutostart;
        Timer timer;
        Icon appIcon;

        // ---- 動畫狀態 ----
        // 視窗永遠是完整面板大小，收合 / 展開只改變 Region；openness 0 = 小條、1 = 整個面板
        const double MaxOpacity = 254.0 / 255.0; // 不設成 1.0，WinForms 才不會把分層樣式拿掉又加回來（會閃）
        readonly Stopwatch clock = Stopwatch.StartNew();
        Timer animTimer;
        double now, lastRaw;
        int railW, panelW;
        Rectangle workArea, regionRect;
        int regionRad = -1;
        float openness, openFrom;
        double openStart;
        float introFactor;
        float magnet, magnetTarget;
        bool cursorLeftDown;
        bool autoCollapsing; // 游標離開而自動收合中；收合途中游標回到面板上就反向展開
        double opacitySmooth = MaxOpacity, opacityApplied = -1;
        double popStart = -1;
        int badgeCount = -1;
        GraphicsPath badgePath;
        float badgeTextW;
        bool dropHover;
        double dragLeaveAt = -1;
        List<PocketItem> pendingNew;

        int S(int v) { return (int)Math.Round(v * s); }

        public PocketForm()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f;
            Gfx.Scale = s;
            railW = S(24);
            panelW = S(300);
            dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropPocket");
            receivedDir = Path.Combine(dataDir, "Received");
            itemsFile = Path.Combine(dataDir, "items.txt");
            settingsFile = Path.Combine(dataDir, "settings.ini");
            Directory.CreateDirectory(dataDir);
            Diag.LogFile = Path.Combine(dataDir, "error.log");

            Text = "暫存口袋";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = new Font("Microsoft JhengHei UI", 9f);
            DoubleBuffered = true;
            // 在視窗建立前就設成半透明，視窗一開始就是分層視窗，之後淡入不會閃
            Opacity = 0;
            appIcon = MakeIcon(S(32), theme);
            Icon = appIcon;

            animTimer = new Timer { Interval = 15 };
            animTimer.Tick += OnAnimTick;

            BuildUi();
            BuildTray();
            bool firstRun = !LoadSettings();
            LoadItems();
            CleanupReceived();
            WireDrop(this);
            RefreshList();
            UpdateMenuChecks();
            // 讀進來的配色與快捷鍵（介面先用預設配色建好）
            ApplyTheme();
            UpdateHotkeyTexts();
            RegisterHotkeys();
            SetExpanded(false);

            MouseClick += (o, e) => { if (!expanded) SetExpanded(true); };
            timer = new Timer { Interval = 120 };
            timer.Tick += OnTick;
            timer.Start();
            SystemEvents.DisplaySettingsChanged += (o, e) => ApplyBounds();

            if (firstRun)
                tray.ShowBalloonTip(6000, "暫存口袋已啟動",
                    "口袋藏在螢幕" + (rightEdge ? "右" : "左") + "側邊緣的小條。拖著檔案碰一下它就能放進去，切換好視窗再從口袋拖出來。",
                    ToolTipIcon.Info);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys();
        }

        protected override void WndProc(ref Message m)
        {
            // 點口袋時不要搶走目前視窗的焦點
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            if (m.Msg == Native.WM_HOTKEY && OnHotkey(m.WParam.ToInt32())) return;
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // 設定視窗先關（它關閉時會把快捷鍵註冊回去），之後才全部取消註冊
            if (settings != null && !settings.IsDisposed) Safe(settings.Close);
            animTimer.Stop();
            timer.Stop();
            UnregisterHotkeys();
            SaveItems();
            SaveSettings();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (animTimer != null) animTimer.Dispose();
                if (badgePath != null) { badgePath.Dispose(); badgePath = null; }
            }
            base.Dispose(disposing);
            if (disposing) { Native.FreeIcon(appIcon); appIcon = null; }
        }

        // ---------- 介面 ----------

        void BuildUi()
        {
            toolTip = new ToolTip();

            header = new Panel { Dock = DockStyle.Top, Height = S(36), BackColor = theme.Header };
            btnAll = MakeHeaderButton("全部拖出", "按住這裡拖曳，一次拖出口袋裡所有項目");
            btnClear = MakeHeaderButton("清空", "清空口袋（不會刪除任何檔案）");
            btnPin = MakeHeaderButton("釘選", "固定展開口袋（Ctrl+Alt+P）");
            title = new SingleLineLabel
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(S(8), 0, 0, 0),
                ForeColor = theme.Text,
                Font = new Font(Font, FontStyle.Bold),
                Cursor = Cursors.SizeAll,
                AutoEllipsis = true, // 標題太長就省略，右邊的按鈕不能被擠掉
                Text = "暫存口袋",
            };
            toolTip.SetToolTip(title, "拖這裡可以移動口袋（拖到螢幕另一側就換邊）");
            btnGear = MakeGearButton();
            header.Controls.Add(btnAll);
            header.Controls.Add(btnClear);
            header.Controls.Add(btnPin);
            header.Controls.Add(btnGear);
            header.Controls.Add(title);
            title.BringToFront();

            btnAll.MouseDown += (o, e) => { if (e.Button == MouseButtons.Left) { dragArmed = true; dragStart = Cursor.Position; } };
            btnAll.MouseUp += (o, e) => dragArmed = false;
            btnAll.MouseMove += (o, e) =>
            {
                if (!dragArmed) return;
                Point c = Cursor.Position;
                Size ds = SystemInformation.DragSize;
                if (Math.Abs(c.X - dragStart.X) > ds.Width || Math.Abs(c.Y - dragStart.Y) > ds.Height)
                {
                    dragArmed = false;
                    StartDragOut(items.ToList());
                }
            };
            btnClear.Click += (o, e) => ClearAll();
            btnPin.Click += (o, e) => { pinned = !pinned; UpdatePinButton(); };

            title.MouseDown += (o, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                movingHeader = true;
                grabOffsetY = Cursor.Position.Y - (Top + Height / 2);
            };
            title.MouseMove += (o, e) =>
            {
                if (!movingHeader) return;
                Point c = Cursor.Position;
                Screen sc = Screen.FromPoint(c);
                Rectangle wa = sc.WorkingArea;
                screenName = sc.DeviceName;
                rightEdge = c.X >= wa.Left + wa.Width / 2;
                yFrac = Math.Max(0, Math.Min(1, (double)(c.Y - grabOffsetY - wa.Top) / wa.Height));
                ApplyBounds();
            };
            title.MouseUp += (o, e) => { if (movingHeader) { movingHeader = false; SaveSettings(); UpdateMenuChecks(); } };

            itemMenu = new ContextMenuStrip();
            itemMenu.Items.Add("開啟", null, (o, e) => OpenSelected());
            itemMenu.Items.Add("在檔案總管中顯示", null, (o, e) => RevealSelected());
            itemMenu.Items.Add("複製路徑 / 文字", null, (o, e) => CopySelected());
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add("從口袋移除", null, (o, e) => RemoveItems(SelectedItems()));
            itemMenu.Opening += (o, e) => { if (list.SelectedItems.Count == 0) e.Cancel = true; };
            itemMenu.Opened += (o, e) => menuOpen = true;
            itemMenu.Closed += (o, e) => menuOpen = false;

            images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(S(32), S(32)) };
            list = new SmoothListView
            {
                Dock = DockStyle.Fill,
                View = View.Tile,
                BorderStyle = BorderStyle.None,
                BackColor = theme.Background,
                ForeColor = theme.Text,
                LargeImageList = images,
                MultiSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                ShowItemToolTips = true,
                ContextMenuStrip = itemMenu,
                // 寬度扣掉側邊條與捲軸，避免出現水平捲軸
                TileSize = new Size(panelW - railW - SystemInformation.VerticalScrollBarWidth - S(8), S(46)),
                Palette = theme,
            };
            list.NeedsAnimation += (o, e) => EnsureAnimating();
            list.Columns.Add("名稱");
            list.Columns.Add("位置");
            list.HandleCreated += (o, e) => ApplyListTheme();
            list.ItemDrag += (o, e) => StartDragOut(SelectedItems());
            list.MouseDoubleClick += (o, e) => OpenSelected();
            list.KeyDown += (o, e) => { if (e.KeyCode == Keys.Delete) RemoveItems(SelectedItems()); };

            emptyState = new EmptyState { Dock = DockStyle.Fill, Palette = theme };
            overlay = new DropOverlay(Font) { Dock = DockStyle.Fill, Palette = theme };
            overlay.SuccessDone += (o, e) =>
            {
                if (pendingNew != null) { list.AnimateNew(pendingNew, now); pendingNew = null; }
            };

            rail = new PaintBox { Dock = DockStyle.Right, Width = railW, BackColor = theme.Header };
            rail.Paint += PaintRail;
            rail.MouseClick += (o, e) => { if (!expanded) SetExpanded(true); };

            footer = new Label
            {
                Dock = DockStyle.Bottom,
                Height = S(26),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = theme.Sub,
                Font = new Font(Font.FontFamily, 8f),
                AutoEllipsis = true,
                Text = "雙擊開啟 · 右鍵更多 · Ctrl+Alt+P 釘選", // 實際文字由 UpdateHotkeyTexts 依快捷鍵設定
            };

            // 版面只排一次：側邊條貼著螢幕邊緣，其餘是內容；清單 / 空白提示 / 拖曳提示疊在 body 裡
            body = new Panel { Dock = DockStyle.Fill, BackColor = theme.Background };
            body.Controls.Add(list);
            body.Controls.Add(emptyState);
            body.Controls.Add(overlay);
            overlay.BringToFront();

            content = new Panel { Dock = DockStyle.Fill, BackColor = theme.Background };
            content.Controls.Add(body);
            content.Controls.Add(header);
            content.Controls.Add(footer);

            Controls.Add(content);
            Controls.Add(rail);
        }

        Button MakeHeaderButton(string text, string tip)
        {
            var b = new Button
            {
                Text = text,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Width = TextRenderer.MeasureText(text, Font).Width + S(14),
                Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            StyleHeaderButton(b);
            toolTip.SetToolTip(b, tip);
            return b;
        }

        void BuildTray()
        {
            var menu = new ContextMenuStrip();
            // 文字裡的快捷鍵由 UpdateHotkeyTexts 補上
            miToggle = new ToolStripMenuItem("顯示 / 隱藏口袋", null, (o, e) => Safe(OnToggleRequest));
            miHideTab = new ToolStripMenuItem("隱藏邊緣小條", null, (o, e) => Safe(() => SetTabHidden(!tabHidden)));
            menu.Items.Add(miToggle);
            menu.Items.Add(miHideTab);
            menu.Items.Add(new ToolStripSeparator());
            miLeft = new ToolStripMenuItem("停靠在左側", null, (o, e) => SetEdge(false));
            miRight = new ToolStripMenuItem("停靠在右側", null, (o, e) => SetEdge(true));
            miKeep = new ToolStripMenuItem("拖出後保留項目", null, (o, e) => { keepAfterDrag = !keepAfterDrag; SaveSettings(); UpdateMenuChecks(); });
            miAutostart = new ToolStripMenuItem("開機時自動啟動", null, (o, e) => ToggleAutostart());
            menu.Items.Add(miLeft);
            menu.Items.Add(miRight);
            menu.Items.Add(miKeep);
            menu.Items.Add(miAutostart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("設定…", null, (o, e) => ShowSettings());
            menu.Items.Add("開啟暫存資料夾", null, (o, e) => { Directory.CreateDirectory(receivedDir); Process.Start("explorer.exe", "\"" + receivedDir + "\""); });
            menu.Items.Add("結束", null, (o, e) => Close());

            tray = new NotifyIcon { Icon = appIcon, Text = "暫存口袋", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) Safe(OnToggleRequest); };
        }

        void UpdateMenuChecks()
        {
            miLeft.Checked = !rightEdge;
            miRight.Checked = rightEdge;
            miKeep.Checked = keepAfterDrag;
            miAutostart.Checked = IsAutostart();
            miHideTab.Checked = tabHidden;
            if (settings != null && !settings.IsDisposed) settings.SyncFromPocket();
        }

        void UpdatePinButton()
        {
            // 文字固定是「釘選」，靠主題色底表示已釘選；改字會變寬，把標題擠成兩行
            btnPin.BackColor = pinned ? theme.Accent : theme.Header;
            btnPin.ForeColor = pinned ? theme.OnAccent : theme.Text;
            // 釘選中滑過時不要變回灰底，主題色很亮、字是深色時會看不清楚
            btnPin.FlatAppearance.MouseOverBackColor = pinned ? theme.AccentHover : theme.ButtonHover;
            btnPin.FlatAppearance.MouseDownBackColor = pinned ? theme.AccentPress : theme.ButtonPress;
        }

        void SetEdge(bool right)
        {
            // 收合時換邊，讓小條從新的邊緣滑出來
            if (right != rightEdge && !expanded) { introP = 0; introFactor = 0f; }
            rightEdge = right;
            ApplyBounds();
            SaveSettings();
            UpdateMenuChecks();
            EnsureAnimating();
        }

        void TogglePinned()
        {
            pinned = !(pinned && expanded);
            UpdatePinButton();
            SetExpanded(pinned);
        }

        void SetExpanded(bool value)
        {
            if (value && tabHidden) return; // 小條藏起來時不展開，要先取消隱藏
            autoCollapsing = false;
            if (value != expanded)
            {
                // 從目前的開合程度接著動，中途反向也不會跳
                openFrom = openness;
                openStart = now;
            }
            expanded = value;
            if (value) magnetTarget = 0f;
            else list.ClearHover();
            hoverTicks = 0;
            outsideTicks = 0;
            ApplyBounds();
            EnsureAnimating();
        }

        void UpdateListVisibility()
        {
            list.Visible = items.Count > 0;
            emptyState.Visible = items.Count == 0;
        }

        Screen CurrentScreen()
        {
            foreach (var sc in Screen.AllScreens)
                if (sc.DeviceName == screenName) return sc;
            return Screen.PrimaryScreen;
        }

        // 視窗永遠是完整面板大小、貼在停靠位置；看得到多少由 Region 決定
        void ApplyBounds()
        {
            workArea = CurrentScreen().WorkingArea;
            int w = panelW;
            int h = Math.Min(S(430), workArea.Height);
            int x = rightEdge ? workArea.Right - w : workArea.Left;
            int cy = workArea.Top + (int)(yFrac * workArea.Height);
            int y = Math.Max(workArea.Top, Math.Min(workArea.Bottom - h, cy - h / 2));
            DockStyle side = rightEdge ? DockStyle.Right : DockStyle.Left;
            if (rail.Dock != side) rail.Dock = side;
            list.SlideFromRight = rightEdge;
            var nb = new Rectangle(x, y, w, h);
            if (Bounds != nb) Bounds = nb;
            UpdateRegion(false);
            rail.Invalidate();
        }

        // 收合時露出的小條（client 座標）：寬度從螢幕邊緣量起，垂直置中在使用者設定的位置並限制在工作區內。
        // 面板本身另外夾在工作區內，所以小條的 y 要獨立計算
        RectangleF TabRectF(float w, float h)
        {
            int cw = ClientSize.Width, ch = ClientSize.Height;
            h = Math.Max(0f, Math.Min(h, ch));
            float cy = workArea.Top + (float)(yFrac * workArea.Height);
            float top = Math.Max(workArea.Top, Math.Min(workArea.Bottom - h, cy - h / 2f));
            float y = Math.Max(0f, Math.Min(ch - h, top - Top));
            return new RectangleF(rightEdge ? cw - w : 0f, y, w, h);
        }

        RectangleF TabClientRectF()
        {
            float w = Ease.Lerp((float)S(16), (float)railW, magnet) * introFactor;
            w = Math.Max(0f, Math.Min(railW, w));
            return TabRectF(w, Ease.Lerp((float)S(110), (float)S(150), magnet));
        }

        Rectangle TabScreenRect()
        {
            Rectangle r = Rectangle.Round(TabClientRectF());
            r.Offset(Left, Top);
            return r;
        }

        // 沒有磁吸放大時的小條，用來量游標距離（避免放大後又影響距離）
        Rectangle RestingTabScreenRect()
        {
            Rectangle r = Rectangle.Round(TabRectF(S(16), S(110)));
            r.Offset(Left, Top);
            return r;
        }

        Rectangle VisibleScreenRect()
        {
            Rectangle r = regionRect;
            r.Offset(Left, Top);
            return r;
        }

        // Region 以外的地方不能點、也不能放，點擊和拖曳會直接穿過去
        void UpdateRegion(bool force)
        {
            var full = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
            Rectangle key = Rectangle.Round(Ease.Lerp(TabClientRectF(), full, openness));
            int rad = (int)Math.Round(Ease.Lerp((float)S(7), (float)S(12), openness));
            if (!force && Region != null && key == regionRect && rad == regionRad) return;
            regionRect = key;
            regionRad = rad;
            Region next;
            if (key.Width < 1 || key.Height < 1) next = new Region(Rectangle.Empty);
            else using (var path = Gfx.RoundedRect(key, rad)) next = new Region(path);
            Region old = Region;
            Region = next;
            if (old != null && !ReferenceEquals(old, next)) old.Dispose();
        }

        // ---------- 側邊條（收合時就是那個小條） ----------

        void PaintRail(object sender, PaintEventArgs e)
        {
            try { DrawRail(e.Graphics); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        void DrawRail(Graphics g)
        {
            int rw = rail.Width, rh = rail.Height;
            if (rw <= 0 || rh <= 0) return;
            float o = openness;
            bool hasItems = items.Count > 0;
            Theme th = theme;
            Color tabFill = hasItems ? Ease.Lerp(th.Accent, th.Glow, magnet * 0.2f) : Ease.Lerp(th.IdleTab, th.Accent, magnet * 0.65f);
            g.Clear(Ease.Lerp(tabFill, th.Header, o));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (o > 0.01f)
            {
                int lip = Math.Max(2, S(2));
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * o), th.Accent)))
                    g.FillRectangle(b, rightEdge ? rw - lip : 0, 0, lip, rh);
            }

            RectangleF tab = TabClientRectF();
            tab.Offset(-rail.Left, -rail.Top);
            float cx = Ease.Lerp(tab.X + tab.Width / 2f, rw / 2f, o);
            float cy = tab.Y + tab.Height / 2f;
            float pop = PopScale();

            if (hasItems)
            {
                float h = S(16) * pop;
                float w = Math.Max(h, (badgeTextW + S(8)) * pop);
                using (var path = Gfx.RoundedRect(new RectangleF(cx - w / 2f, cy - h / 2f, w, h), h / 2f))
                using (var b = new SolidBrush(Ease.Lerp(th.BadgeShade, th.Accent, o)))
                    g.FillPath(b, path);
                if (badgePath != null)
                {
                    GraphicsState st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.ScaleTransform(pop, pop);
                    using (var nb = new SolidBrush(th.OnAccent)) g.FillPath(nb, badgePath);
                    g.Restore(st);
                }
            }
            else
            {
                float r = S(2) * pop, gap = S(8) * pop;
                using (var b = new SolidBrush(Ease.Lerp(th.TabDots, Color.FromArgb(200, th.Sub), o)))
                    for (int i = -1; i <= 1; i++)
                        g.FillEllipse(b, cx - r, cy + i * gap - r, r * 2, r * 2);
            }

            float ca = ChevronAlpha();
            if (ca > 0.01f)
            {
                // 拖著東西靠近時，箭頭往面板方向輕輕擺動，提示「丟這裡」
                float bob = (float)(0.5 - 0.5 * Math.Cos(now * 2 * Math.PI / 900.0)) * S(3);
                float x = tab.X + tab.Width / 2f + (rightEdge ? -bob : bob);
                using (var pen = Gfx.RoundPen(Color.FromArgb((int)(235 * ca), th.OnAccent), Math.Max(1.5f, 2f * s)))
                {
                    Gfx.DrawChevron(g, pen, x, cy - S(32), S(4), rightEdge);
                    Gfx.DrawChevron(g, pen, x, cy + S(32), S(4), rightEdge);
                }
            }
        }

        float ChevronAlpha()
        {
            if (!cursorLeftDown || expanded || magnet <= 0.05f) return 0f;
            return magnet * (1f - openness);
        }

        // 數量改變時徽章彈一下
        float PopScale()
        {
            if (popStart < 0) return 1f;
            double p = (now - popStart) / 600.0;
            if (p < 0 || p >= 1) return 1f;
            return (float)(1 + 0.55 * Math.Exp(-6 * p) * Math.Cos(10 * p));
        }

        // 數字用 GraphicsPath 畫（已置中在原點），縮放時才會平滑
        void RebuildBadgePath()
        {
            if (badgePath != null) { badgePath.Dispose(); badgePath = null; }
            badgeTextW = 0;
            if (items.Count == 0) return;
            string text = items.Count > 99 ? "99+" : items.Count.ToString();
            float pt = text.Length >= 3 ? 6.5f : (text.Length == 2 ? 7.5f : 9f);
            var p = new GraphicsPath();
            try
            {
                FontFamily ff = Font.FontFamily;
                // 字型沒有粗體時 AddString 會丟例外，改用一般字重
                int style = (int)(ff.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular);
                p.AddString(text, ff, style, pt * 96f / 72f * s, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bb = p.GetBounds();
                using (var m = new Matrix())
                {
                    m.Translate(-(bb.X + bb.Width / 2f), -(bb.Y + bb.Height / 2f));
                    p.Transform(m);
                }
                badgePath = p;
                badgeTextW = bb.Width;
            }
            catch (Exception ex)
            {
                p.Dispose();
                Diag.Log(ex);
            }
        }

        // 自動展開 / 收合
        void OnTick(object sender, EventArgs e)
        {
            try { LogicTick(); }
            catch (Exception ex) { Diag.Log(ex); }
        }

        // 只決定目標（展開 / 收合、磁吸程度），實際的動畫交給動畫迴圈
        void LogicTick()
        {
            Point c = Cursor.Position;
            bool leftDown = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (leftDown != cursorLeftDown)
            {
                cursorLeftDown = leftDown;
                rail.Invalidate();
                EnsureAnimating();
            }
            if (tabHidden) { SetMagnetTarget(0f); return; }
            if (!expanded)
            {
                if (TabScreenRect().Contains(c) && !leftDown) hoverTicks++; else hoverTicks = 0;
                if (hoverTicks >= 3) { SetExpanded(true); return; }
                SetMagnetTarget(MagnetFor(c, leftDown));
                return;
            }
            Rectangle zone = Bounds;
            zone.Inflate(S(12), S(12));
            if (pinned || movingHeader || menuOpen || previewHold || zone.Contains(c)) outsideTicks = 0;
            else outsideTicks++;
            if (outsideTicks >= 5) { SetExpanded(false); autoCollapsing = true; }
        }

        // 游標越靠近小條就越「吸」；按著左鍵（大概正拖著東西）時感應範圍更大
        float MagnetFor(Point c, bool leftDown)
        {
            if (draggingOut) return 0f;
            Rectangle tab = RestingTabScreenRect();
            int dx = Math.Max(Math.Max(tab.Left - c.X, 0), c.X - (tab.Right - 1));
            int dy = Math.Max(Math.Max(tab.Top - c.Y, 0), c.Y - (tab.Bottom - 1));
            double d = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (leftDown && d < S(260)) return (float)Ease.OutCubic(1 - d / S(260));
            if (d < S(70)) return (float)(0.35 * (1 - d / S(70)));
            return 0f;
        }

        void SetMagnetTarget(float t)
        {
            if (Math.Abs(t - magnetTarget) < 0.001f) return;
            magnetTarget = t;
            EnsureAnimating();
        }

        // ---------- 動畫迴圈 ----------
        // 只在有東西在動時才跑，全部停好就自己停下（閒置時不耗 CPU）

        void EnsureAnimating()
        {
            if (animTimer == null || animTimer.Enabled) return;
            lastRaw = clock.Elapsed.TotalMilliseconds;
            animTimer.Start();
        }

        void OnAnimTick(object sender, EventArgs e)
        {
            double raw = clock.Elapsed.TotalMilliseconds;
            // 卡住一陣子（例如拖曳的模態迴圈）後不要一次跳到底
            double dt = Math.Max(0, Math.Min(50, raw - lastRaw));
            lastRaw = raw;
            now += dt;
            bool busy;
            try { busy = StepAnimations(dt); }
            catch (Exception ex) { Diag.Log(ex); busy = false; SnapToTargets(); }
            if (!busy) animTimer.Stop();
        }

        // 動畫出錯時直接跳到最終狀態，口袋至少要看得到、能用
        void SnapToTargets()
        {
            try
            {
                openness = expanded ? 1f : 0f;
                introP = showP = tabHidden ? 0 : 1;
                introFactor = (float)introP;
                magnet = magnetTarget;
                popStart = -1;
                opacitySmooth = (!expanded && items.Count == 0) ? 0.6 : MaxOpacity;
                opacityApplied = tabHidden ? 0 : opacitySmooth;
                Opacity = opacityApplied;
                UpdateRegion(true);
                rail.Invalidate();
                FinishHideIfDone();
            }
            catch { }
        }

        bool StepAnimations(double dt)
        {
            bool busy = false, railDirty = false, regionDirty = false;

            float o = OpennessAt(now);
            if (o != openness) { openness = o; railDirty = regionDirty = true; }
            if (autoCollapsing)
            {
                // 收合中的面板還看得到也還能點：游標回到上面就反向展開，不要在游標底下縮掉
                if (expanded || openness <= 0f) autoCollapsing = false;
                else if (openness > 0.02f && VisibleScreenRect().Contains(Cursor.Position)) SetExpanded(true);
            }
            if (openness != (expanded ? 1f : 0f)) busy = true;

            // 小條從邊緣滑出（啟動、換邊、取消隱藏）；隱藏時倒著播，滑回邊緣
            if (StepIntro(dt)) busy = true;
            float intro = introP >= 1 ? 1f : (float)Ease.OutBack(introP);
            if (intro != introFactor) { introFactor = intro; railDirty = regionDirty = true; }

            float m = Ease.Approach(magnet, magnetTarget, dt, 70);
            if (m != magnet) { magnet = m; railDirty = regionDirty = true; }
            if (magnet != magnetTarget) busy = true;

            if (ChevronAlpha() > 0.01f) { railDirty = true; busy = true; }

            if (popStart >= 0)
            {
                railDirty = true;
                if (now - popStart >= 600) popStart = -1; else busy = true;
            }

            if (StepOpacity(dt)) busy = true;

            if (dropHover && dragLeaveAt >= 0)
            {
                // DragLeave 之後一小段時間內沒有新的 DragEnter / DragOver，才算真的離開
                if (now - dragLeaveAt > 120) EndDropHover(); else busy = true;
            }
            if (overlay.Step(now, dt)) busy = true;
            if (list.Step(now, dt)) busy = true;

            if (regionDirty) UpdateRegion(false);
            if (railDirty) rail.Invalidate();
            FinishHideIfDone();
            return busy;
        }

        // 展開 260 ms OutCubic、收合 200 ms InCubic；時間依剩下的距離縮短
        float OpennessAt(double t)
        {
            float target = expanded ? 1f : 0f;
            double dur = expanded ? 260 * (1 - openFrom) : 200 * openFrom;
            if (dur < 1) return target;
            double p = Math.Min(1, (t - openStart) / dur);
            if (p >= 1) return target;
            if (expanded) return openFrom + (1 - openFrom) * (float)Ease.OutCubic(p);
            return openFrom * (1 - (float)Ease.InCubic(p));
        }

        // 啟動淡入；收合、空的、游標也不在附近時半透明
        bool StepOpacity(double dt)
        {
            double target = (!expanded && items.Count == 0 && magnet < 0.02f) ? 0.6 : MaxOpacity;
            opacitySmooth = Ease.Approach(opacitySmooth, target, dt, 90);
            // 啟動、取消隱藏時淡入，隱藏時淡出
            bool fading = StepShowFade(dt);
            double op = Math.Max(0, Math.Min(MaxOpacity, opacitySmooth * Ease.OutCubic(showP)));
            if ((int)(op * 255) != (int)(opacityApplied * 255))
            {
                Opacity = op;
                opacityApplied = op;
            }
            return opacitySmooth != target || fading;
        }

        // ---------- 拖進來 ----------

        void WireDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += OnDragEnterOver;
            c.DragOver += OnDragEnterOver;
            c.DragLeave += OnDragLeave;
            c.DragDrop += OnDragDrop;
            foreach (Control child in c.Controls) WireDrop(child);
        }

        void OnDragEnterOver(object sender, DragEventArgs e)
        {
            if (draggingOut || tabHidden) { e.Effect = DragDropEffects.None; return; }
            if (!expanded) SetExpanded(true);
            e.Effect = AcceptEffect(e);
            if (e.Effect == DragDropEffects.None) return;
            dragLeaveAt = -1;
            if (!dropHover)
            {
                dropHover = true;
                // 上一次「已放進」還沒播完就又拖東西進來：新項目動畫直接開始
                if (pendingNew != null) { list.AnimateNew(pendingNew, now); pendingNew = null; }
                overlay.ShowHover(DescribeDrag(e.Data), now);
                EnsureAnimating();
            }
        }

        // 游標在子控制項之間移動時也會收到 DragLeave + DragEnter，所以先記下時間，稍後才判定是否真的離開
        void OnDragLeave(object sender, EventArgs e)
        {
            if (!dropHover) return;
            dragLeaveAt = now;
            EnsureAnimating();
        }

        void EndDropHover()
        {
            dropHover = false;
            dragLeaveAt = -1;
            overlay.Dismiss();
        }

        // 拖曳提示的第二行：大略說明要放進來的東西
        static string DescribeDrag(IDataObject d)
        {
            try
            {
                var files = d.GetDataPresent(DataFormats.FileDrop) ? d.GetData(DataFormats.FileDrop) as string[] : null;
                if (files != null && files.Length > 0)
                {
                    string name = Path.GetFileName(files[0].TrimEnd('\\'));
                    if (string.IsNullOrEmpty(name)) name = files[0];
                    return files.Length + " 個項目 · " + Shorten(name, 28);
                }
                bool hasText = d.GetDataPresent(DataFormats.UnicodeText);
                var ms = d.GetDataPresent("FileGroupDescriptorW") ? d.GetData("FileGroupDescriptorW") as MemoryStream : null;
                if (ms != null && ms.Length >= 4)
                {
                    bool hasFolder;
                    List<string> tops = VirtualFiles.TopLevelNames(ms.ToArray(), out hasFolder);
                    // 和 AddFromData 一樣：瀏覽器連結附帶的單一 .url 捷徑最後會存成文字
                    if (tops.Count == 1 && hasText && tops[0].EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return "一段文字";
                    if (tops.Count > 0) return tops.Count + (hasFolder ? " 個項目" : " 個檔案");
                }
                if (hasText) return "一段文字";
            }
            catch { }
            return "";
        }

        static string Shorten(string text, int max)
        {
            if (text.Length <= max) return text;
            int tail = Math.Min(10, max / 3);
            return text.Substring(0, max - tail - 1) + "…" + text.Substring(text.Length - tail);
        }

        static DragDropEffects AcceptEffect(DragEventArgs e)
        {
            IDataObject d = e.Data;
            bool ok = d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent("FileGroupDescriptorW")
                      || d.GetDataPresent(DataFormats.UnicodeText);
            if (!ok) return DragDropEffects.None;
            // 只回報「複製 / 連結」，來源端就不會把原檔刪掉
            if ((e.AllowedEffect & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            if ((e.AllowedEffect & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            return DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            if (draggingOut) return;
            dropHover = false;
            dragLeaveAt = -1;
            List<PocketItem> added = null;
            try { added = AddFromData(e.Data); }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(dataDir, "error.log"), DateTime.Now + " " + ex + Environment.NewLine); } catch { }
                tray.ShowBalloonTip(4000, "暫存口袋", "無法放進口袋：" + ex.Message, ToolTipIcon.Warning);
            }
            if (added != null && added.Count > 0)
            {
                // 新項目的滑入動畫等「已放進」提示結束後才開始
                if (overlay.Active)
                {
                    if (pendingNew == null) pendingNew = new List<PocketItem>();
                    pendingNew.AddRange(added);
                    overlay.ShowSuccess(added.Count, now);
                }
                else list.AnimateNew(added, now);
            }
            else overlay.Dismiss();
            EnsureAnimating();
        }

        List<PocketItem> AddFromData(IDataObject d)
        {
            var added = new List<PocketItem>();
            var files = d.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                foreach (string f in files) added.Add(AddPath(f));
            }
            else
            {
                string text = d.GetDataPresent(DataFormats.UnicodeText) ? d.GetData(DataFormats.UnicodeText) as string : null;
                List<string> extracted = null;
                if (d.GetDataPresent("FileGroupDescriptorW"))
                {
                    string dest = Path.Combine(receivedDir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                    extracted = VirtualFiles.Extract(d, dest);
                    // 從瀏覽器拖連結會附帶一個 .url 捷徑，這種情況保留成文字就好
                    if (text != null && extracted.Count == 1 && extracted[0].EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(dest, true); } catch { }
                        extracted = null;
                    }
                }
                if (extracted != null && extracted.Count > 0)
                    foreach (string f in extracted) added.Add(AddPath(f));
                else if (!string.IsNullOrEmpty(text))
                    added.Add(AddText(text));
            }
            RefreshList();
            SaveItems();
            added.RemoveAll(i => !items.Contains(i)); // 同一次拖進重複的路徑，只算最後留下的那個
            return added;
        }

        PocketItem AddPath(string path)
        {
            items.RemoveAll(i => !i.IsText && string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase));
            var it = new PocketItem { FilePath = path };
            items.Insert(0, it);
            return it;
        }

        PocketItem AddText(string text)
        {
            items.RemoveAll(i => i.IsText && i.Text == text);
            var it = new PocketItem { Text = text };
            items.Insert(0, it);
            return it;
        }

        // ---------- 拖出去 ----------

        void StartDragOut(List<PocketItem> selection)
        {
            var valid = selection.Where(i => i.Exists).ToList();
            if (valid.Count == 0) { RefreshList(); return; }

            var data = new DataObject();
            var files = new StringCollection();
            var texts = new List<string>();
            foreach (var it in valid)
            {
                if (it.IsText) texts.Add(it.Text);
                else files.Add(it.FilePath);
            }
            if (files.Count > 0) data.SetFileDropList(files);
            if (texts.Count > 0) data.SetText(string.Join(Environment.NewLine, texts));

            DragDropEffects result;
            draggingOut = true;
            try
            {
                // 提供複製 / 移動 / 連結，由目標視窗決定（例如檔案總管同磁碟預設為移動，和平常拖曳一樣）
                result = list.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally { draggingOut = false; }

            var gone = valid.Where(i => !i.Exists).ToList();
            if (result != DragDropEffects.None && !keepAfterDrag) RemoveItems(valid);
            else if (gone.Count > 0) RemoveItems(gone);
        }

        // ---------- 項目操作 ----------

        List<PocketItem> SelectedItems()
        {
            var r = new List<PocketItem>();
            foreach (ListViewItem lvi in list.SelectedItems) r.Add((PocketItem)lvi.Tag);
            return r;
        }

        void RemoveItems(List<PocketItem> toRemove)
        {
            if (toRemove.Count == 0) return;
            items.RemoveAll(toRemove.Contains);
            RefreshList();
            SaveItems();
        }

        void ClearAll()
        {
            items.Clear();
            RefreshList();
            SaveItems();
        }

        void OpenSelected()
        {
            foreach (var it in SelectedItems())
            {
                if (it.IsText) { CopySelected(); return; }
                try { Process.Start(new ProcessStartInfo(it.FilePath) { UseShellExecute = true }); } catch { }
            }
        }

        void RevealSelected()
        {
            var it = SelectedItems().FirstOrDefault(i => !i.IsText);
            if (it != null) Process.Start("explorer.exe", "/select,\"" + it.FilePath + "\"");
        }

        void CopySelected()
        {
            var sel = SelectedItems();
            if (sel.Count == 0) return;
            try { Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(i => i.IsText ? i.Text : i.FilePath))); } catch { }
        }

        void RefreshList()
        {
            list.BeginUpdate();
            list.Items.Clear();
            if (items.Count == 0) images.Images.Clear();
            foreach (var it in items)
            {
                var lvi = new ListViewItem(it.Name, IconKey(it));
                bool exists = it.Exists;
                lvi.SubItems.Add(exists ? it.Detail : "（找不到了）");
                lvi.ToolTipText = it.IsText ? (it.Text.Length > 400 ? it.Text.Substring(0, 400) + "…" : it.Text) : it.FilePath;
                lvi.Tag = it;
                if (!exists) lvi.ForeColor = theme.Sub;
                list.Items.Add(lvi);
            }
            list.EndUpdate();
            list.Sync();
            title.Text = items.Count > 0 ? "暫存口袋 · " + items.Count : "暫存口袋";
            UpdateListVisibility();
            if (badgeCount != items.Count)
            {
                if (badgeCount >= 0) { popStart = now; EnsureAnimating(); }
                badgeCount = items.Count;
                RebuildBadgePath();
            }
            rail.Invalidate();
        }

        string IconKey(PocketItem it)
        {
            string key = it.IsText ? "::text" : it.FilePath.ToLowerInvariant();
            if (!images.Images.ContainsKey(key))
            {
                Bitmap bmp = it.IsText ? Native.GetIcon(".txt", true) : Native.GetIcon(it.FilePath, !it.Exists);
                if (bmp == null) bmp = new Bitmap(S(32), S(32));
                images.Images.Add(key, bmp);
            }
            return key;
        }

        // ---------- 儲存 ----------

        void LoadItems()
        {
            if (!File.Exists(itemsFile)) return;
            try
            {
                foreach (string line in File.ReadAllLines(itemsFile, Encoding.UTF8))
                {
                    if (line.StartsWith("F\t"))
                    {
                        var it = new PocketItem { FilePath = line.Substring(2) };
                        if (it.Exists) items.Add(it);
                    }
                    else if (line.StartsWith("T\t"))
                    {
                        items.Add(new PocketItem { Text = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(2))) });
                    }
                }
            }
            catch { }
        }

        void SaveItems()
        {
            try
            {
                File.WriteAllLines(itemsFile, items.Select(i => i.IsText
                    ? "T\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(i.Text))
                    : "F\t" + i.FilePath), Encoding.UTF8);
            }
            catch { }
        }

        bool LoadSettings()
        {
            if (!File.Exists(settingsFile)) return false;
            Color accent = Theme.DefaultAccent, bg = Theme.DefaultBackground;
            try
            {
                foreach (string line in File.ReadAllLines(settingsFile, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k == "edge") rightEdge = v != "left";
                    else if (k == "y")
                    {
                        double yv;
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out yv) && yv >= 0 && yv <= 1) yFrac = yv;
                    }
                    else if (k == "keep") keepAfterDrag = v == "1";
                    else if (k == "screen") screenName = v;
                    else if (k == "accent") { Color c; if (Theme.TryParseHex(v, out c)) accent = c; }
                    else if (k == "bg") { Color c; if (Theme.TryParseHex(v, out c)) bg = c; }
                    // 空白 = 不使用快捷鍵；格式不對就用預設
                    else if (k == "hotkey") toggleKey = HotkeyCombo.Parse(v, HotkeyCombo.DefaultToggle);
                    else if (k == "hidekey") hideKey = HotkeyCombo.Parse(v, HotkeyCombo.None);
                }
            }
            catch { }
            theme = new Theme(accent, bg);
            if (!hideKey.IsNone && hideKey.Equals(toggleKey)) hideKey = HotkeyCombo.None;
            return true;
        }

        void SaveSettings()
        {
            try
            {
                File.WriteAllLines(settingsFile, new[]
                {
                    "edge=" + (rightEdge ? "right" : "left"),
                    "y=" + yFrac.ToString("0.###", CultureInfo.InvariantCulture),
                    "keep=" + (keepAfterDrag ? "1" : "0"),
                    "screen=" + (screenName ?? ""),
                    "accent=" + Theme.ToHex(theme.Accent),
                    "bg=" + Theme.ToHex(theme.Background),
                    "hotkey=" + toggleKey.Serialize(),
                    "hidekey=" + hideKey.Serialize(),
                }, Encoding.UTF8);
            }
            catch { }
        }

        // 清掉三天前、而且已經不在口袋裡的暫存檔（瀏覽器圖片等虛擬檔案存在這裡）
        void CleanupReceived()
        {
            try
            {
                if (!Directory.Exists(receivedDir)) return;
                foreach (string dir in Directory.GetDirectories(receivedDir))
                {
                    bool used = items.Any(i => !i.IsText && i.FilePath.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase));
                    if (!used && Directory.GetCreationTime(dir) < DateTime.Now.AddDays(-3))
                        Directory.Delete(dir, true);
                }
            }
            catch { }
        }

        bool IsAutostart()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue("DropPocket") != null;
        }

        void ToggleAutostart()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key.GetValue("DropPocket") != null) key.DeleteValue("DropPocket", false);
                else key.SetValue("DropPocket", "\"" + Application.ExecutablePath + "\"");
            }
            UpdateMenuChecks();
        }

        // ---------- 繪圖小工具 ----------

        static GraphicsPath RoundedRect(RectangleF r, float rad)
        {
            return Gfx.RoundedRect(r, rad);
        }

        static Icon MakeIcon(int size, Theme th)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float m = size * 0.08f;
                    using (var path = RoundedRect(new RectangleF(m, m * 2, size - 2 * m, size - 3 * m), size * 0.2f))
                    using (var b = new SolidBrush(th.Accent))
                        g.FillPath(b, path);
                    using (var pen = new Pen(th.OnAccent, Math.Max(1.5f, size / 11f)))
                    {
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        g.DrawArc(pen, size * 0.26f, size * 0.12f, size * 0.48f, size * 0.48f, 30, 120);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
