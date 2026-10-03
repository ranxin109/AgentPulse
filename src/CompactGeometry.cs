using System;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace TaskProgressWidget
{
    internal static class CompactGeometry
    {
        internal static Rectangle ExpandedAt(Rectangle bubble,Size expanded,Rectangle area)
        {
            int left = area.Right - bubble.Left >= expanded.Width ? bubble.Left : bubble.Right - expanded.Width;
            int top = area.Bottom - bubble.Top >= expanded.Height ? bubble.Top : bubble.Bottom - expanded.Height;
            return ScreenBounds.Limit(new Rectangle(left,top,expanded.Width,expanded.Height),area,Size.Empty,false,0);
        }
        internal static Rectangle? Load(string path)
        {
            if(!File.Exists(path))return null;
            int[] values=new JavaScriptSerializer().Deserialize<int[]>(File.ReadAllText(path));
            if(values==null || values.Length!=4 || values[2]<44 || values[2]>128 || values[3]!=values[2])throw new InvalidDataException("Invalid compact geometry.");
            return new Rectangle(values[0],values[1],values[2],values[3]);
        }
        internal static void Save(string path,Rectangle bounds)
        {
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(new[]{bounds.Left,bounds.Top,bounds.Width,bounds.Height}));
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
