using System;
using System.Management;
using System.Threading;

namespace KeyLight
{
    // Access to the Uniwill EC (embedded controller) RAM through the firmware WMI
    // method AcpiTest_MULong.GetSetULong (GUID ABBC0F6F-8EA1-11D1-00A0-C90629100000).
    //   read : Data = 0x0100 << 32 | addr      -> Return & 0xFF = byte at addr
    //   write: Data = value  << 16 | addr
    // Same encoding as tuxedo-drivers (uniwill_wmi.c) and the vendor Control Center.
    public sealed class Ec : IDisposable
    {
        const ulong ReadFlag = 0x10000000000UL;
        const uint CallFailed = 0xFEFEFEFE;

        // The firmware rejects overlapping calls, so every caller - other threads and
        // other KeyLight processes (tray + CLI) - takes this machine-wide lock first.
        static readonly Mutex gate = new Mutex(false, @"Global\AvellKeyLight.EC");
        readonly ManagementObject wmi;

        public Ec()
        {
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AcpiTest_MULong"))
            {
                foreach (ManagementObject o in searcher.Get())
                {
                    wmi = o;
                    break;
                }
            }
            if (wmi == null)
                throw new InvalidOperationException("WMI class AcpiTest_MULong not found. This is not a Uniwill-based laptop, or the program is not running as administrator.");
        }

        // Total calls that had to be retried (firmware busy), for diagnostics.
        public static int Retries;

        uint Call(ulong data)
        {
            try
            {
                if (!gate.WaitOne(10000))
                    throw new InvalidOperationException("EC lock timeout");
            }
            catch (AbandonedMutexException)
            {
                // previous owner died; we own it now
            }
            try
            {
                // Still fails (0xFEFEFEFE) if the vendor service hits the firmware at the
                // same moment, so retry briefly.
                for (int attempt = 0; ; attempt++)
                {
                    ManagementBaseObject inParams = wmi.GetMethodParameters("GetSetULong");
                    inParams["Data"] = data;
                    ManagementBaseObject outParams = wmi.InvokeMethod("GetSetULong", inParams, null);
                    uint ret = Convert.ToUInt32(outParams["Return"]);
                    if (ret != CallFailed) return ret;
                    if (attempt == 5)
                        throw new InvalidOperationException(string.Format("EC call failed after {0} retries (Data=0x{1:X})", attempt, data));
                    Interlocked.Increment(ref Retries);
                    Thread.Sleep(25 * (attempt + 1));
                }
            }
            finally
            {
                gate.ReleaseMutex();
            }
        }

        public byte Read(ushort addr)
        {
            return (byte)(Call(ReadFlag | addr) & 0xFF);
        }

        public void Write(ushort addr, byte value)
        {
            Call(((ulong)value << 16) | addr);
        }

        public void Dispose()
        {
            wmi.Dispose();
        }
    }
}
