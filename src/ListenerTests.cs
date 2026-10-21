using System;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

internal static class ListenerTests
{
    internal static void Run(Action<bool,string> check)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using(var listener=new ShortcutListener()) {
            check(listener.Configure("Ctrl+Alt+D","Ctrl+Tab",1000,true),"dedicated listener installs hook");
            var dispatcher=(Control)typeof(ShortcutListener).GetField("dispatcher",flags).GetValue(listener);
            var hookField=typeof(ShortcutListener).GetField("hook",flags);
            check(listener.Configure("Ctrl+Alt+D","Ctrl+Tab",1000,false),"listener pauses");
            check((IntPtr)hookField.GetValue(listener)==IntPtr.Zero,"pause releases hook");
            check(listener.Configure("Ctrl+Alt+D","Ctrl+Tab",1000,true),"listener re-enables");
            int ordinaryInteractions=0;listener.UserInteraction+=delegate {ordinaryInteractions++;};
            IntPtr markedKey=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.KeyData)));
            try {
                Marshal.StructureToPtr(new Native.KeyData {Key=0xe8,Extra=MenuMask.Marker},markedKey,false);
                dispatcher.Invoke((Action)delegate {typeof(ShortcutListener).GetMethod("OnKey",flags).Invoke(listener,new object[]{0,new IntPtr(0x100),markedKey});});
                check(ordinaryInteractions==0,"own menu-mask inputs bypass attention and gesture processing");
            } finally {Marshal.FreeHGlobal(markedKey);}
            int clicks=0;System.Drawing.Point lastPoint=System.Drawing.Point.Empty;
            listener.MouseInteraction+=delegate(System.Drawing.Point point) {clicks++;lastPoint=point;};
            var mouseMethod=typeof(ShortcutListener).GetMethod("OnMouse",flags);
            IntPtr mouseData=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.MouseData)));
            try {
                var data=new Native.MouseData {Position=new Native.Point {X=-3700,Y=-100}};
                Marshal.StructureToPtr(data,mouseData,false);
                dispatcher.Invoke((Action)delegate {
                    foreach(int message in new[]{0x201,0x204,0x207,0x20b,0x20a,0x20e})
                        mouseMethod.Invoke(listener,new object[]{0,new IntPtr(message),mouseData});
                    mouseMethod.Invoke(listener,new object[]{0,new IntPtr(0x200),IntPtr.Zero});
                    mouseMethod.Invoke(listener,new object[]{-1,new IntPtr(0x201),IntPtr.Zero});
                });
                check(clicks==6 && lastPoint.X==-3700 && lastPoint.Y==-100,"mouse hook captures exact negative coordinates for all buttons and both scroll axes; movement ignored");
                listener.Configure("Ctrl+Alt+D","Ctrl+Tab",1000,false);
                dispatcher.Invoke((Action)delegate {mouseMethod.Invoke(listener,new object[]{0,new IntPtr(0x201),IntPtr.Zero});});
                check(clicks==6,"paused listener ignores mouse attention changes");
                listener.Configure("Ctrl+Alt+D","Ctrl+Tab",1000,true);
            } finally {Marshal.FreeHGlobal(mouseData);}
            dispatcher.Invoke((Action)delegate {
                Native.UnhookWindowsHookEx((IntPtr)hookField.GetValue(listener));hookField.SetValue(listener,IntPtr.Zero);
                typeof(ShortcutListener).GetField("lastRenewed",flags).SetValue(listener,-30001L);
            });
            // No DoEvents: deliberately block this UI/test thread while the listener recovers.
            Thread.Sleep(300);
            bool recovered=false;
            dispatcher.Invoke((Action)delegate {recovered=(IntPtr)hookField.GetValue(listener)!=IntPtr.Zero;});
            check(recovered,"listener recovers lost hook while UI thread is blocked");
            using(var fired=new ManualResetEvent(false)) {
                int count=0;bool correct=false;
                listener.ActionRequested+=delegate(int kind,IntPtr focus,int direction,bool held) {
                    count++;correct=kind==1 && held && focus==new IntPtr(12345);fired.Set();
                };
                dispatcher.Invoke((Action)delegate {
                    var controller=(ShortcutController)typeof(ShortcutListener).GetField("controller",flags).GetValue(listener);
                    long now=((Stopwatch)typeof(ShortcutListener).GetField("clock",flags).GetValue(listener)).ElapsedMilliseconds;
                    // Isolate the synthetic gesture from the user's real typing during self-tests.
                    Native.UnhookWindowsHookEx((IntPtr)hookField.GetValue(listener));hookField.SetValue(listener,IntPtr.Zero);
                    typeof(ShortcutListener).GetField("lastRenewed",flags).SetValue(listener,now);controller.Reset();
                    controller.Input(0xa2,true,now,IntPtr.Zero);controller.Input(0xa4,true,now,IntPtr.Zero);
                    controller.Input(0x44,true,now,new IntPtr(12345));
                });
                check(fired.WaitOne(2500) && correct,"long press timer fires while UI thread is blocked");
                dispatcher.Invoke((Action)delegate {
                    var controller=(ShortcutController)typeof(ShortcutListener).GetField("controller",flags).GetValue(listener);
                    controller.Input(0x44,false,100000,IntPtr.Zero);controller.Reset();
                });
                check(count==1,"worker long press release does not fire short action");
            }
        }
        check(true,"listener disposes and joins worker cleanly");
    }
}
