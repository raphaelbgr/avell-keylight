using System;
using System.IO;
using System.Text;

namespace KeyLight
{
    // Text log + JSONL trace under %ProgramData%\AvellKeyLight\logs, rotated at 5 MB.
    public static class Log
    {
        const long MaxBytes = 5L * 1024 * 1024; // 5 MB
        static readonly object gate = new object();

        public static string Dir
        {
            get { return Path.Combine(Config.DataDir, "logs"); }
        }

        public static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        }

        public static void Info(string fmt, params object[] args)
        {
            Append("keylight.log", Now() + "  " + (args.Length > 0 ? string.Format(fmt, args) : fmt));
        }

        public static void Trace(string jsonLine)
        {
            Append("trace.jsonl", jsonLine);
        }

        public static void WriteState(string json)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    string p = Path.Combine(Dir, "state.json");
                    File.WriteAllText(p + ".tmp", json, Encoding.UTF8);
                    if (File.Exists(p)) File.Delete(p);
                    File.Move(p + ".tmp", p);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        static void Append(string name, string line)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    string p = Path.Combine(Dir, name);
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = p + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(p, old);
                    }
                    File.AppendAllText(p, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
