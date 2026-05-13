using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class FunctionSuggestionViewModel(AppFunctionEntry function) : ObservableObject
{
    public string Code => function.Code;
    public string Ansi => function.Ansi;
    public string EnglishName => function.EnglishName;
    public string ChineseName => function.ChineseName;
    public string AppsText => function.IsBase ? "基础功能" : string.Join(" / ", function.Apps);
    public string DisplayText => $"{function.Code}  {function.Ansi}  {function.ChineseName}";
}

public sealed class RequestedFunctionViewModel(AppFunctionEntry function) : ObservableObject
{
    public string Code => function.Code;
    public string Ansi => function.Ansi;
    public string CodeWithAnsi => string.IsNullOrWhiteSpace(function.Ansi)
        ? function.Code
        : $"{function.Code} / ANSI {function.Ansi}";
    public string EnglishName => function.EnglishName;
    public string ChineseName => function.ChineseName;
    public bool IsBase => function.IsBase || function.Apps.Count == 0;
    public string AppsText => IsBase ? "基础功能" : string.Join(" / ", function.Apps);
}

public sealed class AppRecommendationViewModel(string id, IReadOnlyList<string> coveredFunctions) : ObservableObject
{
    public string Id { get; } = id;
    public string CoveredFunctionsText { get; } = coveredFunctions.Count == 0
        ? "依赖项"
        : string.Join(", ", coveredFunctions);
}
