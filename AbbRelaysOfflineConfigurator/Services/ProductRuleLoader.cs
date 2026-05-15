using System.Globalization;
using System.Xml.Linq;
using AbbRelaysOfflineConfigurator.Models;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class ProductRuleLoader
{
    public ProductRuleSet Load(string path)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("REX615_ROL.xml 缺少根节点。");
        var rules = new ProductRuleSet();

        LoadMainCodes(root, rules);
        LoadSlotConstraints(root, rules);
        LoadOptionCodes(root, rules);

        return rules;
    }

    private static void LoadMainCodes(XElement root, ProductRuleSet rules)
    {
        var order = 0;
        foreach (var digit in root.Element("MainCodes")?.Elements("Digit") ?? [])
        {
            var group = new OptionGroup
            {
                Name = Attr(digit, "Group"),
                EnglishName = Rex615EnglishTextCatalog.GroupName(Attr(digit, "Group")),
                Location = Attr(digit, "Location"),
                IsMandatory = BoolAttr(digit, "IsMandatory", true),
                IsMultiple = BoolAttr(digit, "IsMultiple", false),
                IsMainCode = true,
                SortOrder = order++
            };

            foreach (var optionElement in digit.Elements("Option"))
            {
                var option = LoadOption(optionElement, group.Name, isMainCode: true);
                group.Options.Add(option);
                rules.OptionsById[option.Id] = option;
            }

            rules.MainGroups.Add(group);
        }
    }

    private static void LoadOptionCodes(XElement root, ProductRuleSet rules)
    {
        var order = 0;
        foreach (var category in root.Element("OptionCodes")?.Elements("Category") ?? [])
        {
            var group = new OptionGroup
            {
                Name = Attr(category, "Name"),
                EnglishName = Rex615EnglishTextCatalog.GroupName(Attr(category, "Name")),
                IsMandatory = BoolAttr(category, "IsMandatory", false),
                IsMultiple = BoolAttr(category, "IsMultiple", false),
                IsMainCode = false,
                SortOrder = order++
            };

            foreach (var multipleElement in category.Elements("IsMultiple"))
            {
                var version = Attr(multipleElement, "Version");
                if (!string.IsNullOrWhiteSpace(version))
                {
                    group.IsMultipleByVersion[version] = BoolAttr(multipleElement, "Value", group.IsMultiple);
                }
            }

            foreach (var optionElement in category.Elements("Option"))
            {
                var option = LoadOption(optionElement, group.Name, isMainCode: false);
                group.Options.Add(option);
                rules.OptionsById[option.Id] = option;
            }

            rules.OptionGroups.Add(group);
        }
    }

    private static void LoadSlotConstraints(XElement root, ProductRuleSet rules)
    {
        var constraintElements = root.Elements("SlotConstraints").ToList();
        if (constraintElements.Count == 0)
        {
            return;
        }

        foreach (var constraints in constraintElements)
        {
            var constraintSet = new SlotConstraintSet
            {
                Version = Attr(constraints, "Version"),
                Source = Attr(constraints, "Source")
            };

            foreach (var housingElement in constraints.Elements("Housing"))
            {
                var housing = new HousingConstraint
                {
                    Id = Attr(housingElement, "Id"),
                    Description = Attr(housingElement, "Description")
                };

                foreach (var slotElement in housingElement.Elements("Slot"))
                {
                    var slot = new SlotDefinition
                    {
                        Id = Attr(slotElement, "Id"),
                        Capacity = IntAttr(slotElement, "Capacity", 1),
                        CodeOrder = IntAttr(slotElement, "CodeOrder", housing.Slots.Count + 1)
                    };

                    foreach (var module in SplitCsv(Attr(slotElement, "Modules")))
                    {
                        slot.Modules.Add(module);
                    }

                    housing.Slots.Add(slot);
                }

                foreach (var requirementElement in housingElement.Elements("Requirement"))
                {
                    var requirement = new SlotRequirement
                    {
                        Id = Attr(requirementElement, "Id"),
                        Type = Attr(requirementElement, "Type"),
                        Slot = NullableAttr(requirementElement, "Slot")
                    };

                    foreach (var slot in SplitCsv(Attr(requirementElement, "Slots")))
                    {
                        requirement.Slots.Add(slot);
                    }

                    foreach (var module in SplitCsv(Attr(requirementElement, "Modules")))
                    {
                        requirement.Modules.Add(module);
                    }

                    housing.Requirements.Add(requirement);
                }

                constraintSet.Housings[housing.Id] = housing;
            }

            if (rules.SlotConstraints.Housings.Count == 0)
            {
                rules.SlotConstraints = constraintSet;
            }

            if (!string.IsNullOrWhiteSpace(constraintSet.Version))
            {
                rules.SlotConstraintsByVersion[constraintSet.Version] = constraintSet;
            }
        }
    }

    private static RuleOption LoadOption(XElement element, string groupName, bool isMainCode)
    {
        var description = Attr(element, "Description");
        var shortDescription = Attr(element, "ShortDescription");
        var englishDescription = Attr(element, "DescriptionEn", "EnglishDescription");
        var englishShortDescription = Attr(element, "ShortDescriptionEn", "EnglishShortDescription");
        var option = new RuleOption
        {
            Id = Attr(element, "Id"),
            GroupName = groupName,
            Description = description,
            ShortDescription = shortDescription,
            EnglishDescription = string.IsNullOrWhiteSpace(englishDescription)
                ? Rex615EnglishTextCatalog.OptionDescription(groupName, Attr(element, "Id"), description, shortDescription)
                : englishDescription,
            EnglishShortDescription = string.IsNullOrWhiteSpace(englishShortDescription)
                ? Rex615EnglishTextCatalog.OptionShortDescription(groupName, Attr(element, "Id"), shortDescription, description)
                : englishShortDescription,
            Validity = NullableAttr(element, "Validity"),
            ModuleType = NullableAttr(element, "ModuleType"),
            ModuleCount = IntAttr(element, "ModuleCount", 0),
            IsMainCode = isMainCode,
            IsDefault = BoolAttr(element, "IsDefault", false)
        };

        foreach (var attribute in element.Attributes())
        {
            option.Attributes[attribute.Name.LocalName] = attribute.Value;
        }

        return option;
    }

    private static string Attr(XElement element, string name) => element.Attribute(name)?.Value ?? "";

    private static string Attr(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = element.Attribute(name)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private static string? NullableAttr(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool BoolAttr(XElement element, string name, bool defaultValue)
    {
        var value = element.Attribute(name)?.Value;
        return value is null ? defaultValue : bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static int IntAttr(XElement element, string name, int defaultValue)
    {
        var value = element.Attribute(name)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static IEnumerable<string> SplitCsv(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal static class Rex615EnglishTextCatalog
{
    private static readonly Dictionary<string, string> GroupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REX615产品"] = "REX615 product",
        ["机箱"] = "Housing",
        ["产品版本"] = "Product version",
        ["接口级别"] = "Interface level",
        ["选项1"] = "Option 1",
        ["保形涂层"] = "Conformal coating",
        ["通讯模块"] = "Communication module",
        ["开关量模块"] = "Binary I/O module",
        ["模拟量模块"] = "Analog input module",
        ["RTD模块"] = "RTD module",
        ["电源模块"] = "Power supply module",
        ["HMI"] = "HMI",
        ["应用包"] = "Application package",
        ["通讯规约"] = "Communication protocol",
        ["信号端子"] = "Signal connectors",
        ["版本"] = "Product connectivity level"
    };

    private static readonly Dictionary<string, string> OptionDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REX615产品:REX615"] = "REX615 protection and control relay",
        ["机箱:A"] = "Standard case",
        ["机箱:B"] = "Wide case, more I/O's",
        ["机箱:C"] = "Wide case, more currents and voltages",
        ["产品版本:1"] = "Product version",
        ["接口级别:0"] = "Interface level",
        ["选项1:G"] = "Global language package: English, Chinese, Croatian, Czech, Finnish, French, German, Hungarian, Italian, Polish, Portuguese, Spanish, Swedish, Turkish",
        ["选项1:C"] = "China language package: Simplified Chinese",
        ["保形涂层:N"] = "Without conformal coating",
        ["保形涂层:C"] = "With conformal coating",
        ["HMI:HMI1"] = "English LHMI panel",
        ["HMI:HMI2"] = "Chinese LHMI panel",
        ["应用包:APP1"] = "APP1 - Current protection package",
        ["应用包:APP2"] = "APP2 - Earth-fault protection extension package",
        ["应用包:APP3"] = "APP3 - Feeder protection extension package",
        ["应用包:APP4"] = "APP4 - Fault locator package",
        ["应用包:APP5"] = "APP5 - Voltage protection package",
        ["应用包:APP6"] = "APP6 - Line differential protection package (requires line differential communication module)",
        ["应用包:APP7"] = "APP7 - Shunt capacitor protection package",
        ["应用包:APP8"] = "APP8 - Interconnection protection package",
        ["应用包:APP9"] = "APP9 - Motor protection package",
        ["应用包:ADD1"] = "ADD1 - Synchronous motor add-on package",
        ["应用包:ADD2"] = "ADD2 - Motor differential add-on package",
        ["应用包:APP10"] = "APP10 - Power transformer protection package",
        ["应用包:APP11"] = "APP11 - Busbar protection package",
        ["应用包:APP12"] = "APP12 - On-load tap changer control package",
        ["信号端子:SCT1"] = "SCT1 - Compression type signal connectors",
        ["信号端子:SCT2"] = "SCT2 - Ring-lug type signal connectors",
        ["版本:PCL1"] = "PCL1",
        ["版本:PCL2"] = "PCL2"
    };

    public static string GroupName(string groupName) =>
        GroupNames.TryGetValue(groupName, out var englishName) ? englishName : groupName;

    public static string OptionDescription(string groupName, string optionId, string description, string shortDescription)
    {
        if (OptionDescriptions.TryGetValue($"{groupName}:{optionId}", out var englishDescription))
        {
            return englishDescription;
        }

        if (groupName.Equals("通讯模块", StringComparison.OrdinalIgnoreCase))
        {
            return PrefixCode(optionId, PreferEnglish(shortDescription, description));
        }

        if (groupName.Equals("开关量模块", StringComparison.OrdinalIgnoreCase))
        {
            return $"Binary I/O module: {AfterColon(PreferEnglish(shortDescription, description))}";
        }

        if (groupName.Equals("模拟量模块", StringComparison.OrdinalIgnoreCase))
        {
            return $"{(optionId.StartsWith("SIM", StringComparison.OrdinalIgnoreCase) ? "Sensor input module" : "Analog input module")}: {AfterColon(PreferEnglish(shortDescription, description))}";
        }

        if (groupName.Equals("RTD模块", StringComparison.OrdinalIgnoreCase))
        {
            return $"RTD module: {AfterColon(PreferEnglish(shortDescription, description))}";
        }

        return PreferEnglish(shortDescription, description);
    }

    public static string OptionShortDescription(string groupName, string optionId, string shortDescription, string description)
    {
        if (!string.IsNullOrWhiteSpace(shortDescription) && !ContainsCjk(shortDescription))
        {
            return shortDescription;
        }

        if (OptionDescriptions.TryGetValue($"{groupName}:{optionId}", out var englishDescription))
        {
            return englishDescription;
        }

        return OptionDescription(groupName, optionId, description, shortDescription);
    }

    private static string PreferEnglish(string primary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary) && !ContainsCjk(primary))
        {
            return primary;
        }

        return !string.IsNullOrWhiteSpace(fallback) && !ContainsCjk(fallback) ? fallback : primary;
    }

    private static string PrefixCode(string optionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return optionId;
        }

        return text.StartsWith(optionId, StringComparison.OrdinalIgnoreCase) ? text : $"{optionId} - {text}";
    }

    private static string AfterColon(string value)
    {
        var index = value.IndexOf(':', StringComparison.Ordinal);
        return index >= 0 && index + 1 < value.Length ? value[(index + 1)..].Trim() : value;
    }

    private static bool ContainsCjk(string value) =>
        value.Any(character => character is >= '\u4e00' and <= '\u9fff');
}
