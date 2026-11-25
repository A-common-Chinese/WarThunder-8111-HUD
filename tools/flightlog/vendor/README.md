# vendor/ — 离线 PDF 回退（方案 B）内联的第三方库

报告「保存 → PDF 完整版」在**找不到本机 Edge/Chrome**（方案 A 预生成失败）时，
改为在浏览器内用下面两个库逐页渲染 9 页 `.page` 并直接下载 `report.pdf`，
全程离线、不联网。

| 文件 | 版本 | 大小 | 许可证 | 来源 |
| --- | --- | --- | --- | --- |
| `jspdf.umd.min.js` | 2.5.2 | 365730 B | MIT（`licenses/jspdf-LICENSE.txt`） | https://cdn.jsdelivr.net/npm/jspdf@2.5.2/dist/jspdf.umd.min.js |
| `html2canvas.min.js` | 1.4.1 | 198689 B | MIT（`licenses/html2canvas-LICENSE.txt`） | https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/dist/html2canvas.min.js |

SHA-256（生成报告时会校验，见 `selftest.py` 的 vendor 断言）：

```text
jspdf.umd.min.js       85ba2cc3ff858a20fa49fe6e457bec863ea40b55a9f3725e58a940e62f6f61a4
html2canvas.min.js     e87e550794322e574a1fda0c1549a3c70dae5a93d9113417a429016838eab8cb
```

## 打包方式

`assets.py` 是上面两个 min.js 的 base64 副本（由 `vendor/build_assets.py` 生成，
在 `tools/flightlog` 下运行 `python vendor/build_assets.py` 即可重新生成）。
`flightlog/vendor` 是一个普通 Python 包，被 `report.py` 以
`from .vendor import assets` 静态引用，因此 `build_exe.bat` 里的
`--collect-submodules flightlog` 会自动把它打进 `FlightLog.exe`（onefile），
**不需要**额外的 `--add-data`。

## 运行方式

* 方案 A（有 Edge/Chrome）：报告 `<body data-pdf="report.pdf">`，「导出 PDF」直接
  `<a download>` 下载预生成 PDF，**不注入**下面这两个库（报告更小）。
* 方案 B（无浏览器 / 预生成失败）：报告不写 `data-pdf`，注入两段内联
  `<script>`（base64 解码后），按钮改为逐页 `html2canvas` → `jsPDF` 生成
  `report.pdf`；系统字体渲染，分页按 `.page` 元素。
* 两个库都是 MIT；本仓库发行时随包附带许可证原文（见 `licenses/`）。
