using System.IO;
using System.Xml.Linq;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Rio600RuleLoader
{
    private const string RulesFileName = "RIO600_1.8.xml";
    private static readonly Lazy<Rio600RuleSet> SharedRules = new(LoadCore);

    public Rio600RuleSet Load() => SharedRules.Value;

    private static Rio600RuleSet LoadCore()
    {
        var path = ResolveRulesPath();
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException("RIO600 rule file is empty.");

        var digits = root.Element("OrderCodes")?
            .Elements("Digit")
            .Select(digit => new Rio600Digit(
                Location: ParseInt((string?)digit.Attribute("Location")),
                Group: (string?)digit.Attribute("Group") ?? "",
                Options: digit.Elements("Option")
                    .Select(option => new Rio600Option(
                        Value: (string?)option.Attribute("value") ?? "",
                        Name: (string?)option.Attribute("name") ?? "",
                        Version: (string?)option.Attribute("Version") ?? "*",
                        Description: (string?)option.Attribute("Description") ?? "",
                        ManageType: (string?)option.Attribute("ManageType") ?? ""))
                    .ToList()))
            .Where(digit => !string.IsNullOrWhiteSpace(digit.Group))
            .ToDictionary(digit => digit.Group, StringComparer.OrdinalIgnoreCase)
            ?? [];

        var validPositionRules = root.Element("ValidOrderCodes")?
            .Element("Position")?
            .Elements("Rule")
            .Select(rule => new Rio600ValidCodeRule(
                Code: (rule.Value ?? "").Trim(),
                Version: (string?)rule.Attribute("Version") ?? "*"))
            .Where(rule => rule.Code.Length == 3)
            .ToList() ?? [];

        var configurations = root.Element("Configurations")?
            .Elements("Configuration")
            .Select(configuration =>
            {
                var defaultOrderCode = (string?)configuration.Element("Default")?.Attribute("OrderCode") ?? "";
                var ioModules = configuration.Element("IOModules");
                return new Rio600Configuration(
                    CommunicationCode: defaultOrderCode.Length >= 9 ? defaultOrderCode.Substring(6, 3) : "",
                    DefaultOrderCode: defaultOrderCode,
                    ChannelLimit: ParseInt((string?)ioModules?.Attribute("channelLimit")),
                    MaxChannels: ParseInt((string?)ioModules?.Attribute("maxChannels")),
                    PointsLimit: ParseInt((string?)ioModules?.Attribute("pointsLimit")),
                    MaxPoints: ParseInt((string?)ioModules?.Attribute("maxPoints")),
                    Modules: ioModules?.Elements("IOModule")
                        .Select(module => new Rio600IoModuleRule(
                            Name: (string?)module.Attribute("name") ?? "",
                            Char: (string?)module.Attribute("char") ?? "",
                            Channels: ParseInt((string?)module.Attribute("channel")),
                            Points: ParseInt((string?)module.Attribute("points"))))
                        .Where(module => !string.IsNullOrWhiteSpace(module.Char))
                        .GroupBy(module => module.Char, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
                        ?? []);
            })
            .Where(configuration => !string.IsNullOrWhiteSpace(configuration.CommunicationCode))
            .ToList() ?? [];

        return new Rio600RuleSet(
            SourcePath: path,
            DefaultOrderCode: (string?)root.Element("Default")?.Attribute("OrderCode") ?? "",
            Versions: root.Element("OrderCodeVersions")?.Elements("Version")
                .Select(version => new Rio600Version(
                    Id: (string?)version.Attribute("Id") ?? "",
                    IedVersion: (string?)version.Attribute("IED_version") ?? "",
                    ConpackVersion: (string?)version.Attribute("Conpack_version") ?? ""))
                .ToList() ?? [],
            Digits: digits,
            ValidPositionRules: validPositionRules,
            Configurations: configurations);
    }

    private static int ParseInt(string? value) => int.TryParse(value, out var parsed) ? parsed : 0;

    private static string ResolveRulesPath()
    {
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

public sealed record Rio600RuleSet(
    string SourcePath,
    string DefaultOrderCode,
    IReadOnlyList<Rio600Version> Versions,
    IReadOnlyDictionary<string, Rio600Digit> Digits,
    IReadOnlyList<Rio600ValidCodeRule> ValidPositionRules,
    IReadOnlyList<Rio600Configuration> Configurations)
{
    public Rio600Digit Digit(string group) =>
        Digits.TryGetValue(group, out var digit) ? digit : new Rio600Digit(0, group, []);

    public Rio600Configuration? FindConfiguration(string communicationCode) =>
        Configurations.FirstOrDefault(configuration =>
            configuration.CommunicationCode.Equals(communicationCode, StringComparison.OrdinalIgnoreCase));
}

public sealed record Rio600Digit(int Location, string Group, IReadOnlyList<Rio600Option> Options);

public sealed record Rio600Option(string Value, string Name, string Version, string Description, string ManageType)
{
    public bool SupportsVersion(string version)
    {
        if (Version == "*" || string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        return Version
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(version, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record Rio600Version(string Id, string IedVersion, string ConpackVersion);

public sealed record Rio600ValidCodeRule(string Code, string Version)
{
    public string ModuleChar => Code.Length > 0 ? Code[..1] : "";
    public string HardwareChar => Code.Length > 1 ? Code.Substring(1, 1) : "";
    public string SoftwareChar => Code.Length > 2 ? Code.Substring(2, 1) : "";

    public bool SupportsVersion(string version)
    {
        if (Version == "*" || string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        return Version
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(version, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record Rio600Configuration(
    string CommunicationCode,
    string DefaultOrderCode,
    int ChannelLimit,
    int MaxChannels,
    int PointsLimit,
    int MaxPoints,
    IReadOnlyDictionary<string, Rio600IoModuleRule> Modules);

public sealed record Rio600IoModuleRule(string Name, string Char, int Channels, int Points);
