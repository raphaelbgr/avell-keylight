using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyLight
{
    // Keeps the keyboard at the chosen setting: applies it on start, resume and
    // AC/battery changes, and polls the EC to undo anything else that changes it.
    // All EC (WMI) calls run on one background worker thread, never on the UI thread:
    // each call takes ~0.1-0.2 s, so doing them on the UI thread froze the window.
    // Requests are coalesced: while an apply runs, newer settings replace older ones.
    // Every change seen on the EC is written to trace.jsonl.
    public sealed class Enforcer : IDisposable
    {
        readonly Control ui;
        readonly Thread worker;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly object gate = new object();
        volatile bool stopping;
        Exception startError;

        // guarded by gate
        Config cfg;
        string pendingReason;
        KbState last;
        int reapplies;
        string lastEvent = "";

        // worker thread only
        Ec ec;
        Keyboard kb;
        long seq;
        bool haveLast;
        DateTime lastStateWrite = DateTime.MinValue;

        public bool IsRgb { get; private set; }
        public int Reapplies { get { lock (gate) return reapplies; } }
        public string LastEvent { get { lock (gate) return lastEvent; } }
        public KbState Current { get { lock (gate) return last; } }

        // raised on the UI thread
        public event EventHandler Changed;
        // the user changed brightness with the Fn keys; raised on the UI thread with the new level
        public event Action<int> HotkeyBrightness;

        public Enforcer(Config cfg, Control ui)
        {
            this.cfg = cfg;
            this.ui = ui;
            pendingReason = "startup";
            worker = new Thread(Run) { IsBackground = true, Name = "KeyLight EC worker" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            ready.WaitOne(30000);
            if (startError != null) throw new InvalidOperationException(startError.Message, startError);
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        public void UpdateConfig(Config c)
        {
            Request(c, "config");
        }

        public void ApplyNow(string reason)
        {
            Request(null, reason);
        }

        void Request(Config c, string reason)
        {
            lock (gate)
            {
                if (c != null) cfg = c;
                pendingReason = reason;
            }
            wake.Set();
        }

        // self-test only: simulate battery state
        public static volatile bool TestPowerOverride;
        public static volatile bool TestOnBattery;
        public static volatile int TestPercent;

        public static bool OnBattery(out int percent)
        {
            if (TestPowerOverride)
            {
                percent = TestPercent;
                return TestOnBattery;
            }
            PowerStatus ps = SystemInformation.PowerStatus;
            float f = ps.BatteryLifePercent;
            percent = f >= 0 && f <= 1 ? (int)Math.Round(f * 100) : 100;
            return ps.PowerLineStatus == PowerLineStatus.Offline;
        }

        public int TargetBrightness(out string why)
        {
            Config c;
            lock (gate) c = cfg;
            return Target(c, out why);
        }

        static int Target(Config c, out string why)
        {
            if (!c.Enabled)
            {
                why = "backlight switched off by user";
                return 0;
            }
            int pct;
            bool batt = OnBattery(out pct);
            if (c.BatteryRule && batt && pct <= c.BatteryThresholdPercent)
            {
                why = string.Format("battery rule (on battery, {0}% <= {1}%)", pct, c.BatteryThresholdPercent);
                return c.BatteryTurnOff ? 0 : Math.Min(c.BatteryBrightness, c.Brightness);
            }
            why = "user setting";
            return c.Brightness;
        }

        void Run()
        {
            try
            {
                ec = new Ec();
                kb = new Keyboard(ec);
                IsRgb = kb.IsRgb;
                Log.Info("start: keyboard type {0}, initial [{1}]", kb.IsRgb ? "1-zone RGB" : "single color", kb.Read());
            }
            catch (Exception ex)
            {
                startError = ex;
                ready.Set();
                return;
            }
            ready.Set();

            while (!stopping)
            {
                Config c;
                string reason;
                lock (gate)
                {
                    c = cfg;
                    reason = pendingReason;
                    pendingReason = null;
                }
                c.ApplyCalibration(kb);
                if (reason != null) DoApply(c, reason);
                else Tick(c);
                wake.WaitOne(c.EnforceIntervalSec * 1000);
            }
            ec.Dispose();
        }

        void DoApply(Config c, string reason)
        {
            string why;
            int target = Target(c, out why);
            try
            {
                KbState before = haveLast ? last : kb.Read();
                kb.Apply(target, c.Color, before);
                KbState after = kb.Read();
                Trace("apply", reason, before, after, target);
                Log.Info("apply ({0}, {1}): target brightness {2}, color {3}, intensity {4}%; before [{5}] after [{6}]",
                    reason, why, target, Config.ToHex(c.Color), c.IntensityPercent, before, after);
                haveLast = true;
                lock (gate)
                {
                    last = after;
                    lastEvent = Log.Now() + " applied (" + reason + ")";
                }
                WriteState(c, target, why);
            }
            catch (Exception ex)
            {
                Log.Info("apply ({0}) failed: {1}", reason, ex.Message);
                lock (gate) lastEvent = Log.Now() + " apply failed: " + ex.Message;
            }
            Raise();
        }

        void Tick(Config c)
        {
            string why;
            int target = Target(c, out why);
            KbState s;
            try
            {
                s = kb.Read();
            }
            catch (Exception ex)
            {
                lock (gate) lastEvent = Log.Now() + " read failed: " + ex.Message;
                Raise();
                return;
            }

            KbState prev = last;
            bool changed = !haveLast || !s.SameAs(prev);
            if (changed)
            {
                // something other than us changed the keyboard (Fn keys, vendor service, EC)
                Trace("change", "external", prev, s, target);
                Log.Info("EC changed externally: [{0}] -> [{1}]", haveLast ? prev.ToString() : "-", s);
            }
            bool hadLast = haveLast;
            haveLast = true;
            lock (gate) last = s;

            // A brightness step to a non-zero level with nothing else changed is the Fn
            // brightness key: keep it as the new setting instead of fighting the user.
            // A change to off is not followed (that is how the vendor service kills the light).
            if (changed && hadLast && c.FollowFnKeys && target > 0 && s.IsLit && !prev.PowerOffBit
                && s.Brightness != prev.Brightness && s.Red == prev.Red && s.Green == prev.Green && s.Blue == prev.Blue)
            {
                int level = s.Brightness;
                Log.Info("Fn key brightness {0} -> {1}: adopted as the new setting", prev.Brightness, level);
                Trace("change", "fn-key-adopted", prev, s, level);
                if (ui.IsHandleCreated)
                    ui.BeginInvoke((MethodInvoker)delegate { var h = HotkeyBrightness; if (h != null) h(level); });
                return;
            }

            if (c.Enforce && !kb.Matches(s, target, c.Color))
            {
                lock (gate) reapplies++;
                DoApply(c, "reapply");
                return;
            }
            if (changed || (DateTime.Now - lastStateWrite).TotalSeconds >= 10) WriteState(c, target, why);
            if (changed) Raise();
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) ApplyNow("resume");
            else if (e.Mode == PowerModes.StatusChange) ApplyNow("power-change");
        }

        void Trace(string ev, string reason, KbState before, KbState after, int target)
        {
            int pct;
            bool batt = OnBattery(out pct);
            seq++;
            Log.Trace(string.Format(
                "{{\"ts\":\"{0}\",\"seq\":{1},\"ch\":\"kbd\",\"ev\":\"{2}\",\"reason\":\"{3}\",\"before\":\"0x{4:X2}\",\"after\":\"0x{5:X2}\",\"brightness\":{6},\"off_bit\":{7},\"rgb_level\":[{8},{9},{10}],\"target\":{11},\"on_battery\":{12},\"battery_pct\":{13},\"thread\":\"bg\"}}",
                Log.Now(), seq, ev, reason, before.Status, after.Status, after.Brightness, after.PowerOffBit ? 1 : 0,
                after.Red, after.Green, after.Blue, target, batt ? "true" : "false", pct));
        }

        void WriteState(Config c, int target, string why)
        {
            int pct;
            bool batt = OnBattery(out pct);
            KbState s;
            int n;
            lock (gate)
            {
                s = last;
                n = reapplies;
            }
            var sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"heartbeat\":\"{0}\",", Log.Now());
            sb.AppendFormat("\"status\":\"0x{0:X2}\",\"brightness\":{1},\"lit\":{2},", s.Status, s.Brightness, s.IsLit ? "true" : "false");
            sb.AppendFormat("\"rgb_level\":[{0},{1},{2}],\"intensity_pct\":{3},", s.Red, s.Green, s.Blue, c.IntensityPercent);
            sb.AppendFormat("\"target\":{0},\"target_reason\":\"{1}\",", target, why);
            sb.AppendFormat("\"on_battery\":{0},\"battery_pct\":{1},\"reapplies\":{2}", batt ? "true" : "false", pct, n);
            sb.Append("}");
            Log.WriteState(sb.ToString());
            lastStateWrite = DateTime.Now;
        }

        void Raise()
        {
            if (!ui.IsHandleCreated) return;
            ui.BeginInvoke((MethodInvoker)delegate
            {
                var h = Changed;
                if (h != null) h(this, EventArgs.Empty);
            });
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            stopping = true;
            wake.Set();
            worker.Join(5000);
        }
    }
}
