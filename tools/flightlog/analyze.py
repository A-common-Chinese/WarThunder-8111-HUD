#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""``python analyze.py ...`` 便捷入口（等价于 ``python -m flightlog ...``）。

本文件不依赖当前工作目录：它把自己所在目录的父目录加入 ``sys.path``，
然后交给 :mod:`flightlog.__main__` 处理命令行参数。

用法::

    python analyze.py <log.jsonl> --out out/report.html
    python analyze.py <log.jsonl> --summary
"""

from __future__ import annotations

import sys
from pathlib import Path

_TOOL_DIR = Path(__file__).resolve().parent
_PARENT = _TOOL_DIR.parent
if str(_PARENT) not in sys.path:
    sys.path.insert(0, str(_PARENT))

from flightlog.__main__ import main  # noqa: E402  (必须在 sys.path 调整之后)

if __name__ == "__main__":
    raise SystemExit(main())
