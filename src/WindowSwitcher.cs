using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

internal sealed class WindowSwitcher
{
    internal sealed class Entry {internal IntPtr Handle;internal uint Pid;internal string Title;}
    List<Entry> cycle=new List<Entry>();
    string cycleScreen;
    IntPtr lastActivated;
    long lastAt;
    internal static List<Entry> Windows(Screen screen,uint onlyPid)
    {
        var result=new List<Entry>();
        Native.EnumWindows(delegate(IntPtr h,IntPtr p) {
            uint pid;Native.GetWindowThreadProcessId(h,out pid);
            if(onlyPid!=0 && pid!=onlyPid || !WindowManager.Eligible(h,true) || WindowManager.ScreenFor(h).DeviceName!=screen.DeviceName) return true;
            var title=new StringBuilder(512);Native.GetWindowText(h,title,title.Capacity);
            if(title.Length>0) result.Add(new Entry {Handle=h,Pid=pid,Title=title.ToString()}); return true;
        },IntPtr.Zero);return result;
    }
    internal static bool Activate(Entry entry)
    {
        uint pid;Native.GetWindowThreadProcessId(entry.Handle,out pid);
        if(!Native.IsWindow(entry.Handle) || pid!=entry.Pid) return false;
        if(Native.IsIconic(entry.Handle)) Native.ShowWindowAsync(entry.Handle,9);
        IntPtr target=Native.GetLastActivePopup(entry.Handle);
        if(target==IntPtr.Zero || !Native.IsWindowVisible(target)) target=entry.Handle;
        return WindowFocus.Activate(target);
    }
    internal Entry Next(Screen screen,IntPtr focus,int direction,uint onlyPid)
    {
        var current=Windows(screen,onlyPid);long now=System.Diagnostics.Stopwatch.GetTimestamp()*1000/System.Diagnostics.Stopwatch.Frequency;
        if(cycleScreen!=screen.DeviceName || focus!=lastActivated || now-lastAt>4000) cycle=current;
        else {cycle.RemoveAll(e=>!current.Exists(c=>c.Handle==e.Handle && c.Pid==e.Pid));foreach(var e in current) if(!cycle.Exists(c=>c.Handle==e.Handle && c.Pid==e.Pid)) cycle.Add(e);}
        cycleScreen=screen.DeviceName;if(cycle.Count==0) return null;
        int index=cycle.FindIndex(e=>e.Handle==focus);if(index<0) index=direction>0?-1:0;
        Entry next=cycle[(index+direction+cycle.Count)%cycle.Count];lastActivated=next.Handle;lastAt=now;return next;
    }
    internal void Switch(Screen screen,IntPtr focus,int direction) {Entry entry=Next(screen,focus,direction,0);if(entry!=null) Program.Log("Switch activated="+Activate(entry));}
    internal static void ShowPicker(Screen screen,IntPtr previous)
    {
        var entries=Windows(screen,0);
        using(var form=new Form {Text="选择应用窗口",Font=new Font("Microsoft YaHei UI",10),ClientSize=new Size(650,380),StartPosition=FormStartPosition.Manual,MinimizeBox=false,MaximizeBox=false,KeyPreview=true,TopMost=true}) {
            Rectangle area=screen.WorkingArea;form.Location=new Point(area.Left+Math.Max(0,(area.Width-form.Width)/2),area.Top+Math.Max(0,(area.Height-form.Height)/2));
            var label=new Label {Text=entries.Count==0?"此屏幕没有可切换的窗口":"双击窗口或选中后按 Enter · Esc 取消",Dock=DockStyle.Top,Height=38,Padding=new Padding(12,8,0,0)};
            var list=new ListBox {Dock=DockStyle.Fill,IntegralHeight=false};foreach(var e in entries) list.Items.Add(e.Title);
            if(entries.Count>0) {int index=entries.FindIndex(e=>e.Handle==previous);list.SelectedIndex=(index+1)%entries.Count;}
            Action refreshEntries=delegate {
                var fresh=Windows(screen,0);bool changed=fresh.Count!=entries.Count;
                if(!changed) for(int i=0;i<fresh.Count;i++) if(fresh[i].Handle!=entries[i].Handle || fresh[i].Pid!=entries[i].Pid || fresh[i].Title!=entries[i].Title) {changed=true;break;}
                if(!changed) return;IntPtr selected=list.SelectedIndex>=0?entries[list.SelectedIndex].Handle:IntPtr.Zero;
                entries=fresh;list.BeginUpdate();list.Items.Clear();foreach(var e in entries) list.Items.Add(e.Title);
                int index=entries.FindIndex(e=>e.Handle==selected);if(entries.Count>0) list.SelectedIndex=Math.Max(0,index);list.EndUpdate();
                label.Text=entries.Count==0?"此屏幕没有可切换的窗口":"双击窗口或选中后按 Enter · Esc 取消";
            };
            Entry chosen=null;Action confirm=delegate {if(list.SelectedIndex>=0) {Entry entry=entries[list.SelectedIndex];if(WindowManager.ScreenFor(entry.Handle).DeviceName!=screen.DeviceName || !Native.IsWindow(entry.Handle)) {refreshEntries();return;}chosen=entry;form.Close();}};
            list.DoubleClick+=delegate {confirm();};form.KeyDown+=delegate(object s,KeyEventArgs e) {if(e.KeyCode==Keys.Enter) {e.SuppressKeyPress=true;confirm();}if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;form.Close();}};
            WindowFocus.Request focusRequest=null;
            form.Controls.Add(list);form.Controls.Add(label);form.Shown+=delegate {focusRequest=new WindowFocus.Request(form,list);};
            using(var refresh=new Timer {Interval=400}) {refresh.Tick+=delegate {refreshEntries();};refresh.Start();try {form.ShowDialog();}finally {refresh.Stop();if(focusRequest!=null) focusRequest.Dispose();}}
            if(chosen!=null) Activate(chosen);else if(Native.IsWindow(previous)) Native.SetForegroundWindow(previous);
        }
    }
}
