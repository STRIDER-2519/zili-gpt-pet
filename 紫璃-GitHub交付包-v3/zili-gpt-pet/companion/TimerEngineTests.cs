using System;
using System.IO;
using System.Text;
using ZiliCompanion;

internal static class TimerEngineTests
{
    private static int passed;
    private static string root;

    public static int Main()
    {
        root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".timer-test-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Run("counting, pause and resume", TestPause);
            Run("midnight split", TestMidnight);
            Run("restart restores today but resets session", TestRestart);
            Run("sleep, lock and monotonic reset", TestSuspension);
            Run("wall-clock correction cannot inflate duration", TestClockCorrection);
            Run("break due, snooze, rest and disable", TestBreaks);
            Run("damaged save recovers previous atomic backup", TestRecovery);
            Run("invalid values and persistence failure are contained", TestInvalidInputs);
            Console.WriteLine("PASS: " + passed + " timer boundary checks.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
        finally
        {
            // Verify the resolved absolute target before any recursive Windows deletion.
            string baseDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string testDirectory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (testDirectory.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(testDirectory).StartsWith(".timer-test-data-", StringComparison.Ordinal)
                && Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
        }
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; }
        catch (Exception error) { throw new Exception(name + ": " + error.Message, error); }
    }

    private static TimerEngine Engine(string name)
    {
        return new TimerEngine(Path.Combine(root, name));
    }

    private static DateTimeOffset TodayAt(int hour, int minute, int second)
    {
        return new DateTimeOffset(DateTime.Today.AddHours(hour).AddMinutes(minute).AddSeconds(second), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
    }

    private static void Advance(TimerEngine engine, ref DateTimeOffset time, ref double monotonic, int seconds, bool suspended = false)
    {
        for (int left = seconds; left > 0; )
        {
            int step = Math.Min(left, 5);
            time = time.AddSeconds(step);
            monotonic += step;
            engine.Tick(time, monotonic, suspended);
            left -= step;
        }
    }

    private static void TestPause()
    {
        TimerEngine engine = Engine("pause");
        DateTimeOffset time = TodayAt(9, 0, 0); double monotonic = 100;
        engine.Tick(time, monotonic, false);
        Equal(0, engine.SessionSeconds, "first sample must only establish baseline");
        Advance(engine, ref time, ref monotonic, 120);
        engine.Pause(); Advance(engine, ref time, ref monotonic, 90);
        Equal(120, engine.SessionSeconds, "paused time must be excluded");
        engine.Resume(); Advance(engine, ref time, ref monotonic, 5);
        Equal(120, engine.SessionSeconds, "resume boundary must be excluded");
        Advance(engine, ref time, ref monotonic, 120);
        Equal(240, engine.SessionSeconds, "running intervals must accumulate");
        Equal(240, engine.TodaySeconds, "daily total");
        Equal(240, engine.ContinuousSeconds, "manual pause preserves the continuous segment");
    }

    private static void TestMidnight()
    {
        TimerEngine engine = Engine("midnight");
        DateTimeOffset time = TodayAt(23, 59, 55); DateTime firstDay = time.Date; double monotonic = 0;
        engine.Tick(time, monotonic, false);
        time = time.AddSeconds(10); monotonic += 10; engine.Tick(time, monotonic, false);
        Equal(5, engine.GetDaySeconds(firstDay), "seconds before midnight");
        Equal(5, engine.TodaySeconds, "seconds after midnight");
        Equal(10, engine.SessionSeconds, "session spans dates");
        True(engine.Save(), "save crossing midnight");
        TimerEngine loaded = Engine("midnight"); loaded.Tick(time, 0, false);
        Equal(5, loaded.TodaySeconds, "new-day total must persist");
    }

    private static void TestRestart()
    {
        TimerEngine engine = Engine("restart");
        DateTimeOffset time = TodayAt(12, 0, 0); double monotonic = 0;
        engine.Tick(time, monotonic, false); Advance(engine, ref time, ref monotonic, 300);
        engine.IntervalMinutes = 60; engine.BreakEnabled = false; True(engine.Save(), "save successful");
        TimerEngine restarted = Engine("restart");
        Equal(0, restarted.SessionSeconds, "session cannot persist across launches");
        Equal(0, restarted.ContinuousSeconds, "continuous time cannot persist across launches");
        Equal(300, restarted.TodaySeconds, "today total survives restart");
        True(!restarted.BreakEnabled && restarted.IntervalMinutes == 60, "reminder settings survive restart");
        time = time.AddHours(8); restarted.Tick(time, 0, false);
        Equal(300, restarted.TodaySeconds, "closed interval cannot add time");
    }

    private static void TestSuspension()
    {
        TimerEngine engine = Engine("suspension");
        DateTimeOffset time = TodayAt(9, 0, 0); double monotonic = 0;
        engine.Tick(time, monotonic, false); Advance(engine, ref time, ref monotonic, 30);
        time = time.AddHours(12); monotonic += 43200; engine.Tick(time, monotonic, false);
        Equal(30, engine.SessionSeconds, "sleep gap must add zero");
        Equal(0, engine.ContinuousSeconds, "sleep is a break");
        Advance(engine, ref time, ref monotonic, 5, true);
        Advance(engine, ref time, ref monotonic, 60, true);
        Advance(engine, ref time, ref monotonic, 5, false);
        Equal(30, engine.SessionSeconds, "locked and unlock boundary intervals must add zero");
        Advance(engine, ref time, ref monotonic, 5, false);
        Equal(35, engine.SessionSeconds, "counting resumes after unlock");
        monotonic = 0; engine.Tick(time, monotonic, false);
        Equal(0, engine.ContinuousSeconds, "monotonic reset must reset continuity");
        Advance(engine, ref time, ref monotonic, 5, false);
        Equal(40, engine.SessionSeconds, "monotonic reset must not disable future counting");
    }

    private static void TestClockCorrection()
    {
        TimerEngine engine = Engine("clock");
        DateTimeOffset time = TodayAt(10, 0, 0); double monotonic = 0;
        engine.Tick(time, monotonic, false);
        time = time.AddDays(1); monotonic += 5; engine.Tick(time, monotonic, false);
        Equal(5, engine.SessionSeconds, "forward wall-clock adjustment uses monotonic duration");
        Equal(5, engine.TodaySeconds, "new local date receives actual sampled time");
        time = time.AddDays(-1); monotonic += 5; engine.Tick(time, monotonic, false);
        Equal(10, engine.SessionSeconds, "backward wall-clock adjustment preserves duration");
        Equal(5, engine.TodaySeconds, "backward date receives only actual sampled time");
    }

    private static void TestBreaks()
    {
        TimerEngine engine = Engine("breaks");
        DateTimeOffset time = TodayAt(9, 0, 0); double monotonic = 0;
        engine.Tick(time, monotonic, false); Advance(engine, ref time, ref monotonic, 2699);
        True(!engine.BreakDue, "default reminder must wait for 45 minutes");
        Equal(1, engine.SecondsUntilBreak, "remaining time before the default boundary");
        Advance(engine, ref time, ref monotonic, 1); True(engine.BreakDue, "45-minute boundary must trigger");
        Equal(0, engine.SecondsUntilBreak, "due reminder has zero remaining time");
        engine.Snooze(); Equal(300, engine.SecondsUntilBreak, "snoozing replaces the reminder gate");
        Advance(engine, ref time, ref monotonic, 299);
        True(!engine.BreakDue, "snooze must wait for five more used minutes");
        Equal(1, engine.SecondsUntilBreak, "remaining time honors the snoozed gate");
        Advance(engine, ref time, ref monotonic, 1); True(engine.BreakDue, "snooze boundary triggers");
        Equal(3000, engine.SessionSeconds, "snooze still counts total use");
        engine.PauseForBreak(); True(engine.IsPaused && !engine.BreakDue, "rest pauses and dismisses");
        Equal(0, engine.ContinuousSeconds, "rest resets continuous use");
        Advance(engine, ref time, ref monotonic, 300);
        engine.Resume(); Advance(engine, ref time, ref monotonic, 5); Advance(engine, ref time, ref monotonic, 30);
        Equal(30, engine.ContinuousSeconds, "after rest a new continuous period begins");
        engine.DisableBreakReminders(); Equal(0, engine.SecondsUntilBreak, "disabled reminder has no countdown");
        Advance(engine, ref time, ref monotonic, 2700);
        True(!engine.BreakDue, "disabled reminders remain silent");
        engine.BreakEnabled = true; Equal(0, engine.ContinuousSeconds, "re-enabling starts a fresh interval");
        Advance(engine, ref time, ref monotonic, 2700); True(engine.BreakDue, "re-enabled reminder works");
    }

    private static void TestRecovery()
    {
        TimerEngine engine = Engine("recovery");
        DateTimeOffset time = TodayAt(11, 0, 0); double monotonic = 0;
        engine.Tick(time, monotonic, false); Advance(engine, ref time, ref monotonic, 15); True(engine.Save(), "first save");
        Advance(engine, ref time, ref monotonic, 15); True(engine.Save(), "atomic second save");
        File.WriteAllText(Path.Combine(root, "recovery", "timer-state.json"), "{broken", Encoding.UTF8);
        TimerEngine recovered = Engine("recovery");
        Equal(15, recovered.TodaySeconds, "corrupt current save restores last valid backup");
        True(!String.IsNullOrEmpty(recovered.LastPersistenceError), "recovery is visible to the UI");
        True(recovered.Save(), "recovered state can be saved again");
        True(String.IsNullOrEmpty(recovered.LastPersistenceError), "successful save clears the issue");
        File.WriteAllText(Path.Combine(root, "recovery", "timer-state.json"), "{broken again", Encoding.UTF8);
        TimerEngine recoveredAgain = Engine("recovery");
        Equal(15, recoveredAgain.TodaySeconds, "repair must not replace the valid backup with damaged data");
    }

    private static void TestInvalidInputs()
    {
        TimerEngine engine = Engine("invalid");
        DateTimeOffset time = TodayAt(9, 0, 0); engine.Tick(time, Double.NaN, false);
        engine.Tick(time, 0, false); engine.Tick(time.AddSeconds(5), 5, false);
        Equal(5, engine.SessionSeconds, "bad sample must not poison later samples");
        string blocker = Path.Combine(root, "blocked-directory"); File.WriteAllText(blocker, "a file occupies this directory");
        TimerEngine unavailable = new TimerEngine(blocker);
        True(!unavailable.Save() && !String.IsNullOrEmpty(unavailable.LastPersistenceError), "save failure must return status rather than crash");
    }

    private static void Equal(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.000001) throw new Exception(message + " (expected " + expected + ", got " + actual + ")");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
