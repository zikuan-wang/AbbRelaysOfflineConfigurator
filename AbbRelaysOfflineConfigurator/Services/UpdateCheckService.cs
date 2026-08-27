using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbbRelaysOfflineConfigurator.Services;

// 客户端更新流程的服务边界：读取独立 Release 仓库的 Latest 元数据、流式下载 MSI，
// 校验服务端长度及可用的 SHA256 摘要后再交给系统安装器。界面只消费结构化状态和进度。
public sealed class UpdateCheckService
{
    public const string ReleaseRepositoryUrl = "https://github.com/zikuan-wang/AbbRelaysOfflineConfigurator_Release";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/zikuan-wang/AbbRelaysOfflineConfigurator_Release/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        // 更新版本与源码仓库解耦，以发布仓库 Latest Release 为唯一线上入口；
        // 这里只选择 MSI 资产，不把源码压缩包或其他附件暴露为可安装更新。
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

        // 使用完整程序集 Version 与去掉 v 前缀的 Release 标签比较，结果展示时再缩减为三段；
        // 无法解析的标签降为 0.0.0，因而不会把异常标签误报为新版本。
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
        // 该路径只打开浏览器供用户查看发布页，不会下载或执行其中的任何内容。
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
        // downloadUrl、文件名、大小和摘要应来自同一次 Release 查询结果；
        // 方法会验证本地落盘结果，但不会自行重新判断资产是否属于期望的 GitHub Release。
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
        // 下载阶段使用非 MSI 临时扩展名；长度及发布信息提供的 SHA256（如有）校验通过后，才移动到最终安装路径。
        var tempPath = targetPath + ".download";

        TryDeleteFile(tempPath);
        TryDeleteFile(targetPath);

        try
        {
            // ResponseHeadersRead 配合固定缓冲区边读边写，避免大型 MSI 整体驻留内存；
            // 进度总量优先采用 Release 元数据，缺失时再使用响应 Content-Length。
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

            // GitHub 若提供规范 sha256: 摘要则执行内容校验；没有摘要时只执行响应头或
            // Release 元数据中实际可用的长度校验。
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
            // 失败或取消时同时清理临时文件和可能存在的旧目标文件，避免后续流程误执行不完整安装包。
            TryDeleteFile(tempPath);
            TryDeleteFile(targetPath);
            throw;
        }

        return targetPath;
    }

    public static void StartInstaller(string installerPath)
    {
        // 下载与校验已在前一步完成；此处只把明确存在的 MSI 交给 Windows Installer，
        // 安装权限提示和发布者证书提示仍由操作系统负责。
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
        // 只保留单个安全文件名，替换 Windows 不允许的字符，防止 Release 资产名改变更新目录之外的路径。
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
            // 清理是尽力而为；若文件被占用，紧随其后的写入或移动会报告更贴近真实操作的错误。
        }
    }

    private static string NormalizeReleaseNotes(string? releaseBody)
    {
        if (string.IsNullOrWhiteSpace(releaseBody))
        {
            return "";
        }

        // 统一换行并限制展示长度，防止异常超长的远端说明拖慢或撑坏更新对话框。
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
