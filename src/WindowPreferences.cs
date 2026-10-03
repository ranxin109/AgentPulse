using System;
using System.IO;
using System.Windows.Forms;

namespace TaskProgressWidget
{
    internal sealed partial class ProgressWindow
    {
        private void InitializePinMenu()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "pinned.txt");
            try { if (File.Exists(path)) TopMost = File.ReadAllText(path).Trim() != "false"; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            var items = new System.Collections.Generic.List<ToolStripMenuItem>();
            foreach (var menu in new[] { widgetMenu, taskContextMenu })
            {
                var item = new ToolStripMenuItem("固定（始终置顶）") { Checked = TopMost, CheckOnClick = false };
                item.Click += delegate
                {
                    bool next = !TopMost;
                    string temp = path + ".tmp";
                    try
                    {
                        File.WriteAllText(temp, next ? "true" : "false");
                        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "无法保存置顶设置：" + ex.Message, "任务组件");
                        return;
                    }
                    TopMost = next;
                    if (compactRing != null) compactRing.TopMost = next;
                    foreach (var peer in items) peer.Checked = next;
                };
                items.Add(item); menu.Items.Insert(0, item);
            }
        }
    }
}
