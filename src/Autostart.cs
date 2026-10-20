using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace KeyLight
{
    // Start with Windows through a Task Scheduler logon task with highest privileges
    // (no UAC prompt at logon). The task is allowed to start and keep running on battery.
    public static class Autostart
    {
        const string TaskName = "AvellKeyLight";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AvellKeyLight"); }
        }

        public static bool IsEnabled()
        {
            return Schtasks("/Query /TN " + TaskName) == 0;
        }

        // Copies the exe to Program Files (so the task survives moving/deleting the download)
        // and registers the logon task. Returns the installed exe path.
        public static string Enable()
        {
            string src = Process.GetCurrentProcess().MainModule.FileName;
            Directory.CreateDirectory(InstallDir);
            string dst = Path.Combine(InstallDir, "AvellKeyLight.exe");
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                File.Copy(src, dst, true);

            string user = WindowsIdentity.GetCurrent().Name;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Avell KeyLight: keeps the keyboard backlight at your setting</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>5</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(dst) + "</Command><Arguments>--tray</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string tmp = Path.Combine(Path.GetTempPath(), "AvellKeyLight-task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            int rc = Schtasks("/Create /TN " + TaskName + " /XML \"" + tmp + "\" /F");
            File.Delete(tmp);
            if (rc != 0) throw new InvalidOperationException("schtasks /Create failed with exit code " + rc);
            Log.Info("autostart enabled: {0}", dst);
            return dst;
        }

        public static void Disable()
        {
            Schtasks("/Delete /TN " + TaskName + " /F");
            Log.Info("autostart disabled");
        }

        static int Schtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(30000))
                {
                    p.Kill();
                    return -1;
                }
                return p.ExitCode;
            }
        }
    }
}
