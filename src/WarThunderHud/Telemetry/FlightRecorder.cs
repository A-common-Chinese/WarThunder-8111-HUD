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

    /// <summary>开始一局记录：清理旧文件（保留最近 recordKeep 个）、建目录、写 meta。</summary>
    public static FlightRecorder Start(HudConfig cfg, TelemetrySnapshot first)
    {
        Directory.CreateDirectory(Folder);
        Cleanup(cfg.RecordKeep);

        string vehicle = string.IsNullOrWhiteSpace(first.VehicleType) ? "unknown" : first.VehicleType;
        foreach (var bad in System.IO.Path.GetInvalidFileNameChars()) vehicle = vehicle.Replace(bad, '_');
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = System.IO.Path.Combine(Folder, $"{Prefix}{stamp}-{vehicle}.jsonl");

        var rec = new FlightRecorder(path);
        rec._queue.Add(JsonSerializer.Serialize(new MetaLine
        {
            Type = "meta",
            Schema = 1,
            App = WtHud.Program.ShortVersion,
            Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Vehicle = vehicle,
            Rate = "per-sample",
        }, JsonOpts));
        return rec;
    }

    /// <summary>写一帧本机数据（在渲染循环里调用，只入队）。</summary>
    public void Add(TelemetrySnapshot s, AttitudeSolution att)
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
            Ralt = s.RadioAltM is double ra ? Math.Round(ra, 1) : null,
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
            Fuel = Math.Round(s.FuelKg, 1),
            Fuel0 = Math.Round(s.Fuel0Kg, 1),
        }, JsonOpts);
        _queue.TryAdd(line);
    }

    /// <summary>在时间轴上记一个事件（死亡/重生/换机型）。</summary>
    public void Event(string kind, string? vehicle = null)
    {
        if (_closed) return;
        _queue.TryAdd(JsonSerializer.Serialize(new EventLine
        {
            Type = "event",
            Kind = kind,
            Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Vehicle = vehicle ?? "",
        }, JsonOpts));
    }

    /// <summary>在时间轴上打一个标记（预留接口）。</summary>
    public void Marker(string label)
    {
        if (_closed) return;
        _queue.TryAdd(JsonSerializer.Serialize(new MarkerLine
        {
            Type = "marker",
            Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Label = label,
        }, JsonOpts));
    }

    /// <summary>结束记录：写 end 并等写线程落盘。</summary>
    public void Stop()
    {
        if (_closed) return;
        _queue.Add(JsonSerializer.Serialize(new EndLine
        {
            Type = "end",
            Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        }, JsonOpts));
        _closed = true;
        _queue.CompleteAdding();
        if (!_writer.Join(3000))
            Console.WriteLine("recorder: writer thread did not finish in 3 s");
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
            Console.WriteLine("recorder: write failed: " + ex.Message);
        }
    }

    /// <summary>清理旧记录：按文件名（时间戳）排序，保留最近的 keep-1 个（给新文件留一位）。</summary>
    internal static void Cleanup(int keep)
    {
        try
        {
            keep = Math.Clamp(keep, 2, 200);
            if (!Directory.Exists(Folder)) return;
            foreach (var old in PlanDeletions(Directory.GetFiles(Folder, Prefix + "*.jsonl"), keep))
            {
                try { File.Delete(old); } catch { }
            }
        }
        catch { }
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
    }

    private sealed class EndLine
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("utc")] public string Utc { get; set; } = "";
    }
}
