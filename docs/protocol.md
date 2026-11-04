# Hardware protocol notes

What RaptorSense knows about the hardware, and how sure we are. Every entry says
which model and BIOS it was observed on. **Confirmed** means a probe or capture on real
hardware showed it; **hypothesis** means it comes from another source and still needs checking.

Reference machine: Predator Helios 16 **PH16-71**, BIOS V1.18, PredatorSense 5.0.1463.0.

## WMI: `root\WMI` → `AcerGamingFunction`

- GUID `{7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56}` (same as `WMID_GUID4` in Linux `acer-wmi`).
- Administrator rights are required, even for the `Get*` methods.
- Getters take a `UInt32` input; setters take a `UInt64`, except the LED/backlight ones, which use byte arrays.
- Integer outputs: bits 0-7 are a status byte (0 = success, 1 and 2 seen for unsupported inputs), the value follows.

| ID | Method | ID | Method |
|---:|---|---:|---|
| 1 | SetGamingProfile | 14 | SetGamingFanBehavior |
| 2 | SetGamingLED | 15 | GetGamingFanBehavior |
| 3 | GetGamingProfile | 16 | SetGamingFanSpeed |
| 4 | GetGamingLED | 17 | GetGamingFanSpeed |
| 5 | GetGamingSysInfo | 18 | SetGamingFanTable |
| 6 | SetGamingRgbKb | 19 | GetGamingFanTable |
| 7 | GetGamingRgbKb | 20 | SetGamingKBBacklight |
| 8 | SetGamingProfileSetting | 21 | GetGamingKBBacklight |
| 9 | GetGamingProfileSetting | 22 | SetGamingMiscSetting |
| 10 | SetGamingLEDBehavior | 23 | GetGamingMiscSetting |
| 11 | GetGamingLEDBehavior | 24 | SetCPUOverclockingProfile (do not touch) |
| 12 | SetGamingLEDColor | 25 | GetCPUOverclockingProfile |
| 13 | GetGamingLEDColor | | |

### Sensors: `GetGamingSysInfo` (confirmed)

Input = `command | (sensorId << 8)`.

- Command `0x00`: supported sensors as a bitmap in bits 24-39, where bit `n` = sensor `n+1`.
  PH16-71 returns `0x0227` → sensors 1, 2, 3, 6, 10.
- Command `0x01`: reading in bits 8-23.

| Sensor | Meaning | Sample |
|---:|---|---|
| 0x01 | CPU temperature, °C | 75 |
| 0x02 | CPU fan, RPM | 2507 |
| 0x03 | System/external temperature, °C | 68 |
| 0x06 | GPU fan, RPM | 2533 |
| 0x0A | GPU temperature, °C | 0 while the dGPU sleeps (hypothesis) |

- Command `0x02` (battery status) returned `0x10000000000`. Meaning unknown.

### Performance profile: `GetGamingMiscSetting` (confirmed)

Input = setting index; value in bits 8-15.

- `0x0A` supported profiles bitmap: `0x73` → Quiet (0), Balanced (1), Performance (4), Turbo (5), Eco (6).
- `0x0B` current profile: `1` = Balanced.
- Also answered with status 0: `0x01`=0, `0x02`=3, `0x06`=1, `0x07`=255, `0x08`=1, `0x09`=7. Meanings unknown.

### Fans

- `GetGamingFanBehavior` (confirmed): input is a fan mask, CPU `0x1`, GPU `0x8`. CPU mode is in bits 8-9, GPU mode in bits 14-15:
  1 = Auto, 2 = Max, 3 = Custom. The PH16-71 reported Auto for both.
- `SetGamingFanBehavior` (confirmed; encoding from `acer-wmi`): same fan mask in bits 0-15, CPU mode in bits 16-17, GPU mode in bits 22-23.
  `0x410009` = both Auto, `0x820009` = both Max.
- `GetGamingFanSpeed`: input `0x1` returned 100 % in bits 8-15 while in Auto (probably the stored custom value).
  Input `0x8` returned status 1, so the GPU fan ID here is something else.
