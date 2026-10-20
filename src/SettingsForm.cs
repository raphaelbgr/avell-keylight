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
        readonly TrackBar trkIntensity = new TrackBar { Minimum = 5, Maximum = 100, TickFrequency = 5, SmallChange = 1, LargeChange = 5, Width = 220 };
        readonly Label lblIntensity = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
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

        readonly TrackBar trkRed = new TrackBar { Minimum = 5, Maximum = 100, TickFrequency = 10, Width = 260 };
        readonly TrackBar trkGreen = new TrackBar { Minimum = 5, Maximum = 100, TickFrequency = 10, Width = 260 };
        readonly TrackBar trkBlue = new TrackBar { Minimum = 5, Maximum = 100, TickFrequency = 10, Width = 260 };
        readonly Label lblRed = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        readonly Label lblGreen = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        readonly Label lblBlue = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        readonly NumericUpDown numGamma = new NumericUpDown { Minimum = 1.0m, Maximum = 3.0m, Increment = 0.1m, DecimalPlaces = 1, Width = 55 };

        readonly Label lblStatus = new Label { AutoSize = true, MaximumSize = new Size(470, 0) };

        static readonly Color[] Presets =
        {
            Color.White, Color.FromArgb(0, 0, 255), Color.Cyan, Color.Lime, Color.Yellow,
            Color.FromArgb(255, 128, 0), Color.Red, Color.FromArgb(160, 0, 255), Color.FromArgb(255, 0, 160)
        };

        readonly System.Collections.Generic.List<Button> presetButtons = new System.Collections.Generic.List<Button>();
        Button btnHideTray;

        // self-test access to the real controls
        internal TrackBar TestBrightness { get { return trkBright; } }
        internal TrackBar TestIntensity { get { return trkIntensity; } }
        internal TrackBar TestTrimRed { get { return trkRed; } }
        internal CheckBox TestOn { get { return chkOn; } }
        internal Button[] TestPresets { get { return presetButtons.ToArray(); } }
        internal Button TestHide { get { return btnHideTray; } }
        internal static Color[] TestPresetColors { get { return Presets; } }

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
            gLight.Controls.Add(Row(Fixed("Brightness", 75), trkBright, lblBright));
            gLight.Controls.Add(Row(Fixed("Intensity", 75), trkIntensity, lblIntensity));
            if (!app.Enforcer.IsRgb) trkIntensity.Enabled = false;
            var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            foreach (Color c in Presets)
            {
                Color pc = c;
                var b = new Button { Width = 26, Height = 26, BackColor = pc, FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
                b.Click += delegate { SetColor(pc); };
                presets.Controls.Add(b);
                presetButtons.Add(b);
            }
            gLight.Controls.Add(Row(new Label { Text = "Color", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, swatch, btnColor));
            gLight.Controls.Add(presets);
            if (!app.Enforcer.IsRgb) { btnColor.Enabled = false; presets.Enabled = false; }
            gLight.Controls.Add(chkEnforce);
            gLight.Controls.Add(Row(new Label { Text = "Check every", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, numInterval, new Label { Text = "seconds", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }));
            root.Controls.Add(gLight.Parent);

            // Color calibration
            var gCal = Group("Color calibration (pick white, then lower the channel that is too strong)");
            gCal.Controls.Add(Row(Fixed("Red", 50), trkRed, lblRed));
            gCal.Controls.Add(Row(Fixed("Green", 50), trkGreen, lblGreen));
            gCal.Controls.Add(Row(Fixed("Blue", 50), trkBlue, lblBlue));
            var btnWhite = new Button { Text = "Test white", AutoSize = true };
            var btnReset = new Button { Text = "Reset calibration", AutoSize = true };
            btnWhite.Click += delegate { SetColor(Color.White); };
            btnReset.Click += delegate
            {
                var d = new Config();
                loading = true;
                trkRed.Value = d.TrimRed;
                trkGreen.Value = d.TrimGreen;
                trkBlue.Value = d.TrimBlue;
                numGamma.Value = (decimal)d.Gamma;
                loading = false;
                OnChanged();
            };
            gCal.Controls.Add(Row(new Label { Text = "Mid-tone curve (gamma)", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, numGamma, btnWhite, btnReset));
            if (!app.Enforcer.IsRgb) gCal.Enabled = false;
            root.Controls.Add(gCal.Parent);

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

            var btnHide = btnHideTray = new Button { Text = "Hide to tray", AutoSize = true };
            var btnExit = new Button { Text = "Exit KeyLight", AutoSize = true };
            btnHide.Click += delegate { Hide(); };
            btnExit.Click += delegate { app.Exit(); };
            root.Controls.Add(Row(btnHide, btnExit));

            Controls.Add(root);

            // minimizing also goes to the tray
            Resize += delegate { if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; } };

            EventHandler changed = delegate { OnChanged(); };
            chkOn.CheckedChanged += changed;
            trkBright.ValueChanged += changed;
            trkIntensity.ValueChanged += changed;
            chkEnforce.CheckedChanged += changed;
            numInterval.ValueChanged += changed;
            chkBatt.CheckedChanged += changed;
            rbDim.CheckedChanged += changed;
            rbOff.CheckedChanged += changed;
            numBattBright.ValueChanged += changed;
            numThreshold.ValueChanged += changed;
            chkFixVendor.CheckedChanged += changed;
            chkStopVendor.CheckedChanged += changed;
            trkRed.ValueChanged += changed;
            trkGreen.ValueChanged += changed;
            trkBlue.ValueChanged += changed;
            numGamma.ValueChanged += changed;
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

        static Label Fixed(string text, int width)
        {
            return new Label { Text = text, AutoSize = false, Width = width, Height = 20, Margin = new Padding(0, 8, 0, 0) };
        }

        void UpdateTrimLabels()
        {
            lblRed.Text = trkRed.Value + "%";
            lblGreen.Text = trkGreen.Value + "%";
            lblBlue.Text = trkBlue.Value + "%";
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
            trkIntensity.Value = c.IntensityPercent;
            lblIntensity.Text = c.IntensityPercent + "%";
            trkRed.Value = c.TrimRed;
            trkGreen.Value = c.TrimGreen;
            trkBlue.Value = c.TrimBlue;
            numGamma.Value = (decimal)c.Gamma;
            UpdateTrimLabels();
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
            c.TrimRed = trkRed.Value;
            c.TrimGreen = trkGreen.Value;
            c.TrimBlue = trkBlue.Value;
            c.Gamma = (double)numGamma.Value;
            c.IntensityPercent = trkIntensity.Value;
            lblIntensity.Text = trkIntensity.Value + "%";
            UpdateTrimLabels();
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
