using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeyLight
{
    public sealed class Config
    {
        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AvellKeyLight"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(DataDir, "config.ini"); }
        }

        public bool Enabled = true;
        public int Brightness = Keyboard.MaxBrightness;     // 1-4
        public Color Color = Color.White;
        public bool Enforce = true;                          // put the setting back if anything changes it
        public int EnforceIntervalSec = 2;
        public bool FollowFnKeys = true;                     // keep Fn-key brightness changes (not "off")

        // Optional battery rule (off unless the user enables it)
        public bool BatteryRule = false;
        public bool BatteryTurnOff = false;                  // false = dim to BatteryBrightness
        public int BatteryBrightness = 1;
        public int BatteryThresholdPercent = 100;            // 100 = whenever on battery

        // Color calibration (white balance). Defaults tuned on an Avell C62, where
        // equal levels look yellow because red+green outshine blue.
        public double Gamma = 2.2;
        public int TrimRed = 60;
        public int TrimGreen = 60;
        public int TrimBlue = 100;

        public int IntensityPercent = 100;                   // 5-100, scales color levels (RGB keyboards)

        public bool TrayIconPromoted = false;               // tray icon made always-visible once

        public bool FixVendorSettings = true;                // neutralize Control Center battery-off + idle timer
        public bool StopVendorService = false;               // stop GCUBridge while KeyLight runs

        public Config Clone()
        {
            return (Config)MemberwiseClone();
        }

        public void ApplyCalibration(Keyboard kb)
        {
            kb.Gamma = Gamma;
            kb.TrimRed = TrimRed;
            kb.TrimGreen = TrimGreen;
            kb.TrimBlue = TrimBlue;
            kb.IntensityPercent = IntensityPercent;
        }

        public static Config Load()
        {
            var c = new Config();
            if (!File.Exists(FilePath)) return c;
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            c.Enabled = GetBool(kv, "Enabled", c.Enabled);
            c.Brightness = Clamp(GetInt(kv, "Brightness", c.Brightness), 1, Keyboard.MaxBrightness);
            c.Color = GetColor(kv, "Color", c.Color);
            c.Enforce = GetBool(kv, "Enforce", c.Enforce);
            c.EnforceIntervalSec = Clamp(GetInt(kv, "EnforceIntervalSec", c.EnforceIntervalSec), 1, 60);
            c.FollowFnKeys = GetBool(kv, "FollowFnKeys", c.FollowFnKeys);
            c.BatteryRule = GetBool(kv, "BatteryRule", c.BatteryRule);
            c.BatteryTurnOff = GetBool(kv, "BatteryTurnOff", c.BatteryTurnOff);
            c.BatteryBrightness = Clamp(GetInt(kv, "BatteryBrightness", c.BatteryBrightness), 1, Keyboard.MaxBrightness);
            c.BatteryThresholdPercent = Clamp(GetInt(kv, "BatteryThresholdPercent", c.BatteryThresholdPercent), 1, 100);
            c.FixVendorSettings = GetBool(kv, "FixVendorSettings", c.FixVendorSettings);
            c.StopVendorService = GetBool(kv, "StopVendorService", c.StopVendorService);
            string gs;
            double g;
            if (kv.TryGetValue("Gamma", out gs) && double.TryParse(gs, NumberStyles.Float, CultureInfo.InvariantCulture, out g) && g >= 1.0 && g <= 3.0) c.Gamma = g;
            c.TrimRed = Clamp(GetInt(kv, "TrimRed", c.TrimRed), 5, 100);
            c.TrimGreen = Clamp(GetInt(kv, "TrimGreen", c.TrimGreen), 5, 100);
            c.TrimBlue = Clamp(GetInt(kv, "TrimBlue", c.TrimBlue), 5, 100);
            c.TrayIconPromoted = GetBool(kv, "TrayIconPromoted", c.TrayIconPromoted);
            c.IntensityPercent = Clamp(GetInt(kv, "IntensityPercent", c.IntensityPercent), 5, 100);
            return c;
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDir);
            var sb = new StringBuilder();
            sb.AppendLine("# Avell KeyLight settings");
            sb.AppendLine("Enabled=" + Enabled);
            sb.AppendLine("Brightness=" + Brightness);
            sb.AppendLine("Color=" + ToHex(Color));
            sb.AppendLine("Enforce=" + Enforce);
            sb.AppendLine("EnforceIntervalSec=" + EnforceIntervalSec);
            sb.AppendLine("FollowFnKeys=" + FollowFnKeys);
            sb.AppendLine("BatteryRule=" + BatteryRule);
            sb.AppendLine("BatteryTurnOff=" + BatteryTurnOff);
            sb.AppendLine("BatteryBrightness=" + BatteryBrightness);
            sb.AppendLine("BatteryThresholdPercent=" + BatteryThresholdPercent);
            sb.AppendLine("FixVendorSettings=" + FixVendorSettings);
            sb.AppendLine("StopVendorService=" + StopVendorService);
            sb.AppendLine("Gamma=" + Gamma.ToString("0.0#", CultureInfo.InvariantCulture));
            sb.AppendLine("TrimRed=" + TrimRed);
            sb.AppendLine("TrimGreen=" + TrimGreen);
            sb.AppendLine("TrimBlue=" + TrimBlue);
            sb.AppendLine("TrayIconPromoted=" + TrayIconPromoted);
            sb.AppendLine("IntensityPercent=" + IntensityPercent);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }

        public static string ToHex(Color c)
        {
            return string.Format("{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        public static bool TryParseHex(string s, out Color c)
        {
            c = Color.White;
            s = s.TrimStart('#');
            int v;
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            c = Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            return true;
        }

        static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        static bool GetBool(Dictionary<string, string> kv, string k, bool def)
        {
            string s;
            bool b;
            return kv.TryGetValue(k, out s) && bool.TryParse(s, out b) ? b : def;
        }

        static int GetInt(Dictionary<string, string> kv, string k, int def)
        {
            string s;
            int i;
            return kv.TryGetValue(k, out s) && int.TryParse(s, out i) ? i : def;
        }

        static Color GetColor(Dictionary<string, string> kv, string k, Color def)
        {
            string s;
            Color c;
            return kv.TryGetValue(k, out s) && TryParseHex(s, out c) ? c : def;
        }
    }
}
