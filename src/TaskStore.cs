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
    internal sealed class TaskSnapshot
    {
        public readonly string FilePath;
        public readonly Dictionary<string, object> Data;
        public readonly string Id;
        public readonly string Title;
        public readonly string Status;
        public readonly DateTimeOffset StartedAt;
        public readonly DateTimeOffset UpdatedAt;
        public readonly DateTimeOffset? CompletedAt;
        public readonly DateTimeOffset? ReadAt;
        public readonly int Progress;
        public readonly int ReportIntervalSeconds;
        public readonly List<Dictionary<string, object>> Phases;

        public TaskSnapshot(string filePath, Dictionary<string, object> data)
        {
            FilePath = filePath;
            Data = data ?? new Dictionary<string, object>();
            Id = TaskStore.Text(Data, "task_id");
            if (String.IsNullOrWhiteSpace(Id)) Id = Path.GetFileNameWithoutExtension(filePath);
            Title = String.IsNullOrWhiteSpace(TaskStore.Text(Data, "title")) ? "未命名任务" : TaskStore.Text(Data, "title");
            Status = TaskStore.Text(Data, "status");
            StartedAt = TaskStore.Date(Data, "started_at") ?? TaskStore.Date(Data, "updated_at") ?? DateTimeOffset.Now;
            UpdatedAt = TaskStore.Date(Data, "updated_at") ?? StartedAt;
            CompletedAt = TaskStore.Date(Data, "completed_at");
            ReadAt = TaskStore.Date(Data, "read_at");
            ReportIntervalSeconds = (int)Math.Max(30, Math.Min(3600, TaskStore.Number(Data, "report_interval_seconds") ?? 180));
            Phases = TaskStore.Phases(Data);
            Progress = CalculateProgress();
        }

        public bool IsCompleted { get { return Status == "completed"; } }
        public bool IsWaiting { get { return Status == "waiting"; } }
        public bool IsAwaitingConfirmation { get { return Status == "awaiting_confirmation"; } }
        public bool IsFailed { get { return Status == "failed"; } }
        public bool IsFault(DateTimeOffset now) { return !IsRead && !IsAwaitingConfirmation && (IsFailed || IsStale(now)); }
        public bool IsRead { get { return ReadAt.HasValue; } }
        public bool IsStale(DateTimeOffset now)
        {
            if (IsCompleted || (Status != "active" && Status != "waiting")) return false;
            double staleAfter = Math.Max(120, ReportIntervalSeconds * 2.0);
            return (now - UpdatedAt).TotalSeconds >= staleAfter;
        }

        public string Requirements { get { return TaskStore.Text(Data, "requirements"); } }
        public string CurrentProblem { get { return TaskStore.Text(Data, "current_problem"); } }
        public string Model
        {
            get
            {
                string model = TaskStore.Text(Data, "model");
                if (String.IsNullOrWhiteSpace(model)) return "未上报（任务数据缺少 model 字段）";
                if (String.Equals(model, "未提供", StringComparison.Ordinal)) return "未上报（Agent 未填写 model）";
                return model;
            }
        }

        public int CalculateProgress()
        {
            if (IsCompleted) return 100;
            if (Phases.Count == 0) return (int)Math.Max(0, Math.Min(100, TaskStore.Number(Data, "progress") ?? 0));
            double totalWeight = 0, doneWeight = 0;
            foreach (var phase in Phases)
            {
                double low = TaskStore.Number(phase, "estimate_min_seconds") ?? 0;
                double high = TaskStore.Number(phase, "estimate_max_seconds") ?? low;
                double weight = Math.Max(0.001, TaskStore.Number(phase, "weight") ?? ((low + high) > 0 ? (low + high) / 2.0 : 1));
                double percent = TaskStore.Text(phase, "status") == "completed" ? 100 : (TaskStore.Number(phase, "progress") ?? 0);
                totalWeight += weight;
                doneWeight += weight * Math.Max(0, Math.Min(100, percent));
            }
            return totalWeight <= 0 ? 0 : Math.Max(0, Math.Min(100, (int)Math.Round(doneWeight / totalWeight)));
        }

        public string EstimateText()
        {
            double low = 0, high = 0;
            bool known = false;
            if (Phases.Count > 0)
            {
                foreach (var phase in Phases)
                {
                    var phaseLow = TaskStore.Number(phase, "estimate_min_seconds");
                    var phaseHigh = TaskStore.Number(phase, "estimate_max_seconds");
                    if (!phaseLow.HasValue || !phaseHigh.HasValue) return "未提供";
                    low += phaseLow.Value;
                    high += phaseHigh.Value;
                    known = true;
                }
            }
            else
            {
                var phaseLow = TaskStore.Number(Data, "estimated_total_min_seconds");
                var phaseHigh = TaskStore.Number(Data, "estimated_total_max_seconds");
                if (phaseLow.HasValue && phaseHigh.HasValue) { low = phaseLow.Value; high = phaseHigh.Value; known = true; }
            }
            if (!known) return "未提供";
            return TaskStore.Duration(low) + (Math.Abs(low - high) < 0.5 ? "" : "–" + TaskStore.Duration(high));
        }

        public double ElapsedSeconds(DateTimeOffset? phaseStarted, DateTimeOffset? phaseCompleted)
        {
            var start = phaseStarted ?? StartedAt;
            var stop = phaseCompleted ?? (IsCompleted ? CompletedAt : null) ?? DateTimeOffset.Now;
            return Math.Max(0, (stop - start).TotalSeconds);
        }

        public string ElapsedText()
        {
            if (IsCompleted && TaskStore.Text(Data, "completion_time_accuracy") == "confirmation_time") return "未记录";
            if (IsCompleted && !CompletedAt.HasValue) return "未记录";
            return TaskStore.Duration(ElapsedSeconds(null, null));
        }
    }

    internal static class TaskStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };

        public static List<TaskSnapshot> Load(string directory, out int errors)
        {
            errors = 0;
            var result = new List<TaskSnapshot>();
            if (!Directory.Exists(directory)) return result;
            foreach (string file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string json;
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true)) json = reader.ReadToEnd();
                    var data = Serializer.Deserialize<Dictionary<string, object>>(json);
                    if (data != null) result.Add(new TaskSnapshot(file, data));
                }
                catch { errors++; }
            }
            return result;
        }

        public static string Fingerprint(string directory)
        {
            if (!Directory.Exists(directory)) return "";
            return String.Join("|", Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => {
                    var info = new FileInfo(x);
                    return info.Name + ":" + info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                }).ToArray());
        }

        public static void MarkRead(string path)
        {
            // Fail promptly if a publisher owns the file; never stall the UI thread.
            using (var stateLock = File.Open(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
            string json;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true)) json = reader.ReadToEnd();
            var data = Serializer.Deserialize<Dictionary<string, object>>(json);
            if (data == null) return;
            if (Text(data, "status") != "completed") return;
            data["read_at"] = DateTimeOffset.Now.ToString("o");
            WriteAtomic(path, data);
            }
        }

        public static bool RecycleFault(string path)
        {
            using (var stateLock = File.Open(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (!File.Exists(path)) return false;
                var data = Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                if (data == null || !new TaskSnapshot(path, data).IsFault(DateTimeOffset.Now)) return false;
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
        }

        private static void WriteAtomic(string path, Dictionary<string, object> data)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Serializer.Serialize(data), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    string swap = path + ".swap." + Guid.NewGuid().ToString("N");
                    File.Replace(temp, path, swap, true);
                    try { File.Delete(swap); } catch { }
                }
                else File.Move(temp, path);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        public static object Get(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        public static string Text(Dictionary<string, object> map, string key)
        {
            var value = Get(map, key);
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static double? Number(Dictionary<string, object> map, string key)
        {
            var value = Get(map, key);
            if (value == null) return null;
            double number;
            return Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out number) && !Double.IsNaN(number) && !Double.IsInfinity(number) ? (double?)number : null;
        }

        public static DateTimeOffset? Date(Dictionary<string, object> map, string key)
        {
            var value = Text(map, key);
            DateTimeOffset result;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result) ? (DateTimeOffset?)result.ToLocalTime() : null;
        }

        public static List<Dictionary<string, object>> Phases(Dictionary<string, object> data)
        {
            var result = new List<Dictionary<string, object>>();
            var list = Get(data, "phases") as IEnumerable;
            if (list == null) return result;
            foreach (var entry in list)
            {
                var phase = entry as Dictionary<string, object>;
                if (phase != null) result.Add(phase);
            }
            return result;
        }

        public static string Duration(double seconds)
        {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return "未提供";
            var span = TimeSpan.FromSeconds(Math.Max(0, Math.Min(315360000, seconds)));
            if (span.TotalDays >= 1) return ((int)span.TotalDays).ToString(CultureInfo.InvariantCulture) + "天 " + span.ToString(@"hh\:mm\:ss");
            if (span.TotalHours >= 1) return span.ToString(@"hh\:mm\:ss");
            if (span.TotalMinutes >= 1) return ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "分 " + span.Seconds.ToString("00", CultureInfo.InvariantCulture) + "秒";
            return ((int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "秒";
        }

        public static List<TaskSnapshot> Ordered(List<TaskSnapshot> all, bool history, IList<string> queueOrder = null)
        {
            var source = history ? all.Where(t => t.IsRead) : all.Where(t => !t.IsRead);
            if (history)
                return source.OrderByDescending(t => t.ReadAt ?? t.CompletedAt ?? t.UpdatedAt).ToList();
            // Task age determines position. Updates and completion must not promote older tasks.
            // Retain the legacy queue argument for compatibility, but ignore manual ranks.
            return source.OrderByDescending(t => Date(t.Data, "created_at") ?? t.StartedAt)
                .ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        }

        public static List<string> LoadQueueOrder(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<string>();
                string json = File.ReadAllText(path, Encoding.UTF8);
                var order = Serializer.Deserialize<List<string>>(json);
                return order == null ? new List<string>() : order.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
            }
            catch { return new List<string>(); }
        }

        public static void SaveQueueOrder(string path, IList<string> order)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Serializer.Serialize(order.ToList()), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    string swap = path + ".swap." + Guid.NewGuid().ToString("N");
                    File.Replace(temp, path, swap, true);
                    try { File.Delete(swap); } catch { }
                }
                else File.Move(temp, path);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        public static void SelfTest()
        {
            var workArea=new Rectangle(-1920,20,1920,1040);
            if (ListGeometry.HeightFor(112,540,1080) != 112 || ListGeometry.HeightFor(700,540,1080) != 540 || ListGeometry.HeightFor(700,540,300) != 284)
                throw new Exception("Adaptive content height and limits");
            var nearbyBubble = new Rectangle(500,400,62,62);
            var nearbyList = CompactGeometry.ExpandedAt(nearbyBubble,new Size(310,112),new Rectangle(0,0,1920,1080));
            if (nearbyList.Location != nearbyBubble.Location) throw new Exception("Single-task restoration stays near circle");
            var edgeBubble = new Rectangle(1840,960,62,62);
            var edgeList = CompactGeometry.ExpandedAt(edgeBubble,new Size(310,112),new Rectangle(0,0,1920,1080));
            if (edgeList.Right != edgeBubble.Right || edgeList.Top != edgeBubble.Top || !new Rectangle(0,0,1920,1080).Contains(edgeList)) throw new Exception("Adaptive restoration uses available screen space");
            foreach(int edge in new[] {1,2,3,4,5,6,7,8})
            foreach(var candidate in new[] {new Rectangle(-2300,-300,2600,1800),new Rectangle(-30,1000,450,600),new Rectangle(-1500,100,10,10)})
            {
                var bounded=ScreenBounds.Limit(candidate,workArea,new Size(300,260),true,edge);
                if(!workArea.Contains(bounded) || bounded.Width<300 || bounded.Height<260) throw new Exception("Live resize screen bounds");
                var moved=ScreenBounds.Limit(candidate,workArea,new Size(300,260),false,edge);
                if(!workArea.Contains(moved)) throw new Exception("Live move screen bounds");
            }
            foreach (int percent in new[] {-1,0,1,50,95,100})
            using (var image = CompactRingWindow.RenderBitmap(93,percent,true))
            {
                if(image.GetPixel(0,0).A != 0 || image.GetPixel(92,92).A != 0) throw new Exception("Compact transparent corners");
                int soft=0;
                for(int x=0;x<93;x++) for(int y=0;y<93;y++) { int alpha=image.GetPixel(x,y).A; if(alpha>0 && alpha<230) soft++; }
                if(soft<30) throw new Exception("Compact antialiased silhouette");
            }
            string temp = Path.Combine(Path.GetTempPath(), "codex-task-widget-check-" + Guid.NewGuid().ToString("N"));
            using (var image = CompactRingWindow.RenderBitmap(93, 0, false, true))
            {
                int orange = 0;
                for (int x=0; x<93; x++) for (int y=0; y<93; y++) { var c=image.GetPixel(x,y); if (c.A>200 && c.R>190 && c.G>100 && c.G<210 && c.B<100) orange++; }
                if (orange < 10 || image.GetPixel(0,0).A != 0) throw new Exception("Confirmation ring orange indicator at zero progress");
            }
            Directory.CreateDirectory(temp);
            try
            {
                string geometryPath=Path.Combine(temp,"geometry.data");
                var bubble=new Rectangle(-150,750,62,62);
                CompactGeometry.Save(geometryPath,bubble);
                if(CompactGeometry.Load(geometryPath)!=bubble) throw new Exception("Compact geometry round trip");
                CompactGeometry.Save(geometryPath,new Rectangle(-300,640,88,88));
                if(CompactGeometry.Load(geometryPath).Value.Width!=88) throw new Exception("Compact resize overwrite");
                var expanded=CompactGeometry.ExpandedAt(bubble,new Size(346,457),new Rectangle(-1920,0,1920,1080));
                if(expanded.Right!=bubble.Right || expanded.Bottom!=bubble.Bottom)throw new Exception("Expanded follows compact anchor");
                var atEdge=CompactGeometry.ExpandedAt(new Rectangle(-1920,0,62,62),new Size(346,457),new Rectangle(-1920,0,1920,1080));
                if(atEdge.Left!=-1920 || atEdge.Top!=0)throw new Exception("Expanded respects screen edge");
                var oldDone = TestMap("older-done", "completed", 100, DateTimeOffset.Now.AddMinutes(-4), DateTimeOffset.Now.AddMinutes(-3));
                var newDone = TestMap("newer-done", "completed", 100, DateTimeOffset.Now.AddMinutes(-2), DateTimeOffset.Now.AddMinutes(-1));
                var active = TestMap("active", "active", 45, DateTimeOffset.Now.AddMinutes(-1), null);
                File.WriteAllText(Path.Combine(temp, "older-done.json"), Serializer.Serialize(oldDone), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(temp, "newer-done.json"), Serializer.Serialize(newDone), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(temp, "active.json"), Serializer.Serialize(active), new UTF8Encoding(false));
                int errors;
                var loaded = Load(temp, out errors);
                var ordered = Ordered(loaded, false);
                if (errors != 0 || ordered.Count != 3 || ordered[0].Id != "active" || ordered[1].Id != "newer-done" || ordered[2].Id != "older-done")
                    throw new InvalidOperationException("Task ordering check failed.");
                if (ordered[0].Progress != 45) throw new InvalidOperationException("Weighted progress fallback check failed.");
                MarkRead(Path.Combine(temp, "newer-done.json"));
                loaded = Load(temp, out errors);
                if (Ordered(loaded, false).Any(t => t.Id == "newer-done") || Ordered(loaded, true).Count != 1)
                    throw new InvalidOperationException("Read/archive check failed.");
                var map = TestMap("edge", "active", 999, DateTimeOffset.Now.AddMinutes(-10), null);
                if (new TaskSnapshot("edge.json", map).Progress != 100) throw new Exception("Progress upper bound");
                map["progress"] = -10;
                if (new TaskSnapshot("edge.json", map).Progress != 0) throw new Exception("Progress lower bound");
                map["progress"] = "NaN";
                if (Number(map, "progress").HasValue) throw new Exception("Nonfinite number");
                map["task_id"] = "";
                if (new TaskSnapshot("fallback.json", map).Id != "fallback") throw new Exception("Missing ID fallback");
                map["report_interval_seconds"] = 60;
                if (!new TaskSnapshot("edge.json", map).IsStale(DateTimeOffset.Now)) throw new Exception("Stale detection");
                map["status"] = "waiting";
                if (!new TaskSnapshot("edge.json", map).IsStale(DateTimeOffset.Now)) throw new Exception("Waiting stale detection");
                map["status"] = "awaiting_confirmation";
                var confirmation = new TaskSnapshot("edge.json", map);
                if (!confirmation.IsAwaitingConfirmation || confirmation.IsStale(DateTimeOffset.Now) || confirmation.IsFault(DateTimeOffset.Now)) throw new Exception("Confirmation state must remain orange and protected");
                var protectedPath = Path.Combine(temp, "protected.data");
                File.WriteAllText(protectedPath, Serializer.Serialize(map), new UTF8Encoding(false));
                if (RecycleFault(protectedPath) || !File.Exists(protectedPath)) throw new Exception("Confirmation cleanup protection");
                map["status"] = "failed";
                if (!new TaskSnapshot("edge.json", map).IsFault(DateTimeOffset.Now)) throw new Exception("Explicit failure eligible for cleanup");
                map["status"] = "active";
                if (!new TaskSnapshot("edge.json", map).IsFault(DateTimeOffset.Now)) throw new Exception("Stale active eligible for cleanup");
                map["updated_at"] = DateTimeOffset.Now.ToString("o");
                if (new TaskSnapshot("edge.json", map).IsFault(DateTimeOffset.Now)) throw new Exception("Fresh active protected from cleanup");
                File.WriteAllText(protectedPath, Serializer.Serialize(map), new UTF8Encoding(false));
                if (RecycleFault(protectedPath) || !File.Exists(protectedPath)) throw new Exception("Resumed task cleanup protection");
                MarkRead(Path.Combine(temp, "active.json"));
                MarkRead(Path.Combine(temp, "older-done.json"));
                loaded = Load(temp, out errors);
                if (Ordered(loaded, true).Count != 2 || loaded.First(t => t.Id == "active").IsRead) throw new Exception("Bulk acknowledgement completion guard");
                map["status"] = "completed";
                if (new TaskSnapshot("edge.json", map).ElapsedText() != "未记录") throw new Exception("Missing completion time");
                map["status"] = "active";
                map["phases"] = new ArrayList {
                    new Dictionary<string, object> { {"status", "completed"}, {"estimate_min_seconds",100}, {"estimate_max_seconds",100} },
                    new Dictionary<string, object> { {"status", "current"}, {"progress",20}, {"estimate_min_seconds",300}, {"estimate_max_seconds",300} }
                };
                if (new TaskSnapshot("edge.json", map).Progress != 40) throw new Exception("Weighted progress");
                map["phases"] = new ArrayList {
                    new Dictionary<string, object> { {"status", "completed"}, {"weight",1} },
                    new Dictionary<string, object> { {"status", "current"}, {"progress",20}, {"weight",3} }
                };
                if (new TaskSnapshot("edge.json", map).Progress != 40) throw new Exception("Explicit phase weights");
                map["updated_at"] = DateTimeOffset.Now.ToString("o");
                if (new TaskSnapshot("edge.json", map).IsStale(DateTimeOffset.Now)) throw new Exception("Fresh report recovery");
                map["status"] = "completed";
                map["updated_at"] = DateTimeOffset.Now.AddDays(-5).ToString("o");
                if (new TaskSnapshot("edge.json", map).IsStale(DateTimeOffset.Now)) throw new Exception("Completed task exemption");
                string orderPath = Path.Combine(temp, "order.data");
                SaveQueueOrder(orderPath, new[] { "a", "b", "a" });
                if (String.Join(",", LoadQueueOrder(orderPath)) != "a,b") throw new Exception("Queue persistence");
                File.WriteAllText(Path.Combine(temp,"broken.json"), "{broken");
                loaded = Load(temp, out errors);
                if (errors != 1 || loaded.Count != 3) throw new Exception("Malformed JSON isolation");
                if (Console.Out != null) Console.WriteLine("SELF_TEST_OK");
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        private static Dictionary<string, object> TestMap(string id, string status, int progress, DateTimeOffset started, DateTimeOffset? completed)
        {
            return new Dictionary<string, object> {
                { "task_id", id }, { "title", id }, { "status", status },
                { "started_at", started.ToString("o") }, { "updated_at", started.ToString("o") },
                { "completed_at", completed.HasValue ? completed.Value.ToString("o") : "" }, { "read_at", "" },
                { "progress", progress }, { "phases", new ArrayList() }
            };
        }
    }

}
