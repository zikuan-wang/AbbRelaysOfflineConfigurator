using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed record LegacyOfflineConversionResult(
    string SourceOrderingCode,
    string DeviceType,
    string? CompositionCode,
    bool IsSuccess,
    string Message);

// 在不依赖 Excel 的情况下复现历史 615/620 工作簿转换逻辑：加载预导出的单元格和公式，
// 注入旧订货号，求得 REX615 组合代码，并在无法直接识别型号时对所有工作表结果评分择优。
public sealed class LegacyOrderCodeConversionService
{
    private const string RulesFileName = "LegacyConversionRules.json";
    // 规则包体积较大且只在离线转换时需要，延迟解析可避免拖慢应用启动。
    private readonly Lazy<LegacyWorkbook> _workbook;

    public LegacyOrderCodeConversionService()
    {
        RulesPath = ResolveRulesPath();
        _workbook = new Lazy<LegacyWorkbook>(() => LegacyWorkbook.Load(RulesPath));
    }

    public string RulesPath { get; }

    public IReadOnlyList<string> GetDeviceTypes()
    {
        if (!File.Exists(RulesPath))
        {
            return [];
        }

        return _workbook.Value.Sheets.Select(sheet => sheet.Name).ToList();
    }

    public Task<IReadOnlyList<LegacyOfflineConversionResult>> ConvertOfflineBatchAsync(
        IReadOnlyList<string> orderingCodes)
    {
        // JSON 解析和公式求值属于同步的 CPU/内存工作，放到后台线程以免阻塞 WPF 界面。
        return Task.Run<IReadOnlyList<LegacyOfflineConversionResult>>(() => ConvertOfflineBatch(orderingCodes));
    }

    private IReadOnlyList<LegacyOfflineConversionResult> ConvertOfflineBatch(
        IReadOnlyList<string> orderingCodes)
    {
        if (!File.Exists(RulesPath))
        {
            return orderingCodes
                .Select(code => new LegacyOfflineConversionResult(code, "", null, false, "未找到本地 615/620 转换规则包。"))
                .ToList();
        }

        var results = new List<LegacyOfflineConversionResult>();
        foreach (var orderingCode in orderingCodes)
        {
            try
            {
                results.Add(ConvertOne(orderingCode.Trim()));
            }
            catch (Exception ex)
            {
                results.Add(new LegacyOfflineConversionResult(orderingCode, "", null, false, $"离线转换失败：{ex.Message}"));
            }
        }

        return results;
    }

    private LegacyOfflineConversionResult ConvertOne(string orderingCode)
    {
        if (string.IsNullOrWhiteSpace(orderingCode))
        {
            return new LegacyOfflineConversionResult(orderingCode, "", null, false, "615/620 订货号为空。");
        }

        // 已知系列前后缀可唯一定位工作表时直接使用，既减少公式求值量，也避免相近版本规则误胜出。
        var preferredDeviceType = DetectDeviceType(orderingCode);
        if (!string.IsNullOrWhiteSpace(preferredDeviceType) &&
            _workbook.Value.TryGetSheet(preferredDeviceType, out var preferredSheet))
        {
            return BuildResult(orderingCode, preferredSheet.ConvertCandidate(orderingCode), "根据订货号型号自动识别");
        }

        // 未命中显式映射时才运行所有工作表。成功输出获得最高权重，其次结合输出片段数、
        // 615/620 家族及 IEC/CN/ANSI 前缀特征评分，以保留对未知修订后缀的兼容能力。
        var candidates = _workbook.Value.Sheets
            .Select(sheet => sheet.ConvertCandidate(orderingCode))
            .OrderByDescending(candidate => candidate.Score)
            .ToList();
        var bestValid = candidates.FirstOrDefault(candidate => candidate.IsSuccess);
        if (bestValid is not null)
        {
            return BuildResult(orderingCode, bestValid, "按规则评分自动识别");
        }

        var best = candidates.FirstOrDefault();
        if (best is null || string.IsNullOrWhiteSpace(best.CompositionCode))
        {
            return new LegacyOfflineConversionResult(orderingCode, "", null, false, "无法识别装置类型，未生成 REX615 组合代码。");
        }

        return new LegacyOfflineConversionResult(
            orderingCode,
            best.DeviceType,
            best.CompositionCode,
            false,
            $"自动识别为 {best.DeviceType}，但规则返回异常结果：{best.CompositionCode}");
    }

