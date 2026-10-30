using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Geometry is the identity: DISPLAY numbers can change after an RDP session.
// Exact topology matching deliberately waits rather than guessing a destination.
internal sealed class LayoutSnapshot
{
    internal sealed class Entry {internal long Handle,Started,Cookie;internal uint Pid;internal Native.Placement Placement;}
    const string Property="ChoiceMinimizeWindow.LayoutIdentity.v1";
    static int restoreEpoch;static long restoreStarted;
    internal static void CancelRestore() {restoreEpoch++;}
    internal static void CancelRestore(long interactionTime) {if(interactionTime>restoreStarted) CancelRestore();}
    internal string Topology;
    internal DateTime Saved;
    internal long Generation;
    internal readonly List<Entry> Windows=new List<Entry>();
    internal static string CurrentTopology()
    {
        var parts=new List<string>();
        foreach(Screen screen in WindowManager.Screens()) {
            var b=screen.Bounds;var w=screen.WorkingArea;
            parts.Add(string.Join(",",new[]{b.X,b.Y,b.Width,b.Height,w.X,w.Y,w.Width,w.Height,screen.Primary?1:0}));
        }
        return string.Join(";",parts.ToArray());
    }
    internal static LayoutSnapshot Capture(uint onlyPid=0)
    {
        var saved=new LayoutSnapshot {Topology=CurrentTopology(),Saved=DateTime.Now};
        Native.EnumWindows(delegate(IntPtr h,IntPtr unused) {
            uint pid;Native.GetWindowThreadProcessId(h,out pid);
            if((onlyPid!=0 && pid!=onlyPid) || !WindowManager.Eligible(h,true)) return true;
            try {
                long started;using(var p=Process.GetProcessById((int)pid)) started=p.StartTime.ToUniversalTime().Ticks;
                long cookie=Native.GetProp(h,Property).ToInt64();
                if(cookie==0) {cookie=BitConverter.ToInt64(Guid.NewGuid().ToByteArray(),0)&long.MaxValue;if(cookie==0) cookie=1;if(!Native.SetProp(h,Property,new IntPtr(cookie))) return true;}
                var placement=new Native.Placement {Length=Marshal.SizeOf(typeof(Native.Placement))};
                if(Native.GetWindowPlacement(h,ref placement)) saved.Windows.Add(new Entry {Handle=h.ToInt64(),Pid=pid,Started=started,Cookie=cookie,Placement=placement});
            }catch(ArgumentException) {}catch(System.ComponentModel.Win32Exception) {}catch(InvalidOperationException) {}
            return true;
        },IntPtr.Zero);
        return saved;
    }
    internal int Restore(uint onlyPid=0)
    {
        if(Topology!=CurrentTopology()) return -1;
        int epoch=++restoreEpoch;restoreStarted=Stopwatch.GetTimestamp();
        int count=0;var targets=new List<Entry>();
        foreach(var entry in Windows) {
            if(onlyPid!=0 && entry.Pid!=onlyPid) continue;
            IntPtr h=new IntPtr(entry.Handle);uint pid;Native.GetWindowThreadProcessId(h,out pid);
            if(pid!=entry.Pid || !WindowManager.Eligible(h,true) || Native.GetProp(h,Property).ToInt64()!=entry.Cookie) continue;
            try {using(var process=Process.GetProcessById((int)pid)) if(process.StartTime.ToUniversalTime().Ticks!=entry.Started) continue;}
            catch(ArgumentException) {continue;}catch(System.ComponentModel.Win32Exception) {continue;}catch(InvalidOperationException) {continue;}
            var p=entry.Placement;p.Length=Marshal.SizeOf(typeof(Native.Placement));
            // Async placement avoids waiting on an unresponsive foreign application's UI thread.
            p.Flags|=4;
            if(Native.SetWindowPlacement(h,ref p)) {count++;targets.Add(entry);}
        }
        if(targets.Count>0) new PlacementRepair(targets,epoch).Start();
        return count;
    }
    sealed class PlacementRepair
    {
        readonly List<Entry> targets;readonly int epoch;readonly Timer timer=new Timer {Interval=200};int attempts,stable;
        internal PlacementRepair(List<Entry> targets,int epoch) {this.targets=targets;this.epoch=epoch;timer.Tick+=delegate {Tick();};}
        internal void Start() {timer.Start();}
        void Tick()
        {
            if(epoch!=restoreEpoch) {timer.Dispose();return;}
            bool matched=true;int mismatches=0;
            foreach(var entry in targets) {
                IntPtr h=new IntPtr(entry.Handle);uint pid;Native.GetWindowThreadProcessId(h,out pid);
                if(pid!=entry.Pid || !WindowManager.Eligible(h,true) || Native.GetProp(h,Property).ToInt64()!=entry.Cookie) continue;
                var current=new Native.Placement {Length=Marshal.SizeOf(typeof(Native.Placement))};
                if(!Native.GetWindowPlacement(h,ref current)) continue;
                var a=entry.Placement.Normal;var b=current.Normal;
                if(current.Show==entry.Placement.Show && Math.Abs(a.Left-b.Left)<=2 && Math.Abs(a.Top-b.Top)<=2 && Math.Abs(a.Right-b.Right)<=2 && Math.Abs(a.Bottom-b.Bottom)<=2) continue;
                matched=false;mismatches++;
                var expected=entry.Placement;expected.Length=Marshal.SizeOf(typeof(Native.Placement));expected.Flags|=4;
                Native.SetWindowPlacement(h,ref expected);
                if(expected.Show==1 && !Native.IsIconic(h) && !Native.IsZoomed(h)) {
                    var rect=System.Drawing.Rectangle.FromLTRB(a.Left,a.Top,a.Right,a.Bottom);var screens=WindowManager.Screens();
                    var screen=screens[WindowManager.BestScreen(rect,Array.ConvertAll(screens,s=>s.Bounds))];
                    rect.Offset(screen.WorkingArea.Left-screen.Bounds.Left,screen.WorkingArea.Top-screen.Bounds.Top);
                    Native.SetWindowPos(h,IntPtr.Zero,rect.Left,rect.Top,rect.Width,rect.Height,0x4014);
                }
            }
            attempts++;stable=matched?stable+1:0;
            if(stable>=2 || attempts>=8) {if(!matched) Program.Log("Layout repair incomplete windows="+mismatches);timer.Dispose();}
        }
    }
    internal void Write(string path)
    {
            // Production alternates two independent files. A failed write never replaces
            // the preceding complete generation, and requires no rename/metadata merge.
            using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))
            using(var writer=new BinaryWriter(stream)) {
                writer.Write("ChoiceLayout2");writer.Write(Topology);writer.Write(Saved.Ticks);writer.Write(Generation);writer.Write(Windows.Count);
                foreach(var e in Windows) {
                    writer.Write(e.Handle);writer.Write(e.Pid);writer.Write(e.Started);writer.Write(e.Cookie);
                    var p=e.Placement;foreach(int n in new[]{p.Flags,p.Show,p.Min.X,p.Min.Y,p.Max.X,p.Max.Y,p.Normal.Left,p.Normal.Top,p.Normal.Right,p.Normal.Bottom}) writer.Write(n);
                }
                writer.Write("EndChoiceLayout2");writer.Flush();stream.Flush(true);
            }
    }
    internal static LayoutSnapshot Read(string path)
    {
        if(new FileInfo(path).Length>1024*1024) throw new InvalidDataException("Layout file too large");
        using(var reader=new BinaryReader(File.OpenRead(path))) {
            string format=reader.ReadString();if(format!="ChoiceLayout1" && format!="ChoiceLayout2") throw new InvalidDataException("Unknown layout format");
            var snapshot=new LayoutSnapshot {Topology=reader.ReadString(),Saved=new DateTime(reader.ReadInt64())};
            if(format=="ChoiceLayout2") {snapshot.Generation=reader.ReadInt64();if(snapshot.Generation<0) throw new InvalidDataException("Invalid generation");}
            int count=reader.ReadInt32();if(count<0 || count>10000 || snapshot.Topology.Length>4096) throw new InvalidDataException("Invalid layout");
            for(int i=0;i<count;i++) {
                var e=new Entry {Handle=reader.ReadInt64(),Pid=reader.ReadUInt32(),Started=reader.ReadInt64(),Cookie=reader.ReadInt64()};
                e.Placement=new Native.Placement {Length=Marshal.SizeOf(typeof(Native.Placement)),Flags=reader.ReadInt32(),Show=reader.ReadInt32(),Min=new Native.Point {X=reader.ReadInt32(),Y=reader.ReadInt32()},Max=new Native.Point {X=reader.ReadInt32(),Y=reader.ReadInt32()},Normal=new Native.Rect {Left=reader.ReadInt32(),Top=reader.ReadInt32(),Right=reader.ReadInt32(),Bottom=reader.ReadInt32()}};
                if(e.Placement.Show!=1 && e.Placement.Show!=2 && e.Placement.Show!=3) throw new InvalidDataException("Invalid window state");
                snapshot.Windows.Add(e);
            }
            if(format=="ChoiceLayout2" && reader.ReadString()!="EndChoiceLayout2") throw new InvalidDataException("Incomplete layout");
            if(reader.BaseStream.Position!=reader.BaseStream.Length) throw new InvalidDataException("Trailing layout data");
            return snapshot;
        }
    }
    internal static LayoutSnapshot LoadLatest(string directory,string prefix="layout")
    {
        LayoutSnapshot latest=null;
        foreach(string name in new[]{prefix+".dat",prefix+"-a.dat",prefix+"-b.dat"}) {
            string path=Path.Combine(directory,name);if(!File.Exists(path)) continue;
            try {var value=Read(path);if(latest==null || value.Generation>latest.Generation || (value.Generation==latest.Generation && value.Saved>latest.Saved)) latest=value;}
            catch(Exception e) {Program.Log("Layout load: "+e.Message);}
        }
        return latest;
    }
}

