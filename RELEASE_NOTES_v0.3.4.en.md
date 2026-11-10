## v0.3.4 — Hotkey fixes (elevated game / panel open / after SAVE)

### Fixes
- **All in-game hotkeys were dead when War Thunder runs as administrator** (F1 panel, F10 hide, F9 body-fixed,
  F7/F8/F11 … did nothing until you pressed Win to switch to the desktop). Cause: once the game process is elevated,
  Windows UIPI blocks global hotkeys of non-elevated processes while the elevated window has focus.
  WtHud now compares its own privilege level with the game's on startup (and when the game restarts):
  if the game is elevated and WtHud is not, it **relaunches itself as administrator** (one UAC prompt).
  If the prompt is declined, the log tells you to run WtHud as administrator manually.
- **HUD hid itself when the F1 panel opened**: the visibility check ignored the "settings panel open" state, so the
  panel taking focus hid the overlay. The HUD now stays visible while the panel is open, so changes can be previewed live.
- **Every hotkey failed to re-register after saving the panel** (`hotkey N ... already in use`): hotkeys must be
  unregistered/re-registered on the thread that registered them; the MFD panel thread could not unregister the
  overlay thread's registrations. Re-registration now happens on the overlay thread, the log includes the Win32 error
  code, and failures are retried up to 3 times.

### Notes
- If the game runs as administrator, starting WtHud shows one UAC prompt (accept it once; hotkeys then work in game).
- If you decline the prompt, the HUD still draws but in-game hotkeys stay unavailable; the log explains how to run it
  as administrator manually.
- No settings/config format changes — just replace the exe.

### Install / upgrade
1. Replace `WtHud.exe` (config stays compatible)
2. Join a match, then run `WtHud.exe`
3. If the game is elevated: accept the UAC prompt
4. Verify with F1 (panel) and F10 (hide HUD) in game
