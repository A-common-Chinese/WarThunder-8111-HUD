## v0.3.3 — Kill banner (SPLASH) + alert fixes

### New
- **Kill banner (SPLASH)**: when *you* shoot down a **player**, a solid green banner appears above
  screen centre — 「击落」 if the alert language is Chinese, `SPLASH` if English — with the victim's
  name shown below in plain green text. **No animation**: appears instantly, disappears after 1.2 s
  (multiple kills queue up).
  - Only "shot down" (`击落了`) counts: AI / ground "destroyed" messages, your own death and other
    players' kills are ignored.
  - 8111 `/hudmsg` is polled at 4 Hz, de-duplicated by id; parsing/network errors stay silent.
- **Two new rows on Display page 2**: `Splash alerts` toggle and `Player name` text entry.
  - Player name entry: type ASCII directly, paste Chinese/special characters with `Ctrl+V`,
    confirm to save / back to cancel.
  - The banner does nothing while the name is empty (8111 has no reliable self identifier).
- **`PULL UP lead` row on the Alerts page**: seconds, default **5.0**, range 1.5–10;
  `TTI < lead` triggers the warning (previously hard-coded to 3.5 s, which was too late).
  Supports per-airframe overrides: **airframe > profile > global**.

### Fixes
- **Auto write layer did not work**: editing in Auto mode still wrote to the global layer, so you had
  to pick an airframe manually to get an override. Now: Auto + detected airframe → write that
  airframe's override; explicit global → global; no airframe detected → global (with an amber hint
  in the editor).
- **F/A-18A flashed `STALL` as soon as you pulled**: the cockpit AOA indexer triggered on a single
  frame above 0.9, and the F/A-18A indexer jitters around that value. It now requires 200 ms above
  0.9 and drops out below 0.85; `stall level` log lines gained `cal/idx` fields for diagnosis.

### Changes
- **Removed the "overspeed mode" switch**: overspeed has always checked IAS and Mach
  simultaneously (either limit triggers the warning); that switch only affected log text, so it is
  gone from the panel and config.
- SPLASH banner: flatter box, fixed above the ladder centre, CJK "击落" laid out with measured letter
  spacing and a snug box width.

### Install / upgrade
1. Extract to its own folder (not the game directory)
2. Set the game display mode to **Fullscreen Window (borderless)**
3. Join a match, then run `WtHud.exe`
4. Press `F1` to open the panel, `F1` again to close
5. To use the kill banner: `F1` → Display page 2 → set `Player name` (must match your in-game name)
   → enable `Splash alerts`

> Upgrading from v0.3.1 / v0.3.2: just replace `WtHud.exe`; the config stays compatible
> (a leftover `overspeedByMach` key is ignored).

### Notes
- The banner only fires for **your own air kills against players**; AI kills, ground targets and
  your own death are never announced.
- Name matching is case-insensitive, but use the exact in-game spelling to be safe.
- A larger `PULL UP lead` warns earlier; 6–8 s is a good starting point for high-speed dives.
