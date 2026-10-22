using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class LayoutProtectionTests
{
    internal static void Logic(Action<bool,string> check)
    {
        var gate=new LayoutGate();
        check(gate.Tick(0,true,"three",null)==0 && gate.Blocked,"layout startup waits for stable local topology");
        check(gate.Tick(4999,true,"three",null)==0,"layout stability has full five-second grace period");
        check(gate.Tick(5000,true,"three",null)==1 && !gate.Blocked,"stable local topology may save");
        check(gate.Tick(6000,false,"one","three")==0 && gate.Pending,"remote session freezes original layout");
        check(gate.Tick(16000,false,"three","three")==0,"matching geometry while remote never restores");
        check(gate.Tick(17000,true,"one","three")==0,"return to console with one screen waits");
        check(gate.Tick(23000,true,"one","three")==0 && gate.Blocked,"reduced topology cannot overwrite saved layout");
        check(gate.Tick(24000,true,"three","three")==0,"original topology must settle after returning");
        check(gate.Tick(29000,true,"three","three")==2,"restoration requested only after original topology settles");
        gate.Restored();check(gate.Tick(30000,true,"three","three")==1 && !gate.Pending,"successful return resumes saving without repeated restores");
        gate.Freeze(false);check(gate.Tick(31000,true,"three","three")==0,"lock/display notifications reset stability");
        check(gate.Tick(36000,true,"three","three")==1,"ordinary local lock does not restore stale positions");
        check(gate.Tick(37000,true,"changed","three")==0,"changed layout blocks writes before remote notification arrives");
        gate.Freeze(true);check(gate.Tick(38000,true,"three","three")==0,"late session notification still protects snapshot");
        check(gate.Tick(43000,true,"three","three")==2,"late notification recovers after stability");
        string path=Path.Combine(Program.DataDir,"layout-test.dat");Directory.CreateDirectory(Program.DataDir);
        var sample=new LayoutSnapshot {Topology="three",Saved=new DateTime(2026,1,1)};
        sample.Windows.Add(new LayoutSnapshot.Entry {Handle=123,Pid=456,Started=789,Cookie=111,Placement=new Native.Placement {Show=3,Normal=new Native.Rect {Left=-300,Top=20,Right=100,Bottom=200}}});
        sample.Write(path);var read=LayoutSnapshot.Read(path);
        check(read.Topology==sample.Topology && read.Saved==sample.Saved && read.Windows.Count==1,"layout file round-trips metadata");
        check(read.Windows[0].Cookie==111 && read.Windows[0].Placement.Normal.Left==-300 && read.Windows[0].Placement.Show==3,"layout file preserves identity, negative coordinates and state");
        sample.Write(path);check(LayoutSnapshot.Read(path).Windows.Count==1,"snapshot rewrite remains readable");
        File.WriteAllText(path,"corrupt");bool rejected=false;try {LayoutSnapshot.Read(path);}catch(Exception) {rejected=true;}
        check(rejected,"corrupt layout file rejected safely");File.Delete(path);
        string recovery=Path.Combine(Program.DataDir,"layout-recovery-test");Directory.CreateDirectory(recovery);
        sample.Generation=1;sample.Write(Path.Combine(recovery,"layout-a.dat"));sample.Generation=2;sample.Write(Path.Combine(recovery,"layout-b.dat"));
        check(LayoutSnapshot.LoadLatest(recovery).Generation==2,"layout recovery loads newest complete generation");
        using(var truncated=File.OpenWrite(Path.Combine(recovery,"layout-b.dat"))) truncated.SetLength(40);
        check(LayoutSnapshot.LoadLatest(recovery).Generation==1,"interrupted snapshot falls back to previous complete generation");
        File.Delete(Path.Combine(recovery,"layout-a.dat"));File.Delete(Path.Combine(recovery,"layout-b.dat"));Directory.Delete(recovery);
    }
    internal static LayoutSnapshot Live(uint fixturePid,Action pump,Action<bool,string> check)
    {
        if(fixturePid==0) throw new ArgumentException("Fixture PID is mandatory");
        check(LayoutProtection.DesktopReady(),"interactive local desktop is available before restoring windows");
        string encrypted=Path.Combine(Program.DataDir,"encrypted-layout-test");Directory.CreateDirectory(encrypted);
        bool efs=false;
        try {File.Encrypt(encrypted);efs=(File.GetAttributes(encrypted)&FileAttributes.Encrypted)!=0;}
        catch(System.ComponentModel.Win32Exception) {}catch(IOException) {}catch(UnauthorizedAccessException) {}
        if(efs) {
            string encryptedPath=Path.Combine(encrypted,"layout.dat");var sample=new LayoutSnapshot {Topology="test",Saved=DateTime.Now};
            sample.Write(encryptedPath);sample.Write(encryptedPath);
            check(LayoutSnapshot.Read(encryptedPath).Topology=="test","snapshot rewrites work in an EFS-encrypted directory");
            check((File.GetAttributes(encryptedPath)&FileAttributes.Encrypted)!=0,"snapshot updates preserve inherited EFS encryption");
            File.Delete(encryptedPath);
        }
        Directory.Delete(encrypted);
        var snapshot=LayoutSnapshot.Capture(fixturePid);
        check(snapshot.Windows.Count>=3,"layout snapshot finds only fixture windows");
        foreach(var e in snapshot.Windows) {
            check(e.Pid==fixturePid,"layout capture process scope excludes user windows");
            IntPtr h=new IntPtr(e.Handle);Native.ShowWindowAsync(h,1);pump();
            var area=WindowManager.Screens()[WindowManager.Screens().Length-1].WorkingArea;
            Native.SetWindowPos(h,IntPtr.Zero,area.Left+150,area.Top+150,500,300,0x14);pump();
        }
        string path=Path.Combine(Program.DataDir,"layout-live-test.dat");snapshot.Write(path);snapshot=LayoutSnapshot.Read(path);
        check(snapshot.Restore(fixturePid)==snapshot.Windows.Count,"persisted layout restores all eligible fixture windows");pump();
        foreach(var e in snapshot.Windows) {
            var p=new Native.Placement {Length=Marshal.SizeOf(typeof(Native.Placement))};Native.GetWindowPlacement(new IntPtr(e.Handle),ref p);
            check(p.Show==e.Placement.Show,"layout restores normal/maximized/minimized state");
            var before=e.Placement.Normal;var after=p.Normal;
            check(Math.Abs(before.Left-after.Left)<=2 && Math.Abs(before.Top-after.Top)<=2 && Math.Abs(before.Right-after.Right)<=2 && Math.Abs(before.Bottom-after.Bottom)<=2,"layout restores original normal rectangle across monitors");
        }
        var first=snapshot.Windows[0];first.Cookie++;check(snapshot.Restore(fixturePid)==snapshot.Windows.Count-1,"window identity mismatch skipped");first.Cookie--;
        first.Started++;check(snapshot.Restore(fixturePid)==snapshot.Windows.Count-1,"process identity mismatch skipped");first.Started--;
        string topology=snapshot.Topology;snapshot.Topology="different";check(snapshot.Restore(fixturePid)==-1,"restoration refuses missing original monitor layout");snapshot.Topology=topology;
        File.Delete(path);
        return snapshot;
    }
}
