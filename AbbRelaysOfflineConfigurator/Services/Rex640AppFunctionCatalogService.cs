using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Rex640AppFunctionCatalogService
{
    private const string CatalogFileName = "Rex640AppFunctionCatalog.json";

    public IReadOnlyList<string> AppPriority { get; } =
    [
        "APP1", "APP2", "APP3", "APP4", "APP5", "APP6", "APP7", "ADD1",
        "APP8", "ADD2", "APP9", "APP10", "APP11", "APP12", "APP13", "APP14",
        "APP51", "APP52", "APP53"
    ];

    private readonly Lazy<Rex640AppFunctionCatalogDocument> _catalog = new(LoadCatalog);

    public IReadOnlyList<Rex640FunctionEntry> GetFunctions(string pclVersion) =>
        _catalog.Value.Functions
            .Where(function => IsVersion(function.Pcl, pclVersion))
            .OrderBy(function => function.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(function => PriorityIndex(function.Apps.FirstOrDefault() ?? "Base"))
            .ThenBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<Rex640FunctionEntry> Search(string pclVersion, string query, int maxCount = 20)
    {
        var token = Normalize(query);
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        return GetFunctions(pclVersion)
            .Select(function => new { Function = function, Score = MatchScore(function, token) })
            .Where(item => item.Score < int.MaxValue)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Function.Code, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(item => item.Function)
            .ToList();
    }

    public Rex640FunctionEntry? ResolveExact(string pclVersion, string query)
    {
        var token = Normalize(query);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var matches = GetFunctions(pclVersion)
            .Where(function =>
                Normalize(function.Code).Equals(token, StringComparison.OrdinalIgnoreCase) ||
                ExpandSearchTerms(function.Ansi).Any(term => Normalize(term).Equals(token, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    public Rex640AppRecommendationResult Recommend(string pclVersion, IReadOnlyCollection<string> functionCodes)
    {
        var functions = functionCodes
            .Select(code => GetFunctions(pclVersion).FirstOrDefault(function => function.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            .Where(function => function is not null)
            .Cast<Rex640FunctionEntry>()
            .DistinctBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var requirements = functions.Where(function => !function.IsBase && function.Apps.Count > 0).ToList();
        var baseFunctions = functions
            .Where(function => function.IsBase || function.Apps.Count == 0)
            .Select(function => function.Code)
            .ToList();
        if (requirements.Count == 0)
        {
            return new Rex640AppRecommendationResult([], baseFunctions);
        }

        var candidates = requirements
            .SelectMany(function => function.Apps)
            .SelectMany(ExpandDependencies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(PriorityIndex)
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
                if ((mask & (1 << index)) == 0)
                {
                    continue;
                }

                foreach (var app in ExpandDependencies(candidates[index]))
                {
                    selected.Add(app);
                }
            }

            if (!requirements.All(function => function.Apps.Any(selected.Contains)))
            {
                continue;
            }

            var selectedApps = selected.OrderBy(PriorityIndex).ToList();
            var score = selectedApps.Count;
            var priority = selectedApps.Sum(PriorityIndex);
            if (best is null || score < bestScore || score == bestScore && priority < bestPriority)
            {
                best = selectedApps;
                bestScore = score;
                bestPriority = priority;
            }
        }

        return new Rex640AppRecommendationResult(
            (best ?? []).Select(app => new Rex640RecommendedApp(
                    app,
                    requirements.Where(function => function.Apps.Contains(app, StringComparer.OrdinalIgnoreCase))
                        .Select(function => function.Code)
                        .ToList()))
                .ToList(),
            baseFunctions);
    }

    private static Rex640AppFunctionCatalogDocument LoadCatalog()
    {
        var path = ResolveCatalogPath();
        if (!File.Exists(path))
        {
            return new Rex640AppFunctionCatalogDocument();
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Rex640AppFunctionCatalogDocument>(
                   stream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new Rex640AppFunctionCatalogDocument();
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

    private static bool IsVersion(string functionVersion, string requestedVersion) =>
        NormalizeVersion(functionVersion).Equals(NormalizeVersion(requestedVersion), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVersion(string version) =>
        string.IsNullOrWhiteSpace(version) ? "PCL6" : version.Trim().ToUpperInvariant();

    private static int MatchScore(Rex640FunctionEntry function, string token)
    {
        var terms = new[]
            {
                function.Code,
                function.Ansi,
                function.Iec60617,
                function.ChineseName,
                function.EnglishName,
                function.Category,
                function.CategoryChinese
            }
            .Concat(function.Apps)
            .SelectMany(ExpandSearchTerms)
            .Select(Normalize)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (terms.Any(term => term.Equals(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        if (terms.Any(term => term.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        if (terms.Any(term => term.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                              (term.Length >= 3 && token.Contains(term, StringComparison.OrdinalIgnoreCase))))
        {
            return 2;
        }

        return int.MaxValue;
    }

    private static IEnumerable<string> ExpandSearchTerms(string value)
    {
        foreach (var chunk in (value ?? "").Split([',', ';', '，', '；', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return chunk;
            if (!chunk.Contains('/'))
            {
                continue;
            }

            var parts = chunk.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                yield return part;
            }

            var prefixMatch = Regex.Match(parts[0], @"^(?<number>\d+)");
            if (!prefixMatch.Success)
            {
                continue;
            }

            var number = prefixMatch.Groups["number"].Value;
            foreach (var part in parts.Skip(1).Where(part => part.All(char.IsLetter)))
            {
                yield return number + part;
            }
        }
    }

    private static string Normalize(string value) =>
        new((value ?? "")
            .Trim()
            .ToUpperInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch >= 0x4E00)
            .ToArray());

    private int PriorityIndex(string app)
    {
        for (var index = 0; index < AppPriority.Count; index++)
        {
            if (AppPriority[index].Equals(app, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static IEnumerable<string> ExpandDependencies(string app)
    {
        if (app.Equals("ADD1", StringComparison.OrdinalIgnoreCase))
        {
            yield return "APP7";
        }

        if (app.Equals("ADD2", StringComparison.OrdinalIgnoreCase))
        {
            yield return "APP8";
        }

        yield return app;
    }
}

public sealed class Rex640AppFunctionCatalogDocument
{
    public int FormatVersion { get; set; }
    public string Source { get; set; } = "";
    public List<Rex640FunctionEntry> Functions { get; set; } = [];
}

public sealed class Rex640FunctionEntry
{
    public string Pcl { get; set; } = "";
    public string Code { get; set; } = "";
    public string Ansi { get; set; } = "";
    public string Iec60617 { get; set; } = "";
    public string ChineseName { get; set; } = "";
    public string EnglishName { get; set; } = "";
    public string Category { get; set; } = "";
    public string CategoryChinese { get; set; } = "";
    public int Pcs { get; set; }
    public List<string> Apps { get; set; } = [];
    public bool IsBase { get; set; }
    public string Description { get; set; } = "";
    public int SourcePage { get; set; }
}

public sealed record Rex640RecommendedApp(string Id, IReadOnlyList<string> CoveredFunctions);

public sealed record Rex640AppRecommendationResult(
    IReadOnlyList<Rex640RecommendedApp> Apps,
    IReadOnlyList<string> BaseFunctions);
