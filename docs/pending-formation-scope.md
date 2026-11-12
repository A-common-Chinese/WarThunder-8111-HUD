# 待定事项：编队态势双罗盘

**状态**：设计已定稿，等数据链提供高度差后开工
**记录日期**：2026-11-12
**前置依赖**：数据链要能把「每架友机的海拔」和 map_obj 里的友机对上（见第 6 节）

---

## 1. 要做什么

编队模式下，在 HUD 两个下角各放一个态势显示，同吃一份数据（最近 4 架友军）：

| 位置 | 视图 | 半径表示 | 回答的问题 |
|---|---|---|---|
| **左下** | 六分区圆盘（Wing Commander 式） | **偏角**（还要转多少度） | 往哪转 |
| **右下** | 2D 罗盘（极坐标） | **距离**（离站位还差多少） | 差多少 |

两者不是冗余：编队动作 = 「往哪转」+「还差多少」，两个视图各承担一半。

## 2. 为什么是这两种

用户在 2026-11-12 上午讨论了四轮，先后否掉了：

- 伪3D 透视压缩（把纵轴压扁造纵深）——不是他要的
- 球面等距圆柱展开（横轴方位、纵轴仰角）——形似但不是
- 六面立方体贴图展开——不是

最终确认同时要两种：

1. **2D 罗盘**：机头朝上，极角 = 相对方位，半径 = 距离 / 量程
2. **六分区**：1990 年 Wing Commander 的雷达布局。手册原文：
   > divided into six sections, shows how far you have to rotate...
   > outermost ring = behind, center circle = ahead, four middle sections =
   > alongside, above or below.

   半径是「还需转多少度」，不是距离；距离靠亮度/数字。

## 3. 六分区的实际样子（★ 关键约束）

`MapData` 没有 z，拿不到俯仰。所以：

| 区域 | 覆盖 | 状态 |
|---|---|---|
| 中心圆 | 正前 ±25° | 可用 |
| 左 / 右扇区 | 正左 / 正右 | 可用 |
| **上 / 下扇区** | — | **留白**，等数据链的 ΔH |
| 外环 | 正后 155°+ | 可用 |

上下扇区留白是**诚实的缺失**，不是 bug。实现时画成虚线禁用态，help 里写明「需要高度数据」。

★ 不要为了填满而把六分区改成「水平面 6 等分」——那就不是 WC 的语义了，
将来有 ΔH 还要改回去。

## 4. 接口设计

投影器插在「几何层产出图元」这一层之后、HudDrawer 之前。绘制器不知道罗盘长什么样。

```csharp
public readonly record struct ScopeBlip(
    double RelBearingDeg,   // 相对方位，机头朝上为 0
    double RangeM,          // 水平距离
    double? DeltaHM,        // 高度差，null = 未知（数据链没配对成功时）
    bool IsLead);

public interface IScopeProjector
{
    string Name { get; }
    void EmitFrame(List<HudPrim> prims, ScopeBox box);                   // 圆 or 椭圆 + 刻度
    bool TryProject(in ScopeBlip b, ScopeBox box, out ScopePoint p);     // 落点 + 大小 + 透明度
}

public readonly record struct ScopePoint(double X, double Y, double SizeScale, double Alpha);
```

三个实现：

- `PlanarCompass`：极角 = 方位，半径 = RangeM / 量程
- `Sector6Projector`：极径 = |偏角| / 180，极角 = 方位；DeltaHM == null 时不投影到上下扇区
- `(预留)` 任何新投影器，`IScopeProjector` 一个字不用改

★ `DeltaHM` 必须保持可空。它是「将来有数据链」的唯一入口，也是 `SizeScale`/`Alpha`
必须留在 `ScopePoint` 里的原因（伪3D 若真要做，靠这两个通道做近大远小）。

## 5. 布局与尺寸

- 左下 / 右下，圆心距边角：`2.0 × pxDeg`
- 半径：`3.0 × pxDeg`（1080 高、视场 33° 时约 98 px）
- 用 `pxDeg` 而不是绝对像素：现有元素（速度条 `cy + 8.6~10.1 pxDeg`、
  导航块 `cx + 7.8 pxDeg`）全部按度数定位，换分辨率/视场不会错位

配置三项：

| 键 | 默认 | 说明 |
|---|---|---|
| `scopeCompassOn` | true | 右下 2D 罗盘 |
| `scopeSectorOn` | true | 左下六分区 |
| `scopeRangeM` | 3000 | 罗盘量程，与「3 km 内才认得到」一致 |

## 6. 数据依赖：等数据链做位置配对

现状（2026-11-12 核实）：

- `MapData.Friendlies` 给**归一化坐标 + 航向 + 机型**，有水平位置，**无高度**
- `DataLink`（UDP 48017，`WTDL2` 报文）只在编队伙伴间共享**高度 / Vy / 航向 / 转向率**
- `DataLink.TryGetPartnerAltitude` 只返回「最近最活跃的那一个」伙伴的高度

★ 缺口：罗盘要显示 4 架，每架都要自己的 ΔH，而节点与 map_obj 里的友机**没有共同 ID**。

解决方向（尚未实现）：数据链报文加发送者的归一化坐标，接收端用位置就近匹配
（实测两者一致到 1–2 m，容差 50 m 足够）把「节点」和「友机」配对，从而给每架友机配上高度。

## 7. 已知取舍（供将来参考）

- **距离不能同时用两个通道**：六分区的半径给了角度，距离就只能靠亮度/数字；
  2D 罗盘的半径给了距离，就没有余量画俯仰。所以两个都做。
- **亮度通道容易和颜色语义打架**：现在是绿 = 本机 / 琥珀 = 意图。
  若六分区用亮度表示距离，需要另定一套（建议用点的大小，不用亮度）。
- 六分区上的点**不带文字**（扇区太窄），距离数字只挂在 2D 罗盘上。

## 8. 验收标准（开工时按这个验）

1. `scopepreview` 能出图，两个视图并排，一眼看出方向与距离
2. 断言覆盖：两个投影器 × 前/左/后/右四个方位 + 越界不画 + 空列表不崩，≥10 条
3. 量程边界：3 km 上的点应正好落在最外圈，3.1 km 不画
4. ΔH 为 null 时，六分区上下扇区必须为空（不能画在中间冒充）
5. `selftest` 全绿，且不影响现有 84 条