    private static LegacyOfflineConversionResult BuildResult(
        string orderingCode,
        LegacyConversionCandidate candidate,
        string detectionMode)
    {
        if (candidate.IsSuccess)
        {
            return new LegacyOfflineConversionResult(
                orderingCode,
                candidate.DeviceType,
                candidate.CompositionCode,
                true,
                $"{detectionMode}为 {candidate.DeviceType}，离线转换通过。");
        }

        if (string.IsNullOrWhiteSpace(candidate.CompositionCode))
        {
            return new LegacyOfflineConversionResult(
                orderingCode,
                candidate.DeviceType,
                null,
                false,
                $"{detectionMode}为 {candidate.DeviceType}，但未生成 REX615 组合代码。");
        }

        return new LegacyOfflineConversionResult(
            orderingCode,
            candidate.DeviceType,
            candidate.CompositionCode,
            false,
            $"{detectionMode}为 {candidate.DeviceType}，但规则返回异常结果：{candidate.CompositionCode}");
    }

    private static string? DetectDeviceType(string orderingCode)
    {
        var value = orderingCode.Trim().ToUpperInvariant();
        if (value.Length < 2)
        {
            return null;
        }

        var family = value[..2];
        return family switch
        {
            "HB" when value.EndsWith("XG", StringComparison.Ordinal) => "615 series IEC 5.0",
            "HB" when value.EndsWith("G", StringComparison.Ordinal) => "615 series IEC 5.0 FP1",
            "HB" when value.EndsWith("XE", StringComparison.Ordinal) => "615 series IEC 4.0",
            "HB" when value.EndsWith("E", StringComparison.Ordinal) => "615 series IEC 4.0 FP1",
            "HB" when value.EndsWith("XD", StringComparison.Ordinal) => "615 series IEC 3.0",
            "HB" when value.EndsWith("XC", StringComparison.Ordinal) => "615 series IEC 2.0",
            "HC" when value.EndsWith("G", StringComparison.Ordinal) => "615 series CN 5.0 FP1",
            "HC" when value.EndsWith("E", StringComparison.Ordinal) => "615 series CN 4.0 FP1",
            "HA" when value.EndsWith("G", StringComparison.Ordinal) => "615 series ANSI 5.0 FP1",
            "HA" when value.EndsWith("2E", StringComparison.Ordinal) => "615 series ANSI 4.0 FP2",
            "HA" when value.EndsWith("1E", StringComparison.Ordinal) => "615 series ANSI 4.0 FP1",
            "HA" when value.EndsWith("XE", StringComparison.Ordinal) => "615 series ANSI 4.0",
            "NB" when value.EndsWith("G", StringComparison.Ordinal) => "620 series IEC_CN 2.0 FP1",
            "NB" when value.EndsWith("XF", StringComparison.Ordinal) => "620 series IEC_CN 2.0",
            "NA" when value.EndsWith("XF", StringComparison.Ordinal) => "620 series ANSI 2.0",
            "NA" when value.EndsWith("F", StringComparison.Ordinal) => "620 series ANSI 2.0 FP1",
            _ => null
        };
    }

