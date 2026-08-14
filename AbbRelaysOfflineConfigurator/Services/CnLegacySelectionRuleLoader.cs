using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AbbRelaysOfflineConfigurator.Models;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class CnLegacySelectionRuleLoader
{
    private const string RulesFileName = "CnLegacySelectionRules.json";
    private static readonly Lazy<CnLegacyRuleSet> SharedRules = new(LoadCore);

    public CnLegacyRuleSet Load() => SharedRules.Value;

    private static CnLegacyRuleSet LoadCore()
    {
        var path = ResolveRulesPath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到 615/620 CN 选型规则数据包。", path);
        }

        using var stream = File.OpenRead(path);
        var rules = JsonSerializer.Deserialize<CnLegacyRuleSet>(
                        stream,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("615/620 CN 选型规则数据包为空。");

        NormalizeRules(rules);
        return rules;
    }

    private static void NormalizeRules(CnLegacyRuleSet rules)
    {
        foreach (var series in rules.Series)
        {
            foreach (var device in series.Devices)
            {
                NormalizeDevice(series.Id, device);
            }
        }
    }

    private static void NormalizeDevice(string seriesId, CnLegacyDevice device)
    {
        foreach (var group in device.Groups)
        {
            if (group.Position == "17-18")
            {
                TranslateProductVersion(group);
            }

            if (!seriesId.Equals("615_CN_5_0_FP1", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (group.Position == "12")
            {
                Normalize615LanguageGroup(group);
            }
            else if (group.Position is "5-6" or "7-8" or "10" or "14" or "15")
            {
                Clean615DisplayDescriptions(group);
            }
        }
    }

    private static void TranslateProductVersion(CnLegacyCodeGroup group)
    {
        foreach (var option in group.Options)
        {
            option.Description = TranslateProductVersionText(option.Description);
            option.ShortDescription = TranslateProductVersionText(option.ShortDescription);
        }
    }

    private static string TranslateProductVersionText(string value)
    {
        var text = CollapseSpaces(value);
        if (text.StartsWith("Product Version ", StringComparison.OrdinalIgnoreCase))
        {
            return "产品版本 " + text["Product Version ".Length..];
        }

        if (text.StartsWith("版本 ", StringComparison.Ordinal))
        {
            return "产品" + text;
        }

        return text;
    }

    private static void Normalize615LanguageGroup(CnLegacyCodeGroup group)
    {
        group.Options.RemoveAll(option => option.Code.Equals("2", StringComparison.OrdinalIgnoreCase));

        var zOption = group.Options.FirstOrDefault(option => option.Code.Equals("Z", StringComparison.OrdinalIgnoreCase));
        if (zOption is null)
        {
            return;
        }

        foreach (var option in group.Options)
        {
            option.IsDefault = option.Code.Equals("Z", StringComparison.OrdinalIgnoreCase);
        }

        zOption.Description = "中文";
        zOption.ShortDescription = "中文";
    }

    private static void Clean615DisplayDescriptions(CnLegacyCodeGroup group)
    {
        foreach (var option in group.Options)
        {
            option.Description = Clean615DisplayDescription(option.Description);
            option.ShortDescription = Clean615DisplayDescription(option.ShortDescription);
        }
    }

    private static string Clean615DisplayDescription(string value)
    {
        var text = CollapseSpaces(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var previous = "";
        while (!text.Equals(previous, StringComparison.Ordinal))
        {
            previous = text;
            text = Regex.Replace(
                text,
                @"\s*[（(](?=[^（）()]*?(?:[,，、]|if\b|且|当|位为))[^（）()]*?(?:\b[A-Z]\b|位为\s*[A-Z])[^（）()]*[）)]",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            text = Regex.Replace(text, @"\s*,\s*,", ",", RegexOptions.CultureInvariant);
            text = Regex.Replace(text, @"\s*,\s*$", "", RegexOptions.CultureInvariant);
            text = Regex.Replace(
                text,
                @"\s+[A-Z](?:\s*[,，]\s*[A-Z])*(?:\s+if\s+(?:AIM|BIO)\s+[A-Z0-9/\s]+(?:\s+or\s+[A-Z0-9/\s]+)?)?\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return CollapseSpaces(text);
    }

    private static string CollapseSpaces(string value) =>
        Regex.Replace(value ?? "", @"\s+", " ", RegexOptions.CultureInvariant).Trim();

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
            candidates.Add(Path.Combine(current.FullName, "AbbRelaysOfflineConfigurator", "Data", RulesFileName));
            candidates.Add(Path.Combine(current.FullName, "Data", RulesFileName));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
