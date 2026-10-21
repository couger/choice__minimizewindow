using System;
using System.Collections.Generic;

internal sealed class ShortcutController
{
    internal string Desktop="Ctrl+Alt+D", Switch="Ctrl+Tab";
    internal int HoldMilliseconds=2000;
    internal event Action<int,IntPtr,int,bool> ActionRequested;
    readonly HashSet<int> pressed=new HashSet<int>();
    readonly HashSet<int> swallowed=new HashSet<int>();
    readonly HashSet<int> menuModifiers=new HashSet<int>();
    readonly ShortcutGesture gesture=new ShortcutGesture();
    int direction=1;
    bool Down(params int[] keys) {foreach(int key in keys) if(pressed.Contains(key)) return true;return false;}
    bool Modifier(int key) {return key==0x10 || key==0x11 || key==0x12 || key>=0xa0 && key<=0xa5 || key==0x5b || key==0x5c;}
    bool Matches(string shortcut)
    {
        bool ctrl=Down(0x11,0xa2,0xa3),alt=Down(0x12,0xa4,0xa5),win=Down(0x5b,0x5c),shift=Down(0x10,0xa0,0xa1);
        if(shortcut=="停用") return false;
        bool modifiers=ctrl==shortcut.Contains("Ctrl") && alt==shortcut.Contains("Alt") && win==shortcut.Contains("Win");
        return modifiers && (shortcut.EndsWith("Tab") || !shift);
    }
    void Fire(int kind,bool held) {if(kind!=0 && ActionRequested!=null) ActionRequested(kind,gesture.Focus,direction,held);}
    internal bool Input(int key,bool down,long now,IntPtr focus)
    {
        bool repeated=down && pressed.Contains(key);
        if(down) pressed.Add(key);else pressed.Remove(key);
        if(swallowed.Contains(key)) {
            if(!down) {swallowed.Remove(key);Fire(gesture.Poll(now,HoldMilliseconds),true);Fire(gesture.Release(),false);}return true;
        }
        if(!down && Modifier(key) && gesture.Active && !Matches(gesture.Kind==1?Desktop:Switch)) {Fire(gesture.Poll(now,HoldMilliseconds),true);Fire(gesture.Release(),false);return false;}
        if(!down || repeated) return false;
        int kind=key==0x44 && Matches(Desktop)?1:key==9 && Matches(Switch)?2:0;
        if(kind!=0) {
            foreach(int modifier in new[]{0x5b,0x5c,0x12,0xa4,0xa5}) if(pressed.Contains(modifier)) menuModifiers.Add(modifier);
            gesture.Begin(kind,key,now,focus);direction=Down(0x10,0xa0,0xa1)?-1:1;swallowed.Add(key);return true;
        }
        if(!Modifier(key)) gesture.Cancel();return false;
    }
    internal void Poll(long now) {Fire(gesture.Poll(now,HoldMilliseconds),true);}
    internal bool TakeMenuMask(int key,bool down) {return !down && menuModifiers.Remove(key);}
    internal bool IsLaunchKey(int key)
    {
        if(key==13) return true;
        bool ctrl=Down(0x11,0xa2,0xa3),alt=Down(0x12,0xa4,0xa5),win=Down(0x5b,0x5c);
        if(win && !(key==0x45 || key==0x52 || key==0x53 || key>=0x30 && key<=0x39)) return false;
        if(!(ctrl || alt || win) || !(key>=0x30 && key<=0x5a || key>=0x70 && key<=0x87)) return false;
        // Common editing and closing commands do not express a window-launch request.
        if(ctrl && !alt && !win && Array.IndexOf(new[]{0x41,0x43,0x56,0x58,0x59,0x5a,0x53,0x57},key)>=0) return false;
        if(alt && key==0x73) return false;
        return true;
    }
    internal void Reset() {gesture.Cancel();pressed.Clear();swallowed.Clear();menuModifiers.Clear();}
    internal void Seed(int key) {pressed.Add(key);}
}
