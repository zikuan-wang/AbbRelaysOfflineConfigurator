using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Ssc600RuleLoader
{
    private const string RulesFileName = "SSC600_1.5.xml";

    public Ssc600RuleSet Load()
    {
        var path = ResolveRulesPath();
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException("SSC600 rule file is empty.");
        var descriptionCatalog = Ssc600DescriptionCatalog.Create();

        var groups = root.Element("OrderCodes")?
            .Elements("Digit")
            .Select(digit =>
            {
                var groupName = (string?)digit.Attribute("Group") ?? "";
                var location = (string?)digit.Attribute("Location") ?? "";
                return new Ssc600GroupRule(
                    Location: location,
                    SortOrder: SortOrder(location),
                    Name: groupName,
                    DisplayName: descriptionCatalog.GroupDisplayName(groupName),
                    DisplayNameEnglish: descriptionCatalog.GroupDisplayNameEnglish(groupName),
                    Options: digit.Elements("Option")
                        .Select(option =>
                        {
                            var id = (string?)option.Attribute("Id") ?? "";
                            var token = (string?)option.Attribute("Description") ?? "";
                            return new Ssc600OptionRule(
                                Id: id,
                                Version: (string?)option.Attribute("Version") ?? "*",
                                OptionCode: (string?)option.Attribute("OptionCode") ?? "",
                                DescriptionKey: token,
                                Description: descriptionCatalog.Description(groupName, id, token),
                                DescriptionEnglish: descriptionCatalog.DescriptionEnglish(groupName, id, token));
                        })
                        .Where(option => !string.IsNullOrWhiteSpace(option.Id))
                        .ToList());
            })
            .Where(group => !string.IsNullOrWhiteSpace(group.Name))
            .OrderBy(group => group.SortOrder)
            .ToList()
            ?? [];

        var validationBlocks = root.Element("ValidOrderCodes")?
            .Elements()
            .Select(block => new Ssc600ValidationBlock(
                Name: block.Name.LocalName,
                DisplayName: descriptionCatalog.ValidationDisplayName(block.Name.LocalName),
                Values: block.Elements("Rule")
                    .Select(rule => new Ssc600ValidationExpression(
                        Pattern: (rule.Value ?? "").Trim(),
                        Version: (string?)rule.Attribute("Version") ?? "*",
                        IsRegex: false))
                    .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
                    .Concat(block.Elements("RulePattern")
                        .Select(rule => new Ssc600ValidationExpression(
                            Pattern: (rule.Value ?? "").Trim(),
                            Version: (string?)rule.Attribute("Version") ?? "*",
                            IsRegex: true))
                        .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern)))
                    .ToList()))
            .ToList()
            ?? [];

        return new Ssc600RuleSet(
            SourcePath: path,
            DefaultOrderCode: (string?)root.Element("Default")?.Attribute("OrderCode") ?? "",
            Versions: root.Element("OrderCodeVersions")?.Elements("Version")
                .Select(version => new Ssc600Version(
                    Id: (string?)version.Attribute("Id") ?? "",
                    IedVersion: (string?)version.Attribute("IED_version") ?? "",
                    ConpackVersion: (string?)version.Attribute("Conpack_version") ?? ""))
                .Where(version => !string.IsNullOrWhiteSpace(version.Id))
                .ToList() ?? [],
            Groups: groups,
            ValidationBlocks: validationBlocks);
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

