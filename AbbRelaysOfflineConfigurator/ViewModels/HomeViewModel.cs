using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

// 首页跨产品功能推荐协调器：聚合不同产品/版本的功能目录，把用户输入统一为功能候选，
// 仅当某个产品能够覆盖全部已选需求时才给出跳转建议；具体选型合法性仍由目标页面负责。
public sealed class HomeViewModel : ObservableObject
{
    private readonly AppFunctionCatalogService _rex615Catalog = new();
    private readonly Rex640AppFunctionCatalogService _rex640Catalog = new();
    private readonly Rex600FunctionCatalogService _rex600Catalog = new();
    private readonly Ssc600FunctionCatalogService _ssc600Catalog = new();
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private string _functionSearchText = "";
    private string _recommendationSummary = "";

    public HomeViewModel()
    {
        FunctionSuggestions = [];
        RequestedFunctions = [];
        ProductRecommendations = [];

        AddFunctionInputCommand = new RelayCommand(AddFunctionInput, () => !string.IsNullOrWhiteSpace(FunctionSearchText));
        ClearFunctionRecommendationCommand = new RelayCommand(ClearFunctionRecommendation, () => RequestedFunctions.Count > 0);

        RecommendationSummary = DefaultRecommendationSummary();
    }

    public ObservableCollection<HomeFunctionSuggestionViewModel> FunctionSuggestions { get; }
    public ObservableCollection<HomeRequestedFunctionViewModel> RequestedFunctions { get; }
    public ObservableCollection<HomeProductRecommendationViewModel> ProductRecommendations { get; }
    public RelayCommand AddFunctionInputCommand { get; }
    public RelayCommand ClearFunctionRecommendationCommand { get; }

    public bool IsEnglish => DisplayLanguage.Equals(ConfiguratorViewModel.EnglishLanguage, StringComparison.OrdinalIgnoreCase);

    public string DisplayLanguage
    {
        get => _displayLanguage;
        set
        {
            var normalized = string.Equals(value, ConfiguratorViewModel.EnglishLanguage, StringComparison.OrdinalIgnoreCase)
                ? ConfiguratorViewModel.EnglishLanguage
                : ConfiguratorViewModel.ChineseLanguage;
            if (SetProperty(ref _displayLanguage, normalized))
            {
                OnPropertyChanged(nameof(IsEnglish));
                RefreshStaticText();
                RefreshFunctionDisplay();
                RefreshRecommendations();
            }
        }
    }

    public string FunctionSearchText
    {
        get => _functionSearchText;
        set
        {
            if (SetProperty(ref _functionSearchText, value))
            {
                AddFunctionInputCommand.RaiseCanExecuteChanged();
                RefreshFunctionSuggestions();
            }
        }
    }

    public string RecommendationSummary
    {
        get => _recommendationSummary;
        private set => SetProperty(ref _recommendationSummary, value);
    }

    public bool HasFunctionSuggestions => FunctionSuggestions.Count > 0;
    public bool HasRequestedFunctions => RequestedFunctions.Count > 0;
    public bool HasProductRecommendations => ProductRecommendations.Count > 0;

