using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class FunctionSuggestionViewModel(AppFunctionEntry function, ConfiguratorViewModel owner) : ObservableObject
{
    public string Code => function.Code;
    public string Ansi => function.Ansi;
    public string EnglishName => function.EnglishName;
    public string ChineseName => function.ChineseName;
    public string AppsText => function.IsBase ? owner.IsEnglish ? "Base functionality" : "基础功能" : string.Join(" / ", function.Apps);
    public string DisplayText => owner.IsEnglish
        ? $"{function.Code}  {function.Ansi}  {function.EnglishName}".Trim()
        : $"{function.Code}  {function.Ansi}  {function.ChineseName}".Trim();

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(AppsText));
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed class RequestedFunctionViewModel(AppFunctionEntry function, ConfiguratorViewModel owner) : ObservableObject
{
    public string Code => function.Code;
    public string Ansi => function.Ansi;
    public string CodeWithAnsi => string.IsNullOrWhiteSpace(function.Ansi)
        ? function.Code
        : $"{function.Code} / ANSI {function.Ansi}";
    public string EnglishName => function.EnglishName;
    public string ChineseName => function.ChineseName;
    public string DisplayName => owner.IsEnglish ? EnglishName : ChineseName;
    public string SecondaryName => owner.IsEnglish ? ChineseName : EnglishName;
    public bool IsBase => function.IsBase || function.Apps.Count == 0;
    public string AppsText => IsBase ? owner.IsEnglish ? "Base functionality" : "基础功能" : string.Join(" / ", function.Apps);

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SecondaryName));
        OnPropertyChanged(nameof(AppsText));
    }
}

public sealed class AppRecommendationViewModel(string id, IReadOnlyList<string> coveredFunctions, ConfiguratorViewModel owner) : ObservableObject
{
    public string Id { get; } = id;
    public string CoveredFunctionsText => coveredFunctions.Count == 0
        ? owner.IsEnglish ? "Dependency" : "依赖项"
        : string.Join(", ", coveredFunctions);

    internal void RefreshLanguage() => OnPropertyChanged(nameof(CoveredFunctionsText));
}
