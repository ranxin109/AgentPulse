using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskProgressWidget
{
    // A separate per-pixel-alpha surface avoids the binary HWND Region edges
    // and opaque panel repaints used by the expanded list.
    internal sealed class CompactRingWindow : Form
    {
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; public NativePoint(int x, int y) { X=x; Y=y; } }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; public NativeSize(int w, int h) { Width=w; Height=h; } }
        [StructLayout(LayoutKind.Sequential, Pack=1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dc, ref NativePoint position, ref NativeSize size, IntPtr source, ref NativePoint origin, int key, ref Blend blend, int flags);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        private int lastPercent = -2;
        private bool lastStale;
        private bool lastConfirmation;
        private int lastSize;
        private Rectangle dragWorkArea;
        private bool dragging;
        private bool sizeAdjustment;
        public event EventHandler GeometryCommitted;
        public void EnableSizeAdjustment() { sizeAdjustment=true; Cursor=Cursors.SizeAll; }
        private void CommitGeometry() { var handler=GeometryCommitted; if(handler!=null)handler(this,EventArgs.Empty); }
        private int ResizeHit(Point point) { return sizeAdjustment && point.X > Width/2 && point.Y > Height/2 && point.X+point.Y > Width*1.25 ? 17 : 2; }

        public CompactRingWindow(ContextMenuStrip menu)
        {
            Text = "任务列表"; FormBorderStyle = FormBorderStyle.None;
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ShowInTaskbar = true; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            ContextMenuStrip = menu; Cursor = Cursors.SizeAll;
            MinimumSize=new Size(44,44); MaximumSize=new Size(128,128);
            Size=new Size(62,62);
        }

        protected override CreateParams CreateParams
        {
            get { var parameters=base.CreateParams; parameters.ExStyle |= 0x80000; return parameters; }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(ResizeHit(e.Location)), IntPtr.Zero);
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x231) { dragging=true; dragWorkArea=Screen.FromRectangle(Bounds).WorkingArea; }
            if(message.Msg==0x84 && sizeAdjustment)
            {
                long packed=message.LParam.ToInt64();
                var point=PointToClient(new Point(unchecked((short)(packed&0xffff)),unchecked((short)((packed>>16)&0xffff))));
                if(ResizeHit(point)==17){message.Result=new IntPtr(17);return;}
            }
            if(dragging && message.Msg==0x214)
            {
                var proposed=(ResizeRect)Marshal.PtrToStructure(message.LParam,typeof(ResizeRect));
                int diameter=Math.Max(44,Math.Min(128,Math.Max(proposed.Right-proposed.Left,proposed.Bottom-proposed.Top)));
                diameter=Math.Min(diameter,Math.Min(dragWorkArea.Right-proposed.Left,dragWorkArea.Bottom-proposed.Top));
                proposed.Right=proposed.Left+diameter;proposed.Bottom=proposed.Top+diameter;
                Marshal.StructureToPtr(proposed,message.LParam,false);
            }
            if(dragging && ScreenBounds.LimitMessage(ref message,dragWorkArea,new Size(44,44))) return;
            base.WndProc(ref message);
            if(message.Msg==0x232) { dragging=false; ClampToScreen(); CommitGeometry(); }
        }
        [StructLayout(LayoutKind.Sequential)] private struct ResizeRect { public int Left,Top,Right,Bottom; }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if(IsHandleCreated && lastPercent>=-1)
                using(var bitmap=RenderBitmap(Width,lastPercent,lastStale,lastConfirmation)) Present(bitmap);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int diameter=Math.Max(44,Math.Min(128,Width+(e.Delta>0 ? 4 : -4)));
            var anchor=new Point(Right-diameter,Bottom-diameter);
            Bounds=new Rectangle(anchor,new Size(diameter,diameter));
            ClampToScreen(); CommitGeometry();
        }
        public void ClampToScreen()
        {
            var area=Screen.FromRectangle(Bounds).WorkingArea;
            Location=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));
        }

        public void RefreshTask(TaskSnapshot task)
        {
            int percent=task == null ? -1 : task.Progress;
            bool stale=task != null && (task.IsStale(DateTimeOffset.Now) || task.IsFailed);
            bool confirmation=task != null && task.IsAwaitingConfirmation;
            if (percent==lastPercent && stale==lastStale && confirmation==lastConfirmation && Width==lastSize) return;
            using (var bitmap=RenderBitmap(Width,percent,stale,confirmation)) Present(bitmap);
            lastPercent=percent; lastStale=stale; lastConfirmation=confirmation; lastSize=Width;
        }

        internal static Bitmap RenderBitmap(int size,int percent,bool stale,bool confirmation=false)
        {
            const int scale=3;
            var result=new Bitmap(size,size,PixelFormat.Format32bppPArgb);
            using (var high=new Bitmap(size*scale,size*scale,PixelFormat.Format32bppPArgb))
            {
                using (var graphics=Graphics.FromImage(high))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.ScaleTransform(scale,scale);
                    graphics.SmoothingMode=SmoothingMode.AntiAlias;
                    float unit=size/62F;
                    // Transparent margin preserves soft anti-aliased circumference.
                    var body=new RectangleF(2*unit,2*unit,size-4*unit,size-4*unit);
                    using(var fill=new SolidBrush(Color.FromArgb(246,250,252,255))) graphics.FillEllipse(fill,body);
                    float stroke=4*unit;
                    var ring=new RectangleF(5*unit,5*unit,size-10*unit,size-10*unit);
                    using(var track=new Pen(Color.FromArgb(210,217,225,236),stroke)) graphics.DrawEllipse(track,ring);
                    if(percent>0 || confirmation || stale)
                    {
                        Color start=confirmation ? Color.FromArgb(224,151,32) : stale ? Color.FromArgb(206,70,91) : Color.FromArgb(57,99,243);
                        Color end=confirmation ? Color.FromArgb(249,192,67) : stale ? Color.FromArgb(234,130,131) : Color.FromArgb(39,191,217);
                        using(var gradient=new LinearGradientBrush(ring,start,end,30F))
                        using(var pen=new Pen(gradient,stroke){StartCap=LineCap.Round,EndCap=LineCap.Round})
                            graphics.DrawArc(pen,ring,-90,Math.Max((confirmation || stale) ? 5 : 0,Math.Min(100,percent))*3.6F);
                    }
                    graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using(var font=new Font("Microsoft YaHei UI",12*unit,FontStyle.Bold,GraphicsUnit.Pixel))
                    using(var brush=new SolidBrush(confirmation ? Color.FromArgb(195,128,23) : stale ? Color.FromArgb(185,66,83) : Color.FromArgb(48,65,95)))
                    using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                        graphics.DrawString(percent<0 ? "—" : percent+"%",font,brush,new RectangleF(0,0,size,size),format);
                }
                using(var graphics=Graphics.FromImage(result))
                {
                    graphics.CompositingMode=CompositingMode.SourceCopy;
                    graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    graphics.DrawImage(high,new Rectangle(0,0,size,size),new Rectangle(0,0,high.Width,high.Height),GraphicsUnit.Pixel);
                }
            }
            return result;
        }

        private void Present(Bitmap bitmap)
        {
            IntPtr screen=GetDC(IntPtr.Zero), memory=IntPtr.Zero, native=IntPtr.Zero, previous=IntPtr.Zero;
            try
            {
                memory=CreateCompatibleDC(screen); native=bitmap.GetHbitmap(Color.FromArgb(0));
                previous=SelectObject(memory,native);
                var location=new NativePoint(Left,Top); var size=new NativeSize(bitmap.Width,bitmap.Height); var origin=new NativePoint(0,0);
                var blend=new Blend{Operation=0,Alpha=255,Format=1};
                if(!UpdateLayeredWindow(Handle,screen,ref location,ref size,memory,ref origin,0,ref blend,2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if(previous!=IntPtr.Zero) SelectObject(memory,previous);
                if(native!=IntPtr.Zero) DeleteObject(native);
                if(memory!=IntPtr.Zero) DeleteDC(memory);
                if(screen!=IntPtr.Zero) ReleaseDC(IntPtr.Zero,screen);
            }
        }
    }
}