    public string HeroTitle => IsEnglish ? "Unofficial ABB Relays Offline Configurator" : "非官方 ABB 继保离线选型工具";
    public string HeroSubtitle => IsEnglish
        ? "One local workspace for relay selection, order code generation, I/O summaries, validation and legacy code conversion."
        : "集中完成继保产品选型、订货代码生成、I/O 摘要、规则校验与旧订货号转换。";
    public string ProductRecommendationTitle => IsEnglish ? "Recommend Product by Protection Function" : "根据保护功能推荐产品";
    public string ProductRecommendationDescription => IsEnglish
        ? "Enter ANSI code, ABB function code, Chinese name or English function name. The tool recommends product families that cover all selected functions."
        : "输入 ANSI CODE、ABB CODE、中文或英文保护功能名称，系统推荐能覆盖全部需求的保护型号。";
    public string FunctionInputHint => IsEnglish ? "ANSI / ABB code / function name" : "ANSI CODE / ABB CODE / 保护功能";
    public string AddText => IsEnglish ? "Add" : "添加";
    public string ClearText => IsEnglish ? "Clear" : "清空";
    public string SelectedFunctionsTitle => IsEnglish ? "Selected functions" : "已选功能";
    public string RecommendationResultTitle => IsEnglish ? "Recommended products" : "推荐型号";
    public string JumpText => IsEnglish ? "Open configurator" : "进入选型";
    public string LicenseTitle => IsEnglish ? "License status" : "授权信息";
    public string SoftwareIntroTitle => IsEnglish ? "Software overview" : "软件介绍";
    public string SoftwareIntroText => IsEnglish
        ? "This unofficial tool uses local data packages to perform offline selection for ABB relay products, validate ordering rules, generate codes, review I/O summaries, convert 615/620 legacy order codes and check selected order codes online when needed."
        : "本非官方工具基于本地数据包实现 ABB 继保产品离线选型、订货规则校验、代码生成、I/O 摘要、615/620 旧订货号转换，并可在需要时执行在线校验。";
    public string CoverageTitle => IsEnglish ? "Current coverage scope" : "当前覆盖范围";
    public string CoverageText => IsEnglish
        ? "REX615, REX600, REX640, RE_611, RE_630, SSC600/SSC600 SW, RIO600, 615/620 CN selection and 615/620 conversion workflows."
        : "支持 REX615、REX600、REX640、RE_611、RE_630、SSC600/SSC600 SW、RIO600、615/620 CN 选型，以及 615/620 订货号转换流程。";
    public string LocalDataTitle => IsEnglish ? "Local-first workflow" : "本地优先";
    public string LocalDataText => IsEnglish
        ? "Most selection and validation work runs locally. Online checks are only used for order number confirmation or release updates."
        : "主要选型和校验逻辑均在本地运行，在线请求仅用于订货号确认或版本更新。";

