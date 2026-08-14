using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class CnLegacyFunctionCatalogService
{
    private const string CatalogFileName = "CnLegacyFunctionCatalog.json";
    private static readonly Lazy<CnLegacyFunctionCatalogDocument> SharedCatalog = new(LoadCatalog);

    public IReadOnlyList<CnLegacyFunctionDeviceCatalog> Devices => SharedCatalog.Value.Devices;

    public IReadOnlyList<CnLegacyFunctionEntry> GetFunctions(string? deviceId = null)
    {
        var devices = string.IsNullOrWhiteSpace(deviceId)
            ? SharedCatalog.Value.Devices
            : SharedCatalog.Value.Devices.Where(device => device.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));

        return devices.SelectMany(device => device.Functions.Select(function => function with
        {
            DeviceId = device.DeviceId,
            SeriesId = device.SeriesId,
            DeviceName = device.DeviceName
        })).ToList();
    }

    public IReadOnlyList<CnLegacyFunctionEntry> Search(string? deviceId, string query, int limit = 18)
    {
        var token = Normalize(query);
        if (token.Length == 0)
        {
            return [];
        }

        return GetFunctions(deviceId)
            .Select(function => new { Function = function, Score = MatchScore(function, token) })
            .Where(item => item.Score < int.MaxValue)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Function.AbbCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Function.AnsiCode, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(item => item.Function)
            .ToList();
    }

    public CnLegacyFunctionEntry? ResolveExact(string? deviceId, string query)
    {
        var token = Normalize(query);
        if (token.Length == 0)
        {
            return null;
        }

        var matches = GetFunctions(deviceId)
            .Where(function =>
                Normalize(function.AbbCode).Equals(token, StringComparison.OrdinalIgnoreCase) ||
                ExpandAnsiSearchTerms(function.AnsiCode).Any(ansi => Normalize(ansi).Equals(token, StringComparison.OrdinalIgnoreCase)) ||
                Normalize(function.ChineseName).Equals(token, StringComparison.OrdinalIgnoreCase) ||
                Normalize(function.EnglishName).Equals(token, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(function => FunctionKey(function), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    public IReadOnlyList<CnLegacyStandardConfigurationRecommendation> Recommend(
        string deviceId,
        IEnumerable<CnLegacyFunctionEntry> requestedFunctions,
        IReadOnlyList<string> selectableCodes)
    {
        var device = SharedCatalog.Value.Devices.FirstOrDefault(item =>
            item.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return [];
        }

        var requested = requestedFunctions
            .Where(function => function.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(FunctionKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count == 0)
        {
            return [];
        }

        var selectable = selectableCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return device.StandardConfigurations
            .Select(config =>
            {
                var covered = requested
                    .Where(function => function.Configs.ContainsKey(config.Code))
                    .ToList();
                var missing = requested
                    .Where(function => !function.Configs.ContainsKey(config.Code))
                    .ToList();

                return new CnLegacyStandardConfigurationRecommendation(
                    device.DeviceId,
                    device.DeviceName,
                    config.Code,
                    config.Description,
                    covered,
                    missing,
                    selectable.Contains(config.Code));
            })
            .Where(item => item.CoveredFunctions.Count > 0)
            .OrderBy(item => item.MissingFunctions.Count)
            .ThenByDescending(item => item.CoveredFunctions.Count)
            .ThenBy(item => ConfigurationSortIndex(device.StandardConfigurations, item.ConfigCode))
            .ToList();
    }

    public CnLegacyFunctionDeviceCatalog? GetDeviceCatalog(string deviceId) =>
        SharedCatalog.Value.Devices.FirstOrDefault(device =>
            device.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> SplitSearchInput(string input) =>
        Regex.Split(input ?? "", @"[\r\n,，;；]+")
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

    public static string FunctionKey(CnLegacyFunctionEntry function) =>
        $"{function.DeviceId}|{function.AbbCode}|{function.AnsiCode}|{function.ChineseName}";

    private static int ConfigurationSortIndex(IReadOnlyList<CnLegacyStandardConfigurationEntry> configs, string code)
    {
        for (var index = 0; index < configs.Count; index++)
        {
            if (configs[index].Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static int MatchScore(CnLegacyFunctionEntry function, string token)
    {
        var abbCode = Normalize(function.AbbCode);
        if (abbCode.Equals(token, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var ansiTerms = ExpandAnsiSearchTerms(function.AnsiCode)
            .Select(Normalize)
            .Where(term => term.Length > 0)
            .ToList();
        if (ansiTerms.Any(ansi => ansi.Equals(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        if (IsNumericAnsiToken(token) && ansiTerms.Any(ansi => ansi.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        if (ansiTerms.Any(ansi => ansi.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        if (abbCode.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith(abbCode, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (Normalize(function.ChineseName).Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (Normalize(function.EnglishName).Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (Normalize(function.Category).Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 7;
        }

        return int.MaxValue;
    }

    private static IEnumerable<string> ExpandAnsiSearchTerms(string ansi)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in Regex.Split(ansi ?? "", @"[\s,，、+]+"))
        {
            var term = raw.Trim();
            if (term.Length == 0)
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
                var expanded = Regex.IsMatch(part, @"^\d", RegexOptions.CultureInvariant)
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
        if (cleaned.Length == 0)
        {
            return;
        }

        terms.Add(cleaned);
        terms.Add(cleaned.Replace("/", "", StringComparison.Ordinal));
        terms.Add(cleaned.Replace("-", "", StringComparison.Ordinal));
    }

    private static bool IsNumericAnsiToken(string token) =>
        Regex.IsMatch(token, @"^\d+[A-Z_]*$", RegexOptions.CultureInvariant);

    private static string Normalize(string? value) =>
        Regex.Replace(value ?? "", @"\s+", "", RegexOptions.CultureInvariant).Trim().ToUpperInvariant();

    private static CnLegacyFunctionCatalogDocument LoadCatalog()
    {
        var path = ResolveCatalogPath();
        if (!File.Exists(path))
        {
            return new CnLegacyFunctionCatalogDocument();
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<CnLegacyFunctionCatalogDocument>(
                   stream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new CnLegacyFunctionCatalogDocument();
    }

    private static string ResolveCatalogPath()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Data", CatalogFileName),
            Path.Combine(Environment.CurrentDirectory, "Data", CatalogFileName)
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "AbbRelaysOfflineConfigurator", "Data", CatalogFileName));
            candidates.Add(Path.Combine(current.FullName, "Data", CatalogFileName));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}

public sealed class CnLegacyFunctionCatalogDocument
{
    public int FormatVersion { get; set; }
    public string Source { get; set; } = "";
    public List<CnLegacyFunctionDeviceCatalog> Devices { get; set; } = [];
}

public sealed class CnLegacyFunctionDeviceCatalog
{
    public string DeviceId { get; set; } = "";
    public string SeriesId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public List<CnLegacyStandardConfigurationEntry> StandardConfigurations { get; set; } = [];
    public List<CnLegacyFunctionEntry> Functions { get; set; } = [];
}

public sealed class CnLegacyStandardConfigurationEntry
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed record CnLegacyFunctionEntry
{
    public string DeviceId { get; init; } = "";
    public string SeriesId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public string Category { get; init; } = "";
    public string ChineseName { get; init; } = "";
    public string EnglishName { get; init; } = "";
    public string AbbCode { get; init; } = "";
    public string AnsiCode { get; init; } = "";
    public Dictionary<string, string> Configs { get; init; } = [];
    public int SourcePage { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(EnglishName)
        ? ChineseName
        : $"{ChineseName} / {EnglishName}";

    public string CodeSummary => string.IsNullOrWhiteSpace(AnsiCode)
        ? AbbCode
        : $"{AbbCode} / {AnsiCode}";
}

public sealed record CnLegacyStandardConfigurationRecommendation(
    string DeviceId,
    string DeviceName,
    string ConfigCode,
    string ConfigDescription,
    IReadOnlyList<CnLegacyFunctionEntry> CoveredFunctions,
    IReadOnlyList<CnLegacyFunctionEntry> MissingFunctions,
    bool CanApply)
{
    public bool IsFullMatch => MissingFunctions.Count == 0;
    public string MatchStatus => IsFullMatch
        ? $"完全覆盖 {CoveredFunctions.Count} 项功能"
        : $"覆盖 {CoveredFunctions.Count} 项，缺少 {MissingFunctions.Count} 项";
    public string CoveredSummary => string.Join("；", CoveredFunctions.Select(function => function.CodeSummary));
    public string MissingSummary => string.Join("；", MissingFunctions.Select(function => function.CodeSummary));
    public string ApplyHint => CanApply ? "应用配置" : "当前订货码无此标准配置位";
}
