// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Globalization;

namespace WtHud.Hud;

/// <summary>日志级别。分级口径按「对程序运行的危害」，不是按飞行安全 —— 见 <see cref="Log"/>。</summary>
public enum LogLevel
{
    /// <summary>对程序无害：游戏内告警/事件、自检结果、常规状态切换。</summary>
    Info,

    /// <summary>可能影响程序功能、但不致命：热键被占、覆盖层重设失败、快照/清理失败等。</summary>
    Warn,

    /// <summary>对程序有害：存档失败、配置加载失败、写盘失败、恢复失败、线程退出等。</summary>
    Error,
}

/// <summary>
/// 统一日志行：<c>LEVEL: [机型] HH:mm:ss.fff key=value… 中文内容</c>。
///
/// <para>0.5.11 之前每类告警各写一行自由文本（<c>stall level 2: AoA=…</c>、
/// <c>SINK RATE ON: …</c>、<c>config load FAILED …</c>），同一个 <c>wthud-overlay.log</c> 里
/// 格式五花八门，想按级别筛/按机型筛/按时间对齐只能肉眼找。现在这三类都走这里。</para>
///
/// <para>★ 分级口径（按对**程序**的危害，不按飞行安全）：</para>
/// <list type="bullet">
/// <item><b>INFO</b>：所有游戏内告警/事件 —— STALL 触发与解除、超速、VNE、下沉率/PULL UP、
///   AoA、Bingo 油量；AOA 自检结果与疑似度数制提示。这些只是「告诉你天上的事」，程序本身没坏。</item>
/// <item><b>WARN</b>：可能影响程序功能但不致命 —— 热键注册失败/被占用、覆盖层 resize/DPI 失败、
///   配置快照 session/snapshot 失败（只影响撤销历史）、recorder 清理失败、数据链坏节点、
///   地图数据错误、8111 轮询偶发错误、配置迁移写失败。</item>
/// <item><b>ERROR</b>：对程序有害 —— SAVE FAILED、配置加载失败、recorder 写盘失败、
///   快照恢复失败、提权重启失败、轮询线程退出、MFD 设置保存失败、未捕获的 IO/线程异常。</item>
/// </list>
///
/// <para>★ 信息/调试/自检输出（selftest/scopetest/perf/dump/probe/启动 banner）保持原样，
/// 不加前缀：它们不是运行期诊断信号，套上级别只会稀释真正的 WARN/ERROR。</para>
/// </summary>
public static class Log
{
    /// <summary>当前机型（轮询线程每个新快照更新）。空 = 日志写 <c>[-]</c>。</summary>
    public static string CurrentVehicle { get; set; } = "";

    /// <summary>级别的固定文本（大写，冒号后一个空格跟在本行最前）。</summary>
    public static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warn => "WARN",
        _ => "INFO",
    };

    /// <summary>
    /// 生成一行日志（纯函数，自检直接断言字符串）。
    /// <para>格式：<c>LEVEL: [&lt;vehicle&gt;] HH:mm:ss.fff &lt;key=value…&gt; &lt;message&gt;</c>；
    /// 机型为空写 <c>[-]</c>，没有字段时不留双空格。</para>
    /// </summary>
    public static string Format(LogLevel level, string? vehicle, DateTime localTime, string message,
                                params (string Key, string Value)[] fields)
    {
        var sb = new System.Text.StringBuilder(96);
        sb.Append(LevelText(level));
        sb.Append(": [");
        sb.Append(string.IsNullOrWhiteSpace(vehicle) ? "-" : vehicle.Trim());
        sb.Append("] ");
        sb.Append(localTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));

        if (fields is { Length: > 0 })
        {
            foreach (var (key, value) in fields)
            {
                if (string.IsNullOrEmpty(key)) continue;
                sb.Append(' ');
                sb.Append(key);
                sb.Append('=');
                sb.Append(value);
            }
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            sb.Append(' ');
            sb.Append(message.Trim());
        }
        return sb.ToString();
    }

    /// <summary>写一行日志（用当前机型）。字符串拼好后一次 Console.WriteLine，多线程下整行不拆。</summary>
    public static void Write(LogLevel level, string message, params (string Key, string Value)[] fields) =>
        Console.WriteLine(Format(level, CurrentVehicle, DateTime.Now, message, fields));

    /// <summary>指定机型写一行（换机型瞬间、自检结果、已知机型的配置错误）。</summary>
    public static void WriteFor(LogLevel level, string? vehicle, string message,
                                params (string Key, string Value)[] fields) =>
        Console.WriteLine(Format(level, vehicle, DateTime.Now, message, fields));

    public static void Info(string message, params (string Key, string Value)[] fields) =>
        Write(LogLevel.Info, message, fields);

    public static void Warn(string message, params (string Key, string Value)[] fields) =>
        Write(LogLevel.Warn, message, fields);

    public static void Error(string message, params (string Key, string Value)[] fields) =>
        Write(LogLevel.Error, message, fields);

    public static void InfoFor(string? vehicle, string message, params (string Key, string Value)[] fields) =>
        WriteFor(LogLevel.Info, vehicle, message, fields);

    public static void WarnFor(string? vehicle, string message, params (string Key, string Value)[] fields) =>
        WriteFor(LogLevel.Warn, vehicle, message, fields);

    public static void ErrorFor(string? vehicle, string message, params (string Key, string Value)[] fields) =>
        WriteFor(LogLevel.Error, vehicle, message, fields);

    /// <summary>数值统一用不变文化格式化：中文系统下日志里的 4,41 会干扰按小数点搜索。</summary>
    public static string Num(double value, string format = "F2") =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
