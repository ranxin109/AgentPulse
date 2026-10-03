using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskProgressWidget
{
    internal static class ScreenBounds
    {
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
        public static bool LimitMessage(ref Message message, Rectangle area, Size minimum)
        {
            if ((message.Msg != 0x216 && message.Msg != 0x214) || message.LParam == IntPtr.Zero) return false;
            var native=(NativeRect)Marshal.PtrToStructure(message.LParam,typeof(NativeRect));
            var proposed=Rectangle.FromLTRB(native.Left,native.Top,native.Right,native.Bottom);
            var result=Limit(proposed,area,minimum,message.Msg==0x214,message.WParam.ToInt32());
            native.Left=result.Left;native.Top=result.Top;native.Right=result.Right;native.Bottom=result.Bottom;
            Marshal.StructureToPtr(native,message.LParam,false);message.Result=new IntPtr(1);return true;
        }
        internal static Rectangle Limit(Rectangle proposed,Rectangle area,Size minimum,bool sizing,int edge)
        {
            if(!sizing)
            {
                int width=Math.Min(proposed.Width,area.Width),height=Math.Min(proposed.Height,area.Height);
                return new Rectangle(Math.Max(area.Left,Math.Min(proposed.Left,area.Right-width)),Math.Max(area.Top,Math.Min(proposed.Top,area.Bottom-height)),width,height);
            }
            int left=Math.Max(area.Left,Math.Min(proposed.Left,area.Right));
            int right=Math.Max(area.Left,Math.Min(proposed.Right,area.Right));
            int top=Math.Max(area.Top,Math.Min(proposed.Top,area.Bottom));
            int bottom=Math.Max(area.Top,Math.Min(proposed.Bottom,area.Bottom));
            int minWidth=Math.Min(minimum.Width,area.Width),minHeight=Math.Min(minimum.Height,area.Height);
            if(right-left<minWidth)
            {
                if(edge==1 || edge==4 || edge==7) left=right-minWidth; else right=left+minWidth;
            }
            if(bottom-top<minHeight)
            {
                if(edge==3 || edge==4 || edge==5) top=bottom-minHeight; else bottom=top+minHeight;
            }
            return Limit(Rectangle.FromLTRB(left,top,right,bottom),area,minimum,false,0);
        }
    }
}
