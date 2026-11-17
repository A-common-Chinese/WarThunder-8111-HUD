// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WtHud.UI;

namespace WtHud.UI.Mfd;

/// <summary>
/// 调用独立工具 <c>FlightLog.exe --mfd-json &lt;log.jsonl&gt; --out &lt;tmp.json&gt;</c> 分析 HUD 自己录的
/// 飞行记录，并把结果解析成 <see cref="FlightLogResult"/>（schema = 1）。
///
/// 这里只做三件事：找 exe、跑进程（15 s 超时、不弹窗）、解析+校验。界面怎么画不归这里管；
/// 页面只拿到模型或一条本地化错误文本。
///
/// <para>★ 所有失败都返回 <see cref="FlightLogError"/>，不向 UI 线程外抛异常 —— 面板是
/// <c>OnPaint</c>/按键回调驱动的，一个未捕获异常就是整个面板消失。</para>
/// </summary>
internal static class FlightLogBridge
{
    /// <summary>进程超时。日志通常 &lt; 1 s，15 s 是给大文件/冷启动 PyInstaller 留的余量。</summary>
    internal const int TimeoutMs = 15000;

    /// <summary>与 WtHud.exe 同目录的发布名。</summary>
    internal const string ExeName = "FlightLog.exe";

    /// <summary>开发环境的相对路径（从 exe 目录逐级向上找）。</summary>
    internal const string DevRelativePath = @"tools\flightlog\dist\FlightLog.exe";

    /// <summary>开发/测试用的覆盖项：直接指定 FlightLog.exe 路径。</summary>
    internal const string ExeOverrideEnv = "WT_FLIGHTLOG_EXE";

    // ------------------------------------------------------------------ 数据契约

    /// <summary>schema = 1 的整份结果。</summary>
    internal sealed class FlightLogResult
    {
        [JsonPropertyName("schema")] public int Schema { get; set; }
        [JsonPropertyName("tool")] public string? Tool { get; set; }
        [JsonPropertyName("tool_version")] public string? ToolVersion { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("generated_utc")] public string? GeneratedUtc { get; set; }
        [JsonPropertyName("meta")] public FlightLogMeta? Meta { get; set; }
        [JsonPropertyName("flights")] public List<FlightLogFlight>? Flights { get; set; }
        [JsonPropertyName("events")] public List<FlightLogEvent>? Events { get; set; }
    }

    internal sealed class FlightLogMeta
    {
        [JsonPropertyName("vehicle")] public string? Vehicle { get; set; }
        [JsonPropertyName("duration_s")] public double? DurationS { get; set; }
        [JsonPropertyName("rate_hz")] public double? RateHz { get; set; }
        [JsonPropertyName("samples")] public int? Samples { get; set; }
    }

