using System.Collections;
using System.IO;
using System.Resources;
using System.Xml.Linq;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Re611RuleLoader
{
    private static readonly Lazy<Re611RuleCatalog> SharedCatalog = new(LoadCore);

    public Re611RuleCatalog Load() => SharedCatalog.Value;

    private static Re611RuleCatalog LoadCore()
    {
        var dataDirectory = ResolveDataDirectory();
        var ruleSets = Directory.GetFiles(dataDirectory, "RE* 611_*.xml")
            .Select(LoadRuleSet)
            .OrderBy(ruleSet => DeviceSortOrder(ruleSet.DeviceId))
            .ToList();

        if (ruleSets.Count == 0)
        {
            throw new FileNotFoundException("No RE_611 XML files were found.", dataDirectory);
        }

        return new Re611RuleCatalog(dataDirectory, ruleSets);
    }

    private static Re611RuleSet LoadRuleSet(string path)
    {
        var resources = LoadResources(Path.Combine(
            Path.GetDirectoryName(path) ?? "",
            Path.GetFileNameWithoutExtension(path) + "_en.resources"));
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException($"{Path.GetFileName(path)} is empty.");
        var defaultOrderCode = ((string?)root.Element("Default")?.Attribute("OrderCode") ?? "").Trim();

        var versions = root.Element("OrderCodeVersions")?
            .Elements("Version")
            .Select((element, index) =>
            {
                var code = ((string?)element.Attribute("Id") ?? "").Trim();
                var productVersion = ((string?)element.Attribute("IED_version") ?? "").Trim();
                return new Re611VersionRule(
                    Code: code,
                    ProductVersion: productVersion,
                    Description: ResolveResource(resources, $"Versions_{code}", $"Product version {productVersion}"),
                    SortOrder: index);
            })
            .Where(version => !string.IsNullOrWhiteSpace(version.Code))
            .ToList() ?? [];

        var groups = new List<Re611GroupRule>();
        var options = new List<Re611OptionRule>();
        var orderCodes = root.Element("OrderCodes");
        if (orderCodes is not null)
        {
            var index = 0;
            foreach (var digit in orderCodes.Elements("Digit"))
            {
                var groupName = ((string?)digit.Attribute("Group") ?? "").Trim();
                var location = ((string?)digit.Attribute("Location") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var title = digit.Nodes().OfType<XComment>().FirstOrDefault()?.Value.Trim();
                groups.Add(new Re611GroupRule(location, groupName, index, title ?? groupName));

                var optionIndex = 0;
                foreach (var option in digit.Elements("Option"))
                {
                    var code = ((string?)option.Attribute("Id") ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    var descriptionKey = ((string?)option.Attribute("Description") ?? "").Trim();
                    options.Add(new Re611OptionRule(
                        GroupName: groupName,
                        Location: location,
                        Code: code,
                        Description: ResolveResource(resources, descriptionKey, descriptionKey),
                        Version: ((string?)option.Attribute("Version") ?? "*").Trim(),
                        SortOrder: optionIndex++));
                }

                index++;
            }
        }

        var validationRules = root.Element("ValidOrderCodes")?
            .Elements()
            .SelectMany(category => category.Elements("Rule").Select(rule => new Re611ValidationRule(
                Category: category.Name.LocalName,
                Version: ((string?)rule.Attribute("Version") ?? "*").Trim(),
                Pattern: (rule.Value ?? "").Trim())))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList() ?? [];

        var configurations = root.Element("Configurations")?
            .Elements("Configuration")
            .Select((element, index) => new Re611ConfigurationRule(
                Name: ((string?)element.Attribute("Name") ?? "").Trim(),
                Version: ((string?)element.Attribute("Version") ?? "").Trim(),
                Edition: ((string?)element.Attribute("Edition") ?? "").Trim(),
                OrderCodePattern: ((string?)element.Element("OrderCode")?.Attribute("Name") ?? "").Trim(),
                Description: element.Nodes().OfType<XComment>().FirstOrDefault()?.Value.Trim().Trim('"') ?? "",
                SortOrder: index))
            .Where(config => !string.IsNullOrWhiteSpace(config.Name))
            .ToList() ?? [];

        var deviceOption = options.FirstOrDefault(option => option.GroupName.Equals("MainApps", StringComparison.OrdinalIgnoreCase));
        var deviceId = deviceOption?.Code ?? DeviceIdFromFile(path);

        return new Re611RuleSet(
            SourcePath: path,
            FileName: Path.GetFileName(path),
            DeviceId: deviceId,
            DeviceDescription: deviceOption?.Description ?? deviceId,
            DefaultOrderCode: defaultOrderCode,
            Versions: versions,
            Groups: groups,
            Options: options,
            ValidationRules: validationRules,
            Configurations: configurations);
    }

    private static Dictionary<string, string> LoadResources(string resourcePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(resourcePath))
        {
            return result;
        }

        using var reader = new ResourceReader(resourcePath);
        foreach (DictionaryEntry entry in reader)
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string ResolveResource(IReadOnlyDictionary<string, string> resources, string key, string fallback) =>
        !string.IsNullOrWhiteSpace(key) && resources.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string DeviceIdFromFile(string path) =>
        Path.GetFileNameWithoutExtension(path).Replace(" ", "", StringComparison.OrdinalIgnoreCase).Split('_')[0].ToUpperInvariant();

    private static int DeviceSortOrder(string deviceId) => deviceId.ToUpperInvariant() switch
    {
        "REF611" => 0,
        "REM611" => 1,
        "REB611" => 2,
        "REU611" => 3,
        _ => 99
    };

    private static string ResolveDataDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "RE_611"),
            Path.Combine(AppContext.BaseDirectory, "Data", "RE_611", "XML"),
            Path.Combine(Environment.CurrentDirectory, "Data", "RE_611"),
            Path.Combine(Environment.CurrentDirectory, "RE_611", "XML")
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "Data", "RE_611"));
            candidates.Add(Path.Combine(current.FullName, "RE_611", "XML"));
            candidates.Add(Path.Combine(current.FullName, "AbbRelaysOfflineConfigurator", "Data", "RE_611"));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(path =>
                   Directory.Exists(path) &&
                   Directory.EnumerateFiles(path, "RE* 611_*.xml").Any())
               ?? candidates[0];
    }
}

