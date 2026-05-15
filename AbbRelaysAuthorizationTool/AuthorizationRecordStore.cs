using System.IO;
using System.Text.Json;
using AbbRelaysLicensing;

namespace AbbRelaysAuthorizationTool;

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
