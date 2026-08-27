using System.IO;
using System.Text.Json;
using AbbRelaysLicensing;

namespace AbbRelaysAuthorizationTool;

// 授权工具侧的本地签发台账，仅用于设备追踪、重复签发计数和界面展示；
// 它不是客户端授权判定的依据，真正的可信凭据仍是由私钥签名后交付的 .zwlic 文件。
internal static class AuthorizationRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string RecordsPath =>
        Path.Combine(AppContext.BaseDirectory, "authorized-devices.json");

    public static IReadOnlyList<AuthorizationDeviceRecord> Load()
    {
        if (!File.Exists(RecordsPath))
        {
            return [];
        }

        try
        {
            // 台账损坏不应阻止授权工具启动或签发，但会表现为无历史记录；
            // 调用方不能据此推断某台设备从未签发过授权。
            return JsonSerializer.Deserialize<List<AuthorizationDeviceRecord>>(
                    File.ReadAllText(RecordsPath),
                    JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void SaveIssuedActivation(
        LicenseRequest request,
        string licensedTo,
        DateTimeOffset issuedAt,
        DateTimeOffset? expiresAt,
        string activationFilePath)
    {
        // 同一 MachineId 视为同一设备：保留一条最新记录并累加签发次数，
        // 避免续期或补发在列表中产生多条难以辨认的重复设备。
        var records = Load().ToList();
        var existingIndex = records.FindIndex(record =>
            record.MachineId.Equals(request.MachineId, StringComparison.OrdinalIgnoreCase));
        var previousCount = existingIndex >= 0 ? records[existingIndex].IssueCount : 0;

        var record = new AuthorizationDeviceRecord(
            request.MachineName,
            request.MachineId,
            request.UserName,
            string.IsNullOrWhiteSpace(licensedTo) ? request.UserName : licensedTo.Trim(),
            request.RequestId,
            request.CreatedAt,
            issuedAt,
            expiresAt,
            activationFilePath,
            previousCount + 1);

        if (existingIndex >= 0)
        {
            records[existingIndex] = record;
        }
        else
        {
            records.Add(record);
        }

        records = records
            .OrderByDescending(item => item.IssuedAt)
            .ThenBy(item => item.MachineName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 只有激活文件已经成功生成后才调用本方法；台账写入失败应向授权人员暴露，
        // 不能静默声称签发记录已保存。
        var directory = Path.GetDirectoryName(RecordsPath)
            ?? throw new InvalidOperationException("无法定位授权记录目录。");
        Directory.CreateDirectory(directory);
        File.WriteAllText(RecordsPath, JsonSerializer.Serialize(records, JsonOptions));
    }
}

internal sealed record AuthorizationDeviceRecord(
    string MachineName,
    string MachineId,
    string UserName,
    string LicensedTo,
    string RequestId,
    DateTimeOffset RequestCreatedAt,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    string ActivationFilePath,
    int IssueCount)
{
    public string ExpireText => ExpiresAt is { } value ? value.ToString("yyyy-MM-dd") : "永久";

    public string IssuedAtText => IssuedAt.ToString("yyyy-MM-dd HH:mm");

    public string MachineIdShort => MachineId.Length <= 16 ? MachineId : $"{MachineId[..8]}...{MachineId[^8..]}";
}
