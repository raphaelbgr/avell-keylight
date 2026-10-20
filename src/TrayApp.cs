using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyLight
{
    public sealed class TrayApp : ApplicationContext
    {
        public const string ShowEventName = @"Global\AvellKeyLight.Show";
        public const string ReloadEventName = @"Global\AvellKeyLight.Reload";

        readonly Control ui = new Control();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Ec ec;
        public readonly Keyboard Kb;
        public readonly Enforcer Enforcer;
        public Config Cfg;
        SettingsForm form;
        readonly EventWaitHandle showEvent, reloadEvent;
        readonly RegisteredWaitHandle showWait, reloadWait;

        public TrayApp(bool showWindow)
        {
            ui.CreateControl();
            Cfg = Config.Load();
            ec = new Ec();
            Kb = new Keyboard(ec);
            Log.Info("start: keyboard type {0}, initial [{1}]", Kb.IsRgb ? "1-zone RGB" : "single color", Kb.Read());

            if (Cfg.FixVendorSettings) Log.Info(TryRun(Vendor.FixSettings));
            if (Cfg.StopVendorService) Log.Info(Vendor.StopService());

            Enforcer = new Enforcer(Kb, Cfg, ui);
            Enforcer.ApplyNow("startup");

            tray.Icon = MakeIcon();
            tray.Text = "Avell KeyLight";
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.ContextMenuStrip = BuildMenu();

            if (!Cfg.TrayIconPromoted)
            {
                // Windows registers the icon a moment after it is first shown
                var once = new System.Windows.Forms.Timer { Interval = 3000 };
                once.Tick += delegate
                {
                    once.Dispose();
                    if (PromoteTrayIcon())
                    {
                        Config c = Cfg.Clone();
                        c.TrayIconPromoted = true;
                        Cfg = c;
                        Cfg.Save();
                    }
                };
                once.Start();
            }

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            reloadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ReloadEventName);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { ui.BeginInvoke((MethodInvoker)ShowSettings); }, null, -1, false);
            reloadWait = ThreadPool.RegisterWaitForSingleObject(reloadEvent, delegate { ui.BeginInvoke((MethodInvoker)ReloadFromDisk); }, null, -1, false);

            if (showWindow) ShowSettings();
        }

        // Windows 11 hides new tray icons in the overflow (^). This marks ours as shown
        // on the taskbar, once; the user can still hide it again in Settings.
        static bool PromoteTrayIcon()
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
                {
                    if (root == null) return false;
                    foreach (string id in root.GetSubKeyNames())
                    {
                        using (RegistryKey k = root.OpenSubKey(id, true))
                        {
                            string path = k == null ? null : k.GetValue("ExecutablePath") as string;
                            if (path == null || !path.EndsWith("AvellKeyLight.exe", StringComparison.OrdinalIgnoreCase)) continue;
                            k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                            Log.Info("tray icon promoted to always visible ({0})", path);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Info("tray icon promote failed: {0}", ex.Message);
            }
            return false;
        }

        static string TryRun(Func<string> f)
        {
            try { return f(); }
            catch (Exception ex) { return "error: " + ex.Message; }
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Settings...", null, delegate { ShowSettings(); });
            m.Items.Add("Backlight on / off", null, delegate
            {
                Config c = Cfg.Clone();
                c.Enabled = !c.Enabled;
                Update(c);
            });
            var bright = new ToolStripMenuItem("Brightness");
            for (int i = 1; i <= Keyboard.MaxBrightness; i++)
            {
                int level = i;
                bright.DropDownItems.Add(level.ToString(), null, delegate
                {
                    Config c = Cfg.Clone();
                    c.Enabled = true;
                    c.Brightness = level;
                    Update(c);
                });
            }
            m.Items.Add(bright);
            m.Items.Add("Re-apply now", null, delegate { Enforcer.ApplyNow("manual"); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit KeyLight", null, delegate { ExitThread(); });
            return m;
        }

        public void Update(Config c)
        {
            bool stopChanged = c.StopVendorService != Cfg.StopVendorService;
            bool fixChanged = c.FixVendorSettings != Cfg.FixVendorSettings;
            Cfg = c;
            Cfg.Save();
            if (fixChanged) Log.Info(TryRun(c.FixVendorSettings ? (Func<string>)Vendor.FixSettings : Vendor.RestoreSettings));
            if (stopChanged) Log.Info(c.StopVendorService ? Vendor.StopService() : Vendor.StartService());
            Enforcer.UpdateConfig(Cfg);
            if (form != null && !form.IsDisposed) form.LoadFrom(Cfg);
        }

        void ReloadFromDisk()
        {
            Log.Info("reload requested");
            Update(Config.Load());
        }

        public void Exit()
        {
            ExitThread();
        }

        void ShowSettings()
        {
            if (form == null || form.IsDisposed) form = new SettingsForm(this);
            form.LoadFrom(Cfg);
            form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        protected override void ExitThreadCore()
        {
            Log.Info("exit");
            if (Cfg.StopVendorService) Log.Info(Vendor.StartService());
            showWait.Unregister(null);
            reloadWait.Unregister(null);
            tray.Visible = false;
            tray.Dispose();
            Enforcer.Dispose();
            ec.Dispose();
            if (form != null) form.Dispose();
            base.ExitThreadCore();
        }

        public static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var body = new SolidBrush(Color.FromArgb(40, 44, 52)))
                        g.FillRectangle(body, 1, 7, 30, 19);
                    using (var key = new SolidBrush(Color.FromArgb(120, 220, 255)))
                    {
                        for (int row = 0; row < 3; row++)
                            for (int col = 0; col < 6; col++)
                                g.FillRectangle(key, 3 + col * 4.6f, 9 + row * 5, 3.4f, 3.4f);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
