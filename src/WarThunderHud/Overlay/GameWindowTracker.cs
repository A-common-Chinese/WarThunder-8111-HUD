// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Diagnostics;

namespace WtHud.Overlay;

/// <summary>
/// Locates the game window and returns its client area in screen coordinates, so the overlay can
/// cover exactly the rendered viewport (windowed mode, moved windows, non-100% scaling).
/// </summary>
public static class GameWindowTracker
{
    /// <summary>Window handle of the game main window (IntPtr.Zero when not found).</summary>
    public static IntPtr GameHwnd { get; private set; }

    /// <summary>True when the game is the foreground window and not minimized.</summary>
    public static bool IsActive
    {
        get
        {
            var h = GameHwnd;
            if (h == IntPtr.Zero) return false;
            if (NativeMethods.IsIconic(h)) return false;
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == h) return true;
            NativeMethods.GetWindowThreadProcessId(fg, out var fgPid);
            NativeMethods.GetWindowThreadProcessId(h, out var gamePid);
            return fgPid == gamePid;
        }
    }

    public static bool TryGetClientRect(out int x, out int y, out int w, out int h, out bool usable)
    {
        usable = false;
        x = y = w = h = 0;

        foreach (var name in new[] { "aces", "aces_BE" })
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var proc in procs)
            {
                var pid = proc.Id;
                var best = IntPtr.Zero;
                var bestArea = 0;
                int bx = 0, by = 0, bw = 0, bh = 0;

                // 遍历该进程的所有顶层窗口，取客户区最大的可见窗口（启动器/闪屏都比主渲染窗小）
                try
                {
                    NativeMethods.EnumWindows((hwnd, _) =>
                    {
                        NativeMethods.GetWindowThreadProcessId(hwnd, out var wpid);
                        if (wpid != pid) return true;
                        if (!NativeMethods.IsWindowVisible(hwnd)) return true;
                        if (!NativeMethods.GetClientRect(hwnd, out var rc)) return true;

                        var cw = rc.Right - rc.Left;
                        var ch = rc.Bottom - rc.Top;
                        if (cw < 320 || ch < 240) return true;

                        var area = cw * ch;
                        // 整屏大小的窗口通常是覆盖层/过渡窗口，优先选真正的游戏渲染窗
                        var scrW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
                        var scrH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
                        var fullScreenArea = scrW * scrH;
                        var isFull = fullScreenArea > 0 && area >= fullScreenArea * 0.95;
                        if (isFull && bestArea > 0 && bestArea < fullScreenArea * 0.95) return true;
                        if (area <= bestArea) return true;

                        if (Environment.GetEnvironmentVariable("WT_TRACKDBG") == "1")
                            Console.WriteLine($"cand pid={pid} hwnd={hwnd} {cw}x{ch}");

                        var pt = new NativeMethods.POINT { X = 0, Y = 0 };
                        if (!NativeMethods.ClientToScreen(hwnd, ref pt)) return true;

                        best = hwnd; bestArea = area;
                        bx = pt.X; by = pt.Y; bw = cw; bh = ch;
                        return true;
                    }, IntPtr.Zero);
                }
                finally { proc.Dispose(); }

                if (best == IntPtr.Zero) continue;

                if (Environment.GetEnvironmentVariable("WT_TRACKDBG") == "1")
                    Console.WriteLine($"track pid={pid} hwnd={best} client={bw}x{bh} at {bx},{by}");
                GameHwnd = best;
                usable = !NativeMethods.IsIconic(best);
                x = bx; y = by; w = bw; h = bh;
                return true;
            }
        }

        GameHwnd = IntPtr.Zero;
        return false;
    }
}