// Deterministic gate also exercised by tests without disconnecting a user's session.
internal sealed class LayoutGate
{
    string observed;long since;bool wasLocal;
    internal bool Pending,Blocked=true;
    internal bool Stable {get;private set;}
    internal void Freeze(bool restore) {Pending|=restore;observed=null;Stable=false;Blocked=true;}
    internal int Tick(long now,bool local,string topology,string savedTopology)
    {
        if(!local) {Freeze(savedTopology!=null);wasLocal=false;return 0;}
        if(!wasLocal || topology!=observed) {observed=topology;since=now;Stable=false;wasLocal=true;}
        Stable=now-since>=5000;
        bool mismatch=savedTopology!=null && topology!=savedTopology;
        if(mismatch) Pending=true;
        Blocked=!Stable || mismatch;
        if(Blocked) return 0;
        if(Pending && savedTopology!=null) return 2;
        return 1;
    }
    internal void Restored() {Pending=false;}
}

internal sealed class LayoutProtection : IDisposable
{
    readonly Timer timer=new Timer {Interval=1000};readonly Stopwatch clock=Stopwatch.StartNew();
    readonly LayoutGate gate=new LayoutGate();
    readonly string setting,dataDirectory;readonly uint onlyPid;readonly Func<bool> localCheck;readonly Func<long> milliseconds;
    long Now {get {return milliseconds==null?clock.ElapsedMilliseconds:milliseconds();}}
    LayoutSnapshot snapshot,manualSnapshot,candidate;long candidateSince,lastCapture;bool disposed,started,active=true,registered;IntPtr owner;
    internal bool Enabled {get;private set;}
    internal bool RoutingBlocked {get {return started && active && Enabled && gate.Blocked;}}
    internal bool CanSave {get {return started && Local() && gate.Stable;}}
    internal bool CanRestore {get {return CanSave && manualSnapshot!=null && manualSnapshot.Topology==LayoutSnapshot.CurrentTopology();}}
    internal bool CanRestoreAutomatic {get {return CanSave && snapshot!=null && snapshot.Topology==LayoutSnapshot.CurrentTopology();}}
    internal string ManualStatus {get {return manualSnapshot==null?"手动布局：尚未保存":"手动布局："+manualSnapshot.Saved.ToString("MM-dd HH:mm:ss")+" · "+manualSnapshot.Windows.Count+" 个窗口";}}
    internal string Status {get;private set;}
    internal event Action Changed;
    internal LayoutProtection(uint onlyPid=0,string directory=null,Func<bool> localCheck=null,Func<long> milliseconds=null)
    {
        if((directory!=null || localCheck!=null || milliseconds!=null) && onlyPid==0) throw new ArgumentException("Test overrides require a fixture PID");
        this.onlyPid=onlyPid;this.localCheck=localCheck;this.milliseconds=milliseconds;dataDirectory=directory??Program.DataDir;setting=Path.Combine(dataDirectory,"layout-enabled.txt");
        Enabled=true;Status="等待本地屏幕稳定";
        try {bool value;if(File.Exists(setting) && bool.TryParse(File.ReadAllText(setting),out value)) Enabled=value; snapshot=LayoutSnapshot.LoadLatest(dataDirectory);manualSnapshot=LayoutSnapshot.LoadLatest(dataDirectory,"layout-manual");}
        catch(Exception e) {Program.Log("Layout load: "+e.Message);}
        timer.Tick+=delegate {Poll();};
    }
    internal void Start(IntPtr handle)
    {
        owner=handle;registered=Native.WTSRegisterSessionNotification(owner,0);started=true;
        Program.Log("Layout protection started; session notifications="+registered+" automaticWindows="+(snapshot==null?0:snapshot.Windows.Count)+" manualWindows="+(manualSnapshot==null?0:manualSnapshot.Windows.Count));Poll();timer.Start();
    }
    bool Local()
    {
        if(localCheck!=null) return localCheck();
        // Poll the current desktop instead of latching a lock notification forever:
        // a missed unlock notification must not permanently stop protection.
        if(Native.WTSGetActiveConsoleSessionId()!=(uint)Process.GetCurrentProcess().SessionId) return false;
        IntPtr buffer;int bytes;
        if(!Native.WTSQuerySessionInformation(IntPtr.Zero,-1,16,out buffer,out bytes)) return false;
        try {if(bytes<2 || Marshal.ReadInt16(buffer)!=0) return false;}finally {Native.WTSFreeMemory(buffer);}
        if(!Native.WTSQuerySessionInformation(IntPtr.Zero,-1,8,out buffer,out bytes)) return false;
        bool connected;try {connected=bytes>=4 && Marshal.ReadInt32(buffer)==0;}finally {Native.WTSFreeMemory(buffer);}
        return connected && DesktopReady();
    }
    internal static bool DesktopReady()
    {
        IntPtr desktop=Native.OpenInputDesktop(0,false,1);if(desktop==IntPtr.Zero) return false;
        try {var name=new System.Text.StringBuilder(256);int needed;return Native.GetUserObjectInformation(desktop,2,name,name.Capacity*2,out needed) && string.Equals(name.ToString(),"Default",StringComparison.OrdinalIgnoreCase);}
        finally {Native.CloseDesktop(desktop);}
    }
    internal void Session(int reason)
    {
        LayoutSnapshot.CancelRestore();
        Program.Log("Layout session reason="+reason);
        if(reason==2 || reason==3 || reason==4) gate.Freeze(snapshot!=null);
        else gate.Freeze(false);
        candidate=null;Poll();
    }
    internal void DisplayChanged() {LayoutSnapshot.CancelRestore();gate.Freeze(snapshot!=null);candidate=null;Poll();}
    internal void SetActive(bool value) {active=value;gate.Freeze(false);candidate=null;if(started) Poll();}
    internal void SetEnabled(bool value)
    {
        Directory.CreateDirectory(dataDirectory);File.WriteAllText(setting,value.ToString());Enabled=value;if(value) gate.Freeze(false);candidate=null;if(started) Poll();
    }
    void Update(string value)
    {
        Status=value;var changed=Changed;if(changed!=null) changed();
    }
    void SaveSnapshot(LayoutSnapshot value)
    {
        Directory.CreateDirectory(dataDirectory);value.Generation=checked((snapshot==null?0:snapshot.Generation)+1);
        value.Write(Path.Combine(dataDirectory,value.Generation%2==1?"layout-a.dat":"layout-b.dat"));snapshot=value;
    }
    internal void SaveNow()
    {
        if(!CanSave) throw new InvalidOperationException("请回到本机并等待屏幕稳定后再保存。");
        var value=LayoutSnapshot.Capture(onlyPid);value.Generation=checked((manualSnapshot==null?0:manualSnapshot.Generation)+1);
        Directory.CreateDirectory(dataDirectory);value.Write(Path.Combine(dataDirectory,value.Generation%2==1?"layout-manual-a.dat":"layout-manual-b.dat"));manualSnapshot=value;
        SaveSnapshot(LayoutSnapshot.Capture(onlyPid));candidate=null;gate.Restored();Poll();Update("已手动保存 · "+manualSnapshot.Saved.ToString("HH:mm:ss"));
        Program.Log("Layout manual saved windows="+manualSnapshot.Windows.Count);
    }
    internal void RestoreNow()
    {
        if(!CanRestore) throw new InvalidOperationException("请回到本机，等待原屏幕布局（分辨率、位置及任务栏工作区）恢复后再试。");
        Restore(manualSnapshot,"manual");
    }
    internal void RestoreAutomatic()
    {
        if(!CanRestoreAutomatic) throw new InvalidOperationException("最近自动布局与当前屏幕不匹配，请等待原屏幕布局恢复。");
        Restore(snapshot,"automatic");
    }
    void Restore(LayoutSnapshot value,string source)
    {
        int count=value.Restore(onlyPid);gate.Restored();candidate=null;lastCapture=Now+5000;
        Update("已发送还原请求："+count+" 个窗口");Program.Log("Layout restored source="+source+" windows="+count+" recorded="+value.Windows.Count);
    }
    internal void Poll()
    {
        if(disposed) return;
        try {
            bool local=Local();int action=gate.Tick(Now,local,LayoutSnapshot.CurrentTopology(),snapshot==null?null:snapshot.Topology);
            if(!Enabled || !active) {candidate=null;Update(!Enabled?"自动恢复已关闭":"应用已停用");return;}
            if(action==0) {candidate=null;Update(!local?"远程／锁定期间保护布局":gate.Stable?"等待原屏幕布局恢复":"等待本地屏幕稳定");return;}
            if(action==2) {RestoreAutomatic();return;}
            // Retain a delayed sample, so late display/session notifications cannot commit
            // the positions Windows already collapsed during a connection transition.
            long now=Now;
            if(candidate!=null && now-candidateSince>=4000) {SaveSnapshot(candidate);candidate=null;}
            if(candidate==null && now-lastCapture>=2000) {candidate=LayoutSnapshot.Capture(onlyPid);candidateSince=now;lastCapture=now;}
            Update(snapshot==null?"正在记录本地布局":"已保护布局 · "+snapshot.Saved.ToString("HH:mm:ss"));
        }catch(Exception e) {candidate=null;gate.Freeze(false);Update("布局保护暂时失败（详见本地日志）");Program.Log("Layout protection: "+e.Message);}
    }
    public void Dispose() {if(disposed) return;disposed=true;timer.Dispose();if(registered) Native.WTSUnRegisterSessionNotification(owner);}
}
