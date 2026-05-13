using System.Globalization;
using System.Text;
using AbbRelaysOfflineConfigurator.Models;
using AbbRelaysOfflineConfigurator.Services;

const string version = "PCL1";

var root = FindWorkspaceRoot();
var dataPath = Path.Combine(root, "AbbRelaysOfflineConfigurator", "Data", "REX615_ROL.xml");
var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? Path.Combine(root, "Generated", $"REX615_{version}_CombinationCodes.txt");
var writeAll = args.Any(arg => arg.Equals("--write-all", StringComparison.OrdinalIgnoreCase));
var maxLines = GetLongArg(args, "--max-lines", 1_000_000);

var rules = new ProductRuleLoader().Load(dataPath);
var context = new ExportContext(rules);
if (args.Any(arg => arg.Equals("--debug-hardware", StringComparison.OrdinalIgnoreCase)))
{
    context.DebugHardware("C", version);
    return;
}

var hardwareByHousing = new Dictionary<string, List<HardwarePart>>(StringComparer.OrdinalIgnoreCase);
foreach (var housing in new[] { "A", "B", "C" })
{
    hardwareByHousing[housing] = context.BuildHardwareParts(housing, version);
}

var otherCountByCustomer = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
var otherSampleByCustomer = new Dictionary<string, List<OtherPart>>(StringComparer.OrdinalIgnoreCase);
foreach (var customer in new[] { "G", "C" })
{
    var count = 0L;
    var samples = new List<OtherPart>();
    foreach (var part in context.EnumerateOtherParts(customer, version))
    {
        count++;
        if (samples.Count < 100)
        {
            samples.Add(part);
        }
    }

    otherCountByCustomer[customer] = count;
    otherSampleByCustomer[customer] = samples;
}

var mainCodes = context.BuildMainCodes(version).ToList();
var totalCount = mainCodes.Sum(main =>
    (long)hardwareByHousing[main.Housing].Count * otherCountByCustomer[main.Customer]);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

if (!writeAll && totalCount > maxLines)
{
    WriteSummary(outputPath, totalCount, hardwareByHousing, otherCountByCustomer, otherSampleByCustomer, mainCodes, context);
    WriteIndexFiles(outputPath, hardwareByHousing, mainCodes, context);
    Console.WriteLine($"组合数量 {totalCount:N0} 超过 --max-lines={maxLines:N0}，已生成摘要文件：{outputPath}");
    Console.WriteLine($"已生成完整组合索引文件：{Path.GetDirectoryName(outputPath)}");
    Console.WriteLine("如确需完整列表，请重新运行并添加 --write-all。");
    return;
}

var otherByCustomer = otherSampleByCustomer.ToDictionary(
    pair => pair.Key,
    pair => context.EnumerateOtherParts(pair.Key, version).ToList(),
    StringComparer.OrdinalIgnoreCase);

await using var stream = File.Create(outputPath);
await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
await writer.WriteLineAsync("CombinationCode");

long written = 0;
foreach (var main in mainCodes)
{
    foreach (var hardware in hardwareByHousing[main.Housing])
    {
        foreach (var other in otherByCustomer[main.Customer])
        {
            await writer.WriteLineAsync(BuildFullCode(main.Code, other.ApplicationCodes, hardware.Codes, other.TailCodes));
            written++;
        }
    }
}

Console.WriteLine($"已生成 {written:N0} 条组合代码：{outputPath}");

