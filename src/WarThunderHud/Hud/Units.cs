// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Globalization;

namespace WtHud.Hud;

/// <summary>速度单位。内部一律 km/h，只在显示与输入处换算。</summary>
public enum SpeedUnit
{
    Kmh = 0,
    Knots = 1,
}

/// <summary>长度单位。内部一律米，只在显示与输入处换算。</summary>
public enum LengthUnit
{
    Metric = 0,
    Imperial = 1,
}

/// <summary>
/// 单位制的**唯一换算出口**。整份代码里只有这里知道 1 kt = 1.852 km/h、1 ft = 0.3048 m。
/// <para>★ 纪律：内部状态（HudConfig、快照、数据链、配置文件）永远是公制。这里的函数只做
/// 「公制 → 显示」和「显示 → 公制」两件事，不参与任何判定、阈值或几何计算。</para>
/// <para>★ 公制路径必须与换算前逐字符一致：所有格式化函数在 Metric / Kmh 下走的分支
/// 和改造前是同一串代码，不经过任何乘除。</para>
/// </summary>
public static class Units
{
    public const double KmhPerKnot = 1.852;
    public const double MetresPerFoot = 0.3048;
    public const double MetresPerNm = 1852.0;
    public const double FpmPerMps = 60.0 / MetresPerFoot;

    /// <summary>配置文件里的原始值 → 枚举。越界一律回到公制，绝不抛。</summary>
    public static SpeedUnit Speed(int raw) => raw == 1 ? SpeedUnit.Knots : SpeedUnit.Kmh;

    public static LengthUnit Length(int raw) => raw == 1 ? LengthUnit.Imperial : LengthUnit.Metric;

    // ---------------------------------------------------------------- speed

    public static string SpeedLabel(SpeedUnit u) => u == SpeedUnit.Knots ? "kt" : "km/h";

    /// <summary>带子角上的大写单位标。</summary>
    public static string SpeedCapsLabel(SpeedUnit u) => u == SpeedUnit.Knots ? "KT" : "KM/H";

    /// <summary>1 个显示单位等于多少 km/h。</summary>
    public static double KmhPerSpeedUnit(SpeedUnit u) => u == SpeedUnit.Knots ? KmhPerKnot : 1.0;

    public static double SpeedValue(double kmh, SpeedUnit u) =>
        u == SpeedUnit.Knots ? kmh / KmhPerKnot : kmh;

    public static double SpeedToKmh(double value, SpeedUnit u) =>
        u == SpeedUnit.Knots ? value * KmhPerKnot : value;

    /// <summary>
    /// 读数框里的整数速度。★ 用 <c>Math.Round</c> 而不是 <c>ToString("0")</c>：改造前这里就是
    /// <c>((int)Math.Round(kmh)).ToString()</c>，两者只在恰好 .5 时差 1（银行家舍入 vs 四舍五入），
    /// 公制口径要逐字符一致。
    /// </summary>
    public static string SpeedText(double kmh, SpeedUnit u) =>
        ((int)Math.Round(SpeedValue(kmh, u))).ToString(CultureInfo.InvariantCulture);

    /// <summary>速度带刻度间隔（显示单位）：50 km/h ↔ 25 kt，两边在屏幕上间距接近。</summary>
    public static double SpeedTickStep(SpeedUnit u) => u == SpeedUnit.Knots ? 25 : 50;

    /// <summary>带标数字的间隔（显示单位）：100 km/h ↔ 50 kt。</summary>
    public static double SpeedMajorStep(SpeedUnit u) => u == SpeedUnit.Knots ? 50 : 100;

    /// <summary>建议加减速量的取整粒度：10 km/h ↔ 5 kt。</summary>
    public static double SpeedDeltaStep(SpeedUnit u) => u == SpeedUnit.Knots ? 5 : 10;

