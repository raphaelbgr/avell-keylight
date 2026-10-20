using System;
using System.Management;

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

        readonly object gate = new object();
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

        uint Call(ulong data)
        {
            lock (gate)
            {
                ManagementBaseObject inParams = wmi.GetMethodParameters("GetSetULong");
                inParams["Data"] = data;
                ManagementBaseObject outParams = wmi.InvokeMethod("GetSetULong", inParams, null);
                uint ret = Convert.ToUInt32(outParams["Return"]);
                if (ret == CallFailed)
                    throw new InvalidOperationException(string.Format("EC call failed (Data=0x{0:X})", data));
                return ret;
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
