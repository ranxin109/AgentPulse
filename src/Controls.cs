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
    internal class BufferedPanel : Panel
    {
        private float revealAmount = 1F;
        public float RevealAmount
        {
            get { return revealAmount; }
            set { revealAmount = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }

        public BufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
        }
    }

    internal sealed class StatusGlyph : Control
    {
        public string State = "active";
        public StatusGlyph() { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float x = Width / 2F, y = Height / 2F;
            using (var pen = new Pen(ForeColor, 1.7F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (State == "waiting") { e.Graphics.DrawLine(pen,x-3,y-5,x-3,y+5); e.Graphics.DrawLine(pen,x+3,y-5,x+3,y+5); }
                else if (State == "completed") e.Graphics.DrawLines(pen,new[] {new PointF(x-5,y),new PointF(x-1,y+4),new PointF(x+6,y-4)});
                else e.Graphics.DrawPolygon(pen,new[] {new PointF(x-4,y-5),new PointF(x+5,y),new PointF(x-4,y+5)});
            }
        }
    }

    internal sealed class SmoothScrollPanel : BufferedPanel
    {
        private readonly System.Windows.Forms.Timer smoothTimer;
        private DateTimeOffset startedAt;
        private int startY;
        private int targetY;
        public bool IsScrolling { get { return smoothTimer.Enabled; } }
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x02000000; return parameters; }
        }
        public bool HideScrollBars { get; set; }
        private bool hidingScrollBars;
        [DllImport("user32.dll")] private static extern bool ShowScrollBar(IntPtr handle, int bar, bool show);
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (HideScrollBars && !hidingScrollBars && IsHandleCreated && (m.Msg == 0x85 || m.Msg == 0x5 || m.Msg == 0x47))
            {
                hidingScrollBars = true;
                try { ShowScrollBar(Handle, 3, false); } finally { hidingScrollBars = false; }
            }
        }

        public SmoothScrollPanel()
        {
            smoothTimer = new System.Windows.Forms.Timer { Interval = 16 };
            smoothTimer.Tick += delegate
            {
                double amount = Math.Max(0, Math.Min(1, (DateTimeOffset.Now - startedAt).TotalMilliseconds / 140.0));
                double eased = 1 - Math.Pow(1 - amount, 3);
                AutoScrollPosition = new Point(0, startY + (int)Math.Round((targetY - startY) * eased));
                if (amount >= 1)
                {
                    AutoScrollPosition = new Point(0, targetY);
                    smoothTimer.Stop();
                }
            };
        }

        public void StopScrolling() { smoothTimer.Stop(); }

        public void ScrollByDelta(int wheelDelta)
        {
            if (wheelDelta == 0 || !AutoScroll) return;
            int currentY = Math.Max(0, -AutoScrollPosition.Y);
            if (!smoothTimer.Enabled) targetY = currentY;
            int lines = SystemInformation.MouseWheelScrollLines;
            int distance = lines < 0 ? Math.Max(48, ClientSize.Height - 24) : Math.Max(24, lines * 24);
            double notches = wheelDelta / 120.0;
            int maxY = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);
            targetY = Math.Max(0, Math.Min(maxY, targetY - (int)Math.Round(notches * distance)));
            startY = currentY;
            startedAt = DateTimeOffset.Now;
            smoothTimer.Stop();
            smoothTimer.Start();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollByDelta(e.Delta);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) smoothTimer.Stop();
            base.OnMouseDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) smoothTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class ReadFade
    {
        public readonly TaskSnapshot Task;
        public readonly Panel Card;
        public readonly DateTimeOffset StartedAt;
        public readonly int OriginalTop;
        public readonly Dictionary<Control, Color> ForeColors = new Dictionary<Control, Color>();
        public float Amount;
        public bool Committed;

        public ReadFade(TaskSnapshot task, Panel card)
        {
            Task = task;
            Card = card;
            StartedAt = DateTimeOffset.Now;
            OriginalTop = card.Top;
            Capture(card);
        }

        private void Capture(Control control)
        {
            if (control is Label || control is Button) ForeColors[control] = control.ForeColor;
            foreach (Control child in control.Controls) Capture(child);
        }
    }

    internal sealed class EntryMotion
    {
        public readonly Panel Card;
        public readonly int TargetTop;
        public readonly DateTimeOffset StartedAt;
        public readonly int DelayMs;

        public EntryMotion(Panel card, int targetTop, DateTimeOffset startedAt, int delayMs = 0)
        {
            Card = card;
            TargetTop = targetTop;
            StartedAt = startedAt;
            DelayMs = delayMs;
        }
    }

    internal enum WidgetIconKind { History, Check, Back }

    internal sealed class WidgetIconButton : Button
    {
        private bool hot;
        private bool isChecked;
        private bool selected;
        private float fadeAmount;
        public readonly WidgetIconKind Kind;
        public bool Checked { get { return isChecked; } set { isChecked = value; Invalidate(); } }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public float FadeAmount { get { return fadeAmount; } set { fadeAmount = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

        public WidgetIconButton(WidgetIconKind kind)
        {
            Kind = kind;
            Text = "";
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TabStop = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            if (hot || Selected)
            {
                using (var path = DrawingHelpers.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 7))
                using (var brush = new SolidBrush(DrawingHelpers.Blend(Color.FromArgb(231, 238, 248), BackColor, FadeAmount))) e.Graphics.FillPath(brush, path);
            }

            float cx = Width / 2F;
            float cy = Height / 2F;
            // Everything is derived from the smaller dimension with a stroke-aware inset.
            // Hard-coding an 8px radius in a 26px box left only two pixels under the circle,
            // so GDI+ antialiasing clipped the bottom arc and the icon looked like a smile.
            const float stroke = 1.6F;
            float inset = stroke + 1.5F;
            float radius = Math.Max(3F, Math.Min(Width, Height) / 2F - inset);
            float inner = radius - stroke;
            Color accent = DrawingHelpers.Blend(Kind == WidgetIconKind.Check ? ProgressWindow.GreenColor : ProgressWindow.AccentColor, BackColor, FadeAmount);
            using (var pen = new Pen(accent, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (Kind == WidgetIconKind.History)
                {
                    e.Graphics.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                    e.Graphics.DrawLine(pen, cx, cy - inner * 0.55F, cx, cy);
                    e.Graphics.DrawLine(pen, cx, cy, cx + inner * 0.5F, cy + inner * 0.3F);
                    e.Graphics.DrawArc(pen, cx - radius - stroke, cy - radius - stroke, (radius + stroke) * 2, (radius + stroke) * 2, 198, 54);
                }
                else if (Checked)
                {
                    using (var brush = new SolidBrush(accent)) e.Graphics.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
                    using (var checkPen = new Pen(DrawingHelpers.Blend(Color.White, BackColor, FadeAmount), stroke + 0.2F))
                    {
                        checkPen.StartCap = LineCap.Round;
                        checkPen.EndCap = LineCap.Round;
                        float u = radius / 8F;
                        e.Graphics.DrawLine(checkPen, cx - 4 * u, cy, cx - u, cy + 3 * u);
                        e.Graphics.DrawLine(checkPen, cx - u, cy + 3 * u, cx + 5 * u, cy - 4 * u);
                    }
                }
                else if (Kind == WidgetIconKind.Back)
                {
                    // Drawn as a vector chevron so the header never depends on an icon
                    // font being installed.
                    float a = Math.Min(Width, Height) / 2F - stroke - 2F;
                    e.Graphics.DrawLine(pen, cx + a * 0.4F, cy - a * 0.7F, cx - a * 0.6F, cy);
                    e.Graphics.DrawLine(pen, cx - a * 0.6F, cy, cx + a * 0.4F, cy + a * 0.7F);
                }
                else e.Graphics.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
            }
        }
    }

    internal sealed class ClockBinding
    {
        public readonly TaskSnapshot Task;
        public ClockBinding(TaskSnapshot task) { Task = task; }
    }

    internal sealed class PhaseClockBinding
    {
        public readonly Dictionary<string, object> Phase;
        public PhaseClockBinding(Dictionary<string, object> phase) { Phase = phase; }
    }

    internal sealed class ProgressTrack : Panel
    {
        private int percent;
        private int shimmerX;
        private float fadeAmount;
        public int Percent { get { return percent; } set { percent = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        public bool Active { get; set; }
        public bool Completed { get; set; }
        public bool Stale { get; set; }
        public bool AwaitingConfirmation { get; set; }
        public float FadeAmount { get { return fadeAmount; } set { fadeAmount = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public ProgressTrack() { DoubleBuffered = true; BackColor = Color.Transparent; }

        public void Animate()
        {
            if (!Active || !Visible || Width < 4) return;
            if (!VisibleInScrollViewport()) return;
            shimmerX = (shimmerX + 4) % Math.Max(1, Width + 44);
            Invalidate();
        }

        private bool VisibleInScrollViewport()
        {
            Control parent = Parent;
            if (parent == null) return false;
            Control viewport = parent;
            while (viewport != null)
            {
                var scrollable = viewport as ScrollableControl;
                if (scrollable != null && scrollable.AutoScroll) { var smooth = scrollable as SmoothScrollPanel; if (smooth != null && smooth.IsScrolling) return false; break; }
                viewport = viewport.Parent;
            }
            if (viewport == null) return Visible;
            try
            {
                Point origin = viewport.PointToClient(parent.PointToScreen(Location));
                return viewport.ClientRectangle.IntersectsWith(new Rectangle(origin, Size));
            }
            catch { return false; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(1, 0, Width - 3, Height - 1);
            using (var path = DrawingHelpers.RoundedPath(bounds, Height / 2))
            using (var brush = new SolidBrush(DrawingHelpers.Blend(Color.FromArgb(229, 235, 244), ProgressWindow.CanvasColor, FadeAmount))) e.Graphics.FillPath(brush, path);
            int fillWidth = Completed ? bounds.Width : (int)Math.Round(bounds.Width * percent / 100.0);
            // A paused task at 0% must still have a visible orange status indicator.
            if (AwaitingConfirmation || Stale) fillWidth = Math.Max(Math.Min(12, bounds.Width), fillWidth);
            if (fillWidth <= 0)
            {
                if (Active)
                {
                    int segmentWidth = Math.Min(42, bounds.Width);
                    int x = (shimmerX % (Width + segmentWidth)) - segmentWidth;
                    var state = e.Graphics.Save();
                    e.Graphics.SetClip(bounds);
                    var segment = new Rectangle(x + bounds.X, 0, segmentWidth, Height - 1);
                    using (var path = DrawingHelpers.RoundedPath(segment, Height / 2))
                    using (var brush = new LinearGradientBrush(segment, DrawingHelpers.Blend((Stale ? Color.FromArgb(225, 133, 142) : Color.FromArgb(150, 196, 240)), ProgressWindow.CanvasColor, FadeAmount), DrawingHelpers.Blend((AwaitingConfirmation ? Color.FromArgb(224, 151, 32) : Stale ? Color.FromArgb(197, 66, 82) : Color.FromArgb(48, 112, 232)), ProgressWindow.CanvasColor, FadeAmount), 0F)) e.Graphics.FillPath(brush, path);
                    e.Graphics.Restore(state);
                }
                return;
            }
            var fill = new Rectangle(bounds.X, 0, Math.Max(1, fillWidth), Height - 1);
            using (var path = DrawingHelpers.RoundedPath(fill, Height / 2))
            using (var brush = new LinearGradientBrush(fill,
                DrawingHelpers.Blend(Completed ? Color.FromArgb(37, 149, 97) : (AwaitingConfirmation ? Color.FromArgb(224, 151, 32) : Stale ? Color.FromArgb(197, 66, 82) : Color.FromArgb(48, 112, 232)), ProgressWindow.CanvasColor, FadeAmount),
                DrawingHelpers.Blend(Completed ? Color.FromArgb(53, 171, 118) : (AwaitingConfirmation ? Color.FromArgb(249, 192, 67) : Stale ? Color.FromArgb(232, 113, 124) : Color.FromArgb(79, 145, 244)), ProgressWindow.CanvasColor, FadeAmount), 0F)) e.Graphics.FillPath(brush, path);
            if (Active && fillWidth > 18)
            {
                var state = e.Graphics.Save();
                e.Graphics.SetClip(new Rectangle(bounds.X, 0, fillWidth, Height));
                int x = bounds.X + shimmerX - 40;
                using (var brush = new LinearGradientBrush(new Rectangle(x, 0, 40, Height), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(75, 255, 255, 255), 0F)) e.Graphics.FillRectangle(brush, x, 0, 40, Height);
                e.Graphics.Restore(state);
            }
        }
    }
    internal static class Glyph
    {
        public const string Task = "\uE9D5";       // 任务 / 诊断式图标
        public const string Phases = "\uE8FD";     // 分段任务（清单）
        public const string Requirements = "\uE8A5"; // 需求（文档）
        public const string Problem = "\uE946";    // 当前问题（信息）
        public const string Model = "\uE8D4";      // 模型（机器人）
        public const string Timer = "\uE916";      // 耗时（秒表）
        public const string Running = "\uE768";    // 处理中
        public const string Waiting = "\uE769";    // 等待中
        public const string Pending = "\uE9F5";    // 未开始
        public const string Completed = "\uE73E";  // 已完成
        public const string Warning = "\uE7BA";    // 警告
        public const string History = "\uE81C";    // 历史记录
        public const string Back = "\uE76B";       // 返回
        public const string Grip = "\uE7C2";       // 拖动区
        public const string GripDots = "\uE784";   // 可拖动提示
    }
    internal sealed class PaintedGlyph : Panel
    {
        private readonly bool useGlyphFont;
        private string content = "";
        private Font contentFont;
        private Color contentColor = Color.White;

        public PaintedGlyph(bool glyphFont)
        {
            useGlyphFont = glyphFont;
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void SetContent(string text, Font font, Color color)
        {
            content = text ?? "";
            contentFont = font;
            contentColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (content.Length == 0 || contentFont == null || Width < 4 || Height < 4) return;
            e.Graphics.TextRenderingHint = useGlyphFont
                ? System.Drawing.Text.TextRenderingHint.AntiAliasGridFit
                : System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (useGlyphFont)
            {
                // Glyph is centred both ways inside an oversized box. Anchoring it to the
                // top-left corner (the previous behaviour) shaved the bottom off taller
                // Fluent glyphs whose ink exceeds the reported line height.
                TextRenderer.DrawText(e.Graphics, content, contentFont, ClientRectangle, contentColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                return;
            }

            // Text variant stays right-aligned, but is vertically centred and given a
            // two-pixel inset so descenders are not clipped by the control edge.
            Size measured = TextRenderer.MeasureText(content, contentFont, new Size(1000, Height),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int x = Math.Max(0, Width - measured.Width - 2);
            int y = Math.Max(0, (Height - measured.Height) / 2);
            TextRenderer.DrawText(e.Graphics, content, contentFont, new Point(x, y), contentColor,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    internal static class DrawingHelpers
    {
        public static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                (int)Math.Round(from.A + (to.A - from.A) * amount),
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
        var path = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
        }
    }
}