    /// <summary>「还要加/减多少」按显示单位取整后返回，已换算成显示单位。</summary>
    public static int SpeedDelta(double deltaKmh, SpeedUnit u)
    {
        double step = SpeedDeltaStep(u);
        return (int)(Math.Round(SpeedValue(deltaKmh, u) / step) * step);
    }

    // ---------------------------------------------------------------- length

    public static string LengthLabel(LengthUnit u) => u == LengthUnit.Imperial ? "ft" : "m";

    /// <summary>带子角上的大写单位标。</summary>
    public static string LengthCapsLabel(LengthUnit u) => u == LengthUnit.Imperial ? "FT" : "M";

    /// <summary>1 个显示单位等于多少米。</summary>
    public static double MetresPerLengthUnit(LengthUnit u) => u == LengthUnit.Imperial ? MetresPerFoot : 1.0;

    public static double LengthValue(double metres, LengthUnit u) =>
        u == LengthUnit.Imperial ? metres / MetresPerFoot : metres;

    public static double LengthToMetres(double value, LengthUnit u) =>
        u == LengthUnit.Imperial ? value * MetresPerFoot : value;

    public static string LengthText(double metres, LengthUnit u) =>
        LengthValue(metres, u).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>高度带刻度间隔：100 m ↔ 250 ft（屏幕上间距接近）。</summary>
    public static double AltTickStep(LengthUnit u) => u == LengthUnit.Imperial ? 250 : 100;

    /// <summary>高度带带标数字的间隔：200 m ↔ 500 ft。</summary>
    public static double AltMajorStep(LengthUnit u) => u == LengthUnit.Imperial ? 500 : 200;

    // ------------------------------------------------------------- distance