    internal sealed class FlightLogFlight
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("start_s")] public double StartS { get; set; }
        [JsonPropertyName("end_s")] public double EndS { get; set; }
        [JsonPropertyName("samples")] public int Samples { get; set; }
        [JsonPropertyName("vehicle")] public string? Vehicle { get; set; }
        [JsonPropertyName("stats")] public FlightLogStats? Stats { get; set; }
        [JsonPropertyName("series")] public FlightLogSeries? Series { get; set; }
    }

    internal sealed class FlightLogStats
    {
        [JsonPropertyName("max_ias_kmh")] public double? MaxIasKmh { get; set; }
        [JsonPropertyName("max_tas_kmh")] public double? MaxTasKmh { get; set; }
        [JsonPropertyName("max_mach")] public double? MaxMach { get; set; }
        [JsonPropertyName("max_g")] public double? MaxG { get; set; }
        [JsonPropertyName("max_alt_m")] public double? MaxAltM { get; set; }
        [JsonPropertyName("avg_ias_kmh")] public double? AvgIasKmh { get; set; }
        [JsonPropertyName("avg_tas_kmh")] public double? AvgTasKmh { get; set; }
        [JsonPropertyName("avg_mach")] public double? AvgMach { get; set; }
        [JsonPropertyName("avg_alt_m")] public double? AvgAltM { get; set; }
        [JsonPropertyName("es_range_m")] public double[]? EsRangeM { get; set; }
        [JsonPropertyName("es_delta_m")] public double? EsDeltaM { get; set; }
        [JsonPropertyName("max_ias_filtered_kmh")] public double? MaxIasFilteredKmh { get; set; }
        [JsonPropertyName("max_tas_filtered_kmh")] public double? MaxTasFilteredKmh { get; set; }
        [JsonPropertyName("avg_filter")] public FlightLogAvgFilter? AvgFilter { get; set; }
    }

    internal sealed class FlightLogAvgFilter
    {
        [JsonPropertyName("death_window_s")] public double? DeathWindowS { get; set; }
        [JsonPropertyName("spike_sigma")] public double? SpikeSigma { get; set; }
        [JsonPropertyName("excluded_samples")] public int? ExcludedSamples { get; set; }
    }

    /// <summary>逐点序列。缺失值是 null，数组长度与 <see cref="T"/> 一致。</summary>
    internal sealed class FlightLogSeries
    {
        [JsonPropertyName("t")] public double[]? T { get; set; }
        [JsonPropertyName("ias_kmh")] public double?[]? IasKmh { get; set; }
        [JsonPropertyName("tas_kmh")] public double?[]? TasKmh { get; set; }
        [JsonPropertyName("mach")] public double?[]? Mach { get; set; }
        [JsonPropertyName("alt_m")] public double?[]? AltM { get; set; }
        [JsonPropertyName("ralt_m")] public double?[]? RaltM { get; set; }
        [JsonPropertyName("es_m")] public double?[]? EsM { get; set; }
        [JsonPropertyName("des_dt_ms")] public double?[]? DesDtMs { get; set; }
        [JsonPropertyName("vy_ms")] public double?[]? VyMs { get; set; }
        [JsonPropertyName("aoa_deg")] public double?[]? AoaDeg { get; set; }
        [JsonPropertyName("ny_g")] public double?[]? NyG { get; set; }
        [JsonPropertyName("pitch_deg")] public double?[]? PitchDeg { get; set; }
        [JsonPropertyName("bank_deg")] public double?[]? BankDeg { get; set; }
    }

    internal sealed class FlightLogEvent
    {
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("t")] public double? T { get; set; }
        [JsonPropertyName("vehicle")] public string? Vehicle { get; set; }
    }

    /// <summary>一条本地化错误：<see cref="Key"/> 查 <see cref="L10n"/>，<see cref="Detail"/> 填 {0}。</summary>
    internal sealed record FlightLogError(string Key, string? Detail = null)
    {
        /// <summary>当前屏幕语言下的完整错误文本。</summary>
        public string Text
        {
            get
            {
                var pattern = L10n.Get(LangScope.Screen, Key);
                if (string.IsNullOrEmpty(Detail)) return pattern;
                try { return string.Format(CultureInfo.InvariantCulture, pattern, Detail); }
                catch (FormatException) { return pattern; }
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // ------------------------------------------------------------------ 解析与校验

    /// <summary>
    /// 解析 <c>--mfd-json</c> 的输出。schema ≠ 1 或必需字段缺失一律按「版本不兼容」处理 ——
    /// 半残的数据画出来比明确报错更误导人。
    /// </summary>
    internal static FlightLogResult? Parse(string json, out FlightLogError? error)
    {
        error = null;
        FlightLogResult? result;
        try
        {
            result = JsonSerializer.Deserialize<FlightLogResult>(json, JsonOpts);
        }
        catch (JsonException)
        {
            error = new FlightLogError("flylog.errSchema");
            return null;
        }

        if (result is null || !IsCompatible(result))
        {
            error = new FlightLogError("flylog.errSchema");
            return null;
        }
        return result;
    }

    /// <summary>读一个已经存在的 JSON 文件（预览/自检用，不跑进程）。</summary>
    internal static FlightLogResult? LoadResultFile(string path, out FlightLogError? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                error = new FlightLogError("flylog.errNoFile");
                return null;
            }
            return Parse(File.ReadAllText(path), out error);
        }
        catch (Exception ex)
        {
            error = new FlightLogError("flylog.errRead", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// schema 与结构校验。要求：schema=1、至少一个架次、每架次有 stats、series.t 非空、
    /// 所有非 null 序列长度与 t 相同（接口约定如此，长度不符说明数据被截断）。
    /// </summary>
    internal static bool IsCompatible(FlightLogResult? result)
    {
        if (result is null || result.Schema != 1) return false;
        if (result.Flights is not { Count: > 0 }) return false;

        foreach (var flight in result.Flights)
        {
            if (flight.Series is not { } s || s.T is not { Length: > 0 } t) return false;
            if (flight.Stats is null) return false;
            if (!SameLength(t, s.IasKmh) || !SameLength(t, s.TasKmh) || !SameLength(t, s.Mach)
                || !SameLength(t, s.AltM) || !SameLength(t, s.RaltM) || !SameLength(t, s.EsM)
                || !SameLength(t, s.DesDtMs) || !SameLength(t, s.VyMs) || !SameLength(t, s.AoaDeg)
                || !SameLength(t, s.NyG) || !SameLength(t, s.PitchDeg) || !SameLength(t, s.BankDeg))
                return false;
        }
        return true;
    }

    private static bool SameLength(double[] t, double?[]? values) =>
        values is null || values.Length == t.Length;

    // ------------------------------------------------------------------ 找 exe

    /// <summary>
    /// 找 FlightLog.exe：环境变量（开发/测试覆盖）→ 与 WtHud.exe 同目录 → 从 exe 目录逐级向上找
    /// <c>tools/flightlog/dist/FlightLog.exe</c>。找到返回完整路径，找不到返回 null。
    /// </summary>
    internal static string? FindExecutable()
    {
        var env = Environment.GetEnvironmentVariable(ExeOverrideEnv);
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return Path.GetFullPath(env);

        var beside = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (File.Exists(beside)) return beside;

        // 开发环境：bin/Debug/net8.0-windows/ 往上 6 级才是仓库根。多走几级不影响正确性。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, DevRelativePath);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>构造进程参数。抽成纯函数是为了能断言「路径带空格时不会被拆开」。</summary>
    internal static List<string> BuildArguments(string logPath, string outPath) => new()
    {
        "--mfd-json", logPath,
        "--out", outPath,
    };

    /// <summary>进程工作目录：exe 所在目录（PyInstaller 单文件包的自解压与相对资源都靠它）。</summary>
    internal static string WorkingDirectoryFor(string exePath) =>
        Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? AppContext.BaseDirectory;

    // ------------------------------------------------------------------ 结果缓存

    private sealed record CacheEntry(long Size, long MtimeTicks, FlightLogResult Result);

    private static readonly object CacheLock = new();
    // 只留最近几条：面板一次只会看一份记录，缓存的意义是「同一个文件翻页/进出来回不重复分析」。
    private const int CacheLimit = 4;
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static int CacheCount
    {
        get { lock (CacheLock) return Cache.Count; }
    }

    internal static void ClearCache()
    {
        lock (CacheLock) Cache.Clear();
    }

    /// <summary>
    /// 缓存命中：路径 + 大小 + mtime 全部一致才算命中（记录文件是追加写的，
    /// 只看路径会把「还在增长的那一局」冻在第一次的结果上）。
    /// </summary>
    internal static FlightLogResult? Cached(string logPath)
    {
        try
        {
            var full = Path.GetFullPath(logPath);
            var info = new FileInfo(full);
            if (!info.Exists) return null;
            lock (CacheLock)
            {
                if (Cache.TryGetValue(full, out var e)
                    && e.Size == info.Length
                    && e.MtimeTicks == info.LastWriteTimeUtc.Ticks)
                    return e.Result;
            }
        }
        catch { }
        return null;
    }

    /// <summary>记下成功分析的结果（<see cref="Cached"/> 的写入口）。</summary>
    internal static void Remember(string logPath, FlightLogResult result)
    {
        try
        {
            var full = Path.GetFullPath(logPath);
            var info = new FileInfo(full);
            if (!info.Exists) return;
            lock (CacheLock)
            {
                if (Cache.Count >= CacheLimit && !Cache.ContainsKey(full))
                {
                    // 没有 LRU 顺序，直接丢掉一条任意项即可：容量只是防止无限增长。
                    var first = Cache.Keys.FirstOrDefault();
                    if (first != null) Cache.Remove(first);
                }
                Cache[full] = new CacheEntry(info.Length, info.LastWriteTimeUtc.Ticks, result);
            }
        }
        catch { }
    }

    // ------------------------------------------------------------------ 运行 FlightLog

    /// <summary>
    /// 分析一份记录：缓存 → 找 exe → 跑进程 → 读临时 JSON → 解析校验。失败返回 null 并给出
    /// 本地化错误（<paramref name="error"/>），**不抛异常**。
    /// </summary>
    internal static FlightLogResult? Analyze(string logPath, out FlightLogError? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            {
                error = new FlightLogError("flylog.errNoFile");
                return null;
            }

            var full = Path.GetFullPath(logPath);
            if (Cached(full) is { } hit) return hit;

            var exe = FindExecutable();
            if (exe is null)
            {
                error = new FlightLogError("flylog.errNoExe");
                return null;
            }

            var tmp = Path.Combine(Path.GetTempPath(),
                "wthud-mfd-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = WorkingDirectoryFor(exe),
                    UseShellExecute = false,      // 不用 shell：不会弹黑色控制台窗口
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var arg in BuildArguments(full, tmp)) psi.ArgumentList.Add(arg);

                string stderr;
                int exitCode;
                using (var process = new Process { StartInfo = psi })
                {
                    try
                    {
                        if (!process.Start())
                        {
                            error = new FlightLogError("flylog.errTool", "start failed");
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        error = new FlightLogError("flylog.errTool", ex.Message);
                        return null;
                    }

                    // 先起读取再等：只等不读的话，工具写满 stdout/stderr 管道会互相卡死。
                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(TimeoutMs))
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        try { process.WaitForExit(2000); } catch { }
                        error = new FlightLogError("flylog.errTimeout");
                        return null;
                    }
                    try { process.WaitForExit(); } catch { }

                    exitCode = process.ExitCode;
                    stderr = SafeResult(stderrTask);
                    _ = SafeResult(stdoutTask);
                }

                if (exitCode != 0)
                {
                    error = new FlightLogError("flylog.errTool", Tail(stderr));
                    return null;
                }
                if (!File.Exists(tmp))
                {
                    error = new FlightLogError("flylog.errOutput");
                    return null;
                }

                var text = File.ReadAllText(tmp);
                var result = Parse(text, out var parseError);
                if (result is null)
                {
                    error = parseError ?? new FlightLogError("flylog.errSchema");
                    return null;
                }

                Remember(full, result);
                return result;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
        catch (Exception ex)
        {
            error = new FlightLogError("flylog.errTool", ex.Message);
            return null;
        }
    }

    private static string SafeResult(Task<string> task)
    {
        try { return task.Wait(2000) ? task.Result ?? "" : ""; }
        catch { return ""; }
    }

    /// <summary>错误详情里只留尾巴：stderr 可能有几百行 traceback，面板放不下。</summary>
    internal static string Tail(string? text, int maxChars = 180)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        while (oneLine.Contains("  ", StringComparison.Ordinal))
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        return oneLine.Length <= maxChars ? oneLine : "…" + oneLine[^maxChars..];
    }

    // ------------------------------------------------------------------ 记录列表

    /// <summary>recordings/ 下的一个记录文件。</summary>
    internal sealed record FlightLogRecording(string Path, string Name, DateTime Modified, long Size)
    {
        /// <summary>行首的日期时间（本地时区，yyyy-MM-dd HH:mm）。</summary>
        public string TimeText => Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>记录目录：与 WtHud.exe 同目录的 recordings/（与 FlightRecorder 一致）。</summary>
    internal static string DefaultRecordingsFolder =>
        Path.Combine(AppContext.BaseDirectory, "recordings");

    /// <summary>
    /// 扫描记录目录：<c>*.jsonl</c>，按修改时间**倒序**（最新的在最前），最多 <paramref name="max"/> 个。
    /// 目录不存在或读不了时返回空列表 —— 调用方据此显示「目录为空」。
    /// </summary>
    internal static List<FlightLogRecording> ListRecordings(string? folder = null, int max = 30)
    {
        var list = new List<FlightLogRecording>();
        try
        {
            var dir = string.IsNullOrWhiteSpace(folder) ? DefaultRecordingsFolder : folder;
            if (!Directory.Exists(dir)) return list;

            foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(file);
                    list.Add(new FlightLogRecording(info.FullName, info.Name, info.LastWriteTime, info.Length));
                }
                catch { }
            }

            list.Sort((a, b) => b.Modified.CompareTo(a.Modified));
            if (max > 0 && list.Count > max) list.RemoveRange(max, list.Count - max);
        }
        catch { }
        return list;
    }

    /// <summary>文件大小文本：B / KB / MB（列表行右对齐用，固定一位小数）。</summary>
    internal static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024L * 1024L)
            return (bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture) + " MB";
    }
}
