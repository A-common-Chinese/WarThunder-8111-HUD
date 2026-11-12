// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>编队目标是从哪条路径来的。</summary>
internal enum FormationSource
{
    /// <summary>没有目标：既没收到长机节点，也没有可用的地图友机。</summary>
    None = 0,
    /// <summary>数据链：按节点 ID / 角色认出长机，不依赖 map_obj 的友机列表。</summary>
    Link = 1,
    /// <summary>无链路：退回「最近的友机」，全部读数来自 map_obj。</summary>
    Map = 2,
}

/// <summary>
/// 编队目标：引导只需要这几样东西。
/// <para><see cref="HasHorizontal"/> = false 表示**认得长机但没有自机坐标**：
/// 高度差和坡度照常（报文里有），但水平引导（圈、转向、距离、油门）必须关掉 ——
/// 否则会把「上一帧的方位」当成真的画出去。</para>
/// </summary>
internal readonly record struct FormationTarget(
    FormationSource Source,
    bool HasHorizontal,
    double RangeM,
    double RelBearingDeg);

/// <summary>
/// 两条编队路径的唯一分叉点。
/// <para>★ 这里只做「谁是我要跟的那架、它在我哪一侧多远」；PID、两阶段、垂直引导、
/// HUD 绘制全部共用一份 —— 两份完整管线迟早会把增益改成不一样的。</para>
/// </summary>
internal static class FormationResolver
{
    /// <summary>数据链路径：目标就是认出来的那个节点，位置来自它自报的归一化坐标。</summary>
    /// <param name="selfX">自机归一化坐标；null = map_obj 没给（8111 半初始化或不在对局）。</param>
    public static FormationTarget ByLink(WtHud.Telemetry.DataLink.LinkNode lead,
        double? selfX, double? selfY, double sizeX, double sizeY, bool yFlip, double navHeading)
    {
        if (selfX is not double sx || selfY is not double sy
            || lead.Xn is not double lx || lead.Yn is not double ly)
            return new FormationTarget(FormationSource.Link, false, 0, 0);

        var (range, rel) = Relative(sx, sy, lx, ly, sizeX, sizeY, yFlip, navHeading);
        return new FormationTarget(FormationSource.Link, true, range, rel);
    }

    /// <summary>无链路路径：数值由地图友机列表直接给出，这里只做转发，便于两条路径同型。</summary>
    public static FormationTarget ByMap(double rangeM, double relBearingDeg)
        => new(FormationSource.Map, true, rangeM, relBearingDeg);

    /// <summary>
    /// 两个归一化点之间的斜距与相对方位（度，正 = 右）。
    /// <para>和地图友机那条路用同一个公式：东向 = Δx·宽，北向 = Δy·高（可翻转），
    /// 相对方位 = 绝对方位 − 本机航向。抽出来是为了让「链路」和「地图」两条路径算得一样。</para>
    /// </summary>
    public static (double RangeM, double RelBearingDeg) Relative(
        double selfX, double selfY, double otherX, double otherY,
        double sizeX, double sizeY, bool yFlip, double navHeading)
    {
        double dEast = (otherX - selfX) * sizeX;
        double dNorth = (otherY - selfY) * sizeY * (yFlip ? -1 : 1);
        double range = Math.Sqrt(dEast * dEast + dNorth * dNorth);

        double bearing = Math.Atan2(dEast, dNorth) * 180.0 / Math.PI;
        if (bearing < 0) bearing += 360;
        return (range, Normalize(bearing - navHeading));
    }

    /// <summary>把角度收进 (-180, 180]。</summary>
    public static double Normalize(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        if (deg <= -180.0) deg += 360.0;
        return deg;
    }
}
