using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ZiliCompanion;

internal static class PanelIndependentQa
{
    private static string fixtureRoot;
    private static string outputRoot;
    private static int passed;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Expected the built panel directory.");
        outputRoot = Path.GetFullPath(args[0]);
        string testsBase = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        fixtureRoot = Path.GetFullPath(Path.Combine(testsBase, "panel-qa-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            Check("compact timer can pause, resume and expand", TestCompactControls);
            Check("reminder toggles preserve a usable fresh interval", TestReminderControls);
            Check("invalid interval cannot change settings", TestInvalidInterval);
            Check("failed settings save cannot claim success", TestSaveFailure);
            Check("corrupt options restore safe defaults", TestCorruptOptions);
            Check("older settings and timer records remain readable", TestLegacyRecords);
            Console.WriteLine("PASS: " + passed + " independent offline panel checks; no main window shown.");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL: " + error.Message);
            return 1;
        }
        finally
        {
            string allowed = testsBase.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fixtureRoot.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(fixtureRoot).StartsWith("panel-qa-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup target.");
            if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, true);
        }
    }

    private static void Check(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception error) { throw new Exception(name + ": " + error.Message, error); }
    }

    private static string Prepare(string name)
    {
        string path = Path.Combine(fixtureRoot, name);
        Directory.CreateDirectory(Path.Combine(path, "assets"));
        File.Copy(Path.Combine(outputRoot, "Panel.xaml"), Path.Combine(path, "Panel.xaml"));
        File.Copy(Path.Combine(outputRoot, "assets", "zili.png"), Path.Combine(path, "assets", "zili.png"));
        return path;
    }

    private static void WithPanel(string name, Action<PanelController, string> action)
    {
        string path = Prepare(name);
        var panel = new PanelController(path, true);
        panel.Window.Left = 50;
        panel.Window.Top = 50;
        try { action(panel, path); }
        finally { panel.Window.Close(); }
    }

    private static object Call(PanelController panel, string name)
    {
        return typeof(PanelController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
    }

    private static TimerEngine Timer(PanelController panel)
    {
        return (TimerEngine)typeof(PanelController).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
    }

    private static T Control<T>(PanelController panel, string name) where T : class
    {
        return panel.Window.FindName(name) as T;
    }

    private static void Click(PanelController panel, string name)
    {
        Control<Button>(panel, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void TestCompactControls()
    {
        WithPanel("compact-controls", delegate(PanelController panel, string path)
        {
            Call(panel, "RenderTimer");
            Assert(Control<TextBlock>(panel, "CompactInfo").Text == "00:00", "Compact timer is missing.");
            Click(panel, "CompactPause");
            Assert(Timer(panel).IsPaused && Control<Button>(panel, "CompactPause").Content.ToString() == "▶", "Compact pause failed.");
            Click(panel, "CompactPause");
            Assert(!Timer(panel).IsPaused, "Compact resume failed.");
            Click(panel, "MiniDetails");
            Assert(Control<ScrollViewer>(panel, "FullBody").Visibility == Visibility.Visible && panel.Window.Width == 300, "Details did not open.");
            Click(panel, "CollapseButton");
            Assert(Control<ScrollViewer>(panel, "FullBody").Visibility == Visibility.Collapsed && panel.Window.Width == 132, "Compact mode did not return.");
        });
    }

    private static void TestReminderControls()
    {
        WithPanel("reminder-controls", delegate(PanelController panel, string path)
        {
            Control<CheckBox>(panel, "RestEnabled").IsChecked = false;
            Assert(!Timer(panel).BreakEnabled, "Reminder did not disable.");
            Control<CheckBox>(panel, "RestEnabled").IsChecked = true;
            Assert(Timer(panel).BreakEnabled && !Timer(panel).BreakDue && Timer(panel).SecondsUntilBreak == 2700, "Reminder did not restart its interval.");
            Control<TextBox>(panel, "IntervalInput").Text = "30";
            Call(panel, "ApplySettings");
            Assert(Timer(panel).IntervalMinutes == 30 && Timer(panel).SecondsUntilBreak == 1800, "New reminder interval did not apply.");
        });
    }

    private static void TestInvalidInterval()
    {
        WithPanel("invalid-interval", delegate(PanelController panel, string path)
        {
            foreach (string value in new[] { "bad", "0", "241" })
            {
                Control<TextBox>(panel, "IntervalInput").Text = value;
                Call(panel, "ApplySettings");
                Assert(Timer(panel).IntervalMinutes == 45 && Control<TextBlock>(panel, "SettingsMessage").Text.Contains("1–240"), "Invalid interval changed settings.");
            }
        });
    }

    private static void TestSaveFailure()
    {
        WithPanel("save-failure", delegate(PanelController panel, string path)
        {
            Directory.CreateDirectory(Path.Combine(path, "smoke-data", "panel-settings.json"));
            Control<TextBox>(panel, "IntervalInput").Text = "45";
            Call(panel, "ApplySettings");
            string message = Control<TextBlock>(panel, "SettingsMessage").Text;
            Assert(!message.Contains("已保存") && (message.Contains("未保存") || message.Contains("失败")), "Failed write claimed success: " + message);
        });
    }

    private static void TestCorruptOptions()
    {
        string path = Prepare("corrupt-options");
        Directory.CreateDirectory(Path.Combine(path, "smoke-data"));
        File.WriteAllText(Path.Combine(path, "smoke-data", "panel-settings.json"), "{broken", Encoding.UTF8);
        var panel = new PanelController(path, true);
        try
        {
            var options = (PanelOptions)typeof(PanelController).GetField("options", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
            Assert(options.Pinned && options.Collapsed && !options.HasPosition, "Malformed options did not restore defaults.");
        }
        finally { panel.Window.Close(); }
    }

    private static void TestLegacyRecords()
    {
        string path = Prepare("legacy-records");
        string data = Path.Combine(path, "smoke-data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "panel-settings.json"),
            "{\"Left\":51,\"Top\":52,\"HasPosition\":true,\"Collapsed\":true,\"Pinned\":false,\"LayoutVersion\":2,\"RemovedFeature\":true}", Encoding.UTF8);
        string day = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        File.WriteAllText(Path.Combine(data, "timer-state.json"),
            "{\"SchemaVersion\":1,\"BreakEnabled\":false,\"IntervalMinutes\":30,\"DaySeconds\":{\"" + day + "\":1234},\"RemovedFeature\":{}}", Encoding.UTF8);
        var panel = new PanelController(path, true);
        try
        {
            var options = (PanelOptions)typeof(PanelController).GetField("options", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
            Assert(options.Left == 51 && options.Top == 52 && options.HasPosition && !options.Pinned, "Saved position/pin settings were lost.");
            Assert(Timer(panel).TodaySeconds == 1234 && Timer(panel).IntervalMinutes == 30 && !Timer(panel).BreakEnabled, "Timer totals or reminder preferences were lost.");
            Assert(Timer(panel).Save(), "Legacy records could not be saved.");
            Assert(new TimerEngine(data).TodaySeconds == 1234, "Daily total did not survive migration.");
        }
        finally { panel.Window.Close(); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
