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
        // Color = what each LED channel gets, 0-100% (100% = EC level 50). Sent as-is,
        // no hidden calibration. Default white is tuned for the Avell C62, where equal
        // levels look yellow because the blue LED is the weakest.
        public int MixRed = 60;
        public int MixGreen = 60;
        public int MixBlue = 100;
        public bool Enforce = true;                          // put the setting back if anything changes it
        public int EnforceIntervalSec = 2;
        public bool FollowFnKeys = true;                     // keep Fn-key brightness changes (not "off")

        // Optional battery rule (off unless the user enables it)
        public bool BatteryRule = false;
        public bool BatteryTurnOff = false;                  // false = dim to BatteryBrightness
        public int BatteryBrightness = 1;
        public int BatteryThresholdPercent = 100;            // 100 = whenever on battery

        public int IntensityPercent = 100;                   // 5-100, scales color levels (RGB keyboards)

        public bool TrayIconPromoted = false;               // tray icon made always-visible once

        public bool FixVendorSettings = true;                // neutralize Control Center battery-off + idle timer
        public bool StopVendorService = false;               // stop GCUBridge while KeyLight runs

        public Config Clone()
        {
            return (Config)MemberwiseClone();
        }

        // EC levels (0-50) for the current mix, scaled by the fine intensity
        public void Levels(out byte r, out byte g, out byte b)
        {
            r = Level(MixRed);
            g = Level(MixGreen);
            b = Level(MixBlue);
        }

        byte Level(int mixPercent)
        {
            return (byte)Math.Round(Keyboard.MaxColorLevel * mixPercent / 100.0 * IntensityPercent / 100.0);
        }

        public void SetMix(int r, int g, int b)
        {
            MixRed = Clamp(r, 0, 100);
            MixGreen = Clamp(g, 0, 100);
            MixBlue = Clamp(b, 0, 100);
        }

        public string MixText()
        {
            return string.Format("R{0}% G{1}% B{2}%", MixRed, MixGreen, MixBlue);
        }

        // screen color (0-255) -> LED mix: LEDs are much brighter at mid values than a
        // screen, so apply a 2.2 curve (otherwise orange looks yellow)
        public static int ToMix(int channel)
        {
            return (int)Math.Round(100 * Math.Pow(channel / 255.0, 2.2));
        }

        // LED mix -> approximate screen color, for the swatch
        public static Color MixToScreen(int r, int g, int b)
        {
            return Color.FromArgb(FromMix(r), FromMix(g), FromMix(b));
        }

        static int FromMix(int mix)
        {
            return (int)Math.Round(255 * Math.Pow(mix / 100.0, 1 / 2.2));
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
            if (kv.ContainsKey("MixRed"))
                c.SetMix(GetInt(kv, "MixRed", c.MixRed), GetInt(kv, "MixGreen", c.MixGreen), GetInt(kv, "MixBlue", c.MixBlue));
            else
            {
                // settings from v1.0-1.3 stored a screen color
                Color old = GetColor(kv, "Color", Color.White);
                if (old.ToArgb() != Color.White.ToArgb()) c.SetMix(ToMix(old.R), ToMix(old.G), ToMix(old.B));
            }
            c.Enforce = GetBool(kv, "Enforce", c.Enforce);
            c.EnforceIntervalSec = Clamp(GetInt(kv, "EnforceIntervalSec", c.EnforceIntervalSec), 1, 60);
            c.FollowFnKeys = GetBool(kv, "FollowFnKeys", c.FollowFnKeys);
            c.BatteryRule = GetBool(kv, "BatteryRule", c.BatteryRule);
            c.BatteryTurnOff = GetBool(kv, "BatteryTurnOff", c.BatteryTurnOff);
            c.BatteryBrightness = Clamp(GetInt(kv, "BatteryBrightness", c.BatteryBrightness), 1, Keyboard.MaxBrightness);
            c.BatteryThresholdPercent = Clamp(GetInt(kv, "BatteryThresholdPercent", c.BatteryThresholdPercent), 1, 100);
            c.FixVendorSettings = GetBool(kv, "FixVendorSettings", c.FixVendorSettings);
            c.StopVendorService = GetBool(kv, "StopVendorService", c.StopVendorService);
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
            sb.AppendLine("MixRed=" + MixRed);
            sb.AppendLine("MixGreen=" + MixGreen);
            sb.AppendLine("MixBlue=" + MixBlue);
            sb.AppendLine("Enforce=" + Enforce);
            sb.AppendLine("EnforceIntervalSec=" + EnforceIntervalSec);
            sb.AppendLine("FollowFnKeys=" + FollowFnKeys);
            sb.AppendLine("BatteryRule=" + BatteryRule);
            sb.AppendLine("BatteryTurnOff=" + BatteryTurnOff);
            sb.AppendLine("BatteryBrightness=" + BatteryBrightness);
            sb.AppendLine("BatteryThresholdPercent=" + BatteryThresholdPercent);
            sb.AppendLine("FixVendorSettings=" + FixVendorSettings);
            sb.AppendLine("StopVendorService=" + StopVendorService);
            sb.AppendLine("TrayIconPromoted=" + TrayIconPromoted);
            sb.AppendLine("IntensityPercent=" + IntensityPercent);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
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
