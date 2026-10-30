using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Program
{
    internal const string Name = "选屏显示桌面";
    internal const string RunName = "ChoiceMinimizeWindow";
    internal static readonly string DataDir = IsTest ? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","test-results","profile")) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RunName);
    static bool IsTest {get {string[] args=Environment.GetCommandLineArgs();return Array.IndexOf(args,"--self-test")>=0 || Array.IndexOf(args,"--logic-test")>=0 || Array.IndexOf(args,"--layout-test")>=0;}}
    internal static EventWaitHandle Signal;
    internal static EventWaitHandle QuitSignal;
    internal static void Log(string text) { try { Directory.CreateDirectory(DataDir); string path=Path.Combine(DataDir,"app.log"); if(File.Exists(path) && new FileInfo(path).Length>262144) File.WriteAllText(path,""); File.AppendAllText(path,DateTime.Now.ToString("s")+" "+text+Environment.NewLine); } catch {} }
    internal static bool Startup { get { using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return k!=null && (string)k.GetValue(RunName)=="\""+Application.ExecutablePath+"\" --tray"; } }
    internal static void SetStartup(bool value) { using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if(value) k.SetValue(RunName,"\""+Application.ExecutablePath+"\" --tray"); else k.DeleteValue(RunName,false); } }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(Array.IndexOf(args,"--self-test")>=0) { SelfTest.Run(); return; }
        if(Array.IndexOf(args,"--logic-test")>=0) { SelfTest.RunLogic(); return; }
        if(Array.IndexOf(args,"--layout-test")>=0) { SelfTest.Run(true); return; }
        if(args.Length==2 && args[0]=="--fixture") { SelfTest.Fixture(args[1]); return; }
        bool takeOver=Array.IndexOf(args,"--take-over")>=0;
        if(takeOver) {try {using(var quit=EventWaitHandle.OpenExisting(@"Local\ChoiceMinimizeWindow.Quit")) quit.Set();}catch(WaitHandleCannotBeOpenedException) {}}
        bool first; using(var mutex=new Mutex(true,@"Local\ChoiceMinimizeWindow.Instance",out first)) {
            bool owns=first;
            if(!owns && takeOver) {try {owns=mutex.WaitOne(15000);}catch(AbandonedMutexException) {owns=true;}}
            Signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\ChoiceMinimizeWindow.Show");
            QuitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\ChoiceMinimizeWindow.Quit");
            if(Array.IndexOf(args,"--quit")>=0) {QuitSignal.Set(); Signal.Dispose(); QuitSignal.Dispose(); return;}
            if(!owns) { Signal.Set(); Signal.Dispose(); QuitSignal.Dispose(); return; }
            try {
                if(Array.IndexOf(args,"--enable-startup")>=0) SetStartup(true);
                Application.ThreadException += delegate(object s,ThreadExceptionEventArgs e) { Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message,Name); };
                using(var app=new MainForm()) Application.Run(app);
            } catch(Exception e) { Log(e.ToString()); MessageBox.Show(e.Message,Name); }
            finally { Signal.Dispose(); QuitSignal.Dispose(); mutex.ReleaseMutex(); }
        }
    }
}

