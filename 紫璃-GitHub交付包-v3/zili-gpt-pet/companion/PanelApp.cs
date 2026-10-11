using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ZiliCompanion
{
    public sealed class PanelOptions
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public bool HasPosition { get; set; }
        public bool Collapsed { get; set; }
        public bool Pinned { get; set; }
        public int LayoutVersion { get; set; }
        public PanelOptions() { Pinned = true; Collapsed = true; }
    }
    public static class PanelProgram
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            bool smoke = args.Contains("--smoke-test");
            bool created;
            string mutexKey;
            using (var sha = SHA256.Create()) mutexKey = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "");
            using (Mutex mutex = new Mutex(true, "Local\\ZiliCompanion_" + mutexKey, out created))
            {
                if (!created && !smoke)
                {
                    MessageBox.Show("紫璃面板已经在运行啦。可以在任务栏找到她。", "紫璃");
                    return 0;
                }
                try
                {
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    PanelController controller = new PanelController(root, smoke);
                    app.MainWindow = controller.Window;
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        File.WriteAllText(Path.Combine(root, "startup-error.txt"), e.Exception.GetType().Name + ": " + e.Exception.Message);
                        MessageBox.Show("面板遇到一个问题，详细说明已保存到 startup-error.txt。", "紫璃");
                        e.Handled = true;
                        controller.Window.Close();
                    };
                    app.Run(controller.Window);
                    return controller.SmokeFailed ? 1 : 0;
                }
                catch (Exception e)
                {
                    File.WriteAllText(Path.Combine(root, "startup-error.txt"), e.ToString());
                    if (!smoke) MessageBox.Show("面板未能打开，详细说明已保存到 startup-error.txt。", "紫璃");
                    return 1;
                }
                finally { if (created) mutex.ReleaseMutex(); }
            }
        }
    }

    public sealed class PanelController
    {
        public Window Window { get; private set; }
        public bool SmokeFailed { get; private set; }
        readonly string root;
        readonly bool smoke;
        readonly TimerEngine timer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly DispatcherTimer heartbeat = new DispatcherTimer();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly List<BitmapSource> petFrames = new List<BitmapSource>();
        PanelOptions options;
        bool closed;
        bool locked;
        bool sleeping;
        bool positionDirty;
        bool drawingSettings;
        double lastSaved;
        int animationStep;
        Window notice;

        public PanelController(string root, bool smoke)
        {
            this.root = root;
            this.smoke = smoke;
            string data = Path.Combine(root, smoke ? "smoke-data" : "data");
            Directory.CreateDirectory(data);
            options = LoadOptions(data);
            if (options.LayoutVersion < 3) { options.Collapsed = true; options.LayoutVersion = 3; }
            timer = new TimerEngine(data);
            using (var input = File.OpenRead(Path.Combine(root, "Panel.xaml")))
                Window = (Window)XamlReader.Load(input);
            Window.Topmost = options.Pinned;
            System.Windows.Automation.AutomationProperties.SetName(Window, "紫璃陪伴面板");
            BuildPetFrames();
            WireControls();
            Window.Loaded += delegate
            {
                drawingSettings = true;
                Get<CheckBox>("RestEnabled").IsChecked = timer.BreakEnabled;
                Get<CheckBox>("Pinned").IsChecked = options.Pinned;
                Get<TextBox>("IntervalInput").Text = timer.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
                drawingSettings = false;
                SetCollapsed(options.Collapsed);
                var area = SystemParameters.WorkArea;
                Window.Left = options.HasPosition ? options.Left : area.Right - Window.Width - 20;
                Window.Top = options.HasPosition ? options.Top : area.Top + 70;
                ClampPosition();
                RenderTimer();
                heartbeat.Interval = TimeSpan.FromMilliseconds(250);
                heartbeat.Tick += OnTick;
                heartbeat.Start();
                if (smoke) Window.Dispatcher.BeginInvoke(new Action(RunSmoke), DispatcherPriority.ApplicationIdle);
            };
            Window.LocationChanged += delegate { positionDirty = true; };
            Window.SizeChanged += delegate { if (Window.IsLoaded) ClampPosition(); };
            Window.Closed += delegate
            {
                closed = true;
                heartbeat.Stop();
                if (notice != null) notice.Close();
                TickTimer();
                timer.Save();
                SaveOptions();
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            };
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        T Get<T>(string name) where T : class { return Window.FindName(name) as T; }
        static Brush Color(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
        void BuildPetFrames()
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(Path.Combine(root, "assets", "zili.png"), UriKind.Absolute);
            source.EndInit();
            source.Freeze();
            for (int i = 0; i < 6; i++)
            {
                var frame = new CroppedBitmap(source, new Int32Rect(i * 192, 0, 192, 208));
                frame.Freeze();
                petFrames.Add(frame);
            }
            Get<Image>("PetImage").Source = petFrames[0];
        }

        void WireControls()
        {
            MouseButtonEventHandler drag = delegate(object sender, MouseButtonEventArgs e)
            {
                DependencyObject item = e.OriginalSource as DependencyObject;
                while (item != null)
                {
                    if (item is Button) return;
                    item = VisualTreeHelper.GetParent(item);
                }
                try { Window.DragMove(); } catch (InvalidOperationException) { }
                ClampPosition();
                SaveOptions();
            };
            Get<Grid>("DragHeader").MouseLeftButtonDown += drag;
            Get<Grid>("MiniDragHeader").MouseLeftButtonDown += drag;
            Get<Button>("CollapseButton").Click += delegate { SetCollapsed(!options.Collapsed); SaveOptions(); };
            Get<Button>("MiniDetails").Click += delegate { SetCollapsed(false); SaveOptions(); };
            Get<Button>("MiniClose").Click += delegate { Window.Close(); };
            Get<Button>("CloseButton").Click += delegate { Window.Close(); };
            Get<Button>("PauseButton").Click += delegate { TogglePause(); };
            Get<Button>("CompactPause").Click += delegate { TogglePause(); };
            Get<Button>("ApplySettings").Click += delegate { ApplySettings(); };
            Get<CheckBox>("RestEnabled").Checked += delegate { if (!drawingSettings) ChangeRest(true); };
            Get<CheckBox>("RestEnabled").Unchecked += delegate { if (!drawingSettings) ChangeRest(false); };
            Get<CheckBox>("Pinned").Checked += delegate { if (!drawingSettings) ChangePinned(true); };
            Get<CheckBox>("Pinned").Unchecked += delegate { if (!drawingSettings) ChangePinned(false); };
            Window.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { SetCollapsed(true); SaveOptions(); e.Handled = true; }
            };
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (closed) return;
            Window.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff)
                    locked = true;
                else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
                    locked = false;
                TickTimer();
            }));
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (closed) return;
            Window.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (e.Mode == PowerModes.Suspend) sleeping = true;
                else if (e.Mode == PowerModes.Resume) sleeping = false;
                TickTimer();
            }));
        }

        void TickTimer() { timer.Tick(DateTimeOffset.Now, clock.Elapsed.TotalSeconds, locked || sleeping); }
        void OnTick(object sender, EventArgs e)
        {
            TickTimer();
            RenderTimer();
            Get<Image>("PetImage").Source = petFrames[(animationStep++ / 2) % petFrames.Count];
            if (!smoke && notice == null && !locked && !sleeping)
            {
                if (timer.BreakDue && timer.BreakEnabled && !timer.IsPaused) ShowBreakNotice();
            }
            if (clock.Elapsed.TotalSeconds - lastSaved >= 20)
            {
                timer.Save();
                if (positionDirty) SaveOptions();
                lastSaved = clock.Elapsed.TotalSeconds;
            }
        }

        void TogglePause()
        {
            TickTimer();
            if (timer.IsPaused) timer.Resume(); else timer.Pause();
            timer.Save();
            RenderTimer();
        }
        void ChangeRest(bool enabled)
        {
            TickTimer();
            timer.BreakEnabled = enabled;
            if (!enabled && notice != null) notice.Close();
            timer.Save();
            RenderTimer();
        }
        void ChangePinned(bool pinned) { options.Pinned = pinned; Window.Topmost = pinned; SaveOptions(); }

        void ApplySettings()
        {
            int minutes;
            if (!Int32.TryParse(Get<TextBox>("IntervalInput").Text, out minutes) || minutes < 1 || minutes > 240)
            {
                Get<TextBlock>("SettingsMessage").Text = "休息间隔请填 1–240 分钟。";
                return;
            }
            TickTimer();
            timer.IntervalMinutes = minutes;
            bool timerSaved = timer.Save();
            bool optionsSaved = SaveOptions();
            Get<TextBlock>("SettingsMessage").Text = timerSaved && optionsSaved ? "设置已保存啦。" : "设置暂未保存，请确认文件夹可以写入。";
            RenderTimer();
        }

        static string Duration(double seconds)
        {
            long total = (long)Math.Max(0, seconds);
            return String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", total / 3600, total / 60 % 60, total % 60);
        }
        void RenderTimer()
        {
            Get<TextBlock>("SessionTime").Text = Duration(timer.SessionSeconds);
            Get<TextBlock>("TodayTime").Text = "今日累计  " + Duration(timer.TodaySeconds);
            Get<TextBlock>("TimerStatus").Text = timer.IsPaused ? "已暂停" : (locked || sleeping ? "暂不计时" : "陪伴中");
            Get<Button>("PauseButton").Content = timer.IsPaused ? "继续" : "暂停";
            Get<Button>("CompactPause").Content = timer.IsPaused ? "▶" : "Ⅱ";
            Get<Button>("CompactPause").ToolTip = timer.IsPaused ? "继续计时" : "暂停计时";
            System.Windows.Automation.AutomationProperties.SetName(Get<Button>("CompactPause"), timer.IsPaused ? "继续计时" : "暂停计时");
            Get<TextBlock>("RestStatus").Text = !timer.BreakEnabled ? "提醒已关闭，可以随时重新开启。" :
                timer.BreakDue ? "眼睛休息一下吧～" : "距离下次提醒 " + Duration(timer.SecondsUntilBreak);
            Get<ProgressBar>("RestProgress").Value = timer.BreakEnabled ? Math.Max(0, Math.Min(100, (1 - timer.SecondsUntilBreak / (timer.IntervalMinutes * 60)) * 100)) : 0;
            long total = (long)Math.Max(0, timer.SessionSeconds);
            Get<TextBlock>("CompactInfo").Text = String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", total / 3600, total / 60 % 60);
            Get<TextBlock>("CompactInfo").ToolTip = "本次 " + Duration(timer.SessionSeconds) + "\n今日 " + Duration(timer.TodaySeconds) +
                "\n" + (timer.IsPaused ? "计时已暂停" : "面板运行时累计") + (timer.BreakEnabled ? "\n距离休息提醒 " + Duration(timer.SecondsUntilBreak) : "\n休息提醒已关闭");
            if (!String.IsNullOrEmpty(timer.LastPersistenceError))
                Get<TextBlock>("TimerStatus").Text = "记录暂未保存";
        }

        void ShowBreakNotice() { ShowNotice("眼睛休息一下吧～", "已经陪伴了 " + Duration(timer.ContinuousSeconds) + "。\n望望远处，活动一下肩膀吧。"); }
        void ShowNotice(string title, string message)
        {
            if (notice != null) return;
            Window popup = new Window { Title = "紫璃 · " + title, Width = 340, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false, Owner = Window,
                FontFamily = Window.FontFamily, FontSize = 12, Foreground = Color("#59416F") };
            Border shell = new Border { Background = Color("#FBF7FF"), CornerRadius = new CornerRadius(18),
                BorderBrush = Color("#DCCBEF"), BorderThickness = new Thickness(1), Padding = new Thickness(18), Margin = new Thickness(6) };
            StackPanel body = new StackPanel();
            body.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 13), Foreground = Color("#9176A8"), LineHeight = 22 });
            WrapPanel buttons = new WrapPanel();
            bool handled = false;
            Action finish = delegate { handled = true; popup.Close(); RenderTimer(); };
            buttons.Children.Add(NoticeButton("稍后 5 分钟", delegate { timer.Snooze(5); finish(); }));
            buttons.Children.Add(NoticeButton("我休息啦", delegate { TickTimer(); timer.PauseForBreak(); finish(); }));
            buttons.Children.Add(NoticeButton("关闭提醒", delegate
            {
                timer.DisableBreakReminders();
                drawingSettings = true; Get<CheckBox>("RestEnabled").IsChecked = false; drawingSettings = false;
                finish();
            }));
            body.Children.Add(buttons);
            shell.Child = body; popup.Content = shell; notice = popup;
            var area = ScreenArea();
            popup.Left = Math.Max(area.Left, Math.Min(area.Right - popup.Width, Window.Left - popup.Width - 6));
            popup.Top = Math.Max(area.Top, Math.Min(area.Bottom - 220, Window.Top + 50));
            popup.Closed += delegate
            {
                if (!handled && !closed && timer.BreakEnabled) timer.Snooze(5);
                timer.Save(); notice = null;
            };
            popup.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) popup.Close(); };
            popup.Show();
        }
        Button NoticeButton(string text, Action action)
        {
            Button button = new Button { Content = text, Style = Window.Resources[typeof(Button)] as Style,
                FontSize = 11, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 6, 5) };
            button.Click += delegate { action(); };
            return button;
        }

        void SetCollapsed(bool collapsed)
        {
            options.Collapsed = collapsed;
            Get<Grid>("DragHeader").Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            Get<ScrollViewer>("FullBody").Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            Get<DockPanel>("CompactBody").Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            Window.Width = collapsed ? 132 : 300;
            Get<Border>("Shell").Margin = new Thickness(collapsed ? 5 : 8);
            Get<Border>("Shell").CornerRadius = new CornerRadius(collapsed ? 14 : 22);
            Get<Button>("CollapseButton").Content = collapsed ? "+" : "−";
            Get<Button>("CollapseButton").ToolTip = collapsed ? "展开面板" : "收起面板";
            System.Windows.Automation.AutomationProperties.SetName(Get<Button>("CollapseButton"), collapsed ? "展开面板" : "收起面板");
            Get<TextBlock>("HeaderSubtitle").Text = collapsed ? "点 + 展开" : "陪你工作，也陪你休息";
            Window.UpdateLayout();
            ClampPosition();
        }
        void ClampPosition()
        {
            var area = ScreenArea();
            Get<ScrollViewer>("FullBody").MaxHeight = Math.Max(150, Math.Min(720, area.Height - 110));
            double height = Double.IsNaN(Window.ActualHeight) ? 650 : Window.ActualHeight;
            Window.Left = Math.Max(area.Left, Math.Min(area.Right - Window.Width, Window.Left));
            Window.Top = Math.Max(area.Top, Math.Min(area.Bottom - Math.Min(height, area.Height), Window.Top));
        }
        Rect ScreenArea()
        {
            var source = PresentationSource.FromVisual(Window);
            double scaleX = 1, scaleY = 1;
            if (source != null && source.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }
            var bounds = new System.Drawing.Rectangle((int)(Window.Left * scaleX), (int)(Window.Top * scaleY),
                (int)(Window.Width * scaleX), (int)(Math.Max(Window.ActualHeight, 80) * scaleY));
            var candidates = System.Windows.Forms.Screen.AllScreens.Where(s => s.WorkingArea.IntersectsWith(bounds)).ToArray();
            var screen = candidates.Length > 0 ? System.Windows.Forms.Screen.FromRectangle(bounds) : System.Windows.Forms.Screen.PrimaryScreen;
            var work = screen.WorkingArea;
            return new Rect(work.Left / scaleX, work.Top / scaleY, work.Width / scaleX, work.Height / scaleY);
        }

        string OptionsPath { get { return Path.Combine(root, smoke ? "smoke-data" : "data", "panel-settings.json"); } }
        PanelOptions LoadOptions(string data)
        {
            try
            {
                string path = Path.Combine(data, "panel-settings.json");
                if (File.Exists(path))
                {
                    var result = json.Deserialize<PanelOptions>(File.ReadAllText(path));
                    if (result != null && !Double.IsNaN(result.Left) && !Double.IsInfinity(result.Left) &&
                        !Double.IsNaN(result.Top) && !Double.IsInfinity(result.Top)) return result;
                }
            }
            catch (Exception) { }
            return new PanelOptions();
        }
        bool SaveOptions()
        {
            options.Left = Window.Left; options.Top = Window.Top; options.HasPosition = true;
            try
            {
                string temp = OptionsPath + ".tmp";
                File.WriteAllText(temp, json.Serialize(options));
                if (File.Exists(OptionsPath)) File.Replace(temp, OptionsPath, null);
                else File.Move(temp, OptionsPath);
                positionDirty = false;
                return true;
            }
            catch (Exception) { Get<TextBlock>("SettingsMessage").Text = "设置暂未保存，请确认文件夹可以写入。"; return false; }
        }

        void Capture(Window target, string name)
        {
            target.UpdateLayout();
            int width = (int)Math.Ceiling(target.ActualWidth * 1.5), height = (int)Math.Ceiling(target.ActualHeight * 1.5);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(target);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(root, name))) encoder.Save(output);
        }
        void RunSmoke()
        {
            heartbeat.Stop();
            var checks = new List<string>();
            try
            {
                SetCollapsed(false);
                Capture(Window, "preview-panel.png");
                TogglePause();
                if (!timer.IsPaused || Get<Button>("PauseButton").Content.ToString() != "继续") throw new Exception("Pause UI failed");
                TogglePause();
                if (timer.IsPaused) throw new Exception("Resume UI failed");
                checks.Add("Pause/resume controls passed");
                SetCollapsed(true);
                if (Get<ScrollViewer>("FullBody").Visibility != Visibility.Collapsed) throw new Exception("Collapse failed");
                Capture(Window, "preview-compact.png");
                SetCollapsed(false); checks.Add("Collapse/expand controls passed");
                ShowBreakNotice(); Capture(notice, "preview-rest.png"); notice.Close();
                checks.Add("Rest notice rendered");
                Get<TextBox>("IntervalInput").Text = "bad"; ApplySettings();
                if (!Get<TextBlock>("SettingsMessage").Text.Contains("1–240")) throw new Exception("Validation failed");
                Get<TextBox>("IntervalInput").Text = "45"; ApplySettings();
                checks.Add("Settings validation passed");
                ChangeRest(false); if (timer.BreakEnabled) throw new Exception("Disable reminders failed");
                ChangeRest(true); checks.Add("Reminder off/on controls passed");
                Window.Left = 100000; Window.Top = -100000; ClampPosition();
                if (Window.Left >= SystemParameters.WorkArea.Right || Window.Top < SystemParameters.WorkArea.Top) throw new Exception("Screen clamp failed");
                checks.Add("Screen position clamp passed");
                File.WriteAllLines(Path.Combine(root, "ui-smoke-test.txt"), checks);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(root, "ui-smoke-test.txt"), "FAILED: " + e.ToString());
                SmokeFailed = true;
            }
            Window.Close();
        }
    }
}
