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
    internal string Topology;
    internal DateTime Saved;
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
        int count=0;
        foreach(var entry in Windows) {
            if(onlyPid!=0 && entry.Pid!=onlyPid) continue;
            IntPtr h=new IntPtr(entry.Handle);uint pid;Native.GetWindowThreadProcessId(h,out pid);
            if(pid!=entry.Pid || !WindowManager.Eligible(h,true) || Native.GetProp(h,Property).ToInt64()!=entry.Cookie) continue;
            try {using(var process=Process.GetProcessById((int)pid)) if(process.StartTime.ToUniversalTime().Ticks!=entry.Started) continue;}
            catch(ArgumentException) {continue;}catch(System.ComponentModel.Win32Exception) {continue;}catch(InvalidOperationException) {continue;}
            var p=entry.Placement;p.Length=Marshal.SizeOf(typeof(Native.Placement));
            // Async placement avoids waiting on an unresponsive foreign application's UI thread.
            p.Flags|=4;
            if(Native.SetWindowPlacement(h,ref p)) count++;
        }
        return count;
    }
    internal void Write(string path)
    {
        string temporary=path+".tmp";
        try {
            using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
            using(var writer=new BinaryWriter(stream)) {
                writer.Write("ChoiceLayout1");writer.Write(Topology);writer.Write(Saved.Ticks);writer.Write(Windows.Count);
                foreach(var e in Windows) {
                    writer.Write(e.Handle);writer.Write(e.Pid);writer.Write(e.Started);writer.Write(e.Cookie);
                    var p=e.Placement;foreach(int n in new[]{p.Flags,p.Show,p.Min.X,p.Min.Y,p.Max.X,p.Max.Y,p.Normal.Left,p.Normal.Top,p.Normal.Right,p.Normal.Bottom}) writer.Write(n);
                }
                writer.Flush();stream.Flush(true);
            }
            if(File.Exists(path)) File.Replace(temporary,path,null);else File.Move(temporary,path);
        }finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }
    internal static LayoutSnapshot Read(string path)
    {
        if(new FileInfo(path).Length>1024*1024) throw new InvalidDataException("Layout file too large");
        using(var reader=new BinaryReader(File.OpenRead(path))) {
            if(reader.ReadString()!="ChoiceLayout1") throw new InvalidDataException("Unknown layout format");
            var snapshot=new LayoutSnapshot {Topology=reader.ReadString(),Saved=new DateTime(reader.ReadInt64())};
            int count=reader.ReadInt32();if(count<0 || count>10000 || snapshot.Topology.Length>4096) throw new InvalidDataException("Invalid layout");
            for(int i=0;i<count;i++) {
                var e=new Entry {Handle=reader.ReadInt64(),Pid=reader.ReadUInt32(),Started=reader.ReadInt64(),Cookie=reader.ReadInt64()};
                e.Placement=new Native.Placement {Length=Marshal.SizeOf(typeof(Native.Placement)),Flags=reader.ReadInt32(),Show=reader.ReadInt32(),Min=new Native.Point {X=reader.ReadInt32(),Y=reader.ReadInt32()},Max=new Native.Point {X=reader.ReadInt32(),Y=reader.ReadInt32()},Normal=new Native.Rect {Left=reader.ReadInt32(),Top=reader.ReadInt32(),Right=reader.ReadInt32(),Bottom=reader.ReadInt32()}};
                if(e.Placement.Show!=1 && e.Placement.Show!=2 && e.Placement.Show!=3) throw new InvalidDataException("Invalid window state");
                snapshot.Windows.Add(e);
            }
            if(reader.BaseStream.Position!=reader.BaseStream.Length) throw new InvalidDataException("Trailing layout data");
            return snapshot;
        }
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
    readonly LayoutGate gate=new LayoutGate();readonly string path=Path.Combine(Program.DataDir,"layout.dat");
    readonly string setting=Path.Combine(Program.DataDir,"layout-enabled.txt");
    LayoutSnapshot snapshot,candidate;long candidateSince,lastCapture;bool locked,disposed,started,active=true,registered;IntPtr owner;
    internal bool Enabled {get;private set;}
    internal bool RoutingBlocked {get {return started && active && Enabled && gate.Blocked;}}
    internal bool CanSave {get {return started && Local() && gate.Stable;}}
    internal bool CanRestore {get {return CanSave && snapshot!=null && snapshot.Topology==LayoutSnapshot.CurrentTopology();}}
    internal string Status {get;private set;}
    internal event Action Changed;
    internal LayoutProtection()
    {
        Enabled=true;Status="等待本地屏幕稳定";
        try {bool value;if(File.Exists(setting) && bool.TryParse(File.ReadAllText(setting),out value)) Enabled=value;if(File.Exists(path)) snapshot=LayoutSnapshot.Read(path);}
        catch(Exception e) {Program.Log("Layout load: "+e.Message);}
        timer.Tick+=delegate {Poll();};
    }
    internal void Start(IntPtr handle)
    {
        owner=handle;registered=Native.WTSRegisterSessionNotification(owner,0);started=true;
        Program.Log("Layout protection started; session notifications="+registered);Poll();timer.Start();
    }
    bool Local()
    {
        if(locked || Native.WTSGetActiveConsoleSessionId()!=(uint)Process.GetCurrentProcess().SessionId) return false;
        IntPtr buffer;int bytes;
        if(!Native.WTSQuerySessionInformation(IntPtr.Zero,-1,16,out buffer,out bytes)) return false;
        try {if(bytes<2 || Marshal.ReadInt16(buffer)!=0) return false;}finally {Native.WTSFreeMemory(buffer);}
        if(!Native.WTSQuerySessionInformation(IntPtr.Zero,-1,8,out buffer,out bytes)) return false;
        try {return bytes>=4 && Marshal.ReadInt32(buffer)==0;}finally {Native.WTSFreeMemory(buffer);}
    }
    internal void Session(int reason)
    {
        if(reason==7) locked=true;
        if(reason==8 || reason==1) locked=false;
        if(reason==2 || reason==3 || reason==4) gate.Freeze(snapshot!=null);
        else gate.Freeze(false);
        candidate=null;Poll();
    }
    internal void DisplayChanged() {gate.Freeze(snapshot!=null);candidate=null;Poll();}
    internal void SetActive(bool value) {active=value;gate.Freeze(false);candidate=null;if(started) Poll();}
    internal void SetEnabled(bool value)
    {
        Directory.CreateDirectory(Program.DataDir);File.WriteAllText(setting,value.ToString());Enabled=value;gate.Freeze(false);candidate=null;if(started) Poll();
    }
    void Update(string value)
    {
        Status=value;var changed=Changed;if(changed!=null) changed();
    }
    void SaveSnapshot(LayoutSnapshot value) {Directory.CreateDirectory(Program.DataDir);value.Write(path);snapshot=value;}
    internal void SaveNow()
    {
        if(!CanSave) throw new InvalidOperationException("请回到本机并等待屏幕稳定后再保存。");
        SaveSnapshot(LayoutSnapshot.Capture());candidate=null;gate.Restored();Poll();Update("已保存布局 · "+snapshot.Saved.ToString("HH:mm:ss"));
    }
    internal void RestoreNow()
    {
        if(!CanRestore) throw new InvalidOperationException("请回到本机，等待原屏幕布局（分辨率、位置及任务栏工作区）恢复后再试。");
        int count=snapshot.Restore();gate.Restored();candidate=null;lastCapture=clock.ElapsedMilliseconds+5000;
        Update("已发送还原请求："+count+" 个窗口");Program.Log("Layout restored windows="+count);
    }
    void Poll()
    {
        if(disposed) return;
        try {
            bool local=Local();int action=gate.Tick(clock.ElapsedMilliseconds,local,LayoutSnapshot.CurrentTopology(),snapshot==null?null:snapshot.Topology);
            if(!Enabled || !active) {candidate=null;Update(!Enabled?"自动恢复已关闭":"应用已停用");return;}
            if(action==0) {candidate=null;Update(!local?"远程／锁定期间保护布局":gate.Stable?"等待原屏幕布局恢复":"等待本地屏幕稳定");return;}
            if(action==2) {RestoreNow();return;}
            // Retain a delayed sample, so late display/session notifications cannot commit
            // the positions Windows already collapsed during a connection transition.
            long now=clock.ElapsedMilliseconds;
            if(candidate!=null && now-candidateSince>=4000) {SaveSnapshot(candidate);candidate=null;}
            if(candidate==null && now-lastCapture>=2000) {candidate=LayoutSnapshot.Capture();candidateSince=now;lastCapture=now;}
            Update(snapshot==null?"正在记录本地布局":"已保护布局 · "+snapshot.Saved.ToString("HH:mm:ss"));
        }catch(Exception e) {candidate=null;gate.Freeze(false);Update("布局保护暂时失败（详见本地日志）");Program.Log("Layout protection: "+e.Message);}
    }
    public void Dispose() {if(disposed) return;disposed=true;timer.Dispose();if(registered) Native.WTSUnRegisterSessionNotification(owner);}
}
