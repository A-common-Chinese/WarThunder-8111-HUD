// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Text.Json;

namespace WtHud.Hud;

/// <summary>
/// Snapshots of the whole config, written next to the executable:
/// <list type="bullet">
/// <item><c>temp/_cancel.json</c> — state when F1 was opened; CANCEL restores it. Always present, overwritten on every open.</item>
/// <item><c>temp/yyyyMMdd-HHmmss-label.json</c> — one file per edit, newest 10 kept (undo/rollback groundwork).</item>
/// </list>
/// </summary>
public static class ConfigSnapshots
{
    private const int MaxHistory = 10;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "temp");

    private static string CancelPath => Path.Combine(Directory, "_cancel.json");

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
                .Where(f => !string.Equals(Path.GetFileName(f), "_cancel.json", StringComparison.OrdinalIgnoreCase))
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
            .Where(f => !string.Equals(Path.GetFileName(f), "_cancel.json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f)
            .ToList();
    }
}
