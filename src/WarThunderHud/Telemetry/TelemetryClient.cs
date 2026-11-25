// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;

namespace WtHud.Telemetry;

/// <summary>
/// Polls the two hot endpoints of the War Thunder local API.
/// The embedded server is single threaded, so requests are strictly serial.
/// </summary>
/// <remarks>
/// ★ 轮询跑在自己的后台线程（<see cref="ThreadPriority.AboveNormal"/>），不在渲染循环里：
/// 渲染循环只读「最新快照」（<see cref="TryGetLatest"/>），所以 60~100 fps 的绘制、
/// DX present 抖动或帧内长任务不会再把轮询采样卡出空洞。
/// 每 5 s 打一行延迟统计：<c>poll: hz=.. p50=.. p95=.. p99=.. max=.. errors=..</c>。
/// </remarks>
public sealed class TelemetryClient : IDisposable
{
    public const string DefaultBaseUrl = "http://127.0.0.1:8111";

    private const int LatencyWindowCapacity = 8192;

    /// <summary>最小轮询周期（ms）：interval=0 时补足到它，别让本地 8111 把专用线程空转成 100+ Hz。</summary>
    internal const int MinCycleMs = 15;

    /// <summary>/state 失败（没游戏、连接被拒）时的退避周期（ms），别把专用线程空转成 100% CPU。</summary>
    internal const int NoDataBackoffMs = 20;

    private readonly string _stateUrl;
    private readonly string _indicatorsUrl;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _intervalMs;

    // 最新快照发布区：轮询线程写、渲染循环读，用锁交换引用（快照本身不可变）。
    private readonly object _latestLock = new();
    private TelemetrySnapshot? _latest;
    private long _latestSeq;
    private long _seq;

    // 5 s 延迟窗口：固定数组复用，轮询路径上零分配（每 5 s 排序时才碰它）。
    private readonly double[] _windowLatMs = new double[LatencyWindowCapacity];
    private int _windowLatCount;
    private int _windowSamples;
    private int _windowErrors;
    private int _windowTimeouts;
    private int _windowHttpErrors;
    private int _windowOtherErrors;
    private double _windowMaxMs;
    private long _windowStartTicks;

    private Thread? _thread;
    private int _disposed;

    /// <summary>统计窗口长度；自检会临时调小，正常运行保持 5 s。</summary>
    internal static int StatsWindowMs = 5000;

    public event Action<TelemetrySnapshot>? SnapshotReceived;

    /// <summary>Diagnostics: smoothed timings in milliseconds.</summary>
    public double AvgCycleMs { get; private set; }
    public double AvgStateRequestMs { get; private set; }
    public double AvgIndRequestMs { get; private set; }

    /// <summary>轮询线程（自检用来断言线程名/优先级/后台属性；运行时不需要碰）。</summary>
    internal Thread? PollThread => _thread;

    /// <summary>最新一帧；还没拿到过数据时为 null。</summary>
    public TelemetrySnapshot? Latest
    {
        get { lock (_latestLock) return _latest; }
    }

    /// <summary>最新一帧的序号；每成功解析一帧 +1。</summary>
    public long LatestSeq
    {
        get { lock (_latestLock) return _latestSeq; }
    }

    /// <summary>
    /// 线程安全地取「最新快照 + 序号」。渲染循环每帧调一次，拿到的引用保持期间不会被改。
    /// </summary>
    public bool TryGetLatest(out TelemetrySnapshot snapshot, out long seq)
    {
        lock (_latestLock)
        {
            if (_latest is null)
            {
                snapshot = null!;
                seq = 0;
                return false;
            }
            snapshot = _latest;
            seq = _latestSeq;
            return true;
        }
    }

