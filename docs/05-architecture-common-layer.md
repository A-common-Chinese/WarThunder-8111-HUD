# 通用层架构（Common Layer）

本文描述 WtHud 的框架层：配置如何生效、面板如何声明、HUD 如何排版与绘制、
以及扩展时必须遵守的边界。**它讲的是「框架怎么用」，不描述具体功能现状**——
版本变更见 `CHANGELOG.md`，界面与操作说明见 `使用教程.md`。

代码规模：69 个 `.cs`、25788 行。主要文件分工：

| 文件 | 行数 | 职责 |
|---|---:|---|
| `Program.cs` | 5491 | 主循环、命令行模式（selftest/preview/dumpgeo/…） |
| `Hud/HudGeometry.cs` | 2233 | HUD 排版：数据 → 图元列表 |
| `UI/Mfd/MfdFrameForm.cs` | 2373 | 面板窗体：绘制、命中、键位、手输、页面缓存 |
| `Program.SelfTest.cs` | 1884 | 自检断言（`partial class Program`） |
| `UI/Mfd/MfdPages.cs` | 1313 | 页面与行的声明 |

---

## 1. 分层结构

```
┌─ 数据源 ─────────────────────────────────────────────────┐
│ Telemetry/   8111 轮询 → TelemetrySnapshot（数据契约）      │
│              AttitudeFusion（姿态融合）                     │
│              MapData（地图/友军）、DataLink（节点互传）      │
└────────────────────────┬────────────────────────────────┘
                         │ TelemetrySnapshot + AttitudeSolution
┌─ 排版层（纯计算）───────▼────────────────────────────────┐
│ Hud/HudGeometry.Build(snap, att, w, h) → List<HudPrim>   │
│   坐标以「度」为单位，pxDeg = EffectivePxPerDeg(height)     │
│ Hud/Units   单位换算（仅影响显示文本与刻度）                 │
└────────────────────────┬────────────────────────────────┘
                         │ List<HudPrim>：Line/Polyline/Circle/Arc/Text
┌─ 绘制层（唯一碰图形的层）▼──────────────────────────────┐
│ Graphics/HudDrawer  把图元画到任意 Direct2D 目标            │
│   ├ 实时叠加：Overlay/OverlayWindow（交换链、TopMost）       │
│   └ 离屏出图：Graphics/HudCanvas（D3D11/D2D，写 PNG）        │
└─────────────────────────────────────────────────────────┘

┌─ 配置层 ────────────────┐  ┌─ 面板框架层 ──────────────────┐
│ Hud/HudConfig   持久化   │  │ UI/Mfd/MfdPages   页面声明      │
│ Hud/SettingScopes 作用域 │  │ UI/Mfd/MfdFrameForm 窗体/输入   │
│ Hud/SettingsRouter 覆盖  │  │ Mfd/Controls/MfdPageLayout 矩形 │
│ Hud/EffectiveSettings    │  │ Mfd/MfdGeometry  底图几何       │
│ Hud/ConfigSnapshots 快照 │  │ UI/L10n          多语言         │
└─────────────────────────┘  │ Mfd/HelpDescriptions 帮助文本   │
                             └────────────────────────────────┘
```

**职责边界（本项目最重要的结构性约定）**：排版层只产出 `HudPrim` 纯数据，不调用任何图形 API；
绘制层只消费图元，不计算任何坐标。由此带来两个能力：同一套排版代码同时服务实时叠加与离屏出图；
自检可以在无窗口、无游戏的前提下断言坐标与文本，这是全部 463 条断言能够成立的前提。

---

## 2. 配置层

### 2.1 数据与文件

`HudConfig` 是唯一的持久化模型，序列化到 exe 同目录的 `hud-config.json`。

运行时文件：

| 文件 | 生成时机 | 用途 |
|---|---|---|
| `hud-config.json` | 首次运行 | 全部设置 |
| `airframes.json` | 首次运行 | 机型档案（能力标记、校准值） |
| `help-desc.json` | 首次运行 | 帮助文本，可手工编辑 |
| `temp/*-save.json`、`_cancel.json`、`*-restore.json` | SAVE / 打开面板 / 恢复 | 配置快照与恢复审计 |
| `wthud-overlay.log` | 每次启动 | 运行日志；首行为独立版本号（`WtHud 0.4.38`） |

### 2.2 生效值解析

