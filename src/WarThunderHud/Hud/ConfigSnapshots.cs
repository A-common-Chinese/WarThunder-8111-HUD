// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Globalization;
using System.Text.Json;

namespace WtHud.Hud;

/// <summary>
/// Snapshots of the whole config, written next to the executable:
/// <list type="bullet">
/// <item><c>temp/_cancel.json</c> — state when F1 was opened; CANCEL restores it. Always present, overwritten on every open.</item>
/// <item><c>temp/yyyyMMdd-HHmmss-label.json</c> — one file per edit, newest 10 kept (undo/rollback groundwork).</item>
/// <item><c>temp/yyyyMMdd-HHmmss-restore.json</c> — 恢复动作的审计记录（恢复后的最终状态）。</item>
/// </list>
/// </summary>
public static class ConfigSnapshots
{
    private const int MaxHistory = 10;

    /// <summary>快照文件名的时间前缀格式（save / restore 都一样）。</summary>
    private const string StampFormat = "yyyyMMdd-HHmmss";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "temp");

    private static string CancelPath => Path.Combine(Directory, "_cancel.json");

    /// <summary>Snapshots of the current (unsaved) panel session; wiped when the panel saves.</summary>
    private static string SessionDirectory => Path.Combine(Directory, "session");

    public static void BeginSession()
    {
        try
        {
            if (System.IO.Directory.Exists(SessionDirectory))
                System.IO.Directory.Delete(SessionDirectory, true);
            EnsureDirectory();
        }
        catch (Exception ex) { Console.WriteLine("session init failed: " + ex.Message); }
    }

    /// <summary>One snapshot per edit while the panel is open (newest 10 kept).</summary>
    public static void SaveSession(HudConfig cfg, string label = "edit")
    {
        try
        {
            System.IO.Directory.CreateDirectory(SessionDirectory);
            var safe = string.Join("_", (label ?? "edit").Split(Path.GetInvalidFileNameChars()));
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{safe}.json";
            File.WriteAllText(Path.Combine(SessionDirectory, name), JsonSerializer.Serialize(cfg, Options));

            var stale = System.IO.Directory.GetFiles(SessionDirectory, "*.json")
                .OrderByDescending(f => f)
                .Skip(MaxHistory)
                .ToList();
            foreach (var old in stale) File.Delete(old);
        }
        catch (Exception ex) { Console.WriteLine("session snapshot failed: " + ex.Message); }
    }

    public static void EndSession()
    {
        try
        {
            if (System.IO.Directory.Exists(SessionDirectory))
                System.IO.Directory.Delete(SessionDirectory, true);
        }
        catch (Exception ex) { Console.WriteLine("session cleanup failed: " + ex.Message); }
    }

    private static void EnsureDirectory() => System.IO.Directory.CreateDirectory(Directory);

    /// <summary>Called when the panel opens: remember the state CANCEL should return to.</summary>
    public static void MarkCancelPoint(HudConfig cfg)
    {
        try
        {
            EnsureDirectory();
            File.WriteAllText(CancelPath, JsonSerializer.Serialize(cfg, Options));
        }
        catch (Exception ex) { Console.WriteLine("snapshot mark failed: " + ex.Message); }
    }

    /// <summary>Restores the cancel point into <paramref name="target"/>; returns false when unavailable.</summary>
    public static bool RestoreCancelPoint(HudConfig target)
    {
        try
        {
            if (!File.Exists(CancelPath)) return false;
            var json = File.ReadAllText(CancelPath);
            var loaded = JsonSerializer.Deserialize<HudConfig>(json);
            if (loaded is null) return false;
            foreach (var prop in typeof(HudConfig).GetProperties()
                         .Where(pr => pr.CanRead && pr.CanWrite))
                prop.SetValue(target, prop.GetValue(loaded));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("snapshot restore failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Writes a history snapshot and prunes everything beyond the newest 10.</summary>
    public static void SaveHistory(HudConfig cfg, string label)
    {
        try
        {
            EnsureDirectory();
            var safe = string.Join("_", (label ?? "edit").Split(Path.GetInvalidFileNameChars()));
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{safe}.json";
            File.WriteAllText(Path.Combine(Directory, name), JsonSerializer.Serialize(cfg, Options));

            var keep = System.IO.Directory.GetFiles(Directory, "*.json")
                .Where(f => !string.Equals(Path.GetFileName(f), "_cancel.json", StringComparison.OrdinalIgnoreCase)
                            && !IsRestoreAudit(f))
                .OrderByDescending(f => f)
                .Skip(MaxHistory)
                .ToList();
            foreach (var stale in keep) File.Delete(stale);
        }
        catch (Exception ex) { Console.WriteLine("snapshot save failed: " + ex.Message); }
    }

    /// <summary>Newest first, excluding the cancel point.</summary>
    public static IReadOnlyList<string> History()
    {
        if (!System.IO.Directory.Exists(Directory)) return Array.Empty<string>();
        return System.IO.Directory.GetFiles(Directory, "*.json")
            .Where(f => !string.Equals(Path.GetFileName(f), "_cancel.json", StringComparison.OrdinalIgnoreCase)
                        && !IsRestoreAudit(f))
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary><c>*-restore.json</c> 只是恢复动作的审计留痕，不占历史快照的名额、也不进列表。</summary>
    private static bool IsRestoreAudit(string path) =>
        Path.GetFileName(path).EndsWith("-restore.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 可恢复的快照：<c>temp/*-save.json</c>，按快照时间倒序（最新在前）。
    /// 只取 <c>*-save.json</c>：<c>_cancel.json</c> 与 <c>-restore.json</c> 审计文件天然不匹配，
    /// 非递归枚举也排除了 <c>session/</c> 子目录。
    /// </summary>
    public static IReadOnlyList<SnapshotEntry> ListRestorable()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return Array.Empty<SnapshotEntry>();

            var list = new List<SnapshotEntry>();
            foreach (var path in System.IO.Directory.GetFiles(Directory, "*-save.json"))
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)
                    || !name.EndsWith("-save.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                var stem = name[..^"-save.json".Length];

                // 时间优先取文件名前缀（SAVE 按此格式命名）；解析失败再退回文件修改时间
                DateTime local;
                if (stem.Length == StampFormat.Length
                    && DateTime.TryParseExact(stem, StampFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                    local = parsed;
                else
                    local = File.GetLastWriteTime(path);

                list.Add(new SnapshotEntry(path, local, "save"));
            }

            return list.OrderByDescending(e => e.LocalTime)
                .ThenByDescending(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine("snapshot list failed: " + ex.Message);
            return Array.Empty<SnapshotEntry>();
        }
    }

    /// <summary>
    /// 把快照读入 <paramref name="target"/>（反射复制全部可读写属性，与 RestoreCancelPoint 同一写法），
    /// 然后删除 <c>temp/session</c> 并写 <c>temp/{yyyyMMdd-HHmmss}-restore.json</c> 审计。
    /// 快照不可读 / 反序列化失败时返回 false，且不修改 target。
    /// </summary>
    public static bool RestoreFrom(string path, HudConfig target)
    {
        try
        {
            if (target is null || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            var loaded = JsonSerializer.Deserialize<HudConfig>(File.ReadAllText(path));
            if (loaded is null) return false;

            // 整体替换：PerVehicleOverrides 等引用型属性也一起换成快照里的数据
            foreach (var prop in typeof(HudConfig).GetProperties()
                         .Where(pr => pr.CanRead && pr.CanWrite))
                prop.SetValue(target, prop.GetValue(loaded));

            EndSession();   // 恢复后本次会话的中间快照已无意义，直接丢弃

            // 审计文件只留痕，写失败不影响恢复结果
            try
            {
                EnsureDirectory();
                var name = $"{DateTime.Now.ToString(StampFormat)}-restore.json";
                File.WriteAllText(Path.Combine(Directory, name), JsonSerializer.Serialize(target, Options));
            }
            catch (Exception ex) { Console.WriteLine("restore audit failed: " + ex.Message); }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("snapshot restore-from failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>一个可恢复的快照文件（现阶段只有 SAVE 快照）。</summary>
    public sealed record SnapshotEntry(string Path, DateTime LocalTime, string Kind)
    {
        public string TimeText => LocalTime.ToString("MM-dd HH:mm");
        public string Display => TimeText + "  " + Kind;
    }
}
