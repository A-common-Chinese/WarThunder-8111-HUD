# WtHud - War Thunder 8111 HUD overlay (M0)

Military-style head-up display overlay for War Thunder, driven entirely by the
game's official local API on `http://127.0.0.1:8111`. Read-only: no injection,
no memory reads, no file modification, no screen capture.

## Current state (milestone M0)

- Transparent, topmost, click-through overlay window (Direct3D 11 + Direct2D +
  DirectComposition), 60-70 fps at 3840x2160
- Telemetry: 25 Hz polling of `/state` + `/indicators` (the embedded server is
  single threaded, so requests are strictly serial)
- Vehicle capability detection: props/early jets expose a native artificial
  horizon, top-tier jets do not (they fall back to synthesized attitude)
- HUD symbology (angular layout, so 1080p and 4K look identical):
  pitch ladder + horizon, bank scale, speed tape (IAS + Mach), altitude tape
  (barometric altitude + vertical speed), heading tape, flight path marker,
  data block (G / AoA / fuel / altitude), attitude source label
- Attitude fusion: native horizon with online bias learning, or synthesized
  (pitch = flight path angle + AoA, bank = coordinated turn rate)
- Hotkeys: **F10** show/hide HUD, **F9** body-fixed mode (hides attitude symbology)

## Requirements

- .NET 8 SDK (this repository uses a portable SDK in `.tools/dotnet`)
- Windows 10/11, a GPU with DX11
- War Thunder running in **Fullscreen Window** (exclusive fullscreen cannot be
  overlaid), 8111 reachable (custom battles / test flight need "Allow web UI")

## Build

```powershell
./build.ps1
```

## Run

```powershell
./run-hud.ps1                # overlay until Ctrl+C / process killed
./run-hud.ps1 -Seconds 60    # auto close after 60 s
./run-hud.ps1 -Demo          # show HUD with synthetic data (no game needed)
```

Other diagnostic modes:

```powershell
./run-hud.ps1 -Mode probe -Args 5         # live telemetry dump
./run-hud.ps1 -Mode replay -Args <file>   # replay a captured recording
./run-hud.ps1 -Mode selftest              # parser unit checks
./run-hud.ps1 -Mode fusiontest            # attitude fusion scenarios
./run-hud.ps1 -Mode preview -Args out.png # render symbology to PNG
```

## Layout / calibration notes

- Symbology is laid out in degrees relative to the boresight and converted with
  px/degree. The camera field of view is NOT exposed by the API; the current
  default assumes 45 deg vertical FOV (`HudCalibration.VerticalFovDeg`).
  The F7 zero / F8 gain hotkey calibration lands in M2.
- Altitude uses the API's `H, m` (barometric altitude) for now.
- On the ground the API reports garbage AoA/AoS (measured -28.9 / -36.4 deg), so
  the parser zeroes them and the HUD hides itself while not airborne.

## Known gaps

- View detection (hide in cockpit / nose view) is not implemented yet - planned
  for M4 using a passive V-key cycle counter, because 8111 exposes no camera state
- Overlay refresh is ~45-70 fps at 4K; dirty-rectangle rendering is planned for M5
- Synthetic bank angle is intentionally slew limited; transients during hard
  manoeuvring are not accurate (shown as `AHRS EST`)

## Project layout

```
src/WarThunderHud/
  Telemetry/   8111 client, parser, capability detection, attitude fusion
  Hud/         symbology primitives, geometry, calibration
  Graphics/    D3D11/D2D canvas, HUD drawer, PNG writer
  Overlay/     Win32 window, DirectComposition setup, hotkeys
```

## Research / design docs

- `../wt8111-research/00-调研笔记.md` - API survey
- `../wt8111-research/01-决策与计划.md` - decisions log
- `../wt8111-research/02-实施方案.md` - implementation plan
- `../wt8111-research/03-视角实验结论.md` - view-detection experiment result
