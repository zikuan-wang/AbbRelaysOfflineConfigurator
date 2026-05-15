using System.IO;
using System.Text.Json;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Rex600FunctionCatalogService
{
    private const string CatalogFileName = "Rex600FunctionCatalog.json";
    private readonly Lazy<Rex600FunctionCatalogDocument> _catalog = new(LoadCatalog);

    public IReadOnlyList<Rex600FunctionEntry> GetFunctions() => _catalog.Value.Functions;

    public IReadOnlyList<Rex600FunctionEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetFunctions();
        }

        var filter = query.Trim();
        return GetFunctions()
            .Where(function =>
                function.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                function.EnglishName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                function.ChineseName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                function.Iec61850.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                function.Iec60617.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                function.AnsiCode.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static Rex600FunctionCatalogDocument LoadCatalog()
    {
        var path = ResolveCatalogPath();
        if (!File.Exists(path))
        {
            return new Rex600FunctionCatalogDocument();
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Rex600FunctionCatalogDocument>(
                   stream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new Rex600FunctionCatalogDocument();
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

public sealed class Rex600FunctionCatalogDocument
{
    public int FormatVersion { get; set; }
    public string Source { get; set; } = "";
    public List<Rex600FunctionEntry> Functions { get; set; } = [];
}

public sealed class Rex600FunctionEntry
{
    public string Category { get; set; } = "";
    public string EnglishName { get; set; } = "";
    public string ChineseName { get; set; } = "";
    public string Iec61850 { get; set; } = "";
    public string Iec60617 { get; set; } = "";
    public string AnsiCode { get; set; } = "";
    public int SourcePage { get; set; }
}