- **Confirmed on Windows (2026-11-03): Max works.** `SetGamingFanBehavior(0x820009)` spun both fans to about 6,400 rpm
  (they idle around 2,500).
- **Confirmed on Windows (2026-11-04): Custom works.** `0xC30009`, then `SetGamingFanSpeed((percent << 8) | fan)` with CPU fan 1
  and GPU fan 4 (from TrueSense). Fan speed followed the setting at 30 % and 60 %, and `GetGamingFanBehavior` read back Custom for both fans.
- **Confirmed on Windows (2026-11-04): Auto (`0x410009`) undoes Custom.** Stopping the helper service at 40 % custom read back Auto for both fans.

### Setting the power profile

`SetGamingMiscSetting((profile << 8) | 0x0B)` is the same call TrueSense uses, and Acer's `AcerHardwareService` uses it too.

**On Windows the call itself reports WMI "Generic failure", yet the switch happens.** Confirmed 2026-11-03:
Balanced (`0x10B`) was requested from Quiet, the call failed, and `GetGamingMiscSetting(0x0B)` then read Balanced.
The error appears to come from passing the reply back, after the firmware has already acted.
Acer's own service has a log line "SetGamingMiscSettingBehavior is fail!!".

So RaptorSense ignores the reply and lets the read-back decide. It polls for up to ~1.5 s and ignores read errors
while the firmware settles, as TrueSense does. Tested on AC power. Confirmed 2026-11-04: switching through Quiet,
Balanced, Performance, Turbo and Eco works this way.

### Keyboard lighting over WMI

After switching the keyboard to static red in PredatorSense, none of these values changed.
They still describe teal:

- `GetGamingRgbKb` zones `0x1`, `0x2`, `0x4` → `0xC7AE0000`, i.e. R=`00` G=`AE` B=`C7` in bits 8-31. Zone `0x8` → 0.
- `GetGamingKBBacklight(1)` → `00 05 64 00 01 00 AE C7 …`. Probable layout: effect, speed 5,
  brightness 100, ?, direction 1, R, G, B.

So on the PH16-71, PredatorSense most likely does not drive the keyboard through WMI. (To confirm: did the keys actually turn red?)

## USB HID: Chicony keyboard `04F2:0117`

| Interface | Usage page | Role |
|---|---|---|
| MI_00 | 0001/0006 | Keyboard |
| MI_01 | **FF00** vendor-defined | RGB candidate |
| MI_02 | several collections | Consumer, mouse, radio, keyboard |
| MI_03 | **FF02** vendor-defined | RGB candidate |

**Confirmed: the keyboard is per-key RGB and is driven over USB HID, not WMI.**

- A photo showed a per-key rainbow effect, while WMI still reported teal for every zone.
- Acer's `RGBDevice.ini` (in the `predatorservice.inf` driver package) maps
  `USB\VID_04F2&PID_0117` (also `011A`, `0119`) to `AcerUSBController.dll` on **usage page FF02**, i.e. `MI_03`.
- `AcerLightingService.exe` imports `HidD_SetFeature` / `HidD_GetFeature`, so colours travel as **HID feature reports**.
  It also takes a global mutex named `Global\ECHIDFeatureVID_%04X&PID_%04X`, which RaptorSense should use too
  so it doesn't collide with Acer's service.
- Other lighting there: `AcerECLightbarController.dll` (lightbar, through the EC) and `AcerECKeyboardController.dll`.
- The same package bundles **OpenRGB 0.7.0** (GPL) plus `hidapi.dll` and `libusb-1.0.dll`.

Other things that can drive the keyboard on Windows and will fight RaptorSense for it:
AcerLightingService, PredatorSense and the Razer Chroma SDK services.

### Lighting protocol

