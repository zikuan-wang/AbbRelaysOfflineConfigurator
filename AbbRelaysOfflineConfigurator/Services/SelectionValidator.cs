using AbbRelaysOfflineConfigurator.Models;

namespace AbbRelaysOfflineConfigurator.Services;

// REX615 选择结果的集中校验器。校验分为四层：组基数、选项条件、依赖条件和物理槽位；
// 返回值同时包含面向用户的错误消息与槽位展示快照，使界面无需自行重复推导装配结果。
public sealed class SelectionValidator(ProductRuleSet rules)
{
    public ValidationResult Validate(
        IReadOnlyCollection<RuleOption> selectedOptions,
        bool useFullDescription = false,
        bool useEnglishDescription = false)
    {
        var messages = new List<string>();
        // 后续表达式均按“组名 -> 已选代码集合”查询。统一建立索引可保证大小写规则一致，
        // 也避免各校验阶段反复遍历 ViewModel 选择集合。
        var selectedByGroup = BuildSelectedByGroup(selectedOptions);
        var selectedVersion = GetSelectedVersion(selectedByGroup);

        // 先报告最基础的缺选/多选，再检查跨组条件，最后执行成本更高的槽位搜索。
        // 即使已有错误仍继续生成槽位结果，界面才能在不完整选择下展示可诊断的当前状态。
        ValidateMandatoryGroups(rules.MainGroups, selectedVersion, selectedByGroup, messages);
        ValidateMandatoryGroups(rules.OptionGroups, selectedVersion, selectedByGroup, messages);
        ValidateValidityExpressions(selectedOptions, selectedByGroup, messages);
        ValidateRequiredExpressions(selectedOptions, selectedByGroup, messages);

        var assignments = ValidateSlotConstraints(selectedOptions, selectedByGroup, messages, useFullDescription, useEnglishDescription);

        return new ValidationResult(messages.Count == 0, messages, assignments);
    }

    private static Dictionary<string, HashSet<string>> BuildSelectedByGroup(IEnumerable<RuleOption> selectedOptions)
    {
        var selectedByGroup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in selectedOptions)
        {
            if (!selectedByGroup.TryGetValue(option.GroupName, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                selectedByGroup[option.GroupName] = set;
            }

            set.Add(option.Id);
        }

        return selectedByGroup;
    }

    private static void ValidateMandatoryGroups(
        IEnumerable<OptionGroup> groups,
        string? selectedVersion,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup,
        ICollection<string> messages)
    {
        foreach (var group in groups)
        {
            selectedByGroup.TryGetValue(group.Name, out var selected);
            var count = selected?.Count ?? 0;

            if (group.IsMandatory && count == 0)
            {
                messages.Add($"{group.Name} 必须选择至少一个选项。");
            }

            if (!group.AllowsMultiple(selectedVersion) && count > 1)
            {
                messages.Add($"{group.Name} 只能选择一个选项。");
            }
        }
    }