数值类配置按三层解析，优先级从高到低：

```
机型覆盖 perVehicleOverrides  →  机型档案 airframes.json  →  全局 hud-config.json
        ValueSource.Override        ValueSource.Profile        ValueSource.Global
```

解析入口集中在 `Hud/EffectiveSettings.cs`，面板详情页展示的「（机型）/（档案）/（全局）」
即 `ValueSource` 的呈现。

### 2.3 作用域登记

`Hud/SettingScopes.cs` 的 `Map` 是「该参数能否按机型区分」的唯一来源，**默认值为 Global**：
未登记的项永远不会隐式变成按机型。新增按机型参数必须显式登记并写明理由。

面板编辑的目标层由 `Hud/EditScope.cs` 决定：自动（跟随识别到的机型）、显式全局、
显式指定机型三态。

### 2.4 写盘与快照

`HudConfig.SaveTo` 先写 `.tmp` 再 `File.Replace` 替换，返回 `SaveResult`；
失败时面板停留原页并提示具体原因（无写权限 / 文件被占用或磁盘只读）。

> 该实现取代了早期 `File.WriteAllText` + 空 `catch` 的写法。后者在目录只读或磁盘满时
> 表现为「按了 SAVE 看似成功、重启全部丢失」，且中途崩溃会留下截断的 JSON。

### 2.5 深拷贝约定

`HudConfig.Clone()` 用于面板草稿。`MemberwiseClone` 只复制引用，因此每个引用类型属性
都必须在 `CopyReferenceTypesFrom` 中显式深拷贝：

```csharp
PerVehicleOverrides = new(...);   // 字典逐层复制
Language = src.Language.Clone();
Mfd      = src.Mfd.Clone();
Layout   = src.Layout.Clone();
```

遗漏的后果是草稿与生效配置共用同一对象：面板里改动会绕过 SAVE 直接生效。
自检中 `clone shares no reference property` 通过反射遍历全部引用类型属性做兜底检查。

---

## 3. 面板框架层

### 3.1 页面模型

全部页面集中于 `UI/Mfd/MfdPages.cs`，`MfdPages.Build()` 返回十一个顶层模块：
显示 / 编队 / 数据链 / 姿态与校准 / 告警 / 按键 / 面板·语言 / 机型 / 回归快照 / 记录 / 诊断。

> 需要自定义绘制或自定义输入的页面（回归快照、记录、帮助、确认…）不建 `MfdPage`，而是
> `MfdPageBase` 子类，由 `MfdFrameForm` 的 `Mode` 分支直接驱动；列表行与命中矩形仍只从
> `MfdPageLayout` 取，保证「画在哪里就能点在哪里」。

一个页面是一份声明（`MfdPage`）：

| 字段 | 含义 |
|---|---|
| `TitleKey` / `MenuKey` / `MenuHintKey` | 标题、首页菜单名、菜单提示（查 `L10n.Screen`） |
| `ParentTitleKey` | 二级页填父页标题，标题行渲染为「父 › 子」 |
| `Key` | 缓存标识，缺省取 `TitleKey` |
| `NoCache` | 内容会变的页面必须置 `true` |
| `Items` | 行列表，两列 × 五行、行优先填充 |
| `LiveCount` | 动态页在首页显示的数量 |

`NoCache` 的必要性来自实现细节：`MfdItemsPage` 在构造时即绑定 `Items`，
缓存住一个动态页等价于把首次打开的内容冻结。已知故障形态是「摘要显示有数据、进入后为空」。
动态页应一律置 `true`，不要通过把内容拼进 `Key` 规避——`Key` 同时是帮助页与详情页的标识。

### 3.2 行模型

行的统一取值口径在 `MfdItem.ValueText`（`MfdPages.cs:192`）。需要自定义显示时使用
`ValueOverride`，不应修改该 switch 本身。

| 工厂 | `ItemKind` | 说明 |
|---|---|---|
| `Toggle` | Toggle | 开关，值列 on/off |
| `Number` | Number | 数值行，支持手输、步进；`quantity` 决定单位换算 |
| `Enum` | Enum | 固定选项循环 |
| `Text` | Text | 文本（ASCII 直接输入，中文用 Ctrl+V） |
| `Key` | KeyBinding | 键位捕获 |
| `Preset` | Preset | 颜色等预设 |
| `Action` / `ActionDirectMsg` | Action | 立即执行 / 附 toast |
| `Submenu` | Submenu | 二级页，`summary` 显示于值列 |
| `Info` | ReadOnly | 只读行（诊断值、余量） |
| `Airframe` | Detail | 机型详情 |

