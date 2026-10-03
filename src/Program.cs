using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TaskProgressWidget
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
        private const int SW_RESTORE = 9;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--self-test")
            {
                TaskStore.SelfTest();
                return;
            }

            try { Run(args); }
            catch (Exception error)
            {
                LogLaunch("Startup failed: " + error);
                MessageBox.Show("任务组件未能启动：\n" + error.Message + "\n\n诊断记录：" + LaunchLogPath,
                    "任务组件启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static readonly string LaunchLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launch-diagnostics.log");
        private static void LogLaunch(string text)
        {
            try { File.AppendAllText(LaunchLogPath, DateTimeOffset.Now.ToString("o") + " pid=" + Process.GetCurrentProcess().Id + " " + text + Environment.NewLine, new UTF8Encoding(false)); }
            catch { }
        }

        private static string DesktopName(IntPtr handle)
        {
            int needed;
            var name = new StringBuilder(1024);
            if (!GetUserObjectInformation(handle, 2, name, name.Capacity * 2, out needed))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法确定当前 Windows 桌面");
            return name.ToString();
        }

        private static IntPtr FindExistingWindow()
        {
            foreach (var process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            using (process)
            {
                if (process.Id == Process.GetCurrentProcess().Id) continue;
                var window = FindTaskWindow(process.Id);
                if (window != IntPtr.Zero) return window;
            }
            return IntPtr.Zero;
        }

        private static void Run(string[] args)
        {
            string desktop = DesktopName(GetProcessWindowStation()) + "\\" + DesktopName(GetThreadDesktop(GetCurrentThreadId()));
            string desktopKey;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                desktopKey = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(desktop))).Replace("-", "");
            LogLaunch("Launch requested; desktop=" + desktop);
            var legacyWindow = FindExistingWindow();
            if (legacyWindow != IntPtr.Zero)
            {
                RestoreTaskWindow(legacyWindow);
                LogLaunch("Existing desktop window activated: " + legacyWindow);
                return;
            }
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string taskDirectory = args != null && args.Length > 0
                ? Path.GetFullPath(args[0])
                : (Environment.GetEnvironmentVariable("CODEX_PROGRESS_TASK_DIR") ?? Path.Combine(documents, "Codex", "progress-widget", "tasks"));
            if (String.Equals(Path.GetExtension(taskDirectory), ".json", StringComparison.OrdinalIgnoreCase)) taskDirectory = Path.Combine(Path.GetDirectoryName(taskDirectory), "tasks");
            Directory.CreateDirectory(taskDirectory);

            bool created;
            using (var mutex = new Mutex(true, @"Local\CodexTaskProgressWidgetWinForms_" + desktopKey, out created))
            {
                if (!created)
                {
                    try
                    {
                        IntPtr window = IntPtr.Zero;
                        DateTime retryUntil = DateTime.UtcNow.AddMilliseconds(1800);
                        do
                        {
                            window = FindExistingWindow();
                            if (window != IntPtr.Zero || DateTime.UtcNow >= retryUntil) break;
                            Thread.Sleep(100);
                        } while (true);
                        if (window != IntPtr.Zero) { RestoreTaskWindow(window); LogLaunch("Existing desktop window activated: " + window); }
                        else {
                            LogLaunch("Desktop instance exists but no target window was found");
                            MessageBox.Show("组件进程正在运行，但没有找到可显示的窗口。\n请由你在任务管理器中检查 ProgressWidget；程序不会自动结束它。\n\n诊断记录：" + LaunchLogPath, "组件暂时无法显示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception error) { throw new InvalidOperationException("无法激活现有组件", error); }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var form = new ProgressWindow(taskDirectory);
                form.Shown += delegate { LogLaunch("Window shown; bounds=" + form.Bounds + "; visible=" + form.Visible); };
                Application.Run(form);
                try { mutex.ReleaseMutex(); } catch { }
            }
        }

        private static IntPtr FindTaskWindow(int processId)
        {
            IntPtr best = IntPtr.Zero;
            IntPtr hidden = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner != (uint)processId) return true;
                var title = new StringBuilder(128);
                GetWindowText(hWnd, title, title.Capacity);
                if (title.ToString() == "任务列表")
                {
                    if (IsWindowVisible(hWnd)) { best = hWnd; return false; }
                    hidden = hWnd;
                }
                return true;
            }, IntPtr.Zero);
            return best != IntPtr.Zero ? best : hidden;
        }

        private static void RestoreTaskWindow(IntPtr window)
        {
            ShowWindowAsync(window, SW_RESTORE);
            NativeRect rect;
            bool hasRect = GetWindowRect(window, out rect);
            Rectangle current = hasRect
                ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
                : Rectangle.Empty;
            bool intersectsDisplay = hasRect && Screen.AllScreens.Any(screen =>
            {
                Rectangle visible = Rectangle.Intersect(current, screen.WorkingArea);
                return visible.Width >= Math.Min(120, current.Width) && visible.Height >= Math.Min(90, current.Height);
            });

            if (hasRect && !intersectsDisplay)
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                int width = Math.Max(1, current.Width);
                int height = Math.Max(1, current.Height);
                int left = Math.Max(area.Left + 12, Math.Min(current.Left, area.Right - width - 12));
                int top = Math.Max(area.Top + 12, Math.Min(current.Top, area.Bottom - height - 12));
                SetWindowPos(window, new IntPtr(0), left, top, 0, 0, SWP_NOSIZE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW | 0x0004);
            }
            else
            {
                SetWindowPos(window, new IntPtr(0), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW | 0x0004);
            }
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
    }

}
