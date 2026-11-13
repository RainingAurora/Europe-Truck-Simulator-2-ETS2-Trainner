using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Ets2Trainer
{
    internal sealed class ViewState
    {
        internal bool Connected, FuelRequested, FatigueRequested, FuelActive, FatigueActive;
        internal string Connection, FuelValue, FatigueValue, FuelIssue, FatigueIssue;
        internal StabilityMode StabilityMode;
        internal bool StabilityActive;
        internal string StabilityIssue;
        internal long Rightings;
    }

    internal sealed class Controller : IDisposable
    {
        private readonly object sync = new object();
        private readonly System.Threading.Timer timer;
        private ProcessMemory memory;
        private TrainerEngine engine;
        private DateTime retryAt;
        private DateTime nextPhysicsLog;
        private bool disposed;
        private string status = "等待游戏启动";
        private string loggedStatus;
        internal static readonly string LogFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETS2Trainer", "logs");
        internal Controller() { timer = new System.Threading.Timer(Poll, null, 0, 100); }
        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                File.AppendAllText(Path.Combine(LogFolder, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private void Poll(object unused)
        {
            if (!Monitor.TryEnter(sync)) return;
            try
            {
                if (disposed) return;
                if (memory != null && !memory.IsAlive) { engine.Dispose(); engine = null; memory = null; status = "游戏已退出，功能已关闭"; retryAt = DateTime.UtcNow.AddSeconds(1); }
                if (engine == null && DateTime.UtcNow >= retryAt)
                {
                    retryAt = DateTime.UtcNow.AddSeconds(3);
                    try { memory = ProcessMemory.Attach(); engine = new TrainerEngine(memory); Log("Connected to supported build " + Build162.Version + ", PID " + memory.ProcessId); }
                    catch (Exception ex) { status = ex.Message; }
                }
                if (engine != null)
                {
                    engine.Tick();
                    status = String.IsNullOrEmpty(engine.Error) ? engine.Snapshot.State : engine.Error;
                    if(engine.StabilityRequested!=StabilityMode.Off && DateTime.UtcNow>=nextPhysicsLog)
                    {
                        nextPhysicsLog=DateTime.UtcNow.AddSeconds(5);
                        Log("Stability "+engine.StabilityRequested+" truckRoll="+engine.Stability.TruckRoll+
                            " trailerRoll="+engine.Stability.TrailerRoll+" corrections="+engine.StabilityCorrections+
                            " rightings="+engine.RightingCount+" active="+engine.StabilityActive+" issue="+engine.Stability.Issue);
                    }
                }
                if (status != loggedStatus) { Log(status); loggedStatus = status; }
            }
            catch (Exception ex)
            {
                if (engine != null) engine.DisableAll();
                status = "修改已停止：" + ex.Message;
                Log(status);
            }
            finally { Monitor.Exit(sync); }
        }
        internal void ToggleFuel() { lock (sync) { if (engine != null && !disposed) { engine.SetFuel(!engine.FuelRequested); Log("Unlimited fuel requested=" + engine.FuelRequested); } } }
        internal void ToggleFatigue() { lock (sync) { if (engine != null && !disposed) { engine.SetFatigue(!engine.FatigueRequested); Log("No fatigue requested=" + engine.FatigueRequested); } } }
        internal void ToggleStability(StabilityMode mode)
        {
            lock(sync) { if(engine!=null&&!disposed) { engine.SetStability(engine.StabilityRequested^mode); Log("Stability mode="+engine.StabilityRequested); } }
        }
        internal void DisableAll() { lock (sync) { if (engine != null) engine.DisableAll(); Log("All features disabled"); } }
        internal ViewState View()
        {
            lock (sync)
            {
                var v = new ViewState { Connected = engine != null, Connection = status, FuelValue = "—", FatigueValue = "—", FuelIssue = "", FatigueIssue = "", StabilityIssue="" };
                if (engine != null)
                {
                    v.FuelRequested = engine.FuelRequested; v.FatigueRequested = engine.FatigueRequested;
                    v.FuelActive = engine.FuelActive; v.FatigueActive = engine.FatigueActive;
                    var s = engine.Snapshot;
                    if (s.Fuel.HasValue) v.FuelValue = (s.Fuel.Value * 100).ToString("F1") + "%";
                    if (s.Fatigue.HasValue) v.FatigueValue = s.Fatigue.Value + " 分钟";
                    v.FuelIssue = s.FuelIssue; v.FatigueIssue = s.FatigueIssue;
                    v.StabilityMode=engine.StabilityRequested;v.StabilityActive=engine.StabilityActive;
                    v.StabilityIssue=engine.Stability.Issue;
                    v.Rightings=engine.RightingCount;
                    if(engine.Stability.TruckRoll.HasValue)
                        v.Connection+=Environment.NewLine+"侧倾：车头 "+engine.Stability.TruckRoll.Value.ToString("F1")+"°"+
                            (engine.Stability.TrailerRoll.HasValue?"  /  挂车 "+engine.Stability.TrailerRoll.Value.ToString("F1")+"°":"");
                }
                return v;
            }
        }
        public void Dispose()
        {
            timer.Dispose();
            lock (sync) { disposed = true; if (engine != null) engine.Dispose(); engine = null; memory = null; }
        }
    }

    internal sealed class MainWindow : Form
    {
        private readonly Controller controller = new Controller();
        private readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer();
        private Label connection, fuelValue, fatigueValue, fuelState, fatigueState, hotkeys;
        private Button fuelButton, fatigueButton;
        private Label naturalValue,strongValue,naturalState,strongState;
        private Button naturalButton,strongButton;
        private readonly Color muted = Color.FromArgb(151, 165, 184), green = Color.FromArgb(84, 217, 165);
        private bool keysRegistered;
        private string hotkeyStatus="正在注册快捷键…";
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

        internal MainWindow()
        {
            SuspendLayout();
            Text = "ETS2 驾驶辅助修改器";
            Font = new Font("Microsoft YaHei UI", 10f);
            BackColor = Color.FromArgb(19, 26, 36); ForeColor = Color.White;
            ClientSize = new Size(580, 818); FormBorderStyle = FormBorderStyle.FixedSingle;
            AutoScroll=true;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AddLabel(this, "ETS2 驾驶辅助修改器", 24, 19, 530, 34, 20, Color.White, FontStyle.Bold);
            AddLabel(this, "适配 " + Build162.Version + " x64  ·  单机使用", 26, 60, 525, 24, 10, muted, FontStyle.Regular);
            connection = AddLabel(this, "正在查找游戏…", 26, 95, 528, 50, 10, green, FontStyle.Regular);
            MakeCard(153, "无限油量", "启用后补满油量，持续保持满油", "Ctrl + Alt + 1", out fuelValue, out fuelState, out fuelButton);
            MakeCard(286, "不会疲劳", "将驾驶疲劳计时持续维持在零", "Ctrl + Alt + 2", out fatigueValue, out fatigueState, out fatigueButton);
            MakeCard(419, "自然稳定辅助", "抑制过大侧倾，可与强力扶正同时开启", "Ctrl + Alt + 3", out naturalValue,out naturalState,out naturalButton);
            MakeCard(552, "强力主动扶正", "侧倾超过 35° 时扶正并减速，可同时开稳定辅助", "Ctrl + Alt + 4", out strongValue,out strongState,out strongButton);
            fuelButton.Click += delegate { controller.ToggleFuel(); Render(); };
            fatigueButton.Click += delegate { controller.ToggleFatigue(); Render(); };
            naturalButton.Click+=delegate {controller.ToggleStability(StabilityMode.Natural);Render();};
            strongButton.Click+=delegate {controller.ToggleStability(StabilityMode.Strong);Render();};
            var allOff = MakeButton(this, "全部关闭  ·  Ctrl + Alt + 0", 26, 692, 352, 42);
            allOff.Click += delegate { controller.DisableAll(); Render(); };
            var log = MakeButton(this, "打开日志", 390, 692, 164, 42);
            log.Click += delegate {
                try { Directory.CreateDirectory(Controller.LogFolder); System.Diagnostics.Process.Start("explorer.exe", "\"" + Controller.LogFolder + "\""); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开日志"); }
            };
            hotkeys = AddLabel(this, "正在注册快捷键…", 26, 746, 528, 26, 9, muted, FontStyle.Regular);
            AddLabel(this, "四项功能独立；强力扶正介入时会短暂抬升车辆并清除速度。", 26, 775, 528, 32, 9, muted, FontStyle.Regular);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
            uiTimer.Interval = 200; uiTimer.Tick += delegate { Render(); }; uiTimer.Start();
        }
        private Label AddLabel(Control parent, string text, int x, int y, int width, int height, float size, Color color, FontStyle style)
        {
            var l = new Label { Text = text, Bounds = new Rectangle(x, y, width, height), ForeColor = color, Font = new Font("Microsoft YaHei UI", size, style), AutoEllipsis = true };
            parent.Controls.Add(l); return l;
        }
        private Button MakeButton(Control parent, string text, int x, int y, int width, int height)
        {
            var b = new Button { Text = text, Bounds = new Rectangle(x, y, width, height), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(41, 55, 74), ForeColor = Color.White, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0; parent.Controls.Add(b); return b;
        }
        private void MakeCard(int y, string title, string description, string key, out Label value, out Label state, out Button button)
        {
            var panel = new Panel { Bounds = new Rectangle(26, y, 528, 120), BackColor = Color.FromArgb(28, 38, 51) }; Controls.Add(panel);
            AddLabel(panel, title, 16, 12, 250, 28, 14, Color.White, FontStyle.Bold);
            value = AddLabel(panel, "—", 314, 14, 194, 27, 13, green, FontStyle.Bold); value.TextAlign = ContentAlignment.TopRight;
            AddLabel(panel, description, 17, 46, 345, 24, 9, muted, FontStyle.Regular);
            state = AddLabel(panel, "已关闭", 17, 80, 330, 26, 9, muted, FontStyle.Regular);
            button = MakeButton(panel, "开启", 382, 72, 128, 35); button.AccessibleDescription = key;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var failures=new System.Collections.Generic.List<string>();
            int[] ids={1,2,3,4,5};uint[] keys={0x31,0x32,0x30,0x33,0x34};
            for(int i=0;i<ids.Length;i++)if(!RegisterHotKey(Handle,ids[i],0x4003,keys[i]))
            {
                int error=Marshal.GetLastWin32Error();
                failures.Add("Ctrl+Alt+"+(char)keys[i]+"："+error+" "+new System.ComponentModel.Win32Exception(error).Message);
            }
            keysRegistered=failures.Count==0;
            hotkeyStatus=keysRegistered?"快捷键已就绪；可在游戏中直接切换。":"快捷键注册失败（窗口按钮可用）："+String.Join("；",failures);
            if(hotkeys!=null)hotkeys.Text=hotkeyStatus;
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            hotkeys.Text=hotkeyStatus;
            new ToolTip().SetToolTip(hotkeys,hotkeyStatus);
            Rectangle area=Screen.FromControl(this).WorkingArea;
            if(Height>area.Height-24){Height=area.Height-24;Top=area.Top+12;}
            Render();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                int id = m.WParam.ToInt32();
                if (id == 1) controller.ToggleFuel(); else if (id == 2) controller.ToggleFatigue(); else if (id == 3) controller.DisableAll();
                else if(id==4)controller.ToggleStability(StabilityMode.Natural);else if(id==5)controller.ToggleStability(StabilityMode.Strong);
                Render();
            }
            base.WndProc(ref m);
        }
        private void Render()
        {
            var v = controller.View(); connection.Text = v.Connection;
            connection.ForeColor = v.Connected ? green : muted;
            fuelValue.Text = v.FuelValue; fatigueValue.Text = v.FatigueValue;
            ShowFeature(fuelButton, fuelState, v.Connected, v.FuelRequested, v.FuelActive, v.FuelIssue);
            ShowFeature(fatigueButton, fatigueState, v.Connected, v.FatigueRequested, v.FatigueActive, v.FatigueIssue);
            ShowFeature(naturalButton,naturalState,v.Connected,(v.StabilityMode&StabilityMode.Natural)!=0,v.StabilityActive,v.StabilityIssue);
            ShowFeature(strongButton,strongState,v.Connected,(v.StabilityMode&StabilityMode.Strong)!=0,v.StabilityActive,v.StabilityIssue);
            naturalValue.Text=(v.StabilityMode&StabilityMode.Natural)!=0?"稳定辅助":"—";
            strongValue.Text=(v.StabilityMode&StabilityMode.Strong)!=0?"主动扶正":"—";
            if(v.StabilityActive)
            {
                if((v.StabilityMode&StabilityMode.Natural)!=0)naturalState.Text="监测中 · 持续抑制过大侧倾";
                if((v.StabilityMode&StabilityMode.Strong)!=0)strongState.Text="监测中 · 已主动扶正 "+v.Rightings+" 次";
            }
        }
        private void ShowFeature(Button button, Label state, bool connected, bool requested, bool active, string issue)
        {
            button.Enabled = connected; button.Text = requested ? "关闭" : "开启";
            button.BackColor = requested ? Color.FromArgb(29, 111, 86) : Color.FromArgb(41, 55, 74);
            state.Text = !requested ? "已关闭" : active ? "正在生效" : String.IsNullOrEmpty(issue) ? "已开启 · 等待驾驶场景恢复" : "已挂起 · " + issue;
            state.ForeColor = active ? green : muted;
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            uiTimer.Stop(); uiTimer.Dispose();
            UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2); UnregisterHotKey(Handle, 3);
            UnregisterHotKey(Handle,4);UnregisterHotKey(Handle,5);
            controller.Dispose(); base.OnFormClosed(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\ETS2Trainer.162", out created))
            {
                if (!created) { MessageBox.Show("修改器已经运行，请使用已打开的窗口。", "ETS2 双功能修改器"); return; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainWindow());
            }
        }
    }
}
