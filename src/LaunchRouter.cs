using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Bound foreground routing to explicit user launch gestures; never move background windows.
internal sealed class LaunchRouter : IDisposable
{
    readonly Timer timer=new Timer {Interval=100};
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Native.WinEventProc callback;
    readonly uint onlyPid;
    IntPtr hook,source,candidate;
    string target;
    long expires,stableSince;
    int attempts;
    bool active,disposed;
    bool sourceIsApplication;
    internal event Action<Screen> Routed;
    internal LaunchRouter(uint onlyPid=0) {this.onlyPid=onlyPid;callback=Foreground;timer.Tick+=delegate {Poll();};}
    internal bool InScope(IntPtr window)
    {
        uint pid;Native.GetWindowThreadProcessId(window,out pid);
        return onlyPid==0 || pid==onlyPid;
    }
    internal bool Configure(bool enabled)
    {
        Cancel();active=enabled;
        if(!enabled) {if(hook!=IntPtr.Zero) Native.UnhookWinEvent(hook);hook=IntPtr.Zero;Program.Log("Launch routing enabled=False");return true;}
        if(hook==IntPtr.Zero) hook=Native.SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,2);
        active=hook!=IntPtr.Zero;Program.Log("Launch routing enabled="+active+" hook="+(hook!=IntPtr.Zero));return active;
    }
    internal void Arm(Screen screen,IntPtr originalFocus)
    {
        if(!active || disposed) return;
        target=screen.DeviceName;source=originalFocus;candidate=IntPtr.Zero;attempts=0;
        sourceIsApplication=originalFocus!=IntPtr.Zero && (!Native.IsWindow(originalFocus) || CanRoute(originalFocus));
        expires=clock.ElapsedMilliseconds+10000;timer.Start();
    }
    internal void Cancel() {target=null;candidate=IntPtr.Zero;attempts=0;timer.Stop();}
    void Foreground(IntPtr unused,uint type,IntPtr window,int objectId,int childId,uint thread,uint time)
    {
        if(!active || target==null || disposed) return;
        // WinEvents are delivered on this UI thread; defer placement until the window settles.
        if(window!=source && window!=candidate) {candidate=IntPtr.Zero;stableSince=clock.ElapsedMilliseconds;}
    }
    void Poll()
    {
        if(!active || target==null || clock.ElapsedMilliseconds>expires) {Cancel();return;}
        // Closing/minimizing the source can automatically activate a different monitor.
        if(sourceIsApplication && (!Native.IsWindow(source) || !Native.IsWindowVisible(source) || Native.IsIconic(source))) {Cancel();return;}
        Screen destination=Array.Find(WindowManager.Screens(),s=>s.DeviceName==target);
        if(destination==null) {Cancel();return;}
        IntPtr foreground=Native.GetForegroundWindow();
        if(foreground==source || !InScope(foreground) || !CanRoute(foreground)) {candidate=IntPtr.Zero;return;}
        if(candidate!=foreground) {candidate=foreground;stableSince=clock.ElapsedMilliseconds;attempts=0;return;}
        if(clock.ElapsedMilliseconds-stableSince<200) return;
        if(WindowManager.ScreenFor(candidate).DeviceName==target) {Cancel();return;}
        bool ok=MoveTo(candidate,destination);attempts++;
        Program.Log("Launch route window="+candidate+" target="+target+" moved="+ok+" attempt="+attempts);
        if(ok) {var routed=Routed;if(routed!=null) routed(destination);}
        // Recheck after DPI/layout changes, with a bounded retry; later user input cancels it.
        if(attempts>=3) Cancel();
    }
    internal static bool CanRoute(IntPtr window)
    {
        if(!WindowManager.Eligible(window) || Native.IsIconic(window)) return false;
        uint pid;Native.GetWindowThreadProcessId(window,out pid);
        try {
            using(var process=Process.GetProcessById((int)pid))
                if(Array.IndexOf(new[]{"StartMenuExperienceHost","SearchHost","SearchApp","ShellExperienceHost","ShellHost","TextInputHost","LockApp"},process.ProcessName)>=0) return false;
        }catch(ArgumentException) {return false;}catch(System.ComponentModel.Win32Exception) {return false;}
        Native.Rect rect;if(!Native.GetWindowRect(window,out rect)) return false;
        Rectangle bounds=WindowManager.ScreenFor(window).Bounds;
        bool fullScreen=(Native.GetWindowLong(window,-16)&0x00c00000)==0 && rect.Left<=bounds.Left && rect.Top<=bounds.Top && rect.Right>=bounds.Right && rect.Bottom>=bounds.Bottom;
        return !fullScreen;
    }
    internal static Rectangle Place(Rectangle normal,Rectangle from,Rectangle to)
    {
        int width=Math.Min(normal.Width,to.Width),height=Math.Min(normal.Height,to.Height);
        double x=(normal.Left-from.Left)/(double)Math.Max(1,from.Width),y=(normal.Top-from.Top)/(double)Math.Max(1,from.Height);
        int left=to.Left+(int)Math.Round(x*to.Width),top=to.Top+(int)Math.Round(y*to.Height);
        left=Math.Max(to.Left,Math.Min(left,to.Right-width));top=Math.Max(to.Top,Math.Min(top,to.Bottom-height));
        return new Rectangle(left,top,width,height);
    }
    internal static bool MoveTo(IntPtr window,Screen destination)
    {
        if(!CanRoute(window)) return false;
        Screen from=WindowManager.ScreenFor(window);if(from.DeviceName==destination.DeviceName) return true;
        if(Native.IsZoomed(window)) {
            var p=new Native.Placement {Length=Marshal.SizeOf(typeof(Native.Placement))};if(!Native.GetWindowPlacement(window,ref p)) return false;
            // WINDOWPLACEMENT uses workspace coordinates for ordinary top-level windows.
            Rectangle normal=Rectangle.FromLTRB(p.Normal.Left,p.Normal.Top,p.Normal.Right,p.Normal.Bottom);
            normal.Offset(from.WorkingArea.Left-from.Bounds.Left,from.WorkingArea.Top-from.Bounds.Top);
            Rectangle placed=Place(normal,from.WorkingArea,destination.WorkingArea);
            Rectangle physical=placed;
            placed.Offset(destination.Bounds.Left-destination.WorkingArea.Left,destination.Bounds.Top-destination.WorkingArea.Top);
            p.Normal=new Native.Rect {Left=placed.Left,Top=placed.Top,Right=placed.Right,Bottom=placed.Bottom};p.Show=1;p.Flags=0;p.Max=new Native.Point {X=-1,Y=-1};
            // Windows may retain the old monitor while already maximized. Place the normal
            // window first, then re-maximize the foreground window on its new monitor.
            if(!Native.SetWindowPlacement(window,ref p)) return false;
            if(!Native.SetWindowPos(window,IntPtr.Zero,physical.Left,physical.Top,physical.Width,physical.Height,0x4000|0x0010|0x0004)) return false;
            Native.ShowWindowAsync(window,3);return true;
        }
        Native.Rect r;if(!Native.GetWindowRect(window,out r)) return false;
        Rectangle moved=Place(Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom),from.WorkingArea,destination.WorkingArea);
        return Native.SetWindowPos(window,IntPtr.Zero,moved.Left,moved.Top,moved.Width,moved.Height,0x4000|0x0010|0x0004);
    }
    public void Dispose() {if(disposed) return;disposed=true;Configure(false);timer.Dispose();}
}
