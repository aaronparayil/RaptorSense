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
- **Only the reference machine's encodings are known.** The class has the same name on other Acer gaming laptops,
  but sensor ids, fan ids and setting values may differ. So the helper writes only on models listed in
  `LaptopModels.Verified`, matched against `Win32_ComputerSystem.Model` (`Predator PH16-71` here, with
  `SystemFamily` `Predator Helios 16`); elsewhere it only reads. To add a model, map it with the probe, check each
  write by hand, then add its model code to that list.

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

## Light bar (rear, 3 zones): WMI only

**Confirmed on Windows (2026-11-05), PH16-71, BIOS V1.18:** the light bar is not a USB device and is not on the keyboard's
HID interfaces. It is driven through `AcerGamingFunction`, and every `Set*` call needs administrator rights.

Protocol from [jma-studio-rgb-keyboard](https://github.com/jadamsky/jma-studio-rgb-keyboard) (MIT, `hardware/lightbar.py`
and `LIGHTBAR_REVERSE_ENGINEERING.md`), checked on this unit. Parameter sizes here: `SetGamingLED` 12 bytes,
`SetGamingKBBacklight` 16 bytes, `SetGamingRgbKb` a `u64`.

- **Static colour, one zone**: `SetGamingRgbKb((r << 8) | (g << 16) | (b << 24) | (0x08 << 32) | (mask << 40))`.
  Masks: 1 = left, 2 = centre, 3rd zone = 4 = right, from the front with the lid up (the left/right order is from jma-studio;
  confirm it by eye on this unit). The plain `mask | r << 8 | …` packing does not work.
- **One commit round**, as sent by Acer's own stack:
  1. `SetGamingLED` with `10 00 00 00 00 00 FF FF FF 15 00 00`.
  2. `SetGamingKBBacklight` with `00 00 <brightness 0-100> 00 00 00 00 00 03 02 00 00 00 00 00 00`.
  3. `SetGamingRgbKb` once per zone.

  jma-studio repeats the round 3 times, 65 ms apart, for a reliable change, and sends a single round for fast updates.
  All calls returned status 0 here with 3 rounds: zone 1 red, zone 2 green, zone 3 blue.
- **Read-back**: `GetGamingKBBacklight` slots 2 and 3 return `00 00 <brightness> 00 00 R G B 03 …` for the current colour
  (blue → `00 00 FF`, then red → `FF 00 00` after the commit). It is a state read-back, **not** where colours are written:
  writing the same layout with `SetGamingKBBacklight` is refused with status 1.