public sealed record Re611RuleCatalog(string SourceDirectory, IReadOnlyList<Re611RuleSet> RuleSets);

public sealed record Re611RuleSet(
    string SourcePath,
    string FileName,
    string DeviceId,
    string DeviceDescription,
    string DefaultOrderCode,
    IReadOnlyList<Re611VersionRule> Versions,
    IReadOnlyList<Re611GroupRule> Groups,
    IReadOnlyList<Re611OptionRule> Options,
    IReadOnlyList<Re611ValidationRule> ValidationRules,
    IReadOnlyList<Re611ConfigurationRule> Configurations)
{
    public IReadOnlyList<Re611OptionRule> OptionsFor(string groupName, string versionCode)
    {
        return Options
            .Where(option => option.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase) &&
                             option.AppliesToVersion(versionCode))
            .GroupBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(option => option.SortOrder).First())
            .OrderBy(option => option.SortOrder)
            .ToList();
    }
}

public sealed record Re611VersionRule(string Code, string ProductVersion, string Description, int SortOrder)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ProductVersion)
        ? Description
        : $"{ProductVersion} ({Code})";
}

public sealed record Re611GroupRule(string Location, string GroupName, int SortOrder, string Title);

public sealed record Re611OptionRule(
    string GroupName,
    string Location,
    string Code,
    string Description,
    string Version,
    int SortOrder)
{
    public bool AppliesToVersion(string versionCode) =>
        string.IsNullOrWhiteSpace(Version) ||
        Version.Equals("*", StringComparison.OrdinalIgnoreCase) ||
        Version.Equals(versionCode, StringComparison.OrdinalIgnoreCase);
}

public sealed record Re611ValidationRule(string Category, string Version, string Pattern)
{
    public bool AppliesToVersion(string versionCode) =>
        string.IsNullOrWhiteSpace(Version) ||
        Version.Equals("*", StringComparison.OrdinalIgnoreCase) ||
        Version.Equals(versionCode, StringComparison.OrdinalIgnoreCase);
}

public sealed record Re611ConfigurationRule(
    string Name,
    string Version,
    string Edition,
    string OrderCodePattern,
    string Description,
    int SortOrder);