static string FindWorkspaceRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "REX615_ROL.xml")) &&
            Directory.Exists(Path.Combine(current.FullName, "AbbRelaysOfflineConfigurator")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static long GetLongArg(string[] args, string name, long fallback)
{
    foreach (var arg in args)
    {
        if (!arg.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return long.TryParse(arg[(name.Length + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    return fallback;
}

static string BuildFullCode(string mainCode, IReadOnlyList<string> applicationCodes, IReadOnlyList<string> hardwareCodes, IReadOnlyList<string> tailCodes)
{
    var parts = applicationCodes.Concat(hardwareCodes).Concat(tailCodes).ToList();
    return parts.Count == 0 ? mainCode : $"{mainCode}+{string.Join("+", parts)}";
}

static void WriteSummary(
    string outputPath,
    long totalCount,
    IReadOnlyDictionary<string, List<HardwarePart>> hardwareByHousing,
    IReadOnlyDictionary<string, long> otherCountByCustomer,
    IReadOnlyDictionary<string, List<OtherPart>> otherSampleByCustomer,
    IReadOnlyCollection<MainPart> mainCodes,
    ExportContext context)
{
    using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    writer.WriteLine($"REX615 {version} 组合代码枚举摘要");
    writer.WriteLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    writer.WriteLine($"完整组合数量：{totalCount:N0}");
    writer.WriteLine();
    writer.WriteLine("按机箱统计的有效硬件组合数量：");
    foreach (var pair in hardwareByHousing.OrderBy(pair => pair.Key))
    {
        writer.WriteLine($"- 机箱 {pair.Key}: {pair.Value.Count:N0}");
    }

    writer.WriteLine();
    writer.WriteLine("按语言包统计的有效非硬件组合数量：");
    foreach (var pair in otherCountByCustomer.OrderBy(pair => pair.Key))
    {
        writer.WriteLine($"- 选项1 {pair.Key}: {pair.Value:N0}");
    }

    writer.WriteLine();
    writer.WriteLine("样例组合代码：");
    var samples = context.BuildSamples(mainCodes, hardwareByHousing, otherSampleByCustomer, 50);
    foreach (var sample in samples)
    {
        writer.WriteLine(sample);
    }
}

static void WriteIndexFiles(
    string outputPath,
    IReadOnlyDictionary<string, List<HardwarePart>> hardwareByHousing,
    IReadOnlyCollection<MainPart> mainCodes,
    ExportContext context)
{
    var directory = Path.GetDirectoryName(outputPath)!;
    var prefix = Path.Combine(directory, $"REX615_{version}");

    using (var writer = new StreamWriter($"{prefix}_MainCodes.csv", append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
    {
        writer.WriteLine("MainCode,Housing,Customer,Coating");
        foreach (var main in mainCodes)
        {
            writer.WriteLine($"{main.Code},{main.Housing},{main.Customer},{main.Coating}");
        }
    }

    using (var writer = new StreamWriter($"{prefix}_HardwareParts.csv", append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
    {
        writer.WriteLine("Housing,HardwareCodes");
        foreach (var pair in hardwareByHousing.OrderBy(pair => pair.Key))
        {
            foreach (var hardware in pair.Value)
            {
                writer.WriteLine($"{pair.Key},{string.Join("+", hardware.Codes)}");
            }
        }
    }

    foreach (var customer in new[] { "G", "C" })
    {
        using var writer = new StreamWriter($"{prefix}_OtherParts_{customer}.csv", append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("Customer,ApplicationCodes,TailCodes");
        foreach (var other in context.EnumerateOtherParts(customer, version))
        {
            writer.WriteLine($"{customer},{string.Join("+", other.ApplicationCodes)},{string.Join("+", other.TailCodes)}");
        }
    }

    using (var writer = new StreamWriter($"{prefix}_README.txt", append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
    {
        writer.WriteLine("REX615 PCL1 完整组合代码索引包");
        writer.WriteLine();
        writer.WriteLine("完整组合代码生成方式：");
        writer.WriteLine("FullCode = MainCode + '+' + ApplicationCodes + '+' + HardwareCodes + '+' + TailCodes");
        writer.WriteLine();
        writer.WriteLine("当 ApplicationCodes 为空时，省略该段和多余的加号。");
        writer.WriteLine("MainCodes.csv 按 Housing/Customer/Coating 区分主代码。");
        writer.WriteLine("HardwareParts.csv 按 Housing 区分硬件槽位段。");
        writer.WriteLine("OtherParts_G.csv / OtherParts_C.csv 按选项1语言包区分应用包、通讯、电源、面板、PCL、端子段。");
    }
}

internal sealed class ExportContext(ProductRuleSet rules)
{
    private readonly IReadOnlyList<OptionGroup> mainGroups = rules.MainGroups.OrderBy(group => group.SortOrder).ToList();
    private readonly IReadOnlyList<OptionGroup> optionGroups = rules.OptionGroups.OrderBy(group => group.SortOrder).ToList();

    public IEnumerable<MainPart> BuildMainCodes(string selectedVersion)
    {
        foreach (var housing in new[] { "A", "B", "C" })
        foreach (var customer in new[] { "G", "C" })
        foreach (var coating in new[] { "N", "C" })
        {
            var selected = BuildBaseMainOptions(housing, customer, coating);
            yield return new MainPart(BuildMainCode(selected), housing, customer, coating);
        }
    }

    public List<HardwarePart> BuildHardwareParts(string housing, string selectedVersion)
    {
        var digitalSubsets = CandidateSubsets("开关量模块", housing, selectedVersion, allowEmpty: true).ToList();
        var analogSubsets = CandidateSubsets("模拟量模块", housing, selectedVersion, allowEmpty: false).ToList();
        var rtdSubsets = CandidateSubsets("RTD模块", housing, selectedVersion, allowEmpty: true).ToList();
        var results = new Dictionary<string, HardwarePart>(StringComparer.OrdinalIgnoreCase);

        foreach (var digitalSet in digitalSubsets)
        foreach (var analogSet in analogSubsets)
        foreach (var rtdSet in rtdSubsets)
        {
            var selected = BuildValidationBase(housing, customer: "G", coating: "N", selectedVersion, includeDefaultHardware: false);
            selected.AddRange(digitalSet);
            selected.AddRange(analogSet);
            selected.AddRange(rtdSet);
            if (!AllOptionRulesPass(selected))
            {
                continue;
            }

            if (!TryBuildHardwareCodes(selected, housing, selectedVersion, out var codes))
            {
                continue;
            }

            var key = string.Join("+", codes);
            results.TryAdd(key, new HardwarePart(codes));
        }

        return results.Values.OrderBy(part => string.Join("+", part.Codes), StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IEnumerable<OtherPart> EnumerateOtherParts(string customer, string selectedVersion)
    {
        var communicationOptions = Options("通讯模块").ToList();
        var protocolOptions = Options("通讯规约").Where(option => !option.Id.Equals("CMP30", StringComparison.OrdinalIgnoreCase)).ToList();
        var psmOptions = Options("电源模块").ToList();
        var hmiOptions = Options("LHMI面板")
            .Where(hmi =>
            {
                var selected = BuildBaseMainOptions(housing: "A", customer, coating: "N");
                selected.Add(Option("版本", selectedVersion));
                selected.Add(hmi);
                return AllOptionRulesPass(selected);
            })
            .ToList();
        var applicationOptions = Options("应用包").ToList();
        var signalOptions = Options("信号端子").ToList();
        var applicationSubsets = Subsets(applicationOptions, allowEmpty: true)
            .Where(IsApplicationSubsetStructurallyValid)
            .ToList();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var communication in communicationOptions)
        foreach (var protocol in protocolOptions)
        foreach (var applicationSet in applicationSubsets)
        {
            var selected = BuildBaseMainOptions(housing: "A", customer, coating: "N");
            selected.Add(Option("版本", selectedVersion));
            selected.AddRange(applicationSet);
            selected.Add(communication);
            selected.Add(protocol);

            if (!AllOptionRulesPass(selected))
            {
                continue;
            }

            var applicationCodes = applicationSet.Select(option => option.Id).ToList();
            foreach (var psm in psmOptions)
            foreach (var hmi in hmiOptions)
            foreach (var signal in signalOptions)
            {
                var tailCodes = new List<string> { communication.Id, protocol.Id, hmi.Id, psm.Id, selectedVersion, signal.Id };
                var key = $"{string.Join("+", applicationCodes)}|{string.Join("+", tailCodes)}";
                if (emitted.Add(key))
                {
                    yield return new OtherPart(applicationCodes, tailCodes);
                }
            }
        }
    }

    public IEnumerable<string> BuildSamples(
        IEnumerable<MainPart> mainCodes,
        IReadOnlyDictionary<string, List<HardwarePart>> hardwareByHousing,
        IReadOnlyDictionary<string, List<OtherPart>> otherByCustomer,
        int count)
    {
        var emitted = 0;
        foreach (var main in mainCodes)
        {
            foreach (var hardware in hardwareByHousing[main.Housing].Take(3))
            {
                foreach (var other in otherByCustomer[main.Customer].Take(3))
                {
                    yield return BuildCode(main.Code, other.ApplicationCodes, hardware.Codes, other.TailCodes);
                    emitted++;
                    if (emitted >= count)
                    {
                        yield break;
                    }
                }
            }
        }
    }

    public void DebugHardware(string housing, string selectedVersion)
    {
        var digitalSubsets = CandidateSubsets("开关量模块", housing, selectedVersion, allowEmpty: true).ToList();
        var analogSubsets = CandidateSubsets("模拟量模块", housing, selectedVersion, allowEmpty: false).ToList();
        var rtdSubsets = CandidateSubsets("RTD模块", housing, selectedVersion, allowEmpty: true).ToList();
        Console.WriteLine($"digital={digitalSubsets.Count}, analog={analogSubsets.Count}, rtd={rtdSubsets.Count}");

        var rulesPass = 0;
        var slotPass = 0;
        foreach (var digitalSet in digitalSubsets)
        foreach (var analogSet in analogSubsets)
        foreach (var rtdSet in rtdSubsets)
        {
            var selected = BuildValidationBase(housing, customer: "G", coating: "N", selectedVersion, includeDefaultHardware: false);
            selected.AddRange(digitalSet);
            selected.AddRange(analogSet);
            selected.AddRange(rtdSet);
            if (!AllOptionRulesPass(selected))
            {
                continue;
            }

            rulesPass++;
            if (TryBuildHardwareCodes(selected, housing, selectedVersion, out var codes))
            {
                slotPass++;
                Console.WriteLine($"sample={string.Join("+", codes)}");
                return;
            }
        }

        Console.WriteLine($"rulesPass={rulesPass}, slotPass={slotPass}");
    }

    private List<RuleOption> BuildValidationBase(
        string housing,
        string customer,
        string coating,
        string selectedVersion,
        bool includeDefaultHardware)
    {
        var selected = BuildBaseMainOptions(housing, customer, coating);
        if (includeDefaultHardware)
        {
            selected.Add(Option("模拟量模块", "AIM16"));
        }

        selected.Add(Option("通讯模块", "COM11"));
        selected.Add(Option("通讯规约", "CMP2"));
        selected.Add(Option("电源模块", "PSM4"));
        selected.Add(Option("LHMI面板", customer.Equals("G", StringComparison.OrdinalIgnoreCase) ? "HMI2" : "HMI2"));
        selected.Add(Option("版本", selectedVersion));
        selected.Add(Option("信号端子", "SCT1"));
        return selected;
    }

    private IEnumerable<List<RuleOption>> CandidateSubsets(string groupName, string housing, string selectedVersion, bool allowEmpty)
    {
        var groupOptions = Options(groupName).ToList();
        foreach (var subset in Subsets(groupOptions, allowEmpty))
        {
            var selected = BuildBaseMainOptions(housing, customer: "G", coating: "N");
            selected.Add(Option("版本", selectedVersion));
            selected.AddRange(subset);
            if (AllOptionRulesPass(selected, includeMainOptions: false))
            {
                yield return subset;
            }
        }
    }

    private static bool IsApplicationSubsetStructurallyValid(IReadOnlyCollection<RuleOption> applicationSet)
    {
        var selected = applicationSet.Select(option => option.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Contains("ADD1") && selected.Contains("ADD2"))
        {
            return false;
        }

        if (selected.Contains("ADD1") && !selected.Contains("APP9"))
        {
            return false;
        }

        return !selected.Contains("ADD2") || selected.Contains("APP9");
    }

    private List<RuleOption> BuildBaseMainOptions(string housing, string customer, string coating)
    {
        return
        [
            Option("REX615产品", "REX615"),
            Option("机箱", housing),
            Option("产品版本", "1"),
            Option("接口级别", "0"),
            Option("选项1", customer),
            Option("保形涂层", coating)
        ];
    }

    private string BuildMainCode(IEnumerable<RuleOption> selected)
    {
        var selectedList = selected.ToList();
        return string.Concat(mainGroups
            .SelectMany(group => selectedList.Where(option => option.GroupName.Equals(group.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(option => option.Id));
    }

    private static string BuildCode(string mainCode, IReadOnlyList<string> applicationCodes, IReadOnlyList<string> hardwareCodes, IReadOnlyList<string> tailCodes)
    {
        var parts = applicationCodes.Concat(hardwareCodes).Concat(tailCodes).ToList();
        return parts.Count == 0 ? mainCode : $"{mainCode}+{string.Join("+", parts)}";
    }

    private IEnumerable<RuleOption> Options(string groupName) => Group(groupName).Options;

    private RuleOption Option(string groupName, string id) =>
        Options(groupName).First(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private OptionGroup Group(string groupName) =>
        mainGroups.Concat(optionGroups).First(group => group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

    private bool TryBuildHardwareCodes(
        IReadOnlyCollection<RuleOption> selectedOptions,
        string housingId,
        string selectedVersion,
        out List<string> codes)
    {
        codes = [];
        var constraints = rules.GetSlotConstraints(selectedVersion);
        if (!constraints.Housings.TryGetValue(housingId, out var housing))
        {
            return false;
        }

        var units = selectedOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.ModuleType) && option.ModuleCount > 0)
            .SelectMany(option => Enumerable.Range(0, option.ModuleCount)
                .Select(_ => new ModuleUnit(option.ModuleType!, option.Id)))
            .ToList();

        if (units.Any(unit => !housing.Slots.Any(slot => slot.Modules.Contains(unit.ModuleType))))
        {
            return false;
        }

        var orderedUnits = units
            .OrderBy(unit => housing.Slots.Count(slot => slot.Modules.Contains(unit.ModuleType)))
            .ThenBy(unit => unit.ModuleType, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var assignments = new Dictionary<string, List<ModuleUnit>>(StringComparer.OrdinalIgnoreCase);
        if (!TryAssign(orderedUnits, housing, assignments, index: 0) ||
            !ValidateRequirements(housing, assignments))
        {
            return false;
        }

        codes = housing.Slots
            .Where(slot => assignments.ContainsKey(slot.Id))
            .SelectMany(slot => assignments[slot.Id].Select(unit => new { slot, unit }))
            .OrderBy(item => item.slot.CodeOrder)
            .ThenBy(item => item.slot.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.unit.ModuleType)
            .ToList();
        return true;
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
        foreach (var slot in housing.Slots.Where(slot => slot.Modules.Contains(unit.ModuleType)))
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
        IReadOnlyDictionary<string, List<ModuleUnit>> assignments)
    {
        foreach (var requirement in housing.Requirements)
        {
            if (requirement.Type.Equals("AtLeastOne", StringComparison.OrdinalIgnoreCase))
            {
                var matched = requirement.Slots.Any(slot =>
                    assignments.TryGetValue(slot, out var units) &&
                    units.Any(unit => requirement.Modules.Contains(unit.ModuleType)));
                if (!matched)
                {
                    return false;
                }
            }
            else if (requirement.Type.Equals("SlotMustContain", StringComparison.OrdinalIgnoreCase))
            {
                var matched = requirement.Slot is not null &&
                    assignments.TryGetValue(requirement.Slot, out var units) &&
                    units.Any(unit => requirement.Modules.Contains(unit.ModuleType));
                if (!matched)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool AllOptionRulesPass(IEnumerable<RuleOption> selectedOptions, bool includeMainOptions = true)
    {
        var selected = selectedOptions.ToList();
        var selectedByGroup = BuildSelectedByGroup(selected);
        return selected.Where(option => includeMainOptions || !option.IsMainCode)
            .All(option => EvaluateExpression(option.Validity, selectedByGroup) &&
            (!option.Attributes.TryGetValue("Requires", out var requires) || EvaluateExpression(requires, selectedByGroup)));
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

    private static bool EvaluateExpression(string? expression, IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        return expression.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(condition => EvaluateCondition(condition, selectedByGroup));
    }

    private static bool EvaluateCondition(string condition, IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
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

    private static IEnumerable<List<RuleOption>> Subsets(IReadOnlyList<RuleOption> options, bool allowEmpty)
    {
        var count = 1 << options.Count;
        var start = allowEmpty ? 0 : 1;
        for (var mask = start; mask < count; mask++)
        {
            var subset = new List<RuleOption>();
            for (var index = 0; index < options.Count; index++)
            {
                if ((mask & (1 << index)) != 0)
                {
                    subset.Add(options[index]);
                }
            }

            yield return subset;
        }
    }
}

internal sealed record MainPart(string Code, string Housing, string Customer, string Coating);

internal sealed record HardwarePart(IReadOnlyList<string> Codes);

internal sealed record OtherPart(IReadOnlyList<string> ApplicationCodes, IReadOnlyList<string> TailCodes);

internal sealed record ModuleUnit(string ModuleType, string OptionId);
