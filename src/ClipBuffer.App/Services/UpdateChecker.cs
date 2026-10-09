using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ClipBuffer.App.Services;

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? Message);

public static class UpdateChecker
{
    public const string GitHubOwner = "ilian21012005-bit";
    public const string GitHubRepo = "clip-buffer";

    private static readonly HttpClient Http = CreateClient();

    public static string CurrentVersion
    {
        get
        {
            var info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                return info.Split('+')[0].TrimStart('v', 'V');
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    public static async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion;
        try
        {
            var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(false, current, null, null, "Aucune release GitHub publiée pour le moment.");
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            var latest = NormalizeVersion(tag);
            if (string.IsNullOrWhiteSpace(latest))
            {
                return new UpdateCheckResult(false, current, null, htmlUrl, "Version distante illisible.");
            }

            var newer = IsNewer(latest, current);
            return newer
                ? new UpdateCheckResult(true, current, latest, htmlUrl, $"Nouvelle version {latest} disponible (actuelle {current}).")
                : new UpdateCheckResult(false, current, latest, htmlUrl, $"Karu est à jour ({current}).");
        }
        catch (Exception ex)
        {
            AppLog.Write("update check: " + ex.Message);
            return new UpdateCheckResult(false, current, null, null, "Impossible de vérifier les mises à jour (réseau).");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Karu", CurrentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string? NormalizeVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V'))
        {
            t = t[1..];
        }

        return Version.TryParse(PadVersion(t), out _) ? t : null;
    }

    private static bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(PadVersion(latest), out var l) ||
            !Version.TryParse(PadVersion(current), out var c))
        {
            return false;
        }

        return l > c;
    }

    private static string PadVersion(string v)
    {
        var parts = v.Split('.');
        while (parts.Length < 3)
        {
            v += ".0";
            parts = v.Split('.');
        }

        return string.Join('.', parts.Take(4));
    }
}
