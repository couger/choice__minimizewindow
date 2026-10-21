using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class SelfTest
{
    static readonly List<string> results=new List<string>();
    static void Check(bool value,string name) {results.Add((value?"PASS ":"FAIL ")+name); if(!value) throw new Exception(name);}
    static void Pump() {for(int i=0;i<10;i++){Application.DoEvents(); Thread.Sleep(60);}}
    internal static void RunLogic()
    {
        string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","test-results"));Directory.CreateDirectory(folder);
        try {
            InteractionTests.Run(Check);MenuMaskTests.Run(Check,false);
            Rectangle[] screens={new Rectangle(-1920,0,1920,1080),new Rectangle(0,0,2560,1440),new Rectangle(2560,0,1920,1080)};
            Check(WindowManager.BestScreen(new Rectangle(-900,100,1000,600),screens)==0,"negative coordinates use greatest intersection");
            Check(WindowManager.BestScreen(new Rectangle(2400,100,800,600),screens)==2,"cross-monitor window uses greatest intersection");
            Check(LaunchRouter.Place(new Rectangle(0,0,4000,3000),new Rectangle(0,0,4000,3000),new Rectangle(100,200,800,600))==new Rectangle(100,200,800,600),"launch placement clamps to target work area");
            results.Add("ALL PASSED");
        }catch(Exception e) {results.Add("ERROR "+e);Environment.ExitCode=1;}
        finally {File.WriteAllLines(Path.Combine(folder,"logic-test.txt"),results.ToArray());}
    }
    internal static void Fixture(string path)
    {
        var forms=new List<Form>(); var lines=new List<string>(); Screen[] screens=WindowManager.Screens();
        for(int i=0;i<screens.Length;i++) {
            var f=new Form {Text="Choice test screen "+(i+1),StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(screens[i].WorkingArea.X+40,screens[i].WorkingArea.Y+50,350,220)};
            f.Show(); forms.Add(f); lines.Add("normal,"+i+","+f.Handle.ToInt64());
        }
        var max=new Form {Text="Choice test maximized",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(screens[0].WorkingArea.X+80,screens[0].WorkingArea.Y+80,320,200)};
        max.Show(); max.WindowState=FormWindowState.Maximized; forms.Add(max); lines.Add("max,0,"+max.Handle.ToInt64());
        var min=new Form {Text="Choice test pre-minimized",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(screens[0].WorkingArea.X+100,screens[0].WorkingArea.Y+100,320,200)};
        min.Show(); min.WindowState=FormWindowState.Minimized; forms.Add(min); lines.Add("min,0,"+min.Handle.ToInt64());
        File.WriteAllLines(path,lines.ToArray()); Application.Run(new ApplicationContext());
    }
    internal static void Run()
    {
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","test-results"); Directory.CreateDirectory(folder);
        string fixture=Path.Combine(folder,"fixture.txt"); if(File.Exists(fixture)) File.Delete(fixture);
        Process child=null; var manager=new WindowManager();
        try {
            TrayMenuTests.Run(Check);
            InteractionTests.Run(Check);
            MenuMaskTests.Run(Check);
            ListenerTests.Run(Check);
            Rectangle[] bounds={new Rectangle(-1920,0,1920,1080),new Rectangle(0,0,2560,1440),new Rectangle(2560,0,1920,1080)};
            Check(WindowManager.BestScreen(new Rectangle(-900,100,1000,600),bounds)==0,"negative coordinate / greatest area");
            Check(WindowManager.BestScreen(new Rectangle(2400,100,800,600),bounds)==2,"cross-monitor greatest area");
            child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--fixture \""+fixture+"\"") {UseShellExecute=false});
            for(int i=0;i<100 && !File.Exists(fixture);i++) Thread.Sleep(100);
            Check(File.Exists(fixture),"fixture process ready"); Pump();
            var handles=new List<IntPtr>(); var monitor=new List<int>(); IntPtr max=IntPtr.Zero,min=IntPtr.Zero;
            foreach(string line in File.ReadAllLines(fixture)) {string[] p=line.Split(','); IntPtr h=new IntPtr(long.Parse(p[2])); if(p[0]=="normal"){handles.Add(h);monitor.Add(int.Parse(p[1]));} else if(p[0]=="max") max=h; else min=h;}
            Screen[] screens=WindowManager.Screens(); results.Add("MONITORS "+screens.Length);
            CurrentScreenTests.Run(handles[0],Check);
            LaunchRouterTests.Run(handles[0],max,Pump,Check);
            foreach(Screen s in screens) {var entries=WindowSwitcher.Windows(s,(uint)child.Id);Check(entries.Count>0,"window picker finds fixture windows on "+s.DeviceName);foreach(var entry in entries) Check(Screen.FromHandle(entry.Handle).DeviceName==s.DeviceName,"window picker isolates monitor");}
            var switcher=new WindowSwitcher();var seen=new HashSet<IntPtr>();IntPtr current=handles[0];
            int windowCount=WindowSwitcher.Windows(screens[0],(uint)child.Id).Count;
            for(int i=0;i<windowCount;i++) {var next=switcher.Next(screens[0],current,1,(uint)child.Id);Check(next!=null,"switch cycle yields candidate");seen.Add(next.Handle);current=next.Handle;}
            Check(seen.Count==windowCount,"switch cycle visits every window including minimized");
            var firstEntry=WindowSwitcher.Windows(screens[0],(uint)child.Id).Find(e=>e.Handle==handles[0]);
            results.Add("INFO foreground activation without user input="+WindowSwitcher.Activate(firstEntry));Pump();
            var minimizedEntry=WindowSwitcher.Windows(screens[0],(uint)child.Id).Find(e=>e.Handle==min);
            WindowSwitcher.Activate(minimizedEntry);Pump();Check(!Native.IsIconic(min),"switch restores minimized window");
            Native.ShowWindowAsync(min,6);Pump();
            for(int i=0;i<screens.Length;i++) {
                Native.ShowWindowAsync(min,6);Pump();
                manager.Toggle(screens[i],(uint)child.Id); Pump();
                for(int j=0;j<handles.Count;j++) Check(Native.IsIconic(handles[j])==(monitor[j]==i),"screen "+(i+1)+" isolation window "+(j+1));
                Check(Native.IsIconic(min),"pre-minimized stays minimized");
                manager.Toggle(screens[i],(uint)child.Id); Pump();
                Check(!Native.IsIconic(handles[i]),"toggle restore screen "+(i+1));
                Check(Native.IsIconic(min)==(WindowManager.ScreenFor(min).DeviceName!=screens[i].DeviceName),"restore includes pre-minimized windows only on chosen screen");
            }
            var placement=new Native.Placement();placement.Length=Marshal.SizeOf(placement); Native.GetWindowPlacement(max,ref placement);
            Check(placement.Show==3,"restore preserves maximized state");
            MovementTests.Run(handles[0],(uint)child.Id,manager,Pump,Check);
            MovementTests.RunMaximized(max,(uint)child.Id,Pump,Check);
            foreach(Screen s in screens) manager.Toggle(s,(uint)child.Id); Pump(); manager.RestoreAll(); Pump();
            Check(manager.Pending==0,"restore all clears batches");
            foreach(IntPtr h in handles) Check(!Native.IsIconic(h),"restore all normal window");
            ExternalRestoreTests.Run((uint)child.Id,max,min,Pump,Check);
            // Closed windows must never cause handle errors during restore.
            manager.Toggle(screens[0],(uint)child.Id); Pump(); child.Kill();child.WaitForExit();manager.RestoreAll();
            Check(manager.Pending==0,"closed windows ignored safely");
            results.Add("ALL PASSED");
        } catch(Exception e) {results.Add("ERROR "+e); Environment.ExitCode=1;}
        finally {manager.RestoreAll(); if(child!=null){if(!child.HasExited) child.Kill();child.Dispose();} File.WriteAllLines(Path.Combine(folder,"self-test.txt"),results.ToArray());}
    }
}
