using System;
using System.Windows.Forms;

internal static class WindowFocus
{
    internal static bool Activate(IntPtr target)
    {
        if(!Native.IsWindow(target)) return false;
        if(Native.SetForegroundWindow(target)) return true;
        uint pid;uint foregroundThread=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out pid),ownThread=Native.GetCurrentThreadId();
        if(foregroundThread==0 || foregroundThread==ownThread || !Native.AttachThreadInput(ownThread,foregroundThread,true)) return false;
        try {return Native.SetForegroundWindow(target);}finally {Native.AttachThreadInput(ownThread,foregroundThread,false);}
    }
    internal sealed class Request : IDisposable
    {
        readonly Form form;readonly Control input;readonly Timer timer=new Timer();int attempts,stableTicks;bool disposed;
        internal Request(Form form,Control input)
        {
            this.form=form;this.input=input;form.TopMost=true;
            timer.Interval=80;timer.Tick+=Tick;form.VisibleChanged+=VisibilityChanged;form.Disposed+=FormDisposed;
            TryFocus();if(!disposed) timer.Start();
        }
        void Tick(object sender,EventArgs e) {TryFocus();}
        void VisibilityChanged(object sender,EventArgs e) {if(!form.Visible) Dispose();}
        void FormDisposed(object sender,EventArgs e) {Dispose();}
        void TryFocus()
        {
            if(disposed || form.IsDisposed || !form.Visible || !form.Enabled) {Dispose();return;}
            // A chooser stays above regular and topmost windows only while it is visible.
            Native.SetWindowPos(form.Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0040);
            Activate(form.Handle);attempts++;
            if(Native.GetForegroundWindow()==form.Handle) {if(input!=null && input.CanFocus) {input.Focus();Native.SetFocus(input.Handle);}stableTicks++;if(stableTicks>=3) {Program.Log("Chooser foreground acquired=True");Dispose();}}
            else stableTicks=0;
            if(!disposed && attempts>=6) {Program.Log("Chooser foreground stable="+(stableTicks>0));Dispose();}
        }
        public void Dispose() {if(disposed) return;disposed=true;timer.Stop();timer.Dispose();form.VisibleChanged-=VisibilityChanged;form.Disposed-=FormDisposed;}
    }
    internal static Request Present(Form form,Control input) {form.TopMost=true;form.Show();form.BringToFront();return new Request(form,input);}
}
