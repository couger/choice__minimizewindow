using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class CurrentScreenTests
{
    internal static void Run(IntPtr unchangedFocus,Action<bool,string> check)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using(var form=new MainForm((uint)System.Diagnostics.Process.GetCurrentProcess().Id)) {
            // Exercise worker-to-UI mouse delivery without installing the application's poll timer.
            ((Timer)typeof(MainForm).GetField("timer",flags).GetValue(form)).Stop();
            var handle=form.Handle;
            var listener=(ShortcutListener)typeof(MainForm).GetField("shortcuts",flags).GetValue(form);
            var dispatcher=(Control)typeof(ShortcutListener).GetField("dispatcher",flags).GetValue(listener);
            var callback=typeof(ShortcutListener).GetMethod("OnMouse",flags);
            var resolve=typeof(MainForm).GetMethod("ResolveCurrentScreen",flags);
            var sequence=(DesktopTarget)typeof(MainForm).GetField("desktopTarget",flags).GetValue(form);
            var router=(LaunchRouter)typeof(MainForm).GetField("launchRouter",flags).GetValue(form);
            check(!router.InScope(unchangedFocus),"self-test routing excludes other-process windows even while armed");
            check(router.Configure(true),"mouse launch routing integration starts");
            Screen[] screens=WindowManager.Screens();string[] devices=Array.ConvertAll(screens,s=>s.DeviceName);
            check(listener.Configure("Ctrl+Alt+D","Ctrl+Tab",1000,true),"attention integration listener starts");
            IntPtr data=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.MouseData)));
            try {
                foreach(Screen screen in screens) {
                    foreach(Point point in new[]{new Point(screen.Bounds.Left+20,screen.Bounds.Top+20),new Point(screen.Bounds.Right-20,screen.Bounds.Bottom-5)}) {
                        sequence.Remember(WindowManager.ScreenFor(unchangedFocus).DeviceName);
                        Marshal.StructureToPtr(new Native.MouseData {Position=new Native.Point {X=point.X,Y=point.Y}},data,false);
                        dispatcher.Invoke((Action)delegate {callback.Invoke(listener,new object[]{0,new IntPtr(0x201),data});});
                        Application.DoEvents();
                        Screen current=(Screen)resolve.Invoke(form,new object[]{unchangedFocus});
                        check(current.DeviceName==screen.DeviceName,"actual click payload selects monitor despite unchanged foreground: "+screen.DeviceName+" at "+point);
                        check(sequence.Resolve(current.DeviceName,devices)==screen.DeviceName,"click replaces preceding desktop toggle sequence: "+screen.DeviceName);
                        check((string)typeof(LaunchRouter).GetField("target",flags).GetValue(router)==screen.DeviceName,"mouse launch request captures clicked screen: "+screen.DeviceName);
                        router.Cancel();
                    }
                }
            } finally {Marshal.FreeHGlobal(data);}
        }
    }
}
