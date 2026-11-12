# 数据链联调（内网穿透验证）

两台机器各放一份本目录。一台发、一台收，30 秒内就知道通不通。
`dlprobe` 不启动 HUD、不读游戏、不碰 8111，只监听 UDP 48017。

---

## 一、★ 两台机器连不同 WiFi（本次场景）

跨 WiFi 等于跨网段，**广播一定不通，必须填对方 IP**。按下面四步走，别跳。

### 第 0 步：先看两边是什么地址

两台机器各开一个 PowerShell，都跑：

```powershell
ipconfig | Select-String "IPv4"
```

看前三段：

| 结果 | 判断 | 下一步 |
|---|---|---|
| 都在 `192.168.1.x`、`192.168.0.x` 之类同一个段 | 同网段 | 直接跳到第 1 步，大概率能通 |
| 两个不同段（如 `192.168.1.x` 与 `192.168.0.x`） | 跨网段 | **先 ping**，见下 |
| 一边是 `100.64.x.x` ~ `100.127.x.x` | 运营商级 NAT | 见下面的红字 |

**先 ping 一次，ping 不通就别往下走：**

```powershell
ping <B的IP>
```

ping 不通说明网络层就不通，UDP 更不可能通。这时只有三条路：
一是让两台机器连同一个路由器的不同 SSID（2.4G 和 5G 经常是同一网段，最省事）；
二是用蒲公英 / ZeroTier 这类组网，拿到互相可达的地址；
三是登录上级路由器做端口映射。

> ★ 本机（接收端）当前实测地址是 `100.66.9.170`，属于 `100.64.0.0/10`。
> 这是运营商级 NAT 段 —— 如果另一台笔记本在普通家用路由器下（`192.168.x.x`），
> 两台机器根本不在同一层网络，**直连不会通**，别浪费时间反复试。
> 换 WiFi 后这个地址会变，每次都要重新 `ipconfig` 确认。

### 第 1 步：接收端 B 放行端口

以**管理员**身份开 PowerShell（右键开始菜单 → 终端(管理员)），跑：

```powershell
New-NetFirewallRule -DisplayName "WtHud DataLink" -Direction Inbound -Protocol UDP -LocalPort 48017 -Action Allow -Profile Any
```

`-Profile Any` 是关键：连不同 WiFi 时 Windows 常把网络识别成「公用」，
默认规则只放行「专用」，等着弹窗点允许经常等不到。这条命令一次到位。

### 第 2 步：接收端 B 起探针

```powershell
cd "C:\Users\noapi\Desktop\WtHud-数据链联调"
.\WtHud.exe dlprobe 120
```

窗口会停在 `listening on udp 48017 for 120s`。**别关它，也别同时开 HUD**（会抢端口）。

### 第 3 步：发送端 A 发包

**A 机器只需要那个 4 KB 的脚本，不用拷 69 MB 的 exe。**

```powershell
powershell -ExecutionPolicy Bypass -File .\fakedatalink.ps1 -Target 100.66.9.170:48017
```

`-Target` 填**第 0 步在 B 上看到的那个 IPv4**，不是这里写死的这个。

### 第 4 步：看 B 的窗口

出现下面这种行就通了：

```
  10:30:26.022  from 192.168.1.7:57522   id=777777  alt= 3000m  vy= 10.0  hdg= 180  pos=0.52000,0.50000
```

120 秒后自动退出并打印 `result: rx=20  bad=0  nodes=1`。

| 统计 | 含义 |
|---|---|
| `rx > 0` | **通了**，其余都是细节 |
| `rx = 0` | 包没到，看第五节排查 |
| `bad > 0` | 包到了但解析失败，把 A 的原始输出贴出来 |

---

## 二、同一局域网（最短路径）

| 步骤 | 在哪台 | 做什么 |
|---|---|---|
| 1 | **B** | `.\WtHud.exe dlprobe 60` |
| 2 | **A** | `powershell -ExecutionPolicy Bypass -File fakedatalink.ps1 -Target <B的IP>:48017` |
| 3 | **B** | 看到 `from ... id=777777` 即通 |