public sealed record Ssc600RuleSet(
    string SourcePath,
    string DefaultOrderCode,
    IReadOnlyList<Ssc600Version> Versions,
    IReadOnlyList<Ssc600GroupRule> Groups,
    IReadOnlyList<Ssc600ValidationBlock> ValidationBlocks)
{
    public Ssc600GroupRule? Group(string name) =>
        Groups.FirstOrDefault(group => group.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public Ssc600Version? Version(string id) =>
        Versions.FirstOrDefault(version => version.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public bool MatchesValidationBlock(string blockName, string value, string version) =>
        ValidationBlocks
            .Where(block => block.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(block => block.Values)
            .Any(expression => expression.SupportsVersion(version) && expression.Matches(value));
}

public sealed record Ssc600Version(string Id, string IedVersion, string ConpackVersion);

public sealed record Ssc600GroupRule(
    string Location,
    int SortOrder,
    string Name,
    string DisplayName,
    string DisplayNameEnglish,
    IReadOnlyList<Ssc600OptionRule> Options);

public sealed record Ssc600OptionRule(
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

public sealed record Ssc600ValidationBlock(
    string Name,
    string DisplayName,
    IReadOnlyList<Ssc600ValidationExpression> Values);

public sealed record Ssc600ValidationExpression(string Pattern, string Version, bool IsRegex)
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

    public bool Matches(string value) =>
        IsRegex
            ? Regex.IsMatch(value, Pattern, RegexOptions.IgnoreCase)
            : Pattern.Equals(value, StringComparison.OrdinalIgnoreCase);
}

internal sealed class Ssc600DescriptionCatalog
{
    private readonly Dictionary<string, string> _groupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mountings"] = "产品类型",
        ["Standards"] = "标准",
        ["MainApps"] = "主应用包",
        ["FunctionalApps"] = "线路/电缆应用包",
        ["Aios"] = "高级线路/电缆应用包",
        ["Bios"] = "附加应用包",
        ["CommSerials"] = "变压器应用包",
        ["CommEthernets"] = "电动机应用包",
        ["CommProtocols"] = "过程总线连接",
        ["Languages"] = "通信接口",
        ["FrontPanels"] = "通信协议",
        ["Options_1"] = "语言",
        ["Options_2"] = "用户界面",
        ["PowerSupplies"] = "特殊单间隔应用包",
        ["Reserved"] = "特殊多间隔应用包",
        ["Reserved2"] = "电源",
        ["Versions"] = "产品版本"
    };

    private readonly Dictionary<string, string> _groupNamesEnglish = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mountings"] = "Basic product",
        ["Standards"] = "Standard",
        ["MainApps"] = "Main AppPack",
        ["FunctionalApps"] = "Cable/Line AppPack",
        ["Aios"] = "Advanced Cable/Line AppPack",
        ["Bios"] = "Spare / Additional Application Package",
        ["CommSerials"] = "Transformer AppPack",
        ["CommEthernets"] = "Motor AppPack",
        ["CommProtocols"] = "IEC 61850-9-2LE Process Bus Connectivity",
        ["Languages"] = "Communication",
        ["FrontPanels"] = "Communication Protocol",
        ["Options_1"] = "Language",
        ["Options_2"] = "User Interface",
        ["PowerSupplies"] = "Special bay-level AppPack",
        ["Reserved"] = "Special multi-bay AppPack",
        ["Reserved2"] = "Power Supply",
        ["Versions"] = "Product Version"
    };

    private readonly Dictionary<string, string> _validationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FunctionalApplication"] = "产品类型、通信接口和电源组合",
        ["Software"] = "应用包和过程总线组合",
        ["Communication"] = "通信组合",
        ["HMI"] = "HMI 组合"
    };

    private readonly Dictionary<string, string> _descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mountings:S"] = "SSC600 硬件本体",
        ["Mountings:V"] = "SSC600 SW 软件发行包",
        ["Standards:B"] = "全球通用",
        ["MainApps:A"] = "基础保护、控制、测量、监视和逻辑功能",
        ["MainApps:B"] = "控制、测量、监视和逻辑功能，含异常检测",
        ["FunctionalApps:A"] = "5 个线路/电缆应用包",
        ["FunctionalApps:B"] = "10 个线路/电缆应用包",
        ["FunctionalApps:C"] = "15 个线路/电缆应用包",
        ["FunctionalApps:D"] = "20 个线路/电缆应用包",
        ["FunctionalApps:E"] = "30 个线路/电缆应用包",
        ["FunctionalApps:N"] = "无",
        ["Aios:A"] = "5 个高级线路/电缆应用包",
        ["Aios:B"] = "10 个高级线路/电缆应用包",
        ["Aios:C"] = "15 个高级线路/电缆应用包",
        ["Aios:D"] = "20 个高级线路/电缆应用包",
        ["Aios:E"] = "30 个高级线路/电缆应用包",
        ["Aios:N"] = "无",
        ["Bios:A"] = "并联电容器保护",
        ["Bios:B"] = "线路差动保护",
        ["Bios:C"] = "并联电容器保护 + 线路差动保护",
        ["Bios:N"] = "不使用",
        ["CommSerials:A"] = "2 个变压器应用包",
        ["CommSerials:B"] = "4 个变压器应用包",
        ["CommSerials:N"] = "无",
        ["CommEthernets:A"] = "5 个电动机应用包",
        ["CommEthernets:B"] = "10 个电动机应用包",
        ["CommEthernets:C"] = "15 个电动机应用包",
        ["CommEthernets:D"] = "20 个电动机应用包",
        ["CommEthernets:E"] = "30 个电动机应用包",
        ["CommEthernets:N"] = "无",
        ["CommProtocols:1"] = "最多连接 5 个合并单元/继电器",
        ["CommProtocols:A"] = "最多连接 10 个合并单元/继电器",
        ["CommProtocols:B"] = "最多连接 15 个合并单元/继电器",
        ["CommProtocols:C"] = "最多连接 20 个合并单元/继电器",
        ["CommProtocols:D"] = "最多连接 30 个合并单元/继电器",
        ["Languages:A"] = "以太网 1000Base-TX / RJ-45",
        ["Languages:B"] = "以太网 1000Base-SX / 2 x LC，支持 PRP",
        ["Languages:N"] = "无",
        ["FrontPanels:A"] = "IEC 61850",
        ["FrontPanels:B"] = "IEC 61850 + IEC 60870-5-104",
        ["FrontPanels:C"] = "IEC 61850 + DNP3",
        ["Options_1:1"] = "英文",
        ["Options_2:A"] = "默认 Web HMI",
        ["PowerSupplies:A"] = "电能质量",
        ["PowerSupplies:B"] = "电压调节",
        ["PowerSupplies:C"] = "距离保护",
        ["PowerSupplies:D"] = "电能质量 + 电压调节",
        ["PowerSupplies:E"] = "电能质量 + 距离保护",
        ["PowerSupplies:F"] = "电压调节 + 距离保护",
        ["PowerSupplies:G"] = "电能质量 + 电压调节 + 距离保护",
        ["PowerSupplies:N"] = "无",
        ["Reserved:A"] = "弧光保护",
        ["Reserved:B"] = "低频减载",
        ["Reserved:C"] = "弧光保护 + 低频减载",
        ["Reserved:D"] = "母线差动保护",
        ["Reserved:E"] = "弧光保护 + 母线差动保护",
        ["Reserved:F"] = "低频减载 + 母线差动保护",
        ["Reserved:G"] = "弧光保护 + 低频减载 + 母线差动保护",
        ["Reserved:N"] = "无",
        ["Reserved2:1"] = "冗余电源：2 x 高压电源（100-250 V AC/DC）",
        ["Reserved2:2"] = "冗余电源：2 x 低压电源（36-72 VDC）",
        ["Reserved2:N"] = "无",
        ["Versions:1G"] = "产品版本 1.0",
        ["Versions:2G"] = "产品版本 1.1",
        ["Versions:3G"] = "产品版本 1.2",
        ["Versions:4G"] = "产品版本 1.3",
        ["Versions:5G"] = "产品版本 1.4",
        ["Versions:6G"] = "产品版本 1.5"
    };

    private readonly Dictionary<string, string> _descriptionsEnglish = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mountings:S"] = "SSC600 hardware",
        ["Mountings:V"] = "SSC600 SW software package",
        ["Standards:B"] = "Global",
        ["MainApps:A"] = "Basic protection, control, measurement, supervision and logic functions",
        ["MainApps:B"] = "Control, measurement, supervision and logic functions with anomaly detector",
        ["FunctionalApps:A"] = "5 Cable/Line AppPack",
        ["FunctionalApps:B"] = "10 Cable/Line AppPack",
        ["FunctionalApps:C"] = "15 Cable/Line AppPack",
        ["FunctionalApps:D"] = "20 Cable/Line AppPack",
        ["FunctionalApps:E"] = "30 Cable/Line AppPack",
        ["FunctionalApps:N"] = "None",
        ["Aios:A"] = "5 Advanced Cable/Line AppPack",
        ["Aios:B"] = "10 Advanced Cable/Line AppPack",
        ["Aios:C"] = "15 Advanced Cable/Line AppPack",
        ["Aios:D"] = "20 Advanced Cable/Line AppPack",
        ["Aios:E"] = "30 Advanced Cable/Line AppPack",
        ["Aios:N"] = "None",
        ["Bios:A"] = "Shunt capacitor protection",
        ["Bios:B"] = "Line differential protection",
        ["Bios:C"] = "Shunt capacitor protection + Line differential protection",
        ["Bios:N"] = "Not used",
        ["CommSerials:A"] = "2 Transformer AppPack",
        ["CommSerials:B"] = "4 Transformer AppPack",
        ["CommSerials:N"] = "None",
        ["CommEthernets:A"] = "5 Motor AppPack",
        ["CommEthernets:B"] = "10 Motor AppPack",
        ["CommEthernets:C"] = "15 Motor AppPack",
        ["CommEthernets:D"] = "20 Motor AppPack",
        ["CommEthernets:E"] = "30 Motor AppPack",
        ["CommEthernets:N"] = "None",
        ["CommProtocols:1"] = "IEC 61850-9-2LE process bus connectivity for up to 5 merging units/relays",
        ["CommProtocols:A"] = "IEC 61850-9-2LE process bus connectivity for up to 10 merging units/relays",
        ["CommProtocols:B"] = "IEC 61850-9-2LE process bus connectivity for up to 15 merging units/relays",
        ["CommProtocols:C"] = "IEC 61850-9-2LE process bus connectivity for up to 20 merging units/relays",
        ["CommProtocols:D"] = "IEC 61850-9-2LE process bus connectivity for up to 30 merging units/relays",
        ["Languages:A"] = "Ethernet 1000Base-TX / RJ-45",
        ["Languages:B"] = "Ethernet 1000Base-SX / 2 x LC, PRP",
        ["Languages:N"] = "None",
        ["FrontPanels:A"] = "IEC 61850",
        ["FrontPanels:B"] = "IEC 61850 + IEC 60870-5-104",
        ["FrontPanels:C"] = "IEC 61850 + DNP3",
        ["Options_1:1"] = "English",
        ["Options_2:A"] = "Default Web HMI",
        ["PowerSupplies:A"] = "Power quality",
        ["PowerSupplies:B"] = "Voltage regulation",
        ["PowerSupplies:C"] = "Distance protection",
        ["PowerSupplies:D"] = "Power quality + Voltage regulation",
        ["PowerSupplies:E"] = "Power quality + Distance protection",
        ["PowerSupplies:F"] = "Voltage regulation + Distance protection",
        ["PowerSupplies:G"] = "Power quality + Voltage regulation + Distance protection",
        ["PowerSupplies:N"] = "None",
        ["Reserved:A"] = "Arc protection",
        ["Reserved:B"] = "Frequency load-shedding",
        ["Reserved:C"] = "Arc protection + Frequency load-shedding",
        ["Reserved:D"] = "Busbar differential protection",
        ["Reserved:E"] = "Arc protection + Busbar differential protection",
        ["Reserved:F"] = "Frequency load-shedding + Busbar differential protection",
        ["Reserved:G"] = "Arc protection + Frequency load-shedding + Busbar differential protection",
        ["Reserved:N"] = "None",
        ["Reserved2:1"] = "Redundant power supply: 2 x HV power supply (100-250 V AC/DC)",
        ["Reserved2:2"] = "Redundant power supply: 2 x LV power supply (36-72 VDC)",
        ["Reserved2:N"] = "None",
        ["Versions:1G"] = "Product version 1.0",
        ["Versions:2G"] = "Product version 1.1",
        ["Versions:3G"] = "Product version 1.2",
        ["Versions:4G"] = "Product version 1.3",
        ["Versions:5G"] = "Product version 1.4",
        ["Versions:6G"] = "Product version 1.5"
    };

    public static Ssc600DescriptionCatalog Create() => new();

    public string GroupDisplayName(string groupName) =>
        _groupNames.TryGetValue(groupName, out var displayName) ? displayName : groupName;

    public string GroupDisplayNameEnglish(string groupName) =>
        _groupNamesEnglish.TryGetValue(groupName, out var displayName) ? displayName : groupName;

    public string ValidationDisplayName(string blockName) =>
        _validationNames.TryGetValue(blockName, out var displayName) ? displayName : blockName;

    public string Description(string groupName, string optionId, string token)
    {
        var key = $"{groupName}:{optionId}";
        if (_descriptions.TryGetValue(key, out var description))
        {
            return description;
        }

        return token.Replace('_', ' ');
    }

    public string DescriptionEnglish(string groupName, string optionId, string token)
    {
        var key = $"{groupName}:{optionId}";
        if (_descriptionsEnglish.TryGetValue(key, out var description))
        {
            return description;
        }

        return token.Replace('_', ' ');
    }
}
