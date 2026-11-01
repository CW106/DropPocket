// 口袋上看得到的文字都從這裡設定，切換語言時整批重新套用
using System;
using System.Drawing;
using System.Windows.Forms;

namespace DropPocket
{
    partial class PocketForm
    {
        ToolStripItem miOpen, miReveal, miCopy, miRemove, miSettings, miOpenFolder, miExit;

        public string LanguageMode { get { return L.Mode; } }

        void ApplyTexts()
        {
            Text = L.T("暫存口袋", "DropPocket");
            tray.Text = Text;
            SetHeaderText(btnAll, L.T("全部拖出", "Drag all"),
                L.T("按住這裡拖曳，一次拖出口袋裡所有項目", "Hold and drag here to drag out everything at once"));
            SetHeaderText(btnClear, L.T("清空", "Clear"),
                L.T("清空口袋（不會刪除任何檔案）", "Empty the pocket (no files are deleted)"));
            SetHeaderText(btnPin, L.T("釘選", "Pin"), null); // 提示含快捷鍵，由 UpdateHotkeyTexts 設定
            toolTip.SetToolTip(btnGear, L.T("設定（顏色、快捷鍵）", "Settings (colors, hotkeys)"));
            toolTip.SetToolTip(title, L.T("拖這裡可以移動口袋（拖到螢幕另一側就換邊）",
                "Drag here to move the pocket (drag it to the other side of the screen to switch edges)"));

            miOpen.Text = L.T("開啟", "Open");
            miReveal.Text = L.T("在檔案總管中顯示", "Show in File Explorer");
            miCopy.Text = L.T("複製路徑 / 文字", "Copy path / text");
            miRemove.Text = L.T("從口袋移除", "Remove from pocket");

            miLeft.Text = L.T("停靠在左側", "Dock on the left");
            miRight.Text = L.T("停靠在右側", "Dock on the right");
            miKeep.Text = L.T("拖出後保留項目", "Keep items after dragging out");
            miAutostart.Text = L.T("開機時自動啟動", "Start with Windows");
            miSettings.Text = L.T("設定…", "Settings…");
            miOpenFolder.Text = L.T("開啟暫存資料夾", "Open temp folder");
            miExit.Text = L.T("結束", "Exit");

            UpdateHotkeyTexts();
            RefreshList(); // 標題的「暫存口袋 · N」、找不到的項目
            emptyState.Invalidate();
            overlay.Invalidate();
        }

        void SetHeaderText(Button b, string text, string tip)
        {
            b.Text = text;
            b.Width = TextRenderer.MeasureText(text, Font).Width + S(14);
            if (tip != null) toolTip.SetToolTip(b, tip);
        }

        public void SetLanguage(string mode)
        {
            if (mode == L.Mode) return;
            L.Set(mode);
            SaveSettings();
            ApplyTexts();
            // 設定視窗的版面是照文字寬度排的，換語言就整個重建、放回原位；
            // 等目前的點擊事件跑完再關，不在它自己的事件裡處置掉它
            if (settings != null && !settings.IsDisposed) BeginInvoke((Action)ReopenSettings);
        }

        void ReopenSettings()
        {
            if (settings == null || settings.IsDisposed) return;
            Point at = settings.Location;
            settings.Close();
            ShowSettings(at);
        }
    }
}
