// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
//
// 编队误差实测 · 采集端（Windows 独立 exe，不需要对方装 Python）
//
// 与 tools/maperr_capture.py 输出**完全相同**的 CSV 列，所以 maperr_analyze.py 直接能用。
// 只读本机 8111：不注入、不读内存、不改游戏。

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MaperrCapture;

internal static class Program
{
    private const string BaseUrl = "http://127.0.0.1:8111";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(900) };

    private static readonly string[] Fields =
    {
        "utc_ms", "grid_w", "grid_h",
        "self_x", "self_y", "self_dx", "self_dy",
        "other_x", "other_y", "other_dx", "other_dy", "other_icon", "other_type",
        "n_aircraft", "dist_m",
        "ias", "tas", "alt", "vy", "ny", "wx", "aoa",
    };

    private static int Main(string[] args)
    {
        // 控制台默认是系统代码页（GBK），中文提示会变乱码；显式切 UTF-8。
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        string label = "A";
        string? outArg = null;
        double hz = 4.0, seconds = 0, probe = 900;
        bool pauseAtEnd = args.Length == 0;   // 纯双击启动时，结束时停住让人看清汇总

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--label": label = Next(args, ref i, label); break;
                case "--out": outArg = Next(args, ref i, ""); break;
                case "--hz": hz = Num(Next(args, ref i, "4"), 4); break;
                case "--seconds": seconds = Num(Next(args, ref i, "0"), 0); break;
                case "--probe": probe = Num(Next(args, ref i, "900"), 900); break;
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
            }
        }

        // 默认把 CSV 落在桌面，省得对方找不到文件
        string outPath = string.IsNullOrWhiteSpace(outArg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"maperr-{label}.csv")
            : outArg;

        double period = 1.0 / Math.Max(0.5, hz);
        Console.WriteLine($"[{label}] 等待 8111 接口（最多 {probe:F0} s）…");
        Console.WriteLine($"[{label}] CSV 将写入：{outPath}");
        var grid = ReadGrid((int)Math.Max(2, probe * 2));
        if (grid is null)
        {
            Console.WriteLine("拿不到 /map_info.json 的 grid_size：游戏没开、或不在对局里。退出。");
            Pause(pauseAtEnd);
            return 2;
        }

        var (gridW, gridH) = grid.Value;
        Console.WriteLine($"[{label}] 地图尺寸 {gridW:F0} x {gridH:F0} m，开始采集 → {outPath}");

        bool stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };

        long rows = 0, ambiguous = 0, noOther = 0, noSelf = 0;
        var clock = Stopwatch.StartNew();
        var nextReport = 0.0;

        // 先探一次能不能写（桌面可能被重定向到 OneDrive 或只读）
        try { using var probeStream = File.Create(outPath); }
        catch (Exception ex)
        {
            Console.WriteLine($"写不了 {outPath}：{ex.Message}");
            Pause(pauseAtEnd);
            return 3;
        }

        using var writer = new StreamWriter(outPath, false, new UTF8Encoding(false)) { AutoFlush = false };
        writer.NewLine = "\r\n";
        WriteHeader(writer);
        while (!stop)
        {
            if (seconds > 0 && clock.Elapsed.TotalSeconds >= seconds) break;

            double tickStart = clock.Elapsed.TotalSeconds;
            long utcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var objs = TryGetArray("/map_obj.json");
            if (objs is null)
            {
                Thread.Sleep(TimeSpan.FromSeconds(period));
                continue;
            }

            var pick = Pick(objs.RootElement, gridW, gridH);
            if (pick is null)
            {
                // 机库/部署界面没有 Player 条目：不写行，但要让人知道"在等什么"
                noSelf++;
                if (noSelf == 1 || noSelf % 20 == 0)
                    Console.WriteLine($"[{label}] 还没看到自己的飞机（在机库/部署界面？）已等 {noSelf} 帧");
                Thread.Sleep(TimeSpan.FromSeconds(period));
                continue;
            }

            var (self, other, nAircraft) = pick.Value;
            if (nAircraft > 1) ambiguous++;
            if (other is null) noOther++;

            var st = StateValues();
            double? dist = other is null
                ? null
                : Math.Sqrt(Sq((other.Value.X - self.X) * gridW) + Sq((other.Value.Y - self.Y) * gridH));

            writer.Write(utcMs.ToString(CultureInfo.InvariantCulture)); writer.Write(',');
            writer.Write(F(gridW, 1)); writer.Write(',');
            writer.Write(F(gridH, 1)); writer.Write(',');
            writer.Write(F(self.X, 6)); writer.Write(',');
            writer.Write(F(self.Y, 6)); writer.Write(',');
            writer.Write(F(self.Dx, 4)); writer.Write(',');
            writer.Write(F(self.Dy, 4)); writer.Write(',');
            writer.Write(other is null ? "" : F(other.Value.X, 6)); writer.Write(',');
            writer.Write(other is null ? "" : F(other.Value.Y, 6)); writer.Write(',');
            writer.Write(other is null ? "" : F(other.Value.Dx, 4)); writer.Write(',');
            writer.Write(other is null ? "" : F(other.Value.Dy, 4)); writer.Write(',');
            writer.Write(other is null ? "" : other.Value.Icon); writer.Write(',');
            writer.Write(other is null ? "" : other.Value.Type); writer.Write(',');
            writer.Write(nAircraft.ToString(CultureInfo.InvariantCulture)); writer.Write(',');
            writer.Write(dist is null ? "" : F(dist.Value, 0)); writer.Write(',');
            writer.Write(F(st.Ias, 1)); writer.Write(',');
            writer.Write(F(st.Tas, 1)); writer.Write(',');
            writer.Write(F(st.Alt, 1)); writer.Write(',');
            writer.Write(F(st.Vy, 2)); writer.Write(',');
            writer.Write(F(st.Ny, 2)); writer.Write(',');
            writer.Write(F(st.Wx, 2)); writer.Write(',');
            writer.Write(F(st.Aoa, 2));
            writer.WriteLine();
            rows++;

            double now = clock.Elapsed.TotalSeconds;
            if (now >= nextReport)
            {
                nextReport = now + 1.0;
                string seen = dist is null ? "   --   " : $"{dist.Value,6:F0} m";
                Console.WriteLine($"[{label}] t={now,6:F1}s  rows={rows,5}  目标 {seen}  " +
                                  $"其他飞机={nAircraft}  ny={st.Ny:F2} wx={st.Wx:+0.0;-0.0;0.0}");
                writer.Flush();
            }

            double sleep = period - (clock.Elapsed.TotalSeconds - tickStart);
            if (sleep > 0) Thread.Sleep(TimeSpan.FromSeconds(sleep));
        }

        writer.Flush();
        writer.Dispose();
        Console.WriteLine($"[{label}] 完成：{rows} 行 → {outPath}");
        if (rows == 0)
            Console.WriteLine($"[{label}] ★ 一行都没采到：多半是没进对局，或停在机库/部署界面。" +
                              "请上飞机（真正飞起来）后再采。");
        if (ambiguous > 0)
            Console.WriteLine($"[{label}] ★ 有 {ambiguous} 次采样里场上不止一架其它飞机，other_* 取的是最近的那架 —— " +
                              "建议下一轮改成只放两架飞机的房间。");
        if (noOther > 0)
            Console.WriteLine($"[{label}] ★ 有 {noOther} 次采样没看到对方（超出地图显示范围/不同队/已阵亡）。");
        Pause(pauseAtEnd);
        return 0;
    }

    private static void Pause(bool enabled)
    {
        if (!enabled) return;
        Console.WriteLine("按任意键关闭…");
        try { Console.ReadKey(true); } catch { }
    }

    private readonly record struct Obj(double X, double Y, double Dx, double Dy, string Icon, string Type);

    private static (Obj Self, Obj? Other, int Count)? Pick(JsonElement root, double gridW, double gridH)
    {
        Obj? self = null;
        Obj? best = null;
        double bestSep = double.MaxValue;
        int count = 0;

        foreach (var e in root.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string type = Str(e, "type");
            if (type != "aircraft") continue;

            string icon = Str(e, "icon");
            var o = new Obj(GetD(e, "x"), GetD(e, "y"), GetD(e, "dx"), GetD(e, "dy"), icon, type);

            if (icon == "Player") { self = o; continue; }

            count++;
            if (self is null) { if (best is null) best = o; continue; }
            if (gridW <= 0 || gridH <= 0) { best ??= o; continue; }

            double sep = Math.Sqrt(Sq((o.X - self.Value.X) * gridW) + Sq((o.Y - self.Value.Y) * gridH));
            if (sep < bestSep) { bestSep = sep; best = o; }
        }

        if (self is null) return null;

        // self 可能出现在其它飞机之后：拿到 self 后重算一次最近者
        if (best is not null && gridW > 0 && gridH > 0)
        {
            Obj? nearest = null;
            double nearestSep = double.MaxValue;
            foreach (var e in root.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (Str(e, "type") != "aircraft") continue;
                string icon = Str(e, "icon");
                if (icon == "Player") continue;
                var o = new Obj(GetD(e, "x"), GetD(e, "y"), GetD(e, "dx"), GetD(e, "dy"), icon, "aircraft");
                double sep = Math.Sqrt(Sq((o.X - self.Value.X) * gridW) + Sq((o.Y - self.Value.Y) * gridH));
                if (sep < nearestSep) { nearestSep = sep; nearest = o; }
            }
            best = nearest;
        }

        return (self.Value, best, count);
    }

    private static (double Ias, double Tas, double Alt, double Vy, double Ny, double Wx, double Aoa) StateValues()
    {
        // ★ 端点是 /state（没有 .json 后缀），写成 /state.json 会 404 并静默变成全 0。
        var doc = TryGet("/state");
        if (doc is null) return (0, 0, 0, 0, 1, 0, 0);
        var r = doc.RootElement;
        return (GetD(r, "IAS, km/h"), GetD(r, "TAS, km/h"), GetD(r, "H, m"), GetD(r, "Vy, m/s"),
                GetD(r, "Ny", 1.0), GetD(r, "Wx, deg/s"), GetD(r, "AoA, deg"));
    }

    private static (double, double)? ReadGrid(int retries)
    {
        for (int i = 0; i < retries; i++)
        {
            var doc = TryGet("/map_info.json");
            if (doc is not null && doc.RootElement.TryGetProperty("grid_size", out var gs)
                && gs.ValueKind == JsonValueKind.Array && gs.GetArrayLength() >= 2)
            {
                double w = gs[0].GetDouble(), h = gs[1].GetDouble();
                if (w > 1 && h > 1) return (w, h);
            }
            Thread.Sleep(500);
        }
        return null;
    }

    private static JsonDocument? TryGet(string path)
    {
        try
        {
            string body = Http.GetStringAsync(BaseUrl + path).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(body)) return null;
            return JsonDocument.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    private static JsonDocument? TryGetArray(string path)
    {
        var doc = TryGet(path);
        if (doc is null) return null;
        if (doc.RootElement.ValueKind != JsonValueKind.Array) { doc.Dispose(); return null; }
        return doc;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double GetD(JsonElement e, string name, double fallback = 0)
    {
        if (!e.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fallback,
            _ => fallback,
        };
    }

    private static void WriteHeader(StreamWriter w)
    {
        w.WriteLine(string.Join(',', Fields));
    }

    private static string Next(string[] args, ref int i, string fallback)
        => i + 1 < args.Length ? args[++i] : fallback;

    private static double Num(string text, double fallback)
        => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static string F(double v, int digits) => v.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static double Sq(double v) => v * v;

    private static void PrintHelp()
    {
        Console.WriteLine("""
maperr-capture —— WtHud 编队误差实测采集端（只读本机 8111）

用法:
  maperr-capture.exe [--label A] [--out A.csv] [--hz 4] [--seconds 0] [--probe 900]

  --label    机号，只影响屏幕提示
  --out      输出 CSV 文件名
  --hz       采样频率，默认 4
  --seconds  采集时长，0 = 一直采到 Ctrl+C
  --probe    等待 8111 可用的秒数（进对局前启动也能等）

停止: 按 Ctrl+C（CSV 每秒落盘，中途停不丢数据）
""");
    }
}