联动项使用 `EnabledWhen`（`Func<HudConfig,bool>` 而非 `bool`，保证同页切换即时反映），
不可用时的说明文本用 `DisabledText`。

### 3.3 单位感知输入

`MfdQuantity` 标注行的物理量，决定显示与输入按哪套单位换算：

| 取值 | 换算 |
|---|---|
| `None` | 不换算（°、%、G、px、ms、倍数） |
| `LengthM` | 米 ↔ 英尺 |
| `LengthKm` | 公里 ↔ 海里 |
| `SpeedKmh` | km/h ↔ 节 |
| `SpeedMs` | m/s ↔ ft/min |

**存储恒为公制**：`Step`、`Min`、`Max` 亦是公制，`Quantity` 只影响呈现。
`LengthM` 与 `LengthKm` 分开，是因为同一换算率会让「12 km」在英制下显示为 39370 ft。

手输的取值必须来自数值本身，不得从显示字符串剥离单位后缀——后者对 `G`、`%` 这类后缀
会解析失败，而早期实现的失败分支不写入、不提示、直接返回列表，表现为「输了没反应」。
解析失败必须给出可见提示。

### 3.4 布局与命中同源

`UI/Mfd/Controls/MfdPageLayout.cs` 是控件矩形的唯一来源，绘制与命中判定都向它取值，
因此不存在「画在一处、点不到」的偏移。缩放系数为 `s = 面板宽 / 1330`，
行高固定 40 DIP，与既有绘制保持一致。

底图几何以归一化坐标存于 `Resources/mfd-geometry.json`（基准 1330×1064），
来自 Blender 渲染，更换底图分辨率无需修改 JSON。

面板尺寸按 `DesignWidth(1330) × fit` 计算，**不按底图像素**——否则更换 2x 底图会使窗口翻倍。
缩放上限由底图分辨率决定：`MaxFitFor = 底图宽 / 1330`（1x → 1.0，2x/4K → 2.0）。
强制缩放值同样需要夹进工作区：窗口一旦超过屏幕，Windows 会截小 `ClientSize`，
而布局仍按原尺寸计算，结果是边框大于窗口、内容被裁。

### 3.5 多语言

三个界面面各自独立选择语言：

| 面 | 常量 | 默认 |
|---|---|---|
| 机框丝印（顶排/底排按键名） | `L10n.Panel` | En |
| MFD 屏幕内容 | `L10n.Screen` | Zh |
| HUD 告警文字 | `L10n.Alerts` | En |

查表入口 `L10n.Get(LangScope.Screen, key)`。键名约定：行标签 `row.*`、页面 `page.*`、
菜单提示 `menu.hint.*`、选项 `opt.*`、单位 `unit.*`。新增行必须同时提供中英文两条，
缺失会导致整行标签空白。

### 3.6 帮助文本

帮助内容存放于外置 `help-desc.json`，内置模板为 `Resources/help-desc-default.json`。
加载时比较外部与模板的 `_version`：外部版本更低则备份为 `help-desc.json.v<版本>.bak`
并以模板替换。

**修改模板文案后必须递增 `_version`**，否则新文案无法到达已经生成过外部文件的机器。
帮助键与行标签同源，动态行（`LabelText`）仍按 `LabelKey` 查帮助。

---

## 4. 排版层

### 4.1 坐标体系

`HudGeometry.Build` 用视线角度作为坐标单位，最后统一换算为像素：

```csharp
double pxDeg = _cal.EffectivePxPerDeg(height);   // 45° FOV / 1080p ≈ 24 px/deg
double cx = width / 2.0 + _cal.CenterOffsetX;
double cy = height / 2.0 + _cal.CenterOffsetY - _cal.LayoutOffsetYDeg * pxDeg;
double blend = SmoothStep(Math.Clamp(_opt.ZoomBlend, 0, 1));   // 瞄准布局过渡
```

所有位置以度表达后乘 `pxDeg`，因此换分辨率自动等比，无需为 1080p/1440p/4K 分别实现。

### 4.2 图元模型

