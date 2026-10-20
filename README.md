# Avell KeyLight

Keyboard backlight control for **Avell** laptops and other **Uniwill / Tongfang** based laptops on Windows.

It keeps the keyboard light **on, at the brightness and color you pick, all the time**. That holds on battery or plugged in, after sleep, and after a reboot. The vendor "Control Center" keeps switching the light off; KeyLight puts it back.

Verified on: **Avell C62 MOB** (i7-11800H, RTX 3050, BIOS N.1.04AVE00, 1-zone RGB keyboard, Control Center 3.24.32.0).
Other Uniwill-based machines (XMG, Schenker, Tuxedo, Eluktronics, PCSpecialist, Mechrevo, Intel NUC X15...) expose the same firmware interface and will probably work. Please open an issue with the output of `AvellKeyLight.exe status` either way.

## Why the light keeps turning off

The vendor background service (`GCUService.exe`, started by the `GCUBridge` Windows service) controls the keyboard. It turns the light off in two cases:

- **On battery:** it applies the "battery brightness" setting `HKLM\SOFTWARE\OEM\GamingCenter2\RGBKeyboard\DCLight`. On the machine this was built on, that setting was `0`, which means off. The service logs it as `In DC Mode Backlight off`.
- **After idle time:** it has an auto-off timer (`MySetting\CloseTimer`).

## Features

- On/off, brightness 1-4 (hardware steps), fine **Intensity** 5-100% on RGB keyboards (scales the LED color levels), any color (on RGB keyboards), with 9 one-click presets.
- **Keep my setting:** checks the keyboard every N seconds (default 2) and re-applies your setting if anything changed it. It also re-applies right after wake from sleep and after plugging in or unplugging.
- **Optional battery rule (off by default):** on battery, either turn the light off or dim it to a level you pick. You can limit it to "only when charge is at or below X%".
- **Start with Windows:** a Task Scheduler logon task with highest privileges, so there is no UAC prompt at logon. The task is set to start and keep running on battery, which Windows does not do by default.
- **Neutralize Control Center:** sets the vendor battery brightness equal to the plugged-in brightness and disables its idle timer. The original values are backed up and can be restored.
- Optional: stop the vendor service while KeyLight runs. This is off by default, because that service also drives fan profiles and the Fn hotkey overlay.
- **Color calibration:** per-channel trims (white balance) plus a mid-tone curve (gamma). LEDs are not linear, and on the Avell C62 equal red, green, and blue levels look yellow, so the defaults are red 60%, green 60%, blue 100%, gamma 2.2. The strongest channel is always kept at full intensity, so the trims change how colors mix but never dim pure red, green, or blue. Use **Test white** and lower whichever channel is too strong.
- Tray icon: left-click opens settings, right-click shows a quick menu (on/off, brightness, **Exit KeyLight**). Minimize, close, or **Hide to tray** all keep it running in the tray. The icon is set to always visible on the taskbar.
- A command-line interface.
- Diagnostics: every change seen on the keyboard controller is written with a timestamp to `%ProgramData%\AvellKeyLight\logs\trace.jsonl`, and the live status goes to `state.json`. This shows exactly when and how the light was changed.

## Install

1. Download `AvellKeyLight.exe` from [Releases](../../releases), or build it yourself (see below).
2. Run it and accept the admin prompt. Admin is required because the keyboard controller is only reachable through a firmware method that Windows reserves for administrators.
3. Pick your brightness and color, then tick **Start with Windows**. The exe is copied to `C:\Program Files\AvellKeyLight\` and runs from there at every logon.

Closing the window keeps KeyLight running in the tray. Use the tray icon's **Exit** to quit.

## Command line

```
AvellKeyLight.exe                      open settings (keeps running in the tray)
AvellKeyLight.exe --tray               start hidden in the tray (used at logon)
AvellKeyLight.exe status               show keyboard registers and settings
AvellKeyLight.exe set [on|off] [brightness=1-4] [color=RRGGBB]
AvellKeyLight.exe autostart on|off
AvellKeyLight.exe vendor fix|restore
AvellKeyLight.exe dump                 read-only dump of EC registers 0x0740-0x07FF
  --out <file>                         also write command output to a file
```

Settings live in `%ProgramData%\AvellKeyLight\config.ini`. A `set` from the command line is picked up immediately by the running tray instance.

## Build

No SDK is needed. Windows ships the C# compiler for .NET Framework 4.x.

```
build.cmd
```

The output is `bin\AvellKeyLight.exe`.

## How it works

1. Uniwill firmware exposes a WMI method (WMI = Windows' built-in channel for talking to BIOS functions): `root\wmi : AcpiTest_MULong.GetSetULong`, GUID `ABBC0F6F-8EA1-11D1-00A0-C90629100000`.
2. That method reads and writes the RAM of the EC (embedded controller, the small chip on the motherboard that runs the keyboard light, fans, and battery).
   - Read: `Data = 0x0100 << 32 | address`. The low byte of `Return` is the value.
   - Write: `Data = value << 16 | address`.
3. Keyboard registers:

| Address | Meaning |
|---|---|
| `0x078C` | bits 7-5 brightness (0-4), bit 4 apply strobe, bit 1 power-off, bit 0 white-only keyboard |
| `0x0769` / `0x076A` / `0x076B` | red / green / blue level, 0-50 |
| `0x0767` | bit 5 = apply color |
| `0x0766` | bit 2 = keyboard is 1-zone RGB |

4. To set the light, KeyLight writes the three color levels, sets the apply-color bit, then writes the brightness into `0x078C` with the apply strobe. This is the same sequence the Linux driver uses.

## Credits

The register map and encoding come from:

- [tuxedo-drivers](https://gitlab.com/tuxedocomputers/development/packages/tuxedo-drivers) (`uniwill_interfaces.h`, `uniwill_leds.h`, `uniwill_wmi.c`)
- the mainline Linux `uniwill-laptop` driver
- [Rev.Center2-Keyboard](https://github.com/lKinderBueno/Rev.Center2-Keyboard)
- [NUCtool](https://github.com/cyear/NUCtool)

No code was copied from them.

## Disclaimer

KeyLight only writes the keyboard-light registers listed above. It writes the same values the vendor software and the Linux driver write. Still, it talks to firmware directly. Use it at your own risk. It is not affiliated with Avell or Uniwill.

## License

MIT
