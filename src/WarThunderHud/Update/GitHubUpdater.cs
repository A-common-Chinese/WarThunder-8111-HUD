// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Net.Http;
using System.Text.Json;

namespace WtHud.Update;

/// <summary>
/// Checks the project's latest GitHub release. Read-only: it reports the version only, never
/// downloads or executes anything.
/// </summary>
public static class GitHubUpdater
{
    private const string LatestUrl =
        "https://api.github.com/repos/A-common-Chinese/WarThunder-8111-HUD/releases/latest";

    public sealed record Result(bool Checked, string LatestTag, string Message);

    public static async Task<Result> CheckAsync(string currentVersion, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WtHud/" + currentVersion);
            var json = await http.GetStringAsync(LatestUrl, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(tag))
                return new Result(false, "", "no-tag");

            var latest = tag.TrimStart('v', 'V');
            return Compare(latest, currentVersion) > 0
                ? new Result(true, latest, "newer")
                : new Result(true, latest, "current");
        }
        catch (OperationCanceledException)
        {
            return new Result(false, "", "timeout");
        }
        catch (Exception ex)
        {
            return new Result(false, "", ex.GetType().Name);
        }
    }

    /// <summary>Numeric segment comparison so 0.1.10 &gt; 0.1.9.</summary>
    public static int Compare(string a, string b)
    {
        static int[] Parts(string s) => s.Split('.', '-', '+')
            .Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();
        var pa = Parts(a);
        var pb = Parts(b);
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
}
