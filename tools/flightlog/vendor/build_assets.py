# -*- coding: utf-8 -*-
"""Regenerate vendor/assets.py from the vendored minified JS files.

用法（在 tools/flightlog 目录下）::

    python vendor/build_assets.py

产物：``vendor/assets.py``（两个 min.js 的 base64 + 版本 + sha256 + 字节数）。
``report.py`` 以 ``from .vendor import assets`` 静态引用它，PyInstaller 的
``--collect-submodules flightlog`` 会自动把它打进 FlightLog.exe（onefile）。
"""
from __future__ import annotations

import base64
import hashlib
from pathlib import Path

VENDOR = Path(__file__).resolve().parent


def _lines(name: str, raw: bytes) -> list[str]:
    digest = hashlib.sha256(raw).hexdigest()
    print("%s: %d bytes sha256=%s" % (name, len(raw), digest))
    encoded = base64.b64encode(raw).decode("ascii")
    out = ["%s_B64 = (" % name]
    for i in range(0, len(encoded), 120):
        out.append('    "%s"' % encoded[i:i + 120])
    out.append(")")
    return out


def main() -> None:
    jspdf = (VENDOR / "jspdf.umd.min.js").read_bytes()
    h2c = (VENDOR / "html2canvas.min.js").read_bytes()
    head = [
        "# -*- coding: utf-8 -*-",
        '"""Vendored offline PDF fallback assets (generated, do not edit by hand).',
        "",
        "离线 PDF 回退（方案 B）内联用的第三方库，base64 存放，保证 PyInstaller",
        "--onefile 打包时无需 --add-data 也能随包带走；原始文件在 vendor/ 下，",
        "许可证见 vendor/licenses/，来源 / 版本 / 校验见 vendor/README.md。",
        "",
        "重新生成：python vendor/build_assets.py",
        '"""',
        "",
        "from __future__ import annotations",
        "",
        'JSPDF_VERSION = "2.5.2"',
        'HTML2CANVAS_VERSION = "1.4.1"',
        'JSPDF_SHA256 = "%s"' % hashlib.sha256(jspdf).hexdigest(),
        'HTML2CANVAS_SHA256 = "%s"' % hashlib.sha256(h2c).hexdigest(),
        "JSPDF_BYTES = %d" % len(jspdf),
        "HTML2CANVAS_BYTES = %d" % len(h2c),
        "",
    ]
    out = head + _lines("JSPDF", jspdf) + [""] + _lines("HTML2CANVAS", h2c) + [""]
    target = VENDOR / "assets.py"
    target.write_text("\n".join(out), encoding="utf-8", newline="\n")
    print("wrote", target, target.stat().st_size, "bytes")


if __name__ == "__main__":
    main()
