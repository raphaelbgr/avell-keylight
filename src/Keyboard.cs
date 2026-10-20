using System;
using System.Drawing;

namespace KeyLight
{
    // Snapshot of the keyboard-backlight registers.
    public struct KbState
    {
        public byte Status;   // 0x078C
        public byte Red;      // 0x0769, level 0-50
        public byte Green;    // 0x076A, level 0-50
        public byte Blue;     // 0x076B, level 0-50

        public int Brightness { get { return Status >> 5; } }
        public bool PowerOffBit { get { return (Status & 0x02) != 0; } }
        public bool IsLit { get { return Brightness > 0 && !PowerOffBit; } }

        public bool SameAs(KbState o)
        {
            // bit 4 is the "apply" strobe; the EC clears it by itself
            return (Status & 0xEF) == (o.Status & 0xEF) && Red == o.Red && Green == o.Green && Blue == o.Blue;
        }

        public override string ToString()
        {
            return string.Format("status=0x{0:X2} brightness={1} off-bit={2} rgb-level=({3},{4},{5})",
                Status, Brightness, PowerOffBit ? 1 : 0, Red, Green, Blue);
        }
    }

    // Keyboard backlight on Uniwill/Tongfang boards (register map from tuxedo-drivers
    // uniwill_interfaces.h and mainline drivers/platform/x86/uniwill/uniwill-acpi.c).
    public sealed class Keyboard
    {
        public const ushort RegFeatures = 0x0766;  // bit 2 = 1-zone RGB keyboard
        public const ushort RegRgbMode = 0x0767;   // bit 5 = apply color
        public const ushort RegRed = 0x0769;
        public const ushort RegGreen = 0x076A;
        public const ushort RegBlue = 0x076B;
        public const ushort RegStatus = 0x078C;    // bits 7-5 brightness, bit 4 apply, bit 1 power off, bit 0 white-only

        public const int MaxBrightness = 4;
        public const int MaxColorLevel = 50;

        readonly Ec ec;
        public readonly bool IsRgb;

        public Keyboard(Ec ec)
        {
            this.ec = ec;
            IsRgb = (ec.Read(RegFeatures) & 0x04) != 0;
        }

        public KbState Read()
        {
            KbState s;
            s.Status = ec.Read(RegStatus);
            s.Red = ec.Read(RegRed);
            s.Green = ec.Read(RegGreen);
            s.Blue = ec.Read(RegBlue);
            return s;
        }

        // 0-255 color channel -> EC level 0-50
        public static byte ToLevel(int channel)
        {
            return (byte)(channel * MaxColorLevel / 255);
        }

        static bool Near(byte a, byte b)
        {
            // the EC may store level 1 where 0 was written
            return Math.Abs(a - b) <= 1;
        }

        static bool IsBlack(Color c)
        {
            return ToLevel(c.R) == 0 && ToLevel(c.G) == 0 && ToLevel(c.B) == 0;
        }

        public bool Matches(KbState s, int brightness, Color color)
        {
            if (brightness <= 0 || IsBlack(color))
                return !s.IsLit;
            if (s.PowerOffBit || s.Brightness != brightness)
                return false;
            if (!IsRgb)
                return true;
            return Near(s.Red, ToLevel(color.R)) && Near(s.Green, ToLevel(color.G)) && Near(s.Blue, ToLevel(color.B));
        }

        public void Apply(int brightness, Color color)
        {
            if (brightness < 0) brightness = 0;
            if (brightness > MaxBrightness) brightness = MaxBrightness;
            // all-zero color triggers an EC "load defaults" special case, so treat black as off
            if (IsBlack(color)) brightness = 0;

            if (IsRgb && brightness > 0)
            {
                ec.Write(RegRed, ToLevel(color.R));
                ec.Write(RegGreen, ToLevel(color.G));
                ec.Write(RegBlue, ToLevel(color.B));
                ec.Write(RegRgbMode, (byte)(ec.Read(RegRgbMode) | 0x20));
            }

            byte status = ec.Read(RegStatus);
            status &= 0x0F;                    // keep low bits (white-only, turbo level)
            status &= unchecked((byte)~0x02);  // clear power-off bit
            status |= (byte)(brightness << 5);
            status |= 0x10;                    // apply strobe
            ec.Write(RegStatus, status);
        }
    }
}
