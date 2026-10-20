using System;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyLight
{
    // Keeps the keyboard at the chosen setting: applies it on start, resume and
    // AC/battery changes, and polls the EC to undo anything else that changes it.
    // Every change seen on the EC is written to trace.jsonl.
    public sealed class Enforcer : IDisposable
    {
        readonly Keyboard kb;
        readonly Control ui;
        readonly Timer timer = new Timer();
        Config cfg;
        long seq;
        bool haveLast;
        KbState last;
        DateTime lastStateWrite = DateTime.MinValue;

        public int Reapplies;
        public string LastEvent = "";
        public KbState Current { get { return last; } }
        public event EventHandler Changed;

        public Enforcer(Keyboard kb, Config cfg, Control ui)
        {
            this.kb = kb;
            this.cfg = cfg;
            this.ui = ui;
            timer.Interval = cfg.EnforceIntervalSec * 1000;
            timer.Tick += delegate { Tick(); };
            timer.Start();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        public void UpdateConfig(Config c)
        {
            cfg = c;
            timer.Interval = c.EnforceIntervalSec * 1000;
            ApplyNow("config");
        }

        public static bool OnBattery(out int percent)
        {
            PowerStatus ps = SystemInformation.PowerStatus;
            float f = ps.BatteryLifePercent;
            percent = f >= 0 && f <= 1 ? (int)Math.Round(f * 100) : 100;
            return ps.PowerLineStatus == PowerLineStatus.Offline;
        }

        public int TargetBrightness(out string why)
        {
            if (!cfg.Enabled)
            {
                why = "backlight switched off by user";
                return 0;
            }
            int pct;
            bool batt = OnBattery(out pct);
            if (cfg.BatteryRule && batt && pct <= cfg.BatteryThresholdPercent)
            {
                why = string.Format("battery rule (on battery, {0}% <= {1}%)", pct, cfg.BatteryThresholdPercent);
                return cfg.BatteryTurnOff ? 0 : Math.Min(cfg.BatteryBrightness, cfg.Brightness);
            }
            why = "user setting";
            return cfg.Brightness;
        }

        public void ApplyNow(string reason)
        {
            string why;
            int target = TargetBrightness(out why);
            try
            {
                KbState before = kb.Read();
                kb.Apply(target, cfg.Color);
                KbState after = kb.Read();
                Trace("apply", reason, before, after, target);
                Log.Info("apply ({0}, {1}): target brightness {2}, color {3}; before [{4}] after [{5}]",
                    reason, why, target, Config.ToHex(cfg.Color), before, after);
                last = after;
                haveLast = true;
                LastEvent = Log.Now() + " applied (" + reason + ")";
            }
            catch (Exception ex)
            {
                Log.Info("apply ({0}) failed: {1}", reason, ex.Message);
                LastEvent = Log.Now() + " apply failed: " + ex.Message;
            }
            Raise();
        }

        void Tick()
        {
            string why;
            int target = TargetBrightness(out why);
            KbState s;
            try
            {
                s = kb.Read();
            }
            catch (Exception ex)
            {
                LastEvent = Log.Now() + " read failed: " + ex.Message;
                Raise();
                return;
            }

            bool changed = !haveLast || !s.SameAs(last);
            if (changed)
            {
                // something other than us changed the keyboard (Fn keys, vendor service, EC)
                Trace("change", "external", last, s, target);
                Log.Info("EC changed externally: [{0}] -> [{1}]", haveLast ? last.ToString() : "-", s);
            }
            last = s;
            haveLast = true;

            if (cfg.Enforce && !kb.Matches(s, target, cfg.Color))
            {
                Reapplies++;
                ApplyNow("reapply");
                return;
            }
            if (changed || (DateTime.Now - lastStateWrite).TotalSeconds >= 10) WriteState(target, why);
            if (changed) Raise();
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            // raised on a system thread; marshal to the UI thread that owns the EC calls
            string reason = e.Mode == PowerModes.Resume ? "resume" : (e.Mode == PowerModes.StatusChange ? "power-change" : null);
            if (reason == null || !ui.IsHandleCreated) return;
            ui.BeginInvoke((MethodInvoker)delegate { ApplyNow(reason); });
        }

        void Trace(string ev, string reason, KbState before, KbState after, int target)
        {
            int pct;
            bool batt = OnBattery(out pct);
            seq++;
            Log.Trace(string.Format(
                "{{\"ts\":\"{0}\",\"seq\":{1},\"ch\":\"kbd\",\"ev\":\"{2}\",\"reason\":\"{3}\",\"before\":\"0x{4:X2}\",\"after\":\"0x{5:X2}\",\"brightness\":{6},\"off_bit\":{7},\"rgb_level\":[{8},{9},{10}],\"target\":{11},\"on_battery\":{12},\"battery_pct\":{13}}}",
                Log.Now(), seq, ev, reason, before.Status, after.Status, after.Brightness, after.PowerOffBit ? 1 : 0,
                after.Red, after.Green, after.Blue, target, batt ? "true" : "false", pct));
        }

        void WriteState(int target, string why)
        {
            int pct;
            bool batt = OnBattery(out pct);
            var sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"heartbeat\":\"{0}\",", Log.Now());
            sb.AppendFormat("\"status\":\"0x{0:X2}\",\"brightness\":{1},\"lit\":{2},", last.Status, last.Brightness, last.IsLit ? "true" : "false");
            sb.AppendFormat("\"rgb_level\":[{0},{1},{2}],", last.Red, last.Green, last.Blue);
            sb.AppendFormat("\"target\":{0},\"target_reason\":\"{1}\",", target, why);
            sb.AppendFormat("\"on_battery\":{0},\"battery_pct\":{1},\"reapplies\":{2}", batt ? "true" : "false", pct, Reapplies);
            sb.Append("}");
            Log.WriteState(sb.ToString());
            lastStateWrite = DateTime.Now;
        }

        void Raise()
        {
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            timer.Dispose();
        }
    }
}
