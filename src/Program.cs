using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KeyLight
{
    static class Program
    {
        const string Usage =
@"Avell KeyLight - keyboard backlight control for Avell / Uniwill laptops

  AvellKeyLight.exe                      open settings (keeps running in the tray)
  AvellKeyLight.exe --tray               start hidden in the tray (used at logon)
  AvellKeyLight.exe status               show keyboard registers and settings
  AvellKeyLight.exe set [on|off] [brightness=1-4] [rgb=R,G,B] [color=RRGGBB]
                                          save and apply a setting
  AvellKeyLight.exe autostart on|off     start with Windows
  AvellKeyLight.exe vendor fix|restore   neutralize / restore Control Center battery-off + idle timer
  AvellKeyLight.exe dump                 read-only dump of EC registers 0x0740-0x07FF
  AvellKeyLight.exe selftest             end-to-end test of the window + keyboard (exit KeyLight first)

  --out <file>                           also write command output to a file";

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        static readonly StringBuilder output = new StringBuilder();
        static string selfTestOut;

        [STAThread]
        static int Main(string[] args)
        {
            var list = new List<string>(args);
            string outFile = null;
            int oi = list.IndexOf("--out");
            if (oi >= 0 && oi + 1 < list.Count)
            {
                outFile = list[oi + 1];
                list.RemoveRange(oi, 2);
            }

            bool tray = list.Remove("--tray");
            if (list.Count == 0) return RunTray(!tray);

            AttachConsole(-1);
            selfTestOut = outFile == null ? null : outFile + ".report.txt";
            int rc;
            try
            {
                rc = RunCommand(list);
            }
            catch (Exception ex)
            {
                Out("error: " + ex.Message);
                rc = 1;
            }
            if (outFile != null) File.WriteAllText(outFile, output.ToString(), Encoding.UTF8);
            return rc;
        }

        static int RunTray(bool showWindow)
        {
            bool created;
            using (var mutex = new Mutex(true, @"Global\AvellKeyLight.Instance", out created))
            {
                if (!created)
                {
                    // already running: ask it to show its window
                    EventWaitHandle ev;
                    if (showWindow && EventWaitHandle.TryOpenExisting(TrayApp.ShowEventName, out ev))
                        using (ev) ev.Set();
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                TrayApp app;
                try
                {
                    app = new TrayApp(showWindow);
                }
                catch (Exception ex)
                {
                    Log.Info("startup failed: {0}", ex);
                    MessageBox.Show(ex.Message, "Avell KeyLight", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                Application.Run(app);
                return 0;
            }
        }

        static int RunCommand(List<string> a)
        {
            string cmd = a[0].ToLowerInvariant();
            if (cmd == "help" || cmd == "-h" || cmd == "--help" || cmd == "/?")
            {
                Out(Usage);
                return 0;
            }
            if (cmd == "selftest")
            {
                // end-to-end test of the real window + EC; KeyLight must not be running
                bool created;
                using (var mutex = new Mutex(true, @"Global\AvellKeyLight.Instance", out created))
                {
                    if (!created)
                    {
                        Out("selftest: exit the running KeyLight first");
                        return 1;
                    }
                    string report = selfTestOut ?? Path.Combine(Log.Dir, "selftest.txt");
                    int rc = SelfTest.Run(report, Path.ChangeExtension(report, ".png"));
                    Out(File.ReadAllText(report));
                    return rc;
                }
            }
            if (cmd == "autostart" && a.Count == 2)
            {
                if (a[1] == "on") Out("autostart enabled: " + Autostart.Enable());
                else { Autostart.Disable(); Out("autostart disabled"); }
                return 0;
            }
            if (cmd == "vendor" && a.Count == 2)
            {
                Out(a[1] == "restore" ? Vendor.RestoreSettings() : Vendor.FixSettings());
                return 0;
            }

            using (var ec = new Ec())
            {
                var kb = new Keyboard(ec);
                if (cmd == "status")
                {
                    Config c = Config.Load();
                    int pct;
                    bool batt = Enforcer.OnBattery(out pct);
                    Out("keyboard type : " + (kb.IsRgb ? "1-zone RGB" : "single color"));
                    Out("keyboard now  : " + kb.Read());
                    Out(string.Format("settings      : enabled={0} brightness={1} color={2} enforce={3}/{4}s", c.Enabled, c.Brightness, c.MixText(), c.Enforce, c.EnforceIntervalSec));
                    Out(string.Format("battery rule  : enabled={0} action={1} threshold={2}%", c.BatteryRule, c.BatteryTurnOff ? "off" : "dim to " + c.BatteryBrightness, c.BatteryThresholdPercent));
                    Out(string.Format("power         : {0}, {1}%", batt ? "battery" : "plugged in", pct));
                    Out("autostart     : " + Autostart.IsEnabled());
                    Out("vendor service: " + Vendor.ServiceStatus());
                    return 0;
                }
                if (cmd == "dump")
                {
                    for (int row = 0x0740; row < 0x0800; row += 16)
                    {
                        var sb = new StringBuilder(string.Format("0x{0:X4}:", row));
                        for (int i = 0; i < 16; i++) sb.AppendFormat(" {0:X2}", ec.Read((ushort)(row + i)));
                        Out(sb.ToString());
                    }
                    return 0;
                }
                if (cmd == "set")
                {
                    Config c = Config.Load();
                    for (int i = 1; i < a.Count; i++)
                    {
                        string p = a[i].ToLowerInvariant();
                        int n;
                        Color col;
                        if (p == "on") c.Enabled = true;
                        else if (p == "off") c.Enabled = false;
                        else if (p.StartsWith("brightness=") && int.TryParse(p.Substring(11), out n) && n >= 1 && n <= Keyboard.MaxBrightness) c.Brightness = n;
                        else if (p.StartsWith("color=") && Config.TryParseHex(p.Substring(6), out col)) c.SetMix(Config.ToMix(col.R), Config.ToMix(col.G), Config.ToMix(col.B));
                        else if (p.StartsWith("rgb=") && TryParseMix(p.Substring(4), c)) { }
                        else throw new ArgumentException("bad argument: " + a[i]);
                    }
                    c.Save();
                    KbState before = kb.Read();
                    byte r, g, b;
                    c.Levels(out r, out g, out b);
                    kb.Apply(c.Enabled ? c.Brightness : 0, r, g, b, before);
                    Out("before: " + before);
                    Out("after : " + kb.Read());
                    Log.Info("cli set: before [{0}] after [{1}]", before, kb.Read());
                    // tell a running tray instance to pick up the new settings
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting(TrayApp.ReloadEventName, out ev))
                        using (ev) ev.Set();
                    return 0;
                }
            }
            Out(Usage);
            return 2;
        }

        // "60,60,100" -> mix percentages
        static bool TryParseMix(string s, Config c)
        {
            string[] p = s.Split(',');
            int r, g, b;
            if (p.Length != 3 || !int.TryParse(p[0], out r) || !int.TryParse(p[1], out g) || !int.TryParse(p[2], out b)) return false;
            c.SetMix(r, g, b);
            return true;
        }

        static void Out(string s)
        {
            output.AppendLine(s);
            Console.WriteLine(s);
        }
    }
}