`HudPrim` 是唯一的绘制指令：`Line`、`Polyline`、`Circle`、`Arc`、`Text`，
附带宽度、字号、对齐、文本样式（`Plain`/`Boxed`/`Caret`）与 RGBA 颜色。
`Build` 内的调用顺序即绘制顺序，后画的覆盖先画的。

### 4.3 布局变体

三种布局由 `Build` 开头一次性计算，下游函数只读取字段，不重复推导过渡系数：

| 变体 | 触发 | 变化 |
|---|---|---|
| 普通 | — | 基准值 |
| 瞄准 | `ZoomBlend` → 1 | 速度/高度带 12°→19°，航向带 12.5°→19.5°，俯仰梯下压 5°，坡度尺半径 8.6°→17.2° |
| 编队 | `FormationOnly` | 仅保留航向带、速度矢量、编队信息与三个最小读数 |

### 4.4 可调边界

「仪表家具」（速度带、高度带、航向带、数据块、导航行、构型提示、编队块）允许调整
位置与字号；**几何绑定元素不允许缩放**——俯仰梯、地平线、速度矢量、坡度尺、转向带
必须与真实地平线保持 1°=1° 的对齐关系，缩放即产生错误的姿态读数。
俯仰梯仅提供整块平移，平移不破坏刻度精度。

---

## 5. 布局微调

两套结构一一对应，配置层负责持久化，排版层负责应用：

| 层 | 类型 | 位置 | 序列化 |
|---|---|---|---|
| 配置 | `LayoutConfig` | `HudConfig.Layout` | 是 |
| 排版 | `LayoutOptions` | `HudOptions.Layout` | 否，每帧从配置同步 |

七个块各含水平偏移、垂直偏移与字号，单位分别为度与倍数，默认 0 / 1.0 表示不改变任何行为。

### 5.1 与瞄准布局的关系

| 项目 | 普通模式 | 瞄准模式 |
|---|---|---|
| 位置偏移 | 全额生效 | 随 `_layoutFade = 1 - blend` 平滑归零 |
| 字号缩放 | 生效 | 生效 |
| 俯仰梯偏移 | 叠加 | 叠加 |

位置偏移在瞄准模式下归零的原因：瞄准布局已将带子外推至 19°，再叠加用户的外向偏移会
使块超出屏幕。字号只改变大小、不改变位置，因此不需要归零。俯仰梯偏移保持叠加，
属于独立决策（整块平移不破坏读数）。

实现方式：各 `Add*` 入口对位置偏移乘 `_layoutFade`，对字号不乘。

---

## 6. 扩展流程

### 6.1 新增一个设置项

| 步骤 | 位置 | 作用 | 缺失后果 |
|---|---|---|---|
| 1 | `Hud/HudConfig.cs` | 属性 + `[JsonPropertyName]` + 默认值 | 无法持久化 |
| 2 | `HudGeometry.cs` 的 `HudOptions` | 排版层可读字段 | 排版层取不到值 |
| 3 | `Program.cs` 主循环（约 1967 行起） | **每帧**赋值并夹取范围 | 面板调整不生效，需重启 |
| 4 | `UI/Mfd/MfdPages.cs` | 增加一行 | 面板无入口 |
| 5 | `UI/L10n.cs` 与 `Resources/help-desc-default.json` | 中英文案；`_version` +1 | 行标签空白 / 旧机器无帮助 |
| 6 | `Program.SelfTest.cs` | 默认值、边界、夹取、渲染断言 | 无回归保护 |

第 3 步的夹取应放在主循环而非仅依赖面板的 `Min/Max`，以覆盖手工编辑 JSON 的情形。

### 6.2 新增 HUD 元素

1. 实现 `AddXxx(prims, s, cx, cy, pxDeg, line, font, ...)`，只向 `prims` 追加图元；
2. 在 `Build` 对应分支调用（普通 / 编队 / 瞄准各有权重）；
3. 需要开关时增加 `HudOptions.ShowXxx` 与面板一行；
4. 需要布局微调时接入 `_opt.Layout`；
5. 补自检：文本断言或坐标断言。

### 6.3 新增页面

在 `MfdPages.cs` 中新增 `private static MfdPage Xxx() => new() {...}`，
需要挂到首页则在 `MfdPages.Build()` 注册，作为二级页则通过 `Submenu` 挂载；
随后补 `TitleKey`/`MenuKey`/`MenuHintKey` 的中英文案与 `help-desc` 条目。

---

## 7. 不变式

