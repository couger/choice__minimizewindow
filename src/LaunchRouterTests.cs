using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class LaunchRouterTests
{
    internal static void Run(IntPtr normal,IntPtr maximized,Action pump,Action<bool,string> check)
    {
        var c=new ShortcutController();c.Seed(0xa2);
        check(c.IsLaunchKey(0x45),"Ctrl+E arms global application recall routing");
        check(c.IsLaunchKey(0x4e) && c.IsLaunchKey(0x4f),"Ctrl+N and Ctrl+O arm new-window routing");
        check(!c.IsLaunchKey(0x43) && !c.IsLaunchKey(0x56) && !c.IsLaunchKey(0x57),"copy paste and close do not arm routing");
        c.Reset();c.Seed(0xa4);check(!c.IsLaunchKey(9) && !c.IsLaunchKey(0x73),"native Alt+Tab and Alt+F4 do not arm routing");
        c.Reset();c.Seed(0x5b);check(c.IsLaunchKey(0x45) && c.IsLaunchKey(0x31) && !c.IsLaunchKey(0x4c),"Explorer and pinned-app Windows shortcuts route; lock shortcut does not");
        c.Reset();check(!c.IsLaunchKey(0x45) && c.IsLaunchKey(13),"plain typing does not arm routing; Enter launch is supported");
        var placed=LaunchRouter.Place(new Rectangle(-3500,-80,800,600),new Rectangle(-3840,-143,3840,2160),new Rectangle(2560,171,1920,1080));
        check(new Rectangle(2560,171,1920,1080).Contains(placed),"routed geometry handles negative origins and smaller monitor");
        check(LaunchRouter.Place(new Rectangle(0,0,4000,3000),new Rectangle(0,0,4000,3000),new Rectangle(100,200,800,600))==new Rectangle(100,200,800,600),"oversized window is constrained to destination work area");
        uint fixturePid;Native.GetWindowThreadProcessId(normal,out fixturePid);
        using(var router=new LaunchRouter(fixturePid)) {
            check(router.InScope(normal) && !router.InScope(IntPtr.Zero),"test launch routing is restricted to its fixture process");
            check(router.Configure(true),"foreground routing event hook installs");
            router.Arm(WindowManager.Screens()[0],normal);
            router.Configure(false);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            check((IntPtr)typeof(LaunchRouter).GetField("hook",flags).GetValue(router)==IntPtr.Zero && typeof(LaunchRouter).GetField("target",flags).GetValue(router)==null,"disabling routing removes hook and pending launch");
            router.Arm(WindowManager.Screens()[0],normal);
            check(typeof(LaunchRouter).GetField("target",flags).GetValue(router)==null,"disabled routing ignores launch requests");
        }
        Screen initialNormal=WindowManager.ScreenFor(normal),initialMax=WindowManager.ScreenFor(maximized);
        check(!LaunchRouter.CanRoute(IntPtr.Zero),"routing ignores invalid windows");
        Native.ShowWindowAsync(normal,6);pump();check(!LaunchRouter.CanRoute(normal),"routing never restores a background minimized window");Native.ShowWindowAsync(normal,9);pump();
        foreach(Screen destination in WindowManager.Screens()) {
            check(LaunchRouter.MoveTo(normal,destination),"request routing normal window to "+destination.DeviceName);pump();
            check(WindowManager.ScreenFor(normal).DeviceName==destination.DeviceName && !Native.IsZoomed(normal),"routed normal window arrives without maximizing on "+destination.DeviceName);
            check(LaunchRouter.MoveTo(maximized,destination),"request routing maximized window to "+destination.DeviceName);pump();
            var placement=new Native.Placement {Length=Marshal.SizeOf(typeof(Native.Placement))};Native.GetWindowPlacement(maximized,ref placement);
            check(WindowManager.ScreenFor(maximized).DeviceName==destination.DeviceName && Native.IsZoomed(maximized),"routed maximized window retains state on "+destination.DeviceName+" actual="+WindowManager.ScreenFor(maximized).DeviceName+" zoomed="+Native.IsZoomed(maximized)+" normal="+placement.Normal.Left+","+placement.Normal.Top+","+placement.Normal.Right+","+placement.Normal.Bottom);
        }
        LaunchRouter.MoveTo(normal,initialNormal);LaunchRouter.MoveTo(maximized,initialMax);pump();
        Native.ShowWindowAsync(maximized,9);pump();
        check(WindowManager.ScreenFor(maximized).DeviceName==initialMax.DeviceName,"maximized route also updates restored monitor");Native.ShowWindowAsync(maximized,3);pump();
    }
}
