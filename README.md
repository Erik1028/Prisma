# Prisma

**Unified control for everything that glows on your desk.**

A single, fast Windows app that drives your motherboard, RAM, GPU, fan strips, keyboard
and mouse lighting from one place — with real animated effects, screen sync, audio
reactivity and per-device overrides. No vendor suite, no cloud, no telemetry, no account.

Prisma talks to hardware through several backends at once, so it keeps working when any
one of them falls over — including a **native GPU driver** that bypasses OpenRGB entirely.

---

## Why

Vendor RGB suites each own one brand, fight each other over the same SMBus, and
routinely lose devices after a boot or a wake. Prisma sits on top of the buses instead:
one owner, one frame loop, one colour, everything in sync — plus explicit cures for the
power-transition bugs that make RGB "randomly stop working".

## Features

### Effects
Static · Rainbow wave · Colour cycle · Breathing · Strobe · Two-colour gradient ·
Comet · Twinkle · Fire · **Ambient** (screen sync) · **Music** (system-audio reactive,
with a separate bass band for on-beat punch) · Surprise · Off

All rendered on a shared 25 fps loop (configurable 5–30 fps) so every device animates
in step. Devices with capable firmware can run an effect onboard instead of being
streamed, which keeps the fragile ones stable.

### Devices
- **Per-device colour and effect** — right-click any device row
- **Device grouping** — runs of identical devices (e.g. four RAM sticks) collapse under
  one header with a shared toggle
- **Live preview** strip showing exactly what is being sent
- Opt-in per device, so you can leave the mouse to its vendor software

### Automation
- **Profiles** with one-click apply, plus **auto-apply on process launch** (game
  triggers) and automatic restore when the process exits
- **Night schedule** — lights off, or dimmed, between chosen hours
- **Idle timeout** off
- Off when the session locks, when the machine sleeps, when the display sleeps, on exit
- **Global hotkeys** — `Ctrl+Alt+L` lights on/off, `Ctrl+Alt+P` next profile,
  `Ctrl+Alt+R` rescan devices
- Tray icon that shows the current colour live

### Reliability
This is where most of the work went.

- **Boot and wake re-assert** — ASUS Aura boards revert their zones to the firmware
  rainbow on resume, and a RAM stick that enumerates late on the SMBus can ignore colour
  writes until the server is restarted. Streaming colours does not fix either; only
  re-issuing *Direct mode* does. Prisma re-asserts after wake and a couple more times as
  the bus settles post-boot.
- **Conflict watchdog** — detects other RGB stacks contending for the lighting bus and
  says which one, instead of leaving you with unexplained flicker.
- **Backend watchdog** — reconnects and re-detects if the OpenRGB server dies, and
  notices when the server's controller count no longer matches the cached list.
- **GPU wedge handling** — the Sapphire I2C controller can latch unrecoverably; Prisma
  orders its writes to avoid it and routes recovery to a sleep/wake rather than looping.

## Backends

| Backend | Devices | Notes |
|---|---|---|
| **OpenRGB SDK** (TCP `127.0.0.1:6742`) | Motherboard, RAM, and anything else OpenRGB enumerates | Needs the OpenRGB server running elevated for SMBus access |
| **Native Sapphire Nitro Glow V3** | Sapphire Radeon GPUs | Direct ADL I2C via `atiadlxx.dll`, no admin, no OpenRGB — this is what makes GPU lighting survive every boot |
| **Logitech HID++ 2.0** | G-series keyboards and mice | Direct HID, no G HUB required |
| **Gigabyte IT8297 / IT5702** | Fan and ARGB strip controllers | Direct USB |

## Requirements

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- For motherboard and RAM lighting: an [OpenRGB](https://openrgb.org/) server running
  **elevated** (SMBus access needs admin). A scheduled task at logon with *Run with
  highest privileges* is the cleanest way — Prisma will start it for you if it is
  registered as `OpenRGB Server`.

> **Run exactly one OpenRGB instance.** Two servers initialising the same SMBus
> controllers will wedge a device — a stuck RAM stick that only a full power-off clears.
> Prisma's conflict watchdog is there to catch this.

## Build

```
dotnet build -c Release
```

Single dependency: [`NAudio.Wasapi`](https://github.com/naudio/NAudio) for the audio
level meter behind the Music effect.

> The assembly is deliberately still named `RGBCommander`: the single-instance mutex,
> the `%APPDATA%\RGBCommander` settings folder and the autostart registry entry all
> reference it, so renaming would orphan existing installs. "Prisma" is the product name.

## Command line

Prisma is single-instance; any of these launched while it is running are forwarded to
the live instance, which makes them ideal for a Stream Deck or a shortcut.

```
--toggle                 lights on / off
--off                    everything off
--effect <name>          static | rainbow | cycle | breathing | strobe | gradient | ...
--color <rrggbb>         6-digit hex
--brightness <1-100>
--speed <n>
--profile <name>
--next-profile
--show                   bring the window up
--minimized              start hidden in the tray
```

## License

MIT — see [LICENSE](LICENSE).
