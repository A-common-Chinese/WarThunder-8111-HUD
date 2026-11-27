// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WtHud.Hud;

namespace WtHud.Telemetry;

/// <summary>
/// 按对局记录本机飞行数据（JSONL，exe 同目录 recordings/）。
/// 只记录本机遥测，不涉及编队/数据链；写盘在独立线程，主循环只入队。
/// 每个文件：第一行 meta，之后每帧一个 sample，按 marker 键插入 marker 事件，结尾 end。
/// </summary>
public sealed class FlightRecorder : IDisposable
{
    private const string Prefix = "wtlog-";

    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 8192);
    private readonly Thread _writer;
    private readonly string _path;
    private readonly object _stopGate = new();
    private volatile bool _closed;

    /// <summary>记录文件所在目录（exe 同目录 /recordings）。</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "recordings");

    public string FilePath => _path;

    private FlightRecorder(string path)
    {
        _path = path;
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "flight-recorder" };
        _writer.Start();
    }

    /// <summary>开始一局记录：建目录、写 meta、把清理旧文件丢到后台。</summary>
    /// <remarks>
    /// ★ 清理会删旧文件（目录 IO，可能被杀软/同步盘拖慢），不能阻塞调用方 —— 这个 Start
    /// 是在轮询线程的 recGate 锁里调的，删文件卡住就等于录制/看护一起卡住。所以只排队清理。
    /// </remarks>
    public static FlightRecorder Start(HudConfig cfg, TelemetrySnapshot first)
    {
        Directory.CreateDirectory(Folder);
        CleanupInBackground(cfg.RecordKeep);

        string vehicle = string.IsNullOrWhiteSpace(first.VehicleType) ? "unknown" : first.VehicleType;
        foreach (var bad in System.IO.Path.GetInvalidFileNameChars()) vehicle = vehicle.Replace(bad, '_');
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = System.IO.Path.Combine(Folder, $"{Prefix}{stamp}-{vehicle}.jsonl");

        var rec = new FlightRecorder(path);
        rec.TryEnqueue(JsonSerializer.Serialize(new MetaLine
        {
            Type = "meta",
            // JSONL 录制 schema（和 FlightLog --mfd-json 的 payload schema 是两套，别混）：
            // schema 2（0.5.10 起）样本行可选带舵面/杆舵 6 个字段，缺键时整条省略。
            // schema 1 的旧文件解析器的 get() 逻辑完全兼容，不需要迁移。
            Schema = 2,
            App = WtHud.Program.ShortVersion,
            Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Vehicle = vehicle,
            Rate = "per-sample",
        }, JsonOpts));
        return rec;
    }

    /// <summary>写一帧本机数据（在渲染循环里调用，只入队）。</summary>
    public void Add(TelemetrySnapshot s, AttitudeSolution att, double? radioAltM = null)
    {
        if (_closed) return;
        var line = JsonSerializer.Serialize(new SampleLine
        {
            Type = "sample",
            T = Math.Round(s.ElapsedSeconds, 3),
            Utc = s.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            Valid = s.Valid,
            InFlight = s.InFlight,
            Vehicle = s.VehicleType,
            Ias = Math.Round(s.IasKmh, 1),
            Tas = Math.Round(s.TasKmh, 1),
            Mach = Math.Round(s.Mach, 3),
            Alt = Math.Round(s.AltM, 1),
            Ralt = radioAltM is double ra ? Math.Round(ra, 1) : null,
            Vy = Math.Round(s.VyMs, 2),
            Aoa = Math.Round(s.AoaDeg, 2),
            Aos = Math.Round(s.AosDeg, 2),
            Ny = Math.Round(s.NyG, 3),
            RollRate = Math.Round(s.RollRateDegS, 2),
            Hdg = Math.Round(s.HeadingDeg, 1),
            HasHdg = s.HasHeading,
            Pitch = Math.Round(att.PitchDeg, 2),
            Bank = Math.Round(att.BankDeg, 2),
            AttSrc = att.Source.ToString(),
            AttConf = Math.Round(att.Confidence, 3),
            Gear = Math.Round(s.GearPercent, 1),
            Flaps = Math.Round(s.FlapsPercent, 1),
            Airbrake = Math.Round(s.AirbrakePercent, 1),
            Aileron = Round(s.AileronPercent, 1),
            Elevator = Round(s.ElevatorPercent, 1),
            Rudder = Round(s.RudderPercent, 1),
            Trimmer = Round(s.Trimmer, 3),
            StickAilerons = Round(s.StickAilerons, 3),
            StickElevator = Round(s.StickElevator, 3),
            Pedals = Round(s.Pedals, 3),
            Fuel = Math.Round(s.FuelKg, 1),
            Fuel0 = Math.Round(s.Fuel0Kg, 1),
        }, JsonOpts);
        TryEnqueue(line);
    }

    /// <summary>可空值取整：null 保持 null（写进 JSONL 时整条字段省略）。</summary>
    private static double? Round(double? value, int digits) =>
        value is double v ? Math.Round(v, digits) : null;

    /// <summary>在时间轴上记一个事件（死亡/重生/换机型/击落/数据中断）。</summary>
    /// <param name="kind">事件种类：death / spawn / vehicle_change / kill / gap。</param>
    /// <param name="vehicle">事件机型（spawn / vehicle_change 用）。</param>
    /// <param name="utc">事件 UTC 时间；不传则用当前时间。</param>
    /// <param name="target">击落事件的目标（被击落的玩家名）；其它事件留空，空值不写进 JSONL。</param>
    /// <param name="gap">数据中断事件的中断秒数（只有 kind=gap 写）；null 不写进 JSONL。
    /// 旧事件保持四条字段不变，旧版 FlightLog 解析器看到的就是原来的形状。</param>
    public void Event(string kind, string? vehicle = null, DateTime? utc = null, string? target = null,
                      double? gap = null)
    {
        if (_closed) return;
        TryEnqueue(JsonSerializer.Serialize(new EventLine
        {
            Type = "event",
            Kind = kind,
            Utc = (utc ?? DateTime.UtcNow).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Vehicle = vehicle ?? "",
            // 空 target/gap 走 JsonIgnoreCondition.WhenWritingNull：旧解析器看到的还是四条字段
            Target = string.IsNullOrWhiteSpace(target) ? null : target.Trim(),
            Gap = gap,
        }, JsonOpts));
    }

    /// <summary>在时间轴上打一个标记（预留接口）。</summary>
    public void Marker(string label)
    {
        if (_closed) return;
        TryEnqueue(JsonSerializer.Serialize(new MarkerLine
        {
            Type = "marker",
            Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Label = label,
        }, JsonOpts));
    }

    /// <summary>结束记录：写 end 并等写线程落盘。</summary>
    public void Stop()
    {
        lock (_stopGate)
        {
            if (_closed) return;
            TryEnqueue(JsonSerializer.Serialize(new EndLine
            {
                Type = "end",
                Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            }, JsonOpts));
            _closed = true;
            _queue.CompleteAdding();
        }
        if (!_writer.Join(3000))
            Log.Warn("记录器写线程 3 s 内未结束（尾部样本可能丢失）");
    }

    /// <summary>
    /// 入队一行。调用方可能来自多个线程（轮询线程写样本、hudmsg 线程写击落、主线程收尾），
    /// Stop() 可能刚好 CompleteAdding，所以这里吞掉那次竞争：丢了收尾后的一行也不抛异常。
    /// </summary>
    private void TryEnqueue(string line)
    {
        try { _queue.TryAdd(line); }
        catch (InvalidOperationException) { }   // CompleteAdding 已经跑过，正常退出竞争
    }

    public void Dispose() => Stop();

    private void WriteLoop()
    {
        try
        {
            using var w = new StreamWriter(_path, append: false, new UTF8Encoding(false)) { AutoFlush = false };
            int lines = 0;
            long lastFlush = Environment.TickCount64;
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                w.WriteLine(line);
                // 定期落盘：飞行中文件就能看到内容，崩溃也不会丢整局。
                // AutoFlush=false：每行一次系统调用太贵，靠这里的 100 行 / 1 s 手动 Flush 兜底，
                // Stop() 里 Join 前也会把队列写完 + 最后一次 Flush，所以收尾不丢数据。
                if (++lines % 100 == 0 || Environment.TickCount64 - lastFlush > 1000)
                {
                    w.Flush();
                    lastFlush = Environment.TickCount64;
                }
            }
            w.Flush();
        }
        catch (Exception ex)
        {
            Log.Error("记录器写盘失败", ("error", ex.Message));
        }
    }

    /// <summary>自检用：最近一次 Cleanup 的 keep 参数与运行线程 id（0 = 还没跑过）。</summary>
    internal static int LastCleanupKeep;
    internal static int LastCleanupThreadId;

    /// <summary>
    /// 把「清理旧记录」丢到后台线程池。Start 里同步做会因为删文件抖动而卡住轮询线程；
    /// 清理失败（权限/文件占用）只打日志，绝不影响本次录制。
    /// </summary>
    internal static void CleanupInBackground(int keep)
    {
        Task.Run(() =>
        {
            try { Cleanup(keep); }
            catch (Exception ex) { Log.Warn("记录器清理失败", ("error", ex.Message)); }
        });
    }

    /// <summary>清理旧记录：按文件名（时间戳）排序，保留最近的 keep-1 个（给新文件留一位）。</summary>
    internal static void Cleanup(int keep)
    {
        System.Threading.Volatile.Write(ref LastCleanupKeep, keep);
        System.Threading.Volatile.Write(ref LastCleanupThreadId, Environment.CurrentManagedThreadId);
        try
        {
            keep = Math.Clamp(keep, 2, 200);
            if (!Directory.Exists(Folder)) return;
            foreach (var old in PlanDeletions(Directory.GetFiles(Folder, Prefix + "*.jsonl"), keep))
            {
                try { File.Delete(old); }
                catch (Exception ex) { Log.Warn("记录器清理无法删除旧记录", ("file", old), ("error", ex.Message)); }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("记录器清理失败", ("error", ex.Message));
        }
    }

    /// <summary>纯函数：返回需要删除的文件（最老的在前）。文件名带时间戳，按名称排序即可。</summary>
    internal static List<string> PlanDeletions(IEnumerable<string> files, int keep)
    {
        keep = Math.Clamp(keep, 2, 200);
        var list = files.OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
        int remove = list.Count - (keep - 1);
        return remove > 0 ? list.Take(remove).ToList() : new List<string>();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class MetaLine
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("schema")] public int Schema { get; set; }
        [JsonPropertyName("app")] public string App { get; set; } = "";
        [JsonPropertyName("utc")] public string Utc { get; set; } = "";
        [JsonPropertyName("vehicle")] public string Vehicle { get; set; } = "";
        [JsonPropertyName("rate")] public string Rate { get; set; } = "";
    }

    private sealed class SampleLine
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("t")] public double T { get; set; }
        [JsonPropertyName("utc")] public string Utc { get; set; } = "";
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("inflight")] public bool InFlight { get; set; }
        [JsonPropertyName("vehicle")] public string Vehicle { get; set; } = "";
        [JsonPropertyName("ias_kmh")] public double Ias { get; set; }
        [JsonPropertyName("tas_kmh")] public double Tas { get; set; }
        [JsonPropertyName("mach")] public double Mach { get; set; }
        [JsonPropertyName("alt_m")] public double Alt { get; set; }
        [JsonPropertyName("ralt_m")] public double? Ralt { get; set; }
        [JsonPropertyName("vy_ms")] public double Vy { get; set; }
        [JsonPropertyName("aoa_deg")] public double Aoa { get; set; }
        [JsonPropertyName("aos_deg")] public double Aos { get; set; }
        [JsonPropertyName("ny_g")] public double Ny { get; set; }
        [JsonPropertyName("roll_rate_dps")] public double RollRate { get; set; }
        [JsonPropertyName("hdg_deg")] public double Hdg { get; set; }
        [JsonPropertyName("has_hdg")] public bool HasHdg { get; set; }
        [JsonPropertyName("pitch_deg")] public double Pitch { get; set; }
        [JsonPropertyName("bank_deg")] public double Bank { get; set; }
        [JsonPropertyName("att_src")] public string AttSrc { get; set; } = "";
        [JsonPropertyName("att_conf")] public double AttConf { get; set; }
        [JsonPropertyName("gear_pct")] public double Gear { get; set; }
        [JsonPropertyName("flaps_pct")] public double Flaps { get; set; }
        [JsonPropertyName("airbrake_pct")] public double Airbrake { get; set; }
        // 舵面实际偏转（−100..100 %）：/state "aileron, %" 等。没这个键的机型为 null，
        // 序列化时整条省略（旧版 FlightLog 读到的样本形状不变）。
        [JsonPropertyName("aileron_pct")] public double? Aileron { get; set; }
        [JsonPropertyName("elevator_pct")] public double? Elevator { get; set; }
        [JsonPropertyName("rudder_pct")] public double? Rudder { get; set; }
        // 俯仰配平量（−1..1）：/indicators "trimmer"。8111 的 stick_* 已被配平/增稳合并，
        // 只有这个键还保留原始设定，所以单独记它（不记 stick_*，那三个等于舵面 ÷ 100）。
        [JsonPropertyName("trimmer")] public double? Trimmer { get; set; }
        // 舵面指令轴（−1..1）：/indicators "stick_ailerons" / "stick_elevator" / "pedals"。
        // 含配平与增稳，不是原始杆量；记它的理由是精度 —— %/100 的 1% 量化在这里是 0.001 级。
        [JsonPropertyName("stick_ailerons")] public double? StickAilerons { get; set; }
        [JsonPropertyName("stick_elevator")] public double? StickElevator { get; set; }
        [JsonPropertyName("pedals")] public double? Pedals { get; set; }
        [JsonPropertyName("fuel_kg")] public double Fuel { get; set; }
        [JsonPropertyName("fuel0_kg")] public double Fuel0 { get; set; }
    }

    private sealed class MarkerLine
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("utc")] public string Utc { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
    }

    private sealed class EventLine
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("utc")] public string Utc { get; set; } = "";
        [JsonPropertyName("vehicle")] public string Vehicle { get; set; } = "";

        // 击落事件的目标（被击落玩家名）。只有 kill 事件写这个字段：
        // null 时 JsonIgnoreCondition.WhenWritingNull 直接省略，旧工具无需兼容新字段。
        [JsonPropertyName("target")] public string? Target { get; set; }

        // 数据中断秒数（只有 kind=gap 写）。旧事件同样省略，事件行仍是原来的四条字段。
        [JsonPropertyName("gap")] public double? Gap { get; set; }
    }

    private sealed class EndLine
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("utc")] public string Utc { get; set; } = "";
    }
}