1. 排版层不调用图形 API；绘制层不计算坐标。
2. 面板修改必须在主循环每帧同步，否则不会生效。
3. 范围夹取置于主循环入口，覆盖手工编辑配置的情形。
4. 单位只影响显示与输入；内部状态、判定阈值、数据链报文与配置文件恒为公制。
5. 新增引用类型属性必须补 `CopyReferenceTypesFrom`。
6. 内容会变的页面必须 `NoCache = true`。
7. 修改 `help-desc` 文案必须递增 `_version`。
8. 几何绑定元素不提供缩放入口。
9. 控件矩形只从 `MfdPageLayout` 取值，绘制与命中不得各算一套。
10. 配置写盘使用 `.tmp` + `File.Replace`，失败必须对用户可见。
11. 配置字段使用 PascalCase 并标注 `[JsonPropertyName]`；默认值须保证旧配置行为不变。
12. 提交只包含相关文件，不使用 `git add -A`。

---

## 8. 历史缺陷对照

以下失效模式均在本项目实际发生并已修复，可作为改动时的检查项。

| 现象 | 成因 | 处理 |
|---|---|---|
| 面板调整后不生效，需重启 | 未在主循环每帧同步 | 每帧赋值 + 夹取 |
| 面板行标签空白 | `L10n` 缺键 | 补中英文 |
| 旧机器看不到新增帮助 | `help-desc` `_version` 未递增 | 递增版本 |
| 未按 SAVE 即生效 | `Clone()` 未深拷贝引用类型 | `CopyReferenceTypesFrom` |
| 摘要显示有数据、进入为空 | 动态页被缓存 | `NoCache = true` |
| `G` / `%` 手输无反应且无提示 | 从显示串剥离单位失败且失败分支静默 | 从数值取初值 + 错误提示 |
| 按钮可见但点不到 | 改了绘制未改几何 | `MfdPageLayout` 同源 |
| 面板大于屏幕、内容被裁 | 窗口超屏后 `ClientSize` 被系统截断 | 缩放夹进工作区 |
| 高分屏面板只占半屏 | 1x 底图将 `fit` 限制在 1.0 | 更换 2x 底图，上限随分辨率提升 |

---

## 9. 验证手段

全部验证可在无游戏、无窗口条件下完成：

| 命令 | 用途 |
|---|---|
| `WtHud.exe selftest` | 463 条断言；失败置 `Environment.ExitCode = 1` |
| `WtHud.exe preview <png> <w> <h> [variant]` | HUD 出图，`variant`：`level`/`est`/`zoom`/`dive` 等 |
| `WtHud.exe dumpgeo <w> <h> <pitch> <bank>` | 打印每个图元的坐标、字号与文本，用于核对排版 |
| `WtHud.exe mfdpreview <png> [view]` | 面板出图，`view`：`menu`/`display`/`panelset`/`shape`/`turn`/`diag`/`flightlog`/`flightlog-speed` 等 |
| `WtHud.exe formationsim` | 编队仿真（默认含 250 ms 链路延迟模型） |
| `WtHud.exe probe` / `mapprobe` | 连接真实游戏读取遥测 |

出图专用的环境变量（仅对 `preview` 生效）：

```
WT_PREVIEW_UNITS="kt,ft"         英制样张
WT_PREVIEW_LADDEROFF="4"         俯仰梯偏移（度）
WT_PREVIEW_LAYOUT="6,0,1.3,0,1"  speedX,speedY,speedScale,dataBlockY,dataBlockScale
WT_MFD_FIT="2"                   强制面板缩放（仍会被夹进工作区）
```

`mfdpreview` 的记录页出图另有两个环境变量：`WT_FLIGHTLOG_DIR`（记录目录；列表页与图表的
“架次”页眉都从这里读）与 `WT_FLIGHTLOG_SAMPLE`（直接指定一份 `--mfd-json` 样例 JSON）；
样例不指定时从仓库 `tools/flightlog/out/mfd/*.json` 取最新一份。`WT_FLIGHTLOG_E2E=1` 会让
`selftest` 真跑一次 FlightLog.exe（默认跳过）。

断言入口 `Check(name, ok)` 定义于 `Program.cs:5038`，失败即置退出码，可直接追加调用。

---

*本文描述框架结构与约定；版本变更见 `CHANGELOG.md`，发文材料见 `RELEASE_NOTES_*.md`。*
