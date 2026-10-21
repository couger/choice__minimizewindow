using System;
using System.Runtime.InteropServices;

internal static class MenuMaskTests
{
    internal static void Run(Action<bool,string> check,bool nativeInput=true)
    {
        check(Marshal.SizeOf(typeof(Native.Input))==40 && Marshal.OffsetOf(typeof(Native.Input),"Data").ToInt32()==8,"menu mask INPUT has correct Windows x64 size and alignment");
        // Exercise the native interop with only an unassigned key; do not synthesize Win/Alt.
        if(nativeInput) check(MenuMask.Release(0xe8,false),"native menu-mask SendInput accepts tagged unassigned-key sequence");
        foreach(int win in new[]{0x5b,0x5c}) foreach(bool modifierFirst in new[]{false,true}) foreach(bool held in new[]{false,true}) {
            var c=new ShortcutController {Desktop="Win+D",HoldMilliseconds=500};int shortCount=0,longCount=0;
            c.ActionRequested+=delegate(int kind,IntPtr focus,int direction,bool isLong) {if(isLong) longCount++;else shortCount++;};
            check(!c.Input(win,true,0,IntPtr.Zero),"Win down remains available to Windows");
            check(c.Input(0x44,true,10,IntPtr.Zero),"Win+D consumes D for menu-mask regression");
            if(held) c.Poll(510);
            long release=held?600:20;
            int first=modifierFirst?win:0x44,second=modifierFirst?0x44:win;
            c.Input(first,false,release,IntPtr.Zero);
            check(c.TakeMenuMask(first,false)==modifierFirst,"mask applies only to modifier release in either key order");
            c.Input(second,false,release+10,IntPtr.Zero);
            check(c.TakeMenuMask(second,false)==!modifierFirst,"pending mask survives trigger-first release");
            check(!c.TakeMenuMask(win,false),"each modifier release is masked once");
            check(shortCount==(held?0:1) && longCount==(held?1:0),"masked Win release preserves short/long gesture classification");
            var inputs=MenuMask.ReleaseInputs(win,true);
            check(inputs.Length==3 && inputs[0].Data.Keyboard.Key==0xe8 && inputs[0].Data.Keyboard.Flags==0 && inputs[1].Data.Keyboard.Key==0xe8 && inputs[1].Data.Keyboard.Flags==2 && inputs[2].Data.Keyboard.Key==win && inputs[2].Data.Keyboard.Flags==3,"mask precedes matching extended Win release");
            foreach(var input in inputs) check(input.Type==1 && input.Data.Keyboard.Extra==MenuMask.Marker,"mask inputs carry bypass marker");
        }
        var shortGesture=new ShortcutController {Desktop="Win+D"};int fired=0;
        shortGesture.ActionRequested+=delegate {fired++;};
        shortGesture.Input(0x5b,true,0,IntPtr.Zero);shortGesture.Input(0x44,true,10,IntPtr.Zero);shortGesture.Input(0x44,false,20,IntPtr.Zero);
        shortGesture.Input(0x44,true,30,IntPtr.Zero);shortGesture.Input(0x44,false,40,IntPtr.Zero);shortGesture.Input(0x5b,false,50,IntPtr.Zero);
        check(fired==2 && shortGesture.TakeMenuMask(0x5b,false),"repeated short Win+D while holding Win still masks final release");
        foreach(int key in new[]{0x5b,0x5c,0xa4,0xa5}) {
            var c=new ShortcutController {Desktop="Win+D",Switch="Alt+Tab"};
            c.Input(key,true,0,IntPtr.Zero);
            check(!c.Input(key,false,10,IntPtr.Zero) && !c.TakeMenuMask(key,false),"standalone Win/Alt retains native behavior: "+key);
        }
        var alt=new ShortcutController {Switch="Alt+Tab"};alt.Input(0xa4,true,0,IntPtr.Zero);alt.Input(9,true,10,IntPtr.Zero);alt.Input(9,false,20,IntPtr.Zero);alt.Input(0xa4,false,30,IntPtr.Zero);
        check(alt.TakeMenuMask(0xa4,false),"Alt+Tab also masks unintended application menu activation");
        var cancelled=new ShortcutController {Desktop="Win+D"};cancelled.Input(0x5b,true,0,IntPtr.Zero);cancelled.Input(0x44,true,10,IntPtr.Zero);cancelled.Input(0x1b,true,20,IntPtr.Zero);cancelled.Input(0x5b,false,30,IntPtr.Zero);
        check(cancelled.TakeMenuMask(0x5b,false),"Escape-cancelled shortcut still suppresses bare Win menu");
        cancelled.Reset();check(!cancelled.TakeMenuMask(0x5b,false),"reset clears pending menu masks");
        var native=new ShortcutController {Desktop="Win+D"};native.Input(0x5b,true,0,IntPtr.Zero);
        check(!native.Input(0x45,true,10,IntPtr.Zero) && !native.TakeMenuMask(0x5b,false),"unconfigured Win shortcuts are not masked or intercepted");
    }
}
