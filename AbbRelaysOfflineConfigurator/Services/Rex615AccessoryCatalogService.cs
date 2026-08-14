using System.IO;
using System.Text.Json;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Rex615AccessoryCatalogService
{
    private const string CatalogFileName = "Rex615Accessories.json";
    private static readonly Lazy<string> SharedCatalogPath = new(ResolveCatalogPath);
    private static readonly Lazy<IReadOnlyList<Rex615AccessoryCatalogItem>> SharedItems = new(
        () => LoadItems(SharedCatalogPath.Value));

    public Rex615AccessoryCatalogService()
    {
        CatalogPath = SharedCatalogPath.Value;
    }

    public string CatalogPath { get; }

    public IReadOnlyList<Rex615AccessoryCatalogItem> GetItems(string query = "")
    {
        var items = SharedItems.Value;
        if (string.IsNullOrWhiteSpace(query))
        {
            return items;
        }

        var tokens = query
            .Split([' ', '\t', '\r', '\n', ',', ';', '，', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return items;
        }

        return items
            .Where(item => tokens.All(token => Matches(item, token)))
            .ToList();
    }

    private static bool Matches(Rex615AccessoryCatalogItem item, string token) =>
        item.Category.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        item.CategoryZh.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        item.Product.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        item.Description.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        item.DescriptionZh.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        item.Add.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Rex615AccessoryCatalogItem> LoadItems(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<Rex615AccessoryCatalogDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new Rex615AccessoryCatalogDocument();

        return document.Categories
            .SelectMany(category => category.Items.Select(item => new Rex615AccessoryCatalogItem(
                category.Name,
                category.NameZh,
                item.Product,
                item.Image,
                item.Description,
                item.DescriptionZh,
                item.Add)))
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Product, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

public sealed record Rex615AccessoryCatalogItem(
    string Category,
    string CategoryZh,
    string Product,
    string Image,
    string Description,
    string DescriptionZh,
    string Add)
{
    public string CategoryDisplay => string.IsNullOrWhiteSpace(CategoryZh) || CategoryZh.Equals(Category, StringComparison.OrdinalIgnoreCase)
        ? Category
        : $"{CategoryZh} / {Category}";

    public bool HasImage => !string.IsNullOrWhiteSpace(Image);
    public string AddDisplay => string.IsNullOrWhiteSpace(Add) ? "-" : Add;
}

public sealed class Rex615AccessoryCatalogDocument
{
    public int FormatVersion { get; set; }
    public string Source { get; set; } = "";
    public List<Rex615AccessoryCategoryDocument> Categories { get; set; } = [];
}

public sealed class Rex615AccessoryCategoryDocument
{
    public string Name { get; set; } = "";
    public string NameZh { get; set; } = "";
    public List<Rex615AccessoryItemDocument> Items { get; set; } = [];
}

public sealed class Rex615AccessoryItemDocument
{
    public string Product { get; set; } = "";
    public string Image { get; set; } = "";
    public string Description { get; set; } = "";
    public string DescriptionZh { get; set; } = "";
    public string Add { get; set; } = "";
}
