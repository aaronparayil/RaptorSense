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
| Software effects: static, breathing, rainbow wave | Works |
| Music effects: spectrum, bass pulse (WASAPI loopback, no microphone) | Works |
| Keyboard's own built-in effects | Works |
| Reactive typing: reactive keys, ripple | Works |
| Key painting: click or drag keys in the preview, right-click to erase | Works |
| Profiles: save, load, delete; app restores your last setup | Works |
| Desktop app with live keyboard preview | Early |
| Tray icon, start with Windows, PREDATOR key | Planned |
| Fans and performance profiles | Planned (firmware interface mapped in [docs/protocol.md](docs/protocol.md)) |
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