- **Animated modes**: `SetGamingKBBacklight` with `[mode, speed, brightness, 0, 1, R, G, B, 3, 2, 0…]`, in the same
  commit round as above (`SetGamingLED` first, then this, no `SetGamingRgbKb`).
  Modes: breathing 1, neon 2, rainbow 3, wave 4, ripple 5, scanner 6, strobe 7. One colour for the whole bar.
  **Confirmed on Windows (2026-11-05):** breathing, neon, rainbow, wave, ripple and scanner, at speed 5 and brightness 100
  (the owner watched the bar; the per-mode look was not written down). Strobe was not tried (rapid flashing).
  **Speed, confirmed (2026-11-05):** with wave, 0, 2, 5, 8 and 10 were all accepted, and 10 looked clearly faster than 0,
  so **a higher number is faster on the bar** (the opposite of the keyboard's built-in effects, where 0 is fastest).
  The in-between values were not compared one by one, other modes were not tried at other speeds, and values above 10
  were not tried; RaptorSense clamps to 0-10. Static mode `0xFF` is documented by jma-studio as not working on this chassis.
- **Timing** (2026-11-05, WMI from a .NET process, helper-less): one static change of 3 rounds takes 0.4-0.5 s, an
  animation change about 0.3 s. A single round is roughly 90 ms, so live per-frame effects on the bar top out near 10 updates a second.
- No handshake or mutex is needed. COM objects are thread-affine, so use one WMI instance per thread.
- `GetGamingLED*` (ids 4, 11, 13) read all zeros or an error status on this laptop; the bar does not use them for reads.

## Nitro 5 AN515-57: 4-zone keyboard

**Static zone colours confirmed on Windows (2026-11-16), AN515-57, BIOS V1.22**, with administrator rights:

- The keyboard is not USB (the only vendor-defined HID device was `248A:5B4A`, a mouse receiver). It is `AcerGamingFunction`.
- Parameter types on this firmware: `SetGamingLED` and `SetGamingKBBacklight` as below, but `SetGamingLED` takes a
  `UInt64` here (not the Helios's 12-byte array), and `SetGamingRgbKb` / `GetGamingRgbKb` take a `UInt32`.
  `GetGamingKBBacklight` returns 15 bytes and a `gmReturn` byte.
- **Zone colours read back.** `GetGamingRgbKb(mask)` with mask 1, 2, 4 or 8 returns `status | R << 8 | G << 16 | B << 24`,
  the same packing as the write with the mask replaced by the status. Mask 15 fails (status 2). Factory colour: `AA00FF`.
- While one of the keyboard's own effects runs, zone colours are stored but not shown. The sequence that worked:
  `GetGamingSysInfo(0)`, `SetGamingLED(0xF0000000008)`, four `SetGamingRgbKb` calls (status 0; the first three answered
  `0x200000000`), then `SetGamingKBBacklight(00 00 64 00 00 00 00 00 00 01 …)`. All four zones showed their colours.
- The factory effect read back as `02 05 64 FF 00 …`: mode 2 (neon), speed 5, brightness 100. In a read, byte 3 is
  `FF` and byte 4 is the direction: after sending mode 2 with direction 1 it read `02 05 64 FF 01`. So reads don't
  carry the wave flag where writes do.
- Only modes 0 and 2 have been sent. The other effect modes and the brightness keys are still unchecked.

What follows was gathered on 2026-11-15, before the AN515-57 arrived, from two Linux sources:
[acer-predator-turbo-and-rgb-keyboard-linux-module](https://github.com/JafarAkhondali/acer-predator-turbo-and-rgb-keyboard-linux-module)
(`src/facer.c`, `facer_rgb.py`; its table lists AN515-55, -56, -57 and -58 with RGB "tested"), and an RFC patch adding
4-zone RGB for the AN515-58 to mainline `acer-wmi`
([May 2026](https://patchew.org/linux/20260503062131.158944-1-dirgayuza123@gmail.com/); one review, no later version found).

- **Where:** the same `AcerGamingFunction` interface (`WMID_GUID4`) as the Helios's light bar, not USB. So it needs
  administrator rights on Windows, and lighting would go through the helper.
- **Methods**, by the Linux numbering: 2 `SetGamingLED`, 4 `GetGamingLED`, 5 `GetGamingSysInfo`,
  6 `SetGamingRgbKb` (static zone colour), 20 `SetGamingKBBacklight` (effects), 21 `GetGamingKBBacklight`.
- **Turning the zones on**, once at start: `GetGamingSysInfo(0)`, then `SetGamingLED(8 | 15 << 40)` ("bit 3 enables
  keyboard backlight update; bits 40-43 select the zones"). The module's comment says the sys-info read "appears to be
  required to enable Nitro AN515-57". Note that 8 at bit 32 and the zone mask at bit 40 are exactly the extra bits the
  Helios's light bar needs in its `SetGamingRgbKb` value.
- **Static colour**, one call per zone: method 6 with the 4 bytes `zone-mask R G B` (masks 1, 2, 4, 8 from the left),
  which as a `u64` is `mask | r << 8 | g << 16 | b << 24`: the packing that does **not** work for the Helios's bar.
  `facer_rgb.py` then sends the effect buffer below with mode 0 and only the brightness and byte 9 = 1 set, to switch the
  keyboard to static colours. No delays between calls in either source, and nothing reads a colour back.
- **Effects**, method 20, 16 bytes: `[mode, speed 1-9, brightness 0-100, 8 for wave else 0, direction (1 right to left,
  2 left to right), R, G, B, 0, 1, 0…]`. Modes 0 static, 1 breath, 2 neon, 3 wave, 4 shifting, 5 zoom.
  The Helios's bar uses the same layout with bytes 8-9 = `3 2` and different mode numbers; perhaps bytes 8-9 select
  the device. Untested guess.
- **Brightness keys:** on the AN515-58, Fn+F9 / Fn+F10 (from the patch's notes, which say they send no release event on Linux).
- **Not to copy:** the Linux module can set an embedded-controller flag (EC register 0x03, bit 4) that PredatorSense
  is said to set on Windows. Writing EC registers directly is outside what RaptorSense does.

These layouts are written down as code in `FourZoneProtocol` (`src/RaptorSense.Core/Hardware/FourZoneKeyboard.cs`),
with tests that pin them to the sources above. `AcerGamingWmi` sends them through the helper's `keyboard` command,
which the helper accepts only on models in `LaptopModels.FourZoneKeyboards`, even where fans and power are read-only.

On the Helios, Windows' `SetGamingLED` takes a 12-byte array, not a `u64`, so the turn-on call may need a different
shape on Windows; the probe's list of method parameters will tell.

Expected cost, from the Helios's light bar (about 18 ms per call, and firmware calls there stall the whole machine for
a few milliseconds): four zone
calls per frame would allow roughly 10 frames a second at best. So static colours, the firmware's own effects and
slow changes look possible; 60 fps effects and quick music sync probably don't. Measure it on the laptop first.

**Fans confirmed on the AN515-57 (2026-11-16)** with the PH16-71's encodings, unchanged: `SetGamingFanBehavior`
`0x820009` (Max), `0x410009` (Auto) and `0xC30009` (Custom) all returned 0 and read back through
`GetGamingFanBehavior(0x9)` as `0x8200`, `0x4100` and `0xC300`. `SetGamingFanSpeed(0x3C01)` / `(0x3C04)` set both
fans to 60 % (read back as `0x3C00`). Fans went from about 2050 / 2380 rpm on Auto to 6250 / 7500 rpm on Max and
4760 / 5660 rpm at 60 %. The speed read keeps the last custom value after going back to Auto. Power profiles read the
same way as on the Helios (supported `0x33`: Quiet, Balanced, Performance, Turbo) but have not been switched yet.

The app plays its per-key effects on the zones by averaging the keys in each zone (`FourZoneLayout`: the main block
in three equal parts by key position, then the arrows and numpad). It sends a frame only when a zone changes, and at
most ten a second, with `Confirm = false` so the helper skips the read-back.

Still to check on the AN515-57: the effect modes other than 2, the wave direction, how long a 4-zone update takes
(for anything faster than static colours), and the brightness keys.

## Battery: Windows battery driver

**Confirmed on Windows (2026-11-06), PH16-71, BIOS V1.18, battery AP21A8T**, without administrator rights:
`IOCTL_BATTERY_QUERY_INFORMATION` with level `BatteryInformation` returns the cycle count, design capacity and full-charge
capacity in mWh. `root\WMI` `BatteryCycleCount` and `BatteryFullChargedCapacity` return the same cycle count and full-charge
capacity, but `BatteryStaticData`, the WMI class with the design capacity, fails with "Generic failure" here, so RaptorSense
uses the driver call. Battery wear doesn't go through `AcerGamingFunction`, so it should work on any Windows laptop whose
battery reports these values.

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

### Special keys and the keyboard's own brightness (confirmed 2026-11-15)

Listened to on Windows, PH16-71, without administrator rights:

- The special keys arrive as 3-byte input reports on `MI_02&COL03` (usage page `0x0088`, usage 1), which a normal
  user can open for reading. Pressed: PREDATOR `04 81 FF`, Fn+F11 (dimmer) `04 7D FF`, Fn+F12 (brighter) `04 7C FF`.
  Letting go of any of them sends `04 00 00`. None of them reaches Windows as a key press, and none raised an
  `AcerGenericEvent` in WMI.
- **The keyboard changes its brightness by itself** on Fn+F11 / Fn+F12, through the levels 0, 12, 25, 37, 50, and
  byte 4 of the `88` state report follows. This holds while one of its own effects runs and after a single painted
  frame.
- **Every painted frame puts the brightness back** to the value in its last packet. While frames were streamed at a
  fixed brightness, each press showed for a moment and was undone (the state still read 25 at the end).
  Reading the state before each frame and adopting a changed value mostly works, but a press that lands while a
  frame is being sent is lost (about one press in four in the test, at 30 frames a second). So RaptorSense takes the step itself when it sees the
  key's report: `SpecialKeyWatcher` and `BrightnessSteps`.
- Byte 5 of the state report counts painted frames, 1 to 7 and round again. Leave it out when comparing states.
- A state read sometimes returns the previous feature packet instead (seen: `08 02 33 05 …`, the frame's last
  packet), roughly once in a hundred reads while streaming. Check for `88` in byte 0 and the checksum before
  trusting one.
- **Unexplained:** in two earlier listening runs the same day, Fn+F11 / Fn+F12 produced no report and changed
  nothing, while the PREDATOR key reported normally. PredatorSense and `AcerLightingService` were running during
  those runs and were not running during the one that worked. Nothing here shows that they were the cause.

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

Linux specifics (written 2026-11-16; **nothing here has run on the keyboard yet**):

- Linux gives one `/dev/hidrawN` per USB interface, not per collection as Windows does. The lighting interface is
  `MI_03` (interface 3), alone on usage page FF02 and without report ids, so `LinuxHidTransport` finds it by parsing
  `/sys/class/hidraw/*/device/report_descriptor` and matching `HID_ID` in `device/uevent`.
- Feature reports go through the `HIDIOCSFEATURE` / `HIDIOCGFEATURE` ioctls and output reports through `write`, each
  with a leading report-id byte of 0.
- hidraw devices belong to root. `linux/70-raptorsense.rules` gives the seated user interface 3 only; the typing
  interfaces (`MI_00`, and `MI_02` with its keyboard collection) stay closed.
- **Open: the gap between a frame's eight output reports.** TrueSense needed about 16 ms on Linux or the last packet
  was dropped, which would mean about 6 frames a second. Whether that holds for plain `write` on a current kernel is
  the first thing to measure: set `RAPTORSENSE_HID_GAP_MS` (default 0) and watch a whole-keyboard fade for keys
  that lag behind.
- The special keys are on `MI_02`, which on Linux is one hidraw device shared with consumer keys, a mouse and a
  keyboard collection. Reading it would need care to drop everything but report id 4. Not started.
- Seen in WSL (Ubuntu 26.04, no keyboard): the app aborts with `free(): invalid pointer` the first time it draws a
  window on a system whose fontconfig cache has never been built, inside the system's font library, and starts
  normally from then on. A desktop install has that cache already. .NET also wants `libicu`; Linux builds are made
  with `InvariantGlobalization` so they start without it.
- The model comes from `/sys/class/dmi/id/product_name`, which should read the same as Windows' model
  (`Predator PH16-71`). To confirm on the laptop.
- Checked in WSL (2026-11-16): the battery from `/sys/class/power_supply`, and a second launch reaching the first
  copy over a per-user pipe (`/tmp/CoreFxPipe_RaptorSense.Show`), also after the first copy was killed.

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
