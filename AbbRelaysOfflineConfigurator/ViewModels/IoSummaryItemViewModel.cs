namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class IoSummaryItemViewModel(string name, string value)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
}