    public void AddSuggestedFunction(HomeFunctionSuggestionViewModel suggestion)
    {
        AddFunction(suggestion.Function);
        FunctionSearchText = "";
        FunctionSuggestions.Clear();
        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    public void RemoveRequestedFunction(HomeRequestedFunctionViewModel function)
    {
        var existing = RequestedFunctions.FirstOrDefault(item => item.Key.Equals(function.Key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        RequestedFunctions.Remove(existing);
        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    private void AddFunctionInput()
    {
        // 每个输入词先尝试跨目录唯一精确匹配；只有无法唯一确定时才展开模糊候选，
        // 防止同名或跨版本功能在用户未确认时被静默选错。
        var tokens = Regex.Split(FunctionSearchText, @"[\r\n,;，；、]+")
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
        var unresolved = new List<string>();
        var candidateFunctions = new List<HomeFunctionCandidate>();

        foreach (var token in tokens)
        {
            var exactMatches = ResolveExactAggregate(token);
            if (exactMatches.Count == 1)
            {
                AddFunction(exactMatches[0]);
                continue;
            }

            var candidates = SearchAggregate(token, 24)
                .Where(candidate => RequestedFunctions.All(selected => !selected.Key.Equals(candidate.FunctionKey, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (candidates.Count == 1)
            {
                AddFunction(candidates[0]);
                continue;
            }

            unresolved.Add(IsEnglish
                ? candidates.Count == 0 ? $"{token} (no candidates)" : $"{token} ({candidates.Count} candidates)"
                : candidates.Count == 0 ? $"{token}（无候选）" : $"{token}（{candidates.Count} 个候选）");
            candidateFunctions.AddRange(candidates);
        }

        FunctionSearchText = "";
        if (unresolved.Count > 0)
        {
            Replace(FunctionSuggestions, candidateFunctions
                .DistinctBy(function => function.CandidateKey, StringComparer.OrdinalIgnoreCase)
                .Select(function => new HomeFunctionSuggestionViewModel(function, this)));
            RefreshRecommendations();
            RecommendationSummary = IsEnglish
                ? $"{RecommendationSummary}; some inputs were not unique, select from candidates: {string.Join(", ", unresolved)}"
                : $"{RecommendationSummary}；以下输入未能唯一匹配，请从候选中选择：{string.Join("，", unresolved)}";
            RefreshFunctionStateProperties();
            return;
        }

        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    private void AddFunction(HomeFunctionCandidate function)
    {
        if (RequestedFunctions.Any(item => item.Key.Equals(function.FunctionKey, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        RequestedFunctions.Add(new HomeRequestedFunctionViewModel(function, this));
    }

    private void ClearFunctionRecommendation()
    {
        RequestedFunctions.Clear();
        FunctionSuggestions.Clear();
        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    private void RefreshFunctionSuggestions()
    {
        var token = Regex.Split(FunctionSearchText, @"[\r\n,;，；、]+").LastOrDefault()?.Trim() ?? "";
        Replace(FunctionSuggestions, SearchAggregate(token, 18)
            .Where(function => RequestedFunctions.All(selected => !selected.Key.Equals(function.FunctionKey, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(function => function.CandidateKey, StringComparer.OrdinalIgnoreCase)
            .Select(function => new HomeFunctionSuggestionViewModel(function, this)));
        OnPropertyChanged(nameof(HasFunctionSuggestions));
    }

    private void RefreshRecommendations()
    {
        if (RequestedFunctions.Count == 0)
        {
            ProductRecommendations.Clear();
            RecommendationSummary = DefaultRecommendationSummary();
            RefreshFunctionStateProperties();
            return;
        }

        // 推荐按“全覆盖”而非命中数量排序：任一需求无法在产品目录中对应时，该产品不会出现。
        var requirements = RequestedFunctions.Select(function => function.Function).ToList();
        var recommendations = BuildProductRecommendations(requirements).ToList();
        Replace(ProductRecommendations, recommendations);

        RecommendationSummary = recommendations.Count == 0
            ? IsEnglish
                ? "No currently indexed product covers all selected functions. Remove or replace unmatched functions and try again."
                : "当前索引的产品中没有型号能覆盖全部已选功能，请减少或替换未匹配功能后再试。"
            : IsEnglish
                ? $"{recommendations.Count} product option(s) cover all selected functions."
                : $"{recommendations.Count} 个产品方案可覆盖全部已选功能。";
        RefreshFunctionStateProperties();
    }

    private IEnumerable<HomeProductRecommendationViewModel> BuildProductRecommendations(IReadOnlyList<HomeFunctionCandidate> requirements)
    {
        // 每个产品版本独立评估，避免把 PCL1/PCL2/PCL3 的功能或 APP 能力合并成一个并不存在的配置。
        // priority 是稳定的产品展示顺序，不代表技术优劣或自动替用户作最终型号选择。
        var products = new[]
        {
            BuildRex615Product("REX615 PCL3", "REX615_PCL3", "PCL3", targetTabIndex: 1, priority: 1, requirements),
            BuildRex615Product("REX615 PCL2", "REX615_PCL2", "PCL2", targetTabIndex: 1, priority: 2, requirements),
            BuildRex615Product("REX615 PCL1", "REX615_PCL1", "PCL1", targetTabIndex: 1, priority: 3, requirements),
            BuildRex640Product("REX640 PCL7", "REX640_PCL7", "PCL7", targetTabIndex: 8, priority: 4, requirements),
            BuildRex640Product("REX640 PCL6", "REX640_PCL6", "PCL6", targetTabIndex: 8, priority: 5, requirements),
            BuildRex640Product("REX640 PCL5", "REX640_PCL5", "PCL5", targetTabIndex: 8, priority: 6, requirements),
            BuildRex600Product(requirements),
            BuildSsc600Product(requirements),
            BuildRio600Product(requirements)
        };

        return products
            .Where(product => product is not null)
            .Cast<HomeProductRecommendationViewModel>()
            .OrderBy(product => product.Priority)
            .ThenBy(product => product.ProductName, StringComparer.OrdinalIgnoreCase);
    }

    private HomeProductRecommendationViewModel? BuildRex615Product(
        string productName,
        string productKey,
        string version,
        int targetTabIndex,
        int priority,
        IReadOnlyList<HomeFunctionCandidate> requirements)
    {
        var functions = _rex615Catalog.GetFunctions(version);
        var matched = MatchRequirements(requirements, functions.Select(HomeFunctionCandidate.FromRex615Function).ToList());
        if (matched.Count != requirements.Count)
        {
            return null;
        }

        var appResult = _rex615Catalog.Recommend(version, matched.Select(function => function.Code).ToList());
        var config = appResult.Apps.Count == 0
            ? IsEnglish ? "Base functionality; no additional APP required." : "基础功能，无需额外 APP。"
            : IsEnglish
                ? $"Recommended APP: {string.Join(" + ", appResult.Apps.Select(app => app.Id))}"
                : $"推荐 APP：{string.Join(" + ", appResult.Apps.Select(app => app.Id))}";
        var coverage = IsEnglish
            ? $"{matched.Count} matched function(s): {string.Join(", ", matched.Select(function => function.Code))}"
            : $"匹配 {matched.Count} 项功能：{string.Join("，", matched.Select(function => function.Code))}";

        return new HomeProductRecommendationViewModel(
            productName,
            productKey,
            targetTabIndex,
            version,
            IsEnglish ? "Feeder protection and control relay configuration" : "馈线保护及控制继电器选型",
            coverage,
            config,
            priority,
            this);
    }

    private HomeProductRecommendationViewModel? BuildRex640Product(
        string productName,
        string productKey,
        string version,
        int targetTabIndex,
        int priority,
        IReadOnlyList<HomeFunctionCandidate> requirements)
    {
        var functions = _rex640Catalog.GetFunctions(version)
            .Select(function => HomeFunctionCandidate.FromRex640Function(version, function))
            .ToList();
        var matched = MatchRequirements(requirements, functions);
        if (matched.Count != requirements.Count)
        {
            return null;
        }

        var appResult = _rex640Catalog.Recommend(version, matched.Select(function => function.Code).ToList());
        var config = appResult.Apps.Count == 0
            ? IsEnglish ? "Base functionality; no additional APP required." : "基础功能，无需额外 APP。"
            : IsEnglish
                ? $"Recommended APP: {string.Join(" + ", appResult.Apps.Select(app => app.Id))}"
                : $"推荐 APP：{string.Join(" + ", appResult.Apps.Select(app => app.Id))}";
        var coverage = IsEnglish
            ? $"{matched.Count} matched function(s): {string.Join(", ", matched.Select(function => function.Code))}"
            : $"匹配 {matched.Count} 项功能：{string.Join("，", matched.Select(function => function.Code))}";

        return new HomeProductRecommendationViewModel(
            productName,
            productKey,
            targetTabIndex,
            version,
            IsEnglish ? "All-in-one protection and control relay configuration" : "多功能一体化保护与控制装置选型",
            coverage,
            config,
            priority,
            this);
    }

    private HomeProductRecommendationViewModel? BuildRex600Product(IReadOnlyList<HomeFunctionCandidate> requirements)
    {
        var functions = _rex600Catalog.GetFunctions()
            .Select(HomeFunctionCandidate.FromRex600Function)
            .ToList();
        var matched = MatchRequirements(requirements, functions);
        if (matched.Count != requirements.Count)
        {
            return null;
        }

        var coverage = IsEnglish
            ? $"{matched.Count} matched function(s): {string.Join(", ", matched.Select(function => function.Code))}"
            : $"匹配 {matched.Count} 项功能：{string.Join("，", matched.Select(function => function.Code))}";

        return new HomeProductRecommendationViewModel(
            "REX600",
            "REX600",
            targetTabIndex: 7,
            recommendedVersion: "",
            IsEnglish ? "Compact protection relay configuration" : "紧凑型保护装置选型",
            coverage,
            IsEnglish ? "Select the REX600 variant and options according to the required functions." : "根据所需功能选择 REX600 型号及选项。",
            priority: 7,
            this);
    }

    private HomeProductRecommendationViewModel? BuildRio600Product(IReadOnlyList<HomeFunctionCandidate> requirements)
    {
        var functions = Rio600FunctionCandidates().ToList();
        var matched = MatchRequirements(requirements, functions);
        if (matched.Count != requirements.Count)
        {
            return null;
        }

        var coverage = IsEnglish
            ? $"{matched.Count} matched I/O requirement(s): {string.Join(", ", matched.Select(function => function.Code))}"
            : $"匹配 {matched.Count} 项 I/O 需求：{string.Join("，", matched.Select(function => function.Code))}";

        return new HomeProductRecommendationViewModel(
            "RIO600",
            "RIO600",
            targetTabIndex: 3,
            recommendedVersion: "",
            IsEnglish ? "Remote I/O unit for extending binary, RTD and mA signals" : "用于扩展开关量、RTD 和 mA 信号的远程 I/O 装置",
            coverage,
            IsEnglish ? "Configure LECM and I/O modules according to required signal points." : "按所需信号点数配置 LECM 与 I/O 模块。",
            priority: 9,
            this);
    }

    private HomeProductRecommendationViewModel? BuildSsc600Product(IReadOnlyList<HomeFunctionCandidate> requirements)
    {
        var functions = _ssc600Catalog.GetFunctions()
            .Select(HomeFunctionCandidate.FromSsc600Function)
            .ToList();
        var matched = MatchRequirements(requirements, functions);
        if (matched.Count != requirements.Count)
        {
            return null;
        }

        var appResult = _ssc600Catalog.Recommend(matched.Select(function => function.Code).ToList());
        var config = appResult.Recommendations.Count == 0
            ? IsEnglish ? "Base functionality; no additional AppPack required." : "基础功能，无需额外应用包。"
            : IsEnglish
                ? $"Recommended AppPack: {string.Join(" + ", appResult.Recommendations.Select(item => item.OptionId))}"
                : $"推荐应用包：{string.Join(" + ", appResult.Recommendations.Select(item => item.OptionId))}";
        var coverage = IsEnglish
            ? $"{matched.Count} matched function(s): {string.Join(", ", matched.Select(function => function.Code))}"
            : $"匹配 {matched.Count} 项功能：{string.Join("，", matched.Select(function => function.Code))}";

        return new HomeProductRecommendationViewModel(
            "SSC600 / SSC600 SW",
            "SSC600",
            targetTabIndex: 2,
            recommendedVersion: "",
            IsEnglish ? "Centralized protection and control solution" : "集中式保护与控制选型",
            coverage,
            config,
            priority: 8,
            this);
    }

    private static IReadOnlyList<HomeFunctionCandidate> MatchRequirements(
        IReadOnlyList<HomeFunctionCandidate> requirements,
        IReadOnlyList<HomeFunctionCandidate> productFunctions)
    {
        // 返回的是产品目录中的匹配项而非原始需求；调用方以数量相等判断全部覆盖，
        // 后续 APP 推荐也因此使用目标版本真实存在的功能代码。
        var matched = new List<HomeFunctionCandidate>();
        foreach (var requirement in requirements)
        {
            var match = productFunctions.FirstOrDefault(function => IsSameFunction(requirement, function));
            if (match is null)
            {
                continue;
            }

            matched.Add(match);
        }

        return matched;
    }

    private static bool IsSameFunction(HomeFunctionCandidate requested, HomeFunctionCandidate candidate)
    {
        // ABB/IEC 功能代码精确相同时优先认定为同一功能；代码体系不一致时再以规范化 ANSI 术语求交集。
        // 名称相似不用于跨产品覆盖判定，避免仅靠文本包含关系产生技术能力误报。
        if (!string.IsNullOrWhiteSpace(requested.Code) &&
            requested.Code.Equals(candidate.Code, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requestedAnsi = ExpandAnsiSearchTerms(requested.Ansi).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requestedAnsi.Count > 0 &&
               ExpandAnsiSearchTerms(candidate.Ansi).Any(requestedAnsi.Contains);
    }

    private IReadOnlyList<HomeFunctionCandidate> ResolveExactAggregate(string query)
    {
        // 精确解析覆盖所有已索引版本，随后按 FunctionKey 去重；
        // 如果同一输入仍对应多个技术功能，上层会要求用户从候选中明确选择。
        var candidates = new List<HomeFunctionCandidate>();
        foreach (var version in new[] { "PCL3", "PCL2", "PCL1" })
        {
            var function = _rex615Catalog.ResolveExact(version, query);
            if (function is not null)
            {
                candidates.Add(HomeFunctionCandidate.FromRex615Function(version, function));
            }
        }

        foreach (var version in new[] { "PCL5", "PCL6", "PCL7" })
        {
            var function = _rex640Catalog.ResolveExact(version, query);
            if (function is not null)
            {
                candidates.Add(HomeFunctionCandidate.FromRex640Function(version, function));
            }
        }

        var rex600Function = ResolveExactRex600(query);
        if (rex600Function is not null)
        {
            candidates.Add(HomeFunctionCandidate.FromRex600Function(rex600Function));
        }

        var sscFunction = _ssc600Catalog.ResolveExact(query);
        if (sscFunction is not null)
        {
            candidates.Add(HomeFunctionCandidate.FromSsc600Function(sscFunction));
        }

        candidates.AddRange(Rio600FunctionCandidates()
            .Where(function => IsExactCandidateMatch(function, query)));

        return candidates
            .DistinctBy(function => function.FunctionKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<HomeFunctionCandidate> SearchAggregate(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var candidates = new List<HomeFunctionCandidate>();
        candidates.AddRange(_rex615Catalog.Search("PCL3", query, limit).Select(function => HomeFunctionCandidate.FromRex615Function("PCL3", function)));
        candidates.AddRange(_rex615Catalog.Search("PCL2", query, limit).Select(function => HomeFunctionCandidate.FromRex615Function("PCL2", function)));
        candidates.AddRange(_rex615Catalog.Search("PCL1", query, limit).Select(function => HomeFunctionCandidate.FromRex615Function("PCL1", function)));
        candidates.AddRange(_rex640Catalog.Search("PCL5", query, limit).Select(function => HomeFunctionCandidate.FromRex640Function("PCL5", function)));
        candidates.AddRange(_rex640Catalog.Search("PCL6", query, limit).Select(function => HomeFunctionCandidate.FromRex640Function("PCL6", function)));
        candidates.AddRange(_rex640Catalog.Search("PCL7", query, limit).Select(function => HomeFunctionCandidate.FromRex640Function("PCL7", function)));
        candidates.AddRange(_rex600Catalog.Search(query).Take(limit).Select(HomeFunctionCandidate.FromRex600Function));
        candidates.AddRange(_ssc600Catalog.Search(query, limit).Select(HomeFunctionCandidate.FromSsc600Function));
        candidates.AddRange(Rio600FunctionCandidates()
            .Where(function => CandidateContains(function, query)));
        return candidates
            .DistinctBy(function => function.CandidateKey, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private Rex600FunctionEntry? ResolveExactRex600(string query)
    {
        var matches = _rex600Catalog.GetFunctions()
            .Where(function =>
                NormalizeSearchToken(function.Iec61850).Equals(NormalizeSearchToken(query), StringComparison.OrdinalIgnoreCase) ||
                NormalizeSearchToken(function.Iec60617).Equals(NormalizeSearchToken(query), StringComparison.OrdinalIgnoreCase) ||
                ExpandAnsiSearchTerms(function.AnsiCode).Any(term => NormalizeSearchToken(term).Equals(NormalizeSearchToken(query), StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(function => $"{function.Iec61850}:{function.AnsiCode}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool IsExactCandidateMatch(HomeFunctionCandidate candidate, string query) =>
        NormalizeSearchToken(candidate.Code).Equals(NormalizeSearchToken(query), StringComparison.OrdinalIgnoreCase) ||
        ExpandAnsiSearchTerms(candidate.Ansi).Any(term => NormalizeSearchToken(term).Equals(NormalizeSearchToken(query), StringComparison.OrdinalIgnoreCase));

    private static bool CandidateContains(HomeFunctionCandidate candidate, string query)
    {
        var token = NormalizeSearchToken(query);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return new[]
            {
                candidate.Code,
                candidate.Ansi,
                candidate.ChineseName,
                candidate.EnglishName,
                candidate.Category
            }
            .Select(NormalizeSearchToken)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                          token.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<HomeFunctionCandidate> Rio600FunctionCandidates()
    {
        yield return new HomeFunctionCandidate("RIO600", "RIO600", "BI", "", "远程开关量输入扩展", "Remote binary input extension", "I/O");
        yield return new HomeFunctionCandidate("RIO600", "RIO600", "DI", "", "远程开关量输入扩展", "Remote digital input extension", "I/O");
        yield return new HomeFunctionCandidate("RIO600", "RIO600", "BO", "", "远程开关量输出扩展", "Remote binary output extension", "I/O");
        yield return new HomeFunctionCandidate("RIO600", "RIO600", "DO", "", "远程开关量输出扩展", "Remote digital output extension", "I/O");
        yield return new HomeFunctionCandidate("RIO600", "RIO600", "RTD", "", "远程 RTD 测量扩展", "Remote RTD measurement extension", "I/O");
        yield return new HomeFunctionCandidate("RIO600", "RIO600", "mA", "", "远程 mA 模拟量输入扩展", "Remote mA analog input extension", "I/O");
        yield return new HomeFunctionCandidate("RIO600", "RIO600", "I/O", "", "远程 I/O 扩展", "Remote I/O extension", "I/O");
    }

    private static string NormalizeSearchToken(string value) =>
        // 搜索规范化去除 ASCII 范围内的空白和标点，并保留字母、数字及中文字符，
        // 兼容“50/51”“50-51”等常见录入差异。
        new((value ?? "")
            .Trim()
            .ToUpperInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch >= 0x4E00)
            .ToArray());

    private static IEnumerable<string> ExpandAnsiSearchTerms(string ansi)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in Regex.Split(ansi ?? "", @"[\s,，、]+"))
        {
            var term = raw.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            AddAnsiTerm(terms, term);
            var slashParts = term.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (slashParts.Length <= 1)
            {
                continue;
            }

            var numericPrefix = Regex.Match(slashParts[0], @"^\d+").Value;
            foreach (var part in slashParts)
            {
                var expanded = Regex.IsMatch(part, @"^\d")
                    ? part
                    : string.IsNullOrWhiteSpace(numericPrefix) ? part : numericPrefix + part;
                AddAnsiTerm(terms, expanded);
            }
        }

        return terms;
    }

    private static void AddAnsiTerm(ISet<string> terms, string term)
    {
        var cleaned = Regex.Replace(term.Trim(), @"[^\w/>.\-]", "");
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return;
        }

        terms.Add(cleaned);
        terms.Add(cleaned.Replace("/", "", StringComparison.Ordinal));
        var hyphenIndex = cleaned.IndexOf('-', StringComparison.Ordinal);
        if (hyphenIndex > 0)
        {
            terms.Add(cleaned[..hyphenIndex]);
        }
    }

    private string DefaultRecommendationSummary() => IsEnglish
        ? "Add one or more protection functions to see matching product families."
        : "添加一个或多个保护功能后，将显示可覆盖全部需求的产品型号。";

    private void RefreshFunctionDisplay()
    {
        foreach (var suggestion in FunctionSuggestions)
        {
            suggestion.RefreshLanguage();
        }

        foreach (var function in RequestedFunctions)
        {
            function.RefreshLanguage();
        }

        foreach (var recommendation in ProductRecommendations)
        {
            recommendation.RefreshLanguage();
        }
    }

    private void RefreshFunctionStateProperties()
    {
        AddFunctionInputCommand.RaiseCanExecuteChanged();
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasFunctionSuggestions));
        OnPropertyChanged(nameof(HasRequestedFunctions));
        OnPropertyChanged(nameof(HasProductRecommendations));
    }

    private void RefreshStaticText()
    {
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(HeroSubtitle));
        OnPropertyChanged(nameof(ProductRecommendationTitle));
        OnPropertyChanged(nameof(ProductRecommendationDescription));
        OnPropertyChanged(nameof(FunctionInputHint));
        OnPropertyChanged(nameof(AddText));
        OnPropertyChanged(nameof(ClearText));
        OnPropertyChanged(nameof(SelectedFunctionsTitle));
        OnPropertyChanged(nameof(RecommendationResultTitle));
        OnPropertyChanged(nameof(JumpText));
        OnPropertyChanged(nameof(LicenseTitle));
        OnPropertyChanged(nameof(SoftwareIntroTitle));
        OnPropertyChanged(nameof(SoftwareIntroText));
        OnPropertyChanged(nameof(CoverageTitle));
        OnPropertyChanged(nameof(CoverageText));
        OnPropertyChanged(nameof(LocalDataTitle));
        OnPropertyChanged(nameof(LocalDataText));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}

public sealed record HomeFunctionCandidate(
    string ProductKey,
    string ProductName,
    string Code,
    string Ansi,
    string ChineseName,
    string EnglishName,
    string Category)
{
    public string CandidateKey => $"{ProductKey}:{Code}:{Ansi}";
    public string FunctionKey => $"{Code}:{Ansi}";

    public static HomeFunctionCandidate FromRex615Function(string version, AppFunctionEntry function) =>
        new(
            $"REX615_{version}",
            $"REX615 {version}",
            function.Code,
            function.Ansi,
            function.ChineseName,
            function.EnglishName,
            function.Category);

    public static HomeFunctionCandidate FromRex615Function(AppFunctionEntry function) =>
        new(
            "REX615",
            "REX615",
            function.Code,
            function.Ansi,
            function.ChineseName,
            function.EnglishName,
            function.Category);

    public static HomeFunctionCandidate FromSsc600Function(Ssc600FunctionEntry function) =>
        new(
            "SSC600",
            "SSC600 / SSC600 SW",
            function.Code,
            function.Ansi,
            function.ChineseName,
            function.EnglishName,
            function.Category);

    public static HomeFunctionCandidate FromRex640Function(string version, Rex640FunctionEntry function) =>
        new(
            $"REX640_{version}",
            $"REX640 {version}",
            function.Code,
            function.Ansi,
            function.ChineseName,
            function.EnglishName,
            function.Category);

    public static HomeFunctionCandidate FromRex600Function(Rex600FunctionEntry function) =>
        new(
            "REX600",
            "REX600",
            string.IsNullOrWhiteSpace(function.Iec61850) ? function.Iec60617 : function.Iec61850,
            function.AnsiCode,
            function.ChineseName,
            function.EnglishName,
            function.Category);
}

public sealed class HomeFunctionSuggestionViewModel : ObservableObject
{
    private readonly HomeViewModel _owner;

    public HomeFunctionSuggestionViewModel(HomeFunctionCandidate function, HomeViewModel owner)
    {
        Function = function;
        _owner = owner;
    }

    public HomeFunctionCandidate Function { get; }
    public string DisplayText => string.IsNullOrWhiteSpace(Function.Ansi)
        ? $"{Function.Code}  {DisplayName}"
        : $"{Function.Code}  ANSI {Function.Ansi}  {DisplayName}";
    public string ProductText => Function.ProductName;
    public string DisplayName => _owner.IsEnglish ? Function.EnglishName : Function.ChineseName;

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(DisplayName));
    }
}

public sealed class HomeRequestedFunctionViewModel : ObservableObject
{
    private readonly HomeViewModel _owner;

    public HomeRequestedFunctionViewModel(HomeFunctionCandidate function, HomeViewModel owner)
    {
        Function = function;
        _owner = owner;
    }

    public HomeFunctionCandidate Function { get; }
    public string Key => Function.FunctionKey;
    public string CodeWithAnsi => string.IsNullOrWhiteSpace(Function.Ansi)
        ? Function.Code
        : $"{Function.Code} / ANSI {Function.Ansi}";
    public string DisplayName => _owner.IsEnglish ? Function.EnglishName : Function.ChineseName;
    public string SecondaryName => _owner.IsEnglish ? Function.ChineseName : Function.EnglishName;

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SecondaryName));
    }
}

public sealed class HomeProductRecommendationViewModel : ObservableObject
{
    private readonly HomeViewModel _owner;

    public HomeProductRecommendationViewModel(
        string productName,
        string productKey,
        int targetTabIndex,
        string recommendedVersion,
        string description,
        string coverageText,
        string configurationText,
        int priority,
        HomeViewModel owner)
    {
        ProductName = productName;
        ProductKey = productKey;
        TargetTabIndex = targetTabIndex;
        RecommendedVersion = recommendedVersion;
        Description = description;
        CoverageText = coverageText;
        ConfigurationText = configurationText;
        Priority = priority;
        _owner = owner;
    }

    public string ProductName { get; }
    public string ProductKey { get; }
    public int TargetTabIndex { get; }
    public string RecommendedVersion { get; }
    public int Priority { get; }
    public string Description { get; }
    public string CoverageText { get; }
    public string ConfigurationText { get; }
    public string ActionText => _owner.JumpText;

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(ActionText));
    }
}
