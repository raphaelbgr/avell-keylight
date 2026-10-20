using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KeyLight
{
    public sealed class SettingsForm : Form
    {
        readonly TrayApp app;
        bool loading;

        readonly CheckBox chkOn = new CheckBox { Text = "Backlight on", AutoSize = true };
        readonly TrackBar trkBright = new TrackBar { Minimum = 1, Maximum = Keyboard.MaxBrightness, Width = 260, TickStyle = TickStyle.BottomRight };
        readonly Label lblBright = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        readonly TrackBar trkIntensity = new TrackBar { Minimum = 5, Maximum = 100, TickFrequency = 5, SmallChange = 1, LargeChange = 5, Width = 260 };
        readonly Label lblIntensity = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        readonly CheckBox chkEnforce = new CheckBox { Text = "Keep my setting (re-apply if anything changes it)", AutoSize = true };
        readonly NumericUpDown numInterval = new NumericUpDown { Minimum = 1, Maximum = 60, Width = 50 };

        // color = the level each LED channel gets, sent as-is
        readonly TrackBar trkRed = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 5, Width = 260 };
        readonly TrackBar trkGreen = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 5, Width = 260 };
        readonly TrackBar trkBlue = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 5, Width = 260 };
        readonly Label lblRed = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        readonly Label lblGreen = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        readonly Label lblBlue = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        readonly Panel swatch = new Panel { Width = 60, Height = 26, BorderStyle = BorderStyle.FixedSingle };
        readonly Button btnColor = new Button { Text = "Pick color...", AutoSize = true };

        readonly CheckBox chkBatt = new CheckBox { Text = "Enable battery rule (off by default)", AutoSize = true };
        readonly RadioButton rbDim = new RadioButton { Text = "Dim to level", AutoSize = true };
        readonly NumericUpDown numBattBright = new NumericUpDown { Minimum = 1, Maximum = Keyboard.MaxBrightness, Width = 45 };
        readonly RadioButton rbOff = new RadioButton { Text = "Turn off", AutoSize = true };
        readonly NumericUpDown numThreshold = new NumericUpDown { Minimum = 1, Maximum = 100, Width = 55 };

        readonly CheckBox chkAutostart = new CheckBox { Text = "Start with Windows (applies at logon, no UAC prompt)", AutoSize = true };
        readonly CheckBox chkFixVendor = new CheckBox { Text = "Neutralize Avell Control Center battery-off and idle timer", AutoSize = true };
        readonly CheckBox chkStopVendor = new CheckBox { Text = "Stop Avell Control Center service while KeyLight runs", AutoSize = true };

        readonly Label lblStatus = new Label { AutoSize = true, MaximumSize = new Size(470, 0) };

        // LED mixes (R, G, B in %) tuned on the Avell C62: the blue LED is the weakest,
        // so white needs less red and green
        static readonly int[][] Presets =
        {
            new[] { 60, 60, 100 },  // white
            new[] { 0, 0, 100 },    // blue
            new[] { 0, 60, 100 },   // cyan
            new[] { 0, 100, 0 },    // green
            new[] { 100, 100, 0 },  // yellow
            new[] { 100, 22, 0 },   // orange
            new[] { 100, 0, 0 },    // red
            new[] { 22, 0, 100 },   // purple
            new[] { 100, 0, 60 }    // pink
        };

        readonly List<Button> presetButtons = new List<Button>();
        Button btnHideTray;

        // self-test access to the real controls
        internal TrackBar TestBrightness { get { return trkBright; } }
        internal TrackBar TestIntensity { get { return trkIntensity; } }
        internal TrackBar TestRed { get { return trkRed; } }
        internal TrackBar TestGreen { get { return trkGreen; } }
        internal TrackBar TestBlue { get { return trkBlue; } }
        internal CheckBox TestOn { get { return chkOn; } }
        internal Button[] TestPresets { get { return presetButtons.ToArray(); } }
        internal Button TestHide { get { return btnHideTray; } }
        internal static int[][] TestPresetMixes { get { return Presets; } }

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
            gLight.Controls.Add(chkEnforce);
            gLight.Controls.Add(Row(new Label { Text = "Check every", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, numInterval, new Label { Text = "seconds", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }));
            root.Controls.Add(gLight.Parent);

            // Color
            var gColor = Group("Color (each slider = how strong that LED is)");
            gColor.Controls.Add(Row(Fixed("Red", 75), trkRed, lblRed));
            gColor.Controls.Add(Row(Fixed("Green", 75), trkGreen, lblGreen));
            gColor.Controls.Add(Row(Fixed("Blue", 75), trkBlue, lblBlue));
            var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            foreach (int[] p in Presets)
            {
                int[] mix = p;
                var b = new Button { Width = 26, Height = 26, BackColor = Config.MixToScreen(mix[0], mix[1], mix[2]), FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
                b.Click += delegate { SetMix(mix[0], mix[1], mix[2]); };
                presets.Controls.Add(b);
                presetButtons.Add(b);
            }
            gColor.Controls.Add(Row(Fixed("Presets", 75), presets));
            gColor.Controls.Add(Row(Fixed("Preview", 75), swatch, btnColor));
            if (!app.Enforcer.IsRgb)
            {
                gColor.Enabled = false;
                trkIntensity.Enabled = false;
            }
            root.Controls.Add(gColor.Parent);

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
            trkRed.ValueChanged += changed;
            trkGreen.ValueChanged += changed;
            trkBlue.ValueChanged += changed;
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
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        SetMix(Config.ToMix(dlg.Color.R), Config.ToMix(dlg.Color.G), Config.ToMix(dlg.Color.B));
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

        static FlowLayoutPanel Row(params Control[] items)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.AddRange(items);
            return row;
        }

        void UpdateLabels()
        {
            lblBright.Text = trkBright.Value + " / " + Keyboard.MaxBrightness;
            lblIntensity.Text = trkIntensity.Value + "%";
            lblRed.Text = trkRed.Value + "%";
            lblGreen.Text = trkGreen.Value + "%";
            lblBlue.Text = trkBlue.Value + "%";
            swatch.BackColor = Config.MixToScreen(trkRed.Value, trkGreen.Value, trkBlue.Value);
        }

        public void LoadFrom(Config c)
        {
            loading = true;
            chkOn.Checked = c.Enabled;
            trkBright.Value = c.Brightness;
            trkIntensity.Value = c.IntensityPercent;
            trkRed.Value = c.MixRed;
            trkGreen.Value = c.MixGreen;
            trkBlue.Value = c.MixBlue;
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
            UpdateLabels();
            UpdateEnabledState();
            loading = false;
        }

        void UpdateEnabledState()
        {
            rbDim.Enabled = rbOff.Enabled = numThreshold.Enabled = chkBatt.Checked;
            numBattBright.Enabled = chkBatt.Checked && rbDim.Checked;
            numInterval.Enabled = chkEnforce.Checked;
        }

        void SetMix(int r, int g, int b)
        {
            // move all three sliders, then apply once
            loading = true;
            trkRed.Value = r;
            trkGreen.Value = g;
            trkBlue.Value = b;
            loading = false;
            OnChanged();
        }

        void OnChanged()
        {
            if (loading) return;
            UpdateEnabledState();
            UpdateLabels();
            Config c = app.Cfg.Clone();
            c.Enabled = chkOn.Checked;
            c.Brightness = trkBright.Value;
            c.IntensityPercent = trkIntensity.Value;
            c.SetMix(trkRed.Value, trkGreen.Value, trkBlue.Value);
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
                "Keyboard: {0} (brightness {1}/{2}, LED levels R{3} G{4} B{5} of {6})\r\nTarget: {7} ({8})\r\nPower: {9}, battery {10}%\r\nRe-applied {11} time(s). {12}\r\nAvell service: {13}\r\nLogs: {14}",
                s.IsLit ? "lit" : "off", s.Brightness, Keyboard.MaxBrightness, s.Red, s.Green, s.Blue, Keyboard.MaxColorLevel,
                target, why, batt ? "on battery" : "plugged in", pct, app.Enforcer.Reapplies, app.Enforcer.LastEvent,
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
