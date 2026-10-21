using System;
using System.Windows.Forms;

internal static class MovementTests
{
    internal static void Run(IntPtr window,uint pid,WindowManager manager,Action pump,Action<bool,string> check)
    {
        Screen[] screens=WindowManager.Screens();if(screens.Length<2) return;
        var cache=new WindowSwitcher();IntPtr current=window;
        for(int step=1;step<=screens.Length;step++) {
            Screen source=WindowManager.ScreenFor(window),destination=screens[step%screens.Length];
            // Prime the same cycling cache before moving the actual, separate-process window.
            var next=cache.Next(source,current,1,pid);if(next!=null) current=next.Handle;
            var area=destination.WorkingArea;
            check(Native.SetWindowPos(window,IntPtr.Zero,area.Left+70,area.Top+80,350,220,0x4000|0x0010|0x0004),"request actual window move "+step);pump();
            check(WindowManager.ScreenFor(window).DeviceName==destination.DeviceName,"moved window uses destination screen "+step);
            check(!WindowSwitcher.Windows(source,pid).Exists(e=>e.Handle==window),"moved window absent from old screen list "+step);
            check(WindowSwitcher.Windows(destination,pid).Exists(e=>e.Handle==window),"moved window appears in new screen list "+step);
            int oldCount=WindowSwitcher.Windows(source,pid).Count;
            for(int i=0;i<oldCount+1;i++) {next=cache.Next(source,current,1,pid);check(next==null || next.Handle!=window,"cycle cache removes moved window "+step);if(next!=null) current=next.Handle;}
            manager.Toggle(source,pid);pump();check(!Native.IsIconic(window),"old screen minimize leaves moved window alone "+step);manager.RestoreAll();pump();
            manager.Toggle(destination,pid);pump();check(Native.IsIconic(window),"new screen minimizes moved window "+step);
            check(WindowManager.ScreenFor(window).DeviceName==destination.DeviceName,"minimized moved window retains new screen "+step);
            manager.RestoreAll();pump();check(!Native.IsIconic(window) && WindowManager.ScreenFor(window).DeviceName==destination.DeviceName,"restore keeps moved window on new screen "+step);
        }
    }
    internal static void RunMaximized(IntPtr window,uint pid,Action pump,Action<bool,string> check)
    {
        Screen[] screens=WindowManager.Screens();if(screens.Length<2) return;Screen original=WindowManager.ScreenFor(window);
        foreach(Screen destination in new[]{screens[1],original}) {
            Native.ShowWindowAsync(window,9);pump();var area=destination.WorkingArea;
            Native.SetWindowPos(window,IntPtr.Zero,area.Left+70,area.Top+80,350,220,0x4000|0x0010|0x0004);pump();Native.ShowWindowAsync(window,3);pump();
            check(WindowManager.ScreenFor(window).DeviceName==destination.DeviceName,"maximized moved window uses current screen");
            foreach(Screen screen in screens) check(WindowSwitcher.Windows(screen,pid).Exists(e=>e.Handle==window)==(screen.DeviceName==destination.DeviceName),"maximized moved window appears only on current screen");
        }
    }
}
