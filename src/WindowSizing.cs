using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TaskProgressWidget
{
    internal sealed partial class ProgressWindow
    {
        private bool layoutEditing;
        private Rectangle boundsBeforeEditing;
        private readonly List<ToolStripMenuItem> editActions = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> saveActions = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> cancelActions = new List<ToolStripMenuItem>();

        private void InitializeSizeControls()
        {
            listResizeHandle = MakeGlyphLabel("\uE740", 26, TextSoft);
            listResizeHandle.Size = new Size(26, 26); listResizeHandle.Location = new Point(38, 5);
            listResizeHandle.Cursor = Cursors.SizeNWSE;
            hints.SetToolTip(listResizeHandle, "拖动调整大小");
            hints.SetToolTip(listMoveHandle, "拖动调整位置");
            listMoveHandle.Location = new Point(3, 5);
            listResizeHandle.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left || !layoutEditing) return;
                ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HTBOTTOMRIGHT, 0);
            };
            listMoveBar.Controls.Add(listResizeHandle);
            var save = new Button { Text = "保存", Location = new Point(78, 4), Size = new Size(68, 30), FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Teal };
            var cancel = new Button { Text = "取消", Location = new Point(154, 4), Size = new Size(68, 30), FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = TextSoft };
            save.FlatAppearance.BorderSize = 0; cancel.FlatAppearance.BorderSize = 0;
            save.Click += delegate { FinishLayoutEditing(true); };
            cancel.Click += delegate { FinishLayoutEditing(false); };
            listMoveBar.Controls.Add(save); listMoveBar.Controls.Add(cancel);
            foreach (var menu in new[] { widgetMenu, taskContextMenu })
            {
                var edit = new ToolStripMenuItem("调整大小和位置", MakeGlyphIcon("\uE740", TextSoft));
                var accept = new ToolStripMenuItem("保存设置", MakeGlyphIcon("\uE73E", Teal));
                var discard = new ToolStripMenuItem("取消调整");
                edit.Click += delegate { BeginLayoutEditing(); };
                accept.Click += delegate { FinishLayoutEditing(true); };
                discard.Click += delegate { FinishLayoutEditing(false); };
                menu.Items.Insert(1, edit); menu.Items.Insert(2, accept); menu.Items.Insert(3, discard);
                editActions.Add(edit); saveActions.Add(accept); cancelActions.Add(discard);
            }
            ApplyEditingMode();
        }

        private void BeginLayoutEditing()
        {
            if (layoutEditing) return;
            if (compactMode && compactRing != null) { compactRing.EnableSizeAdjustment(); hints.SetToolTip(compactRing,"拖动圆环右下边缘或滚轮调大小；移动、缩放自动保存"); return; }
            FinishPageTransition(); EndQueuePress();
            boundsBeforeEditing = Bounds; layoutEditing = true;
            Bounds = ListGeometry.ResizeAt(Bounds, Math.Max(260, Height), Screen.FromRectangle(Bounds).WorkingArea);
            selectedTaskId = ""; showingHistory = false;
            ApplyEditingMode(); RenderCurrentView();
        }

        private void FinishLayoutEditing(bool save)
        {
            if (!layoutEditing) return;
            if (save) {
                FitWindowToScreen();
                try { File.WriteAllText(ListHeightLimitPath, Height.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                catch (Exception error) { MessageBox.Show(this, "高度上限未保存：" + error.Message, "保存设置"); return; }
                if (!SaveWindowBounds()) return;
                listHeightLimit = Height;
            }
            else Bounds = boundsBeforeEditing;
            layoutEditing = false; ApplyEditingMode(); RenderCurrentView();
        }

        private void ApplyEditingMode()
        {
            listMoveBar.Visible = layoutEditing; listMoveBar.Height = layoutEditing ? 40 : 0;
            detailMoveHandle.Visible = false;
            foreach (var item in editActions) item.Visible = !layoutEditing;
            foreach (var item in saveActions) item.Visible = layoutEditing;
            foreach (var item in cancelActions) item.Visible = layoutEditing;
            if (compactMenuItem != null) compactMenuItem.Enabled = !layoutEditing;
            if (taskCompactMenuItem != null) taskCompactMenuItem.Enabled = !layoutEditing;
            historyMenuItem.Enabled = !layoutEditing;
        }

        private void FitWindowToScreen()
        {
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            int margin = 8;
            int width = Math.Min(Width, Math.Max(1, area.Width - margin * 2));
            int height = Math.Min(Height, Math.Max(1, area.Height - margin * 2));
            MinimumSize = new Size(Math.Min(compactMode ? 60 : 300, width), Math.Min(compactMode ? 60 : layoutEditing || !String.IsNullOrEmpty(selectedTaskId) ? 260 : 64, height));
            Bounds = new Rectangle(Math.Max(area.Left + margin, Math.Min(Left, area.Right - width - margin)),
                Math.Max(area.Top + margin, Math.Min(Top, area.Bottom - height - margin)), width, height);
        }

        private void LayoutDetailHeader()
        {
            if (detailTitle == null || detailStateIcon == null || detailStatus == null) return;
            int rightSpace = 16;
            int titleWidth = Math.Max(100, header.ClientSize.Width - 48 - rightSpace);
            int titleHeight = Math.Max(24, MeasureWrapped(detailTitle.Text, detailTitle.Font, titleWidth).Height + 4);
            detailTitle.SetBounds(46, 8, titleWidth, titleHeight);
            detailTitle.AutoEllipsis = false;
            int rowTop = detailTitle.Bottom + 4;
            int rowHeight = Math.Max(24, Measure("M", detailStatus.Font).Height + 4);
            detailStateIcon.SetBounds(46, rowTop, 24, rowHeight);
            detailStatus.AutoSize = false;
            detailStatus.TextAlign = ContentAlignment.MiddleLeft;
            detailStatus.SetBounds(76, rowTop, titleWidth - 30, rowHeight);
            detailMoveHandle.Visible = false;

            header.Height = rowTop + rowHeight + 10;
        }
    }
}
