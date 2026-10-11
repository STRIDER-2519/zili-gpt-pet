using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ZiliCompanion
{
    // Counts sampled running time, never the wall-clock gap between launches.
    public sealed class TimerEngine
    {
        private const double MaximumSampleGapSeconds = 10.0;
        private readonly string statePath;
        private readonly Dictionary<string, double> daySeconds = new Dictionary<string, double>(StringComparer.Ordinal);
        private bool hasSample;
        private bool lastSuspended;
        private bool lastPaused;
        private bool loadedFromBackup;
        private DateTimeOffset lastWallTime;
        private double lastMonotonicSeconds;
        private string currentDay;
        private bool breakEnabled = true;
        private int intervalMinutes = 45;
        private double nextBreakSeconds = 45 * 60;

        public TimerEngine(string dataDirectory)
        {
            if (String.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("A data directory is required.", "dataDirectory");
            statePath = Path.Combine(Path.GetFullPath(dataDirectory), "timer-state.json");
            currentDay = DayKey(DateTimeOffset.Now);
            Load();
        }

        public double SessionSeconds { get; private set; }
        public double TodaySeconds
        {
            get { double value; return daySeconds.TryGetValue(currentDay, out value) ? value : 0; }
        }
        public double ContinuousSeconds { get; private set; }
        public bool IsPaused { get; private set; }
        public bool BreakDue { get; private set; }
        public double SecondsUntilBreak
        {
            get { return !BreakEnabled || BreakDue ? 0 : Math.Max(0, nextBreakSeconds - ContinuousSeconds); }
        }
        public string LastPersistenceError { get; private set; }
        public bool BreakEnabled
        {
            get { return breakEnabled; }
            set
            {
                if (breakEnabled == value) return;
                breakEnabled = value;
                ResetContinuousPeriod();
            }
        }
        public int IntervalMinutes
        {
            get { return intervalMinutes; }
            set
            {
                if (value < 1 || value > 240) throw new ArgumentOutOfRangeException("value", "The interval must be 1 to 240 minutes.");
                if (intervalMinutes == value) return;
                intervalMinutes = value;
                // A settings change starts a fresh reminder interval.
                ResetContinuousPeriod();
            }
        }

        public void Tick(DateTimeOffset now, double monotonicSeconds, bool suspended)
        {
            currentDay = DayKey(now);
            if (!IsFinite(monotonicSeconds))
            {
                hasSample = false;
                return;
            }
            if (!hasSample)
            {
                SetSample(now, monotonicSeconds, suspended);
                if (suspended) ResetContinuousPeriod();
                return;
            }

            double elapsed = monotonicSeconds - lastMonotonicSeconds;
            bool gapIsUsable = elapsed > 0 && elapsed <= MaximumSampleGapSeconds;
            if (suspended || lastSuspended || elapsed > MaximumSampleGapSeconds || elapsed < 0)
            {
                // Locking or waking constitutes a break, and cannot add overnight time.
                ResetContinuousPeriod();
            }
            else if (gapIsUsable && !IsPaused && !lastPaused)
            {
                SessionSeconds += elapsed;
                ContinuousSeconds += elapsed;
                AddDailyTime(lastWallTime, now, elapsed);
                if (BreakEnabled && ContinuousSeconds >= nextBreakSeconds) BreakDue = true;
            }
            SetSample(now, monotonicSeconds, suspended);
        }

        public void Pause()
        {
            IsPaused = true;
            // Changing modes between samples must not count the boundary interval.
            lastPaused = true;
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            lastPaused = true;
        }

        public void Snooze(int minutes = 5)
        {
            if (minutes < 1 || minutes > 240) throw new ArgumentOutOfRangeException("minutes");
            BreakDue = false;
            nextBreakSeconds = ContinuousSeconds + minutes * 60.0;
        }

        public void PauseForBreak()
        {
            Pause();
            ResetContinuousPeriod();
        }

        public void ResetContinuousPeriod()
        {
            ContinuousSeconds = 0;
            BreakDue = false;
            nextBreakSeconds = IntervalMinutes * 60.0;
        }

        public void DisableBreakReminders()
        {
            BreakEnabled = false;
        }

        public double GetDaySeconds(DateTime localDate)
        {
            double value;
            return daySeconds.TryGetValue(localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out value) ? value : 0;
        }

        public bool Save()
        {
            string temporaryPath = statePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                TrimRecords();
                TimerPersistedState state = new TimerPersistedState
                {
                    SchemaVersion = 1,
                    BreakEnabled = BreakEnabled,
                    IntervalMinutes = IntervalMinutes,
                    DaySeconds = new Dictionary<string, double>(daySeconds)
                };
                string json = new JavaScriptSerializer().Serialize(state);
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                // After recovery, keep the known-good backup instead of replacing it
                // with the damaged file we are about to overwrite.
                if (File.Exists(statePath)) File.Replace(temporaryPath, statePath, loadedFromBackup ? null : statePath + ".bak", true);
                else File.Move(temporaryPath, statePath);
                loadedFromBackup = false;
                LastPersistenceError = null;
                return true;
            }
            catch (Exception error)
            {
                LastPersistenceError = "无法保存计时记录：" + error.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch { /* A failed cleanup must not stop the companion. */ }
            }
        }

        private void Load()
        {
            if (!File.Exists(statePath) && !File.Exists(statePath + ".bak")) return;
            Exception lastError = null;
            foreach (string path in new[] { statePath, statePath + ".bak" })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    TimerPersistedState state = new JavaScriptSerializer().Deserialize<TimerPersistedState>(File.ReadAllText(path, Encoding.UTF8));
                    if (state == null || state.SchemaVersion != 1) throw new InvalidDataException("计时记录格式无法识别。");
                    if (state.DaySeconds != null)
                    {
                        foreach (KeyValuePair<string, double> pair in state.DaySeconds)
                        {
                            DateTime date;
                            if (DateTime.TryParseExact(pair.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                                && IsFinite(pair.Value) && pair.Value >= 0 && pair.Value <= 172800)
                                daySeconds[pair.Key] = pair.Value;
                        }
                    }
                    breakEnabled = state.BreakEnabled;
                    intervalMinutes = state.IntervalMinutes >= 1 && state.IntervalMinutes <= 240 ? state.IntervalMinutes : 45;
                    ResetContinuousPeriod();
                    TrimRecords();
                    loadedFromBackup = path != statePath;
                    LastPersistenceError = path == statePath ? null : "计时记录已从上次备份恢复。";
                    return;
                }
                catch (Exception error) { lastError = error; }
            }
            LastPersistenceError = "无法读取计时记录，已从零开始：" + (lastError == null ? "文件不可用。" : lastError.Message);
        }

        private void SetSample(DateTimeOffset now, double monotonicSeconds, bool suspended)
        {
            hasSample = true;
            lastWallTime = now;
            lastMonotonicSeconds = monotonicSeconds;
            lastSuspended = suspended;
            lastPaused = IsPaused;
        }

        private void AddDailyTime(DateTimeOffset start, DateTimeOffset end, double elapsed)
        {
            double wallElapsed = (end - start).TotalSeconds;
            if (DayKey(start) == DayKey(end) || wallElapsed <= 0 || Math.Abs(wallElapsed - elapsed) > 2)
            {
                // Clock corrections affect the date label, never the counted duration.
                AddToDay(DayKey(end), elapsed);
                return;
            }
            DateTimeOffset midnight = new DateTimeOffset(start.Date.AddDays(1), start.Offset);
            double firstPart = Math.Max(0, Math.Min(elapsed, elapsed * (midnight - start).TotalSeconds / wallElapsed));
            AddToDay(DayKey(start), firstPart);
            AddToDay(DayKey(end), elapsed - firstPart);
        }

        private void AddToDay(string key, double seconds)
        {
            double previous;
            daySeconds.TryGetValue(key, out previous);
            daySeconds[key] = previous + seconds;
        }

        private void TrimRecords()
        {
            foreach (string key in daySeconds.Keys.OrderByDescending(value => value, StringComparer.Ordinal).Skip(90).ToArray())
                daySeconds.Remove(key);
        }

        private static string DayKey(DateTimeOffset time)
        {
            return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }

    public sealed class TimerPersistedState
    {
        public int SchemaVersion { get; set; }
        public bool BreakEnabled { get; set; }
        public int IntervalMinutes { get; set; }
        public Dictionary<string, double> DaySeconds { get; set; }
    }
}
