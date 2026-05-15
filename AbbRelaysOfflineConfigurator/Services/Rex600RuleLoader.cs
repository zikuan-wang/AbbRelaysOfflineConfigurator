using System.IO;
using System.Xml.Linq;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Rex600RuleLoader
{
    private const string RulesFileName = "REX600_1.0.xml";

    public Rex600RuleSet Load()
    {
        var path = ResolveRulesPath();
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException("REX600 rule file is empty.");
        var catalog = Rex600DescriptionCatalog.Create();

        var groups = root.Element("OrderCodes")?
            .Elements("Digit")
            .Select(digit =>
            {
                var groupName = (string?)digit.Attribute("Group") ?? "";
                var location = (string?)digit.Attribute("Location") ?? "";
                return new Rex600GroupRule(
                    Location: location,
                    SortOrder: SortOrder(location),
                    Name: groupName,
                    DisplayName: catalog.GroupDisplayName(groupName),
                    DisplayNameEnglish: catalog.GroupDisplayNameEnglish(groupName),
                    Options: digit.Elements("Option")
                        .Select(option =>
                        {
                            var id = (string?)option.Attribute("Id") ?? "";
                            var token = (string?)option.Attribute("Description") ?? "";
                            return new Rex600OptionRule(
                                Id: id,
                                Version: (string?)option.Attribute("Version") ?? "*",
                                OptionCode: (string?)option.Attribute("OptionCode") ?? "",
                                DescriptionKey: token,
                                Description: catalog.Description(groupName, id, token),
                                DescriptionEnglish: catalog.DescriptionEnglish(groupName, id, token));
                        })
                        .Where(option => !string.IsNullOrWhiteSpace(option.Id))
                        .ToList());
            })
            .Where(group => !string.IsNullOrWhiteSpace(group.Name))
            .OrderBy(group => group.SortOrder)
            .ToList()
            ?? [];

        return new Rex600RuleSet(
            SourcePath: path,
            DefaultOrderCode: (string?)root.Element("Default")?.Attribute("OrderCode") ?? "",
            Versions: root.Element("OrderCodeVersions")?.Elements("Version")
                .Select(version => new Rex600Version(
                    Id: (string?)version.Attribute("Id") ?? "",
                    IedVersion: (string?)version.Attribute("IED_version") ?? "",
                    ConpackVersion: (string?)version.Attribute("Conpack_version") ?? ""))
                .Where(version => !string.IsNullOrWhiteSpace(version.Id))
                .ToList() ?? [],
            Groups: groups);
    }

    private static int SortOrder(string location)
    {
        var first = location.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? location;
        return int.TryParse(first, out var parsed) ? parsed : 0;
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
            candidates.Add(Path.Combine(current.FullName, "AbbRelaysOfflineConfigurator", "Data", RulesFileName));
            candidates.Add(Path.Combine(current.FullName, "Data", RulesFileName));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}

public sealed record Rex600RuleSet(
    string SourcePath,
    string DefaultOrderCode,
    IReadOnlyList<Rex600Version> Versions,
    IReadOnlyList<Rex600GroupRule> Groups)
{
    public Rex600Version? Version(string id) =>
        Versions.FirstOrDefault(version => version.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

public sealed record Rex600Version(string Id, string IedVersion, string ConpackVersion);

public sealed record Rex600GroupRule(
    string Location,
    int SortOrder,
    string Name,
    string DisplayName,
    string DisplayNameEnglish,
    IReadOnlyList<Rex600OptionRule> Options);

public sealed record Rex600OptionRule(
    string Id,
    string Version,
    string OptionCode,
    string DescriptionKey,
    string Description,
    string DescriptionEnglish)
{
    public bool SupportsVersion(string version)
    {
        if (Version == "*" || string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        return Version
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(version, StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class Rex600DescriptionCatalog
{
    private readonly Dictionary<string, string> _groupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mountings"] = "REX600 产品",
        ["Standards"] = "标准",
        ["MainApps"] = "主应用包",
        ["FunctionalApps"] = "功能应用",
        ["Aios"] = "模拟量输入",
        ["Bios"] = "开关量 I/O",
        ["CommSerials"] = "预留",
        ["CommEthernets"] = "预留",
        ["CommProtocols"] = "预留",
        ["Languages"] = "通信接口",
        ["FrontPanels"] = "通信协议",
        ["Options_1"] = "语言",
        ["Options_2"] = "用户界面",
        ["PowerSupplies"] = "预留",
        ["Reserved"] = "预留",
        ["Reserved2"] = "电源",
        ["Versions"] = "产品版本"
    };

    private readonly Dictionary<string, string> _groupNamesEnglish = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mountings"] = "REX600 product",
        ["Standards"] = "Standard",
        ["MainApps"] = "Main application package",
        ["FunctionalApps"] = "Functional application",
        ["Aios"] = "Analog inputs",
        ["Bios"] = "Binary inputs/outputs",
        ["CommSerials"] = "Reserved",
        ["CommEthernets"] = "Reserved",
        ["CommProtocols"] = "Reserved",
        ["Languages"] = "Communication interface",
        ["FrontPanels"] = "Communication protocol",
        ["Options_1"] = "Language",
        ["Options_2"] = "User interface",
        ["PowerSupplies"] = "Spare",
        ["Reserved"] = "Spare",
        ["Reserved2"] = "Power supply",
        ["Versions"] = "Product version"
    };

    private readonly Dictionary<string, string> _descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mountings:M"] = "REX600 合并单元",
        ["Standards:G"] = "全球标准",
        ["MainApps:A"] = "合并单元主应用包，包含控制、测量、监视和逻辑功能",
        ["FunctionalApps:A"] = "基础后备保护",
        ["FunctionalApps:B"] = "基础后备保护 + 多频导纳保护 + 基于电流的故障通道指示",
        ["FunctionalApps:N"] = "无功能应用",
        ["Aios:A"] = "3 路组合传感器 + I0（Rogowski/LPCT + 电容/电阻分压器）",
        ["Bios:A"] = "6 路开关量输入 + 3 路开关量输出",
        ["CommSerials:N"] = "预留位，无选项",
        ["CommEthernets:N"] = "预留位，无选项",
        ["CommProtocols:N"] = "预留位，无选项",
        ["Languages:A"] = "3 个 RJ-45 LAN 以太网端口",
        ["FrontPanels:A"] = "IEC 61850 通信协议",
        ["Options_1:1"] = "英文",
        ["Options_2:A"] = "默认 WebHMI",
        ["PowerSupplies:N"] = "预留位，无选项",
        ["Reserved:N"] = "预留位，无选项",
        ["Reserved2:A"] = "24 VDC 电源",
        ["Versions:1G"] = "产品版本 1.0"
    };

    private readonly Dictionary<string, string> _descriptionsEnglish = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mountings:M"] = "REX600 merging unit",
        ["Standards:G"] = "Global",
        ["MainApps:A"] = "Merging unit with control, measurement, monitoring and logic functions",
        ["FunctionalApps:A"] = "Basic backup protection",
        ["FunctionalApps:B"] = "Basic backup protection, multi-frequency admittance protection and current based fault passage indication",
        ["FunctionalApps:N"] = "None",
        ["Aios:A"] = "3 combi sensors + I0 (Rogowski/LPCT + capacitive/resistive divider)",
        ["Bios:A"] = "6 binary inputs + 3 binary outputs",
        ["CommSerials:N"] = "Reserved, not used",
        ["CommEthernets:N"] = "Reserved, not used",
        ["CommProtocols:N"] = "Reserved, not used",
        ["Languages:A"] = "3 x RJ-45 LAN ports",
        ["FrontPanels:A"] = "IEC 61850 communication protocol",
        ["Options_1:1"] = "English",
        ["Options_2:A"] = "Default WebHMI",
        ["PowerSupplies:N"] = "Reserved, not used",
        ["Reserved:N"] = "Reserved, not used",
        ["Reserved2:A"] = "24 VDC power supply",
        ["Versions:1G"] = "Product version 1.0"
    };

    public static Rex600DescriptionCatalog Create() => new();

    public string GroupDisplayName(string groupName) =>
        _groupNames.TryGetValue(groupName, out var value) ? value : groupName;

    public string GroupDisplayNameEnglish(string groupName) =>
        _groupNamesEnglish.TryGetValue(groupName, out var value) ? value : groupName;

    public string Description(string groupName, string id, string token) =>
        _descriptions.TryGetValue($"{groupName}:{id}", out var value)
            ? value
            : HumanizeToken(token);

    public string DescriptionEnglish(string groupName, string id, string token) =>
        _descriptionsEnglish.TryGetValue($"{groupName}:{id}", out var value)
            ? value
            : HumanizeToken(token);

    private static string HumanizeToken(string token) =>
        string.IsNullOrWhiteSpace(token)
            ? ""
            : token.Replace('_', ' ');
}
