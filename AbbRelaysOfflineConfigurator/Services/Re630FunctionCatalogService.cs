using System.IO;
using System.Text.Json;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Re630FunctionCatalogService
{
    private const string CatalogFileName = "Re630FunctionCatalog.json";
    private static readonly Lazy<Re630FunctionCatalogDocument> SharedCatalog = new(LoadCatalog);

    public IReadOnlyList<string> Devices =>
        SharedCatalog.Value.Functions
            .Select(function => function.Device)
            .Where(device => !string.IsNullOrWhiteSpace(device))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(device => device, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<Re630FunctionEntry> GetFunctions(string? device = null)
    {
        return SharedCatalog.Value.Functions
            .Where(function => string.IsNullOrWhiteSpace(device) ||
                               device.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                               function.Device.Equals(device, StringComparison.OrdinalIgnoreCase))
            .OrderBy(function => function.Device, StringComparer.OrdinalIgnoreCase)
            .ThenBy(function => CategoryOrder(function.Category))
            .ThenBy(function => function.Iec61850, StringComparer.OrdinalIgnoreCase)
            .ThenBy(function => function.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<Re630FunctionEntry> Search(string? device, string query)
    {
        var token = (query ?? "").Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return GetFunctions(device);
        }

        return GetFunctions(device)
            .Where(function => Matches(function, token))
            .ToList();
    }

    private static bool Matches(Re630FunctionEntry function, string token)
    {
        return function.Device.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               function.Category.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               function.Description.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               function.Iec61850.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               function.Iec60617.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               function.Ansi.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               function.Source.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               function.Page.ToString().Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static Re630FunctionCatalogDocument LoadCatalog()
    {
        var path = ResolveCatalogPath();
        if (!File.Exists(path))
        {
            return new Re630FunctionCatalogDocument();
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Re630FunctionCatalogDocument>(
                   stream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new Re630FunctionCatalogDocument();
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

    private static int CategoryOrder(string category) => category switch
    {
        "Protection" => 0,
        "Protection-related functions" => 1,
        "Control" => 2,
        "Generic process I/O" => 3,
        "Supervision and monitoring" => 4,
        "Power quality" => 5,
        "Measurement" => 6,
        "Station communication (GOOSE)" => 7,
        _ => 99
    };
}

public sealed class Re630FunctionCatalogDocument
{
    public int FormatVersion { get; set; }
    public string Source { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public List<Re630FunctionEntry> Functions { get; set; } = [];
}

public sealed class Re630FunctionEntry
{
    public string Device { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Iec61850 { get; set; } = "";
    public string Iec60617 { get; set; } = "";
    public string Ansi { get; set; } = "";
    public string Source { get; set; } = "";
    public int Page { get; set; }
}