Source: [TrueSense `docs/PROTOCOL.md`](https://github.com/Order52/truesense) (MIT), verified there on a PH16-71 with BIOS V1.18.
RaptorSense's Windows implementation is in `scripts/lib/RaptorHid.cs`.

- **Command packets** are 8-byte feature reports. Byte 7 is a checksum: `(0xFF - sum of bytes 0-6) & 0xFF`.
- **Per-key frame**, 128 cells, with `cell = column * 6 + row`:
  1. Feature `88 00 00 00 00 00 00 77`, then read the feature report back.
  2. Feature `12 00 00 08 00 00 00 E5`.
  3. Eight 64-byte output reports carrying `00 R G B` per cell, about 16 ms apart.
     Without the pauses, the last packet gets dropped.
  4. Feature `08 02 33 05 <brightness 0-50> 08 01 chk`.
- **Built-in effects**:
  1. Feature `B1 00 00 00 00 00 00 4E`.
  2. Colour `14 00 00 R G B 00 chk`.
  3. `08 02 <effect> <speed 0 fastest-11 slowest> <brightness> 01 <direction+1> chk`.

  Effect IDs: static 1, breathing 2, wave 3, snake 5, ripple 6, neon 8, raindrop 10, lightning 18, spot 37, star 38,
  fireball 39, snow 40, heartbeat 41.
- **Brightness read-back**: send `88 …`, read the feature report, byte 4.
- **Fn and PREDATOR keys**: usage page `0x88`, report id 4. `04 81 FF` = PREDATOR key pressed.

Windows specifics (confirmed 2026-11-03):

- The FF02 collection opens **without administrator rights**.
- `HidP_GetCaps` reports feature reports as 9 bytes and output reports as 65 bytes (report id 0 + payload).
- Acer's `Global\ECHIDFeatureVID_04F2&PID_0117` mutex did not exist while the test ran.
- `WriteFile` on the output report is synchronous: it returns once the transfer is done, unlike Linux hidraw writes.
- Frame rate with a 1 ms system timer (`timeBeginPeriod(1)`), full 128-cell frames, frame built in PowerShell:

  | Pacing between packets | Frame rate |
  |---|---|
  | 16 ms | 6.5 fps |
  | 4 ms | 18.2 fps |
  | 1 ms | 34.9 fps |
  | 0 ms | 75.3 fps |

  Before the timer change, one 16 ms-paced frame took 246 ms.
- **Confirmed: frames arrive complete at every pacing, including 0 ms.** In a whole-keyboard rainbow fade, all
  keys stayed the same colour in every stage. On Windows, RaptorSense sends packets back to back and gets about 75 fps.
  The 16 ms gap is only needed with Linux hidraw's asynchronous writes. That still needs checking once we get to Linux.

### Key map check (confirmed 2026-11-03)

Every key was pressed with the reactive effect running, and each lit its own cell. That covers Esc (cell 5),
Caps Lock (cell 2), Win (cell 12), `-` (cell 70), the arrows, and the numpad with NumLock on and off.
The scan-code table is `src/RaptorSense.Core/Layout/ScanCodes.cs`.

### Prior art for this keyboard (read before reverse engineering)

- [Order52/truesense](https://github.com/Order52/truesense): Linux; tested on a PH16-71 with BIOS V1.18. Per-key RGB, fans, power.
- fuho/rgbkb: per-key protocol for this keyboard, cited in
  [cleyton1986/predator-sense#74](https://github.com/cleyton1986/predator-sense/issues/74).
- [NT411/Acer-Predator-Fan-RGB-Controller-Linux](https://github.com/NT411/Acer-Predator-Fan-RGB-Controller-Linux): lists `04F2:0117`.
- [OpenRGB#4975](https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/4975): request for `04F2:011A` on a PH16-71. Upstream OpenRGB doesn't detect it.

Windows projects for the same laptop, listed in TrueSense's credits:

- [PreySense](https://github.com/hammadzaigham/PreySense) (MIT): a PredatorSense alternative.
- [jma-studio-rgb-keyboard](https://github.com/jadamsky/jma-studio-rgb-keyboard) (MIT): a Windows RGB studio for this keyboard and the light bar.
- [TensorRaya/acer-predator-helios-rgb](https://github.com/TensorRaya/acer-predator-helios-rgb) (GPL-2.0): OpenRGB controller. Notes that the PH16-73 uses a LampArray keyboard instead.

Check each project's licence before reusing code, and credit them in the README.
