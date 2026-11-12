## v0.4.19 — Formation and data link overhaul (pre-release)

2026-11-12

> Everything since 0.4.8. This is a **pre-release**: multi-ship formation and the data link still need
> a real sortie to confirm. Single-player display features are unaffected.

### Three things worth knowing

1. **The lead can see every wingman's state.** A wingman writes its station into the data-link packet
   (which slot, on station / behind / off station, by how many metres), and the lead's HUD lists them
   below the status line:

   ```
   W1  ON STATION
   W2  BEHIND 480m
   W3  DATA ERR
   W4  OFFLINE 42s
   ```

2. **Two leads no longer share one table.** The packet gained a 12th field, "the lead I follow".
   A lead lists only the wings that name it, plus those that name nobody. A wingman fills the lead node
   ID once in the panel; a wrong ID shows as OFFLINE right under it.

3. **A batch of problems that looked like a broken game.** Half-started 8111, silently reset configs,
   a compass-less jet reading 0° of bank in a steady turn, and the main menu's side keys opening the
   wrong module — all fixed.

### Fixes

- **Main menu side keys off by one**: cell numbers (0/2/4/6/8 across two columns) were mixed with
  physical key numbers, so the second key opened the third module. Keys are registered by cell now;
  all ten modules line up.
- **A corrupt config silently reset everything**: one bad `hud-config.json` replaced the whole
  configuration with defaults, with no message and nothing to trace. The original is kept as
  `hud-config.json.bad` and the failure is printed.
- **Half-started 8111**: `/state` answers normally while `/indicators` and `/map_obj.json` return empty.
  Attitude silently degrades to estimation and formation loses own-ship position; only a client restart
  clears it. The panel's API state now separates `ok / not in battle / half ready / no indicators /
  no map`.
- **Compass-less aircraft (Rafale and friends) read 0° of bank in a steady turn**: bank is anchored on
  Ny now — 55° reads 55°, and it returns to zero when you roll level.
- **Ten symbol keys could not be bound**: `~ - = [ ] \ ; ' , . /` silently fell back to the default key
  after saving ("backtick does nothing"). The capture page shows the symbol now, and values already
  saved as `OEMTILDE` start working without re-binding.
- **Speed delta readout** moved under the speed box, aligned to that box's left edge.

### Formation

- **Two paths, one pipeline**: with a link the lead is identified by node; without one it falls back to
  the nearest friendly. PID, the two-stage guidance, vertical guidance and drawing are shared, so the
  two paths cannot drift apart.
- **Holding means being in the slot**, not just at the right range: dead astern at 500 m and 45° off at
  500 m are no longer both "on station".
- **The lead uses the wingman's own reported slot** for the expected range; with three ships, using the
  lead's own slot never matches.
- **Data error**: when a wing is in the formation but cannot produce a position or a verdict this frame,
  the lead's table says DATA ERR. It does not count towards "formation complete", and the lead no longer
  falls back to map range guessing whether the icon 800 m away is that wing.
- **Offline**: three seconds without a packet shows OFFLINE 42s; the row is dropped after 60 s. When
  nobody can be judged, the main line says the wingman is offline.
- **Formation-mode display**: real speed tape with a target-speed triangle, speed delta under the speed
  box, and the amber zero datum only in formation mode.

### Data link

```
WTDL2|id|alt|vy|hdg|turn|x|y|bank|role|slot,state,err|leadId
```

- Fields 7/8 (position) stay as **empty placeholders** when unknown; role and station are still sent and
  no field shifts position.
- Field 11 is `slot,state,err`; state `-1` means "in the formation, but nothing to report this frame".
- Field 12 is `leadId`: the lead node this aircraft follows, `0` = unspecified.
- Every addition is appended: old readers take the first six fields, new readers treat an old packet as
  0. Mixed versions can fly together.

Also:

- A lead now replies to **every node that has sent it a packet**. It used to reply only to the address
  typed in the panel, so the second wingman of a three-ship never received anything.
- Panel: formation is split into shared / wingman / lead pages, and the data link is its own module with
  two entries to the link status page. A read-only "lead online" row sits under the lead node ID.
- A wingman seeing two `role=1` nodes without a configured ID now gets
  "multiple leads · set node ID" on the HUD instead of watching the cue jump between them.
- `dlprobe` accepts an optional second argument (local node ID) and filters the wing table from a lead's
  point of view; every packet also prints `lead=`, so two machines can be checked without the game.

### Compatibility

- Config compatible: replace `WtHud.exe`, leave `hud-config.json` alone.
- `help-desc.json` was bumped: the old file is backed up as `.bak` and rewritten on start; local edits
  survive in the backup.
- Mixed versions work; all new fields are appended.
- The data link still uses UDP 48017.

### Known limitations

- **Multi-ship behaviour is not verified in game yet** — that is why this is a pre-release. The first
  sortie with two leads and their wings is the real test.
- Named formations have three slots (four aircraft with the lead); the friendly list keeps four.
- The wing table has no row cap; with more than five wings the extra rows run off screen and are clipped.
- The lead still resolves a wing's slot by pairing it with the nearest map icon, so two leads flying
  close together can pick up each other's wing (known, unchanged).

### Install / upgrade

1. Close WtHud and make sure no `WtHud.exe` is left in the task manager
2. Replace `WtHud.exe`
3. Join a match, then run it
4. Open the panel with F1 and check that API state reads `ok`; the data link page should list the nodes
