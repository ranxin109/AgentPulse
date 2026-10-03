using System;
using System.Linq;
using System.Windows.Forms;

namespace TaskProgressWidget
{
    internal partial class ProgressWindow
    {
        private void InitializeBulkMenus()
        {
            foreach (var menu in new[] { widgetMenu, taskContextMenu })
            {
                var confirm = new ToolStripMenuItem("一键确认所有已完成任务", MakeGlyphIcon(Glyph.Completed, Green));
                confirm.Click += delegate { ConfirmAllCompleted(); };
                var clear = new ToolStripMenuItem("一键清理故障任务", MakeGlyphIcon(Glyph.Warning, System.Drawing.Color.IndianRed));
                clear.Click += delegate { ClearFaultTasks(); };
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(confirm); menu.Items.Add(clear);
                menu.Opening += delegate
                {
                    confirm.Enabled = readFades.Count == 0 && tasks.Any(t => t.IsCompleted && !t.IsRead);
                    clear.Enabled = readFades.Count == 0 && tasks.Any(t => t.IsFault(DateTimeOffset.Now));
                };
            }
        }

        private void BeginRead(TaskSnapshot task, Panel card)
        {
            if (!task.IsCompleted || task.IsRead || readFades.ContainsKey(task.Id)) return;
            card.Enabled = false;
            foreach (var button in card.Controls.OfType<WidgetIconButton>()) button.Checked = true;
            readFades[task.Id] = new ReadFade(task, card);
            ApplyReadFade(readFades[task.Id], true); transitionTimer.Start();
        }

        private void ConfirmAllCompleted()
        {
            if (readFades.Count > 0) return;
            // Show the live list so every acknowledgement uses the same fade behaviour.
            if (compactMode) ToggleCompactView();
            if (compactMode) return;
            showingHistory = false; selectedTaskId = "";
            PollTasks(true);
            foreach (var task in tasks.Where(t => t.IsCompleted && !t.IsRead).ToArray())
            {
                Panel card;
                if (taskCards.TryGetValue(task.Id, out card)) BeginRead(task, card);
            }
        }

        private void ClearFaultTasks()
        {
            if (readFades.Count > 0) return;
            PollTasks(true);
            var candidates = tasks.Where(t => t.IsFault(DateTimeOffset.Now)).ToArray();
            if (candidates.Length == 0) return;
            if (MessageBox.Show(this, "将 " + candidates.Length + " 个明确失败或超时未更新的任务移到回收站？\n待确认任务不会被清理；超时不代表已经确认失败。",
                "清理故障任务", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            int errors = 0;
            foreach (var task in candidates)
            {
                try
                {
                    // Re-read under the publishing lock to protect a task that just resumed.
                    if (!TaskStore.RecycleFault(task.FilePath)) continue;
                    queueOrder.RemoveAll(id => id == task.Id);
                    if (selectedTaskId == task.Id) selectedTaskId = "";
                }
                catch { errors++; }
            }
            try { TaskStore.SaveQueueOrder(queueOrderFile, queueOrder); } catch { errors++; }
            PollTasks(true);
            if (errors > 0) MessageBox.Show(this, "有 " + errors + " 项未能清理或保存，可能正在写入或没有权限。已保留未成功处理的数据。", "清理结果");
        }
    }
}
