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
    double RelBearingDeg,
    // 「只跟地图可见」把它挡下来的：位置有，但地图上没显示这架友机（全真 1 km 外）。
    bool LeadHidden = false);

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

    /// <summary>
    /// 地图上是不是真的显示着这架友机。
    /// <para>判据用距离 + 相对方位，不是坐标：地图给的是相对量，链路给的是坐标，
    /// 两边采样时刻不同（链路 4 Hz、地图更慢），拿坐标硬比会一直对不上。</para>
    /// <para>★ 容差必须按**米**给，不能只按角度：固定 25° 在 40 m 处只剩 17 m 横向余量，
    /// 而 4 Hz 报文在 250 m/s 时就漂 60 m —— 两机贴在一起时判据必然在边界上抖，
    /// 表现就是「提示一直闪」。所以拆成横向 / 纵向两个米制容差，并随距离放宽。</para>
    /// <para>这项只是个「要不要用链路位置」的闸门，真正引导用的仍是链路位置，
    /// 所以宁可放宽也不能抖：误放行的代价只是一次规则松动，误拦的代价是仪表一直闪。</para>
    /// </summary>
    public static bool VisibleOnMap(IReadOnlyList<FriendlyContact> friendlies,
                                    double rangeM, double relBearingDeg,
                                    double lateralFloorM = 120.0, double alongFloorM = 180.0,
                                    double closeRangeM = 200.0)
    {
        // 贴到 200 m 以内：地图图标本身就可能不画（近距重叠），这时候「图上有没有」
        // 已经失去意义 —— 这个距离上肉眼都看得见对方。
        if (rangeM <= closeRangeM) return true;
        if (friendlies.Count == 0) return false;

        double rad = relBearingDeg * Math.PI / 180.0;
        double linkLat = rangeM * Math.Sin(rad);
        double linkAhead = rangeM * Math.Cos(rad);
        double latTol = Math.Max(lateralFloorM, rangeM * 0.15);
        double alongTol = Math.Max(alongFloorM, rangeM * 0.20);

        foreach (var f in friendlies)
        {
            double fr = f.RelBearingDeg * Math.PI / 180.0;
            if (Math.Abs(f.RangeM * Math.Sin(fr) - linkLat) > latTol) continue;
            if (Math.Abs(f.RangeM * Math.Cos(fr) - linkAhead) > alongTol) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 地图判定加迟滞：命中即锁存，连续 <paramref name="holdS"/> 秒都判不到才放掉。
    /// <para>不加迟滞的话，即使容差够宽，在边界附近（尤其长机刚好飞出 1 km）也会逐帧翻转，
    /// 屏幕上就是「长机不在图上」一闪一闪。留 0.8 s 正好压住抖动，又不至于让人等太久。</para>
    /// </summary>
    public static bool LatchVisible(bool hit, ref bool latched, ref double lastHitS,
                                    double nowS, double holdS = 0.8)
    {
        if (hit)
        {
            lastHitS = nowS;
            latched = true;
        }
        else if (nowS - lastHitS > holdS)
        {
            latched = false;
        }
        return latched;
    }
}
