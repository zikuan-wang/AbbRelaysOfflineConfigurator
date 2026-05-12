using System.IO;
using System.Text.Json;
using Rex615OfflineConfigurator.Models;

namespace Rex615OfflineConfigurator.Services;

public sealed class CnLegacySelectionRuleLoader
{
    private const string RulesFileName = "CnLegacySelectionRules.json";

    public CnLegacyRuleSet Load()
    {
        var path = ResolveRulesPath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到 615/620 CN 选型规则数据包。", path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<CnLegacyRuleSet>(
                   stream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("615/620 CN 选型规则数据包为空。");
    }

    private static string ResolveRulesPath()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Data", RulesFileName),
            Path.Combine(Environment.CurrentDirectory, "Data", RulesFileName)
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "Rex615OfflineConfigurator", "Data", RulesFileName));
            candidates.Add(Path.Combine(current.FullName, "Data", RulesFileName));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
