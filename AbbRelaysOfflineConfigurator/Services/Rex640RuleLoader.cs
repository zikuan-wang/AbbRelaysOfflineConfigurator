using System.IO;
using System.Xml.Linq;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Rex640RuleLoader
{
    private const string RulesFileName = "REX640.xml";

    public Rex640RuleSet Load()
    {
        var path = ResolveRulesPath();
        var xml = SanitizeXml(File.ReadAllText(path));
        var document = XDocument.Parse(xml);
        var root = document.Root ?? throw new InvalidOperationException("REX640 rule file is empty.");
        var catalog = Rex640DescriptionCatalog.Create();

        var mainGroups = NormalizeMainGroups(root.Element("MainCodes")?
            .Elements("Digit")
            .Select((element, index) => ParseGroup(element, catalog, true, index))
            .Where(group => !string.IsNullOrWhiteSpace(group.Name))
            .OrderBy(group => group.SortOrder)
            .ToList() ?? []);

        var rawOptionGroups = root.Element("OptionCodes")?
            .Elements("Category")
            .Select((element, index) => ParseGroup(element, catalog, false, index + 100))
            .Where(group => !string.IsNullOrWhiteSpace(group.Name))
            .OrderBy(group => group.SortOrder)
            .ToList() ?? [];

        var optionGroups = BuildRolOptionGroups(catalog, rawOptionGroups);

        return new Rex640RuleSet(path, mainGroups, optionGroups);
    }

    private static IReadOnlyList<Rex640GroupRule> BuildRolOptionGroups(
        Rex640DescriptionCatalog catalog,
        IReadOnlyList<Rex640GroupRule> rawGroups)
    {
        var order = 100;
        return
        [
            Group("ArcModule", "弧光模块", "Arc module", false, false, order++,
            [
                Option("None", "无弧光模块", "No arc module"),
                Option("ARC1", "ARC1：4 路弧光传感器，支持环形或透镜传感器", "ARC1: 4 arc sensors; loop or lens")
            ]),
            Group("CommunicationModule", "通信模块", "Communication module", true, false, order++,
            [
                Option("COM1", "COM1：RJ45(LHMI) + 3xRJ45 + SFP 100M LC", "COM1: RJ45 (LHMI) + 3xRJ45 + SFP 100M LC"),
                Option("COM2", "COM2：RJ45(LHMI) + 2xLC + RJ45 + SFP 100M LC", "COM2: RJ45 (LHMI) + 2xLC + RJ45 + SFP 100M LC"),
                Option("COM3", "COM3：RJ45(LHMI) + 3xLC + SFP 100M LC", "COM3: RJ45 (LHMI) + 3xLC + SFP 100M LC"),
                Option("COM4", "COM4：RJ45(LHMI) + 2xRJ45 + SFP 100M LC + RS485/IRIG-B + ST", "COM4: RJ45 (LHMI) + 2xRJ45 + SFP 100M LC + RS485/IRIG-B + ST"),
                Option("COM5", "COM5：RJ45(LHMI) + 2xLC + SFP 100M LC + RS485/IRIG-B + ST", "COM5: RJ45 (LHMI) + 2xLC + SFP 100M LC + RS485/IRIG-B + ST")
            ]),
            Group("BIO1Module", "BIO 模块", "BIO module", false, false, order++,
            [
                Option("None", "无 BIO1 模块", "No BIO1 module"),
                Option("1x BIO1", "1x BIO1：14BI + 8SO", "1x BIO1: 14BI + 8SO"),
                Option("2x BIO1", "2x BIO1：每块 14BI + 8SO", "2x BIO1: each 14BI + 8SO"),
                Option("3x BIO1", "3x BIO1：每块 14BI + 8SO", "3x BIO1: each 14BI + 8SO", "Housing=B")
            ]),
            Group("BIO2Module", "BIO 模块（静态功率输出）", "BIO module with static power outputs", false, false, order++,
            [
                Option("None", "无 BIO2 模块", "No BIO2 module"),
                Option("1x BIO2", "1x BIO2：9BI + 6SPO + 2SPO with TCS", "1x BIO2: 9BI + 6SPO + 2SPO with TCS"),
                Option("2x BIO2", "2x BIO2：每块 9BI + 6SPO + 2SPO with TCS", "2x BIO2: each 9BI + 6SPO + 2SPO with TCS"),
                Option("3x BIO2", "3x BIO2：每块 9BI + 6SPO + 2SPO with TCS", "3x BIO2: each 9BI + 6SPO + 2SPO with TCS", "Housing=B")
            ]),
            Group("RTD1Module", "RTD 模块", "RTD module", false, false, order++,
            [
                Option("None", "无 RTD1 模块", "No RTD1 module"),
                Option("1x RTD1", "1x RTD1：10RTD + 2mA 输入或输出", "1x RTD1: 10RTD + 2mA in or out"),
                Option("2x RTD1", "2x RTD1：每块 10RTD + 2mA 输入或输出", "2x RTD1: each 10RTD + 2mA in or out", "Housing=B")
            ]),
            Group("RTD2Module", "RTD/BI 模块", "RTD/BI module", false, false, order++,
            [
                Option("None", "无 RTD2 模块", "No RTD2 module"),
                Option("1x RTD2", "1x RTD2：3RTD + 6mA 输入或输出 + 12BI", "1x RTD2: 3RTD + 6mA in or out + 12BI"),
                Option("2x RTD2", "2x RTD2：每块 3RTD + 6mA 输入或输出 + 12BI", "2x RTD2: each 3RTD + 6mA in or out + 12BI", "Housing=B")
            ]),
            Group("BIM1Module", "BIM 模块", "BIM module", false, false, order++,
            [
                Option("None", "无 BIM1 模块", "No BIM1 module"),
                Option("1x BIM1", "1x BIM1：24BI", "1x BIM1: 24BI", "ConnectivityLevel=PCL5,PCL6"),
                Option("2x BIM1", "2x BIM1：每块 24BI", "2x BIM1: each 24BI", "ConnectivityLevel=PCL5,PCL6"),
                Option("3x BIM1", "3x BIM1：每块 24BI", "3x BIM1: each 24BI", "Housing=B&ConnectivityLevel=PCL5,PCL6")
            ]),
            Group("WideSlotEModule", "宽模块槽 E", "Wide module slot E", false, false, order++,
            [
                Option("None", "槽 E 不选宽模块", "No wide module in slot E"),
                Option("1x BIO3", "1x BIO3：14BI + 8SO（宽模块槽 E）", "1x BIO3: 14BI + 8SO (wide module slot E)", "Housing=B&ConnectivityLevel=PCL5,PCL6"),
                Option("1x BIO4", "1x BIO4：9BI + 6SPO + 2SPO with TCS（宽模块槽 E）", "1x BIO4: 9BI + 6SPO + 2SPO with TCS (wide module slot E)", "Housing=B&ConnectivityLevel=PCL5,PCL6"),
                Option("1x BIM3", "1x BIM3：24BI（宽模块槽 E）", "1x BIM3: 24BI (wide module slot E)", "Housing=B&ConnectivityLevel=PCL5,PCL6")
            ]),
            Group("AnalogModule", "模拟量/传感器模块", "Analog/sensor module", true, false, order++,
            [
                Option("1x AIM1", "1x AIM1：4CT(1/5A) + 1CT(0.2/1A) + 5VT", "1x AIM1: 4CT (1/5A) + 1CT (0.2/1A) + 5VT"),
                Option("2x AIM1", "2x AIM1：每块 4CT(1/5A) + 1CT(0.2/1A) + 5VT", "2x AIM1: each 4CT (1/5A) + 1CT (0.2/1A) + 5VT", "Housing=B"),
                Option("1x AIM2", "1x AIM2：6CT(1/5A) + 4VT", "1x AIM2: 6CT (1/5A) + 4VT"),
                Option("2x AIM2", "2x AIM2：每块 6CT(1/5A) + 4VT", "2x AIM2: each 6CT (1/5A) + 4VT", "Housing=B"),
                Option("1x AIM3", "1x AIM3：7CT(1/5A) + 3VT", "1x AIM3: 7CT (1/5A) + 3VT", "ConnectivityLevel=PCL5,PCL6"),
                Option("2x AIM3", "2x AIM3：每块 7CT(1/5A) + 3VT", "2x AIM3: each 7CT (1/5A) + 3VT", "Housing=B&ConnectivityLevel=PCL5,PCL6"),
                Option("1x SIM1", "1x SIM1：3 个组合传感器 + 1CT(0.2/1A) + 1VT，IEC 60044", "1x SIM1: 3 combi sensors + 1CT (0.2/1A) + 1VT IEC 60044"),
                Option("2x SIM1", "2x SIM1：每块 3 个组合传感器 + 1CT(0.2/1A) + 1VT，IEC 60044", "2x SIM1: each 3 combi sensors + 1CT (0.2/1A) + 1VT IEC 60044", "Housing=B"),
                Option("1x SIM2", "1x SIM2：3 个组合传感器 + 1CT(0.2/1A) + 1VT，IEC 61869", "1x SIM2: 3 combi sensors + 1CT (0.2/1A) + 1VT IEC 61869"),
                Option("2x SIM2", "2x SIM2：每块 3 个组合传感器 + 1CT(0.2/1A) + 1VT，IEC 61869", "2x SIM2: each 3 combi sensors + 1CT (0.2/1A) + 1VT IEC 61869", "Housing=B"),
                Option("1x SIM3", "1x SIM3：2 x 3 个组合传感器，IEC 61869", "1x SIM3: 2 x 3 combi sensors IEC 61869", "ConnectivityLevel=PCL5,PCL6"),
                Option("2x SIM3", "2x SIM3：每块 2 x 3 个组合传感器，IEC 61869", "2x SIM3: each 2 x 3 combi sensors IEC 61869", "Housing=B&ConnectivityLevel=PCL5,PCL6")
            ]),
            Group("PSM", "电源模块", "Power supply module", true, false, order++,
            [
                Option("PSM1", "PSM1：24-60 VDC + 3SO + 2SSO + 2PO + 3PO with TCS", "PSM1: 24-60 VDC + 3SO + 2SSO + 2PO + 3PO with TCS"),
                Option("PSM2", "PSM2：48-250 VDC / 100-240 VAC + 3SO + 2SSO + 2PO + 3PO with TCS", "PSM2: 48-250 VDC / 100-240 VAC + 3SO + 2SSO + 2PO + 3PO with TCS"),
                Option("PSM3", "PSM3：110-125 VDC + 3SO + 2SSO + 2PO + 3PO with TCS", "PSM3: 110-125 VDC + 3SO + 2SSO + 2PO + 3PO with TCS")
            ]),
            GroupFromRaw("Current_Connectors", "电流/电压端子", "CT and VT connectors", rawGroups, catalog, order++),
            GroupFromRaw("Signal_Connectors", "信号端子", "Signal connectors", rawGroups, catalog, order++),
            ApplicationGroup(order++),
            GroupFromRaw("Protocol", "通信协议", "Protocols", rawGroups, catalog, order++),
            Group("ConnectivityLevel", "PCL 版本", "Connectivity level", true, false, order++,
            [
                Option("PCL5", "PCL5：软件版本 2.0", "PCL5: SW version 2.0"),
                Option("PCL6", "PCL6：软件版本 6.1（REX640 2.0 推荐）", "PCL6: SW version 6.1 (recommended for REX640 2.0)")
            ])
        ];
    }

    private static IReadOnlyList<Rex640GroupRule> NormalizeMainGroups(IReadOnlyList<Rex640GroupRule> groups) =>
        groups.Select(group => group.Name switch
            {
                "ProductVersion" => group with
                {
                    Options = group.Options
                        .Where(option => option.Id.Equals("2", StringComparison.OrdinalIgnoreCase))
                        .ToList()
                },
                "CustomerSpecific" => group with
                {
                    Options = group.Options
                        .Where(option => option.Id is "G" or "C")
                        .ToList()
                },
                "ConformalCoating" => group with
                {
                    Options = group.Options
                        .Where(option => option.Id.Equals("C", StringComparison.OrdinalIgnoreCase))
                        .ToList()
                },
                _ => group
            })
            .ToList();

    private static Rex640GroupRule Group(
        string name,
        string displayName,
        string displayNameEnglish,
        bool isMandatory,
        bool isMultiple,
        int sortOrder,
        IReadOnlyList<Rex640OptionRule> options,
        string invalidSlot = "") =>
        new(
            Name: name,
            Location: "",
            Slot: "",
            SortOrder: sortOrder,
            IsMainGroup: false,
            IsMandatory: isMandatory,
            BaseIsMultiple: isMultiple,
            InvalidSlot: invalidSlot,
            DisplayName: displayName,
            DisplayNameEnglish: displayNameEnglish,
            MultipleRules: [],
            Options: options);

    private static Rex640GroupRule GroupFromRaw(
        string rawName,
        string displayName,
        string displayNameEnglish,
        IReadOnlyList<Rex640GroupRule> rawGroups,
        Rex640DescriptionCatalog catalog,
        int sortOrder,
        string groupVisibility = "",
        string invalidSlot = "")
    {
        var raw = rawGroups.First(group => group.Name.Equals(rawName, StringComparison.OrdinalIgnoreCase));
        var options = raw.Options
            .Select(option => option with
            {
                Description = catalog.Description(rawName, option.Id, option.DescriptionKey),
                DescriptionEnglish = catalog.DescriptionEnglish(rawName, option.Id, option.DescriptionKey),
                ShortDescription = catalog.ShortDescription(rawName, option.Id, option.ShortDescriptionKey),
                ShortDescriptionEnglish = catalog.ShortDescriptionEnglish(rawName, option.Id, option.ShortDescriptionKey),
                Visibility = MergeExpression(option.Visibility, groupVisibility)
            })
            .ToList();

        return raw with
        {
            SortOrder = sortOrder,
            InvalidSlot = invalidSlot,
            DisplayName = displayName,
            DisplayNameEnglish = displayNameEnglish,
            Options = options
        };
    }

    private static Rex640GroupRule ApplicationGroup(int sortOrder) =>
        Group("Application", "应用包", "Application packages", false, true, sortOrder,
        [
            Option("APP1", "APP1：馈线接地故障保护扩展包", "APP1: Feeder earth-fault protection extension package"),
            Option("APP2", "APP2：馈线故障定位包", "APP2: Feeder fault locator package"),
            Option("APP3", "APP3：线路距离保护包", "APP3: Line distance protection package"),
            Option("APP4", "APP4：线路差动保护包", "APP4: Line differential protection package"),
            Option("APP5", "APP5：并联电容器保护包", "APP5: Shunt capacitor protection package"),
            Option("APP6", "APP6：并网/联络保护包", "APP6: Interconnection protection package"),
            Option("APP7", "APP7：电机/机器保护包", "APP7: Machine protection package"),
            Option("ADD1", "ADD1：同步电机附加包", "ADD1: Synchronous machine add-on package", "Application=APP7"),
            Option("APP8", "APP8：电力变压器保护包", "APP8: Power transformer protection package"),
            Option("ADD2", "ADD2：三绕组电力变压器附加包", "ADD2: 3-winding power transformer add-on package", "Application=APP8"),
            Option("APP9", "APP9：母线保护包", "APP9: Busbar protection package"),
            Option("APP10", "APP10：有载调压开关控制包（自动电压调节）", "APP10: OLTC control package (automatic voltage regulator)"),
            Option("APP11", "APP11：发电机自动同期包", "APP11: Generator autosynchronizer package"),
            Option("APP12", "APP12：网络自动同期包", "APP12: Network autosynchronizer package"),
            Option("APP13", "APP13：消弧线圈控制包", "APP13: Petersen coil control package"),
            Option("APP14", "APP14：柴油发电机组监视包", "APP14: DG-set monitoring package"),
            Option("APP51", "APP51：一路备用馈线高速切换装置", "APP51: HSTD for one stand-by feeder", "Application=!APP52,!APP53", "ConnectivityLevel=PCL5,PCL6"),
            Option("APP52", "APP52：两路备用馈线高速切换装置", "APP52: HSTD for two stand-by feeders", "Application=!APP51,!APP53", "ConnectivityLevel=PCL5,PCL6"),
            Option("APP53", "APP53：三路等价馈线高速切换装置", "APP53: HSTD for three equal feeders", "Application=!APP51,!APP52", "ConnectivityLevel=PCL5,PCL6")
        ]);

    private static Rex640OptionRule Option(
        string id,
        string description,
        string descriptionEnglish,
        string validity = "",
        string visibility = "") =>
        new(
            Id: id,
            Version: "*",
            DescriptionKey: id,
            ShortDescriptionKey: id,
            Description: description,
            DescriptionEnglish: descriptionEnglish,
            ShortDescription: description,
            ShortDescriptionEnglish: descriptionEnglish,
            Validity: validity,
            Visibility: visibility,
            Hidden: false,
            SelectionPriority: 0);

    private static string MergeExpression(string expression, string extra)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return extra;
        }

        if (string.IsNullOrWhiteSpace(extra))
        {
            return expression;
        }

        return $"{expression}&{extra}";
    }

    private static Rex640GroupRule ParseGroup(
        XElement element,
        Rex640DescriptionCatalog catalog,
        bool isMainGroup,
        int fallbackSortOrder)
    {
        var name = (string?)element.Attribute(isMainGroup ? "Group" : "Name") ?? "";
        var location = (string?)element.Attribute("Location") ?? "";
        var slot = (string?)element.Attribute("Slot") ?? "";
        var isMandatory = ParseBool((string?)element.Attribute("IsMandatory"));
        var isMultiple = ParseBool((string?)element.Attribute("IsMultiple"));
        var invalidSlot = (string?)element.Attribute("InvalidSlot") ?? "";

        var multipleRules = element.Elements("IsMultiple")
            .Select(rule => new Rex640MultipleRule(
                Version: (string?)rule.Attribute("Version") ?? "*",
                Value: ParseBool((string?)rule.Attribute("Value"))))
            .ToList();

        var options = element.Elements("Option")
            .Select((option, index) =>
            {
                var id = (string?)option.Attribute("Id") ?? "";
                var descriptionKey = (string?)option.Attribute("Description") ?? "";
                var shortDescriptionKey = (string?)option.Attribute("ShortDescription") ?? "";
                return new Rex640OptionRule(
                    Id: id,
                    Version: (string?)option.Attribute("Version") ?? "*",
                    DescriptionKey: descriptionKey,
                    ShortDescriptionKey: shortDescriptionKey,
                    Description: catalog.Description(name, id, descriptionKey),
                    DescriptionEnglish: catalog.DescriptionEnglish(name, id, descriptionKey),
                    ShortDescription: catalog.ShortDescription(name, id, shortDescriptionKey),
                    ShortDescriptionEnglish: catalog.ShortDescriptionEnglish(name, id, shortDescriptionKey),
                    Validity: (string?)option.Attribute("Validity") ?? "",
                    Visibility: (string?)option.Attribute("Visibility") ?? "",
                    Hidden: ParseBool((string?)option.Attribute("Hidden")),
                    SelectionPriority: ParseInt((string?)option.Attribute("Selection_Priority"), index));
            })
            .Where(option => !string.IsNullOrWhiteSpace(option.Id))
            .OrderBy(option => option.SelectionPriority)
            .ToList();

        return new Rex640GroupRule(
            Name: name,
            Location: location,
            Slot: slot,
            SortOrder: isMainGroup ? SortOrder(location, fallbackSortOrder) : fallbackSortOrder,
            IsMainGroup: isMainGroup,
            IsMandatory: isMandatory,
            BaseIsMultiple: isMultiple,
            InvalidSlot: invalidSlot,
            DisplayName: catalog.GroupDisplayName(name),
            DisplayNameEnglish: catalog.GroupDisplayNameEnglish(name),
            MultipleRules: multipleRules,
            Options: options);
    }

    private static string SanitizeXml(string xml) =>
        xml.Replace("\"/>/>", "\"/>", StringComparison.Ordinal)
            .Replace(" />\"", " />", StringComparison.Ordinal);

    private static bool ParseBool(string? value) =>
        bool.TryParse(value, out var result) && result;

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private static int SortOrder(string location, int fallback)
    {
        var first = location.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        return int.TryParse(first, out var parsed) ? parsed : fallback;
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

public sealed record Rex640RuleSet(
    string SourcePath,
    IReadOnlyList<Rex640GroupRule> MainGroups,
    IReadOnlyList<Rex640GroupRule> OptionGroups);

public sealed record Rex640GroupRule(
    string Name,
    string Location,
    string Slot,
    int SortOrder,
    bool IsMainGroup,
    bool IsMandatory,
    bool BaseIsMultiple,
    string InvalidSlot,
    string DisplayName,
    string DisplayNameEnglish,
    IReadOnlyList<Rex640MultipleRule> MultipleRules,
    IReadOnlyList<Rex640OptionRule> Options)
{
    public bool AllowsMultiple(string connectivityLevel)
    {
        var rule = MultipleRules.FirstOrDefault(rule => Rex640OptionRule.VersionMatches(rule.Version, connectivityLevel));
        return rule?.Value ?? BaseIsMultiple;
    }
}

public sealed record Rex640MultipleRule(string Version, bool Value);

public sealed record Rex640OptionRule(
    string Id,
    string Version,
    string DescriptionKey,
    string ShortDescriptionKey,
    string Description,
    string DescriptionEnglish,
    string ShortDescription,
    string ShortDescriptionEnglish,
    string Validity,
    string Visibility,
    bool Hidden,
    int SelectionPriority)
{
    public bool SupportsVersion(string connectivityLevel) => VersionMatches(Version, connectivityLevel);

    public static bool VersionMatches(string versionExpression, string connectivityLevel)
    {
        if (string.IsNullOrWhiteSpace(versionExpression) || versionExpression == "*" || string.IsNullOrWhiteSpace(connectivityLevel))
        {
            return true;
        }

        return versionExpression
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(connectivityLevel, StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class Rex640DescriptionCatalog
{
    private readonly Dictionary<string, string> _groupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REX640Product"] = "REX640 产品",
        ["Housing"] = "机箱",
        ["ProductVersion"] = "产品版本",
        ["InterfaceLevel"] = "接口级别",
        ["CustomerSpecific"] = "客户/区域选项",
        ["ConformalCoating"] = "保形涂层",
        ["Slot_A1"] = "A1 插槽：弧光模块",
        ["Slot_A2"] = "A2 插槽：通信模块",
        ["Slot_B"] = "B 插槽：I/O 模块",
        ["Slot_C"] = "C 插槽：I/O 模块",
        ["Slot_D"] = "D 插槽：I/O 模块",
        ["Slot_E"] = "E 插槽：I/O 模块",
        ["Slot_F"] = "F 插槽：模拟量/传感器模块",
        ["Slot_G"] = "G 插槽：电源模块",
        ["Application"] = "应用包",
        ["Protocol"] = "通信协议",
        ["Language"] = "语言",
        ["Signal_Connectors"] = "信号端子",
        ["Current_Connectors"] = "电流端子",
        ["ConnectivityLevel"] = "PCL 版本"
    };

    private readonly Dictionary<string, string> _groupNamesEnglish = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REX640Product"] = "REX640 product",
        ["Housing"] = "Housing",
        ["ProductVersion"] = "Product version",
        ["InterfaceLevel"] = "Interface level",
        ["CustomerSpecific"] = "Customer/region option",
        ["ConformalCoating"] = "Conformal coating",
        ["Slot_A1"] = "Slot A1: arc module",
        ["Slot_A2"] = "Slot A2: communication module",
        ["Slot_B"] = "Slot B: I/O module",
        ["Slot_C"] = "Slot C: I/O module",
        ["Slot_D"] = "Slot D: I/O module",
        ["Slot_E"] = "Slot E: I/O module",
        ["Slot_F"] = "Slot F: analog/sensor module",
        ["Slot_G"] = "Slot G: power supply module",
        ["Application"] = "Application packages",
        ["Protocol"] = "Communication protocol",
        ["Language"] = "Language",
        ["Signal_Connectors"] = "Signal connectors",
        ["Current_Connectors"] = "Current connectors",
        ["ConnectivityLevel"] = "PCL version"
    };

    private readonly Dictionary<string, string> _languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LNG1"] = "英语",
        ["LNG2"] = "中文",
        ["LNG3"] = "德语",
        ["LNG4"] = "瑞典语",
        ["LNG5"] = "西班牙语",
        ["LNG6"] = "俄语",
        ["LNG7"] = "波兰语",
        ["LNG8"] = "葡萄牙语（巴西）",
        ["LNG9"] = "葡萄牙语（葡萄牙）",
        ["LNG10"] = "意大利语",
        ["LNG11"] = "芬兰语",
        ["LNG12"] = "法语",
        ["LNG13"] = "挪威语",
        ["LNG14"] = "捷克语",
        ["LNG15"] = "阿拉伯语",
        ["LNG16"] = "波斯语",
        ["LNG17"] = "韩语",
        ["LNG18"] = "荷兰语（比利时）",
        ["LNG19"] = "丹麦语",
        ["LNG20"] = "土耳其语",
        ["LNG21"] = "匈牙利语",
        ["LNG22"] = "克罗地亚语",
        ["LNG23"] = "斯洛文尼亚语",
        ["LNG24"] = "泰语",
        ["LNG25"] = "日语",
        ["LNG26"] = "马来语",
        ["LNG27"] = "越南语",
        ["LNG28"] = "保加利亚语",
        ["LNG29"] = "荷兰语",
        ["LNG99"] = "中文"
    };

    private readonly Dictionary<string, string> _languagesEnglish = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LNG1"] = "English",
        ["LNG2"] = "Chinese",
        ["LNG3"] = "German",
        ["LNG4"] = "Swedish",
        ["LNG5"] = "Spanish",
        ["LNG6"] = "Russian",
        ["LNG7"] = "Polish",
        ["LNG8"] = "Portuguese (Brazil)",
        ["LNG9"] = "Portuguese (Portugal)",
        ["LNG10"] = "Italian",
        ["LNG11"] = "Finnish",
        ["LNG12"] = "French",
        ["LNG13"] = "Norwegian",
        ["LNG14"] = "Czech",
        ["LNG15"] = "Arabic",
        ["LNG16"] = "Persian",
        ["LNG17"] = "Korean",
        ["LNG18"] = "Dutch (Belgium)",
        ["LNG19"] = "Danish",
        ["LNG20"] = "Turkish",
        ["LNG21"] = "Hungarian",
        ["LNG22"] = "Croatian",
        ["LNG23"] = "Slovenian",
        ["LNG24"] = "Thai",
        ["LNG25"] = "Japanese",
        ["LNG26"] = "Malay",
        ["LNG27"] = "Vietnamese",
        ["LNG28"] = "Bulgarian",
        ["LNG29"] = "Dutch",
        ["LNG99"] = "Chinese"
    };

    public static Rex640DescriptionCatalog Create() => new();

    public string GroupDisplayName(string groupName) =>
        _groupNames.TryGetValue(groupName, out var value) ? value : groupName;

    public string GroupDisplayNameEnglish(string groupName) =>
        _groupNamesEnglish.TryGetValue(groupName, out var value) ? value : groupName;

    public string Description(string groupName, string id, string token) =>
        Describe(groupName, id, token, false);

    public string DescriptionEnglish(string groupName, string id, string token) =>
        Describe(groupName, id, token, true);

    public string ShortDescription(string groupName, string id, string token)
    {
        var description = Describe(groupName, id, token, false);
        return description.Length <= 80 ? description : $"{id}: {ShortKind(groupName, id, false)}";
    }

    public string ShortDescriptionEnglish(string groupName, string id, string token)
    {
        var description = Describe(groupName, id, token, true);
        return description.Length <= 80 ? description : $"{id}: {ShortKind(groupName, id, true)}";
    }

    private string Describe(string groupName, string id, string token, bool english)
    {
        if (id.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "None" : "无";
        }

        if (groupName.Equals("REX640Product", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "REX640 protection and control relay" : "REX640 保护和控制继电器";
        }

        if (groupName.Equals("Housing", StringComparison.OrdinalIgnoreCase))
        {
            return id.Equals("A", StringComparison.OrdinalIgnoreCase)
                ? english ? "A: compact housing" : "A：紧凑机箱"
                : english ? "B: extended housing with more I/O slots" : "B：扩展机箱（更多 I/O 插槽）";
        }

        if (groupName.Equals("ProductVersion", StringComparison.OrdinalIgnoreCase))
        {
            return english ? $"Product version {id}" : $"产品版本 {id}";
        }

        if (groupName.Equals("InterfaceLevel", StringComparison.OrdinalIgnoreCase))
        {
            return english ? $"Interface level {id}" : $"接口级别 {id}";
        }

        if (groupName.Equals("CustomerSpecific", StringComparison.OrdinalIgnoreCase))
        {
            return id switch
            {
                "G" => english ? "Global option" : "全球选项",
                "C" => english ? "China option" : "中国选项",
                "N" => english ? "No customer-specific option" : "无客户特定选项",
                _ => HumanizeToken(token)
            };
        }

        if (groupName.Equals("ConformalCoating", StringComparison.OrdinalIgnoreCase))
        {
            return id.Equals("C", StringComparison.OrdinalIgnoreCase)
                ? english ? "With conformal coating" : "带保形涂层"
                : english ? "No conformal coating" : "无保形涂层";
        }

        if (groupName.Equals("Language", StringComparison.OrdinalIgnoreCase))
        {
            var map = english ? _languagesEnglish : _languages;
            return map.TryGetValue(id, out var value)
                ? $"{id}: {value}"
                : $"{id}: {HumanizeToken(token)}";
        }

        if (groupName.Equals("ConnectivityLevel", StringComparison.OrdinalIgnoreCase))
        {
            return english ? $"{id}: product connectivity level" : $"{id}：产品连接包等级";
        }

        if (groupName.Equals("Application", StringComparison.OrdinalIgnoreCase))
        {
            return id.StartsWith("ADD", StringComparison.OrdinalIgnoreCase)
                ? english ? $"{id}: additional application package" : $"{id}：附加应用包"
                : english ? $"{id}: application package" : $"{id}：应用包";
        }

        if (groupName.Equals("Protocol", StringComparison.OrdinalIgnoreCase))
        {
            return id switch
            {
                "CMP1" => "CMP1: IEC 61850",
                "CMP2" => english ? "CMP2: IEC 61850 and Modbus" : "CMP2：IEC 61850 + Modbus",
                "CMP3" => english ? "CMP3: IEC 61850 and IEC 60870-5-103" : "CMP3：IEC 61850 + IEC 60870-5-103",
                "CMP4" => english ? "CMP4: IEC 61850 and DNP3" : "CMP4：IEC 61850 + DNP3",
                "CMP5" => english ? "CMP5: IEC 61850 and IEC 60870-5-104" : "CMP5：IEC 61850 + IEC 60870-5-104",
                "CMP30" => english ? "CMP30: IEC 61850 and Modbus (master)" : "CMP30：IEC 61850 + Modbus（主站）",
                _ => english ? $"{id}: communication protocol option" : $"{id}：通信协议选项"
            };
        }

        if (groupName.Equals("Signal_Connectors", StringComparison.OrdinalIgnoreCase))
        {
            return id switch
            {
                "SCT1" => english ? "SCT1: Compression type signal connectors" : "SCT1：压接型信号端子",
                "SCT2" => english ? "SCT2: Ring lug type signal connectors" : "SCT2：环形端子型信号端子",
                "SCT3" => english ? "SCT3: Push-in type signal connectors" : "SCT3：直插型信号端子",
                "None" => english ? "No signal connectors" : "无信号端子",
                _ => english ? $"{id}: signal connector option" : $"{id}：信号端子选项"
            };
        }

        if (groupName.Equals("Current_Connectors", StringComparison.OrdinalIgnoreCase))
        {
            return id switch
            {
                "MCT1" => english ? "MCT1: Compression type CT and VT connectors" : "MCT1：压接型 CT/VT 端子",
                "MCT2" => english ? "MCT2: Ring lug type CT and VT connectors" : "MCT2：环形端子型 CT/VT 端子",
                "None" => english ? "No CT and VT connectors" : "无 CT/VT 端子",
                _ => english ? $"{id}: current connector option" : $"{id}：电流端子选项"
            };
        }

        return $"{id}: {ShortKind(groupName, id, english)}";
    }

    private static string ShortKind(string groupName, string id, bool english)
    {
        if (id.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "communication module" : "通信模块";
        }

        if (id.StartsWith("BIO", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "binary I/O module" : "开关量 I/O 模块";
        }

        if (id.StartsWith("BIM", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "binary input module" : "开关量输入模块";
        }

        if (id.StartsWith("RTD", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "RTD/mA module" : "RTD/mA 模块";
        }

        if (id.StartsWith("AIM", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "analog input module" : "模拟量输入模块";
        }

        if (id.StartsWith("SIM", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "sensor input module" : "传感器输入模块";
        }

        if (id.StartsWith("PSM", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "power supply module" : "电源模块";
        }

        if (id.StartsWith("ARC", StringComparison.OrdinalIgnoreCase))
        {
            return english ? "arc protection module" : "弧光保护模块";
        }

        return HumanizeToken(id);
    }

    private static string HumanizeToken(string token) =>
        string.IsNullOrWhiteSpace(token)
            ? ""
            : token.Replace('_', ' ');
}
