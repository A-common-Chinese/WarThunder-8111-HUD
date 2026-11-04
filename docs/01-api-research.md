# 战争雷霆 localhost:8111 插件 — 调研笔记

整理时间：2026-11-04。用途：后续开发 8111 插件的决策依据。

## 0. 本机现状
- 游戏：`D:\Program Files (x86)\Steam\steamapps\common\War Thunder`（Steam appid 236390）
- `config.blk`：`use_eac:b=no`、`driver:t="dx11"`、`mode:t="fullscreen"`、`fpsLimit:i=100`、3840x2160
- 调研时 `127.0.0.1:8111` 未监听（游戏未运行）

## 1. 接口性质
- 游戏进程内嵌只读 HTTP server，端口**硬编码 8111**，无鉴权，JSON 返回。
- 无"开启端口"选项；游戏启动即监听。只有自定义战斗/试飞需勾 **Allow web UI**（2.45 Hornet's Sting 起）。
- `config.blk` 内**不存在** enable_http/http_server 字段（俄文官方 CDK 参数文档无 http/port）→ 讹传。
- 主机版无 localhost。
- 返回头示例含 `Access-Control-Allow-Origin: *`（待实测），浏览器/OBS 网页源可能可直读。
- 机库中 /state、/indicators 常 `valid:false` 或空 → 开发需 mock 数据。

## 2. 端点
| 端点 | 说明 |
|---|---|
| /state | 飞行物理量：IAS/TAS(H, m)、M、AoA/AoS、Ny、Vy、Mfuel、各发动机 throttle/power/RPM/manifold/oil temp/pitch/thrust/efficiency |
| /indicators | 仪表指针值（部分 0..1 归一化，待实测），compass/bank/vario/gears/flaps/g_meter/fuel/rpm/oil_pressure/mach/wing_sweep_lever |
| /map_obj.json | 目标列表：type/color/icon(Player=自机)/x,y(0..1)/dx,dy(方向向量)/机场 sx,sy,ex,ey |
| /map_info.json | grid_steps/grid_zero/map_min/map_max（坐标换算） |
| /map.img | 小地图 PNG |
| /mission.json | objectives[].primary/status/text、status |
| /hudmsg?lastEvt=&lastDmg= | 击杀/受伤日志，增量 id |
| /gamechat?lastId= | 聊天，增量 id |

文档仓库（含 openapi.yaml，本目录已存）：https://github.com/lucasvmx/WarThunder-localhost-documentation

## 3. 数据边界
- 拿不到：武器/弹药余量、HMD/MFD 内容、部分机型 radio_altitude/fuel。
- 风险：2026-09 有报告称未认证 WT Assistant 时 `/map_obj.json` 返回空（未定论）→ 地图类功能需降级方案。

## 4. 工程约定
- 轮询 10Hz（100ms）；map_obj 1–2Hz；HTTP server 单线程 → 短超时(200–1000ms)+低并发。
- hudmsg/gamechat 必须回传游标。
- /state 键名自带单位、含空格逗号，int/float 混用，字段随载具缺失，一律 `.get`。
- /map.img 需 CRC/字节去重。
- Windows overlay：无边框窗口 + WS_EX_LAYERED/TRANSPARENT + TOPMOST + 点击穿透；游戏须 Fullscreen windowed；HAGS 会造成卡顿。
- **启动器会删除游戏根目录内的第三方 exe** → 插件放独立目录。
- 游戏更新可能改字段。

## 5. 合规
- 用户协议 6.1.3：禁止未授权修改客户端/游戏画面以获利。
- Stona_WT(2024-05)：localhost 数据做 overlay 一般可，不封号；无标记模式敌人指示=ESP，不认可。
- OrsonES(2026-06)：官方 WebUI 数据可被第三方使用，非 ESP 型一般允许；官方不背书/不授权/不担保。
- Monika_in_action(2026-10)：不保证第三方工具安全，后果自负。
- 红线：只读 8111；不注入/不读内存/不改文件/不拦包/不自动化；不做敌人 ESP；不读屏 OCR 识别敌人。
- 来源：https://forum.warthunder.com/t/tools-using-data-provided-on-port-8111/106664 ；https://forum.warthunder.com/t/localhost-8111/359519

## 6. 先例
- WTRTI（标杆，闭源，245★）https://github.com/MeSoftHorny/WTRTI ；文档 https://mesofthorny.github.io/WTRTI/
- PowerBroker2/WarThunder（Python 库，67★）、WTDashboard（PyQt6）、wt_overlay_linux（Rust）、WarThunderRPC、DGLAB-WT（郊狼联动）、WarTelemetry（Go）、warthunder-ui（JS）、wtmap（2013）

## 7. 待实测（需游戏运行）
1. /indicators 是否归一化；2. /map_obj.json 是否仍返回数据；3. 返回头是否 ACAO:*；4. 真实字段全集与缺失情况。