    /// <summary>
    /// 距离（长）：公制 1 km 以内给米、以上给 km；英制 1 NM 以内给英尺、以上给 NM。
    /// <para>返回的是**不带单位**的数字串，单位另行拼（和改造前 FormatRange 的用法一致）。</para>
    /// </summary>
    public static string DistanceNumber(double metres, LengthUnit u)
    {
        if (u == LengthUnit.Metric)
            return metres < 1000
                ? ((int)(Math.Round(metres / 10.0) * 10)).ToString(CultureInfo.InvariantCulture)
                : (metres / 1000.0).ToString("F1", CultureInfo.InvariantCulture);

        return metres < MetresPerNm
            ? ((int)(Math.Round(metres / (10.0 * MetresPerFoot)) * 10)).ToString(CultureInfo.InvariantCulture)
            : (metres / MetresPerNm).ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>距离的单位后缀，与 <see cref="DistanceNumber"/> 成对使用。</summary>
    public static string DistanceSuffix(double metres, LengthUnit u)
    {
        if (u == LengthUnit.Metric) return metres < 1000 ? "m" : "km";
        return metres < MetresPerNm ? "ft" : "NM";
    }

    /// <summary>距离（短）：数字 + 单位，例如 <c>830m</c> / <c>2720ft</c>。</summary>
    public static string DistanceShort(double metres, LengthUnit u)
        => DistanceNumber(metres, u) + DistanceSuffix(metres, u);

    /// <summary>
    /// 距离（长）：大单位以内**只给数字不带单位**（调用方自己排版），超过才带 km / NM。
    /// <para>这是改造前 FormatRange 的行为，逐字符保留：&lt;1000 m 时画面上就是裸数字。</para>
    /// </summary>
    public static string DistanceLong(double metres, LengthUnit u)
    {
        bool small = u == LengthUnit.Metric ? metres < 1000 : metres < MetresPerNm;
        return DistanceNumber(metres, u) + (small ? "" : DistanceSuffix(metres, u));
    }

    /// <summary>只给米/英尺的定点数字（不带 km/NM 切换），用于「→ 400m」这类目标值。</summary>
    public static string DistancePlain(double metres, LengthUnit u)
    {
        if (u == LengthUnit.Metric)
            return ((int)Math.Round(metres)).ToString(CultureInfo.InvariantCulture) + "m";
        return ((int)Math.Round(metres / MetresPerFoot)).ToString(CultureInfo.InvariantCulture) + "ft";
    }

    /// <summary>
    /// 同上，但数字和单位之间留一个空格。面板里的整句（「门槛 40 m / 延迟 35 m」）沿用原排版，
    /// 值列那种紧凑写法用不带空格的 <see cref="DistancePlain"/>。
    /// </summary>
    public static string DistancePlainSpaced(double metres, LengthUnit u) =>
        u == LengthUnit.Metric
            ? ((int)Math.Round(metres)).ToString(CultureInfo.InvariantCulture) + " m"
            : ((int)Math.Round(metres / MetresPerFoot)).ToString(CultureInfo.InvariantCulture) + " ft";

    /// <summary>
    /// 导航行的距离：一直用「大单位 + 一位小数」（原来固定是 <c>F1</c> km，小距离也是 0.5km）。
    /// 英制对应 NM，保留同样的形状。
    /// </summary>
    public static string NavDistance(double metres, LengthUnit u) =>
        u == LengthUnit.Metric
            ? (metres / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + "km"
            : (metres / MetresPerNm).ToString("F1", CultureInfo.InvariantCulture) + "NM";

    // ------------------------------------------------------- vertical speed

    /// <summary>垂直速度数字：公制保留一位小数（8.2），英制取整到 ft/min（1614）。</summary>
    public static string VerticalSpeedNumber(double ms, LengthUnit u) =>
        u == LengthUnit.Metric
            ? ms.ToString("0.0", CultureInfo.InvariantCulture)
            : Math.Round(ms * FpmPerMps).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>
    /// 垂直速度的单位后缀。公制返回空串 —— 原来 V/S 后面就没有单位，加了反而改变画面。
    /// </summary>
    public static string VerticalSpeedSuffix(LengthUnit u) => u == LengthUnit.Metric ? "" : "fpm";

    /// <summary>垂直速度整句（含单位），用于引导提示。</summary>
    public static string VerticalSpeedText(double ms, LengthUnit u) =>
        u == LengthUnit.Metric
            ? ms.ToString("0.0", CultureInfo.InvariantCulture) + " m/s"
            : Math.Round(ms * FpmPerMps).ToString("0", CultureInfo.InvariantCulture) + " fpm";

    /// <summary>
    /// 高度差读数（「↑ 40 m」这类）。公制取整到 5 m，英制取整到 10 ft —— 和原来一样是
    /// 一个「够用的整数量级」，不是精确值。
    /// </summary>
    public static string AltitudeGapText(double metres, LengthUnit u)
    {
        if (u == LengthUnit.Metric)
            return ((int)Math.Round(metres / 5.0) * 5).ToString(CultureInfo.InvariantCulture) + " m";
        double feet = metres / MetresPerFoot;
        return ((int)Math.Round(feet / 10.0) * 10).ToString(CultureInfo.InvariantCulture) + " ft";
    }

    // -------------------------------------------------------------- 面板步长

    /// <summary>
    /// 把换算后的步长对齐到 1/2/5×10ⁿ，免得面板出现「+2.7」这种步进。
    /// <para>例：1 km → 0.54 NM 对齐成 0.5；10 m → 32.8 ft 对齐成 20；1 m/s → 197 fpm 对齐成 200。</para>
    /// </summary>
    public static double NiceStep(double raw)
    {
        if (!(raw > 0) || double.IsInfinity(raw)) return raw;
        double exp = Math.Floor(Math.Log10(raw));
        double pow = Math.Pow(10, exp);
        double mantissa = raw / pow;                     // 1..10
        double nice = mantissa < 1.5 ? 1 : mantissa < 3.5 ? 2 : mantissa < 7.5 ? 5 : 10;
        return nice * pow;
    }
}
