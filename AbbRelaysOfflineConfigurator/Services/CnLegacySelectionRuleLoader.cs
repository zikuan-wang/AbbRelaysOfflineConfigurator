using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AbbRelaysOfflineConfigurator.Models;

namespace AbbRelaysOfflineConfigurator.Services;

// 615/620 CN 选型规则包的加载和兼容归一化入口。JSON 保留从历史资料提取的业务结构，
// 本类只修正已知版本的展示/默认项差异，最终可用性与组合校验由 ViewModel 分层执行。
public sealed class CnLegacySelectionRuleLoader
{
    private const string RulesFileName = "CnLegacySelectionRules.json";
    // 规则包在进程内只反序列化并归一化一次；返回对象被所有 CN 选型页面共享，使用方应按只读数据处理。
    private static readonly Lazy<CnLegacyRuleSet> SharedRules = new(LoadCore);

    public CnLegacyRuleSet Load() => SharedRules.Value;

    private static CnLegacyRuleSet LoadCore()
    {
        // 发布包与源码调试的路径解析统一在这里完成，避免各 ViewModel 对数据位置做不同假设。
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

        // 反序列化后集中归一化，再向上层暴露规则，保证所有调用者看到同一套显示文本和默认项。
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

            // 以下修正仅适用于 615 CN 5.0 FP1 的已知源资料差异，不能扩散到其他系列或版本。
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
        // 源资料中的代码 2 不属于当前可交付语言选项；Z 才是本版本的中文默认值。
        // 在加载阶段统一处理可防止界面默认项、导入匹配和订货号拼接出现不同结果。
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
        // 这里只清理说明文本中夹带的位号兼容条件；真正的约束已由结构化规则字段表达，
        // 不能从清理后的短描述反向推导选型合法性。
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
        // 优先使用程序或当前工作目录的 Data，向父目录搜索用于源码树内调试；
        // 若都不存在则返回标准发布路径，由 LoadCore 抛出带路径的明确错误。
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
