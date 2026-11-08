#!/usr/bin/env python3
"""Checklist scanner for migrating one panel page to the page layer.

Usage:
    python tools/scan_page_migration.py PickVehicle [MoreModes...]

It reports every place that can still bypass a migrated page:
  1. `_mode == Mode.X` / `_mode = Mode.X`  -- special-case branches and direct jumps
  2. legacy state fields                    -- e.g. _pickSel, _pickPage, _presetSel
  3. legacy draw methods                    -- e.g. DrawPickVehiclePage(...)
Run it BEFORE and AFTER a migration; after the migration only the page-layer call sites
(`_pickPageUI?.Handle(...)`, `?.Draw(...)`) should remain.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src" / "WarThunderHud"
FORM = ROOT / "UI" / "Mfd" / "MfdFrameForm.cs"

# legacy fields per page mode (extend when a new page is migrated)
LEGACY = {
    "PickVehicle": ["_pickSel", "_pickPage", "_pickReturn", "ApplyPickedVehicle"],
    "Preset": ["_presetSel", "_presetTarget", "ApplyPreset"],
    "Confirm": ["_confirmSel", "RunPending", "CancelPending"],
    "EditNumber": ["_editBuffer", "_confirmSel", "CommitNumber"],
    "PitchDetect": ["_detectTimer"],
}


def scan(mode: str) -> int:
    mode_pat = re.compile(rf"Mode\.{mode}\b")
    legacy_names = LEGACY.get(mode, [])
    legacy_pat = re.compile(r"\b(" + "|".join(map(re.escape, legacy_names)) + r")\b") if legacy_names else None
    draw_pat = re.compile(rf"Draw{mode}Page\s*\(")

    hits = {"mode": [], "legacy": [], "draw": []}
    for line_no, line in enumerate(FORM.read_text(encoding="utf-8").splitlines(), 1):
        if mode_pat.search(line):
            hits["mode"].append((line_no, line.strip()))
        if legacy_pat and legacy_pat.search(line):
            hits["legacy"].append((line_no, line.strip()))
        if draw_pat.search(line):
            hits["draw"].append((line_no, line.strip()))

    print(f"\n=== {mode} ===")
    for title, key in (("Mode references", "mode"), ("legacy state", "legacy"), ("legacy draw", "draw")):
        rows = hits[key]
        print(f"-- {title}: {len(rows)}")
        for line_no, text in rows:
            print(f"   L{line_no}: {text}")

    danger = 0
    for line_no, text in hits["mode"]:
        if re.search(rf"_mode\s*=\s*Mode\.{mode}\s*;", text):
            print(f"   !! direct jump without page builder: L{line_no}")
            danger += 1
    for line_no, text in hits["legacy"]:
        if "_pageUI" in text or "_Page?" in text:
            continue
        print(f"   !! legacy state still used: L{line_no}")
        danger += 1
    for line_no, text in hits["draw"]:
        if "Draw(" not in text:
            print(f"   !! legacy draw still reachable: L{line_no}")
            danger += 1

    print(f"-- verdict: {'CLEAN' if danger == 0 else str(danger) + ' risky spot(s)'}")
    return danger


def main() -> int:
    modes = sys.argv[1:]
    if not modes:
        print(__doc__)
        return 2
    total = sum(scan(m) for m in modes)
    return 0 if total == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