    public TelemetryClient(int intervalMs = 25, string baseUrl = DefaultBaseUrl)
    {
        _intervalMs = Math.Clamp(intervalMs, 0, 500);
        string root = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
        _stateUrl = root + "/state";
        _indicatorsUrl = root + "/indicators";
        // Small requests over loopback stall ~40 ms with Nagle + delayed ACK enabled,
        // which capped the effective sample rate at ~17 Hz. Disable Nagle explicitly.
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = async (context, token) =>
            {
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            },
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(900) };
    }

    public void Start()
    {
        if (_thread != null) return;
        _windowStartTicks = Stopwatch.GetTimestamp();
        // 专用线程而不是线程池：线程池会被渲染/面板/写盘任务挤占，采样节奏就跟着抖。
        _thread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "telemetry-poll",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    private void PollLoop()
    {
        try
        {
            // Each request to the embedded server costs ~10 ms, so an extra sleep on top of the
            // round trip halves the achievable rate. /state is polled every cycle (it carries
            // everything except heading/native attitude); /indicators every second cycle.
            string? lastIndicators = null;
            int iter = 0;
            while (!_cts.IsCancellationRequested)
            {
                long cycleStart = Stopwatch.GetTimestamp();

                string? state = Get(_stateUrl, out double stateMs);
                if (_cts.IsCancellationRequested) break;
                AvgStateRequestMs = AvgStateRequestMs * 0.9 + stateMs * 0.1;

                if ((iter & 1) == 0)
                {
                    string? fresh = Get(_indicatorsUrl, out double indMs);
                    if (_cts.IsCancellationRequested) break;
                    AvgIndRequestMs = AvgIndRequestMs * 0.9 + indMs * 0.1;
                    if (fresh != null) lastIndicators = fresh;
                }

                // ★ 本次 /state 请求失败（state == null）就整轮跳过 Parse/发布/事件：
                // 不要拿上一次的 /indicators 拼一个 valid=false 的快照发出去 —— 那等于凭空
                // 造了一帧「掉线」，会让融合姿态/记录器/UI 跟着抖一下。等下一轮重新拿 /state。
                if (state != null)
                {
                    try
                    {
                        long seq = Interlocked.Increment(ref _seq);
                        var snap = TelemetryParser.Parse(state, lastIndicators, _clock.Elapsed, DateTime.UtcNow, seq);
                        if (snap != null)
                        {
                            lock (_latestLock)
                            {
                                _latest = snap;
                                _latestSeq = seq;
                            }
                            _windowSamples++;
                            // 订阅方在轮询线程上跑：它只改「最新快照/融合姿态」，渲染循环只读。
                            SnapshotReceived?.Invoke(snap);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!_cts.IsCancellationRequested) CountError(ex);
                    }
                }

                AvgCycleMs = AvgCycleMs * 0.9 + Stopwatch.GetElapsedTime(cycleStart).TotalMilliseconds * 0.1;
                iter++;
                FlushStatsIfDue(Stopwatch.GetTimestamp());

                if (_intervalMs > 0)
                {
                    if (_cts.Token.WaitHandle.WaitOne(_intervalMs)) break;
                }
                else
                {
                    // ★ 最小轮询间隔：本地 8111 一次往返只要 5~10 ms，interval=0（overlay 就是传 0）
                    // 时「拿到就立刻再问」会把专用线程空转成 100+ Hz。本轮耗时不够就补足；
                    // 没数据（/state 失败，连接被拒）退回 20 ms 退避，别把专用线程空转成 100% CPU。
                    int minPeriodMs = state is null ? NoDataBackoffMs : MinCycleMs;
                    double cycleMs = Stopwatch.GetElapsedTime(cycleStart).TotalMilliseconds;
                    int waitMs = (int)Math.Ceiling(minPeriodMs - cycleMs);
                    if (waitMs > 0 && _cts.Token.WaitHandle.WaitOne(waitMs)) break;
                }
            }
        }
        catch (Exception ex)
        {
            // 专用线程的异常会带走整个进程，这里必须兜住（含 Dispose 竞态）。
            if (!_cts.IsCancellationRequested) Console.WriteLine("poll: thread stopped: " + ex.Message);
        }
    }

    /// <summary>发一次 HTTP 请求并计时（失败也计时、也计数）。返回 null = 本次失败。</summary>
    private string? Get(string url, out double elapsedMs)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            string? text = _http.GetStringAsync(url, _cts.Token).GetAwaiter().GetResult();
            elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            RecordLatency(elapsedMs);
            return text;
        }
        catch (Exception ex)
        {
            elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            RecordLatency(elapsedMs);
            if (!_cts.IsCancellationRequested) CountError(ex);   // 主动退出不算错误
            return null;
        }
    }

    private void CountError(Exception ex)
    {
        _windowErrors++;
        switch (ex)
        {
            case OperationCanceledException: _windowTimeouts++; break;   // HttpClient.Timeout / 取消
            case HttpRequestException: _windowHttpErrors++; break;       // 连接被拒、连接被重置
            default: _windowOtherErrors++; break;
        }
    }

    private void RecordLatency(double ms)
    {
        if (_windowLatCount < _windowLatMs.Length) _windowLatMs[_windowLatCount++] = ms;
        if (ms > _windowMaxMs) _windowMaxMs = ms;
    }

    private void FlushStatsIfDue(long nowTicks)
    {
        double elapsedS = (nowTicks - _windowStartTicks) / (double)Stopwatch.Frequency;
        if (elapsedS * 1000.0 < StatsWindowMs) return;

        var (p50, p95, p99) = Percentiles(_windowLatMs, _windowLatCount);
        double hz = elapsedS > 0 ? _windowSamples / elapsedS : 0;
        Console.WriteLine(FormatPollLine(hz, p50, p95, p99, _windowMaxMs, _windowErrors));
        if (_windowErrors > 0)
            Console.WriteLine(FormatPollErrors(_windowTimeouts, _windowHttpErrors, _windowOtherErrors));

        _windowStartTicks = nowTicks;
        _windowLatCount = 0;
        _windowSamples = 0;
        _windowErrors = _windowTimeouts = _windowHttpErrors = _windowOtherErrors = 0;
        _windowMaxMs = 0;
    }

    /// <summary>最近秩百分位；缓冲区不够长时只用已记录的前 N 个。</summary>
    internal static (double P50, double P95, double P99) Percentiles(double[] values, int count)
    {
        if (count <= 0) return (0, 0, 0);
        if (count > values.Length) count = values.Length;
        Array.Sort(values, 0, count);
        return (Rank(values, count, 0.50), Rank(values, count, 0.95), Rank(values, count, 0.99));
    }

    private static double Rank(double[] sorted, int count, double p)
    {
        int idx = (int)Math.Ceiling(p * count) - 1;
        if (idx < 0) idx = 0;
        if (idx >= count) idx = count - 1;
        return sorted[idx];
    }

    /// <summary>5 s 汇总行。样例：<c>poll: hz=29.4 p50=12ms p95=48ms p99=120ms max=2660ms errors=0</c></summary>
    internal static string FormatPollLine(double hz, double p50, double p95, double p99, double maxMs, int errors)
        => string.Create(CultureInfo.InvariantCulture,
            $"poll: hz={hz:F1} p50={p50:F0}ms p95={p95:F0}ms p99={p99:F0}ms max={maxMs:F0}ms errors={errors}");

    /// <summary>只在有错误时打一行，避免每次失败刷屏。<c>poll error: timeout x12 http x3</c></summary>
    internal static string FormatPollErrors(int timeouts, int http, int other)
    {
        var sb = new StringBuilder("poll error:");
        if (timeouts > 0) sb.Append(" timeout x").Append(timeouts);
        if (http > 0) sb.Append(" http x").Append(http);
        if (other > 0) sb.Append(" other x").Append(other);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        var t = _thread;
        if (t != null && t != Thread.CurrentThread && !t.Join(1500))
            Console.WriteLine("poll: thread did not stop in 1.5 s");
        _http.Dispose();
        _cts.Dispose();
    }
}
