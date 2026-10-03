using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskProgressWidget
{
    internal sealed partial class ProgressWindow
    {
        private bool compactMode;
        private Rectangle expandedBounds;
        private string CompactPreferencesPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(preferencesPath),"compact-window.json"); } }
        private CompactRingWindow compactRing;
        private ToolStripMenuItem compactMenuItem;
        private ToolStripMenuItem taskCompactMenuItem;

        private void InitializeCompactView()
        {
            compactMenuItem=new ToolStripMenuItem("最小化显示",MakeGlyphIcon("\uE73F",TextSoft));
            taskCompactMenuItem=new ToolStripMenuItem("最小化显示",MakeGlyphIcon("\uE73F",TextSoft));
            compactMenuItem.Click += delegate { ToggleCompactView(); };
            taskCompactMenuItem.Click += delegate { ToggleCompactView(); };
            widgetMenu.Items.Insert(0,compactMenuItem);
            taskContextMenu.Items.Insert(0,taskCompactMenuItem);
            FormClosed += delegate { if(compactRing!=null) { compactRing.Dispose(); compactRing=null; } };
        }

        private void ToggleCompactView()
        {
            if(layoutEditing) return;
            FinishPageTransition(); EndQueuePress();
            if(!compactMode)
            {
                expandedBounds=Bounds; compactMode=true;
                compactRing=new CompactRingWindow(widgetMenu){TopMost=TopMost};
                compactRing.Location=new Point(Right-compactRing.Width,Bottom-compactRing.Height);
                try { var saved=CompactGeometry.Load(CompactPreferencesPath); if(saved.HasValue) compactRing.Bounds=saved.Value; }
                catch(Exception error) { hints.SetToolTip(compactRing,"圆环设置无法读取："+error.Message); }
                compactRing.ClampToScreen();
                compactRing.GeometryCommitted += delegate { SaveCompactGeometry(); };
                compactRing.Show(); RefreshCompactView(); Hide();
            }
            else
            {
                var bubble=compactRing==null ? expandedBounds : compactRing.Bounds;
                compactMode=false;
                if(compactRing!=null){compactRing.Dispose();compactRing=null;}
                MinimumSize = new Size(300,64);
                Bounds=expandedBounds;
                selectedTaskId="";showingHistory=false;lastViewDetails=false;
                RenderCurrentView();
                Bounds=CompactGeometry.ExpandedAt(bubble,Size,Screen.FromRectangle(bubble).WorkingArea);
                FitWindowToScreen(); SaveWindowBounds(); Show(); Activate();
            }
            compactMenuItem.Text=taskCompactMenuItem.Text=compactMode ? "取消最小化" : "最小化显示";
            historyMenuItem.Visible=!compactMode;
        }

        private void SaveCompactGeometry()
        {
            if(compactRing==null || compactRing.IsDisposed) return;
            try
            {
                CompactGeometry.Save(CompactPreferencesPath,compactRing.Bounds);
                expandedBounds=CompactGeometry.ExpandedAt(compactRing.Bounds,expandedBounds.Size,Screen.FromRectangle(compactRing.Bounds).WorkingArea);
                CompactGeometry.Save(preferencesPath,expandedBounds);
            }
            catch(Exception error){MessageBox.Show(compactRing,"调整未保存："+error.Message,"任务组件");}
        }

        private void RefreshCompactView()
        {
            if(compactRing==null || compactRing.IsDisposed) return;
            var first=OrderedTasks(false).FirstOrDefault(t=>!t.IsCompleted);
            compactRing.RefreshTask(first);
            hints.SetToolTip(compactRing,first==null ? "暂无未完成任务 · 拖动移动 · 滚轮调大小 · 自动保存 · 右键恢复" : first.Title+" · "+first.Progress+"% · 拖动移动 · 滚轮调大小 · 自动保存 · 右键恢复");
        }
    }
}
