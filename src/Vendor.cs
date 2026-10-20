using System;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace KeyLight
{
    // Neutralizes the parts of the vendor "Control Center" (Uniwill GCUService) that
    // switch the keyboard off: the battery brightness (RGBKeyboard\DCLight, 0 = off on
    // battery) and the idle auto-off timer (MySetting\CloseTimer).
    public static class Vendor
    {
        const string KeyRgb = @"SOFTWARE\OEM\GamingCenter2\RGBKeyboard";
        const string KeySetting = @"SOFTWARE\OEM\GamingCenter2\MySetting";
        const string ServiceName = "GCUBridge";

        static string BackupPath
        {
            get { return Path.Combine(Config.DataDir, "vendor-backup.ini"); }
        }

        public static bool Present()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(KeyRgb))
                return k != null;
        }

        public static string FixSettings()
        {
            if (!Present()) return "Control Center not installed";
            using (RegistryKey rgb = Registry.LocalMachine.OpenSubKey(KeyRgb, true))
            using (RegistryKey set = Registry.LocalMachine.OpenSubKey(KeySetting, true))
            {
                object ac = rgb.GetValue("ACLight");
                object dc = rgb.GetValue("DCLight");
                object timer = set != null ? set.GetValue("CloseTimer") : null;

                if (!File.Exists(BackupPath))
                {
                    Directory.CreateDirectory(Config.DataDir);
                    File.WriteAllText(BackupPath, string.Format("DCLight={0}\r\nCloseTimer={1}\r\n", dc, timer));
                }

                int acLevel = ac is int && (int)ac > 0 ? (int)ac : Keyboard.MaxBrightness;
                bool changed = false;
                if (!(dc is int) || (int)dc != acLevel)
                {
                    rgb.SetValue("DCLight", acLevel, RegistryValueKind.DWord);
                    changed = true;
                }
                if (set != null && (!(timer is int) || (int)timer != 0))
                {
                    set.SetValue("CloseTimer", 0, RegistryValueKind.DWord);
                    changed = true;
                }
                return changed
                    ? string.Format("vendor settings fixed: DCLight {0} -> {1}, CloseTimer {2} -> 0", dc, acLevel, timer)
                    : "vendor settings already OK";
            }
        }

        public static string RestoreSettings()
        {
            if (!File.Exists(BackupPath)) return "no backup found";
            int dc = -1, timer = -1;
            foreach (string line in File.ReadAllLines(BackupPath))
            {
                string[] kv = line.Split('=');
                int v;
                if (kv.Length != 2 || !int.TryParse(kv[1], out v)) continue;
                if (kv[0] == "DCLight") dc = v;
                if (kv[0] == "CloseTimer") timer = v;
            }
            using (RegistryKey rgb = Registry.LocalMachine.OpenSubKey(KeyRgb, true))
            using (RegistryKey set = Registry.LocalMachine.OpenSubKey(KeySetting, true))
            {
                if (rgb != null && dc >= 0) rgb.SetValue("DCLight", dc, RegistryValueKind.DWord);
                if (set != null && timer >= 0) set.SetValue("CloseTimer", timer, RegistryValueKind.DWord);
            }
            File.Delete(BackupPath);
            return string.Format("vendor settings restored: DCLight={0}, CloseTimer={1}", dc, timer);
        }

        public static string ServiceStatus()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                    return sc.Status.ToString();
            }
            catch (InvalidOperationException)
            {
                return "not installed";
            }
        }

        public static string StopService()
        {
            return Control(false);
        }

        public static string StartService()
        {
            return Control(true);
        }

        static string Control(bool start)
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    if (start && sc.Status == ServiceControllerStatus.Stopped) sc.Start();
                    if (!start && sc.Status == ServiceControllerStatus.Running) sc.Stop();
                    sc.WaitForStatus(start ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    return ServiceName + " " + sc.Status;
                }
            }
            catch (Exception ex)
            {
                return ServiceName + ": " + ex.Message;
            }
        }
    }
}
