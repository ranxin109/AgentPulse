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
    internal sealed partial class ProgressWindow : Form
    {
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int message, int wParam, int lParam);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int HTCAPTION = 0x2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int WS_THICKFRAME = 0x00040000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_NONE = 1;

        private readonly string taskDirectory;
        private readonly System.Windows.Forms.Timer pollTimer;
        private readonly System.Windows.Forms.Timer animationTimer;
        private readonly System.Windows.Forms.Timer transitionTimer;
        private readonly System.Windows.Forms.Timer pageTransitionTimer;
        private readonly System.Windows.Forms.Timer dragHoldTimer;
        private readonly System.Windows.Forms.Timer scrollRefreshTimer;
        private readonly BufferedPanel header;
        private readonly Button backButton;
        private readonly Label detailTitle;
        private readonly Label detailStatus;
        private readonly StatusGlyph detailStateIcon;
        private Label listResizeHandle;
        private readonly Label detailMoveHandle;
        private readonly BufferedPanel listView;
        private readonly BufferedPanel detailView;
        private readonly BufferedPanel listMoveBar;
        private readonly Label listMoveHandle;
        private readonly SmoothScrollPanel listItems;
        private readonly ToolTip hints = new ToolTip { AutoPopDelay = 10000, InitialDelay = 500 };
        private int listScrollMemory, detailScrollMemory;
        private bool lastViewDetails;
        private Size stableRegionSize;
        private string stableRegionMode = "";
        private readonly string preferencesPath;
        private readonly SmoothScrollPanel detailItems;
        private readonly List<ProgressTrack> tracks = new List<ProgressTrack>();
        private readonly List<Panel> glassCards = new List<Panel>();
        private readonly Dictionary<string, ReadFade> readFades = new Dictionary<string, ReadFade>(StringComparer.Ordinal);
        private readonly Dictionary<string, Panel> taskCards = new Dictionary<string, Panel>(StringComparer.Ordinal);
        private readonly Dictionary<string, EntryMotion> entryMotions = new Dictionary<string, EntryMotion>(StringComparer.Ordinal);
        private readonly HashSet<string> entranceIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly string queueOrderFile;
        private readonly ContextMenuStrip widgetMenu;
        private readonly ContextMenuStrip taskContextMenu;
        private readonly ToolStripMenuItem historyMenuItem;
        private readonly ToolStripMenuItem deleteTaskMenuItem;
        private List<TaskSnapshot> tasks = new List<TaskSnapshot>();
        private List<string> queueOrder = new List<string>();
        private List<Panel> dragLiveOrder = new List<Panel>();
        private string fingerprint;
        private string staleFingerprint = "";
        private string selectedTaskId = "";
        private bool showingHistory;
        private int loadErrors;
        private bool hasInitialSnapshot;
        private bool scrollRefreshPending;
        private bool insideInteractiveMoveSize;
        private Rectangle interactiveWorkArea;
        private Size interactiveMoveStartSize;
        private Control pageTransitionView;
        private Rectangle pageTransitionBounds;
        private DateTimeOffset pageTransitionStartedAt;
        private int pageTransitionDirection;
        private Panel pressedCard;
        private Panel draggingCard;
        private Point pressScreenPoint;
        private int dragPointerOffsetY;
        private int dragQueueBaseY;
        private string suppressCardClickId = "";
        private TaskSnapshot contextMenuTask;

        private static readonly Color Background = Color.FromArgb(246, 248, 252);
        private static readonly Color Surface = Color.FromArgb(253, 254, 255);
        private static readonly Color SurfaceHover = Color.FromArgb(242, 247, 255);
        private static readonly Color SurfaceActive = Color.FromArgb(244, 247, 252);
        private static readonly Color CardEdge = Color.FromArgb(255, 218, 225, 235);
        private static readonly Color TextMain = Color.FromArgb(34, 43, 58);
        private static readonly Color TextSoft = Color.FromArgb(109, 122, 140);
        private static readonly Color Teal = Color.FromArgb(48, 112, 232);
        private static readonly Color Amber = Color.FromArgb(161, 113, 35);
        private static readonly Color Green = Color.FromArgb(37, 149, 97);
        internal static Color AccentColor { get { return Teal; } }
        internal static Color GreenColor { get { return Green; } }
        internal static Color CanvasColor { get { return Background; } }

        internal const int GapIconText = 8;    // between an icon and the text it labels
        internal const int GapCluster = 12;    // between two independent groups on a row
        internal const int GapXSmall = 4;
        internal const int GapSmall = 7;       // between stacked lines inside a card
        internal const int PadCardX = 13;      // card inner padding, horizontal
        internal const int PadCardTop = 8;
        internal const int PadCardBottom = 9;
        internal const int HitMin = 18;        // minimum width of a click target
        internal const string IconLabelTag = "icon-label"; // marks a glyph label in the control tree
        internal const int TrackHeight = 6;

        internal enum CardFont
        {
            Title,
            Percent,
            Body,
            BodySmall,
            Status,
            SectionLabel,
            Icon,
            IconSmall,
            Micro,
            PercentLarge
        }

        private static readonly Dictionary<CardFont, Font> CachedFonts = BuildFontCache();

        private static Dictionary<CardFont, Font> BuildFontCache()
        {
            var cache = new Dictionary<CardFont, Font>();
            cache[CardFont.Title] = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            cache[CardFont.Percent] = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold);
            cache[CardFont.Body] = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            cache[CardFont.BodySmall] = new Font("Microsoft YaHei UI", 8.0F, FontStyle.Regular);
            cache[CardFont.Status] = new Font("Microsoft YaHei UI", 7.6F, FontStyle.Bold);
            cache[CardFont.SectionLabel] = new Font("Microsoft YaHei UI", 7.6F, FontStyle.Bold);
            cache[CardFont.Icon] = new Font("Segoe Fluent Icons", 10.5F, FontStyle.Regular);
            cache[CardFont.IconSmall] = new Font("Segoe Fluent Icons", 8.5F, FontStyle.Regular);
            cache[CardFont.Micro] = new Font("Microsoft YaHei UI", 7.4F, FontStyle.Regular);
            cache[CardFont.PercentLarge] = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
            return cache;
        }

        internal static Font UiFont(CardFont which) { return CachedFonts[which]; }
        private static Size Measure(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return new Size(0, 0);
            return TextRenderer.MeasureText(text, font);
        }
        private static Size MeasureWrapped(string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth < 8) return new Size(0, 0);
            return TextRenderer.MeasureText(text, font, new Size(maxWidth, 10000),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
        }

        private static int GlyphBoxSize(string glyph, Font font)
        {
            Size ink = TextRenderer.MeasureText(glyph, font, new Size(256, 256), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int lineHeight = (int)Math.Ceiling(font.GetHeight());
            int needed = Math.Max(Math.Max(ink.Width, ink.Height), lineHeight);
            return Math.Max(20, needed + 8);
        }
        private static Label MakeGlyphLabel(string glyph, int boxSize, Color color)
        {
            var label = new Label
            {
                Text = glyph,
                Font = CachedFonts[CardFont.IconSmall],
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(boxSize, boxSize),
                Tag = IconLabelTag
            };
            return label;
        }


        public ProgressWindow(string directory)
        {
            taskDirectory = directory;
            preferencesPath = Path.Combine(Path.GetDirectoryName(directory), "window.json");
            LoadListHeightLimit();
            queueOrderFile = Path.Combine(Path.GetDirectoryName(taskDirectory), "queue-order.json");
            queueOrder = TaskStore.LoadQueueOrder(queueOrderFile);
            Text = "任务列表";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Name = "CodexTaskProgressWidget";
            Width = 372;
            Height = 540;
            MinimumSize = new Size(300, 64);
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = Background;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            DoubleBuffered = true;
            var area = Screen.PrimaryScreen.WorkingArea;
            MaximumSize = new Size(Math.Max(MinimumSize.Width, area.Width - 24), Math.Max(MinimumSize.Height, area.Height - 24));
            Location = new Point(Math.Max(area.Left + 12, area.Right - Width - 20), Math.Max(area.Top + 12, area.Bottom - Height - 20));

            header = new BufferedPanel { Dock = DockStyle.Top, Height = 0, BackColor = Color.Transparent, Padding = new Padding(8, 4, 8, 3) };
            backButton = new WidgetIconButton(WidgetIconKind.Back) { Width = 26, Height = 26, Location = new Point(8, 6), BackColor = SurfaceActive };
            backButton.Visible = false;
            backButton.Click += delegate { selectedTaskId = ""; RenderCurrentView(); BeginPageTransition(listView, -1); };
            detailTitle = MakeLabel("", 9.5F, true, TextMain);
            detailTitle.AutoSize = false;
            detailTitle.Location = new Point(43, 4);
            detailTitle.Size = new Size(Width - 138, 22);
            detailTitle.AutoEllipsis = true;
            detailStatus = MakeLabel("", 7.5F, true, Teal);
            detailStatus.Location = new Point(58, 23);
            int headerIconSize = GlyphBoxSize(Glyph.Running, CachedFonts[CardFont.IconSmall]);
            detailStateIcon = new StatusGlyph { Size = new Size(24, 24), ForeColor = Teal };
            detailStateIcon.Location = new Point(42, 24);
            detailStateIcon.Size = new Size(headerIconSize, headerIconSize);
            detailStateIcon.Visible = false;
            int moveIconSize = GlyphBoxSize(Glyph.Grip, CachedFonts[CardFont.Icon]);
            detailMoveHandle = MakeGlyphLabel(Glyph.Grip, moveIconSize, TextSoft);
            detailMoveHandle.Size = new Size(moveIconSize + 4, moveIconSize + 4);
            detailMoveHandle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            detailMoveHandle.Location = new Point(Math.Max(0, Width - 30), 4);
            detailMoveHandle.Cursor = Cursors.SizeAll;
            WireDrag(detailMoveHandle);
            header.Resize += delegate { LayoutDetailHeader(); };
            header.Controls.Add(backButton);
            header.Controls.Add(detailTitle);
            header.Controls.Add(detailStateIcon);
            header.Controls.Add(detailStatus);
            header.Controls.Add(detailMoveHandle);
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (header.Width < 4 || header.Height < 4) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = DrawingHelpers.RoundedPath(new Rectangle(0, 0, header.Width - 1, header.Height - 1), 12))
                using (var brush = new SolidBrush(Color.FromArgb(255, 253, 254, 255)))
                using (var pen = new Pen(Color.FromArgb(255, 218, 225, 235)))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
            };
            WireDrag(header);

            listView = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(8) };
            listItems = new SmoothScrollPanel { HideScrollBars = true, Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent };
            listView.Controls.Add(listItems);
            listMoveBar = new BufferedPanel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
            int listMoveIconSize = GlyphBoxSize(Glyph.Grip, CachedFonts[CardFont.Icon]);
            listMoveHandle = MakeGlyphLabel(Glyph.Grip, listMoveIconSize, TextSoft);
            listMoveHandle.Size = new Size(listMoveIconSize + 6, listMoveIconSize + 6);
            listMoveHandle.Location = new Point(2, 0);
            listMoveHandle.Cursor = Cursors.SizeAll;
            listMoveBar.Controls.Add(listMoveHandle);
            WireDrag(listMoveHandle);
            WireDrag(listMoveBar);
            listView.Controls.Add(listMoveBar);

            detailView = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
            detailItems = new SmoothScrollPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent, Padding = new Padding(12, 10, 12, 14) };
            detailView.Controls.Add(detailItems);
            listItems.Scroll += delegate { ScheduleScrolledRefresh(); };
            detailItems.Scroll += delegate { ScheduleScrolledRefresh(); };

            widgetMenu = new ContextMenuStrip();
            historyMenuItem = new ToolStripMenuItem("历史记录");
            historyMenuItem.Image = MakeGlyphIcon(Glyph.History, Color.FromArgb(70, 96, 128));
            historyMenuItem.Click += delegate
            {
                showingHistory = !showingHistory;
                selectedTaskId = "";
                RenderCurrentView();
                BeginPageTransition(listView, showingHistory ? 1 : -1);
            };
            widgetMenu.Items.Add(historyMenuItem);
            widgetMenu.Items.Add(new ToolStripSeparator());
            var closeItem = new ToolStripMenuItem("关闭组件");
            closeItem.Click += delegate { Close(); };
            widgetMenu.Items.Add(closeItem);

            taskContextMenu = new ContextMenuStrip();
            deleteTaskMenuItem = new ToolStripMenuItem("删除任务") { Font = new Font("Microsoft YaHei UI", 9F) };
            deleteTaskMenuItem.Image = MakeGlyphIcon("\uE74D", Color.FromArgb(196, 128, 128));
            deleteTaskMenuItem.Click += delegate { DeleteContextTask(); };
            taskContextMenu.Items.Add(deleteTaskMenuItem);
            taskContextMenu.Items.Add(new ToolStripSeparator());
            var taskHistory = new ToolStripMenuItem("历史记录", MakeGlyphIcon(Glyph.History, TextSoft));
            taskHistory.Click += delegate { historyMenuItem.PerformClick(); };
            taskContextMenu.Items.Add(taskHistory);
            var exitFromCard = new ToolStripMenuItem("关闭组件"); exitFromCard.Click += delegate { Close(); }; taskContextMenu.Items.Add(exitFromCard);
            taskContextMenu.Opening += delegate
            {
                contextMenuTask = FindTaskForControl(taskContextMenu.SourceControl);
                deleteTaskMenuItem.Enabled = contextMenuTask != null && !readFades.ContainsKey(contextMenuTask.Id);
            };

            InitializeBulkMenus();
            InitializePinMenu();
            Controls.Add(listView);
            Controls.Add(detailView);
            Controls.Add(header);
            AttachContextMenu(this);
            AttachContextMenu(header);
            AttachContextMenu(detailMoveHandle);
            AttachContextMenu(backButton);
            AttachContextMenu(listView);
            AttachContextMenu(listMoveBar);
            AttachContextMenu(listMoveHandle);
            AttachContextMenu(listItems);
            AttachContextMenu(detailView);
            AttachContextMenu(detailItems);
            ContextMenuStrip = widgetMenu;
            InitializeCompactView();
            InitializeSizeControls();

            pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            pollTimer.Tick += delegate { PollTasks(); RefreshStaleState(); UpdateClockLabels(this); };
            animationTimer = new System.Windows.Forms.Timer { Interval = 40 };
            transitionTimer = new System.Windows.Forms.Timer { Interval = 16 };
            transitionTimer.Tick += delegate
            {
                UpdateReadFades();
                UpdateEntryMotions();
                if (entryMotions.Count == 0 && readFades.Count == 0) transitionTimer.Stop();
            };
            pageTransitionTimer = new System.Windows.Forms.Timer { Interval = 16 };
            pageTransitionTimer.Tick += delegate { UpdatePageTransition(); };
            dragHoldTimer = new System.Windows.Forms.Timer { Interval = 350 };
            dragHoldTimer.Tick += delegate { dragHoldTimer.Stop(); BeginQueueDrag(); };
            scrollRefreshTimer = new System.Windows.Forms.Timer { Interval = 40 };
            scrollRefreshTimer.Tick += delegate { scrollRefreshTimer.Stop(); scrollRefreshPending = false; UpdateWindowRegion(); };
            animationTimer.Tick += delegate { foreach (var track in tracks.ToArray()) if (!track.IsDisposed) track.Animate(); };
            Shown += delegate { RestoreWindowBounds(); PollTasks(true); pollTimer.Start(); animationTimer.Start(); BringToFrontVisible(); };
            FormClosed += delegate { pollTimer.Stop(); animationTimer.Stop(); transitionTimer.Stop(); pageTransitionTimer.Stop(); dragHoldTimer.Stop(); scrollRefreshTimer.Stop(); };
            Resize += delegate { if (!insideInteractiveMoveSize) UpdateWindowRegion(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplySystemBackdrop();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style |= WS_THICKFRAME;
                parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW: let Windows cast the shadow outside the widget region.
                return parameters;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (insideInteractiveMoveSize && ScreenBounds.LimitMessage(ref m, interactiveWorkArea, MinimumSize)) return;
            if (m.Msg == 0x0083) { m.Result = IntPtr.Zero; return; } // Preserve resizing without a non-client frame.
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (!layoutEditing && (int)m.Result >= HTLEFT && (int)m.Result <= HTBOTTOMRIGHT) { m.Result = new IntPtr(1); return; }
                if ((int)m.Result == 0 || (int)m.Result == 1)
                {
                    long packed = m.LParam.ToInt64();
                    int screenX = unchecked((short)(packed & 0xffff));
                    int screenY = unchecked((short)((packed >> 16) & 0xffff));
                    Point point = PointToClient(new Point(screenX, screenY));
                    int hit = GetResizeHitCode(point);
                    if (hit != 0) m.Result = new IntPtr(hit);
                }
                return;
            }

            if (m.Msg == WM_ENTERSIZEMOVE)
            {
                insideInteractiveMoveSize = true;
                interactiveWorkArea = Screen.FromRectangle(Bounds).WorkingArea;
                interactiveMoveStartSize = Size;
                FinishPageTransition();
            }

            base.WndProc(ref m);

            if (m.Msg == WM_EXITSIZEMOVE)
            {
                bool resized = Size != interactiveMoveStartSize;
                insideInteractiveMoveSize = false;
                if (resized)
                {
                    var area = Screen.FromControl(this).WorkingArea;
                    Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
                    UpdateWindowRegion();
                    ReflowCurrentContent();
                }
                else UpdateWindowRegion();
            }
        }

        private int GetResizeHitCode(Point point)
        {
            if (compactMode || !layoutEditing) return 0;
            Rectangle surface = Rectangle.Empty;
            if (!String.IsNullOrEmpty(selectedTaskId) && header.Visible && header.Height > 0)
                surface = Rectangle.Intersect(ClientRectangle, header.Bounds);

            foreach (var card in glassCards.ToArray())
            {
                if (card == null || card.IsDisposed || !card.Visible || card.Parent == null) continue;
                Point origin;
                try { origin = PointToClient(card.Parent.PointToScreen(card.Location)); }
                catch { continue; }
                var bounds = Rectangle.Intersect(ClientRectangle, new Rectangle(origin, card.Size));
                var viewport = card.Parent as ScrollableControl;
                if (viewport != null && viewport.AutoScroll)
                {
                    try
                    {
                        Point viewportOrigin = PointToClient(viewport.PointToScreen(Point.Empty));
                        bounds = Rectangle.Intersect(bounds, new Rectangle(viewportOrigin, viewport.ClientSize));
                    }
                    catch { continue; }
                }
                if (bounds.Width > 0 && bounds.Height > 0)
                    surface = surface.IsEmpty ? bounds : Rectangle.Union(surface, bounds);
            }

            if (surface.IsEmpty) return 0;
            const int grip = 7;
            bool left = point.X >= surface.Left && point.X < surface.Left + grip;
            bool right = point.X <= surface.Right && point.X > surface.Right - grip;
            bool top = point.Y >= surface.Top && point.Y < surface.Top + grip;
            bool bottom = point.Y <= surface.Bottom && point.Y > surface.Bottom - grip;
            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return 0;
        }

        private void FinishPageTransition()
        {
            if (pageTransitionView != null && !pageTransitionView.IsDisposed)
            {
                pageTransitionView.Bounds = pageTransitionBounds;
                pageTransitionView.Dock = DockStyle.Fill;
            }
            pageTransitionView = null;
            pageTransitionTimer.Stop();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Background);
        }

        private void ApplySystemBackdrop()
        {
            int backdrop = DWMSBT_NONE;
            try
            {
                DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
            catch { }

            BackColor = Background;
            // Keep the top-level HWND opaque. The previous layered-window opacity
            // combined with a padded window region and alpha shadows, which exposed
            // the light form surface as a pale block on dark desktops.
            Opacity = 1.0;
            Invalidate(true);
            Update();
        }

        private void ScheduleScrolledRefresh()
        {
            if (!IsHandleCreated || IsDisposed || scrollRefreshPending) return;
            scrollRefreshPending = true;
            scrollRefreshTimer.Start();
        }


        private bool SaveWindowBounds()
        {
            try
            {
                string temp = preferencesPath + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(new[] { Left, Top, Width, Height }));
                if (File.Exists(preferencesPath)) File.Replace(temp, preferencesPath, null);
                else File.Move(temp, preferencesPath);
                return true;
            }
            catch (Exception error) { MessageBox.Show(this, "设置未保存：" + error.Message, "保存设置"); return false; }
        }

        private void RestoreWindowBounds()
        {
            try
            {
                if (!File.Exists(preferencesPath)) return;
                int[] values = new JavaScriptSerializer().Deserialize<int[]>(File.ReadAllText(preferencesPath));
                if (values == null || values.Length != 4) return;
                var requested = new Rectangle(values[0], values[1], values[2], values[3]);
                var area = Screen.FromRectangle(requested).WorkingArea;
                requested.Width = Math.Max(MinimumSize.Width, Math.Min(area.Width - 24, requested.Width));
                requested.Height = Math.Max(MinimumSize.Height, Math.Min(area.Height - 24, requested.Height));
                requested.X = Math.Max(area.Left, Math.Min(area.Right - requested.Width, requested.X));
                requested.Y = Math.Max(area.Top, Math.Min(area.Bottom - requested.Height, requested.Y));
                Bounds = requested;
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && layoutEditing) { FinishLayoutEditing(false); return true; }
            if (keyData == Keys.Escape && (!String.IsNullOrEmpty(selectedTaskId) || showingHistory))
            { selectedTaskId = ""; showingHistory = false; RenderCurrentView(); return true; }
            if (keyData == Keys.F5) { PollTasks(true); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var timer in new[] { pollTimer, animationTimer, transitionTimer, pageTransitionTimer, dragHoldTimer, scrollRefreshTimer }) if (timer != null) timer.Dispose();
                hints.Dispose(); if (widgetMenu != null) widgetMenu.Dispose(); if (taskContextMenu != null) taskContextMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        private void AttachContextMenu(Control control)
        {
            if (control == null) return;
            control.ContextMenuStrip = widgetMenu;
            control.MouseDown -= ResizeFromChild; control.MouseDown += ResizeFromChild;
            foreach (Control child in control.Controls) AttachContextMenu(child);
        }

        private void AttachTaskContextMenu(Control control)
        {
            if (control == null) return;
            control.ContextMenuStrip = taskContextMenu;
            control.MouseDown -= ResizeFromChild; control.MouseDown += ResizeFromChild;
            foreach (Control child in control.Controls) AttachTaskContextMenu(child);
        }

        private void ResizeFromChild(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var control = sender as Control;
            if (control == null) return;
            int hit = GetResizeHitCode(PointToClient(control.PointToScreen(e.Location)));
            if (hit == 0) return;
            dragHoldTimer.Stop(); pressedCard = null;
            ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, hit, 0);
        }

        private static TaskSnapshot FindTaskForControl(Control control)
        {
            while (control != null)
            {
                var task = control.Tag as TaskSnapshot;
                if (task != null) return task;
                control = control.Parent;
            }
            return null;
        }

        private void DeleteContextTask()
        {
            if (contextMenuTask == null) return;
            var task = tasks.FirstOrDefault(t => t.Id == contextMenuTask.Id);
            contextMenuTask = null;
            if (task == null) return;
            var answer = MessageBox.Show(this,
                "确定将“" + task.Title + "”移到回收站吗？",
                "删除任务", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            try
            {
                if (File.Exists(task.FilePath))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(task.FilePath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                queueOrder.RemoveAll(id => String.Equals(id, task.Id, StringComparison.Ordinal));
                TaskStore.SaveQueueOrder(queueOrderFile, queueOrder);
                readFades.Remove(task.Id);
                entranceIds.Remove(task.Id);
                if (String.Equals(selectedTaskId, task.Id, StringComparison.Ordinal)) selectedTaskId = "";
                PollTasks(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "删除任务失败：" + ex.Message, "删除任务", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StyleCard(Panel card)
        {
            card.BorderStyle = BorderStyle.None;
            card.BackColor = Color.Transparent;
            glassCards.Add(card);
            card.ParentChanged += delegate
            {
                Control parent = card.Parent;
                if (parent == null) return;
                parent.Invalidate();
                ScheduleScrolledRefresh();
            };
            // AutoScroll moves every child while the viewport scrolls. Let WinForms
            // invalidate the exposed area; only queue the window-region refresh here.
            card.LocationChanged += delegate { ScheduleScrolledRefresh(); };
            card.SizeChanged += delegate { ScheduleScrolledRefresh(); };
            Action setRegion = delegate
            {
                if (card.Width < 4 || card.Height < 4) return;
                using (var path = DrawingHelpers.RoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 15))
                {
                    Region old = card.Region;
                    card.Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            };
            card.Resize += delegate { setRegion(); };
            setRegion();
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (card.Width < 4 || card.Height < 4) return;
                var task = card.Tag as TaskSnapshot;
                Color edge = task != null && task.IsStale(DateTimeOffset.Now)
                    ? Color.FromArgb(200, 208, 148, 148)
                    : CardEdge;
                ReadFade fade = null;
                if (task != null && readFades.TryGetValue(task.Id, out fade)) edge = DrawingHelpers.Blend(edge, Background, fade.Amount);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = DrawingHelpers.RoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 15))
                {
                    var buffered = card as BufferedPanel;
                    float reveal = buffered == null ? 1F : buffered.RevealAmount;
                    Color cardSurface = card.BackColor == Color.Transparent ? Surface : card.BackColor;
                    Color baseFill = fade == null ? DrawingHelpers.Blend(Background, cardSurface, reveal) : DrawingHelpers.Blend(Color.FromArgb(235, 238, 243), Background, fade.Amount);
                    if (fade == null) edge = DrawingHelpers.Blend(Background, edge, reveal);
                    // Vertical sheen: the surface reads as a sheet of frosted glass rather
                    // than a flat opaque block. Colours stay neutral and opaque so nothing
                    // depends on the desktop behind the panel.
                    using (var brush = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(2, card.Width), Math.Max(2, card.Height)),
                        DrawingHelpers.Blend(baseFill, Color.FromArgb(255, 255, 255), 0.08F),
                        DrawingHelpers.Blend(baseFill, Color.FromArgb(0, 0, 0), 0.012F),
                        LinearGradientMode.Vertical))
                    using (var pen = new Pen(edge))
                    {
                        e.Graphics.FillPath(brush, path);
                        e.Graphics.DrawPath(pen, path);
                        // A faint highlight near the top edge reads as a glass rim; the
                        // previous bottom inset line was too heavy on a dark card.
                        using (var rim = new Pen(Color.FromArgb(255, 255, 255, 255), 1F))
                            e.Graphics.DrawLine(rim, 16, 1, card.Width - 17, 1);
                    }
                }
            };
        }

        private void UpdateReadFades()
        {
            if (readFades.Count == 0) return;
            foreach (var fade in readFades.Values.ToArray())
            {
                if (fade.Card.IsDisposed) { readFades.Remove(fade.Task.Id); continue; }
                double elapsed = (DateTimeOffset.Now - fade.StartedAt).TotalSeconds;
                fade.Amount = (float)Math.Max(0, Math.Min(1, (elapsed - 3.0) / 0.28));
                ApplyReadFade(fade, elapsed < 3.0);
                if (elapsed >= 3.0 && elapsed < 3.28)
                    fade.Card.Top = fade.OriginalTop + (int)Math.Round(10 * fade.Amount);
                if (elapsed >= 3.28 && !fade.Committed)
                {
                    try
                    {
                        TaskStore.MarkRead(fade.Task.FilePath);
                        fade.Committed = true;
                    }
                    catch
                    {
                        readFades.Remove(fade.Task.Id);
                        fade.Amount = 0;
                        ApplyReadFade(fade, false);
                    }
                }
            }
            if (readFades.Count > 0 && readFades.Values.All(x => x.Committed))
            {
                readFades.Clear();
                PollTasks(true);
            }
        }

        private void ApplyReadFade(ReadFade fade, bool disabledHold)
        {
            fade.Card.BackColor = Color.Transparent;
            fade.Card.Enabled = !readFades.ContainsKey(fade.Task.Id);
            foreach (Control child in fade.Card.Controls)
            {
                Color original;
                if (child is Label && fade.ForeColors.TryGetValue(child, out original)) child.ForeColor = disabledHold
                    ? DrawingHelpers.Blend(original, Color.FromArgb(120, 120, 126), 0.72F)
                    : DrawingHelpers.Blend(original, Background, fade.Amount);
                var progress = child as ProgressTrack;
                if (progress != null) progress.FadeAmount = disabledHold ? 0.68F : fade.Amount;
                var icon = child as WidgetIconButton;
                if (icon != null) icon.FadeAmount = disabledHold ? 0.68F : fade.Amount;
                var button = child as Button;
                if (button != null && !(button is WidgetIconButton) && fade.ForeColors.TryGetValue(child, out original)) button.ForeColor = disabledHold
                    ? DrawingHelpers.Blend(original, Color.FromArgb(120, 120, 126), 0.72F)
                    : DrawingHelpers.Blend(original, Background, fade.Amount);
            }
            fade.Card.Invalidate();
        }


        private void UpdateWindowRegion()
        {
            if (!IsHandleCreated || ClientSize.Width < 20 || ClientSize.Height < 20) return;
            string mode = compactMode ? "compact" : String.IsNullOrEmpty(selectedTaskId) ? "list" : "detail";
            if (mode != "list" && mode == stableRegionMode && stableRegionSize == ClientSize) return;
            stableRegionMode = mode; stableRegionSize = ClientSize;
            var combined = new Region(); combined.MakeEmpty();
            if (compactMode)
            {
                using (var circle = new GraphicsPath()) { circle.AddEllipse(new Rectangle(1, 1, ClientSize.Width - 2, ClientSize.Height - 2)); combined.Union(circle); }
            }
            else if (!String.IsNullOrEmpty(selectedTaskId))
            {
                using (var path = DrawingHelpers.RoundedPath(ClientRectangle, 16)) combined.Union(path);
            }
            else
            {
                foreach (Control card in glassCards)
                {
                    if (card.IsDisposed || !card.Visible || card.Parent == null) continue;
                    Point point = PointToClient(card.Parent.PointToScreen(card.Location));
                    Point viewport = PointToClient(listItems.PointToScreen(Point.Empty));
                    var bounds = Rectangle.Intersect(new Rectangle(point, card.Size), new Rectangle(viewport, listItems.ClientSize));
                    if (bounds.Width < 4 || bounds.Height < 4) continue;
                    using (var path = DrawingHelpers.RoundedPath(bounds, 15)) combined.Union(path);
                }
                if (layoutEditing)
                {
                    Point toolbar = PointToClient(listMoveBar.PointToScreen(Point.Empty));
                    using (var path = DrawingHelpers.RoundedPath(new Rectangle(toolbar, listMoveBar.Size), 8)) combined.Union(path);
                }
                if (listItems.VerticalScroll.Visible && !listItems.HideScrollBars)
                {
                    Point edge = PointToClient(listItems.PointToScreen(new Point(listItems.ClientSize.Width, 0)));
                    combined.Union(new Rectangle(edge, new Size(SystemInformation.VerticalScrollBarWidth, listItems.ClientSize.Height)));
                }
            }
            Region previous = Region; Region = combined; if (previous != null) previous.Dispose();
        }


        private void BringToFrontVisible()
        {
            if (compactMode && compactRing != null) { compactRing.Show(); compactRing.Activate(); return; }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            SetWindowPos(Handle, new IntPtr(TopMost ? -1 : -2), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW);
            BringToFront();
            Activate();
        }

        private void WireDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || !layoutEditing) return;
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            };
        }

        private static readonly Dictionary<string, Font> LabelFonts = new Dictionary<string, Font>();

        private static Label MakeLabel(string text, float size, bool bold, Color color)
        {
            string key = size.ToString(CultureInfo.InvariantCulture) + ":" + bold;
            Font font; if (!LabelFonts.TryGetValue(key, out font)) { font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular); LabelFonts[key] = font; }
            return new Label { Text = text, UseMnemonic = false, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = true };
        }




        private void PollTasks(bool force = false)
        {
            if (!force && (listItems.IsScrolling || detailItems.IsScrolling || readFades.Count > 0 || pressedCard != null || insideInteractiveMoveSize || pageTransitionView != null)) return;
            string nextFingerprint;
            try { nextFingerprint = TaskStore.Fingerprint(taskDirectory); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            if (!force && String.Equals(nextFingerprint, fingerprint, StringComparison.Ordinal)) return;
            var existingIds = new HashSet<string>(tasks.Select(t => t.Id), StringComparer.Ordinal);
            bool firstLoad = !hasInitialSnapshot;
            fingerprint = nextFingerprint;
            tasks = TaskStore.Load(taskDirectory, out loadErrors);
            hasInitialSnapshot = true;
            entranceIds.Clear();
            if (!firstLoad)
                foreach (var task in tasks.Where(t => !existingIds.Contains(t.Id) && !t.IsCompleted && !t.IsRead)) entranceIds.Add(task.Id);
            staleFingerprint = BuildStaleFingerprint();
            if (!String.IsNullOrEmpty(selectedTaskId) && !tasks.Any(t => t.Id == selectedTaskId)) selectedTaskId = "";
            RenderCurrentView();
            UpdateHeader();
        }

        private void ReconcileQueueOrder()
        {
            var queued = tasks.Where(t => !t.IsCompleted && !t.IsRead)
                .OrderBy(t => t.StartedAt).ThenBy(t => t.UpdatedAt).ToList();
            var validIds = new HashSet<string>(queued.Select(t => t.Id), StringComparer.Ordinal);
            string before = String.Join("|", queueOrder.ToArray());
            queueOrder.RemoveAll(id => !validIds.Contains(id));
            foreach (var task in queued) if (!queueOrder.Contains(task.Id)) queueOrder.Add(task.Id);
            if (!String.Equals(before, String.Join("|", queueOrder.ToArray()), StringComparison.Ordinal))
            {
                try { TaskStore.SaveQueueOrder(queueOrderFile, queueOrder); } catch { }
            }
        }

        private List<TaskSnapshot> OrderedTasks(bool history)
        {
            return TaskStore.Ordered(tasks, history, history ? null : queueOrder);
        }

        private string BuildStaleFingerprint()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            return String.Join("|", tasks.Where(t => t.IsStale(now)).Select(t => t.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }

        private void RefreshStaleState()
        {
            if (listItems.IsScrolling || detailItems.IsScrolling || readFades.Count > 0 || pressedCard != null || insideInteractiveMoveSize || pageTransitionView != null) return;
            string next = BuildStaleFingerprint();
            if (String.Equals(next, staleFingerprint, StringComparison.Ordinal)) return;
            staleFingerprint = next;
            RenderCurrentView();
            UpdateHeader();
        }

        private void UpdateEntryMotions()
        {
            if (entryMotions.Count == 0) return;
            foreach (var pair in entryMotions.ToArray())
            {
                EntryMotion motion = pair.Value;
                if (motion.Card.IsDisposed) { entryMotions.Remove(pair.Key); entranceIds.Remove(pair.Key); continue; }
                double elapsedMs = (DateTimeOffset.Now - motion.StartedAt).TotalMilliseconds - motion.DelayMs;
                if (elapsedMs < 0) continue;
                double amount = Math.Max(0, Math.Min(1, elapsedMs / 280.0));
                double eased = 1 - Math.Pow(1 - amount, 3);
                motion.Card.Top = motion.TargetTop + (int)Math.Round((1 - eased) * 14);
                var card = motion.Card as BufferedPanel;
                if (card != null) card.RevealAmount = (float)eased;
                if (amount >= 1)
                {
                    motion.Card.Top = motion.TargetTop;
                    entryMotions.Remove(pair.Key);
                    entranceIds.Remove(pair.Key);
                }
            }
        }
        private static Image MakeGlyphIcon(string glyph, Color color)
        {
            using (var font = new Font("Segoe Fluent Icons", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                Size ink = TextRenderer.MeasureText(glyph, font, new Size(256, 256),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int side = Math.Max(20, Math.Max(ink.Width, ink.Height) + 4);
                var bitmap = new Bitmap(side, side);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    TextRenderer.DrawText(graphics, glyph, font, new Rectangle(0, 0, side, side), color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
                return bitmap;
            }
        }

        private void UpdateHeader()
        {
            if (compactMode) { header.Visible = false; return; }
            if (!String.IsNullOrEmpty(selectedTaskId))
            {
                var selected = tasks.FirstOrDefault(t => t.Id == selectedTaskId);
                backButton.Visible = true;
                detailTitle.Text = selected == null ? "任务详情" : selected.Title;
                hints.SetToolTip(detailTitle, detailTitle.Text);
                detailTitle.Visible = true;
                bool done = selected != null && selected.IsCompleted;
                bool confirm = selected != null && selected.IsAwaitingConfirmation;
                bool failed = selected != null && selected.IsFailed;
                bool wait = selected != null && (selected.IsWaiting || confirm);
                detailStateIcon.State = done ? "completed" : wait ? "waiting" : "active";
                detailStateIcon.Invalidate();
                detailStateIcon.ForeColor = done ? Green : failed ? Color.IndianRed : wait ? Amber : Teal;
                detailStateIcon.Visible = true;
                detailStatus.Text = selected == null ? "" : (done ? "已完成" : confirm ? "待确认" : failed ? "失败" : wait ? "等待中" : "处理中");
                detailStatus.ForeColor = done ? Green : failed ? Color.IndianRed : wait ? Amber : Teal;
                detailStatus.Visible = true;
                // Header height is measured, not fixed: a fixed 38px clipped the top of the
                // title text because YaHei's line box is taller than its ink box.
                LayoutDetailHeader();
                header.Visible = true;
                historyMenuItem.Text = showingHistory ? "返回任务列表" : "历史记录";
                return;
            }
            backButton.Visible = false;
            detailTitle.Visible = false;
            detailStateIcon.Visible = false;
            detailStatus.Visible = false;
            header.Height = 0;
            header.Visible = false;
            historyMenuItem.Text = showingHistory ? "返回任务列表" : "历史记录";
        }


        private void RenderCurrentView()
        {
            if (compactMode) { RefreshCompactView(); return; }
            FinishPageTransition();
            if (lastViewDetails) detailScrollMemory = Math.Max(0, -detailItems.AutoScrollPosition.Y);
            else listScrollMemory = Math.Max(0, -listItems.AutoScrollPosition.Y);
            bool details = !String.IsNullOrEmpty(selectedTaskId);
            if (details) PrepareDetailSize();
            MinimumSize = new Size(300, details || layoutEditing ? 260 : 64);
            if (details && !layoutEditing) FitWindowToScreen();
            if (details != lastViewDetails) detailScrollMemory = 0;
            SuspendLayout();
            listView.Visible = !details; detailView.Visible = details;
            UpdateHeader(); header.SendToBack(); ResumeLayout(true);
            detailView.PerformLayout(); detailItems.PerformLayout(); listView.PerformLayout(); listItems.PerformLayout();
            if (details)
            {
                var task = tasks.FirstOrDefault(t => t.Id == selectedTaskId);
                if (task == null) { selectedTaskId = ""; listView.Visible = true; detailView.Visible = false; details = false; RenderList(); }
                else RenderDetail(task);
            }
            else RenderList();
            if (!details) FitListToContent();
            lastViewDetails = details;
            var scroller = details ? (ScrollableControl)detailItems : listItems;
            scroller.AutoScrollPosition = new Point(0, Math.Min(details ? detailScrollMemory : listScrollMemory, Math.Max(0, scroller.AutoScrollMinSize.Height - scroller.ClientSize.Height)));
            UpdateClockLabels(this); UpdateWindowRegion();
            if (entryMotions.Count > 0) transitionTimer.Start();
        }


        private void ReflowCurrentContent()
        {
            if (compactMode) { RefreshCompactView(); return; }
            bool details = !String.IsNullOrEmpty(selectedTaskId);
            int listScroll = Math.Max(0, -listItems.AutoScrollPosition.Y);
            int detailScroll = Math.Max(0, -detailItems.AutoScrollPosition.Y);

            if (details)
            {
                var task = tasks.FirstOrDefault(t => t.Id == selectedTaskId);
                if (task != null) RenderDetail(task);
                else
                {
                    selectedTaskId = "";
                    details = false;
                    listView.Visible = true;
                    detailView.Visible = false;
                    RenderList();
                }
            }
            else RenderList();

            PerformLayout();
            UpdateHeader();
            UpdateWindowRegion();
            BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed || !IsHandleCreated) return;
                ScrollableControl scrollView = details ? (ScrollableControl)detailItems : listItems;
                int savedScroll = details ? detailScroll : listScroll;
                int maximum = Math.Max(0, scrollView.AutoScrollMinSize.Height - scrollView.ClientSize.Height);
                scrollView.AutoScrollPosition = new Point(0, Math.Min(savedScroll, maximum));
                UpdateWindowRegion();
            });
        }

        private void BeginPageTransition(Control view, int direction)
        {
            if (view == null || view.IsDisposed || !view.Visible) return;
            pageTransitionTimer.Stop();
            if (pageTransitionView != null && !pageTransitionView.IsDisposed)
            {
                pageTransitionView.Bounds = pageTransitionBounds;
                pageTransitionView.Dock = DockStyle.Fill;
            }
            view.Dock = DockStyle.None;
            view.PerformLayout();
            pageTransitionBounds = view.Bounds;
            if (pageTransitionBounds.Width < 2 || pageTransitionBounds.Height < 2)
            {
                view.Dock = DockStyle.Fill;
                return;
            }
            pageTransitionView = view;
            pageTransitionDirection = direction < 0 ? -1 : 1;
            pageTransitionStartedAt = DateTimeOffset.Now;
            view.Left = pageTransitionBounds.Left + pageTransitionDirection * 6;
            view.BringToFront();
            if (header.Visible) header.SendToBack();
            pageTransitionTimer.Start();
        }

        private void UpdatePageTransition()
        {
            if (pageTransitionView == null || pageTransitionView.IsDisposed)
            {
                pageTransitionView = null;
                pageTransitionTimer.Stop();
                return;
            }
            double amount = Math.Max(0, Math.Min(1, (DateTimeOffset.Now - pageTransitionStartedAt).TotalMilliseconds / 140.0));
            double eased = 1 - Math.Pow(1 - amount, 3);
            pageTransitionView.Left = pageTransitionBounds.Left + pageTransitionDirection * (int)Math.Round((1 - eased) * 6);
            if (amount >= 1)
            {
                pageTransitionView.Bounds = pageTransitionBounds;
                pageTransitionView.Dock = DockStyle.Fill;
                if (header.Visible) header.SendToBack();
                pageTransitionView = null;
                pageTransitionTimer.Stop();
                UpdateWindowRegion();
            }
        }

        private void RenderList()
        {
            listItems.StopScrolling();
            DisposeChildren(listItems);
            tracks.Clear();
            glassCards.Clear();
            taskCards.Clear();
            entryMotions.Clear();
            
            var visible = OrderedTasks(showingHistory);
            listItems.AutoScrollMinSize = Size.Empty;
            listItems.AutoScrollPosition = new Point(0, 0);
            listItems.PerformLayout();
            int y = GapXSmall;
            int cardWidth = Math.Max(160, listItems.ClientSize.Width - 8);
            if (visible.Count == 0)
            {
                Font emptyFont = UiFont(CardFont.Body);
                int emptyTextHeight = Math.Max(18, Measure("M", emptyFont).Height);
                int width = Math.Max(160, cardWidth - 4);
                int contentWidth = Math.Max(80, width - PadCardX * 2 - 32);
                string title = loadErrors > 0 ? "任务读取异常" : showingHistory ? "暂无历史记录" : tasks.Any(t => t.IsRead) ? "任务已全部确认" : "暂时没有任务";
                string description = loadErrors > 0 ? "部分任务状态暂时无法读取，请检查任务数据。" : showingHistory ? "确认完成的任务后，会保留在这里。" : "新任务会自动显示在这里。\n右键可查看历史或关闭组件。";
                Font captionFont = UiFont(CardFont.BodySmall);
                int captionHeight = Math.Max(18, MeasureWrapped(description, captionFont, contentWidth).Height + 2);
                int emptyHeight = PadCardTop + emptyTextHeight + 8 + captionHeight + PadCardBottom;
                var emptyCard = new BufferedPanel { Width = width, Height = emptyHeight, BackColor = Color.Transparent, BorderStyle = BorderStyle.None };
                StyleCard(emptyCard);
                emptyCard.Location = new Point(2, y);
                var icon = MakeGlyphLabel(loadErrors > 0 ? Glyph.Warning : showingHistory ? Glyph.History : Glyph.Pending, 24, TextSoft);
                icon.Location = new Point(PadCardX, PadCardTop); emptyCard.Controls.Add(icon);
                var empty = MakeLabel(title, emptyFont.Size, false, TextMain);
                empty.AutoSize = false;
                empty.UseMnemonic = false;
                empty.Size = new Size(contentWidth, emptyTextHeight + 2);
                empty.Location = new Point(PadCardX + 32, PadCardTop);
                emptyCard.Controls.Add(empty);
                var caption = MakeLabel(description, captionFont.Size, false, TextSoft);
                caption.AutoSize = false;
                caption.Location = new Point(PadCardX + 32, PadCardTop + emptyTextHeight + 8);
                caption.Size = new Size(contentWidth, captionHeight);
                emptyCard.Controls.Add(caption);
                listItems.Controls.Add(emptyCard);
                AttachContextMenu(emptyCard);
                listItems.AutoScrollMinSize = new Size(0, y + emptyHeight + GapXSmall);
                return;
            }
            foreach (var task in visible)
            {
                var card = CreateTaskCard(task, cardWidth, showingHistory);
                card.Location = new Point(2, y + (entranceIds.Contains(task.Id) ? 14 : 0));
                if (entranceIds.Contains(task.Id))
                {
                    var buffered = card as BufferedPanel;
                    if (buffered != null) buffered.RevealAmount = 0;
                    entryMotions[task.Id] = new EntryMotion(card, y, DateTimeOffset.Now);
                }
                card.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                listItems.Controls.Add(card);
                y += card.Height + GapSmall;
            }
            listItems.AutoScrollMinSize = new Size(0, y + GapXSmall);
            WireListWheel(listItems);
        }


        private Panel CreateTaskCard(TaskSnapshot task, int width, bool history)
        {
            bool stale = task.IsStale(DateTimeOffset.Now) || task.IsFailed;
            int checkSpace = task.IsCompleted ? 36 : 0;
            int textWidth = Math.Max(80, width - PadCardX * 2 - checkSpace);
            int lineHeight = Measure("M", UiFont(CardFont.Title)).Height;
            int titleHeight = Math.Min(lineHeight * 2 + 2, Math.Max(lineHeight, MeasureWrapped(task.Title, UiFont(CardFont.Title), textWidth).Height));
            int barY = 12 + titleHeight + 10;
            int clockY = barY + TrackHeight + 8;
            var card = new BufferedPanel { Width = width, Height = clockY + 32, Cursor = Cursors.Hand, Tag = task };
            StyleCard(card);
            var title = new Label { Text = task.Title, Font = UiFont(CardFont.Title), ForeColor = TextMain, BackColor = Color.Transparent, AutoEllipsis = true, UseMnemonic = false,
                Location = new Point(PadCardX, 12), Size = new Size(textWidth, titleHeight) };
            card.Controls.Add(title); hints.SetToolTip(title, task.Title);
            var bar = new ProgressTrack { Location = new Point(PadCardX, barY), Size = new Size(width - PadCardX * 2, TrackHeight), Percent = task.Progress, Active = !task.IsCompleted && !task.IsWaiting && !task.IsAwaitingConfirmation && !task.IsFailed, Completed = task.IsCompleted, Stale = task.IsStale(DateTimeOffset.Now) || task.IsFailed, AwaitingConfirmation = task.IsAwaitingConfirmation };
            tracks.Add(bar); card.Controls.Add(bar);
            var timer = MakeGlyphLabel(Glyph.Timer, 24, TextSoft); timer.Location = new Point(PadCardX - 2, clockY); card.Controls.Add(timer);
            var clock = new Label { Text = task.ElapsedText(), Font = UiFont(CardFont.BodySmall), ForeColor = TextSoft, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(PadCardX + 26, clockY), Size = new Size(width - PadCardX * 2 - 66, 24), Tag = new ClockBinding(task) };
            card.Controls.Add(clock);
            hints.SetToolTip(bar, task.Progress + "% · 预计 " + task.EstimateText());
            if (task.IsCompleted)
            {
                var check = new WidgetIconButton(WidgetIconKind.Check) { Location = new Point(width - PadCardX - 26, 10), Size = new Size(26, 26), Checked = history, Enabled = !history, BackColor = Surface, AccessibleName = "确认已阅" };
                hints.SetToolTip(check, "已阅 · 3 秒后移入历史");
                check.Click += delegate
                {
                    BeginRead(task, card);
                };
                card.Controls.Add(check);
            }
            else
            {
                var grip = MakeGlyphLabel(stale ? Glyph.Warning : (task.IsWaiting || task.IsAwaitingConfirmation) ? Glyph.Waiting : Glyph.Running, 24, stale ? Color.FromArgb(186,83,94) : task.IsAwaitingConfirmation ? Amber : TextSoft);
                grip.Location = new Point(width - PadCardX - 24, clockY); grip.Cursor = Cursors.Hand; card.Controls.Add(grip);
                hints.SetToolTip(grip, task.IsFailed ? "任务已报告失败" : stale ? "长时间未更新 · 点击查看详情" : task.IsAwaitingConfirmation ? "待用户确认 · 点击查看详情" : "按创建时间排序 · 最新在上");
            }
            WireCardClicks(card, delegate
            {
                if (readFades.ContainsKey(task.Id) || draggingCard != null) return;
                if (layoutEditing) return;
                if (suppressCardClickId == task.Id) { suppressCardClickId = ""; return; }
                selectedTaskId = task.Id; RenderCurrentView(); BeginPageTransition(detailView, 1);
            });
            taskCards[task.Id] = card; AttachTaskContextMenu(card); return card;
        }


        private void WireCardClicks(Control control, EventHandler handler)
        {
            if (control is Button) return;
            control.Click += handler;
            foreach (Control child in control.Controls) WireCardClicks(child, handler);
        }

        private void WireQueueDrag(Control control, TaskSnapshot task)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || layoutEditing || readFades.ContainsKey(task.Id) || GetResizeHitCode(PointToClient(Cursor.Position)) != 0) return;
                pressedCard = taskCards.ContainsKey(task.Id) ? taskCards[task.Id] : control as Panel;
                pressScreenPoint = Cursor.Position;
                dragHoldTimer.Stop();
                dragHoldTimer.Start();
            };
            control.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (pressedCard == null) return;
                if (draggingCard == null)
                {
                    if (e.Button != MouseButtons.Left) { dragHoldTimer.Stop(); pressedCard = null; return; }
                    Point cursor = Cursor.Position;
                    if (Math.Abs(cursor.X - pressScreenPoint.X) + Math.Abs(cursor.Y - pressScreenPoint.Y) > 9)
                    {
                        dragHoldTimer.Stop();
                        // Start a normal mouse drag as soon as the pointer crosses the
                        // movement threshold. Keep the stationary long-press path too,
                        // but do not cancel the gesture before it can capture the card.
                        BeginQueueDrag();
                        if (draggingCard != null) MoveQueueDrag();
                        return;
                    }
                    return;
                }
                MoveQueueDrag();
            };
            control.MouseUp += delegate(object sender, MouseEventArgs e) { EndQueuePress(); };
            foreach (Control child in control.Controls) WireQueueDrag(child, task);
        }

        private void BeginQueueDrag()
        {
            if (pressedCard == null || pressedCard.IsDisposed) return;
            var task = pressedCard.Tag as TaskSnapshot;
            if (task == null || task.IsCompleted || task.IsRead) { pressedCard = null; return; }
            draggingCard = pressedCard;
            dragPointerOffsetY = draggingCard.PointToClient(Cursor.Position).Y;
            dragLiveOrder = OrderedTasks(false).Where(t => !t.IsCompleted && taskCards.ContainsKey(t.Id))
                .Select(t => taskCards[t.Id]).ToList();
            if (!dragLiveOrder.Contains(draggingCard)) { draggingCard = null; pressedCard = null; return; }
            dragQueueBaseY = dragLiveOrder.Min(c => c.Top);
            draggingCard.BackColor = Color.FromArgb(255, 231, 240, 254);
            draggingCard.Cursor = Cursors.SizeAll;
            draggingCard.Capture = true;
            draggingCard.BringToFront();
        }


        private void MoveQueueDrag()
        {
            if (draggingCard == null || draggingCard.IsDisposed || dragLiveOrder.Count == 0) return;
            int cursorY = listItems.PointToClient(Cursor.Position).Y;
            int target = 0, y = dragQueueBaseY;
            var others = dragLiveOrder.Where(c => c != draggingCard).ToList();
            foreach (var card in others) { if (cursorY > y + card.Height / 2) target++; y += card.Height + GapSmall; }
            dragLiveOrder.Remove(draggingCard); dragLiveOrder.Insert(Math.Min(target, dragLiveOrder.Count), draggingCard);
            y = dragQueueBaseY;
            foreach (var card in dragLiveOrder) { if (card != draggingCard) card.Top = y; y += card.Height + GapSmall; }
            draggingCard.Top = cursorY - dragPointerOffsetY; ScheduleScrolledRefresh();
        }


        private void EndQueuePress()
        {
            dragHoldTimer.Stop();
            if (draggingCard != null)
            {
                Panel moved = draggingCard;
                var movedTask = moved.Tag as TaskSnapshot;
                moved.Capture = false;
                moved.Cursor = Cursors.Hand;
                moved.BackColor = Color.Transparent;
                int y = dragQueueBaseY;
                foreach (var card in dragLiveOrder) { card.Top = y; y += card.Height + GapSmall; }
                if (movedTask != null)
                {
                    var queuedIds = new HashSet<string>(dragLiveOrder.Select(c => ((TaskSnapshot)c.Tag).Id), StringComparer.Ordinal);
                    queueOrder.RemoveAll(id => queuedIds.Contains(id));
                    queueOrder.AddRange(dragLiveOrder.Select(c => ((TaskSnapshot)c.Tag).Id));
                    try { TaskStore.SaveQueueOrder(queueOrderFile, queueOrder); } catch { }
                    suppressCardClickId = movedTask.Id;
                    try { BeginInvoke((MethodInvoker)delegate { suppressCardClickId = ""; }); } catch { suppressCardClickId = ""; }
                }
                draggingCard = null;
                pressedCard = null;
                dragLiveOrder.Clear();
                ScheduleScrolledRefresh();
                return;
            }
            pressedCard = null;
        }


        private void RenderDetail(TaskSnapshot task)
        {
            detailItems.StopScrolling();
            DisposeChildren(detailItems);
            tracks.Clear(); glassCards.Clear(); entryMotions.Clear(); 
            detailItems.AutoScrollPosition = Point.Empty;
            detailItems.AutoScrollMinSize = Size.Empty;
            int width = Math.Max(180, detailItems.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 24);
            int line = Math.Max(22, Measure("M", UiFont(CardFont.Body)).Height + 4);
            var summary = new BufferedPanel { Location = new Point(12, 10), Size = new Size(width, line * 3 + 42) };
            StyleCard(summary);
            var heading = MakeLabel("总体进度", 8.5F, true, TextSoft);
            heading.Location = new Point(PadCardX, 10); summary.Controls.Add(heading);
            var percent = new PaintedGlyph(false) { Location = new Point(width - 92, 4), Size = new Size(76, line + 6) };
            percent.SetContent(task.Progress + "%", UiFont(CardFont.PercentLarge), task.IsCompleted ? Green : task.IsAwaitingConfirmation ? Amber : task.IsFailed ? Color.IndianRed : Teal);
            summary.Controls.Add(percent);
            var overall = new ProgressTrack { Location = new Point(PadCardX, line + 16), Size = new Size(width - PadCardX * 2, 7), Percent = task.Progress, Active = !task.IsCompleted && !task.IsWaiting && !task.IsAwaitingConfirmation && !task.IsFailed, Completed = task.IsCompleted, Stale = task.IsStale(DateTimeOffset.Now) || task.IsFailed, AwaitingConfirmation = task.IsAwaitingConfirmation };
            tracks.Add(overall); summary.Controls.Add(overall);
            var elapsed = MakeLabel(task.ElapsedText(), 8.5F, false, TextSoft);
            elapsed.Tag = new ClockBinding(task); elapsed.Location = new Point(PadCardX + 28, line + 32);
            summary.Controls.Add(elapsed);
            var timer = MakeGlyphLabel(Glyph.Timer, 24, TextSoft); timer.Location = new Point(PadCardX, line + 29); summary.Controls.Add(timer);
            var estimate = MakeLabel("预计 " + task.EstimateText(), 8F, false, TextSoft);
            estimate.Location = new Point(PadCardX, line * 2 + 32); summary.Controls.Add(estimate);
            detailItems.Controls.Add(summary); AttachContextMenu(summary);
            int y = summary.Bottom + GapCluster;
            y += AddTextCard(width, y, "任务需求", String.IsNullOrWhiteSpace(task.Requirements) ? "尚未上报需求" : task.Requirements);
            y += AddTextCard(width, y, task.IsAwaitingConfirmation ? "待确认事项" : "正在处理的问题", String.IsNullOrWhiteSpace(task.CurrentProblem) ? "尚未上报" : task.CurrentProblem);
            if (task.IsStale(DateTimeOffset.Now))
                y += AddTextCard(width, y, "更新间隔过长", "可能是网络超时或任务卡住导致长时间信息不更新", Color.FromArgb(186, 83, 94));
            y += AddTextCard(width, y, "当前使用的模型", task.Model);
            y += AddTextCard(width, y, "最近更新", "Agent 自报进度，未经独立验证\n" + task.UpdatedAt.ToString("MM-dd HH:mm:ss") + " · 汇报间隔 " + TaskStore.Duration(task.ReportIntervalSeconds)
                + (task.CompletedAt.HasValue ? "\n完成于 " + task.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : ""));
            if (task.Phases.Count == 0) y += AddTextCard(width, y, "分段任务", "尚未上报分段信息");
            for (int i = 0; i < task.Phases.Count; i++)
            {
                var card = CreatePhaseCard(task.Phases[i], i + 1, width);
                card.Location = new Point(12, y); detailItems.Controls.Add(card); y += card.Height + GapSmall;
            }
            detailItems.AutoScrollMinSize = new Size(0, y + 8);
            WireSmoothWheel(detailItems);
        }


        private void WireListWheel(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                child.MouseWheel += delegate(object sender, MouseEventArgs e) { listItems.ScrollByDelta(e.Delta); var h = e as HandledMouseEventArgs; if (h != null) h.Handled = true; };
                if (child.HasChildren) WireListWheel(child);
            }
        }

        private void WireSmoothWheel(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                child.MouseWheel += delegate(object sender, MouseEventArgs e) { detailItems.ScrollByDelta(e.Delta); var h = e as HandledMouseEventArgs; if (h != null) h.Handled = true; };
                if (child.HasChildren) WireSmoothWheel(child);
            }
        }

        private int AddTextCard(int width, int y, string heading, string body, Color? iconColor = null)
        {
            Font headingFont = UiFont(CardFont.SectionLabel);
            Font bodyFont = UiFont(CardFont.Body);
            Font iconFont = UiFont(CardFont.Icon);

            int iconSize = GlyphBoxSize(GlyphForHeading(heading), iconFont);
            int headingX = PadCardX + iconSize + GapIconText;
            int headingRowHeight = Math.Max(iconSize, Measure("M", headingFont).Height);
            int innerWidth = Math.Max(120, width - PadCardX * 2);
            Size bodySize = MeasureWrapped(body, bodyFont, innerWidth);
            int bodyTop = PadCardTop + headingRowHeight + GapSmall;
            int height = bodyTop + Math.Max(16, bodySize.Height) + PadCardBottom;

            var card = new BufferedPanel { Location = new Point(12, y), Size = new Size(width, height), BackColor = Color.Transparent, BorderStyle = BorderStyle.None };
            StyleCard(card);

            var headingIcon = MakeGlyphLabel(GlyphForHeading(heading), iconSize, iconColor.HasValue ? iconColor.Value : Teal);
            headingIcon.Location = new Point(PadCardX, PadCardTop);
            headingIcon.Size = new Size(iconSize, iconSize);
            card.Controls.Add(headingIcon);

            var title = MakeLabel(heading, headingFont.Size, true, Teal);
            title.Location = new Point(headingX, PadCardTop + Math.Max(0, (iconSize - Measure("M", headingFont).Height) / 2) - 1);
            card.Controls.Add(title);

            var text = new Label { Text = body, Font = bodyFont, ForeColor = TextMain, BackColor = Color.Transparent, Location = new Point(PadCardX, bodyTop), Size = new Size(innerWidth, Math.Max(16, bodySize.Height) + 2), AutoEllipsis = false, UseMnemonic = false };
            card.Controls.Add(text);
            AttachContextMenu(card);
            detailItems.Controls.Add(card);
            return height + GapSmall + 2;
        }

        private static string GlyphForHeading(string heading)
        {
            if (heading.Contains("更新")) return Glyph.Warning;
            if (heading.Contains("模型")) return Glyph.Model;
            if (heading.Contains("问题")) return Glyph.Problem;
            if (heading.Contains("需求")) return Glyph.Requirements;
            return Glyph.Phases;
        }

        private Panel CreatePhaseCard(Dictionary<string, object> phase, int index, int width)
        {
            string status = TaskStore.Text(phase, "status");
            bool done = status == "completed", waiting = status == "waiting" || status == "awaiting_confirmation", pending = status == "pending";
            Color accent = done ? Green : waiting ? Amber : pending ? TextSoft : Teal;
            int percent = done ? 100 : (int)(TaskStore.Number(phase, "progress") ?? 0);
            string title = index + ". " + TaskStore.Text(phase, "name");
            int inner = width - PadCardX * 2;
            int titleHeight = Math.Max(24, MeasureWrapped(title, UiFont(CardFont.Title), inner).Height + 2);
            var card = new BufferedPanel { Width = width, Height = 1 };
            StyleCard(card);
            var name = new Label { Text = title, Font = UiFont(CardFont.Title), ForeColor = TextMain, BackColor = Color.Transparent, UseMnemonic = false,
                Location = new Point(PadCardX, 12), Size = new Size(inner, titleHeight) };
            card.Controls.Add(name);
            int y = name.Bottom + 6;
            var icon = MakeGlyphLabel(done ? Glyph.Completed : waiting ? Glyph.Waiting : pending ? Glyph.Pending : Glyph.Running, 24, accent);
            icon.Location = new Point(PadCardX, y); card.Controls.Add(icon);
            var state = MakeLabel((done ? "已完成" : status == "awaiting_confirmation" ? "待确认" : waiting ? "等待中" : pending ? "未开始" : "处理中") + " · " + percent + "%", 8F, false, accent);
            state.Location = new Point(PadCardX + 30, y + 3); card.Controls.Add(state);
            y += 32;
            var bar = new ProgressTrack { Location = new Point(PadCardX, y), Size = new Size(inner, 6), Percent = percent, Completed = done, AwaitingConfirmation = status == "awaiting_confirmation", Active = !done && !waiting && !pending };
            tracks.Add(bar); card.Controls.Add(bar); y += 17;
            double? low = TaskStore.Number(phase, "estimate_min_seconds"), high = TaskStore.Number(phase, "estimate_max_seconds");
            string range = low.HasValue && high.HasValue ? TaskStore.Duration(low.Value) + (low == high ? "" : "–" + TaskStore.Duration(high.Value)) : "未提供";
            var estimate = MakeLabel("预计 " + range, 8F, false, TextSoft); estimate.Location = new Point(PadCardX, y); card.Controls.Add(estimate); y += estimate.PreferredHeight + 6;
            var elapsed = MakeLabel("", 8F, false, TextSoft); elapsed.Tag = new PhaseClockBinding(phase);
            elapsed.Location = new Point(PadCardX, y); card.Controls.Add(elapsed); y += elapsed.PreferredHeight + 8;
            string note = TaskStore.Text(phase, "note");
            if (!String.IsNullOrWhiteSpace(note))
            {
                int height = MeasureWrapped(note, UiFont(CardFont.Body), inner).Height + 4;
                card.Controls.Add(new Label { Text = note, Font = UiFont(CardFont.Body), ForeColor = TextMain, BackColor = Color.Transparent, UseMnemonic = false, Location = new Point(PadCardX, y), Size = new Size(inner, height) });
                y += height + 8;
            }
            if (done)
            {
                var completed = TaskStore.Date(phase, "completed_at");
                var time = MakeLabel("完成于 " + (completed.HasValue ? completed.Value.ToString("MM-dd HH:mm:ss") : "未记录"), 8F, false, Green);
                time.Location = new Point(PadCardX, y); card.Controls.Add(time); y += time.PreferredHeight + 6;
            }
            card.Height = y + 8; AttachContextMenu(card); UpdateClockLabels(card); return card;
        }


        private void UpdateClockLabels(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                var label = control as Label;
                if (label != null)
                {
                    var taskBinding = label.Tag as ClockBinding;
                    if (taskBinding != null)
                    {
                        // The compact list row has its timer glyph painted by a sibling
                        // control, so no glyph prefix is prepended here.
                        label.Text = taskBinding.Task.ElapsedText();
                    }
                    var phaseBinding = label.Tag as PhaseClockBinding;
                    if (phaseBinding != null)
                    {
                        var started = TaskStore.Date(phaseBinding.Phase, "started_at");
                        var finished = TaskStore.Text(phaseBinding.Phase, "status") == "completed" ? TaskStore.Date(phaseBinding.Phase, "completed_at") : (DateTimeOffset?)null;
                        bool phaseCompleted = TaskStore.Text(phaseBinding.Phase, "status") == "completed";
                        bool confirmationTimeOnly = TaskStore.Text(phaseBinding.Phase, "completion_time_accuracy") == "confirmation_time";
                        label.Text = "本段 " + (phaseCompleted && (confirmationTimeOnly || !finished.HasValue) ? "未记录" : started.HasValue ? TaskStore.Duration(Math.Max(0, ((finished ?? DateTimeOffset.Now) - started.Value).TotalSeconds)) : "未开始");
                    }
                }
                if (control.HasChildren) UpdateClockLabels(control);
            }
        }

        private static void DisposeChildren(Control parent)
        {
            var old = parent.Controls.Cast<Control>().ToArray();
            parent.Controls.Clear();
            foreach (var child in old) child.Dispose();
        }
    }

}
