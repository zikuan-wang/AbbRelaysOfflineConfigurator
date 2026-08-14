using System.Text.RegularExpressions;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Ssc600FunctionCatalogService
{
    private static readonly Lazy<IReadOnlyList<Ssc600FunctionEntry>> SharedFunctions = new(BuildFunctions);
    private static IReadOnlyList<Ssc600FunctionEntry> Functions => SharedFunctions.Value;

    public IReadOnlyList<Ssc600FunctionEntry> Search(string query, int limit = 20)
    {
        var token = Normalize(query);
        if (token.Length == 0)
        {
            return [];
        }

        return Functions
            .Select(function => new { Function = function, Score = MatchScore(function, token) })
            .Where(item => item.Score < int.MaxValue)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Function.Code, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(item => item.Function)
            .ToList();
    }

    public Ssc600FunctionEntry? ResolveExact(string query)
    {
        var token = Normalize(query);
        if (token.Length == 0)
        {
            return null;
        }

        var matches = Functions
            .Where(function =>
                IsCodeMatch(function.Code, token) ||
                ExpandAnsiSearchTerms(function.Ansi).Any(ansi => Normalize(ansi) == token))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public IReadOnlyList<Ssc600FunctionEntry> GetFunctions() => Functions;

    public Ssc600RecommendationResult Recommend(IReadOnlyCollection<string> functionCodes)
    {
        var functions = functionCodes
            .Select(code => Functions.FirstOrDefault(function => function.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            .Where(function => function is not null)
            .Cast<Ssc600FunctionEntry>()
            .DistinctBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var baseFunctions = functions.Where(function => function.Requirements.Count == 0).ToList();
        var recommendations = functions
            .SelectMany(function => function.Requirements.Select(requirement => (Function: function, Requirement: requirement)))
            .GroupBy(item => item.Requirement.GroupName, StringComparer.OrdinalIgnoreCase)
            .Select(group => ResolveGroupRecommendation(group.Key, group.Select(item => item.Requirement.OptionKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
                group.Select(item => item.Function.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(item => item is not null)
            .Cast<Ssc600PackageRecommendation>()
            .OrderBy(item => item.SortOrder)
            .ToList();

        return new Ssc600RecommendationResult(recommendations, baseFunctions);
    }

    private static Ssc600PackageRecommendation? ResolveGroupRecommendation(string groupName, ISet<string> optionKeys, IReadOnlyList<string> coveredFunctions)
    {
        return groupName switch
        {
            "MainApps" => Recommendation("MainApps", "B", "主应用包 B", 3, coveredFunctions),
            "FunctionalApps" => Recommendation("FunctionalApps", "A", "线路/电缆应用包 A（5 个）", 4, coveredFunctions),
            "Aios" => Recommendation("Aios", "A", "高级线路/电缆应用包 A（5 个）", 5, coveredFunctions),
            "Bios" => Recommendation("Bios", AdditionalOption(optionKeys), AdditionalText(optionKeys), 6, coveredFunctions),
            "CommSerials" => Recommendation("CommSerials", "A", "变压器应用包 A（2 个）", 7, coveredFunctions),
            "CommEthernets" => Recommendation("CommEthernets", "A", "电动机应用包 A（5 个）", 8, coveredFunctions),
            "CommProtocols" => Recommendation("CommProtocols", "1", "过程总线连接 1（最多 5 个合并单元/继电器）", 9, coveredFunctions),
            "PowerSupplies" => Recommendation("PowerSupplies", SingleBayOption(optionKeys), SingleBayText(optionKeys), 14, coveredFunctions),
            "Reserved" => Recommendation("Reserved", MultiBayOption(optionKeys), MultiBayText(optionKeys), 15, coveredFunctions),
            _ => null
        };
    }

    private static Ssc600PackageRecommendation Recommendation(string groupName, string optionId, string displayText, int sortOrder, IReadOnlyList<string> coveredFunctions) =>
        new(groupName, optionId, displayText, sortOrder, coveredFunctions);

    private static string AdditionalOption(ISet<string> keys)
    {
        var hasCapacitor = keys.Contains("Capacitor");
        var hasLineDiff = keys.Contains("LineDiff");
        return (hasCapacitor, hasLineDiff) switch
        {
            (true, true) => "C",
            (true, false) => "A",
            (false, true) => "B",
            _ => "N"
        };
    }

    private static string AdditionalText(ISet<string> keys) => AdditionalOption(keys) switch
    {
        "A" => "附加应用包 A（并联电容器保护）",
        "B" => "附加应用包 B（线路差动保护）",
        "C" => "附加应用包 C（并联电容器保护 + 线路差动保护）",
        _ => "无附加应用包"
    };

    private static string SingleBayOption(ISet<string> keys)
    {
        var pq = keys.Contains("PowerQuality");
        var voltage = keys.Contains("VoltageRegulation");
        var distance = keys.Contains("Distance");
        return (pq, voltage, distance) switch
        {
            (true, true, true) => "G",
            (true, true, false) => "D",
            (true, false, true) => "E",
            (false, true, true) => "F",
            (true, false, false) => "A",
            (false, true, false) => "B",
            (false, false, true) => "C",
            _ => "N"
        };
    }

    private static string SingleBayText(ISet<string> keys) => SingleBayOption(keys) switch
    {
        "A" => "特殊单间隔应用包 A（电能质量）",
        "B" => "特殊单间隔应用包 B（电压调节）",
        "C" => "特殊单间隔应用包 C（距离保护）",
        "D" => "特殊单间隔应用包 D（电能质量 + 电压调节）",
        "E" => "特殊单间隔应用包 E（电能质量 + 距离保护）",
        "F" => "特殊单间隔应用包 F（电压调节 + 距离保护）",
        "G" => "特殊单间隔应用包 G（电能质量 + 电压调节 + 距离保护）",
        _ => "无特殊单间隔应用包"
    };

    private static string MultiBayOption(ISet<string> keys)
    {
        var arc = keys.Contains("Arc");
        var shedding = keys.Contains("LoadShedding");
        var busbar = keys.Contains("Busbar");
        return (arc, shedding, busbar) switch
        {
            (true, true, true) => "G",
            (true, true, false) => "C",
            (true, false, true) => "E",
            (false, true, true) => "F",
            (true, false, false) => "A",
            (false, true, false) => "B",
            (false, false, true) => "D",
            _ => "N"
        };
    }

    private static string MultiBayText(ISet<string> keys) => MultiBayOption(keys) switch
    {
        "A" => "特殊多间隔应用包 A（弧光保护）",
        "B" => "特殊多间隔应用包 B（低频减载）",
        "C" => "特殊多间隔应用包 C（弧光保护 + 低频减载）",
        "D" => "特殊多间隔应用包 D（母线差动保护）",
        "E" => "特殊多间隔应用包 E（弧光保护 + 母线差动保护）",
        "F" => "特殊多间隔应用包 F（低频减载 + 母线差动保护）",
        "G" => "特殊多间隔应用包 G（弧光保护 + 低频减载 + 母线差动保护）",
        _ => "无特殊多间隔应用包"
    };

    private static int MatchScore(Ssc600FunctionEntry function, string token)
    {
        var code = Normalize(function.Code);
        if (IsCodeMatch(function.Code, token))
        {
            return 0;
        }

        if (ExpandAnsiSearchTerms(function.Ansi).Any(ansi => Normalize(ansi) == token))
        {
            return 1;
        }

        var ansiTerms = ExpandAnsiSearchTerms(function.Ansi).Select(Normalize).ToList();
        if (IsNumericAnsiToken(token) && ansiTerms.Any(ansi => ansi.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        if (ansiTerms.Any(ansi => ansi.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        if (code.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith(code, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (Normalize(function.ChineseName).Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (function.ChineseAliases.Any(alias => Normalize(alias).Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 6;
        }

        if (Normalize(function.EnglishName).Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 7;
        }

        return int.MaxValue;
    }

    private static bool IsNumericAnsiToken(string token) =>
        Regex.IsMatch(token, @"^\d+[A-Z_]*$", RegexOptions.CultureInvariant);

    private static IEnumerable<string> ExpandAnsiSearchTerms(string ansi)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in Regex.Split(ansi ?? "", @"[\s,，、]+"))
        {
            var term = raw.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            AddAnsiTerm(terms, term);
            var slashParts = term.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (slashParts.Length <= 1)
            {
                continue;
            }

            var numericPrefix = Regex.Match(slashParts[0], @"^\d+").Value;
            foreach (var part in slashParts)
            {
                var expanded = Regex.IsMatch(part, @"^\d")
                    ? part
                    : string.IsNullOrWhiteSpace(numericPrefix) ? part : numericPrefix + part;
                AddAnsiTerm(terms, expanded);
            }
        }

        return terms;
    }

    private static void AddAnsiTerm(ISet<string> terms, string term)
    {
        var cleaned = Regex.Replace(term.Trim(), @"[^\w/>.\-]", "");
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return;
        }

        terms.Add(cleaned);
        terms.Add(cleaned.Replace("/", "", StringComparison.Ordinal));
        var hyphenIndex = cleaned.IndexOf('-', StringComparison.Ordinal);
        if (hyphenIndex > 0)
        {
            terms.Add(cleaned[..hyphenIndex]);
        }
    }

    private static string Normalize(string value) =>
        Regex.Replace((value ?? "").Trim(), @"\s+", "", RegexOptions.CultureInvariant).ToUpperInvariant();

    private static bool IsCodeMatch(string code, string normalizedToken)
    {
        var normalizedCode = Normalize(code);
        return normalizedCode == normalizedToken ||
               Regex.Replace(normalizedToken, @"\d+$", "", RegexOptions.CultureInvariant) == normalizedCode;
    }

    private static IReadOnlyList<Ssc600FunctionEntry> BuildFunctions()
    {
        static Ssc600FunctionEntry F(string code, string ansi, string english, string chinese, string category, params Ssc600PackageRequirement[] requirements) =>
            new(code, ansi, english, chinese, category, requirements.ToList());

        static Ssc600PackageRequirement R(string group, string optionKey) => new(group, optionKey);

        return
        [
            F("PHLPTOC", "51P-1", "Three-phase non-directional overcurrent protection, low stage", "三相无方向过流保护，低定值段", "基础功能"),
            F("PHHPTOC", "51P-2", "Three-phase non-directional overcurrent protection, high stage", "三相无方向过流保护，高定值段", "基础功能"),
            F("PHIPTOC", "50P", "Three-phase non-directional overcurrent protection, instantaneous stage", "三相无方向过流保护，瞬时段", "基础功能"),
            F("EFLPTOC", "51G/51N-1", "Non-directional earth-fault protection, low stage", "无方向接地故障保护，低定值段", "基础功能"),
            F("EFHPTOC", "51G/51N-2", "Non-directional earth-fault protection, high stage", "无方向接地故障保护，高定值段", "基础功能"),
            F("EFIPTOC", "50G/50N", "Non-directional earth-fault protection, instantaneous stage", "无方向接地故障保护，瞬时段", "基础功能"),
            F("NSPTOC", "46M", "Negative-sequence overcurrent protection", "负序过流保护", "基础功能"),
            F("ROVPTOV", "59G/59N", "Residual overvoltage protection", "零序过压保护", "基础功能"),
            F("PHPTUV", "27", "Three-phase undervoltage protection", "三相低电压保护", "基础功能"),
            F("PHPTOV", "59", "Three-phase overvoltage protection", "三相过电压保护", "基础功能"),
            F("PSPTUV", "27PS", "Positive-sequence undervoltage protection", "正序低电压保护", "基础功能"),
            F("NSPTOV", "59NS", "Negative-sequence overvoltage protection", "负序过电压保护", "基础功能"),
            F("FRPFRQ", "81", "Frequency protection", "频率保护", "基础功能"),
            F("CCBRBRF", "50BF", "Circuit breaker failure protection", "断路器失灵保护", "基础功能"),
            F("INRPHAR", "68HB", "Three-phase inrush detector", "三相励磁涌流检测", "基础功能"),
            F("CBPSOF", "SOTF", "Switch onto fault", "合闸于故障保护", "基础功能"),
            F("TRPPTRC", "94/86", "Master trip", "总跳闸", "基础功能"),
            F("MAPGAPC", "MAP", "Multipurpose analog protection", "多用途模拟量保护", "基础功能"),
            F("ANOGAPC", "ANOGAPC", "Anomaly detector", "异常检测", "主应用包", R("MainApps", "B")),
            F("SMVRECEIVE", "SVRECEIVE", "Function related to SMV stream receiver", "SMV 采样值接收", "过程总线", R("CommProtocols", "ProcessBus")),

            F("DPHLPDOC", "67P/51P-1", "Three-phase directional overcurrent protection, low stage", "三相方向过流保护，低定值段", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("DPHHPDOC", "67P/51P-2", "Three-phase directional overcurrent protection, high stage", "三相方向过流保护，高定值段", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("DEFLPDEF", "67G/N-1 51G/N-1", "Directional earth-fault protection, low stage", "方向接地故障保护，低定值段", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("DEFHPDEF", "67G/N-2 51G/N-2", "Directional earth-fault protection, high stage", "方向接地故障保护，高定值段", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("DOPPDPR", "32R/32O", "Reverse power/directional overpower protection", "反向功率/方向过功率保护", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("PDNSPTOC", "46PD", "Phase discontinuity protection", "断相保护", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("T1PTTR", "49F", "Three-phase thermal protection for feeders, cables and distribution transformers", "馈线、电缆和配电变压器三相热保护", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("FPIPTOC", "67NFPI", "Fault passage indicator", "故障通道指示", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("DARREC", "79", "Autoreclosing", "自动重合闸", "线路/电缆应用包", R("FunctionalApps", "CableLine")),
            F("SECRSYN", "25", "Synchronism and energizing check", "同期和带电检查", "线路/电缆应用包", R("FunctionalApps", "CableLine")),

            F("EFPADM", "21YN", "Admittance based earth-fault protection", "基于导纳的接地故障保护", "高级线路/电缆应用包", R("Aios", "AdvancedCableLine")),
            F("MFADPSDE", "67NYH", "Multi-frequency admittance-based earth-fault protection", "多频导纳接地故障保护", "高级线路/电缆应用包", R("Aios", "AdvancedCableLine")),
            F("WPWDE", "32N", "Wattmetric based earth-fault protection", "有功功率方向接地故障保护", "高级线路/电缆应用包", R("Aios", "AdvancedCableLine")),
            F("INTRPTEF", "67NTEF/NIEF", "Transient/intermittent earth-fault protection", "暂态/间歇性接地故障保护", "高级线路/电缆应用包", R("Aios", "AdvancedCableLine")),
            F("SCEFRFLO", "FLOC", "Fault locator", "故障定位", "高级线路/电缆应用包", R("Aios", "AdvancedCableLine")),
            F("DQPTUV", "32Q,27", "Directional reactive power undervoltage protection", "方向无功低电压保护", "高级线路/电缆应用包", R("Aios", "AdvancedCableLine")),
            F("LVRTPTUV", "27RT", "Low-voltage ride through protection", "低电压穿越保护", "高级线路/电缆应用包", R("Aios", "AdvancedCableLine")),

            F("T2PTTR", "49T/G/C", "Three-phase thermal overload protection for power transformers", "电力变压器三相热过载保护", "变压器应用包", R("CommSerials", "Transformer")),
            F("TR2PTDF", "87T", "Stabilized and instantaneous differential protection for two-winding transformers", "双绕组变压器稳态和瞬时差动保护", "变压器应用包", R("CommSerials", "Transformer")),
            F("LREFPNDF", "87NLI", "Numerical stabilized low-impedance restricted earth-fault protection", "数值稳态低阻抗限制接地故障保护", "变压器应用包", R("CommSerials", "Transformer")),
            F("TPOSYLTC", "84M", "Tap changer position indication", "分接开关位置指示", "变压器应用包", R("CommSerials", "Transformer")),

            F("MNSPTOC", "46M", "Negative-sequence overcurrent protection for motors", "电动机负序过流保护", "电动机应用包", R("CommEthernets", "Motor")),
            F("LOFLPTUC", "37", "Loss of load supervision", "失载监视", "电动机应用包", R("CommEthernets", "Motor")),
            F("JAMPTOC", "50TDJAM", "Motor load jam protection", "电动机堵转保护", "电动机应用包", R("CommEthernets", "Motor")),
            F("STTPMSU", "49,66,48,50TDLR", "Motor start-up supervision", "电动机启动监视", "电动机应用包", R("CommEthernets", "Motor")),
            F("PREVPTOC", "46R", "Phase reversal protection", "相序反转保护", "电动机应用包", R("CommEthernets", "Motor")),
            F("MPTTR", "49T/G/C", "Thermal overload protection for motors", "电动机热过载保护", "电动机应用包", R("CommEthernets", "Motor")),

            F("COLPTOC", "51,37,86C", "Three-phase overload protection for shunt capacitor banks", "并联电容器组三相过载保护", "附加应用包", R("Bios", "Capacitor")),
            F("CUBPTOC", "60N", "Current unbalance protection for shunt capacitor banks", "并联电容器组电流不平衡保护", "附加应用包", R("Bios", "Capacitor")),
            F("HCUBPTOC", "60P", "Three-phase current unbalance protection for shunt capacitor banks", "并联电容器组三相电流不平衡保护", "附加应用包", R("Bios", "Capacitor")),
            F("SRCPTOC", "55ITHD", "Shunt capacitor bank switching resonance protection, current based", "并联电容器组投切谐振保护（基于电流）", "附加应用包", R("Bios", "Capacitor")),
            F("LN2PDIF", "87L", "Line differential protection", "线路差动保护", "附加应用包", R("Bios", "LineDiff")),

            F("OLATCC", "90", "Tap changer control with voltage regulator", "带电压调节器的分接开关控制", "特殊单间隔应用包", R("PowerSupplies", "VoltageRegulation")),
            F("DSTPDIS", "21P,21N", "Distance protection", "距离保护", "特殊单间隔应用包", R("PowerSupplies", "Distance")),
            F("CH00MHAI", "PQM3I", "Current total demand and harmonic distortion", "电流总需量畸变和谐波畸变", "特殊单间隔应用包", R("PowerSupplies", "PowerQuality")),
            F("VH00MHAI", "PQM3V", "Voltage total harmonic distortion", "电压总谐波畸变", "特殊单间隔应用包", R("PowerSupplies", "PowerQuality")),
            F("PHQVVR", "PQMV", "Voltage variation", "电压波动", "特殊单间隔应用包", R("PowerSupplies", "PowerQuality")),
            F("VSQVUB", "PQVUB", "Voltage unbalance", "电压不平衡", "特殊单间隔应用包", R("PowerSupplies", "PowerQuality")),

            F("ARCSARC", "AFD", "Arc flash protection", "弧光保护", "特殊多间隔应用包", R("Reserved", "Arc")),
            F("LSHDPFRQ", "81LSH", "Load shedding and restoration across 4 bus sections", "4 段母线低频减载和恢复", "特殊多间隔应用包", R("Reserved", "LoadShedding")),
            F("BBPBDF", "87BL", "Busbar differential protection", "母线差动保护", "特殊多间隔应用包", R("Reserved", "Busbar")),
            F("ZNRCRC", "ZNRSRC", "Busbar zone selection", "母线区域选择", "特殊多间隔应用包", R("Reserved", "Busbar"))
        ];
    }
}

public sealed record Ssc600FunctionEntry(
    string Code,
    string Ansi,
    string EnglishName,
    string ChineseName,
    string Category,
    IReadOnlyList<Ssc600PackageRequirement> Requirements)
{
    public IReadOnlyList<string> ChineseAliases { get; init; } = [];
    public bool IsBase => Requirements.Count == 0;
}

public sealed record Ssc600PackageRequirement(string GroupName, string OptionKey);

public sealed record Ssc600PackageRecommendation(
    string GroupName,
    string OptionId,
    string DisplayText,
    int SortOrder,
    IReadOnlyList<string> CoveredFunctions);

public sealed record Ssc600RecommendationResult(
    IReadOnlyList<Ssc600PackageRecommendation> Recommendations,
    IReadOnlyList<Ssc600FunctionEntry> BaseFunctions);
