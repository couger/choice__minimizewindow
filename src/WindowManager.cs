using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class WindowManager
{
    internal sealed class Saved { public IntPtr Handle; public uint Pid; public Native.Placement Placement; }
    readonly Dictionary<string, List<Saved>> batches = new Dictionary<string, List<Saved>>();
    internal int Pending { get { int n = 0; foreach (var b in batches.Values) n += b.Count; return n; } }
    internal static Screen[] Screens() { var s = Screen.AllScreens; Array.Sort(s, (a,b) => { int x = a.Bounds.X.CompareTo(b.Bounds.X); return x != 0 ? x : a.Bounds.Y.CompareTo(b.Bounds.Y); }); return s; }
    internal static Screen ScreenFor(IntPtr h)
    {
        Native.Rect r;
        if(Native.IsWindow(h) && !Native.IsIconic(h) && Native.GetWindowRect(h,out r) && r.Right>r.Left && r.Bottom>r.Top) {
            Screen[] screens=Screens();Rectangle rect=Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
            Screen best=screens[BestScreen(rect,Array.ConvertAll(screens,s=>s.Bounds))];
            if(rect.IntersectsWith(best.Bounds)) return best;
        }
        // Minimized windows have off-screen rectangles; Windows retains their restore monitor.
        return Screen.FromHandle(h);
    }
    internal static int BestScreen(Rectangle window, Rectangle[] monitors)
    {
        long best = -1; int index = 0;
        for (int i = 0; i < monitors.Length; i++) { Rectangle r = Rectangle.Intersect(window, monitors[i]); long area = (long)Math.Max(0,r.Width)*Math.Max(0,r.Height); if (area > best) { best = area; index = i; } }
        return index;
    }
    internal static bool Eligible(IntPtr h,bool includeMinimized=false)
    {
        uint pid; Native.GetWindowThreadProcessId(h, out pid);
        if (pid == Process.GetCurrentProcess().Id || !Native.IsWindowVisible(h) || (!includeMinimized && Native.IsIconic(h)) || h == Native.GetShellWindow()) return false;
        int style = Native.GetWindowLong(h, -16), ex = Native.GetWindowLong(h, -20);
        if ((ex & 0x80) != 0 || (!includeMinimized && (style & 0x20000000) != 0) || (style & 0x08000000) != 0) return false;
        if (Native.GetWindow(h, 4) != IntPtr.Zero && (ex & 0x40000) == 0) return false;
        int cloaked; if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
        return true;
    }
    internal string Toggle(Screen screen) { return Toggle(screen,0); }
    internal static List<IntPtr> WindowsOn(Screen screen,uint onlyPid)
    {
        var windows=new List<IntPtr>();
        Native.EnumWindows(delegate(IntPtr h,IntPtr unused) {
            uint pid;Native.GetWindowThreadProcessId(h,out pid);
            if((onlyPid==0 || pid==onlyPid) && Eligible(h,true) && ScreenFor(h).DeviceName==screen.DeviceName) windows.Add(h);
            return true;
        },IntPtr.Zero);
        return windows;
    }
    internal string ActionText(Screen screen) {return ActionText(screen,0);}
    internal string ActionText(Screen screen,uint onlyPid)
    {
        var windows=WindowsOn(screen,onlyPid);
        return windows.Count>0 && windows.TrueForAll(h=>Native.IsIconic(h))?"还原窗口":"显示桌面";
    }
    internal string Toggle(Screen screen, uint onlyPid)
    {
        var windows=WindowsOn(screen,onlyPid);
        if(windows.Count>0 && windows.TrueForAll(h=>Native.IsIconic(h))) return RestoreMinimized(screen,onlyPid);
        var saved = new List<Saved>();
        foreach(IntPtr h in windows) {
            if (!Eligible(h)) continue;
            Native.Rect r; if (!Native.GetWindowRect(h, out r)) continue;
            Rectangle rect = Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
            if (rect.Width <= 0 || rect.Height <= 0 || ScreenFor(h).DeviceName != screen.DeviceName) continue;
            var p = new Native.Placement(); p.Length = Marshal.SizeOf(p);
            uint pid; Native.GetWindowThreadProcessId(h,out pid);
            if (Native.GetWindowPlacement(h, ref p) && Native.ShowWindowAsync(h, 6)) saved.Add(new Saved { Handle=h, Pid=pid, Placement=p });
        }
        if (saved.Count > 0) {
            List<Saved> prior;
            if(batches.TryGetValue(screen.DeviceName,out prior)) {
                foreach(Saved entry in saved) {prior.RemoveAll(s=>s.Handle==entry.Handle);prior.Add(entry);}
            } else batches[screen.DeviceName]=saved;
        }
        return "已向 " + saved.Count + " 个窗口发送最小化请求";
    }
    internal string RestoreMinimized(Screen screen,uint onlyPid)
    {
            var windows=WindowsOn(screen,onlyPid);
            int requested=0,failed=0;
            // The action depends on current state, never on who minimized a window.
            for(int i=windows.Count-1;i>=0;i--) {
                IntPtr h=windows[i];
                if(!Eligible(h,true) || !Native.IsIconic(h) || ScreenFor(h).DeviceName!=screen.DeviceName) continue;
                var placement=new Native.Placement();placement.Length=Marshal.SizeOf(placement);
                if(!Native.GetWindowPlacement(h,ref placement)) {failed++;Program.Log("Restore placement failed hwnd="+h+" error="+Marshal.GetLastWin32Error());continue;}
                if(Native.ShowWindowAsync(h,(placement.Flags&2)!=0?3:9)) {
                    requested++;
                    uint pid;Native.GetWindowThreadProcessId(h,out pid);
                    foreach(var batch in batches.Values) batch.RemoveAll(s=>s.Handle==h && s.Pid==pid);
                }
                else {failed++;Program.Log("Restore request failed hwnd="+h+" error="+Marshal.GetLastWin32Error());}
            }
            foreach(string device in new List<string>(batches.Keys)) if(batches[device].Count==0) batches.Remove(device);
            return "已向 "+requested+" 个已最小化窗口发送还原请求"+(failed>0?"，失败 "+failed+" 个；管理员窗口可尝试以管理员身份重启本程序":"");
    }
    internal void RestoreEveryScreen() {foreach(Screen screen in Screens()) Program.Log("Restore screen="+screen.DeviceName+" "+RestoreMinimized(screen,0));}
    internal static int MinimizedCount() {int count=0;Native.EnumWindows(delegate(IntPtr h,IntPtr p) {if(Eligible(h,true) && Native.IsIconic(h)) count++;return true;},IntPtr.Zero);return count;}
    internal string Restore(string device)
    {
        List<Saved> saved; if (!batches.TryGetValue(device,out saved)) return "没有待还原窗口";
        int n = 0; var failed = new List<Saved>();
        // EnumWindows returns front-to-back; restore back-to-front to preserve stacking as far as possible.
        for (int i=saved.Count-1;i>=0;i--) {
            Saved s=saved[i]; uint pid; Native.GetWindowThreadProcessId(s.Handle,out pid);
            if (!Native.IsWindow(s.Handle) || pid!=s.Pid || !Native.IsIconic(s.Handle)) continue;
            var p=s.Placement;
            if (Native.SetWindowPlacement(s.Handle,ref p)) n++; else failed.Add(s);
        }
        if (failed.Count==0) batches.Remove(device); else batches[device]=failed;
        return "已还原 " + n + " 个窗口" + (failed.Count>0 ? "，部分窗口无法还原" : "");
    }
    internal void RestoreAll() { foreach(string key in new List<string>(batches.Keys)) Restore(key); }
    internal bool Has(Screen s) { return batches.ContainsKey(s.DeviceName); }
}