internal sealed class MainForm : Form
{
    readonly WindowManager manager=new WindowManager();
    readonly NotifyIcon tray=new NotifyIcon();
    readonly Panel map=new Panel();
    readonly Label hint=new Label();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    readonly ShortcutListener shortcuts=new ShortcutListener();
    readonly WindowSwitcher switcher=new WindowSwitcher();
    readonly DesktopTarget desktopTarget=new DesktopTarget();
    readonly CurrentScreen currentScreen=new CurrentScreen();
    readonly LaunchRouter launchRouter;
    readonly LayoutProtection layoutProtection;
    bool routeEnabled=true;
    Label title;
    string hotkey="Ctrl+Alt+D";
    string switchHotkey="Ctrl+Tab";
    int holdMilliseconds=2000;
    bool switchMode;
    bool paused, exiting, applicationEnabled=true, resourcesDisposed;
    IntPtr previousFocus;
    WindowFocus.Request focusRequest;
    internal MainForm(uint routingOnlyPid=0)
    {
        launchRouter=new LaunchRouter(routingOnlyPid);
        layoutProtection=new LayoutProtection();
        bool routingBlocked=false;
        layoutProtection.Changed+=delegate {bool blocked=layoutProtection.RoutingBlocked;if(blocked!=routingBlocked) {routingBlocked=blocked;ConfigureRouting();}};
        Text=Program.Name; Icon=MakeIcon(); Font=new Font("Microsoft YaHei UI",10);
        AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(720,390); MinimumSize=new Size(600,340);
        StartPosition=FormStartPosition.CenterScreen; FormBorderStyle=FormBorderStyle.Sizable; MaximizeBox=false;
        BackColor=Color.FromArgb(245,247,251); KeyPreview=true;
        title=new Label { Text="选择要显示桌面的屏幕", Font=new Font(Font.FontFamily,18,FontStyle.Bold), Dock=DockStyle.Top, Height=60, Padding=new Padding(20,14,0,0) };
        var subtitle=new Label {Text="按数字或点击选择屏幕 · 应用列表按 Enter 确认 · Esc 取消",Dock=DockStyle.Top,Height=36,Padding=new Padding(22,4,0,0)};
        map.Dock=DockStyle.Fill; map.Resize += delegate { RenderMap(); };
        var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=60,Padding=new Padding(16,10,0,0)};
        AddButton(footer,"设置",delegate { Settings(); });
        AddButton(footer,"还原全部",delegate { manager.RestoreEveryScreen(); RenderMap(); });
        AddButton(footer,"收起到托盘",delegate { Hide(); });
        hint.Dock=DockStyle.Bottom; hint.Height=34; hint.Padding=new Padding(20,4,0,0);
        Controls.Add(map); Controls.Add(subtitle); Controls.Add(title); Controls.Add(hint); Controls.Add(footer);
        try { string path=Path.Combine(Program.DataDir,"hotkey.txt"); if(File.Exists(path)) { string s=File.ReadAllText(path).Trim(); if(Array.IndexOf(KeysList,s)>=0) hotkey=s; } } catch(Exception e) {Program.Log(e.Message);}
        try { string path=Path.Combine(Program.DataDir,"enabled.txt"); bool value; if(File.Exists(path) && bool.TryParse(File.ReadAllText(path).Trim(),out value)) applicationEnabled=value; } catch(Exception e) {Program.Log(e.Message);}
        try {string path=Path.Combine(Program.DataDir,"interaction.txt");if(File.Exists(path)) {string[] lines=File.ReadAllLines(path);int ms;if(lines.Length>0 && int.TryParse(lines[0],out ms) && ms>=500 && ms<=5000) holdMilliseconds=ms;if(lines.Length>1 && Array.IndexOf(SwitchKeys,lines[1])>=0) switchHotkey=lines[1];}}catch(Exception e){Program.Log(e.Message);}
        try {string path=Path.Combine(Program.DataDir,"routing.txt");bool value;if(File.Exists(path) && bool.TryParse(File.ReadAllText(path).Trim(),out value)) routeEnabled=value;}catch(Exception e){Program.Log(e.Message);}
        shortcuts.ActionRequested+=delegate(int kind,IntPtr focus,int direction,bool held) {QueueAction(delegate {RunShortcut(kind,focus,direction,held);});};
        shortcuts.UserInteraction+=delegate {long observed=System.Diagnostics.Stopwatch.GetTimestamp();QueueAction(delegate {
            LayoutSnapshot.CancelRestore(observed);
            desktopTarget.Reset();IntPtr focus=Native.GetForegroundWindow();
            currentScreen.Keyboard(WindowManager.Eligible(focus,true));
        });};
        shortcuts.MouseInteraction+=delegate(Point point) {long observed=System.Diagnostics.Stopwatch.GetTimestamp();QueueAction(delegate {LayoutSnapshot.CancelRestore(observed);launchRouter.Cancel();SelectCurrentScreen(Screen.FromPoint(point));});};
        shortcuts.LaunchClick+=delegate(Point point,IntPtr focus) {QueueAction(delegate {launchRouter.Arm(Screen.FromPoint(point),focus);});};
        shortcuts.RoutingKey+=delegate(IntPtr focus,bool launch) {QueueAction(delegate {
            uint pid;Native.GetWindowThreadProcessId(focus,out pid);
            if(launch && pid!=(uint)System.Diagnostics.Process.GetCurrentProcess().Id) launchRouter.Arm(ResolveCurrentScreen(focus),focus);else launchRouter.Cancel();
        });};
        launchRouter.Routed+=delegate(Screen screen) {SelectCurrentScreen(screen);};
        tray.Icon=Icon; tray.Text=Program.Name+(applicationEnabled?"":"（已停用）"); tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate { OpenSelector(); };
        menu.Opening+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { BuildMenu(); e.Cancel=menu.Items.Count==0; }; tray.Visible=true;
        FormClosing+=delegate(object s,FormClosingEventArgs e) { if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } };
        Resize+=delegate {if(WindowState==FormWindowState.Minimized) Hide();};
        KeyDown+=delegate(object s,KeyEventArgs e) { if(e.KeyCode==Keys.Escape) {Hide(); if(previousFocus!=IntPtr.Zero) Native.SetForegroundWindow(previousFocus); e.Handled=true;} int n=(int)e.KeyCode-(int)Keys.D1; if(n<0 || n>8) n=(int)e.KeyCode-(int)Keys.NumPad1; Screen[] screens=WindowManager.Screens(); if(n>=0 && n<screens.Length) {e.Handled=true; Choose(screens[n]);} };
        timer.Interval=30; timer.Tick+=delegate {if(Program.QuitSignal.WaitOne(0)) {exiting=true;manager.RestoreAll();Close();return;} if(Program.Signal.WaitOne(0)) OpenSelector();}; timer.Start();
        Shown+=delegate {layoutProtection.SetActive(applicationEnabled);layoutProtection.Start(Handle);if(!ConfigureHotkey()) MessageBox.Show("快捷键监听启动失败，请重新运行程序。仍可使用托盘选屏。",Program.Name); RenderMap(); bool trayOk=VerifyTray(); if(trayOk && Array.IndexOf(Environment.GetCommandLineArgs(),"--tray")>=0) Hide();};
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
    }
    static readonly string[] KeysList={"Win+Alt+D","Ctrl+Alt+D","Win+Ctrl+D","Win+D"};
    static readonly string[] SwitchKeys={"Ctrl+Tab","Alt+Tab","Ctrl+Alt+Tab","停用"};
    void QueueAction(Action action) {if(IsHandleCreated && !IsDisposed && !resourcesDisposed) BeginInvoke((Action)delegate {if(!IsDisposed && !resourcesDisposed && !exiting) action();});}
    Screen ResolveCurrentScreen(IntPtr focus)
    {
        Screen[] screens=WindowManager.Screens();
        string device=currentScreen.Resolve(WindowManager.ScreenFor(focus).DeviceName,Array.ConvertAll(screens,s=>s.DeviceName));
        return Array.Find(screens,s=>s.DeviceName==device);
    }
    void RunShortcut(int kind,IntPtr focus,int direction,bool held)
    {
        if(!applicationEnabled || paused || exiting || ModalChildOpen()) return;
        launchRouter.Cancel();
        Screen[] screens=WindowManager.Screens();string[] devices=Array.ConvertAll(screens,s=>s.DeviceName);
        Screen focusedScreen=WindowManager.ScreenFor(focus);
        Screen screen=ResolveCurrentScreen(focus);
        if(held) {previousFocus=focus;OpenSelector(kind==2,true);return;}
        if(kind==1) {
            string target=desktopTarget.Resolve(screen.DeviceName,devices);
            Program.Log("Desktop shortcut focus="+focus+" focusScreen="+focusedScreen.DeviceName+" currentScreen="+screen.DeviceName+" target="+target);
            ChooseDesktop(Array.Find(screens,s=>s.DeviceName==target));
        } else {desktopTarget.Reset();Hide();switcher.Switch(screen,focus,direction);}
    }
    bool ModalChildOpen() {foreach(Form form in Application.OpenForms) if(form!=this && form.Modal) return true;return false;}
    static Icon MakeIcon()
    {
        using(var bitmap=new Bitmap(32,32)) {using(var g=Graphics.FromImage(bitmap)) {g.Clear(Color.Transparent); using(var brush=new SolidBrush(Color.FromArgb(38,99,218))) g.FillRectangle(brush,1,5,30,21); using(var pen=new Pen(Color.White,2)) {g.DrawRectangle(pen,4,8,8,14);g.DrawRectangle(pen,16,8,11,14);g.DrawLine(pen,16,26,16,29);g.DrawLine(pen,10,29,22,29);}} IntPtr h=bitmap.GetHicon();try {return (Icon)Icon.FromHandle(h).Clone();}finally{Native.DestroyIcon(h);}}
    }
    bool VerifyTray()
    {
        try {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var window=(NativeWindow)typeof(NotifyIcon).GetField("window",flags).GetValue(tray);
            int id=(int)typeof(NotifyIcon).GetField("id",flags).GetValue(tray);
            var identifier=new Native.NotifyIdentifier {Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.NotifyIdentifier)),Window=window.Handle,Id=(uint)id};
            Native.Rect rect; int result=Native.Shell_NotifyIconGetRect(ref identifier,out rect); Program.Log("Tray shell rectangle HRESULT="+result+" bounds="+rect.Left+","+rect.Top+","+rect.Right+","+rect.Bottom); return result==0;
        }catch(Exception e) {Program.Log("Tray verification: "+e.Message);return false;}
    }
    static void AddButton(Control parent,string text,Action action) {var b=new Button {Text=text,AutoSize=true,Height=32,Margin=new Padding(4)}; b.Click+=delegate {action();}; parent.Controls.Add(b);}
    void DisplayChanged(object sender,EventArgs e) {QueueAction(delegate {layoutProtection.DisplayChanged();if(Visible) RenderMap();});}
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x02b1 && layoutProtection!=null && m.LParam.ToInt64()==System.Diagnostics.Process.GetCurrentProcess().SessionId) layoutProtection.Session(m.WParam.ToInt32());
        base.WndProc(ref m);
    }
    void Status(string text) {hint.Text=text;}
    void BuildMenu()
    {
        menu.Items.Clear();
        var enabled=new ToolStripMenuItem("启用") {Checked=applicationEnabled}; enabled.Click+=delegate {SetApplicationEnabled(!applicationEnabled); enabled.Checked=applicationEnabled;}; menu.Items.Add(enabled);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("显示桌面：选择屏幕",null,delegate {OpenSelector();}).Enabled=applicationEnabled;
        var routing=new ToolStripMenuItem("新开／唤出窗口跟随当前屏幕") {Checked=routeEnabled,Enabled=applicationEnabled};routing.Click+=delegate {SetRouting(!routeEnabled);};menu.Items.Add(routing);
        var layout=new ToolStripMenuItem("远程桌面布局保护");
        var automatic=new ToolStripMenuItem("自动保存并恢复本地布局") {Checked=layoutProtection.Enabled};
        automatic.Click+=delegate {try {layoutProtection.SetEnabled(!layoutProtection.Enabled);}catch(Exception e) {MessageBox.Show(e.Message,Program.Name);}};
        layout.DropDownItems.Add(automatic);
        layout.DropDownItems.Add(new ToolStripMenuItem(layoutProtection.Status) {Enabled=false});
        layout.DropDownItems.Add(new ToolStripMenuItem(layoutProtection.ManualStatus) {Enabled=false});
        layout.DropDownItems.Add(new ToolStripSeparator());
        layout.DropDownItems.Add("保存当前布局",null,delegate {try {layoutProtection.SaveNow();}catch(Exception e) {MessageBox.Show(e.Message,Program.Name);}}).Enabled=layoutProtection.CanSave;
        layout.DropDownItems.Add("恢复手动保存的布局",null,delegate {try {layoutProtection.RestoreNow();}catch(Exception e) {MessageBox.Show(e.Message,Program.Name);}}).Enabled=layoutProtection.CanRestore;
        layout.DropDownItems.Add("恢复最近自动布局",null,delegate {try {layoutProtection.RestoreAutomatic();}catch(Exception e) {MessageBox.Show(e.Message,Program.Name);}}).Enabled=layoutProtection.CanRestoreAutomatic;
        menu.Items.Add(layout);
        menu.Items.Add("切换应用：选择屏幕",null,delegate {OpenSelector(true,false);}).Enabled=applicationEnabled;
        Screen[] screens=WindowManager.Screens();
        for(int i=0;i<screens.Length;i++) {Screen s=screens[i]; menu.Items.Add((i+1)+" 号屏："+manager.ActionText(s),null,delegate {Choose(s);}).Enabled=applicationEnabled;}
        menu.Items.Add("还原全部窗口",null,delegate {manager.RestoreEveryScreen(); RenderMap();});
        menu.Items.Add(new ToolStripSeparator());
        var p=new ToolStripMenuItem("暂停快捷键") {Checked=paused,Enabled=applicationEnabled}; p.Click+=delegate {paused=!paused; ConfigureHotkey();RenderMap();}; menu.Items.Add(p);
        var startup=new ToolStripMenuItem("随系统启动") {Checked=Program.Startup}; startup.Click+=delegate {try {Program.SetStartup(!Program.Startup);} catch(Exception e) {MessageBox.Show(e.Message,Program.Name);}}; menu.Items.Add(startup);
        menu.Items.Add("设置与使用说明",null,delegate {Settings();});
        using(var identity=System.Security.Principal.WindowsIdentity.GetCurrent()) {
            if(!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                menu.Items.Add("以管理员身份重启（支持管理员窗口）",null,delegate {
                    try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath,"--tray --take-over") {UseShellExecute=true,Verb="runas"});}
                    catch(System.ComponentModel.Win32Exception e) {if(e.NativeErrorCode!=1223) MessageBox.Show(e.Message,Program.Name);}
                });
        }
        menu.Items.Add("退出（先还原窗口）",null,delegate {exiting=true; manager.RestoreAll(); Close();});
    }
    void RenderMap()
    {
        if(map.Width<1 || map.Height<1) return;
        title.Text=switchMode?"选择要切换应用的屏幕":"选择要显示桌面的屏幕";
        while(map.Controls.Count>0) map.Controls[0].Dispose();
        Screen[] screens=WindowManager.Screens(); Rectangle total=screens[0].Bounds;
        foreach(Screen s in screens) total=Rectangle.Union(total,s.Bounds);
        double scale=Math.Min((map.Width-44.0)/total.Width,(map.Height-30.0)/total.Height);
        if(scale<=0) return;
        int ox=(map.Width-(int)(total.Width*scale))/2, oy=(map.Height-(int)(total.Height*scale))/2;
        for(int i=0;i<screens.Length;i++) {
            Screen screen=screens[i]; Rectangle r=screen.Bounds;
            var b=new Button {Bounds=new Rectangle(ox+(int)((r.X-total.X)*scale)+3,oy+(int)((r.Y-total.Y)*scale)+3,Math.Max(30,(int)(r.Width*scale)-6),Math.Max(30,(int)(r.Height*scale)-6)),
                Text=(i+1)+" 号屏"+(screen.Primary?" · 主屏":"")+"\n"+r.Width+" × "+r.Height+"\n"+(switchMode?"选择应用":manager.ActionText(screen)),
                BackColor=manager.ActionText(screen)=="还原窗口"?Color.FromArgb(219,238,224):Color.FromArgb(224,235,255),FlatStyle=FlatStyle.Flat,TabIndex=i,Enabled=applicationEnabled};
            b.Click+=delegate {Choose(screen);}; map.Controls.Add(b);
        }
        Status((!applicationEnabled?"应用已停用":paused?"快捷键已暂停":"桌面 "+hotkey+" · 应用 "+switchHotkey+" · 长按 "+(holdMilliseconds/1000.0).ToString("0.#")+" 秒选屏")+"   |   已最小化："+WindowManager.MinimizedCount());
    }
    internal void SetApplicationEnabled(bool value)
    {
        bool previous=applicationEnabled; applicationEnabled=value;
        layoutProtection.SetActive(value);
        if(!ConfigureHotkey()) {applicationEnabled=previous;layoutProtection.SetActive(previous); ConfigureHotkey(); MessageBox.Show("启用失败，无法启动快捷键监听，请重新运行程序。",Program.Name); return;}
        try {Directory.CreateDirectory(Program.DataDir); File.WriteAllText(Path.Combine(Program.DataDir,"enabled.txt"),value.ToString());}
        catch(Exception e) {applicationEnabled=previous;layoutProtection.SetActive(previous); ConfigureHotkey(); MessageBox.Show("无法保存启用状态："+e.Message,Program.Name); return;}
        if(!applicationEnabled) {manager.RestoreAll(); Hide();}
        tray.Text=Program.Name+(applicationEnabled?"":"（已停用）"); RenderMap(); Program.Log("Application enabled="+applicationEnabled);
    }
    void SelectCurrentScreen(Screen screen) {currentScreen.Select(screen.DeviceName);desktopTarget.Reset();}
    bool SetRouting(bool enabled)
    {
        bool previous=routeEnabled;routeEnabled=enabled;
        try {
            if(!ConfigureRouting()) throw new InvalidOperationException("窗口跟随监听启动失败。");
            Directory.CreateDirectory(Program.DataDir);File.WriteAllText(Path.Combine(Program.DataDir,"routing.txt"),routeEnabled.ToString());
            return true;
        }catch(Exception e) {routeEnabled=previous;ConfigureRouting();MessageBox.Show(e.Message,Program.Name);return false;}
    }
    bool ConfigureRouting() {return launchRouter.Configure(routeEnabled && applicationEnabled && !paused && !layoutProtection.RoutingBlocked);}
    void ChooseDesktop(Screen screen) {if(!applicationEnabled) return;currentScreen.Select(screen.DeviceName);desktopTarget.Remember(screen.DeviceName);Hide();Program.Log("Desktop screen="+screen.DeviceName+" "+manager.Toggle(screen));}
    void Choose(Screen screen) {if(!applicationEnabled) return;if(!switchMode) ChooseDesktop(screen);else {SelectCurrentScreen(screen);Hide();WindowSwitcher.ShowPicker(screen,previousFocus);}}
    void OpenSelector() {OpenSelector(false,false);}
    void OpenSelector(bool applications,bool preserveFocus) {if(!applicationEnabled || ModalChildOpen()) return;switchMode=applications;if(!preserveFocus) previousFocus=Native.GetForegroundWindow(); WindowState=FormWindowState.Normal; RenderMap();if(focusRequest!=null) focusRequest.Dispose(); focusRequest=WindowFocus.Present(this,map.Controls.Count>0?map.Controls[0]:null);}
    void Settings()
    {
        using(var f=new Form {Text="设置 · "+Program.Name,Font=Font,ClientSize=new Size(510,460),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterScreen,MaximizeBox=false,MinimizeBox=false}) {
            var l=new Label {Text="显示桌面快捷键",Location=new Point(22,22),AutoSize=true};
            var combo=new ComboBox {Location=new Point(170,18),Width=260,DropDownStyle=ComboBoxStyle.DropDownList}; combo.Items.AddRange(KeysList); combo.SelectedItem=hotkey;
            var switchLabel=new Label {Text="切换应用快捷键",Location=new Point(22,66),AutoSize=true};
            var switchCombo=new ComboBox {Location=new Point(170,62),Width=260,DropDownStyle=ComboBoxStyle.DropDownList};switchCombo.Items.AddRange(SwitchKeys);switchCombo.SelectedItem=switchHotkey;
            var holdLabel=new Label {Text="长按选屏时间（秒）",Location=new Point(22,110),AutoSize=true};
            var duration=new NumericUpDown {Location=new Point(210,106),Width=110,Minimum=0.5m,Maximum=5m,DecimalPlaces=1,Increment=0.1m,Value=holdMilliseconds/1000m};
            var check=new CheckBox {Text="随 Windows 登录启动",Location=new Point(24,154),AutoSize=true,Checked=Program.Startup};
            var routing=new CheckBox {Text="新开／唤出窗口跟随当前屏幕",Location=new Point(24,188),AutoSize=true,Checked=routeEnabled};
            var text=new Label {Text="短按：操作当前屏幕，连续短按保持同屏。\n点击／滚轮选屏（含桌面、任务栏），仅移动不换屏。\n持续按住整个组合：呼出对应功能的屏幕选择。\n数字 1–9 选屏，应用列表按 Enter 确认，Esc 取消。\nCtrl+Tab 会接管应用原有的标签页切换。\n切换应用时加 Shift 可反向切换。\n停用／暂停后恢复原有快捷键；不显示气泡。",Location=new Point(22,232),Size=new Size(470,145)};
            var save=new Button {Text="保存",Location=new Point(390,410),Width=90};
            save.Click+=delegate {try {string oldSwitch=switchHotkey;int oldDuration=holdMilliseconds;switchHotkey=(string)switchCombo.SelectedItem;holdMilliseconds=(int)(duration.Value*1000);string selected=(string)combo.SelectedItem; if(!ApplyHotkey(selected)) {switchHotkey=oldSwitch;holdMilliseconds=oldDuration;ConfigureHotkey();return;} if(!SetRouting(routing.Checked)) return;Program.SetStartup(check.Checked); Directory.CreateDirectory(Program.DataDir); File.WriteAllText(Path.Combine(Program.DataDir,"hotkey.txt"),hotkey);File.WriteAllLines(Path.Combine(Program.DataDir,"interaction.txt"),new[]{holdMilliseconds.ToString(),switchHotkey});RenderMap(); f.Close();}catch(Exception e){MessageBox.Show(e.Message,Program.Name);}};
            f.Controls.AddRange(new Control[]{l,combo,switchLabel,switchCombo,holdLabel,duration,check,routing,text,save}); f.ShowDialog(this);
        }
    }
    bool ApplyHotkey(string value)
    {
        string old=hotkey; hotkey=value;
        if(ConfigureHotkey()) {Status("快捷键："+hotkey); return true;}
        hotkey=old; ConfigureHotkey(); MessageBox.Show("快捷键监听启动失败，请重新运行程序。",Program.Name); return false;
    }
    bool ConfigureHotkey()
    {
        desktopTarget.Reset();currentScreen.Reset();
        bool ok=shortcuts.Configure(hotkey,switchHotkey,holdMilliseconds,applicationEnabled && !paused);
        if(!ok) launchRouter.Configure(false);else if(!ConfigureRouting()) Program.Log("Launch route listener failed");
        return ok;
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing && !resourcesDisposed) {resourcesDisposed=true;layoutProtection.Dispose(); shortcuts.Dispose();launchRouter.Dispose(); if(focusRequest!=null) focusRequest.Dispose(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged; manager.RestoreAll(); timer.Dispose(); if(IsHandleCreated) Native.UnregisterHotKey(Handle,1); tray.Visible=false;tray.Dispose();menu.Dispose();}
        base.Dispose(disposing);
    }
}
