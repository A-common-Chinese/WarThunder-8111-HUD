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
/// <para>
/// ★ 外部文件优先，所以程序里改了文案，老用户目录里那份旧文件会一直盖住它（删掉的行也还在）。
/// 因此模板带一个 <c>"_version"</c>：加载时若外部版本更低，就把旧文件备份成
/// <c>help-desc.json.v&lt;旧版本&gt;.bak</c>，再用内置模板重写。用户自己改的内容不会丢（备份里）。
/// 改模板文案后必须把 <c>_version</c> 加一，否则新文案到不了已经生成过文件的机器。
/// </para>
/// </summary>
internal static class HelpDescriptions
{
    private sealed record Entry(
        [property: System.Text.Json.Serialization.JsonPropertyName("zh")] string Zh,
        [property: System.Text.Json.Serialization.JsonPropertyName("en")] string En);

    private static readonly JsonSerializerOptions EntryOptions = new() { PropertyNameCaseInsensitive = true };

    private static Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "help-desc.json");

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        string? templateJson = null;
        int templateVersion = 0;
        Dictionary<string, Entry>? templateMap = null;
        try
        {
            templateJson = ReadTemplate(out templateVersion, out templateMap);
        }
        catch { }

        try
        {
            if (!File.Exists(FilePath))
            {
                // 首次运行：把内置模板写到 exe 同目录，用户可直接改
                if (templateJson != null && templateMap != null)
                {
                    File.WriteAllText(FilePath, templateJson);
                    _map = templateMap;
                    return;
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
            var (extVersion, extMap) = Parse(json);

            // 外部文件比本程序旧 → 备份后重写。否则改过名的行会永远留在老用户那里。
            if (templateJson != null && templateMap != null && extVersion < templateVersion)
            {
                TryReplaceWithTemplate(templateJson, $"v{extVersion}");
                _map = templateMap;
                return;
            }

            _map = extMap;
        }
        catch
        {
            // 外部文件损坏：同样备份后重写，别让 HELP 页变成空白
            if (templateJson != null && templateMap != null)
            {
                TryReplaceWithTemplate(templateJson, "broken");
                _map = templateMap;
                return;
            }
            _map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>把外部文件挪成备份，然后用内置模板覆盖。失败就静默放弃（内存里仍用模板）。</summary>
    private static void TryReplaceWithTemplate(string templateJson, string suffix)
    {
        try
        {
            var bak = FilePath + "." + suffix + ".bak";
            if (!File.Exists(bak)) File.Copy(FilePath, bak, false);
            File.WriteAllText(FilePath, templateJson);
        }
        catch { }
    }

    /// <summary>读内置模板（嵌入资源），同时给出它的版本号和解析结果。</summary>
    private static string? ReadTemplate(out int version, out Dictionary<string, Entry>? map)
    {
        version = 0;
        map = null;

        var asm = typeof(HelpDescriptions).Assembly;
        var res = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("help-desc-default.json", StringComparison.OrdinalIgnoreCase));
        if (res == null) return null;

        using var stream = asm.GetManifestResourceStream(res);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var (v, m) = Parse(json);
        version = v;
        map = m;
        return json;
    }

    /// <summary>
    /// 解析顶层键值对。`"_version"` 是数字（不是 {zh,en}），单独取出来；
    /// 其余每个键都是 { "zh": ..., "en": ... }。缺 _version 的旧文件按 0 处理，会被重写。
    /// </summary>
    private static (int Version, Dictionary<string, Entry> Map) Parse(string json)
    {
        int version = 0;
        var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return (version, map);

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name.Equals("_version", StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int v)) version = v;
                else if (prop.Value.ValueKind == JsonValueKind.String
                         && int.TryParse(prop.Value.GetString(), out int sv)) version = sv;
                continue;
            }

            if (prop.Value.ValueKind != JsonValueKind.Object) continue;

            try
            {
                var e = prop.Value.Deserialize<Entry>(EntryOptions);
                if (e != null) map[prop.Name] = e;
            }
            catch { }
        }

        return (version, map);
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
