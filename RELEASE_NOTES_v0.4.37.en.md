## v0.4.37 — First stable release: units + panel scaling

2026-11-15

> This is the **first stable release** of War Thunder HUD (8111). The core display
> (reading 8111, drawing attitude, the tapes, alerts, navigation, keybinds, and the
> panel) is stable for everyday use.
>
> **Note: formation assist and the data link are still beta.** They are off by
> default and do not affect single-player use — ignore them if you fly alone. Read
> "Known limitations" at the bottom before using them.

### What is new

**1. Units (speed and altitude/distance set separately)**

Two independent switches at the top of the DISPLAY page, matching how the game
splits them:

| Switch | Options | Affects |
|---|---|---|
| Speed unit | `km/h` / `knots` | Speed tape and ticks, target speed, accel/decel amount |
| Altitude/distance unit | `m / km` / `ft / NM` | Altitude tape and ticks, `V/S`, formation ranges, nav line |

- **Display and input only**: internal state, thresholds, data-link packets and the
  config file stay metric. Switching back never drifts a value, and never
  desynchronises a wingman who has not switched.
- Imperial ticks are regenerated on whole knots and hundreds of feet, not by
  relabelling the metric grid as 27/54/81.
- Distances above 1 NM use NM, below it feet.

**2. Panel scaling + 4K bezel**

- A new "panel scale" row on the PANEL page: 100% (auto) / 90 / 80 / 70 / 60%.
- The bezel artwork is now 4K (3840×3073), so the panel is larger and sharper on
  1440p / 4K displays.
- Scaling only shrinks; to get bigger, use a higher-resolution screen — it fills
  automatically.

**3. Fix: several settings silently dropped typed input**

The `G` (target G / turn tolerance) and `%` (done/drop percentage) rows used to
silently discard a typed value as invalid, with no message. Fixed, and an invalid
entry now reports "not a number — not applied".

### Self-test

**419 checks, all passing** (0.4.36 had 354). Covers unit conversion, panel
round-trips, typed-input regression, one actual render each in metric and imperial,
and panel scaling across screen/bezel combinations.

### Install / upgrade

1. Extract to its own folder (not inside the game directory).
2. Set the game display mode to borderless windowed / windowed fullscreen.
3. Enter a battle first, then run `WtHud.exe`.
4. Press `F1` in game to open the panel.

**Keep `hud-config.json` when upgrading**: all your settings carry over, and units
default to metric (unchanged).

### Known limitations

**Formation assist and data link (beta, off by default)**:

- Multi-ship behaviour is verified by the self-test and the formation simulation,
  but still needs a real sortie before it is called final.
- The lead's table depends entirely on wing self-reports: a wingman that never
  enables formation assist never shows up in it.
- Preset geometry (especially the far echelon slot 3 station) is confirmed in the
  panel and the simulation only.

**Units**:

- Not converted: Mach, G, degrees, percent, pixels, milliseconds, and `ACC` (m/s²,
  an engineering readout).
- The game's own speed/altitude units are a separate in-game setting; this tool only
  controls the HUD display.

---

*See `SHA256.txt` for the checksum.*
