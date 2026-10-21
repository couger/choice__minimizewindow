using System;
using System.Collections.Generic;

internal static class InteractionTests
{
    internal static void Run(Action<bool,string> check)
    {
        var target=new DesktopTarget();string[] monitors={"left","middle","right"};
        check(target.Resolve("middle",monitors)=="middle","desktop sequence starts on focused monitor");
        check(target.Resolve("right",monitors)=="middle","automatic focus jump does not change desktop target");
        check(target.Resolve("left",monitors)=="middle","repeated desktop toggle stays on original monitor");
        target.Reset();check(target.Resolve("right",monitors)=="right","explicit user interaction allows another monitor");
        target.Remember("left");check(target.Resolve("middle",monitors)=="left","explicit screen selection sets desktop target");
        check(target.Resolve("middle",new[]{"middle","right"})=="middle","removed desktop monitor falls back to current focus");
        var attention=new CurrentScreen();
        check(attention.Resolve("middle",monitors)=="middle","initial current screen follows focused application");
        attention.Select("right");
        check(attention.Resolve("middle",monitors)=="right","desktop or taskbar click overrides unchanged application focus");
        target.Reset();target.Remember("middle");target.Reset();
        check(target.Resolve(attention.Resolve("middle",monitors),monitors)=="right","click selects new desktop toggle screen despite previous sequence");
        check(target.Resolve(attention.Resolve("left",monitors),monitors)=="right","automatic focus jump still preserves clicked desktop target");
        check(attention.Resolve("left",monitors)=="right","application switch also uses clicked screen");
        attention.Keyboard(false);
        check(attention.Resolve("middle",monitors)=="right","shell keyboard interaction preserves clicked screen");
        attention.Keyboard(true);target.Reset();
        check(attention.Resolve("left",monitors)=="left","application keyboard interaction follows updated focus");
        attention.Select("right");
        check(attention.Resolve("middle",new[]{"left","middle"})=="middle","disconnected clicked monitor falls back to focused screen");
        attention.Reset();check(attention.Resolve("middle",monitors)=="middle","re-enabling starts from current focus");
        var c=new ShortcutController();int shortCount=0,longCount=0,lastKind=0,lastDirection=0;IntPtr captured=IntPtr.Zero;
        c.ActionRequested+=delegate(int kind,IntPtr focus,int direction,bool held) {if(held) longCount++;else shortCount++;lastKind=kind;captured=focus;lastDirection=direction;};
        var focusWindow=new IntPtr(12345);
        c.Input(0xa2,true,0,focusWindow);c.Input(0xa4,true,0,focusWindow);
        check(c.Input(0x44,true,10,focusWindow),"desktop trigger consumed");
        c.Poll(2009);check(shortCount==0 && longCount==0,"hold does not fire early");
        c.Input(0x44,false,2009,IntPtr.Zero);
        check(shortCount==1 && longCount==0 && lastKind==1 && captured==focusWindow,"short release uses captured focus");
        c.Reset();c.Input(0xa2,true,0,focusWindow);c.Input(0xa4,true,0,focusWindow);c.Input(0x44,true,10,focusWindow);
        c.Input(0x44,true,1000,focusWindow);c.Poll(2010);c.Poll(3000);c.Input(0x44,false,3010,focusWindow);
        check(longCount==1 && shortCount==1,"long hold fires once without short action or repeats");
        c.Reset();c.Input(0xa2,true,0,focusWindow);check(c.Input(9,true,10,focusWindow),"Ctrl+Tab consumed");c.Input(9,false,30,focusWindow);
        check(lastKind==2 && lastDirection==1 && shortCount==2,"switch short action");
        c.Input(0xa0,true,40,focusWindow);c.Input(9,true,50,focusWindow);c.Input(9,false,60,focusWindow);
        check(lastDirection==-1,"Shift switches backward");
        c.Reset();c.HoldMilliseconds=500;c.Input(0xa2,true,0,focusWindow);c.Input(9,true,20,focusWindow);c.Poll(519);
        check(longCount==1,"custom threshold not early");c.Poll(520);c.Input(0xa2,false,600,focusWindow);c.Input(9,false,610,focusWindow);
        check(longCount==2 && shortCount==3 && lastKind==2,"custom threshold long switch does not short fire");
        c.Reset();c.Input(0xa2,true,0,focusWindow);c.Input(9,true,10,focusWindow);c.Input(0xa2,false,30,focusWindow);c.Input(9,false,40,focusWindow);
        check(shortCount==4,"modifier-first release fires short exactly once");
        c.Reset();c.Input(0xa2,true,0,focusWindow);c.Input(9,true,10,focusWindow);c.Input(0x1b,true,20,focusWindow);c.Input(9,false,30,focusWindow);c.Poll(5000);
        check(shortCount==4 && longCount==2,"Escape cancels pending gesture");
        c.Reset();c.Switch="停用";c.Input(0xa2,true,0,focusWindow);check(!c.Input(9,true,10,focusWindow),"disabled switch preserves Ctrl+Tab");
        c.Reset();c.Switch="Alt+Tab";c.Input(0xa4,true,0,focusWindow);c.Input(9,true,20,focusWindow);c.Input(9,false,40,focusWindow);
        check(shortCount==5 && lastKind==2,"Alt+Tab option");
        c.Reset();c.Desktop="Win+D";c.Input(0x5b,true,0,focusWindow);c.Input(0x44,true,10,focusWindow);c.Input(0x44,false,30,focusWindow);
        check(shortCount==6 && lastKind==1,"Win+D short option");
        c.Reset();c.Poll(10000);check(shortCount==6 && longCount==2,"reset cancels pending gestures");
        c.Input(0x5b,true,0,focusWindow);c.Input(0x44,true,10,focusWindow);c.Input(0x44,false,2000,focusWindow);
        check(shortCount==6 && longCount==3,"release classifies long hold even if timer was delayed");
    }
}
