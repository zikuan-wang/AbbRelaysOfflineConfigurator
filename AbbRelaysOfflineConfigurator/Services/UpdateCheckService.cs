using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class UpdateCheckService
{
    public const string ReleaseRepositoryUrl = "https://github.com/zikuan-wang/AbbRelaysOfflineConfigurator_Release";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/zikuan-wang/AbbRelaysOfflineConfigurator_Release/releases/latest";
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
            .Where(asset =>
                !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
                asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return new UpdateCheckResult(
            true,
            latestVersion > currentVersion,
            currentVersion.ToString(3),
            latestVersion.ToString(3),
            release.Name,
            release.HtmlUrl,
            NormalizeReleaseNotes(release.Body),
            downloadAsset?.BrowserDownloadUrl,
            downloadAsset?.Name,
            downloadAsset?.Size,
            downloadAsset?.Digest,
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

    public async Task<string> DownloadInstallerAsync(
        string downloadUrl,
        string? assetName,
        IProgress<UpdateDownloadProgress>? progress = null,
        long? expectedSizeBytes = null,
        string? expectedDigest = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("没有可下载的安装包地址。");
        }

        var fileName = SafeInstallerFileName(assetName, downloadUrl);
        if (!fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("在线安装只支持 GitHub Release 中的 MSI 安装包。");
        }

        var updateDirectory = Path.Combine(Path.GetTempPath(), "AbbRelaysOfflineConfigurator", "Updates");
        Directory.CreateDirectory(updateDirectory);
        var targetPath = Path.Combine(updateDirectory, fileName);
        var tempPath = targetPath + ".download";

        TryDeleteFile(tempPath);
        TryDeleteFile(targetPath);

        try
        {
            using var response = await Client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContentLength = response.Content.Headers.ContentLength;
            var totalBytes = expectedSizeBytes is > 0 ? expectedSizeBytes : responseContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = File.Create(tempPath))
            {
                var buffer = new byte[1024 * 128];
                long receivedBytes = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    receivedBytes += read;
                    progress?.Report(new UpdateDownloadProgress(receivedBytes, totalBytes));
                }

                await target.FlushAsync(cancellationToken);

                if (responseContentLength is > 0 && receivedBytes != responseContentLength.Value)
                {
                    throw new InvalidOperationException(
                        $"安装包下载不完整：已下载 {receivedBytes} 字节，服务器声明 {responseContentLength.Value} 字节。");
                }

                if (expectedSizeBytes is > 0 && receivedBytes != expectedSizeBytes.Value)
                {
                    throw new InvalidOperationException(
                        $"安装包下载不完整：已下载 {receivedBytes} 字节，发布文件应为 {expectedSizeBytes.Value} 字节。");
                }
            }

            var expectedSha256 = NormalizeSha256Digest(expectedDigest);
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actualSha256 = await ComputeSha256Async(tempPath, cancellationToken);
                if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("安装包 SHA256 校验失败，已删除损坏的下载文件。");
                }
            }

            File.Move(tempPath, targetPath, true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            TryDeleteFile(targetPath);
            throw;
        }

        return targetPath;
    }

    public static void StartInstaller(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            throw new FileNotFoundException("安装包不存在。", installerPath);
        }

        Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{installerPath}\"")
        {
            UseShellExecute = true
        });
    }

    private static Version ParseReleaseVersion(string tagName)
    {
        var normalized = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) ? version : new Version(0, 0, 0);
    }

    private static string SafeInstallerFileName(string? assetName, string downloadUrl)
    {
        var fileName = string.IsNullOrWhiteSpace(assetName)
            ? Path.GetFileName(new Uri(downloadUrl).LocalPath)
            : assetName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "ABBRelaysOfflineConfigurator_Update.msi";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string? NormalizeSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var normalized = digest.Trim();
        const string prefix = "sha256:";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        normalized = normalized[prefix.Length..].Trim();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized.ToUpperInvariant()
            : null;
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // The following write or move reports the real failure if the file is locked.
        }
    }

    private static string NormalizeReleaseNotes(string? releaseBody)
    {
        if (string.IsNullOrWhiteSpace(releaseBody))
        {
            return "";
        }

        var notes = releaseBody.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        const int maxLength = 2000;
        return notes.Length <= maxLength
            ? notes
            : notes[..maxLength].TrimEnd() + "...";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AbbRelaysOfflineConfigurator/1.0");
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
    string ReleaseNotes,
    string? DownloadUrl,
    string? DownloadAssetName,
    long? DownloadAssetSizeBytes,
    string? DownloadAssetDigest,
    string? ErrorMessage)
{
    public static UpdateCheckResult Failed(string message) =>
        new(false, false, "", "", "", "", "", null, null, null, null, message);
}

public sealed record UpdateDownloadProgress(long ReceivedBytes, long? TotalBytes)
{
    public int? Percent => TotalBytes is > 0
        ? (int)Math.Min(100, ReceivedBytes * 100 / TotalBytes.Value)
        : null;
}

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto>? Assets { get; set; }
}

internal sealed class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}
