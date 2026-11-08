#!/usr/bin/env python3
"""Checklist scanner for migrating one panel page to the page layer.

Usage:
    python tools/scan_page_migration.py PickVehicle [MoreModes...]

It reports every place that can still bypass a migrated page:
  1. `_mode == Mode.X` / `_mode = Mode.X`  -- special-case branches and direct jumps
  2. legacy state fields                    -- e.g. _pickSel, _pickPage, _presetSel
  3. legacy draw methods                    -- e.g. DrawPickVehiclePage(...)

What counts as legacy: only state that still exists for the old switch -- a bypass field
such as `_pickSel` / `_confirmSel` / `_editBuffer`, or an old draw method such as
`Draw<Mode>Page(...)`. Methods handed to a page constructor as `onOk` / `onCancel` /
`onApply` / `onCommit` callbacks are the page layer's own building blocks, not leftovers,
so they are NOT registered as legacy (e.g. RunPending, CancelPending, CommitPreset,
CommitNumber).

Exception: a `_mode = Mode.X;` assignment placed inside a method whose name starts with
`Enter` (e.g. `EnterSnapshots`, `EnterSnapshotConfirm`) is a page-constructor entry point
introduced by the migration, not a direct jump, so it is never reported as a risky spot.

Run it BEFORE and AFTER a migration; after the migration only the page-layer call sites
(`_pickPageUI?.Handle(...)`, `?.Draw(...)`) should remain.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src" / "WarThunderHud"
FORM = ROOT / "UI" / "Mfd" / "MfdFrameForm.cs"

# legacy bypass state per page mode (extend when a new page is migrated).
# Only true leftovers belong here: bypass fields and old helper functions. Methods passed to
# a page constructor as onOk / onCancel / onApply / onCommit are page-layer callbacks and are
# deliberately NOT listed (e.g. RunPending, CancelPending, CommitPreset, CommitNumber).
LEGACY = {
    "PickVehicle": ["_pickSel", "_pickPage", "_pickReturn", "ApplyPickedVehicle"],
    "Preset": ["_presetSel", "_presetTarget"],
    "Confirm": ["_confirmSel"],
    "EditNumber": ["_editBuffer", "_confirmSel"],
    "PitchDetect": ["_detectTimer"],
    "Snapshots": [],          # 快照页由页面层整体接管，没有遗留状态字段
}

# MfdFrameForm 的方法声明行：4 格缩进 + 访问修饰符 + 返回类型 + 方法名 + "("
METHOD_DECL = re.compile(
    r"^\s{4}(?:private|public|internal|protected)\s+"
    r"(?:(?:static|async|override|virtual|sealed)\s+)*"
    r"[A-Za-z_][\w<>\[\],\.\?]*\s+([A-Za-z_]\w*)\s*\("
)


def enter_method_ranges(lines):
    """记录所有 `Enter*` 方法的行范围（声明行到下一个方法声明前一行）。

    迁移后页面由 `Enter<Page>()` 构造，`_mode = Mode.X;` 出现在这类方法体内属于合法的
    页面构造入口；扫描器用它来豁免 direct jump 误报。
    """
    decls = []
    for line_no, line in enumerate(lines, 1):
        match = METHOD_DECL.search(line)
        if match:
            decls.append((line_no, match.group(1)))

    ranges = {}
    for i, (start, name) in enumerate(decls):
        if not name.startswith("Enter"):
            continue
        end = decls[i + 1][0] - 1 if i + 1 < len(decls) else len(lines)
        ranges[name] = (start, end)
    return ranges


def in_enter_method(ranges, line_no):
    """该行是否落在某个 `Enter*` 方法体内。"""
    return any(start <= line_no <= end for start, end in ranges.values())


def scan(mode: str) -> int:
    mode_pat = re.compile(rf"Mode\.{mode}\b")
    legacy_names = LEGACY.get(mode, [])
    legacy_pat = re.compile(r"\b(" + "|".join(map(re.escape, legacy_names)) + r")\b") if legacy_names else None
    draw_pat = re.compile(rf"Draw{mode}Page\s*\(")

    lines = FORM.read_text(encoding="utf-8").splitlines()
    enter_ranges = enter_method_ranges(lines)

    hits = {"mode": [], "legacy": [], "draw": []}
    for line_no, line in enumerate(lines, 1):
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
            # 页面构造入口（Enter* 方法体内）的赋值是迁移后的正常写法，不算 direct jump
            if in_enter_method(enter_ranges, line_no):
                continue
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
