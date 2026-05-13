using System.Collections.ObjectModel;

namespace AbbRelaysOfflineConfigurator.Models;

public sealed class ProductRuleSet
{
    public ObservableCollection<OptionGroup> MainGroups { get; } = [];
    public ObservableCollection<OptionGroup> OptionGroups { get; } = [];
    public SlotConstraintSet SlotConstraints { get; set; } = new();
    public Dictionary<string, SlotConstraintSet> SlotConstraintsByVersion { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, RuleOption> OptionsById { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SlotConstraintSet GetSlotConstraints(string? version)
    {
        if (!string.IsNullOrWhiteSpace(version) &&
            SlotConstraintsByVersion.TryGetValue(version, out var constraints))
        {
            return constraints;
        }

        return SlotConstraints;
    }

    public string SlotConstraintSourceSummary
    {
        get
        {
            if (SlotConstraintsByVersion.Count == 0)
            {
                return SlotConstraints.Source;
            }

            return string.Join(" / ", SlotConstraintsByVersion
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => string.IsNullOrWhiteSpace(pair.Value.Source)
                    ? pair.Key
                    : $"{pair.Key}: {pair.Value.Source}"));
        }
    }
}

public sealed class OptionGroup
{
    public string Name { get; init; } = "";
    public string? Location { get; init; }
    public bool IsMandatory { get; init; }
    public bool IsMultiple { get; init; }
    public bool IsMainCode { get; init; }
    public int SortOrder { get; init; }
    public ObservableCollection<RuleOption> Options { get; } = [];
    public Dictionary<string, bool> IsMultipleByVersion { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool AllowsMultiple(string? version)
    {
        if (!string.IsNullOrWhiteSpace(version) &&
            IsMultipleByVersion.TryGetValue(version, out var versionValue))
        {
            return versionValue;
        }

        return IsMultiple;
    }
}

public sealed class RuleOption
{
    public string Id { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string Description { get; init; } = "";
    public string ShortDescription { get; init; } = "";
    public string? Validity { get; init; }
    public string? ModuleType { get; init; }
    public int ModuleCount { get; init; }
    public bool IsMainCode { get; init; }
    public bool IsDefault { get; init; }
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SlotConstraintSet
{
    public string Version { get; set; } = "";
    public string Source { get; set; } = "";
    public Dictionary<string, HousingConstraint> Housings { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class HousingConstraint
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public List<SlotDefinition> Slots { get; } = [];
    public List<SlotRequirement> Requirements { get; } = [];
}

public sealed class SlotDefinition
{
    public string Id { get; init; } = "";
    public int Capacity { get; init; } = 1;
    public int CodeOrder { get; init; }
    public HashSet<string> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SlotRequirement
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Slot { get; init; }
    public HashSet<string> Slots { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record SlotAssignment(
    string SlotId,
    string Code,
    string Description,
    string? GroupName,
    string? OptionId,
    bool IsFixed,
    bool IsHardware,
    bool IsAssigned,
    int CodeOrder);

public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<string> Messages,
    IReadOnlyList<SlotAssignment> SlotAssignments);
