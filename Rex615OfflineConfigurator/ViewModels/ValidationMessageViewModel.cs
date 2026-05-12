namespace Rex615OfflineConfigurator.ViewModels;

public sealed class ValidationMessageTargetViewModel(string groupName, string? optionId)
{
    public string GroupName { get; } = groupName;
    public string? OptionId { get; } = optionId;
    public string Label => string.IsNullOrWhiteSpace(OptionId) ? GroupName : $"{GroupName} / {OptionId}";
}

public sealed class ValidationMessageViewModel(string text, IEnumerable<ValidationMessageTargetViewModel> targets, bool isSuccess = false)
{
    public string Text { get; } = text;
    public IReadOnlyList<ValidationMessageTargetViewModel> Targets { get; } = targets.ToList();
    public bool HasTargets => Targets.Count > 0;
    public bool IsSuccess { get; } = isSuccess;
    public bool IsError => !IsSuccess;
    public ValidationMessageTargetViewModel? PrimaryTarget => Targets.FirstOrDefault();
}
