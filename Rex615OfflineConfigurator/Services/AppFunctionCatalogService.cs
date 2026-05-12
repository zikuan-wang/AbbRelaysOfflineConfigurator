using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rex615OfflineConfigurator.Services;

public sealed class AppFunctionCatalogService
{
    private const string CatalogFileName = "AppFunctionCatalog.json";
    private readonly Lazy<AppFunctionCatalogDocument> _catalog;

    public AppFunctionCatalogService()
    {
        CatalogPath = ResolveCatalogPath();
        _catalog = new Lazy<AppFunctionCatalogDocument>(() => Load(CatalogPath));
    }

    public string CatalogPath { get; }

    public IReadOnlyList<AppFunctionEntry> Search(string version, string query, int limit = 20)
    {
        var token = NormalizeSearchToken(query);
        if (token.Length == 0)
        {
            return [];
        }

        return GetFunctions(version)
            .Select(function => new
            {
                Function = function,
                Score = MatchScore(function, token)
            })
            .Where(item => item.Score < int.MaxValue)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Function.Code, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(item => item.Function)
            .ToList();
    }

    public AppFunctionEntry? ResolveExact(string version, string query)
    {
        var token = NormalizeSearchToken(query);
        if (token.Length == 0)
        {
            return null;
        }

        var functions = GetFunctions(version);
        var matches = functions
            .Where(function =>
                NormalizeSearchToken(function.Code) == token ||
                ExpandAnsiSearchTerms(function.Ansi).Any(ansi => NormalizeSearchToken(ansi) == token))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public AppFunctionEntry? Resolve(string version, string query)
    {
        var token = NormalizeSearchToken(query);
        if (token.Length == 0)
        {
            return null;
        }

        var functions = GetFunctions(version);
        return functions.FirstOrDefault(function => NormalizeSearchToken(function.Code) == token)
            ?? functions.FirstOrDefault(function => ExpandAnsiSearchTerms(function.Ansi).Any(ansi => NormalizeSearchToken(ansi) == token))
            ?? Search(version, query, 1).FirstOrDefault();
    }

    public AppRecommendationResult Recommend(string version, IReadOnlyCollection<string> functionCodes)
    {
        var functions = functionCodes
            .Select(code => GetFunctions(version).FirstOrDefault(function => function.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            .Where(function => function is not null)
            .Cast<AppFunctionEntry>()
            .DistinctBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var baseFunctions = functions.Where(function => function.IsBase || function.Apps.Count == 0).ToList();
        var requirements = functions.Where(function => !function.IsBase && function.Apps.Count > 0).ToList();
        if (requirements.Count == 0)
        {
            return new AppRecommendationResult([], baseFunctions, []);
        }

        var candidates = requirements
            .SelectMany(function => function.Apps)
            .SelectMany(ExpandDependencies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(AppPriorityIndex)
            .ToList();

        List<string>? best = null;
        var bestScore = int.MaxValue;
        var bestPriority = int.MaxValue;
        var totalMasks = 1 << candidates.Count;
        for (var mask = 1; mask < totalMasks; mask++)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < candidates.Count; index++)
            {
                if ((mask & (1 << index)) != 0)
                {
                    foreach (var app in ExpandDependencies(candidates[index]))
                    {
                        selected.Add(app);
                    }
                }
            }

            if (!requirements.All(function => function.Apps.Any(selected.Contains)))
            {
                continue;
            }

            var selectedApps = selected.OrderBy(AppPriorityIndex).ToList();
            var score = selectedApps.Count;
            var priority = selectedApps.Sum(AppPriorityIndex);
            if (best is null || score < bestScore || score == bestScore && priority < bestPriority)
            {
                best = selectedApps;
                bestScore = score;
                bestPriority = priority;
            }
        }

        var recommendedApps = (best ?? [])
            .Select(app => new RecommendedAppEntry(
                app,
                requirements
                    .Where(function => function.Apps.Contains(app, StringComparer.OrdinalIgnoreCase))
                    .Select(function => function.Code)
                    .ToList()))
            .ToList();

        var unresolved = requirements
            .Where(function => !recommendedApps.Any(app => function.Apps.Contains(app.Id, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        return new AppRecommendationResult(recommendedApps, baseFunctions, unresolved);
    }

    public IReadOnlyList<AppFunctionEntry> GetFunctions(string version)
    {
        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? "PCL1" : version.Trim();
        return _catalog.Value.Versions
            .FirstOrDefault(item => item.Version.Equals(normalizedVersion, StringComparison.OrdinalIgnoreCase))
            ?.Functions ?? _catalog.Value.Versions.FirstOrDefault()?.Functions ?? [];
    }

    public IReadOnlyList<string> AppPriority => _catalog.Value.AppPriority;

    private static AppFunctionCatalogDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AppFunctionCatalogDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new AppFunctionCatalogDocument();
    }

    private int AppPriorityIndex(string app)
    {
        var index = AppPriority
            .Select((value, position) => new { value, position })
            .FirstOrDefault(item => item.value.Equals(app, StringComparison.OrdinalIgnoreCase))
            ?.position;
        return index ?? int.MaxValue / 2;
    }

    private static IEnumerable<string> ExpandDependencies(string app)
    {
        if (app.Equals("ADD1", StringComparison.OrdinalIgnoreCase) ||
            app.Equals("ADD2", StringComparison.OrdinalIgnoreCase))
        {
            yield return "APP9";
        }

        yield return app;
    }

    private static int MatchScore(AppFunctionEntry function, string token)
    {
        var code = NormalizeSearchToken(function.Code);
        if (code == token)
        {
            return 0;
        }

        if (ExpandAnsiSearchTerms(function.Ansi).Any(ansi => NormalizeSearchToken(ansi) == token))
        {
            return 1;
        }

        var ansiTerms = ExpandAnsiSearchTerms(function.Ansi)
            .Select(NormalizeSearchToken)
            .ToList();
        if (IsNumericAnsiToken(token) && ansiTerms.Any(ansi => ansi.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        if (ansiTerms.Any(ansi => ansi.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        if (code.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        var chinese = NormalizeSearchToken(function.ChineseName);
        if (chinese.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (function.ChineseAliases.Any(alias => NormalizeSearchToken(alias).Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 6;
        }

        var principle = NormalizeSearchToken(function.PrincipleSummary);
        if (principle.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 7;
        }

        var english = NormalizeSearchToken(function.EnglishName);
        if (english.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 8;
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

    private static string NormalizeSearchToken(string value) =>
        Regex.Replace(value.Trim(), @"\s+", "", RegexOptions.CultureInvariant).ToUpperInvariant();

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
            candidates.Add(Path.Combine(current.FullName, "Rex615OfflineConfigurator", "Data", CatalogFileName));
            candidates.Add(Path.Combine(current.FullName, "Data", CatalogFileName));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}

public sealed class AppFunctionCatalogDocument
{
    public int FormatVersion { get; set; }
    public string Source { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public List<string> AppPriority { get; set; } = [];
    public List<AppFunctionVersionCatalog> Versions { get; set; } = [];
}

public sealed class AppFunctionVersionCatalog
{
    public string Version { get; set; } = "";
    public List<AppFunctionEntry> Functions { get; set; } = [];
}

public sealed class AppFunctionEntry
{
    public string Code { get; set; } = "";
    public string Ansi { get; set; } = "";
    public string Iec60617 { get; set; } = "";
    public string EnglishName { get; set; } = "";
    public string ChineseName { get; set; } = "";
    public string Category { get; set; } = "";
    public int Pcs { get; set; }
    public bool IsBase { get; set; }
    public List<string> Apps { get; set; } = [];
    public List<string> ChineseAliases { get; set; } = [];
    public string FunctionalityCode { get; set; } = "";
    public string PrincipleSummary { get; set; } = "";
    public string PrincipleSource { get; set; } = "";
    public string PrincipleSourceUrl { get; set; } = "";
}

public sealed record RecommendedAppEntry(string Id, IReadOnlyList<string> CoveredFunctions);

public sealed record AppRecommendationResult(
    IReadOnlyList<RecommendedAppEntry> Apps,
    IReadOnlyList<AppFunctionEntry> BaseFunctions,
    IReadOnlyList<AppFunctionEntry> UnresolvedFunctions);
