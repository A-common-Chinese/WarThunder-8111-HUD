// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>
/// 僚机站位相对长机的几何。横向右为正、纵向用「后退量」（站位永远在长机后方，所以是负值）。
/// <para>★ 斜距必须逐个站位算：自定义队形三个站位的斜距都等于尾随距离（150²+370²≈400²），
/// 但梯队/纵队/菱形的槽位斜距各不相同，速度前馈与到位判定都读这个值。</para>
/// </summary>
public readonly record struct FormationSlotSpec(double SideM, double BackM, double? RangeOverrideM = null)
{
    /// <summary>
    /// 斜距。具名队形按真实几何算；自定义队形沿用历史口径 —— 三个站位**共用**
    /// 「尾随距离」这一个目标距离（150²+370²=399.25，但老代码按 400 判定）。
    /// ★ 这条覆盖值是刻意的：改掉它会让 1G/2G 的历史仿真数据整体偏移（实测 swAt 3.2→6.0）。
    /// </summary>
    public double RangeM => RangeOverrideM ?? Math.Sqrt(SideM * SideM + BackM * BackM);

    /// <summary>内圈/外圈的判据：右侧 +1、左侧 −1、正后 0。原来直接用 FormationWingSide 的符号。</summary>
    public int SideSign => Math.Sign(SideM);
}

/// <summary>
/// 具名队形的槽位表。新增队形只在这里加一行，面板、制导、仿真都自动跟上。
/// <para>横队（纯侧向）故意没有：站位后距为 0 时引导角长期指向 ±90°，
/// 横向位移靠航向而不是推力，速度前馈失效 —— 那是另一种飞行模式，不是换几个数字。</para>
/// </summary>
public static class FormationGeometry
{
    public const string ShapeCustom = "custom";
    public const string ShapeEchelon = "echelon";
    public const string ShapeColumn = "column";
    public const string ShapeDiamond = "diamond";

    public static readonly string[] ShapeIds = { ShapeCustom, ShapeEchelon, ShapeColumn, ShapeDiamond };

    public static bool IsKnownShape(string shape) => Array.IndexOf(ShapeIds, shape) >= 0;

    /// <summary>该队形有几个槽位。自定义只有 1 个（几何由三个距离字段决定）。</summary>
    public static int SlotCount(string shape) => shape switch
    {
        ShapeEchelon => 3,
        ShapeColumn => 3,
        ShapeDiamond => 3,
        _ => 1,
    };

    /// <summary>
    /// 取第 <paramref name="index"/> 个槽位。
    /// <para>梯队：每架再靠后 450 m、再偏左 250 m（同一侧成斜线）。
    /// 纵队：正后方每架间隔 400 m。菱形：左后 / 右后 / 正后。</para>
    /// </summary>
    public static FormationSlotSpec Slot(string shape, int index, HudConfig cfg)
    {
        var i = Math.Clamp(index, 0, SlotCount(shape) - 1);
        switch (shape)
        {
            case ShapeEchelon:
                // 逐架累加：第 2 架 (500, 900)，第 3 架 (750, 1350)，斜距 1030 / 1545
                return new FormationSlotSpec(-250.0 * (i + 1), -450.0 * (i + 1));
            case ShapeColumn:
                return new FormationSlotSpec(0, -400.0 * (i + 1));
            case ShapeDiamond:
                return i switch
                {
                    1 => new FormationSlotSpec(300, -400),
                    2 => new FormationSlotSpec(0, -600),
                    _ => new FormationSlotSpec(-300, -400),
                };
            default:
                // 自定义：沿用既有三项距离。尾随位（side=0）的后距就是「尾随距离」，
                // 侧位的后距是「侧位后距」—— 与参数化之前完全一致。
                return LegacySlot(cfg.FormationWingSide, cfg);
        }
    }

    /// <summary>当前配置对应的站位（形状 + 槽位，越界钳位）。</summary>
    public static FormationSlotSpec Current(HudConfig cfg) =>
        Slot(cfg.FormationShape, cfg.FormationSlot, cfg);

    /// <summary>
    /// 参数化之前的三个站位（尾随 / 左 / 右）。自定义队形走这条路，
    /// 保证历史行为逐字不变 —— 仿真回归就是盯它。
    /// </summary>
    public static FormationSlotSpec LegacySlot(int wingSide, HudConfig cfg) =>
        wingSide == 0
            ? new FormationSlotSpec(0, -cfg.FormationRangeM, cfg.FormationRangeM)
            : new FormationSlotSpec(wingSide * cfg.FormationWingOffsetM, -cfg.FormationStationBackM,
                                    cfg.FormationRangeM);

    /// <summary>
    /// 仿真要遍历的站位表。自定义队形仍是原来三个（尾随、左、右），保持 1G/2G 历史数据可比；
    /// 具名队形按自己的槽位表逐个跑。
    /// </summary>
    public static List<(FormationSlotSpec Spec, int SideSign, string Label)> SimSlots(HudConfig cfg)
    {
        var list = new List<(FormationSlotSpec, int, string)>();
        if (!IsKnownShape(cfg.FormationShape) || cfg.FormationShape == ShapeCustom)
        {
            list.Add((LegacySlot(0, cfg), 0, "0"));
            list.Add((LegacySlot(-1, cfg), -1, "L"));
            list.Add((LegacySlot(+1, cfg), +1, "R"));
            return list;
        }

        for (var i = 0; i < SlotCount(cfg.FormationShape); i++)
        {
            var spec = Slot(cfg.FormationShape, i, cfg);
            list.Add((spec, spec.SideSign, (i + 1).ToString()));
        }
        return list;
    }
}
