using System;
using System.Reflection;
using System.Windows.Forms;

internal static class TrayMenuTests
{
    internal static void Run(Action<bool,string> check)
    {
        // Exercise the actual NotifyIcon path on a fresh application form.
        using(var form=new MainForm((uint)System.Diagnostics.Process.GetCurrentProcess().Id)) {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var tray=(NotifyIcon)typeof(MainForm).GetField("tray",flags).GetValue(form);
            var menu=tray.ContextMenuStrip;
            var show=typeof(NotifyIcon).GetMethod("ShowContextMenu",flags);
            check(menu.Items.Count==0,"tray menu starts empty before first right click");
            show.Invoke(tray,null);
            check(menu.Visible && menu.Items.Count>0,"first tray right click displays dynamically populated menu");
            menu.Close();
            show.Invoke(tray,null);
            check(menu.Visible,"subsequent tray right click displays menu");
            menu.Close();menu.Items.Clear();
            show.Invoke(tray,null);
            check(menu.Visible && menu.Items.Count>0,"empty tray menu can be rebuilt and displayed in one click");
            menu.Close();
            check(menu.Items[0].Text=="启用","tray enable option remains available");
            ToolStripMenuItem layout=null;foreach(ToolStripItem item in menu.Items) if(item.Text=="远程桌面布局保护") layout=(ToolStripMenuItem)item;
            check(layout!=null && layout.DropDownItems.Count==7,"layout protection submenu distinguishes manual and automatic records");
            check(((ToolStripMenuItem)layout.DropDownItems[0]).Checked,"layout auto protection enabled by default in isolated test profile");
            check(!layout.DropDownItems[4].Enabled && !layout.DropDownItems[5].Enabled && !layout.DropDownItems[6].Enabled,"layout actions unavailable until local screens stabilize");
        }
    }
}
