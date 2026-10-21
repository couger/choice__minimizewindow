using System;

// Keep a desktop-toggle sequence on its original monitor until the user acts elsewhere.
internal sealed class DesktopTarget
{
    string device;
    internal string Resolve(string focusedDevice,string[] available)
    {
        if(Array.IndexOf(available,device)<0) device=Array.IndexOf(available,focusedDevice)>=0?focusedDevice:available[0];
        return device;
    }
    internal void Remember(string selectedDevice) {device=selectedDevice;}
    internal void Reset() {device=null;}
}