    private static string ResolveRulesPath()
    {
        // 发布环境优先从程序 Data 目录读取；向父目录搜索仅服务于源码调试和测试运行。
        // 最终仍返回首选发布路径，让缺失文件错误包含稳定、可诊断的位置。
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

internal sealed class LegacyWorkbook
{
    // 规则包保留“工作簿 -> 工作表 -> 单元格”的原始边界，使每种历史装置版本可以独立求值，
    // 而不必把大量 Excel 分支人工改写成难以核对的 C# 条件树。
    private LegacyWorkbook(IReadOnlyList<LegacyWorksheet> sheets)
    {
        Sheets = sheets;
    }

    public IReadOnlyList<LegacyWorksheet> Sheets { get; }

    public bool TryGetSheet(string name, out LegacyWorksheet worksheet)
    {
        worksheet = Sheets.FirstOrDefault(sheet => sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase))!;
        return worksheet is not null;
    }

    public static LegacyWorkbook Load(string path)
    {
        using var stream = File.OpenRead(path);
        var rules = JsonSerializer.Deserialize<LegacyRulesDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("615/620 转换规则包为空。");
        var sheets = rules.Sheets
            .Where(sheet => !string.IsNullOrWhiteSpace(sheet.Name))
            .Select(LegacyWorksheet.Load)
            .ToList();

        return new LegacyWorkbook(sheets);
    }
}

internal sealed class LegacyRulesDocument
{
    public int FormatVersion { get; set; }
    public List<LegacySheetData> Sheets { get; set; } = [];
}

internal sealed class LegacySheetData
{
    public string Name { get; set; } = "";
    public string InputCell { get; set; } = "C2";
    public string OutputCell { get; set; } = "C4";
    public List<LegacyCellData> Cells { get; set; } = [];
}

internal sealed class LegacyCellData
{
    public string Reference { get; set; } = "";
    public string Value { get; set; } = "";
    public string Formula { get; set; } = "";
    public int? SharedFormulaIndex { get; set; }
}

internal sealed class LegacyWorksheet
{
    private readonly Dictionary<string, SpreadsheetCell> _cells;

    private LegacyWorksheet(
        string name,
        string inputCellReference,
        string outputCellReference,
        Dictionary<string, SpreadsheetCell> cells)
    {
        Name = name;
        _cells = cells;
        InputCellReference = CellAddress.NormalizeReference(inputCellReference);
        OutputCellReference = CellAddress.NormalizeReference(outputCellReference);
    }

    public string Name { get; }
    public string InputCellReference { get; }
    public string OutputCellReference { get; }

    public static LegacyWorksheet Load(LegacySheetData sheetData)
    {
        var cells = new Dictionary<string, SpreadsheetCell>(StringComparer.OrdinalIgnoreCase);
        var sharedFormulas = new Dictionary<int, SharedFormula>();

        foreach (var cellData in sheetData.Cells)
        {
            if (string.IsNullOrWhiteSpace(cellData.Reference))
            {
                continue;
            }

            var normalizedReference = CellAddress.NormalizeReference(cellData.Reference);
            var address = CellAddress.Parse(normalizedReference);
            var formula = cellData.Formula == "System.Xml.XmlElement" ? "" : cellData.Formula ?? "";
            var sharedIndex = cellData.SharedFormulaIndex;

            var cell = new SpreadsheetCell(address, normalizedReference, cellData.Value ?? "", formula, sharedIndex);
            cells[normalizedReference] = cell;

            if (sharedIndex is not null && !string.IsNullOrWhiteSpace(formula))
            {
                sharedFormulas[sharedIndex.Value] = new SharedFormula(address, formula);
            }
        }

        // Excel 共享公式只在基准单元格保存公式文本，其余单元格需按相对偏移还原后才能离线求值。
        foreach (var cell in cells.Values)
        {
            if (!string.IsNullOrWhiteSpace(cell.Formula) ||
                cell.SharedFormulaIndex is null ||
                !sharedFormulas.TryGetValue(cell.SharedFormulaIndex.Value, out var sharedFormula))
            {
                continue;
            }

            cell.Formula = FormulaReferenceTranslator.Translate(
                sharedFormula.Formula,
                sharedFormula.BaseAddress,
                cell.Address);
        }

        return new LegacyWorksheet(sheetData.Name, sheetData.InputCell, sheetData.OutputCell, cells);
    }

    public LegacyConversionCandidate ConvertCandidate(string orderingCode)
    {
        // overrides 相当于把用户订货号写入工作簿输入格；其余单元格按需递归求值，
        // 每个候选使用独立 evaluator，缓存和循环检测不会跨订货号或工作表泄漏状态。
        var evaluator = new FormulaEvaluator(this, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [InputCellReference] = orderingCode
        });

