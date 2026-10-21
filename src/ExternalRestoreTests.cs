using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class ExternalRestoreTests
{
    internal static void Run(uint pid,IntPtr maximized,IntPtr originalMinimized,Action pump,Action<bool,string> check)
    {
        Screen[] screens=WindowManager.Screens();
        var all=new List<IntPtr>();foreach(Screen screen in screens) all.AddRange(WindowManager.WindowsOn(screen,pid));
        foreach(Screen target in screens) {
            foreach(IntPtr h in all) if(!Native.IsIconic(h)) Native.ShowWindowAsync(h,6);pump();
            var fresh=new WindowManager();
            check(fresh.Pending==0,"external restore starts without a saved batch");
            check(fresh.ActionText(target,pid)=="还原窗口","all-minimized screen offers restore");
            fresh.Toggle(target,pid);pump();
            foreach(IntPtr h in all) {
                bool belongs=WindowManager.ScreenFor(h).DeviceName==target.DeviceName;
                check(Native.IsIconic(h)!=belongs,"external restore affects only chosen screen");
            }
            check(fresh.Pending==0,"external restore creates no minimize batch");
            if(WindowManager.ScreenFor(maximized).DeviceName==target.DeviceName) {
                var p=new Native.Placement();p.Length=Marshal.SizeOf(p);Native.GetWindowPlacement(maximized,ref p);
                check(p.Show==3,"external restore preserves formerly maximized state");
            }
            check(fresh.ActionText(target,pid)=="显示桌面","restored screen offers show desktop");
            fresh.Toggle(target,pid);pump();
            check(fresh.Pending>0,"restored screen can be minimized again");
            IntPtr reopened=WindowManager.WindowsOn(target,pid)[0];Native.ShowWindowAsync(reopened,9);pump();
            check(fresh.ActionText(target,pid)=="显示桌面","manually reopened window overrides old batch state");
            fresh.Toggle(target,pid);pump();
            foreach(IntPtr h in WindowManager.WindowsOn(target,pid)) check(Native.IsIconic(h),"existing batch does not force restore when a window is open");
            fresh.Toggle(target,pid);pump();
            foreach(IntPtr h in WindowManager.WindowsOn(target,pid)) check(!Native.IsIconic(h),"next toggle restores fresh batch");
        }
        // Restore fixture baselines for subsequent checks.
        foreach(IntPtr h in all) if(Native.IsIconic(h)) Native.ShowWindowAsync(h,9);pump();
        Native.ShowWindowAsync(maximized,3);Native.ShowWindowAsync(originalMinimized,6);pump();
    }
}
