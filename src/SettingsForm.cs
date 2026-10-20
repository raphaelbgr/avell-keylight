using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyLight
{
    public sealed class SettingsForm : Form
    {
        readonly TrayApp app;
        bool loading;

        readonly CheckBox chkOn = new CheckBox { Text = "Backlight on", AutoSize = true };
        readonly TrackBar trkBright = new TrackBar { Minimum = 1, Maximum = Keyboard.MaxBrightness, Width = 220, TickStyle = TickStyle.BottomRight };
        readonly Label lblBright = new Label { AutoSize = true };
        readonly Panel swatch = new Panel { Width = 60, Height = 26, BorderStyle = BorderStyle.FixedSingle };
        readonly Button btnColor = new Button { Text = "Pick color...", AutoSize = true };
        readonly CheckBox chkEnforce = new CheckBox { Text = "Keep my setting (re-apply if anything changes it)", AutoSize = true };
        readonly NumericUpDown numInterval = new NumericUpDown { Minimum = 1, Maximum = 60, Width = 50 };

        readonly CheckBox chkBatt = new CheckBox { Text = "Enable battery rule (off by default)", AutoSize = true };
        readonly RadioButton rbDim = new RadioButton { Text = "Dim to level", AutoSize = true };
        readonly NumericUpDown numBattBright = new NumericUpDown { Minimum = 1, Maximum = Keyboard.MaxBrightness, Width = 45 };
        readonly RadioButton rbOff = new RadioButton { Text = "Turn off", AutoSize = true };
        readonly NumericUpDown numThreshold = new NumericUpDown { Minimum = 1, Maximum = 100, Width = 55 };

        readonly CheckBox chkAutostart = new CheckBox { Text = "Start with Windows (applies at logon, no UAC prompt)", AutoSize = true };
        readonly CheckBox chkFixVendor = new CheckBox { Text = "Neutralize Avell Control Center battery-off and idle timer", AutoSize = true };
        readonly CheckBox chkStopVendor = new CheckBox { Text = "Stop Avell Control Center service while KeyLight runs", AutoSize = true };

        readonly Label lblStatus = new Label { AutoSize = true, MaximumSize = new Size(470, 0) };

        static readonly Color[] Presets =
        {
            Color.White, Color.FromArgb(0, 0, 255), Color.Cyan, Color.Lime, Color.Yellow,
            Color.FromArgb(255, 128, 0), Color.Red, Color.FromArgb(160, 0, 255), Color.FromArgb(255, 0, 160)
        };

        public SettingsForm(TrayApp app)
        {
            this.app = app;
            Text = "Avell KeyLight";
            Icon = TrayApp.MakeIcon();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(10), WrapContents = false };

            // Light
            var gLight = Group("Keyboard light");
            gLight.Controls.Add(chkOn);
            gLight.Controls.Add(Row(new Label { Text = "Brightness", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, trkBright, lblBright));
            var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            foreach (Color c in Presets)
            {
                Color pc = c;
                var b = new Button { Width = 26, Height = 26, BackColor = pc, FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
                b.Click += delegate { SetColor(pc); };
                presets.Controls.Add(b);
            }
            gLight.Controls.Add(Row(new Label { Text = "Color", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, swatch, btnColor));
            gLight.Controls.Add(presets);
            if (!app.Kb.IsRgb) { btnColor.Enabled = false; presets.Enabled = false; }
            gLight.Controls.Add(chkEnforce);
            gLight.Controls.Add(Row(new Label { Text = "Check every", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, numInterval, new Label { Text = "seconds", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }));
            root.Controls.Add(gLight.Parent);

            // Battery rule
            var gBatt = Group("Battery (optional)");
            gBatt.Controls.Add(chkBatt);
            gBatt.Controls.Add(Row(new Label { Text = "On battery:", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, rbDim, numBattBright, rbOff));
            gBatt.Controls.Add(Row(new Label { Text = "Only when charge is at or below", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, numThreshold, new Label { Text = "% (100 = always on battery)", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }));
            root.Controls.Add(gBatt.Parent);

            // System
            var gSys = Group("System");
            gSys.Controls.Add(chkAutostart);
            gSys.Controls.Add(chkFixVendor);
            gSys.Controls.Add(chkStopVendor);
            root.Controls.Add(gSys.Parent);

            var gStat = Group("Status");
            gStat.Controls.Add(lblStatus);
            root.Controls.Add(gStat.Parent);

            Controls.Add(root);

            EventHandler changed = delegate { OnChanged(); };
            chkOn.CheckedChanged += changed;
            trkBright.ValueChanged += changed;
            chkEnforce.CheckedChanged += changed;
            numInterval.ValueChanged += changed;
            chkBatt.CheckedChanged += changed;
            rbDim.CheckedChanged += changed;
            rbOff.CheckedChanged += changed;
            numBattBright.ValueChanged += changed;
            numThreshold.ValueChanged += changed;
            chkFixVendor.CheckedChanged += changed;
            chkStopVendor.CheckedChanged += changed;
            chkAutostart.CheckedChanged += delegate { OnAutostart(); };
            btnColor.Click += delegate
            {
                using (var dlg = new ColorDialog { Color = swatch.BackColor, FullOpen = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK) SetColor(dlg.Color);
            };

            app.Enforcer.Changed += OnEnforcerChanged;
            FormClosed += delegate { app.Enforcer.Changed -= OnEnforcerChanged; };
            RefreshStatus();
        }

        static FlowLayoutPanel Group(string title)
        {
            var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(490, 0), Padding = new Padding(8) };
            var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            box.Controls.Add(inner);
            return inner;
        }

        static FlowLayoutPanel Row(params Control[] items)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.AddRange(items);
            return row;
        }

        public void LoadFrom(Config c)
        {
            loading = true;
            chkOn.Checked = c.Enabled;
            trkBright.Value = c.Brightness;
            lblBright.Text = c.Brightness + " / " + Keyboard.MaxBrightness;
            swatch.BackColor = c.Color;
            chkEnforce.Checked = c.Enforce;
            numInterval.Value = c.EnforceIntervalSec;
            chkBatt.Checked = c.BatteryRule;
            rbOff.Checked = c.BatteryTurnOff;
            rbDim.Checked = !c.BatteryTurnOff;
            numBattBright.Value = c.BatteryBrightness;
            numThreshold.Value = c.BatteryThresholdPercent;
            chkFixVendor.Checked = c.FixVendorSettings;
            chkStopVendor.Checked = c.StopVendorService;
            chkAutostart.Checked = Autostart.IsEnabled();
            UpdateEnabledState();
            loading = false;
        }

        void UpdateEnabledState()
        {
            rbDim.Enabled = rbOff.Enabled = numThreshold.Enabled = chkBatt.Checked;
            numBattBright.Enabled = chkBatt.Checked && rbDim.Checked;
            numInterval.Enabled = chkEnforce.Checked;
        }

        void SetColor(Color c)
        {
            swatch.BackColor = c;
            OnChanged();
        }

        void OnChanged()
        {
            if (loading) return;
            UpdateEnabledState();
            lblBright.Text = trkBright.Value + " / " + Keyboard.MaxBrightness;
            Config c = app.Cfg.Clone();
            c.Enabled = chkOn.Checked;
            c.Brightness = trkBright.Value;
            c.Color = Color.FromArgb(swatch.BackColor.R, swatch.BackColor.G, swatch.BackColor.B);
            c.Enforce = chkEnforce.Checked;
            c.EnforceIntervalSec = (int)numInterval.Value;
            c.BatteryRule = chkBatt.Checked;
            c.BatteryTurnOff = rbOff.Checked;
            c.BatteryBrightness = (int)numBattBright.Value;
            c.BatteryThresholdPercent = (int)numThreshold.Value;
            c.FixVendorSettings = chkFixVendor.Checked;
            c.StopVendorService = chkStopVendor.Checked;
            app.Update(c);
        }

        void OnAutostart()
        {
            if (loading) return;
            try
            {
                if (chkAutostart.Checked) Autostart.Enable();
                else Autostart.Disable();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Start with Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            loading = true;
            chkAutostart.Checked = Autostart.IsEnabled();
            loading = false;
        }

        void OnEnforcerChanged(object sender, EventArgs e)
        {
            if (!IsDisposed && Visible) RefreshStatus();
        }

        void RefreshStatus()
        {
            string why;
            int target = app.Enforcer.TargetBrightness(out why);
            int pct;
            bool batt = Enforcer.OnBattery(out pct);
            KbState s = app.Enforcer.Current;
            lblStatus.Text = string.Format(
                "Keyboard: {0} (brightness {1}/{2})\r\nTarget: {3} ({4})\r\nPower: {5}, battery {6}%\r\nRe-applied {7} time(s). {8}\r\nAvell service: {9}\r\nLogs: {10}",
                s.IsLit ? "lit" : "off", s.Brightness, Keyboard.MaxBrightness, target, why,
                batt ? "on battery" : "plugged in", pct, app.Enforcer.Reapplies, app.Enforcer.LastEvent,
                Vendor.ServiceStatus(), Log.Dir);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // closing the window keeps KeyLight running in the tray
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
