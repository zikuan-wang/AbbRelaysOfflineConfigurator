namespace AbbRelaysOfflineConfigurator.Models;

public sealed class CnLegacyRuleSet
{
    public int FormatVersion { get; set; }
    public List<CnLegacyProductSeries> Series { get; set; } = [];
}

public sealed class CnLegacyProductSeries
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> SourceDocuments { get; set; } = [];
    public List<CnLegacyDevice> Devices { get; set; } = [];
}

public sealed class CnLegacyDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<CnLegacyCodeGroup> Groups { get; set; } = [];
}

public sealed class CnLegacyCodeGroup
{
    public string Position { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsRequired { get; set; } = true;
    public List<CnLegacyCodeOption> Options { get; set; } = [];
}

public sealed class CnLegacyCodeOption
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string ShortDescription { get; set; } = "";
    public bool IsDefault { get; set; }
    public List<CnLegacySelectionRequirement> RequiredSelections { get; set; } = [];
    public List<CnLegacyCombinedSelectionExclusion> ExcludedCombinedSelections { get; set; } = [];
}

public sealed class CnLegacySelectionRequirement
{
    public string Position { get; set; } = "";
    public List<string> Codes { get; set; } = [];
    public string Mode { get; set; } = "AnyOf";
    public string Message { get; set; } = "";
}

public sealed class CnLegacyCombinedSelectionExclusion
{
    public List<string> Positions { get; set; } = [];
    public List<string> Codes { get; set; } = [];
    public string Message { get; set; } = "";
}
