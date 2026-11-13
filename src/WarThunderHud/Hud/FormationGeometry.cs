// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>
/// 僚机站位相对长机的几何。横向右为正、纵向用「后退量」（站位永远在长机后方，所以是负值）。
/// <para>★ 0.4.21 起这里只有**一条**几何路径：四个配置数值（侧位 / 横偏 / 后距 / 高度）。
/// 预设队形退化成「把四个值填好」的按钮，不再是一张独立的槽位表 ——
/// 两套表并存时，面板上的「槽位」和「侧位」会各说一套（谁决定位置取决于形状）。</para>
/// </summary>
public readonly record struct FormationSlotSpec(double SideM, double BackM, double UpM = 0)
{
    /// <summary>
    /// 斜距（水平面内）：<c>√(Side² + Back²)</c>。到位判定、速度前馈、面板只读值读的都是它，
    /// 所以面板上看到多少，判定就用多少。
    /// </summary>
    public double RangeM => Math.Sqrt(SideM * SideM + BackM * BackM);

    /// <summary>内圈/外圈的判据：右侧 +1、左侧 −1、正后 0。</summary>
    public int SideSign => Math.Sign(SideM);

    /// <summary>站位相对长机的目标高度差（正 = 站位更高）。垂直引导的目标就是这个值。</summary>
    public double TargetDeltaHM => -UpM;
}

/// <summary>
/// 站位几何与预设队形。
/// <para>几何只有两个入口：<see cref="Station"/> 读当前配置（制导、判定、绘制都走它），
/// <see cref="ApplyPreset"/> 把某个预设写进配置（面板点一下时调用）。</para>
/// </summary>
public static class FormationGeometry
{
    public const string ShapeCustom = "custom";
    public const string ShapeEchelon = "echelon";
    public const string ShapeColumn = "column";
    public const string ShapeDiamond = "diamond";

    public static readonly string[] ShapeIds = { ShapeCustom, ShapeEchelon, ShapeColumn, ShapeDiamond };

    /// <summary>僚机编号上限：三机队形。编号只是编号，和几何无关。</summary>
    public const int MaxSlots = 3;

    public static bool IsKnownShape(string shape) => Array.IndexOf(ShapeIds, shape) >= 0;

    /// <summary>
    /// 当前配置对应的站位。**这是唯一的几何入口。**
    /// <para>侧位为正后（0）时横偏乘 0，横偏那一项自然不起作用 —— 不需要额外的分支。</para>
    /// </summary>
    public static FormationSlotSpec Station(HudConfig cfg) =>
        new(cfg.FormationWingSide * cfg.FormationWingOffsetM,
            -Math.Max(1.0, cfg.FormationStationBackM),
            cfg.FormationUpM);

    /// <summary>该形状当前使用的基准间距（米）。自定义队形没有间距，返回后距。</summary>
    public static double Spacing(HudConfig cfg, string shape) => shape switch
    {
        ShapeEchelon => cfg.FormationEchelonSpacingM,
        ShapeColumn => cfg.FormationColumnSpacingM,
        ShapeDiamond => cfg.FormationDiamondSpacingM,
        _ => cfg.FormationStationBackM,
    };

    /// <summary>写回该形状的基准间距（面板那一行用它）。</summary>
    public static void SetSpacing(HudConfig cfg, string shape, double value)
    {
        switch (shape)
        {
            case ShapeEchelon: cfg.FormationEchelonSpacingM = value; break;
            case ShapeColumn: cfg.FormationColumnSpacingM = value; break;
            case ShapeDiamond: cfg.FormationDiamondSpacingM = value; break;
            default: cfg.FormationStationBackM = value; break;
        }
    }

    /// <summary>
    /// 预设的四个值：侧位、横偏、后距、高度。
    /// <para>比例固定，只有基准间距可变 —— 所以「改间距」等于整体缩放，形状不会变形。</para>
    /// <para>梯队默认 450：再后 450、再左 250（250/450）、再高 25（25/450）。
    /// 菱形默认 400：左右后 300/400，后位 0/600 且抬高 30（0.075 × 400）。</para>
    /// </summary>
    public static (int Side, double OffsetM, double BackM, double UpM) Preset(
        string shape, int index, double spacingM)
    {
        var i = Math.Clamp(index, 0, MaxSlots - 1);
        var s = Math.Clamp(spacingM, 50.0, 2000.0);
        var k = i + 1;
        switch (shape)
        {
            case ShapeEchelon:
                return (-1, s * 250.0 / 450.0 * k, s * k, s * 25.0 / 450.0 * k);
            case ShapeColumn:
                return (0, 0, s * k, 0);
            case ShapeDiamond:
                return i switch
                {
                    1 => (+1, s * 0.75, s, 0),
                    2 => (0, 0, s * 1.5, s * 0.075),
                    _ => (-1, s * 0.75, s, 0),
                };
            default:
                return (0, 0, s, 0);
        }
    }

    /// <summary>
    /// 把预设写进配置。★ 只写四个数值，**不碰**快照 —— 快照由调用方在「自定义 → 预设」时存。
    /// </summary>
    public static void ApplyPreset(HudConfig cfg, string shape, int index)
    {
        var (side, offset, back, up) = Preset(shape, index, Spacing(cfg, shape));
        cfg.FormationWingSide = side;
        cfg.FormationWingOffsetM = offset;
        cfg.FormationStationBackM = back;
        cfg.FormationUpM = up;
    }

    /// <summary>
    /// 仿真要遍历的站位表：三个侧位，数值全部来自当前配置。
    /// <para>统一模型之后不再按形状分叉 —— 仿真跑的就是用户真正会飞的几何。</para>
    /// </summary>
    public static List<(FormationSlotSpec Spec, int SideSign, string Label)> SimSlots(HudConfig cfg)
    {
        var back = -Math.Max(1.0, cfg.FormationStationBackM);
        var up = cfg.FormationUpM;
        var off = Math.Max(0.0, cfg.FormationWingOffsetM);
        return new List<(FormationSlotSpec, int, string)>
        {
            (new FormationSlotSpec(0, back, up), 0, "0"),
            (new FormationSlotSpec(-off, back, up), -1, "L"),
            (new FormationSlotSpec(+off, back, up), +1, "R"),
        };
    }
}
