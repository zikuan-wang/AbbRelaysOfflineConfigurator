using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex615OfflineConfigurator.Services;

public sealed class UpdateCheckService
{
    public const string ReleaseRepositoryUrl = "https://github.com/zikuan-wang/Rex615OfflineConfigurator_Release";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/zikuan-wang/Rex615OfflineConfigurator_Release/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseApiUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return UpdateCheckResult.Failed($"GitHub Release 查询失败：HTTP {(int)response.StatusCode}。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            return UpdateCheckResult.Failed("GitHub Release 返回内容无效。");
        }

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var latestVersion = ParseReleaseVersion(release.TagName);
        var downloadAsset = release.Assets?
            .Where(asset => !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            .OrderByDescending(asset => asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return new UpdateCheckResult(
            true,
            latestVersion > currentVersion,
            currentVersion.ToString(3),
            latestVersion.ToString(3),
            release.Name,
            release.HtmlUrl,
            downloadAsset?.BrowserDownloadUrl,
            downloadAsset?.Name,
            null);
    }

    public static void OpenReleasePage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            url = ReleaseRepositoryUrl + "/releases";
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static Version ParseReleaseVersion(string tagName)
    {
        var normalized = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) ? version : new Version(0, 0, 0);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Rex615OfflineConfigurator/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

public sealed record UpdateCheckResult(
    bool IsSuccess,
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseName,
    string ReleaseUrl,
    string? DownloadUrl,
    string? DownloadAssetName,
    string? ErrorMessage)
{
    public static UpdateCheckResult Failed(string message) =>
        new(false, false, "", "", "", "", null, null, message);
}

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto>? Assets { get; set; }
}

internal sealed class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
