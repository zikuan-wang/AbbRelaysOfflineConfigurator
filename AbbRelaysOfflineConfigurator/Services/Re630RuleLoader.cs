using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed class Re630RuleLoader
{
    private static readonly Lazy<Re630RuleCatalog> SharedCatalog = new(LoadCore);
    private static readonly Regex VariantFileRegex = new(
        @"^(?<device>RE[FGMT]630)__(?<version>.+)_VarientList\.xml$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public Re630RuleCatalog Load() => SharedCatalog.Value;

    private static Re630RuleCatalog LoadCore()
    {
        var dataDirectory = ResolveDataDirectory();
        var ruleSets = Directory.GetFiles(dataDirectory, "RE*630__*_VarientList.xml")
            .Select(LoadRuleSet)
            .OrderBy(ruleSet => DeviceSortOrder(ruleSet.DeviceId))
            .ThenBy(ruleSet => VersionSortKey(ruleSet.VersionText))
            .ToList();

        if (ruleSets.Count == 0)
        {
            throw new FileNotFoundException("No RE_630 variant XML files were found.", dataDirectory);
        }

        return new Re630RuleCatalog(dataDirectory, ruleSets);
    }

    private static Re630RuleSet LoadRuleSet(string path)
    {
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException($"{Path.GetFileName(path)} is empty.");
        var groups = root.Element("CodeDigits")?
            .Elements("CodeDigit")
            .Select((element, index) => new Re630GroupRule(
                Digit: ((string?)element.Attribute("Digit") ?? "").Trim(),
                SortOrder: index,
                Title: ((string?)element.Attribute("Title") ?? "").Trim()))
            .Where(group => !string.IsNullOrWhiteSpace(group.Digit))
            .ToList() ?? [];

        var details = root.Element("CodeDetails")?
            .Elements("CodeDetail")
            .Select((element, index) => new Re630OptionRule(
                Digit: ((string?)element.Attribute("Digit") ?? "").Trim(),
                Code: ((string?)element.Attribute("Code") ?? "").Trim(),
                Description: ((string?)element.Attribute("Description") ?? "").Trim(),
                BasicCode: ((string?)element.Attribute("BasicCode") ?? "").Trim(),
                SortOrder: index))
            .Where(option => !string.IsNullOrWhiteSpace(option.Digit) &&
                             !string.IsNullOrWhiteSpace(option.Code))
            .ToList() ?? [];

        var rules = root.Element("CodeRules")?
            .Elements("CodeRule")
            .Select(element => new Re630CodeRule(
                BasicCode: ((string?)element.Attribute("BasicCode") ?? "").Trim(),
                Digit: ((string?)element.Attribute("Digit") ?? "").Trim(),
                Code: ((string?)element.Attribute("Code") ?? "").Trim(),
                DependentDigit: ((string?)element.Attribute("DependentDigit") ?? "").Trim(),
                PossibleCodes: SplitCodes((string?)element.Attribute("PossibleCodes"))))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.BasicCode) &&
                           !string.IsNullOrWhiteSpace(rule.Digit) &&
                           !string.IsNullOrWhiteSpace(rule.Code) &&
                           !string.IsNullOrWhiteSpace(rule.DependentDigit) &&
                           rule.PossibleCodes.Count > 0)
            .ToList() ?? [];

        var deviceId = DeviceIdFromPath(path);
        var deviceCode = details.FirstOrDefault(option => option.Digit.Equals("3", StringComparison.OrdinalIgnoreCase))
            ?.Code ?? "";
        var deviceDescription = details.FirstOrDefault(option => option.Digit.Equals("3", StringComparison.OrdinalIgnoreCase))
            ?.Description ?? deviceId;
        var versionCode = details.FirstOrDefault(option => option.Digit.Equals("18", StringComparison.OrdinalIgnoreCase))
            ?.Code ?? "";
        var versionText = VersionTextFromPath(path);
        var versionDescription = details.FirstOrDefault(option => option.Digit.Equals("18", StringComparison.OrdinalIgnoreCase))
            ?.Description ?? versionText;

        return new Re630RuleSet(
            SourcePath: path,
            FileName: Path.GetFileName(path),
            DeviceId: deviceId,
            DeviceCode: deviceCode,
            DeviceDescription: deviceDescription,
            VersionText: versionText,
            VersionCode: versionCode,
            VersionDescription: versionDescription,
            Groups: groups,
            Options: details,
            Rules: rules);
    }

    private static IReadOnlyList<string> SplitCodes(string? value) =>
        (value ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .ToList();

    private static string DeviceIdFromPath(string path)
    {
        var match = VariantFileRegex.Match(Path.GetFileName(path));
        return match.Success ? match.Groups["device"].Value.ToUpperInvariant() : Path.GetFileNameWithoutExtension(path);
    }

    private static string VersionTextFromPath(string path)
    {
        var match = VariantFileRegex.Match(Path.GetFileName(path));
        return match.Success
            ? match.Groups["version"].Value.Replace('_', '.').Trim('.')
            : "";
    }

    private static int DeviceSortOrder(string deviceId) => deviceId.ToUpperInvariant() switch
    {
        "REF630" => 0,
        "REG630" => 1,
        "REM630" => 2,
        "RET630" => 3,
        _ => 99
    };

    private static string VersionSortKey(string versionText) =>
        string.Join(".", versionText.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var value) ? value.ToString("D4") : part));

    private static string ResolveDataDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "RE_630"),
            Path.Combine(Environment.CurrentDirectory, "Data", "RE_630"),
            Path.Combine(Environment.CurrentDirectory, "RE_630")
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "Data", "RE_630"));
            candidates.Add(Path.Combine(current.FullName, "RE_630"));
            candidates.Add(Path.Combine(current.FullName, "AbbRelaysOfflineConfigurator", "Data", "RE_630"));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(path =>
                   Directory.Exists(path) &&
                   Directory.EnumerateFiles(path, "RE*630__*_VarientList.xml").Any())
               ?? candidates[0];
    }
}

public sealed record Re630RuleCatalog(string SourceDirectory, IReadOnlyList<Re630RuleSet> RuleSets);

public sealed record Re630RuleSet(
    string SourcePath,
    string FileName,
    string DeviceId,
    string DeviceCode,
    string DeviceDescription,
    string VersionText,
    string VersionCode,
    string VersionDescription,
    IReadOnlyList<Re630GroupRule> Groups,
    IReadOnlyList<Re630OptionRule> Options,
    IReadOnlyList<Re630CodeRule> Rules)
{
    public string DisplayName => $"{DeviceId} {VersionDescription}";

    public IReadOnlyList<Re630OptionRule> OptionsFor(string digit, string basicCode)
    {
        return Options
            .Where(option => option.Digit.Equals(digit, StringComparison.OrdinalIgnoreCase) &&
                             (string.IsNullOrWhiteSpace(option.BasicCode) ||
                              option.BasicCode.Equals(basicCode, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(option => option.SortOrder).First())
            .OrderBy(option => option.SortOrder)
            .ToList();
    }
}

public sealed record Re630GroupRule(string Digit, int SortOrder, string Title)
{
    public int CodeLength => Digit.Contains(',', StringComparison.OrdinalIgnoreCase)
        ? Digit.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length
        : 1;
}

public sealed record Re630OptionRule(
    string Digit,
    string Code,
    string Description,
    string BasicCode,
    int SortOrder);

public sealed record Re630CodeRule(
    string BasicCode,
    string Digit,
    string Code,
    string DependentDigit,
    IReadOnlyList<string> PossibleCodes);
