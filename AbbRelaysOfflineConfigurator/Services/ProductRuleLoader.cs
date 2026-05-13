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
        var option = new RuleOption
        {
            Id = Attr(element, "Id"),
            GroupName = groupName,
            Description = Attr(element, "Description"),
            ShortDescription = Attr(element, "ShortDescription"),
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
