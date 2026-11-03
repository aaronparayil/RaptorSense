# RaptorSense

An open-source alternative to PredatorSense for Acer Predator laptops, focused on **per-key RGB**:
custom animations, music-reactive lighting, and more control than the stock software. Windows first; Linux is planned.

Created by [aaronparayil](https://github.com/aaronparayil). Free and open source, forever.

> Unofficial. Not affiliated with or endorsed by Acer. "Predator" and "PredatorSense" are Acer trademarks,
> used here only to describe compatibility.

## Status

Early development. Tested on one machine: **Predator Helios 16 PH16-71**, BIOS V1.18, keyboard `04F2:0117`.

| Feature | State |
|---|---|
| Per-key RGB, 60 fps, no admin rights | Works |
| 24 software effects (60 fps, real key positions, two colours or Auto rainbow): static, gradient, breathing, rainbow wave, colour wave, neon, snake, radar, spotlight, heartbeat, aurora, ripples, rain drop, matrix rain, snow, twinkling stars, lightning, fire, fireball, and more below | Works |
| The keyboard's 13 built-in effects (keep running when RaptorSense is closed) | Works |
| Music effects: spectrum, bass pulse (WASAPI loopback, no microphone) | Works |
| Keyboard's own built-in effects | Works |
| 11 typing effects: reactive keys, typing ripple, typing lightning, typing fireball, comet, burst, splash, crosshair, heatmap, confetti, sparkle | Works |
| Typing overlay: layer any typing effect on top of any effect (e.g. lightning on key presses over Aurora) | Works |
| Key painting: click or drag keys in the preview, right-click to erase | Works |
| Profiles: save, load, delete; app restores your last setup | Works |
| Desktop app with a live preview of the real key layout | Works |
| Tray icon: runs in the background, profiles, pause, quit | Works |
| Start with Windows (per-user, no admin), starts hidden with your last setup | Works |
| Fans (Auto / Max / Custom) and power profiles, live temperatures | Built; needs the helper service |
| PREDATOR key | Planned |
| Linux | Planned |

## Build and run

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build RaptorSense.slnx
dotnet run --project src/RaptorSense.App
```

Command line:

```
dotnet run --project src/RaptorSense.Cli -- list
dotnet run --project src/RaptorSense.Cli -- run rainbow
dotnet run --project src/RaptorSense.Cli -- run spectrum --color 00AEC7 --color2 FF0080
dotnet run --project src/RaptorSense.Cli -- builtin wave
dotnet run --project src/RaptorSense.Cli -- profile "WASD gaming"
```

Profiles are readable JSON files in `%APPDATA%\RaptorSense\profiles`. Painted keys are stored by name
(`"W": "FF0000"`), so profiles can be shared and edited by hand.

Acer's lighting service, PredatorSense and Razer Chroma can also drive the keyboard. RaptorSense re-sends its
frame every second, so it wins, but close PredatorSense while you use it.

## Fans and power modes

Windows lets only administrators change fans and power profiles, so these go through a small helper service,
`RaptorSense.Helper`. The app itself keeps running without admin rights and talks to the helper over a local pipe.
The helper accepts only a fixed set of commands (read sensors, set a power profile, set the fan mode), never
arbitrary firmware calls.

Install it once, from an **administrator** terminal:

```
dotnet build RaptorSense.slnx
src\RaptorSense.Helper\bin\Debug\net10.0-windows\RaptorSense.Helper.exe install
```

`RaptorSense.Helper.exe uninstall` removes it. To try it without installing, run `RaptorSense.Helper.exe` in an
administrator terminal and keep it open.

Safety rules, enforced by the helper (tests in `tests/RaptorSense.Tests`):

- Custom fan speeds are at least 25 %.
- At 90 °C in custom mode the fans go to Max until everything is below 80 °C.
- If the sensors can't be read in custom mode, the fans go to Max.
- When the helper stops, the fans go back to Auto. After a crash, it resets leftover custom speeds on its next start.
- Power profiles are limited to the ones the firmware reports, and every change is read back.

Command line: `raptorsense thermal`, `raptorsense power turbo`, `raptorsense fans custom 60 70`, `raptorsense fans auto`.

## Privacy

Reactive effects need a Windows keyboard hook. RaptorSense uses it only to learn **which key position** was
pressed: it reads the physical scan code, turns it into a lighting cell, and passes the key on untouched. It never
looks at characters, never records or stores anything, and sends nothing anywhere. The hook exists only while a
reactive effect is selected. See `src/RaptorSense.Core/Input/WindowsKeyHook.cs`.

## Layout

| Path | What |
|---|---|
| `src/RaptorSense.Core` | Keyboard protocol, HID transport, key map, effects engine, audio analysis |
| `src/RaptorSense.App` | Avalonia desktop app |
| `src/RaptorSense.Cli` | `raptorsense` command-line tool |
| `scripts/` | Hardware probes used to map the laptop (`probe.ps1` is read-only) |
| `docs/protocol.md` | Everything known about the hardware, with confidence levels |

## Other laptops

If you have a different Predator or Nitro model, the probe helps us add it:

```
powershell -ExecutionPolicy Bypass -File scripts\probe.ps1 -Label mymodel
```

Run it from an administrator PowerShell. It only reads from the firmware and saves a JSON report to `reports\`. Open an issue and attach it.

## Licence

RaptorSense is free software under the [GNU General Public License v3.0 or later](LICENSE). You may use, study,
share and change it. If you distribute it or a modified version, you must give out the full source
under the same licence and keep the author credits. That keeps RaptorSense and every fork of it open and free.

## Credits

Created by [aaronparayil](https://github.com/aaronparayil).

The keyboard protocol and key map come from [TrueSense](https://github.com/Order52/truesense) (MIT),
which mapped this keyboard on the same model and BIOS. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
and the prior-art list in [docs/protocol.md](docs/protocol.md).
