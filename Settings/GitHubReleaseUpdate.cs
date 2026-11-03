using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace EndfieldChargePlus.Settings;

internal static class GitHubReleaseUpdate
{
    internal const string Repository = "GlacierGlimmer/zmd-charge-plus";
    internal const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    internal const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";

    internal static async Task<string?> FetchTagAsync(HttpClient client)
    {
        using var response = await client.GetAsync(LatestReleaseUrl);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var release = json.RootElement;
        // Keep this guard even though GitHub's latest endpoint excludes these.
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || release.TryGetProperty("prerelease", out var preview) && preview.GetBoolean()) return null;
        return release.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(tag.GetString()) ? tag.GetString() : null;
    }
}
