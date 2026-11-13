## v0.4.25 — One formation model, and a choice of guidance mode (pre-release)

2026-11-13

> Everything since 0.4.19. This is a **pre-release**: both the station geometry and what the climb stage
> shows have changed, so fly a sortie on CUSTOM before calling it final. Single-player display features
> are unaffected.

### Three things worth knowing

1. **One formation model.** A "slot" used to mean where you sit under a named shape and merely which
   wing you are under CUSTOM — the same word for two things — while SIDE only worked under CUSTOM.
   Now four numbers decide everything: side, lateral offset, back distance, station altitude.
   Named shapes (echelon / column / diamond) are just buttons that fill those four numbers in.

2. **Guidance mode is a choice.** One-stage (default) shows circle, heading, range and throttle
   together; two-stage leaves only a climb rate while the altitude gate is unmet. The old
   "climb first, don't get distracted" behaviour is now the second option.

3. **Old configs migrate themselves.** The trail-specific range folds into a single back distance and
   **the station you currently fly does not move**; the original file is kept as `hud-config.json.bak`.

### Formation

| Item | How it works now |
|---|---|
| Geometry | Side + lateral + back + altitude, and nothing else |
| Slant range | `sqrt(lateral² + back²)`; the read-only panel value is the judged value |
| Preset | Fills the four numbers in; the shape name is only a label |
| Editing a value | Drops the label to CUSTOM immediately |
| Switching back to CUSTOM | Restores the numbers you had before the preset (snapshot persists) |
| Number | A number only (W1/W2/W3), no geometry |
| Spacing | One base interval per preset, scales the whole shape, and does **not** drop to CUSTOM |

- **Fixed: the judged range for a CUSTOM side slot.** All three stations used to be judged against the
  trail distance, so widening the lateral offset produced "the lead says on station, the wing never
  reaches formation set". It now uses the real slant range.
- **Fixed: three CUSTOM wingmen all reporting W1.** The number is always editable, so the lead's table
  can tell them apart.
- **The lead no longer derives a station from a wing's number** (numbers no longer imply geometry);
  its fallback state machine uses its own config.

### Guidance

- **Guidance mode**: one-stage (default) / two-stage. The gate is still ALT GATE, 30 m by default,
  exiting at 60% for hysteresis.
- **Climb range** (only meaningful in two-stage): `0` = show at any distance (default),
  `-1` = hide, a positive value = show beyond that range.
- In one-stage the PID integrates during the climb again, so throttle advice is no longer frozen.

### Compatibility and migration

| Change | Effect |
|---|---|
| `formationRangeM` folds into `formationStationBackM` (default 370 → 400) | The trail station is unchanged; **switching to a side slot now gives 427 m instead of 399** (150/400 is the real geometry). Set the back distance to 370 to get 399 back |
| New fields: `formationVerticalFirst`, three preset spacings, four snapshot values | Old files fall back to defaults; nothing to fill in |
| First launch migration | Original kept as `hud-config.json.bak`; `formationModel` walks 0 → 3 and then stops |

### Known limitations

- **Multi-ship behaviour has not been verified in game.** The claims above come from the self-test
  (292 checks) and the formation simulation; a real sortie is still needed.
- The lead's table depends entirely on wing self-reports: a wingman that never enables formation assist
  never appears in it.
- Preset geometry is verified in the panel and the simulation only; the feel still needs a sortie
  (especially echelon slot 3 at 1545 m).

### Install

1. Extract to its own folder (do not put it inside the game directory).
2. Set the game to borderless / fullscreen window.
3. Join a match first, then double-click `WtHud.exe`.
4. Press `F1` in game to open the panel.

When upgrading over an old install, keep `hud-config.json` — it migrates on first launch.