同网段时 `-Target` 也可以写 `255.255.255.255`（广播），但既然知道 IP，写 IP 更可靠。

---

## 三、三种网络情形对照

| 情形 | `-Target` 填什么 | 还要做什么 |
|---|---|---|
| 同一局域网 | B 的 IP | 无 |
| 跨网段、可路由 | B 的 IP | 两端防火墙放通 UDP 48017 |
| NAT / 公网穿透 | B 的公网映射地址 | 端口转发，或用蒲公英类组网 |

> ★ 跨省、两台机器都在各自家里的宽带后面：直接看 **[datalink-pgy-remote.md](datalink-pgy-remote.md)** ——
> 蒲公英组网 + 双向验收（含丢包门槛、5 分钟稳定性、测试记录表），比本文更贴合那个场景。

> 广播地址 `255.255.255.255` **出不了路由器**，这是协议决定的，不是设置问题。

---

## 四、防火墙（最常见的失败原因）

除了第一节那条规则，还有两种做法：

**弹窗法**：第一次运行 `dlprobe` 时 Windows 弹「是否允许访问网络」，
必须点允许，并且要勾上「专用网络」。点错了之后一直收不到，但不会有任何提示。

**检查现有规则**：

```powershell
Get-NetFirewallRule -DisplayName "WtHud*" | Format-Table DisplayName, Enabled, Profile, Direction
```

---

## 五、不通怎么排（按顺序）

| 现象 | 判断 | 下一步 |
|---|---|---|
| 启动就报 `cannot bind udp 48017` | 端口被占 | `Get-Process WtHud` 看是不是还有 HUD 在跑 |
| 有统计行，`rx=0` | 包没到 | 依次查：A 是否报 `failed` → 目标 IP → ping → 防火墙 → 网络是否真互通 |
| `rx` 在涨，`bad` 也在涨 | 收到但解析不了 | 把 A 机原始输出贴出来，逐段对 |
| `rx` 在涨，`nodes=0` | 发送端 ID 和接收端 ID 撞了 | 改 `-Id` 换个值 |

★ UDP 没有「连接」概念，**不能用 telnet 或 `Test-NetConnection -Port` 测**（那些是 TCP）。
唯一可靠的判断是 `dlprobe` 的 `rx` 计数。

---

## 六、报文长什么样

```
WTDL2|777777|3000|100|180|57|52000|50000
      │      │    │   │   │   └ x 归一化 0..1 ×100000
      │      │    │   │   └ 转向率 0.1 °/s
      │      │    │   └ 航向 度
      │      │    └ 升降速度 0.1 m/s
      │      └ 气压高度 m
      └ 节点 ID
```

一行纯文本，竖线分隔。任何能发 UDP 的工具都能手工造包，方便逐跳定位。
后两段（坐标）是 2026-11-12 追加的：老版本只发 6 段，接收端照收，只是配不上对。

---

## 七、通之后接进 HUD

### 面板（推荐，改完立即生效，不用重启）

接收端按 **F1 → 数据链**：

| 行 | 填什么 |
|---|---|
| 使用数据互联 | **开** |
| **对端地址** | `172.16.5.23:48017`，多个用逗号分隔；留空 = 广播 |
| 发送目标（只读） | 自动显示 `1 target(s)`；若是 `1 ok / 1 bad` 说明有地址格式不对 |

`对端地址` 按确认键进文本输入页，直接敲键盘（支持 `Ctrl+V` 粘贴），
提交后立刻生效 —— 不用关 HUD、不用改文件。联调时 IP 会变好几次，这一点很重要。

`发送目标` 读的是**链路当前实际状态**，不是上面那行文本框。地址写错了会留在文本框里，
但不会被真正使用，区别只在这一行上显示。

### 配置文件（等价，适合批量或远程改）

`hud-config.json`：

