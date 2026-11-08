// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Text.Json;

namespace WtHud.UI.Mfd;

/// <summary>
/// Loads help descriptions from an external <c>help-desc.json</c> next to the executable.
/// Every key holds both languages, e.g.
/// <code>
/// "page.display": { "zh": "...", "en": "..." }
/// </code>
/// so the text follows the screen language without rebuilding.
/// </summary>
internal static class HelpDescriptions
{
    private sealed record Entry(
        [property: System.Text.Json.Serialization.JsonPropertyName("zh")] string Zh,
        [property: System.Text.Json.Serialization.JsonPropertyName("en")] string En);

    private static Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "help-desc.json");

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath))
            {
                // 首次运行：把内置的完整模板（71 条中英文）写到 exe 同目录，用户可直接改
                var asm = typeof(HelpDescriptions).Assembly;
                var res = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("help-desc-default.json", StringComparison.OrdinalIgnoreCase));

                if (res != null)
                {
                    using var stream = asm.GetManifestResourceStream(res);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var defJson = reader.ReadToEnd();
                        File.WriteAllText(FilePath, defJson);
                        var defData = JsonSerializer.Deserialize<Dictionary<string, Entry>>(
                            defJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (defData != null)
                            _map = new Dictionary<string, Entry>(defData, StringComparer.OrdinalIgnoreCase);
                        return;
                    }
                }

                // 兜底：极简模板
                var template = new Dictionary<string, Entry>
                {
                    ["_default"] = new("暂无说明。可在 help-desc.json 中补充。", "No description yet."),
                    ["L0"] = new("主菜单：用两侧按键选择模块。", "Main menu: pick a module with the side keys."),
                };
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(template,
                        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                _map = template;
                return;
            }

            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, Entry>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data != null) _map = new Dictionary<string, Entry>(data, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Description of one help key in the requested language.</summary>
    public static string Get(string key, bool zh)
    {
        EnsureLoaded();
        if (_map.TryGetValue(key, out var e))
        {
            var text = zh ? e.Zh : e.En;
            if (!string.IsNullOrWhiteSpace(text)) return text;
            text = zh ? e.En : e.Zh;                 // 缺翻译时退回另一种语言
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return _map.TryGetValue("_default", out var d) ? (zh ? d.Zh : d.En) : "";
    }
}
