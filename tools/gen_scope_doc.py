#!/usr/bin/env python3
"""Generate docs/settings-scope.md from Hud/SettingScopes.cs (single source of truth).

Usage: python tools/gen_scope_doc.py
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "WarThunderHud" / "Hud" / "SettingScopes.cs"
OUT = ROOT / "docs" / "settings-scope.md"

ENTRY = re.compile(r'\["(row\.[A-Za-z]+)"\]\s*=\s*\(SettingScope\.(\w+),\s*"([^"]*)"\)')

text = SRC.read_text(encoding="utf-8")
rows = ENTRY.findall(text)

airframe = [(k, why) for k, scope, why in rows if scope == "Airframe"]
global_rows = [(k, why) for k, scope, why in rows if scope == "Global"]

lines = [
    "# 参数作用域（自动生成，请勿手改）",
    "",
    "生成命令：`python tools/gen_scope_doc.py`  ",
    "来源：`src/WarThunderHud/Hud/SettingScopes.cs`",
    "",
    "优先级：**机型覆盖 > 全局 > 内置默认**",
    "",
    f"## 按机型覆盖（{len(airframe)} 项）",
    "",
    "| 行 key | 原因 |",
    "|---|---|",
]
for key, why in sorted(airframe):
    lines.append(f"| `{key}` | {why} |")

lines += [
    "",
    f"## 显式登记为全局（{len(global_rows)} 项）",
    "",
    "| 行 key | 原因 |",
    "|---|---|",
]
for key, why in sorted(global_rows):
    lines.append(f"| `{key}` | {why} |")

lines += [
    "",
    "## 默认规则",
    "",
    "- 未在注册表中出现的行 = 全局",
    "- 开关（Toggle）与点击式枚举一律全局：没有编辑页，无法选择作用域",
    "- `row.pitchSign` 是唯一按机型的枚举，由「自动判断俯仰方向」或手动选择机型写入",
]

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"wrote {OUT} ({len(airframe)} airframe, {len(global_rows)} global)")
