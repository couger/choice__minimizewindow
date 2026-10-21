using System;

// A gesture fires exactly once: on release, or when the hold threshold is reached.
internal sealed class ShortcutGesture
{
    internal bool Active, LongFired;
    internal int Kind, Trigger;
    internal long Started;
    internal IntPtr Focus;
    internal void Begin(int kind,int trigger,long now,IntPtr focus) {Active=true;LongFired=false;Kind=kind;Trigger=trigger;Started=now;Focus=focus;}
    internal int Poll(long now,int threshold) {if(!Active || LongFired || now-Started<threshold) return 0;LongFired=true;return Kind;}
    internal int Release() {int action=Active && !LongFired?Kind:0;Active=false;return action;}
    internal void Cancel() {Active=false;LongFired=false;}
}
