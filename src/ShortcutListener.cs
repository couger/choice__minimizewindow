using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Own the hook and gesture timer on a message thread independent of window rendering.
internal sealed class ShortcutListener : IDisposable
{
    readonly ShortcutController controller=new ShortcutController();
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Thread thread;
    Control dispatcher;
    IntPtr hook,mouseHook;
    Native.HookProc callback,mouseCallback;
    System.Windows.Forms.Timer timer;
    long lastRenewed;
    bool enabled,disposed;
    internal event Action<int,IntPtr,int,bool> ActionRequested;
    internal event Action UserInteraction;
    internal event Action<System.Drawing.Point> MouseInteraction;
    internal event Action<System.Drawing.Point,IntPtr> LaunchClick;
    internal event Action<IntPtr,bool> RoutingKey;
    internal ShortcutListener()
    {
        using(var ready=new ManualResetEvent(false)) {
            thread=new Thread(delegate() {
                dispatcher=new Control();var handle=dispatcher.Handle;
                callback=OnKey;
                mouseCallback=OnMouse;
                controller.ActionRequested+=delegate(int kind,IntPtr focus,int direction,bool held) {
                    var action=ActionRequested;if(action!=null) action(kind,focus,direction,held);
                };
                timer=new System.Windows.Forms.Timer {Interval=30};
                timer.Tick+=delegate {
                    if(!enabled) return;
                    controller.Poll(clock.ElapsedMilliseconds);
                    // Windows silently removes timed-out hooks. Renew only between gestures.
                    if(clock.ElapsedMilliseconds-lastRenewed>=30000 && !RelevantKeyDown()) Install();
                };
                timer.Start();ready.Set();Application.Run();
                timer.Dispose();dispatcher.Dispose();
            });
            thread.IsBackground=true;thread.Name="Shortcut listener";thread.SetApartmentState(ApartmentState.STA);
            thread.Start();ready.WaitOne();
        }
    }
    static readonly int[] Keys={0xa0,0xa1,0xa2,0xa3,0xa4,0xa5,0x5b,0x5c,0x44,9};
    static bool RelevantKeyDown() {foreach(int key in Keys) if(Native.GetAsyncKeyState(key)<0) return true;return false;}
    void Remove() {if(hook!=IntPtr.Zero) {Native.UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}if(mouseHook!=IntPtr.Zero) {Native.UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}controller.Reset();}
    bool Install()
    {
        Remove();foreach(int key in Keys) if(key!=0x44 && key!=9 && Native.GetAsyncKeyState(key)<0) controller.Seed(key);
        hook=Native.SetWindowsHookEx(13,callback,Native.GetModuleHandle(null),0);
        mouseHook=Native.SetWindowsHookEx(14,mouseCallback,Native.GetModuleHandle(null),0);lastRenewed=clock.ElapsedMilliseconds;
        bool ok=hook!=IntPtr.Zero && mouseHook!=IntPtr.Zero;if(!ok) Remove();return ok;
    }
    internal bool Configure(string desktop,string switchKey,int hold,bool active)
    {
        return (bool)dispatcher.Invoke(new Func<bool>(delegate {
            enabled=active;Remove();controller.Desktop=desktop;controller.Switch=switchKey;controller.HoldMilliseconds=hold;
            bool ok=!enabled || Install();
            Program.Log("Shortcut listener thread=True hook="+(hook!=IntPtr.Zero)+" desktop="+desktop+" switch="+switchKey+" holdMs="+hold+" enabled="+enabled);
            return ok;
        }));
    }
    IntPtr OnKey(int code,IntPtr w,IntPtr l)
    {
        if(code>=0 && enabled) {
            var key=(Native.KeyData)Marshal.PtrToStructure(l,typeof(Native.KeyData));
            // Our mask and replayed release must reach Windows without cancelling the gesture
            // or being interpreted as intentional keyboard activity on another monitor.
            if(key.Extra==MenuMask.Marker) return Native.CallNextHookEx(hook,code,w,l);
            bool down=w.ToInt64()==0x100 || w.ToInt64()==0x104;
            IntPtr focus=Native.GetForegroundWindow();
            bool consumed=controller.Input((int)key.Key,down,clock.ElapsedMilliseconds,focus);
            if(controller.TakeMenuMask((int)key.Key,down)) {
                if(MenuMask.Release((int)key.Key,(key.Flags&1)!=0)) return new IntPtr(1);
                dispatcher.BeginInvoke((Action)delegate {Program.Log("Menu mask input failed; physical modifier release passed through");});
            }
            if(consumed) return new IntPtr(1);
            int vk=(int)key.Key;
            if(down && vk!=0x10 && vk!=0x11 && vk!=0x12 && !(vk>=0xa0 && vk<=0xa5) && vk!=0x5b && vk!=0x5c) {
                var routing=RoutingKey;if(routing!=null) routing(focus,controller.IsLaunchKey(vk));
                NotifyInteraction();
            }
        }
        return Native.CallNextHookEx(hook,code,w,l);
    }
    void NotifyInteraction() {var action=UserInteraction;if(action!=null) action();}
    IntPtr OnMouse(int code,IntPtr w,IntPtr l)
    {
        long message=w.ToInt64();
        if(code>=0 && enabled && (message==0x201 || message==0x204 || message==0x207 || message==0x20b || message==0x20a || message==0x20e)) {
            var data=(Native.MouseData)Marshal.PtrToStructure(l,typeof(Native.MouseData));
            var action=MouseInteraction;if(action!=null) action(new System.Drawing.Point(data.Position.X,data.Position.Y));
            if(message==0x201 || message==0x207) {
                uint pid;Native.GetWindowThreadProcessId(Native.WindowFromPoint(data.Position),out pid);
                var launch=LaunchClick;if(launch!=null && pid!=(uint)Process.GetCurrentProcess().Id) launch(new System.Drawing.Point(data.Position.X,data.Position.Y),Native.GetForegroundWindow());
            }
        }
        return Native.CallNextHookEx(mouseHook,code,w,l);
    }
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        dispatcher.Invoke((Action)delegate {enabled=false;Remove();Application.ExitThread();});
        thread.Join();
    }
}
