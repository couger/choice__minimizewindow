using System;

// A desktop/taskbar click can express attention without moving foreground focus.
internal sealed class CurrentScreen
{
    string device;
    internal void Select(string selectedDevice) {device=selectedDevice;}
    internal void Keyboard(bool applicationFocused) {if(applicationFocused) device=null;}
    internal void Reset() {device=null;}
    internal string Resolve(string focusedDevice,string[] available)
    {
        if(Array.IndexOf(available,device)>=0) return device;
        return Array.IndexOf(available,focusedDevice)>=0?focusedDevice:available[0];
    }
}
