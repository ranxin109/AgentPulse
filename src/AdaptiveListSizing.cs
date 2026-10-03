using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TaskProgressWidget
{
    internal static class ListGeometry
    {
        internal static int HeightFor(int contentHeight, int limit, int workHeight)
        {
            return Math.Max(1, Math.Min(Math.Max(64, contentHeight), Math.Min(Math.Max(64, limit), Math.Max(1, workHeight - 16))));
        }
        internal static Rectangle ResizeAt(Rectangle current, int height, Rectangle area)
        {
            bool bottomAnchored = area.Bottom - current.Bottom < current.Top - area.Top;
            int top = bottomAnchored ? current.Bottom - height : current.Top;
            return ScreenBounds.Limit(new Rectangle(current.Left, top, current.Width, height), area, Size.Empty, false, 0);
        }
    }

    internal sealed partial class ProgressWindow
    {
        private int listHeightLimit = 540;
        private string ListHeightLimitPath { get { return Path.Combine(Path.GetDirectoryName(preferencesPath), "list-height-limit.txt"); } }
        private void LoadListHeightLimit()
        {
            try { int value; if (File.Exists(ListHeightLimitPath) && Int32.TryParse(File.ReadAllText(ListHeightLimitPath), out value) && value >= 64) listHeightLimit = value; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private void FitListToContent()
        {
            if (compactMode || layoutEditing || !String.IsNullOrEmpty(selectedTaskId)) return;
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            int height = ListGeometry.HeightFor(listItems.AutoScrollMinSize.Height + (Height - ClientSize.Height) + 4, listHeightLimit, area.Height);
            MinimumSize = new Size(Math.Min(300, area.Width), Math.Min(64, area.Height));
            if (height != Height) Bounds = ListGeometry.ResizeAt(Bounds, height, area);
            listView.PerformLayout(); listItems.PerformLayout();
        }
        private void PrepareDetailSize()
        {
            if (layoutEditing || lastViewDetails) return;
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            int height = Math.Min(Math.Max(360, listHeightLimit), Math.Max(1, area.Height - 16));
            Bounds = ListGeometry.ResizeAt(Bounds, height, area);
        }
    }
}