        var value = evaluator.EvaluateCell(OutputCellReference);
        var compositionCode = FormulaEvaluator.ToText(value);

        // 某些历史表的汇总输出格为空，但第 23 列仍保留拼接结果；该位置是规则包兼容回退，
        // 不能推广为任意工作簿的通用 Excel 行为。
        if (string.IsNullOrWhiteSpace(compositionCode))
        {
            var outputAddress = CellAddress.Parse(OutputCellReference);
            var fallback = evaluator.EvaluateCell(CellAddress.ToReference(outputAddress.Row, 23));
            compositionCode = FormulaEvaluator.ToText(fallback);
        }

        // 输出片段越多，通常说明当前工作表识别了更多订货号位；它只参与型号猜测评分，
        // 最终成功仍要求结果以 REX615 开头且不含 error 标记。
        var fragmentCount = CountOutputFragments(evaluator);
        var isSuccess = compositionCode.StartsWith("REX615", StringComparison.OrdinalIgnoreCase) &&
                        !compositionCode.Contains("error", StringComparison.OrdinalIgnoreCase);
        var score = fragmentCount + PrefixScore(orderingCode);
        if (isSuccess)
        {
            score += 10000;
        }
        else if (compositionCode.StartsWith("REX615", StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        return new LegacyConversionCandidate(Name, compositionCode, isSuccess, fragmentCount, score);
    }

    public bool TryGetCell(string reference, out SpreadsheetCell cell) =>
        _cells.TryGetValue(CellAddress.NormalizeReference(reference), out cell!);

    public IEnumerable<SpreadsheetCell> CellsInRange(string startReference, string endReference)
    {
        var start = CellAddress.Parse(startReference);
        var end = CellAddress.Parse(endReference);
        for (var row = Math.Min(start.Row, end.Row); row <= Math.Max(start.Row, end.Row); row++)
        {
            for (var column = Math.Min(start.Column, end.Column); column <= Math.Max(start.Column, end.Column); column++)
            {
                if (_cells.TryGetValue(CellAddress.ToReference(row, column), out var cell))
                {
                    yield return cell;
                }
            }
        }
    }

    private int CountOutputFragments(FormulaEvaluator evaluator)
    {
        var outputRow = CellAddress.Parse(OutputCellReference).Row;
        return _cells.Values
            .Where(cell => cell.Address.Column == 23 &&
                           cell.Address.Row > outputRow &&
                           cell.Reference != OutputCellReference)
            .Select(cell => FormulaEvaluator.ToText(evaluator.EvaluateCell(cell.Reference)))
            .Count(value => !string.IsNullOrWhiteSpace(value));
    }

    private int PrefixScore(string orderingCode)
    {
        var value = orderingCode.Trim().ToUpperInvariant();
        if (value.Length < 2)
        {
            return 0;
        }

        var score = 0;
        if (value[0] == 'H' && Name.Contains("615", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }
        else if (value[0] == 'N' && Name.Contains("620", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }

        var family = value[..2];
        if (family == "HA" && Name.Contains("ANSI", StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }
        else if (family == "HC" && Name.Contains("CN", StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }
        else if (family == "HB" && Name.Contains("IEC", StringComparison.OrdinalIgnoreCase) && !Name.Contains("CN", StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }
        else if (family == "NA" && Name.Contains("ANSI", StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }
        else if (family == "NB" && Name.Contains("IEC_CN", StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }

        return score;
    }
}

internal sealed record LegacyConversionCandidate(
    string DeviceType,
    string CompositionCode,
    bool IsSuccess,
    int FragmentCount,
    int Score);

internal sealed class FormulaEvaluator
{
    // 这是针对转换规则包的受限 Excel 解释器，只实现已导出公式实际使用的比较、引用和少量函数；
    // 未知函数返回空值，调用方必须通过最终组合代码形态判断转换是否成功，不能视其为通用表格引擎。
    private static readonly Regex CellReferenceRegex = new(@"^\$?[A-Z]{1,3}\$?\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RangeReferenceRegex = new(@"^(?<start>\$?[A-Z]{1,3}\$?\d+):(?<end>\$?[A-Z]{1,3}\$?\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly Dictionary<string, object?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _overrides;
    private readonly HashSet<string> _stack = new(StringComparer.OrdinalIgnoreCase);
    private readonly LegacyWorksheet _worksheet;

    public FormulaEvaluator(LegacyWorksheet worksheet, Dictionary<string, string> overrides)
    {
        _worksheet = worksheet;
        _overrides = overrides;
    }

    public object? EvaluateCell(string reference)
    {
        var normalizedReference = CellAddress.NormalizeReference(reference);
        if (_overrides.TryGetValue(normalizedReference, out var overrideValue))
        {
            return overrideValue;
        }

        // 公式依赖图会多次引用同一格，按单元格缓存可避免指数级重复计算；输入覆盖优先于缓存，
        // 且 evaluator 生命周期只对应一个候选转换，因此缓存键无需包含订货号。
        if (_cache.TryGetValue(normalizedReference, out var cached))
        {
            return cached;
        }

        if (!_worksheet.TryGetCell(normalizedReference, out var cell))
        {
            return "";
        }

        // 正常规则不应循环引用；异常时按空值结束本次求值，避免递归耗尽调用栈。
        if (!_stack.Add(normalizedReference))
        {
            return "";
        }

        try
        {
            var value = string.IsNullOrWhiteSpace(cell.Formula)
                ? cell.Value
                : EvaluateExpression(cell.Formula);
            _cache[normalizedReference] = value;
            return value;
        }
        finally
        {
            _stack.Remove(normalizedReference);
        }
    }

    public object? EvaluateExpression(string expression)
    {
        var value = expression.Trim();
        if (value.StartsWith('='))
        {
            value = value[1..].Trim();
        }

        if (value.Length == 0)
        {
            return "";
        }

        // 只在括号和字符串之外寻找比较运算符，保证 IF(A1="X",...) 内部比较不会被外层错误拆分。
        if (TryFindTopLevelComparison(value, out var comparisonIndex, out var comparisonOperator))
        {
            var left = EvaluateExpression(value[..comparisonIndex]);
            var right = EvaluateExpression(value[(comparisonIndex + comparisonOperator.Length)..]);
            return Compare(left, right, comparisonOperator);
        }

        if (TryParseFunction(value, out var functionName, out var arguments))
        {
            return EvaluateFunction(functionName, arguments);
        }

        if (IsStringLiteral(value))
        {
            return value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (CellReferenceRegex.IsMatch(value))
        {
            return EvaluateCell(value);
        }

        return value;
    }

    public static string ToText(object? value) =>
        value switch
        {
            null => "",
            bool boolean => boolean ? "TRUE" : "FALSE",
            double number when Math.Abs(number - Math.Round(number)) < 0.0000001 =>
                ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };

    private object? EvaluateFunction(string functionName, IReadOnlyList<string> arguments)
    {
        switch (functionName.ToUpperInvariant())
        {
            case "IF":
                if (arguments.Count < 2)
                {
                    return "";
                }

                // 只求值命中的分支，保持 Excel IF 的短路语义；未命中分支中的无效引用不会污染结果。
                return ToBoolean(EvaluateExpression(arguments[0]))
                    ? EvaluateExpression(arguments[1])
                    : arguments.Count >= 3 ? EvaluateExpression(arguments[2]) : "";

            case "CONCATENATE":
                return string.Concat(arguments.Select(argument => ToText(EvaluateExpression(argument))));

            case "OR":
                return arguments.Any(argument => ToBoolean(EvaluateExpression(argument)));

            case "AND":
                return arguments.All(argument => ToBoolean(EvaluateExpression(argument)));

            case "MID":
                if (arguments.Count < 3)
                {
                    return "";
                }

                return Mid(
                    ToText(EvaluateExpression(arguments[0])),
                    ToInteger(EvaluateExpression(arguments[1])),
                    ToInteger(EvaluateExpression(arguments[2])));

            case "LEN":
                return arguments.Count == 0 ? 0d : ToText(EvaluateExpression(arguments[0])).Length;

            case "SUM":
                return arguments.Sum(SumArgument);

            default:
                return "";
        }
    }

    private double SumArgument(string argument)
    {
        var trimmed = argument.Trim();
        var rangeMatch = RangeReferenceRegex.Match(trimmed);
        if (rangeMatch.Success)
        {
            return _worksheet
                .CellsInRange(rangeMatch.Groups["start"].Value, rangeMatch.Groups["end"].Value)
                .Select(cell => EvaluateCell(cell.Reference))
                .Sum(ToNumber);
        }

        return ToNumber(EvaluateExpression(trimmed));
    }

    private static string Mid(string text, int start, int length)
    {
        if (start <= 0 || length <= 0 || start > text.Length)
        {
            return "";
        }

        var startIndex = start - 1;
        var safeLength = Math.Min(length, text.Length - startIndex);
        return text.Substring(startIndex, safeLength);
    }

    private static bool Compare(object? left, object? right, string comparisonOperator)
    {
        var leftText = ToText(left);
        var rightText = ToText(right);
        var leftParsed = double.TryParse(leftText, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber);
        var rightParsed = double.TryParse(rightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber);
        var numeric = leftParsed && rightParsed;

        var comparison = numeric
            ? leftNumber.CompareTo(rightNumber)
            : string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);

        return comparisonOperator switch
        {
            "=" => comparison == 0,
            "<>" => comparison != 0,
            "<" => comparison < 0,
            ">" => comparison > 0,
            "<=" => comparison <= 0,
            ">=" => comparison >= 0,
            _ => false
        };
    }

    private static bool ToBoolean(object? value)
    {
        if (value is bool boolean)
        {
            return boolean;
        }

        var text = ToText(value);
        if (bool.TryParse(text, out var parsedBoolean))
        {
            return parsedBoolean;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return Math.Abs(number) > 0.0000001;
        }

        return !string.IsNullOrWhiteSpace(text);
    }

    private static int ToInteger(object? value)
    {
        if (value is double number)
        {
            return (int)Math.Round(number);
        }

        return int.TryParse(ToText(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static double ToNumber(object? value)
    {
        if (value is double number)
        {
            return number;
        }

        return double.TryParse(ToText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0d;
    }

    private static bool TryParseFunction(
        string expression,
        out string functionName,
        out IReadOnlyList<string> arguments)
    {
        functionName = "";
        arguments = [];
        var openIndex = expression.IndexOf('(');
        if (openIndex <= 0 || !expression.EndsWith(')'))
        {
            return false;
        }

        functionName = expression[..openIndex].Trim();
        if (!functionName.All(char.IsLetter))
        {
            return false;
        }

        var closeIndex = FindMatchingParenthesis(expression, openIndex);
        if (closeIndex != expression.Length - 1)
        {
            return false;
        }

        arguments = SplitArguments(expression[(openIndex + 1)..^1]);
        return true;
    }

    private static int FindMatchingParenthesis(string expression, int openIndex)
    {
        var depth = 0;
        var inString = false;
        for (var index = openIndex; index < expression.Length; index++)
        {
            var ch = expression[index];
            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitArguments(string value)
    {
        // 逗号只有在最外层且不位于字符串中时才分隔参数，从而正确处理嵌套 IF、CONCATENATE
        // 以及字符串字面量中的逗号。
        var arguments = new List<string>();
        var start = 0;
        var depth = 0;
        var inString = false;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
            }
            else if (ch == ',' && depth == 0)
            {
                arguments.Add(value[start..index].Trim());
                start = index + 1;
            }
        }

        arguments.Add(value[start..].Trim());
        return arguments;
    }

    private static bool TryFindTopLevelComparison(
        string expression,
        out int index,
        out string comparisonOperator)
    {
        index = -1;
        comparisonOperator = "";
        var depth = 0;
        var inString = false;
        for (var position = 0; position < expression.Length; position++)
        {
            var ch = expression[position];
            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch == ')')
            {
                depth--;
                continue;
            }

            if (depth != 0)
            {
                continue;
            }

            if ((ch == '<' || ch == '>') && position + 1 < expression.Length && expression[position + 1] == '=')
            {
                index = position;
                comparisonOperator = expression.Substring(position, 2);
                return true;
            }

            if (ch == '<' && position + 1 < expression.Length && expression[position + 1] == '>')
            {
                index = position;
                comparisonOperator = "<>";
                return true;
            }

            if (ch is '=' or '<' or '>')
            {
                index = position;
                comparisonOperator = ch.ToString();
                return true;
            }
        }

        return false;
    }

    private static bool IsStringLiteral(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"';
}

internal sealed record SharedFormula(CellAddress BaseAddress, string Formula);

internal sealed class SpreadsheetCell
{
    public SpreadsheetCell(
        CellAddress address,
        string reference,
        string value,
        string formula,
        int? sharedFormulaIndex)
    {
        Address = address;
        Reference = reference;
        Value = value;
        Formula = formula;
        SharedFormulaIndex = sharedFormulaIndex;
    }

    public CellAddress Address { get; }
    public string Reference { get; }
    public string Value { get; }
    public string Formula { get; set; }
    public int? SharedFormulaIndex { get; }
}

internal readonly record struct CellAddress(int Row, int Column)
{
    // 内部行列均使用 Excel 的 1 基坐标；绝对引用符号仅影响共享公式平移，
    // 作为字典键时会被 NormalizeReference 去除。
    private static readonly Regex ReferenceRegex = new(@"^\$?(?<column>[A-Z]{1,3})\$?(?<row>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CellAddress Parse(string reference)
    {
        var match = ReferenceRegex.Match(NormalizeReference(reference));
        if (!match.Success)
        {
            throw new FormatException($"无效单元格引用：{reference}");
        }

        return new CellAddress(
            int.Parse(match.Groups["row"].Value, CultureInfo.InvariantCulture),
            ColumnToNumber(match.Groups["column"].Value));
    }

    public static string NormalizeReference(string reference) =>
        reference.Replace("$", "", StringComparison.Ordinal).ToUpperInvariant();

    public static string ToReference(int row, int column) =>
        $"{NumberToColumn(column)}{row}";

    public static int ColumnToNumber(string column)
    {
        var number = 0;
        foreach (var ch in column.ToUpperInvariant())
        {
            number = number * 26 + ch - 'A' + 1;
        }

        return number;
    }

    public static string NumberToColumn(int number)
    {
        var column = "";
        while (number > 0)
        {
            number--;
            column = (char)('A' + number % 26) + column;
            number /= 26;
        }

        return column;
    }
}

internal static partial class FormulaReferenceTranslator
{
    private static readonly Regex ReferenceRegex = new(
        @"(?<![A-Z0-9_])(?<colAbs>\$?)(?<column>[A-Z]{1,3})(?<rowAbs>\$?)(?<row>\d+)(?![A-Z0-9_])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Translate(string formula, CellAddress baseAddress, CellAddress targetAddress)
    {
        // 共享公式按目标格与基准格的行列偏移平移相对引用；带 $ 的绝对行或绝对列保持不变。
        // 正则边界避免把函数名、普通数字或更长标识符的一部分误识别为单元格地址。
        var rowOffset = targetAddress.Row - baseAddress.Row;
        var columnOffset = targetAddress.Column - baseAddress.Column;

        return ReferenceRegex.Replace(formula, match =>
        {
            var columnAbsolute = match.Groups["colAbs"].Value == "$";
            var rowAbsolute = match.Groups["rowAbs"].Value == "$";
            var column = CellAddress.ColumnToNumber(match.Groups["column"].Value);
            var row = int.Parse(match.Groups["row"].Value, CultureInfo.InvariantCulture);

            if (!columnAbsolute)
            {
                column += columnOffset;
            }

            if (!rowAbsolute)
            {
                row += rowOffset;
            }

            return $"{(columnAbsolute ? "$" : "")}{CellAddress.NumberToColumn(column)}{(rowAbsolute ? "$" : "")}{row}";
        });
    }
}
