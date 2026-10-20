using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KeyLight
{
    // End-to-end self-test: runs the real tray app and settings window, drives the real
    // controls from a test thread, and verifies every result on the EC with an
    // independent reader. Measures UI-thread responsiveness the whole time.
    // The user's config is backed up and restored.
    static class SelfTest
    {
        const int UiFreezeLimitMs = 250;

        static readonly StringBuilder report = new StringBuilder();
        static int passed, failed;
        static TrayApp app;
        static SettingsForm form;
        static Ec probe;
        static readonly Stopwatch uiClock = Stopwatch.StartNew();
        static long lastUiTick, maxUiGapMs;
        static string maxUiGapWhere = "";
        static volatile string phase = "start";

        public static int Run(string outFile, string shotFile)
        {
            string backup = File.Exists(Config.FilePath) ? File.ReadAllText(Config.FilePath) : null;
            var baseline = new Config
            {
                Enabled = true, Brightness = 4, MixRed = 60, MixGreen = 60, MixBlue = 100, IntensityPercent = 100,
                Enforce = true, EnforceIntervalSec = 1, FollowFnKeys = true,
                BatteryRule = false, FixVendorSettings = backup == null || Config.Load().FixVendorSettings,
                StopVendorService = false, TrayIconPromoted = true
            };
            baseline.Save();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            app = new TrayApp(false);

            var heartbeat = new System.Windows.Forms.Timer { Interval = 15 };
            heartbeat.Tick += delegate
            {
                long now = uiClock.ElapsedMilliseconds;
                if (lastUiTick > 0 && now - lastUiTick > maxUiGapMs)
                {
                    maxUiGapMs = now - lastUiTick;
                    maxUiGapWhere = phase;
                }
                lastUiTick = now;
            };
            heartbeat.Start();

            var t = new Thread(() => Script(shotFile)) { IsBackground = true, Name = "selftest" };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            Application.Run(app);
            heartbeat.Dispose();

            if (backup != null) File.WriteAllText(Config.FilePath, backup, Encoding.UTF8);
            else File.Delete(Config.FilePath);
            Line("config restored");
            Line(string.Format("RESULT: {0} passed, {1} failed", passed, failed));
            File.WriteAllText(outFile, report.ToString(), Encoding.UTF8);
            return failed == 0 ? 0 : 1;
        }

        static void Script(string shotFile)
        {
            try
            {
                probe = new Ec();
                Line("== EC latency");
                var sw = Stopwatch.StartNew();
                long worst = 0;
                for (int i = 0; i < 20; i++)
                {
                    long t0 = sw.ElapsedMilliseconds;
                    probe.Read(Keyboard.RegStatus);
                    worst = Math.Max(worst, sw.ElapsedMilliseconds - t0);
                }
                Line(string.Format("   20 reads: avg {0:0.0} ms, worst {1} ms", sw.ElapsedMilliseconds / 20.0, worst));

                phase = "startup";
                Check("startup applies baseline (white, brightness 4)", WaitEc(Expect(4, Cfg()), 8000));

                phase = "open window";
                Ui(() => { form = app.ShowSettingsForTest(); });
                Thread.Sleep(500);
                Check("settings window visible", UiGet(() => form.Visible));

                // Brightness slider dragged quickly 4->1->3->2 (each step is a separate change event)
                phase = "brightness slider drag";
                foreach (int v in new[] { 3, 2, 1, 2, 3, 4, 3, 2 })
                {
                    int vv = v;
                    Ui(() => { form.TestBrightness.Value = vv; });
                    Thread.Sleep(40);
                }
                Check("brightness slider drag lands on 2", WaitEc(Expect(2, Cfg()), 6000));
                Check("config saved brightness 2", Config.Load().Brightness == 2);
                foreach (int v in new[] { 1, 3, 4 })
                {
                    int vv = v;
                    phase = "brightness slider " + v;
                    Ui(() => { form.TestBrightness.Value = vv; });
                    Check("brightness slider " + v, WaitEc(Expect(vv, Cfg()), 6000));
                }

                // Intensity slider drag 100 -> 5 -> 60
                phase = "intensity slider drag";
                for (int v = 100; v >= 5; v -= 5)
                {
                    int vv = v;
                    Ui(() => { form.TestIntensity.Value = vv; });
                    Thread.Sleep(25);
                }
                for (int v = 10; v <= 60; v += 10)
                {
                    int vv = v;
                    Ui(() => { form.TestIntensity.Value = vv; });
                    Thread.Sleep(25);
                }
                Check("intensity slider drag lands on 60%", WaitEc(Expect(4, Cfg()), 6000));
                foreach (int v in new[] { 25, 100 })
                {
                    int vv = v;
                    phase = "intensity " + v;
                    Ui(() => { form.TestIntensity.Value = vv; });
                    Check("intensity " + v + "%", WaitEc(Expect(4, Cfg()), 6000));
                }

                // every color preset button
                Button[] presets = UiGet(() => form.TestPresets);
                int[][] mixes = SettingsForm.TestPresetMixes;
                for (int i = 0; i < presets.Length; i++)
                {
                    Button b = presets[i];
                    int[] m = mixes[i];
                    string name = string.Format("R{0} G{1} B{2}", m[0], m[1], m[2]);
                    phase = "preset " + name;
                    Ui(() => b.PerformClick());
                    Check("preset " + name + " -> sliders and LEDs",
                        WaitEc(Expect(4, Cfg()), 6000) + "",
                        UiGet(() => form.TestRed.Value == m[0] && form.TestGreen.Value == m[1] && form.TestBlue.Value == m[2]));
                }

                // each color slider changes ONLY its own LED, by exactly the amount set
                Ui(() => presets[0].PerformClick());
                WaitEc(Expect(4, Cfg()), 6000);
                ChannelSlider("Blue", () => form.TestBlue, 30, s => s.Blue, s => s.Red, s => s.Green);
                ChannelSlider("Green", () => form.TestGreen, 0, s => s.Green, s => s.Red, s => s.Blue);
                ChannelSlider("Red", () => form.TestRed, 100, s => s.Red, s => s.Green, s => s.Blue);
                ChannelSlider("Blue", () => form.TestBlue, 100, s => s.Blue, s => s.Red, s => s.Green);
                Ui(() => presets[0].PerformClick());
                Check("white preset restored", WaitEc(Expect(4, Cfg()), 6000));

                // on/off checkbox
                phase = "on/off";
                Ui(() => { form.TestOn.Checked = false; });
                Check("'Backlight on' unchecked turns light off", WaitEc(s => !s.IsLit, 6000));
                Ui(() => { form.TestOn.Checked = true; });
                Check("'Backlight on' checked turns light back on", WaitEc(Expect(4, Cfg()), 6000));

                // Fn key simulation: someone else sets brightness 3 on the EC
                phase = "fn key";
                WriteBrightness(3);
                Thread.Sleep(3500);
                Check("Fn-key brightness 3 adopted as setting", Cfg().Brightness == 3 && UiGet(() => form.TestBrightness.Value) == 3 && ProbeRead().Brightness == 3,
                    string.Format("config={0} slider={1} ec={2}", Cfg().Brightness, UiGet(() => form.TestBrightness.Value), ProbeRead().Brightness));

                // vendor-style kill: brightness 0 must be reverted
                phase = "vendor off";
                WriteBrightness(0);
                Check("external switch-off is reverted", WaitEc(Expect(3, Cfg()), 6000));

                // battery rule with simulated battery
                phase = "battery rule";
                Enforcer.TestOnBattery = true;
                Enforcer.TestPercent = 50;
                Enforcer.TestPowerOverride = true;
                Update(c => { c.BatteryRule = true; c.BatteryTurnOff = false; c.BatteryBrightness = 1; c.BatteryThresholdPercent = 100; });
                Check("battery rule: dim to 1 on battery", WaitEc(Expect(1, Cfg()), 6000));
                Update(c => { c.BatteryTurnOff = true; });
                Check("battery rule: turn off on battery", WaitEc(s => !s.IsLit, 6000));
                Update(c => { c.BatteryThresholdPercent = 40; });
                Check("battery rule: 50% > 40% threshold keeps setting", WaitEc(Expect(3, Cfg()), 6000));
                Update(c => { c.BatteryThresholdPercent = 100; c.BatteryRule = false; });
                Check("battery rule disabled: on battery stays at setting", WaitEc(Expect(3, Cfg()), 6000));
                Enforcer.TestPowerOverride = false;

                // window behaviour
                phase = "window";
                Ui(() => { form.WindowState = FormWindowState.Minimized; });
                Thread.Sleep(300);
                Check("minimize hides to tray", !UiGet(() => form.Visible));
                Ui(() => { form = app.ShowSettingsForTest(); });
                Thread.Sleep(300);
                Check("reopen from tray", UiGet(() => form.Visible));
                Ui(() =>
                {
                    using (var bmp = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(shotFile, ImageFormat.Png);
                    }
                });
                Line("   window screenshot: " + shotFile);
                Ui(() => form.TestHide.PerformClick());
                Thread.Sleep(300);
                Check("'Hide to tray' button hides window", !UiGet(() => form.Visible));

                Line("   EC calls retried (firmware busy): " + Ec.Retries);
                Check(string.Format("UI never froze > {0} ms (worst gap {1} ms during '{2}')", UiFreezeLimitMs, maxUiGapMs, maxUiGapWhere),
                    maxUiGapMs <= UiFreezeLimitMs);

                phase = "exit";
                Line("== exit via tray menu 'Exit KeyLight'");
                ToolStripItem exit = UiGet(() => app.TestMenuItem("Exit KeyLight"));
                Check("tray menu has 'Exit KeyLight'", exit != null);
                probe.Dispose();
                Ui(() => exit.PerformClick());
            }
            catch (Exception ex)
            {
                Check("test script crashed: " + ex, false);
                try { Ui(() => app.Exit()); } catch (Exception) { }
            }
        }

        static Config Cfg()
        {
            return UiGet(() => app.Cfg);
        }

        static void Update(Action<Config> change)
        {
            Ui(() =>
            {
                Config c = app.Cfg.Clone();
                change(c);
                app.Update(c);
            });
        }

        // drags one color slider to target in steps; that LED must end at target% of 50,
        // the other two must not move
        static void ChannelSlider(string name, Func<TrackBar> bar, int target,
            Func<KbState, int> own, Func<KbState, int> other1, Func<KbState, int> other2)
        {
            phase = name + " slider -> " + target;
            KbState before = ProbeRead();
            int from = UiGet(() => bar().Value);
            int step = target > from ? 5 : -5;
            for (int v = from; v != target; )
            {
                v = Math.Abs(target - v) < 5 ? target : v + step;
                int vv = v;
                Ui(() => { bar().Value = vv; });
                Thread.Sleep(30);
            }
            string wait = WaitEc(Expect(4, Cfg()), 6000);
            KbState after = ProbeRead();
            int want = (int)Math.Round(Keyboard.MaxColorLevel * target / 100.0);
            Check(string.Format("{0} slider {1}% -> {0} LED level {2}, other channels unchanged", name, target, want),
                wait.StartsWith("OK") && Math.Abs(own(after) - want) <= 1 && other1(after) == other1(before) && other2(after) == other2(before),
                string.Format("before [{0}] after [{1}]", before, after));
        }

        static void Check(string name, string waitResult, bool extra)
        {
            Check(name, waitResult.StartsWith("OK") && extra, waitResult + (extra ? "" : " (sliders did not match preset)"));
        }

        static Func<KbState, bool> Expect(int brightness, Config c)
        {
            byte r, g, b;
            c.Levels(out r, out g, out b);
            return s => s.IsLit && s.Brightness == brightness
                && Math.Abs(s.Red - r) <= 1 && Math.Abs(s.Green - g) <= 1 && Math.Abs(s.Blue - b) <= 1;
        }

        static KbState ProbeRead()
        {
            KbState s;
            s.Status = probe.Read(Keyboard.RegStatus);
            s.Red = probe.Read(Keyboard.RegRed);
            s.Green = probe.Read(Keyboard.RegGreen);
            s.Blue = probe.Read(Keyboard.RegBlue);
            return s;
        }

        static void WriteBrightness(int level)
        {
            byte st = probe.Read(Keyboard.RegStatus);
            st = (byte)((st & 0x0D) | (level << 5) | 0x10);
            probe.Write(Keyboard.RegStatus, st);
        }

        // waits until the EC (read independently of the app) satisfies pred
        static string WaitEc(Func<KbState, bool> pred, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            KbState s = ProbeRead();
            while (!pred(s) && sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(150);
                s = ProbeRead();
            }
            return (pred(s) ? "OK" : "TIMEOUT") + string.Format(" after {0} ms, EC [{1}]", sw.ElapsedMilliseconds, s);
        }

        static void Check(string name, string waitResult)
        {
            Check(name, waitResult.StartsWith("OK"), waitResult);
        }

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) passed++;
            else failed++;
            Line((ok ? "PASS " : "FAIL ") + name + (detail != null ? "  -- " + detail : ""));
        }

        static void Line(string s)
        {
            lock (report) report.AppendLine(Log.Now() + "  " + s);
        }

        static void Ui(Action a)
        {
            app.TestInvoke(a);
        }

        static T UiGet<T>(Func<T> f)
        {
            T result = default(T);
            app.TestInvoke(() => { result = f(); });
            return result;
        }
    }
}