    private static void ValidateValidityExpressions(
        IEnumerable<RuleOption> selectedOptions,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup,
        ICollection<string> messages)
    {
        foreach (var option in selectedOptions)
        {
            if (string.IsNullOrWhiteSpace(option.Validity))
            {
                continue;
            }

            foreach (var condition in option.Validity.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!EvaluateCondition(condition, selectedByGroup))
                {
                    messages.Add($"{option.GroupName} / {option.Id} 不满足条件：{condition}");
                }
            }
        }
    }

    private static void ValidateRequiredExpressions(
        IEnumerable<RuleOption> selectedOptions,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup,
        ICollection<string> messages)
    {
        foreach (var option in selectedOptions)
        {
            if (!option.Attributes.TryGetValue("Requires", out var requires) || string.IsNullOrWhiteSpace(requires))
            {
                continue;
            }

            foreach (var condition in requires.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!EvaluateCondition(condition, selectedByGroup))
                {
                    messages.Add($"{option.GroupName} / {option.Id} 要求选择：{condition}");
                }
            }
        }
    }

    private static bool EvaluateCondition(string condition, IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
        // 规则语法为“组=候选1,候选2,!排除项”：正值之间是 OR，所有负值均不得出现；
        // 外层调用用 & 拆分多个条件，因此不同组条件之间是 AND。
        var parts = condition.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return true;
        }

        selectedByGroup.TryGetValue(parts[0], out var selected);
        selected ??= [];

        var values = parts[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        var positives = values.Where(value => !value.StartsWith('!')).ToArray();
        var negatives = values.Where(value => value.StartsWith('!')).Select(value => value[1..]).ToArray();

        if (positives.Length > 0 && !positives.Any(selected.Contains))
        {
            return false;
        }

        return negatives.All(value => !selected.Contains(value));
    }

    private IReadOnlyList<SlotAssignment> ValidateSlotConstraints(
        IReadOnlyCollection<RuleOption> selectedOptions,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup,
        ICollection<string> messages,
        bool useFullDescription,
        bool useEnglishDescription)
    {
        // 槽位规则随 PCL 版本切换；机箱选择确定要搜索的槽位集合，模块选项随后展开为物理单元。
        var slotConstraints = ResolveSlotConstraints(selectedByGroup, messages);
        if (!selectedByGroup.TryGetValue("机箱", out var housingSet) || housingSet.Count != 1)
        {
            messages.Add("无法校验槽位：必须先选择一个机箱。");
            return BuildFixedAssignments(selectedByGroup, useFullDescription, useEnglishDescription);
        }

        var housingId = housingSet.First();
        if (!slotConstraints.Housings.TryGetValue(housingId, out var housing))
        {
            messages.Add($"未找到机箱 {housingId} 的槽位约束。");
            return BuildFixedAssignments(selectedByGroup, useFullDescription, useEnglishDescription);
        }

        // 一个组合代码选项可能代表多个同型模块，必须按 ModuleCount 展开后再计算容量。
        var units = BuildModuleUnits(selectedOptions, useFullDescription, useEnglishDescription);
        var assignments = new Dictionary<string, List<ModuleUnit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var unit in units)
        {
            if (!housing.Slots.Any(slot => slot.Modules.Contains(unit.ModuleType)))
            {
                messages.Add($"{unit.OptionId} ({unit.ModuleType}) 不适用于机箱 {housingId}。");
            }
        }

        // 先放置可选槽位最少的模块以缩小回溯分支；具体槽位顺序仍由物理分配优先级决定。
        var orderedUnits = units
            .OrderBy(unit => housing.Slots.Count(slot => slot.Modules.Contains(unit.ModuleType)))
            .ThenBy(unit => unit.ModuleType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // TryAssign 找到任一满足模块兼容性与容量的完整放置方案即可；若失败，assignments 仅用于辅助展示，
        // 不能被当作一个有效的部分装配方案继续生成组合代码。
        if (!TryAssign(orderedUnits, housing, assignments, index: 0))
        {
            messages.Add($"当前模块组合无法装入 {housingId} 机箱槽位。");
            return BuildSlotAssignments(selectedByGroup, housing, assignments, useFullDescription, useEnglishDescription);
        }

        if (!ValidateRequirements(housing, assignments, messages))
        {
            return BuildSlotAssignments(selectedByGroup, housing, assignments, useFullDescription, useEnglishDescription);
        }

        return BuildSlotAssignments(selectedByGroup, housing, assignments, useFullDescription, useEnglishDescription);
    }

    private SlotConstraintSet ResolveSlotConstraints(
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup,
        ICollection<string> messages)
    {
        var selectedVersion = GetSelectedVersion(selectedByGroup);
        if (!string.IsNullOrWhiteSpace(selectedVersion) &&
            rules.SlotConstraintsByVersion.TryGetValue(selectedVersion, out var versionedConstraints))
        {
            return versionedConstraints;
        }

        if (!string.IsNullOrWhiteSpace(selectedVersion) && rules.SlotConstraintsByVersion.Count > 0)
        {
            messages.Add($"版本 / {selectedVersion} 未找到对应的槽位规则。");
        }

        // 首个约束集保留为旧调用或尚未选定版本时的兼容回退；
        // 若数据包提供了版本索引却缺少当前版本，上方仍会显式记录错误，避免静默掩盖数据缺口。
        return rules.SlotConstraints;
    }

    private static string? GetSelectedVersion(IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
        return selectedByGroup.TryGetValue("版本", out var versionSet) && versionSet.Count == 1
            ? versionSet.First()
            : null;
    }

    private List<ModuleUnit> BuildModuleUnits(
        IEnumerable<RuleOption> selectedOptions,
        bool useFullDescription,
        bool useEnglishDescription)
    {
        var units = new List<ModuleUnit>();
        foreach (var option in selectedOptions)
        {
            if (string.IsNullOrWhiteSpace(option.ModuleType) || option.ModuleCount <= 0)
            {
                continue;
            }

            for (var index = 1; index <= option.ModuleCount; index++)
            {
                units.Add(new ModuleUnit(
                    option.ModuleType,
                    option.GroupName,
                    option.Id,
                    index,
                    DisplaySlotDescription(option, useFullDescription, useEnglishDescription)));
            }
        }

        return units;
    }

    private string DisplaySlotDescription(RuleOption option, bool useFullDescription, bool useEnglishDescription)
    {
        return DisplayDescription(FindSingleModuleOption(option) ?? option, useFullDescription, useEnglishDescription);
    }

    private RuleOption? FindSingleModuleOption(RuleOption option)
    {
        if (option.ModuleCount <= 1 || string.IsNullOrWhiteSpace(option.ModuleType))
        {
            return null;
        }

        // 多数量选项只占一个选型代码；槽位表按物理模块逐项展示，因此优先复用同类型单模块选项的描述。
        return rules.OptionGroups
            .SelectMany(group => group.Options)
            .FirstOrDefault(candidate =>
                candidate.ModuleCount == 1 &&
                candidate.GroupName.Equals(option.GroupName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.ModuleType, option.ModuleType, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<SlotAssignment> BuildSlotAssignments(
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup,
        HousingConstraint housing,
        IReadOnlyDictionary<string, List<ModuleUnit>> assignments,
        bool useFullDescription,
        bool useEnglishDescription)
    {
        // 固定接口槽与可分配硬件槽合并为同一展示模型；空物理槽也保留，
        // 让槽位表始终完整反映机箱布局，而不是只列出已安装模块。
        var slots = BuildFixedAssignments(selectedByGroup, useFullDescription, useEnglishDescription).ToList();

        foreach (var slot in housing.Slots)
        {
            if (assignments.TryGetValue(slot.Id, out var units) && units.Count > 0)
            {
                foreach (var unit in units)
                {
                    slots.Add(new SlotAssignment(
                        slot.Id,
                        unit.ModuleType,
                        unit.Description,
                        unit.GroupName,
                        unit.OptionId,
                        IsFixed: false,
                        IsHardware: true,
                        IsAssigned: true,
                        slot.CodeOrder));
                }
            }
            else
            {
                slots.Add(new SlotAssignment(
                    slot.Id,
                    "N/A",
                    "Description N/A",
                    null,
                    null,
                    IsFixed: false,
                    IsHardware: true,
                    IsAssigned: false,
                    slot.CodeOrder));
            }
        }

        return slots;
    }

    private IReadOnlyList<SlotAssignment> BuildFixedAssignments(
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup,
        bool useFullDescription,
        bool useEnglishDescription)
    {
        // X000 通讯模块与 X100 电源模块由选项组直接决定，不参与可变硬件槽的回溯搜索。
        return
        [
            BuildFixedAssignment("X000", "通讯模块", useEnglishDescription ? "COM: not selected" : "COM: 未选择"),
            BuildFixedAssignment("X100", "电源模块", useEnglishDescription ? "PSM: not selected" : "PSM: 未选择")
        ];

        SlotAssignment BuildFixedAssignment(string slotId, string groupName, string emptyDescription)
        {
            var option = FindSelectedOption(selectedByGroup, groupName);
            return new SlotAssignment(
                slotId,
                option?.Id ?? "N/A",
                option is null
                    ? emptyDescription
                    : DisplayDescription(option, useFullDescription, useEnglishDescription),
                option?.GroupName,
                option?.Id,
                IsFixed: true,
                IsHardware: false,
                IsAssigned: option is not null,
                CodeOrder: 0);
        }
    }

    private RuleOption? FindSelectedOption(
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup,
        string groupName)
    {
        if (!selectedByGroup.TryGetValue(groupName, out var selected))
        {
            return null;
        }

        return selected.Select(id => rules.OptionsById.GetValueOrDefault(id)).FirstOrDefault(option => option is not null);
    }

    private static string DisplayDescription(RuleOption option, bool useFullDescription, bool useEnglishDescription)
    {
        if (useEnglishDescription)
        {
            return useFullDescription
                ? FirstNonEmpty(option.EnglishDescription, option.EnglishShortDescription, option.ShortDescription, option.Description)
                : FirstNonEmpty(option.EnglishShortDescription, option.ShortDescription, option.EnglishDescription, option.Description);
        }

        return useFullDescription
            ? FirstNonEmpty(option.Description, option.ShortDescription)
            : FirstNonEmpty(option.ShortDescription, option.Description);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static bool TryAssign(
        IReadOnlyList<ModuleUnit> units,
        HousingConstraint housing,
        Dictionary<string, List<ModuleUnit>> assignments,
        int index)
    {
        if (index == units.Count)
        {
            return true;
        }

        var unit = units[index];
        // ABB CodeOrder 仅控制组合代码输出顺序，不代表物理槽位优先级；这里必须使用 AssignmentPriority。
        // 每次尝试先写入 assignments，递归失败后再严格撤销；这一“选择-递归-回滚”保证其他分支
        // 看到的容量状态不受前一次失败尝试污染。
        foreach (var slot in housing.Slots
                     .Where(slot => slot.Modules.Contains(unit.ModuleType))
                     .OrderBy(slot => slot.AssignmentPriority))
        {
            if (!assignments.TryGetValue(slot.Id, out var used))
            {
                used = [];
                assignments[slot.Id] = used;
            }

            if (used.Count >= slot.Capacity)
            {
                continue;
            }

            used.Add(unit);
            if (TryAssign(units, housing, assignments, index + 1))
            {
                return true;
            }

            used.RemoveAt(used.Count - 1);
            if (used.Count == 0)
            {
                assignments.Remove(slot.Id);
            }
        }

        return false;
    }

    private static bool ValidateRequirements(
        HousingConstraint housing,
        IReadOnlyDictionary<string, List<ModuleUnit>> assignments,
        ICollection<string> messages)
    {
        // 兼容槽位和容量只回答“能否放下”；机箱 Requirement 还表达“必须放在哪里/至少包含什么”。
        // 因而它们必须在完整放置后统一检查，不能用搜索成功代替最终业务合法性。
        var valid = true;
        foreach (var requirement in housing.Requirements)
        {
            if (requirement.Type.Equals("AtLeastOne", StringComparison.OrdinalIgnoreCase))
            {
                var matched = requirement.Slots.Any(slot =>
                    assignments.TryGetValue(slot, out var units) &&
                    units.Any(unit => requirement.Modules.Contains(unit.ModuleType)));

                if (!matched)
                {
                    messages.Add($"{housing.Id} 机箱要求在 {string.Join("/", requirement.Slots)} 至少安装一个模拟量模块。");
                    valid = false;
                }
            }
            else if (requirement.Type.Equals("SlotMustContain", StringComparison.OrdinalIgnoreCase))
            {
                var matched = requirement.Slot is not null &&
                    assignments.TryGetValue(requirement.Slot, out var units) &&
                    units.Any(unit => requirement.Modules.Contains(unit.ModuleType));

                if (!matched)
                {
                    messages.Add($"{housing.Id} 机箱要求 {requirement.Slot} 安装：{string.Join(", ", requirement.Modules)}。");
                    valid = false;
                }
            }
        }

        return valid;
    }

    private sealed record ModuleUnit(string ModuleType, string GroupName, string OptionId, int Index, string Description);
}
