using System.Text.RegularExpressions;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Re611FunctionCatalogService
{
    private static readonly Lazy<IReadOnlyList<Re611FunctionEntry>> SharedFunctions = new(BuildFunctions);

    public IReadOnlyList<Re611FunctionEntry> GetFunctions(string? deviceId = null, string? versionCode = null)
    {
        var productVersion = VersionCodeToProductVersion(versionCode);
        return SharedFunctions.Value
            .Where(function =>
                (string.IsNullOrWhiteSpace(deviceId) || function.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(productVersion) || function.ProductVersion.Equals(productVersion, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public IReadOnlyList<Re611FunctionEntry> Search(string? deviceId, string? versionCode, string query, int limit = 12)
    {
        var token = Normalize(query);
        if (token.Length == 0)
        {
            return [];
        }

        return GetFunctions(deviceId, versionCode)
            .Select(function => new { Function = function, Score = MatchScore(function, token) })
            .Where(item => item.Score < int.MaxValue)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Function.Iec61850, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Function.AnsiCode, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(item => item.Function)
            .ToList();
    }

    public Re611FunctionEntry? ResolveExact(string? deviceId, string? versionCode, string query)
    {
        var token = Normalize(query);
        if (token.Length == 0)
        {
            return null;
        }

        var matches = GetFunctions(deviceId, versionCode)
            .Where(function =>
                Normalize(function.Iec61850).Equals(token, StringComparison.OrdinalIgnoreCase) ||
                ExpandAnsiSearchTerms(function.AnsiCode).Any(ansi => Normalize(ansi).Equals(token, StringComparison.OrdinalIgnoreCase)) ||
                Normalize(function.ChineseName).Equals(token, StringComparison.OrdinalIgnoreCase) ||
                Normalize(function.EnglishName).Equals(token, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(FunctionKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    public static string VersionCodeToProductVersion(string? versionCode) =>
        versionCode?.Trim().ToUpperInvariant() switch
        {
            "XE" => "1.0",
            "1G" => "2.0",
            _ => ""
        };

    public static IReadOnlyList<string> SplitSearchInput(string input) =>
        Regex.Split(input ?? "", @"[\r\n,，;；+]+")
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

    public static string FunctionKey(Re611FunctionEntry function) =>
        $"{function.DeviceId}|{function.ProductVersion}|{function.Iec61850}|{function.AnsiCode}|{function.ChineseName}";

    private static int MatchScore(Re611FunctionEntry function, string token)
    {
        var iec = Normalize(function.Iec61850);
        if (iec.Equals(token, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (ExpandAnsiSearchTerms(function.AnsiCode).Any(ansi => Normalize(ansi).Equals(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        if (Normalize(function.ChineseName).Equals(token, StringComparison.OrdinalIgnoreCase) ||
            Normalize(function.EnglishName).Equals(token, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (iec.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            ExpandAnsiSearchTerms(function.AnsiCode).Any(ansi => Normalize(ansi).Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        if (Normalize(function.ChineseName).Contains(token, StringComparison.OrdinalIgnoreCase) ||
            Normalize(function.EnglishName).Contains(token, StringComparison.OrdinalIgnoreCase) ||
            Normalize(function.Category).Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        return int.MaxValue;
    }

    private static IEnumerable<string> ExpandAnsiSearchTerms(string value)
    {
        yield return value;
        foreach (var part in Regex.Split(value ?? "", @"[/,，;；\s]+"))
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                yield return part.Trim();
            }
        }
    }

    private static string Normalize(string value) =>
        Regex.Replace(value ?? "", @"\s+", "").Trim().ToUpperInvariant();

    private static IReadOnlyList<Re611FunctionEntry> BuildFunctions()
    {
        var rows = new List<Re611FunctionEntry>();

        void Add(string device, string version, string configs, string category, string chinese, string english, string code, string ansi, int page)
        {
            rows.Add(new Re611FunctionEntry
            {
                DeviceId = device,
                ProductVersion = version,
                Configs = ParseConfigs(configs),
                Category = category,
                ChineseName = chinese,
                EnglishName = english,
                Iec61850 = code,
                AnsiCode = ansi,
                SourcePage = page
            });
        }

        void AddBoth(string device, string configs, string category, string chinese, string english, string code, string ansi, int page1, int page2)
        {
            Add(device, "1.0", configs, category, chinese, english, code, ansi, page1);
            Add(device, "2.0", configs, category, chinese, english, code, ansi, page2);
        }

        AddBoth("REB611", "A=1", "保护", "无方向接地故障保护，低定值段", "Non-directional earth-fault protection, low stage", "EFLPTOC", "51N-1", 4, 5);
        AddBoth("REB611", "A=1", "保护", "无方向接地故障保护，高定值段", "Non-directional earth-fault protection, high stage", "EFHPTOC", "51N-2", 4, 5);
        AddBoth("REB611", "A=1", "保护", "断路器失灵保护", "Circuit breaker failure protection", "CCBRBRF", "51BF/51NBF", 4, 5);
        AddBoth("REB611", "A=2", "保护", "主跳闸", "Master trip", "TRPPTRC", "94/86", 4, 5);
        AddBoth("REB611", "A=1", "保护", "A 相高阻差动保护", "High-impedance differential protection for phase A", "HIAPDIF", "87", 4, 5);
        AddBoth("REB611", "A=1", "保护", "B 相高阻差动保护", "High-impedance differential protection for phase B", "HIBPDIF", "87", 4, 5);
        AddBoth("REB611", "A=1", "保护", "C 相高阻差动保护", "High-impedance differential protection for phase C", "HICPDIF", "87", 4, 5);
        AddBoth("REB611", "A=1", "控制", "断路器控制", "Circuit-breaker control", "CBXCBR", "CB", 4, 5);
        AddBoth("REB611", "A=2", "监视", "跳闸回路监视", "Trip circuit supervision", "TCSSCBR", "TCM", 4, 5);
        AddBoth("REB611", "A=1", "监视", "A 相分相 CT 监视", "Phase segregated CT supervision, phase A", "HZCCASPVC", "MCS 1I", 4, 5);
        AddBoth("REB611", "A=1", "监视", "B 相分相 CT 监视", "Phase segregated CT supervision, phase B", "HZCCBSPVC", "MCS 1I", 4, 5);
        AddBoth("REB611", "A=1", "监视", "C 相分相 CT 监视", "Phase segregated CT supervision, phase C", "HZCCCSPVC", "MCS 1I", 4, 5);
        AddBoth("REB611", "A=1", "记录", "故障录波", "Disturbance recorder", "RDRE", "DFR", 4, 5);
        Add("REB611", "2.0", "A=1", "记录", "故障记录", "Fault recorder", "FLTRFRC", "FR", 5);
        AddBoth("REB611", "A=1", "测量", "三相电流测量", "Three-phase current measurement", "CMMXU", "3I", 4, 5);
        AddBoth("REB611", "A=1", "测量", "零序电流测量", "Residual current measurement", "RESCMMXU", "In", 4, 5);

        AddBoth("REF611", "A=1;B=1", "保护", "三相无方向过流保护，低定值段", "Three-phase non-directional overcurrent protection, low stage", "PHLPTOC", "51P-1", 3, 7);
        AddBoth("REF611", "A=2;B=2", "保护", "三相无方向过流保护，高定值段", "Three-phase non-directional overcurrent protection, high stage", "PHHPTOC", "51P-2", 3, 7);
        AddBoth("REF611", "A=1;B=1", "保护", "三相无方向过流保护，瞬时段", "Three-phase non-directional overcurrent protection, instantaneous stage", "PHIPTOC", "50P/51P", 3, 7);
        AddBoth("REF611", "B=2", "保护", "无方向接地保护，低定值段", "Non-directional earth-fault protection, low stage", "EFLPTOC", "51N-1", 3, 7);
        AddBoth("REF611", "B=1", "保护", "无方向接地保护，高定值段", "Non-directional earth-fault protection, high stage", "EFHPTOC", "51N-2", 3, 7);
        AddBoth("REF611", "B=1", "保护", "无方向接地保护，瞬时段", "Non-directional earth-fault protection, instantaneous stage", "EFIPTOC", "50N/51N", 3, 7);
        Add("REF611", "2.0", "C=2", "保护", "三相方向过流保护，低定值段", "Three-phase directional overcurrent protection, low stage", "DPHLPDOC", "67-1", 7);
        Add("REF611", "2.0", "C=1", "保护", "三相方向过流保护，高定值段", "Three-phase directional overcurrent protection, high stage", "DPHHPDOC", "67-2", 7);
        AddBoth("REF611", "A=2", "保护", "方向接地故障保护，低定值段", "Directional earth-fault protection, low stage", "DEFLPDEF", "67N-1", 3, 7);
        AddBoth("REF611", "A=1", "保护", "方向接地故障保护，高定值段", "Directional earth-fault protection, high stage", "DEFHPDEF", "67N-2", 4, 7);
        AddBoth("REF611", "A=1", "保护", "瞬时/间歇性接地保护", "Transient/intermittent earth-fault protection", "INTRPTEF", "67NIEF", 4, 7);
        AddBoth("REF611", "A=1;B=1", "保护", "负序过流保护", "Negative-sequence overcurrent protection", "NSPTOC", "46", 4, 7);
        AddBoth("REF611", "A=1;B=1", "保护", "断相保护", "Phase discontinuity protection", "PDNSPTOC", "46PD", 4, 7);
        AddBoth("REF611", "A=3", "保护", "零序过电压保护", "Residual overvoltage protection", "ROVPTOV", "59G", 4, 7);
        AddBoth("REF611", "A=1;B=1", "保护", "三相热过负荷保护", "Three-phase thermal protection", "T1PTTR", "49F", 4, 7);
        AddBoth("REF611", "A=1;B=1", "保护", "断路器失灵保护", "Circuit breaker failure protection", "CCBRBRF", "51BF/51NBF", 4, 7);
        AddBoth("REF611", "A=1;B=1", "保护", "三相涌流检测", "Three-phase inrush detector", "INRPHAR", "68", 4, 7);
        AddBoth("REF611", "A=2;B=2", "保护", "主跳闸", "Master trip", "TRPPTRC", "94/86", 4, 7);
        Add("REF611", "2.0", "A=1;B=1;C=1", "保护", "开关合于故障", "Switch onto fault", "CBPSOF", "SOTF", 7);
        AddBoth("REF611", "A=1;B=1", "控制", "断路器控制", "Circuit-breaker control", "CBXCBR", "CB", 4, 7);
        AddBoth("REF611", "A=(1);B=(1)", "控制", "自动重合闸", "Autoreclosing", "DARREC", "79", 4, 7);
        AddBoth("REF611", "A=2;B=2", "监视", "跳闸回路监视", "Trip circuit supervision", "TCSSCBR", "TCM", 4, 7);
        AddBoth("REF611", "A=1;B=1", "记录", "故障录波", "Disturbance recorder", "RDRE", "DFR", 4, 7);
        Add("REF611", "2.0", "A=1;B=1;C=1", "记录", "故障记录", "Fault recorder", "FLTRFRC", "FR", 7);
        AddBoth("REF611", "A=1;B=1", "测量", "三相电流测量", "Three-phase current measurement", "CMMXU", "3I", 4, 7);
        AddBoth("REF611", "A=1;B=1", "测量", "电流序分量测量", "Sequence current measurement", "CSMSQI", "I1, I2, I0", 4, 7);
        AddBoth("REF611", "A=1;B=1", "测量", "零序电流测量", "Residual current measurement", "RESCMMXU", "In", 4, 7);
        Add("REF611", "2.0", "C=1", "测量", "三相电压测量", "Three-phase voltage measurement", "VMMXU", "3U", 7);
        AddBoth("REF611", "A=1", "测量", "电压序分量测量", "Sequence voltage measurement", "VSMSQI", "U1, U2, U0", 4, 7);
        Add("REF611", "2.0", "A=1;C=1", "测量", "零序电压测量", "Residual voltage measurement", "RESVMMXU", "Vn", 7);
        Add("REF611", "2.0", "C=1", "测量", "频率测量", "Frequency measurement", "FMMXU", "f", 8);
        Add("REF611", "2.0", "C=1", "测量", "三相功率及电能测量", "Three-phase power and energy measurement", "PEMMXU", "P, E", 8);

        AddBoth("REM611", "A=1", "保护", "三相无方向过流保护，低定值段", "Three-phase non-directional overcurrent protection, low stage", "PHLPTOC", "51P-1", 4, 5);
        AddBoth("REM611", "A=1", "保护", "三相无方向过流保护，瞬时段", "Three-phase non-directional overcurrent protection, instantaneous stage", "PHIPTOC", "50P/51P", 4, 5);
        AddBoth("REM611", "A=1", "保护", "无方向接地保护，低定值段", "Non-directional earth-fault protection, low stage", "EFLPTOC", "51N-1", 4, 5);
        AddBoth("REM611", "A=1", "保护", "无方向接地保护，高定值段", "Non-directional earth-fault protection, high stage", "EFHPTOC", "51N-2", 4, 5);
        AddBoth("REM611", "A=2", "保护", "电机负序过电流保护", "Negative-sequence overcurrent protection for machines", "MNSPTOC", "46M", 4, 5);
        AddBoth("REM611", "A=1", "保护", "失载保护", "Loss of load supervision", "LOFLPTUC", "37", 4, 5);
        AddBoth("REM611", "A=1", "保护", "堵转保护", "Motor load jam protection", "JAMPTOC", "51LR", 4, 5);
        AddBoth("REM611", "A=1", "保护", "电机启动监视", "Motor start-up supervision", "STTPMSU", "49/66/48/51LR", 4, 5);
        AddBoth("REM611", "A=1", "保护", "反转保护", "Phase reversal protection", "PREVPTOC", "46R", 4, 5);
        AddBoth("REM611", "A=1", "保护", "电机热过负荷保护", "Thermal overload protection for motors", "MPTTR", "49M", 4, 5);
        AddBoth("REM611", "A=1", "保护", "断路器失灵保护", "Circuit breaker failure protection", "CCBRBRF", "51BF/51NBF", 4, 5);
        AddBoth("REM611", "A=1", "保护", "主跳闸", "Master trip", "TRPPTRC", "94/86", 4, 5);
        AddBoth("REM611", "A=1", "控制", "断路器控制", "Circuit-breaker control", "CBXCBR", "CB", 4, 5);
        AddBoth("REM611", "A=1", "控制", "紧急启动", "Emergency start-up", "ESMGAPC", "ESTART", 4, 5);
        AddBoth("REM611", "A=2", "监视", "跳闸回路监视", "Trip circuit supervision", "TCSSCBR", "TCM", 4, 5);
        AddBoth("REM611", "A=1", "监视", "电机运行时间累计", "Runtime counter for machines and devices", "MDSOPT", "MDSOPT", 4, 5);
        AddBoth("REM611", "A=1", "记录", "故障录波", "Disturbance recorder", "RDRE", "DFR", 4, 5);
        Add("REM611", "2.0", "A=1", "记录", "故障记录", "Fault recorder", "FLTRFRC", "FR", 5);
        AddBoth("REM611", "A=1", "测量", "三相电流测量", "Three-phase current measurement", "CMMXU", "3I", 4, 5);
        AddBoth("REM611", "A=1", "测量", "电流序分量测量", "Sequence current measurement", "CSMSQI", "I1, I2, I0", 4, 5);
        AddBoth("REM611", "A=1", "测量", "零序电流测量", "Residual current measurement", "RESCMMXU", "In", 4, 5);

        Add("REU611", "2.0", "A=3", "保护", "零序过电压保护", "Residual overvoltage protection", "ROVPTOV", "59G", 5);
        Add("REU611", "2.0", "A=3", "保护", "三相低电压保护", "Three-phase undervoltage protection", "PHPTUV", "27", 5);
        Add("REU611", "2.0", "A=3", "保护", "三相过电压保护", "Three-phase overvoltage protection", "PHPTOV", "59", 5);
        Add("REU611", "2.0", "A=2", "保护", "正序低电压保护", "Positive-sequence undervoltage protection", "PSPTUV", "47U+", 5);
        Add("REU611", "2.0", "A=2", "保护", "负序过电压保护", "Negative-sequence overvoltage protection", "NSPTOV", "47O-", 5);
        Add("REU611", "2.0", "A=2", "保护", "频率保护", "Frequency protection", "FRPFRQ", "81", 5);
        Add("REU611", "2.0", "A=2", "保护", "主跳闸", "Master trip", "TRPPTRC", "94/86", 5);
        Add("REU611", "2.0", "A=1", "控制", "断路器控制", "Circuit-breaker control", "CBXCBR", "CB", 5);
        Add("REU611", "2.0", "A=2", "监视", "跳闸回路监视", "Trip circuit supervision", "TCSSCBR", "TCM", 5);
        Add("REU611", "2.0", "A=1", "记录", "故障录波", "Disturbance recorder", "RDRE", "DFR", 5);
        Add("REU611", "2.0", "A=1", "记录", "故障记录", "Fault recorder", "FLTRFRC", "FR", 5);
        Add("REU611", "2.0", "A=2", "测量", "三相电压测量", "Three-phase voltage measurement", "VMMXU", "3U", 5);
        Add("REU611", "2.0", "A=1", "测量", "电压序分量测量", "Sequence voltage measurement", "VSMSQI", "U1, U2, U0", 5);
        Add("REU611", "2.0", "A=1", "测量", "零序电压测量", "Residual voltage measurement", "RESVMMXU", "Vn", 5);
        Add("REU611", "2.0", "A=1", "测量", "频率测量", "Frequency measurement", "FMMXU", "f", 5);

        return rows;
    }

    private static Dictionary<string, string> ParseConfigs(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pieces.Length == 2 && !string.IsNullOrWhiteSpace(pieces[0]))
            {
                result[pieces[0]] = pieces[1];
            }
        }

        return result;
    }
}

public sealed record Re611FunctionEntry
{
    public string DeviceId { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public Dictionary<string, string> Configs { get; init; } = [];
    public string Category { get; init; } = "";
    public string ChineseName { get; init; } = "";
    public string EnglishName { get; init; } = "";
    public string Iec61850 { get; init; } = "";
    public string AnsiCode { get; init; } = "";
    public int SourcePage { get; init; }
}