```json
"dataLinkEnabled": true,
"dataLinkPeers": ["172.16.5.23:48017"]
```

留空 `[]` 就走广播（只能同一局域网）。启动日志会打印实际用的是哪种：

```
data link: on, node id 483920, udp 48017
data link: targets -> 1 target(s)
```

看到 `** BIND FAILED - port busy or blocked **` 说明端口没绑上，
这一侧什么都收不到，先去关掉占端口的进程。

---

## 八、用组网软件（推荐，不直连）

跨 WiFi + 运营商级 NAT 时，直连没有可行路径。组网软件会在两台机器上各装一块虚拟网卡，
给它们一个**互相可达的虚拟 IP**，数据链走这个 IP 就行 —— 协议、脚本、代码全都不用改，
只是 `-Target` 和 `dataLinkPeers` 里那个地址换了。

### 步骤

**① 两台机器装同一个组网软件，登录同一账号，加入同一个网络**

免费额度：蒲公英 3 台、ZeroTier 25 台，都够两台笔记本用。

**② 在接收端 B 上找虚拟 IP**

```powershell
Get-NetIPAddress -AddressFamily IPv4 | Select-Object IPAddress, InterfaceAlias
```

组网软件的网卡名通常带 `Pgy` / `Oray` / `ZeroTier` / `Tailscale`。
蒲公英常见段是 `172.16.x.x`，ZeroTier 是 `10.x` 或 `172.x`。

**③ 在发送端 A 上 ping 这个虚拟 IP**

```powershell
ping <B的虚拟IP>
```

通了才往下走。这一步是整个方案的关键验证 —— 组网软件自己会显示「已连接」，
但那只代表它连上了服务器，**不代表两台机器之间能通**。

**④ 剩下和第一节完全一样**

```powershell
# B（管理员）
New-NetFirewallRule -DisplayName "WtHud DataLink" -Direction Inbound -Protocol UDP -LocalPort 48017 -Action Allow -Profile Any

# B
.\WtHud.exe dlprobe 120

# A
powershell -ExecutionPolicy Bypass -File .\fakedatalink.ps1 -Target <B的虚拟IP>:48017
```

### 两个坑（务必先看）

> ★ **蒲公英的 VGC 驱动在 ROBOT-04 上有 `0x133 DPC_WATCHDOG_VIOLATION` 蓝屏史。**
> 装之前先看驱动版本；如果又蓝，换成 ZeroTier —— 同样是虚拟网卡 + 单播，配置和命令一个字都不用改。

> ★ **不要用 Tailscale。** 它分配的地址在 `100.64.0.0/10` 段，而本机物理网卡现在就是
> `100.66.9.170`，同一段。两者会抢路由，表现为「组网显示已连接，但包不知道往哪走」，
> 而且这种故障很难从现象上看出来是地址冲突。

### 免费额度对比

| 方案 | 免费设备数 | 虚拟网段 | 备注 |
|---|---|---|---|
| 蒲公英 | 3 | `172.16.x` | 有蓝屏史，装前先看驱动 |
| ZeroTier | 25 | `10.x` / `172.x` | 需要在管理台手动批准设备 |
| Tailscale | 100 | `100.64/10` | **和本机现有网段冲突，本次不用** |

### 组网通了之后

在接收端 `hud-config.json` 里填**虚拟 IP**，不要填物理 IP：

```json
"dataLinkPeers": ["172.16.5.23:48017"]
```

因为物理 IP 换 WiFi 就变，虚拟 IP 相对稳定。

---

## 九、脚本参数

```
-Target   必填，ip:port 或 ip（端口默认 48017）
-Id       节点 ID，默认 777777
-RateHz   发送频率，默认 4
-Mode     circle（绕圈，坐标和高度都在变）/ fixed（定点定高）
-Seconds  发多久，0 = 一直发
-Quiet    只打统计行
```

绕圈模式下 `x`、`y`、`alt`、`hdg`、`turn` 全在动，接收端能直接看出数据有没有被截断或错位。
