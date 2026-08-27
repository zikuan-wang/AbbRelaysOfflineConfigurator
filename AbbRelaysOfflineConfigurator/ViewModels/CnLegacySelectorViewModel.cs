using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using AbbRelaysOfflineConfigurator.Models;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

// 615/620 CN 历史型号选型页的总状态协调器。它按固定订货号位号管理单选组，
// 同时区分“候选是否可选”的交互规则与“当前完整组合是否合规”的 XML 终态校验规则。
public sealed class CnLegacySelectorViewModel : ObservableObject
{
    private const string DefaultOrderingCode = "HCFCACABNBCZCCN11G";

    private readonly CnLegacyFunctionCatalogService _functionCatalog = new();
    private CnLegacySeriesViewModel? _selectedSeries;
    private CnLegacyDeviceViewModel? _selectedDevice;
    private string _orderingCode = "";
    private string _status = "";
    private string _functionSearchText = "";
    private string _functionRecommendationStatus = "输入 ABB Code、ANSI Code 或保护功能名称，推荐可覆盖这些功能的标准配置。";
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private bool _hasErrors;

    public CnLegacySelectorViewModel()
    {
        // 数据包先构造成系列/装置/位号三级 ViewModel，再通过默认订货号走正式导入流程初始化；
        // 这样首次页面状态与用户后来导入同一代码的行为完全一致。
        var rules = new CnLegacySelectionRuleLoader().Load();
        Series = new ObservableCollection<CnLegacySeriesViewModel>(
            rules.Series.Select(series => new CnLegacySeriesViewModel(series)));
        Devices = [];
        Groups = [];
        SummaryItems = [];
        IoSummaryItems = [];
        ValidationMessages = [];
        FunctionSuggestions = [];
        RequestedFunctions = [];
        StandardConfigurationRecommendations = [];

        CopyOrderingCodeCommand = new RelayCommand(CopyOrderingCode, () => !string.IsNullOrWhiteSpace(OrderingCode));
        ImportOrderingCodeCommand = new RelayCommand(ImportOrderingCode);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription, () => !string.IsNullOrWhiteSpace(OrderingCode));
        PushToConversionCommand = new RelayCommand(PushToConversion, () => !string.IsNullOrWhiteSpace(OrderingCode));
        ExpandAllCommand = new RelayCommand(() => SetAllGroupsExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllGroupsExpanded(false));
        ResetCommand = new RelayCommand(ResetSelections, () => SelectedDevice is not null);
        AddFunctionSearchInputCommand = new RelayCommand(AddFunctionSearchInput, () => SelectedDevice is not null);
        ClearFunctionRecommendationCommand = new RelayCommand(ClearFunctionRecommendation, () => RequestedFunctions.Count > 0);

        SelectedSeries = Series.FirstOrDefault();
        ImportOrderingCodeValue(DefaultOrderingCode, showMessages: false);
    }

    public ObservableCollection<CnLegacySeriesViewModel> Series { get; }
    public ObservableCollection<CnLegacyDeviceViewModel> Devices { get; }
    public ObservableCollection<CnLegacyGroupViewModel> Groups { get; }
    public ObservableCollection<CnLegacySelectionSummaryItemViewModel> SummaryItems { get; }
    public ObservableCollection<IoSummaryItemViewModel> IoSummaryItems { get; }
    public ObservableCollection<CnLegacyValidationMessageViewModel> ValidationMessages { get; }
    public ObservableCollection<CnLegacyFunctionSuggestionViewModel> FunctionSuggestions { get; }
    public ObservableCollection<CnLegacyRequestedFunctionViewModel> RequestedFunctions { get; }
    public ObservableCollection<CnLegacyStandardConfigurationRecommendationViewModel> StandardConfigurationRecommendations { get; }
    public RelayCommand CopyOrderingCodeCommand { get; }
    public RelayCommand ImportOrderingCodeCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }
    public RelayCommand PushToConversionCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand AddFunctionSearchInputCommand { get; }
    public RelayCommand ClearFunctionRecommendationCommand { get; }
    public event EventHandler<string>? PushToConversionRequested;
    internal bool IsEnglish => DisplayLanguage.Equals(ConfiguratorViewModel.EnglishLanguage, StringComparison.OrdinalIgnoreCase);

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
                OnPropertyChanged(nameof(SourceDocumentsText));
                OnPropertyChanged(nameof(FunctionRecommendationScope));
                foreach (var group in Groups)
                {
                    group.RefreshLanguage();
                }

                if (RequestedFunctions.Count == 0)
                {
                    FunctionRecommendationStatus = DefaultFunctionRecommendationStatus();
                }

                RefreshFromSelection();
                RefreshFunctionSuggestions();
            }
        }
    }

    public CnLegacySeriesViewModel? SelectedSeries
    {
        get => _selectedSeries;
        set
        {
            if (!SetProperty(ref _selectedSeries, value))
            {
                return;
            }

            Devices.Clear();
            if (value is not null)
            {
                foreach (var device in value.Devices)
                {
                    Devices.Add(device);
                }
            }

            SelectedDevice = Devices.FirstOrDefault();
            OnPropertyChanged(nameof(SourceDocumentsText));
        }
    }

    public CnLegacyDeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            LoadDevice(value);
            OnPropertyChanged(nameof(DeviceDescription));
            OnPropertyChanged(nameof(FunctionRecommendationScope));
            ClearFunctionRecommendation();
        }
    }

    public string DeviceDescription => SelectedDevice?.Description ?? "";
    public string SourceDocumentsText => SelectedSeries is null
        ? ""
        : string.Join(IsEnglish ? "; " : "；", SelectedSeries.SourceDocuments);
    public string FunctionRecommendationScope => SelectedDevice is null
        ? IsEnglish ? "No device selected" : "当前未选择装置"
        : IsEnglish ? $"{SelectedDevice.Name} standard configuration recommendation" : $"{SelectedDevice.Name} 标准配置推荐";

    public string FunctionSearchText
    {
        get => _functionSearchText;
        set
        {
            if (SetProperty(ref _functionSearchText, value))
            {
                RefreshFunctionSuggestions();
            }
        }
    }

    public string FunctionRecommendationStatus
    {
        get => _functionRecommendationStatus;
        private set => SetProperty(ref _functionRecommendationStatus, value);
    }

    public bool HasFunctionSuggestions => FunctionSuggestions.Count > 0;
    public bool HasRequestedFunctions => RequestedFunctions.Count > 0;
    public bool HasStandardConfigurationRecommendations => StandardConfigurationRecommendations.Count > 0;

    public string OrderingCode
    {
        get => _orderingCode;
        private set
        {
            if (SetProperty(ref _orderingCode, value))
            {
                CopyOrderingCodeCommand.RaiseCanExecuteChanged();
                ShowDeviceDescriptionCommand.RaiseCanExecuteChanged();
                PushToConversionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool HasErrors
    {
        get => _hasErrors;
        private set => SetProperty(ref _hasErrors, value);
    }

    internal void RefreshFromSelection()
    {
        // 一次选择变化的刷新顺序固定为：候选状态 -> 展示摘要/I/O/订货号 -> 终态校验 -> 功能推荐。
        // 所有阶段读取同一组 SelectedOption，避免订货号已更新而错误提示仍对应旧选择。
        RefreshAvailability();
        RefreshSummary();
        RefreshIoSummary();
        RefreshOrderingCode();
        RefreshValidationMessagesWithTargets();
        RefreshStandardConfigurationRecommendations();
    }

    private void LoadDevice(CnLegacyDeviceViewModel? device)
    {
        // 切换装置会替换整套位号定义，旧装置选项不能复用；新组在构造时先选默认值，
        // 集合完整建立后再统一刷新派生状态。
        Groups.Clear();
        if (device is not null)
        {
            foreach (var group in device.Model.Groups.Select(group => new CnLegacyGroupViewModel(group, this)))
            {
                Groups.Add(group);
            }
        }

        RefreshFromSelection();
        ResetCommand.RaiseCanExecuteChanged();
    }

    private void ResetSelections()
    {
        foreach (var group in Groups)
        {
            group.SelectDefault();
        }

        RefreshFromSelection();
    }

    private void SetAllGroupsExpanded(bool isExpanded)
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = isExpanded;
        }
    }

    private void RefreshFunctionSuggestions()
    {
        FunctionSuggestions.Clear();
        if (SelectedDevice is not null && !string.IsNullOrWhiteSpace(FunctionSearchText))
        {
            foreach (var function in _functionCatalog.Search(SelectedDevice.Id, FunctionSearchText, 10)
                         .Where(function => RequestedFunctions.All(requested =>
                             !CnLegacyFunctionCatalogService.FunctionKey(requested.Function)
                                 .Equals(CnLegacyFunctionCatalogService.FunctionKey(function), StringComparison.OrdinalIgnoreCase))))
            {
                FunctionSuggestions.Add(new CnLegacyFunctionSuggestionViewModel(function));
            }
        }

        OnPropertyChanged(nameof(HasFunctionSuggestions));
    }

    private void AddFunctionSearchInput()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var inputs = CnLegacyFunctionCatalogService.SplitSearchInput(FunctionSearchText);
        if (inputs.Count == 0 && FunctionSuggestions.FirstOrDefault() is { } firstSuggestion)
        {
            AddRequestedFunction(firstSuggestion.Function);
            FunctionSearchText = "";
            return;
        }

        var unresolved = new List<string>();
        foreach (var input in inputs)
        {
            var exact = _functionCatalog.ResolveExact(SelectedDevice.Id, input);
            if (exact is not null)
            {
                AddRequestedFunction(exact);
                continue;
            }

            var candidates = _functionCatalog.Search(SelectedDevice.Id, input, 3);
            if (candidates.Count == 1)
            {
                AddRequestedFunction(candidates[0]);
            }
            else
            {
                unresolved.Add(input);
            }
        }

        FunctionSearchText = unresolved.Count == 0 ? "" : string.Join("，", unresolved);
        FunctionRecommendationStatus = unresolved.Count == 0
            ? IsEnglish ? "Protection function added. Recommendation results have been updated." : "已加入保护功能，推荐结果已更新。"
            : IsEnglish
                ? $"The following inputs were not uniquely matched. Select from candidates: {string.Join(", ", unresolved)}"
                : $"以下输入未能唯一匹配，请从候选项中选择：{string.Join("，", unresolved)}";
        RefreshFunctionSuggestions();
    }

    public void AddRequestedFunction(CnLegacyFunctionEntry function)
    {
        if (SelectedDevice is null ||
            !function.DeviceId.Equals(SelectedDevice.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = CnLegacyFunctionCatalogService.FunctionKey(function);
        if (RequestedFunctions.Any(item =>
                CnLegacyFunctionCatalogService.FunctionKey(item.Function).Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            FunctionRecommendationStatus = IsEnglish ? "This protection function is already in the recommendation criteria." : "该保护功能已在推荐条件中。";
            return;
        }

        RequestedFunctions.Add(new CnLegacyRequestedFunctionViewModel(function));
        OnPropertyChanged(nameof(HasRequestedFunctions));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        RefreshFunctionSuggestions();
        RefreshStandardConfigurationRecommendations();
    }

    public void RemoveRequestedFunction(CnLegacyRequestedFunctionViewModel function)
    {
        RequestedFunctions.Remove(function);
        OnPropertyChanged(nameof(HasRequestedFunctions));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        RefreshFunctionSuggestions();
        RefreshStandardConfigurationRecommendations();
    }

    private void ClearFunctionRecommendation()
    {
        RequestedFunctions.Clear();
        FunctionSuggestions.Clear();
        StandardConfigurationRecommendations.Clear();
        FunctionSearchText = "";
        FunctionRecommendationStatus = DefaultFunctionRecommendationStatus();
        OnPropertyChanged(nameof(HasRequestedFunctions));
        OnPropertyChanged(nameof(HasFunctionSuggestions));
        OnPropertyChanged(nameof(HasStandardConfigurationRecommendations));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        AddFunctionSearchInputCommand.RaiseCanExecuteChanged();
    }

    private void RefreshStandardConfigurationRecommendations()
    {
        StandardConfigurationRecommendations.Clear();
        if (SelectedDevice is null || RequestedFunctions.Count == 0)
        {
            if (RequestedFunctions.Count == 0)
            {
                FunctionRecommendationStatus = DefaultFunctionRecommendationStatus();
            }

            OnPropertyChanged(nameof(HasStandardConfigurationRecommendations));
            return;
        }

        var selectableCodes = Groups
            .FirstOrDefault(group => group.Position.Equals("4", StringComparison.OrdinalIgnoreCase))
            ?.Options
            .Select(option => option.Code)
            .ToList() ?? [];

        var recommendations = _functionCatalog.Recommend(
            SelectedDevice.Id,
            RequestedFunctions.Select(function => function.Function),
            selectableCodes);

        foreach (var item in recommendations)
        {
            StandardConfigurationRecommendations.Add(new CnLegacyStandardConfigurationRecommendationViewModel(item, IsEnglish));
        }

        FunctionRecommendationStatus = recommendations.Count switch
        {
            0 => IsEnglish
                ? "No standard configuration for the current device covers the entered protection functions."
                : "当前装置的标准配置不能覆盖已输入的保护功能。",
            _ when recommendations.Any(item => item.IsFullMatch) => IsEnglish
                ? "A standard configuration with full coverage was found."
                : "已找到可完整覆盖的标准配置。",
            _ => IsEnglish
                ? "No single standard configuration covers all functions. The best coverage candidates are listed below."
                : "没有单个标准配置可完整覆盖，以下为覆盖度最高的配置。"
        };
        OnPropertyChanged(nameof(HasStandardConfigurationRecommendations));
    }

    public void ApplyStandardConfigurationRecommendation(CnLegacyStandardConfigurationRecommendationViewModel recommendation)
    {
        var group = Groups.FirstOrDefault(item => item.Position.Equals("4", StringComparison.OrdinalIgnoreCase));
        if (group is null || !recommendation.CanApply)
        {
            FunctionRecommendationStatus = IsEnglish
                ? "This recommendation is not available in the current order code's standard configuration position and is shown only as a manual reference."
                : "该推荐配置不在当前订货码标准配置位中，仅作为手册配置参考。";
            return;
        }

        if (group.SelectByCode(recommendation.ConfigCode))
        {
            FunctionRecommendationStatus = IsEnglish
                ? $"Standard configuration {recommendation.ConfigCode} applied."
                : $"已应用标准配置 {recommendation.ConfigCode}。";
        }
    }

    private string DefaultFunctionRecommendationStatus() => IsEnglish
        ? "Enter ABB Code, ANSI Code or protection function name to recommend standard configurations that cover the selected functions."
        : "输入 ABB Code、ANSI Code 或保护功能名称，推荐可覆盖这些功能的标准配置。";

    private void RefreshOrderingCode()
    {
        OrderingCode = string.Concat(Groups.Select(group => group.SelectedOption?.Code ?? ""));
    }

    private void RefreshSummary()
    {
        SummaryItems.Clear();
        foreach (var group in Groups)
        {
            SummaryItems.Add(new CnLegacySelectionSummaryItemViewModel(
                group.Position,
                group.Name,
                group.SelectedOption?.Code ?? "",
                group.SelectedOption?.Description ?? ""));
        }
    }

    private void RefreshIoSummary()
    {
        IoSummaryItems.Clear();
        foreach (var item in BuildIoSummary())
        {
            IoSummaryItems.Add(item);
        }
    }

    private IEnumerable<IoSummaryItemViewModel> BuildIoSummary()
    {
        var communication = Groups
            .Where(IsCommunicationHardwareGroup)
            .Select(BuildCommunicationSummaryPart)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        if (communication.Count > 0)
        {
            yield return new IoSummaryItemViewModel(IsEnglish ? "Communication module" : "通讯模块", string.Join(IsEnglish ? "; " : "；", communication));
        }

        var selectedDescriptions = Groups
            .Where(IsIoCountSourceGroup)
            .Select(group => group.SelectedOption)
            .Where(option => option is not null)
            .Select(option => option!.DescriptionSource)
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .ToList();

        foreach (var key in new[] { "CT", "VT", "BI", "BO", "HSO", "RTD", "mA" })
        {
            var value = selectedDescriptions.Sum(description => GetIoCount(description, key));
            if (value > 0)
            {
                yield return new IoSummaryItemViewModel(key, value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static bool IsCommunicationHardwareGroup(CnLegacyGroupViewModel group) =>
        group.Position.Equals("9", StringComparison.OrdinalIgnoreCase) ||
        group.Position.Equals("10", StringComparison.OrdinalIgnoreCase) ||
        group.Position.Equals("9-10", StringComparison.OrdinalIgnoreCase);

    private static bool IsIoCountSourceGroup(CnLegacyGroupViewModel group) =>
        group.Position.Equals("5-6", StringComparison.OrdinalIgnoreCase) ||
        group.Position.Equals("7-8", StringComparison.OrdinalIgnoreCase);

    private string? BuildCommunicationSummaryPart(CnLegacyGroupViewModel group)
    {
        var option = group.SelectedOption;
        if (option is null || IsNoneOption(option))
        {
            return null;
        }

        return IsEnglish
            ? $"Position {group.Position}: {option.ShortDescription}"
            : $"{group.Name}: {option.ShortDescription}";
    }

    private static bool IsNoneOption(CnLegacyOptionViewModel option)
    {
        var text = $"{option.Code} {option.ShortDescription} {option.Description}";
        return option.Code.Equals("N", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("None", StringComparison.OrdinalIgnoreCase) ||
                text.Contains('无'));
    }

    private static int GetIoCount(string source, string key)
    {
        var pattern = key switch
        {
            "CT" => @"(?<![A-Za-z])(\d+)\s*(?:I|CT)(?![A-Za-z])",
            "VT" => @"(?<![A-Za-z])(\d+)\s*(?:U|VT)(?![A-Za-z])",
            "BI" => @"(?<![A-Za-z])(\d+)\s*BI(?![A-Za-z])",
            "BO" => @"(?<![A-Za-z])(\d+)\s*BO(?![A-Za-z])",
            "HSO" => @"(?<![A-Za-z])(\d+)\s*HSO(?![A-Za-z])",
            "RTD" => @"(?<![A-Za-z])(\d+)\s*RTD(?![A-Za-z])",
            "mA" => @"(?<![A-Za-z])(\d+)\s*mA(?![A-Za-z])",
            _ => ""
        };

        return string.IsNullOrWhiteSpace(pattern)
            ? 0
            : Regex.Matches(source, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Sum(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    private void RefreshAvailability()
    {
        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                // PDF 选型表衍生规则只控制页面当前可选项；原 XML pattern 块保留给最终校验和错误定位，两层不可混用。
                // 计算候选时 SelectedCodesWith 会临时把该候选代入自己的位号，不会改变当前真实选择。
                var selectionResult = EvaluateSelectionRules(option);
                option.SetAvailability(selectionResult.IsValid);

                var validationResult = option.IsSelected
                    ? EvaluateValidationWithTargets(option)
                    : null;
                option.SetError(validationResult is { IsValid: false });
            }

            group.RefreshValidationState();
        }
    }

    private void RefreshValidationMessages()
    {
        ValidationMessages.Clear();

        foreach (var group in Groups)
        {
            if (group.Model.IsRequired && group.SelectedOption is null)
            {
                ValidationMessages.Add(new CnLegacyValidationMessageViewModel(
                    IsEnglish ? $"{group.Name} must have one option selected." : $"{group.Name} 必须选择一项。"));
                continue;
            }

            if (group.SelectedOption is not null)
            {
                var result = EvaluateValidationWithTargets(group.SelectedOption);
                if (!result.IsValid)
                {
                    foreach (var issue in result.Issues)
                    {
                        ValidationMessages.Add(new CnLegacyValidationMessageViewModel(
                            IsEnglish
                                ? $"{group.Name} / {group.SelectedOption.Code}: {issue.Message}"
                                : $"{group.Name} / {group.SelectedOption.Code}：{issue.Message}"));
                    }
                }
            }
        }

        HasErrors = ValidationMessages.Count > 0;
        Status = HasErrors
            ? IsEnglish ? "Order code needs adjustment" : "订货号需要调整"
            : IsEnglish ? "Offline rule validation passed" : "离线规则校验通过";
    }

    private void RefreshValidationMessagesWithTargets()
    {
        // 每次完整重建消息集合，并把业务问题关联到一个或多个位号/代码；
        // 导航目标不是新的校验规则，只为界面展开最可能需要调整的选择组。
        ValidationMessages.Clear();

        foreach (var group in Groups)
        {
            if (group.Model.IsRequired && group.SelectedOption is null)
            {
                ValidationMessages.Add(new CnLegacyValidationMessageViewModel(
                    IsEnglish ? $"{group.Name} must have one option selected." : $"{group.Name} 必须选择一项。",
                    [new CnLegacyValidationTargetViewModel(group.Position, group.Name, null)]));
                continue;
            }

            if (group.SelectedOption is null)
            {
                continue;
            }

            var result = EvaluateValidationWithTargets(group.SelectedOption);
            foreach (var issue in result.Issues)
            {
                ValidationMessages.Add(new CnLegacyValidationMessageViewModel(
                    IsEnglish ? $"{group.Name} / {group.SelectedOption.Code}: {issue.Message}" : $"{group.Name} / {group.SelectedOption.Code}：{issue.Message}",
                    issue.Targets));
            }
        }

        HasErrors = ValidationMessages.Count > 0;
        Status = HasErrors
            ? IsEnglish ? "Order code needs adjustment" : "订货号需要调整"
            : IsEnglish ? "Offline rule validation passed" : "离线规则校验通过";
    }

    private CnLegacyEvaluationResultWithTargets EvaluateValidationWithTargets(CnLegacyOptionViewModel option)
    {
        // 终态校验依次覆盖版本适用性、结构化依赖/排除关系和原 XML pattern 块。
        // 同一问题同时携带触发项和期望项，便于用户在消息区直接定位冲突双方。
        var issues = new List<CnLegacyValidationIssue>();
        var selectedCodes = SelectedCodesWith(option);
        var version = CurrentVersionCode(option);

        if (!option.Model.SupportsVersion(version))
        {
            issues.Add(new CnLegacyValidationIssue(
                IsEnglish
                    ? $"{option.Code} is not available for product version {version}."
                    : $"{option.Code} 不适用于产品版本 {version}。",
                [new CnLegacyValidationTargetViewModel(option.Group.Position, option.Group.Name, option.Code)]));
        }

        foreach (var requirement in option.Model.RequiredSelections)
        {
            if (!ShouldEvaluateRequirement(requirement))
            {
                continue;
            }

            var targetGroup = Groups.FirstOrDefault(group =>
                group.Position.Equals(requirement.Position, StringComparison.OrdinalIgnoreCase));
            selectedCodes.TryGetValue(requirement.Position, out var selectedCode);

            var matches = !string.IsNullOrWhiteSpace(selectedCode) &&
                          requirement.Codes.Any(code => code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase));
            var isValid = requirement.Mode.Equals("NoneOf", StringComparison.OrdinalIgnoreCase)
                ? !matches
                : matches;

            if (isValid)
            {
                continue;
            }

            var expected = string.Join("/", requirement.Codes);
            var targetName = targetGroup?.Name ?? requirement.Position;
            var targets = new List<CnLegacyValidationTargetViewModel>
            {
                new(option.Group.Position, option.Group.Name, option.Code)
            };

            if (targetGroup is not null)
            {
                if (requirement.Mode.Equals("NoneOf", StringComparison.OrdinalIgnoreCase))
                {
                    targets.Add(new CnLegacyValidationTargetViewModel(targetGroup.Position, targetGroup.Name, selectedCode));
                }
                else
                {
                    foreach (var code in requirement.Codes)
                    {
                        targets.Add(new CnLegacyValidationTargetViewModel(targetGroup.Position, targetGroup.Name, code));
                    }
                }
            }

            issues.Add(new CnLegacyValidationIssue(
                BuildRequirementMessage(requirement, targetName, expected, selectedCode),
                DeduplicateTargets(targets)));
        }

        foreach (var exclusion in option.Model.ExcludedCombinedSelections)
        {
            var combined = string.Concat(exclusion.Positions.Select(position =>
                selectedCodes.TryGetValue(position, out var code) ? code : ""));
            if (!exclusion.Codes.Any(code => code.Equals(combined, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var targets = exclusion.Positions
                .Select(position => Groups.FirstOrDefault(group => group.Position.Equals(position, StringComparison.OrdinalIgnoreCase)))
                .Where(group => group is not null)
                .Select(group => new CnLegacyValidationTargetViewModel(
                    group!.Position,
                    group.Name,
                    selectedCodes.TryGetValue(group.Position, out var code) ? code : group.SelectedOption?.Code))
                .ToList();
            targets.Insert(0, new CnLegacyValidationTargetViewModel(option.Group.Position, option.Group.Name, option.Code));

            issues.Add(new CnLegacyValidationIssue(
                BuildExclusionMessage(exclusion.Message, combined),
                DeduplicateTargets(targets)));
        }

        issues.AddRange(EvaluatePatternRulesWithTargets(option, selectedCodes, version));

        return new CnLegacyEvaluationResultWithTargets(issues.Count == 0, issues);
    }

    private IEnumerable<CnLegacyValidationIssue> EvaluatePatternRulesWithTargets(
        CnLegacyOptionViewModel option,
        IReadOnlyDictionary<string, string> selectedCodes,
        string version)
    {
        foreach (var block in SelectedDevice?.Model.ValidationBlocks ?? [])
        {
            // 一个跨位号块只由其首个位号负责生成消息，避免遍历每个已选项时重复报告同一组合错误。
            if (block.Positions.Count == 0 ||
                !block.Positions[0].Equals(option.Group.Position, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = BuildBlockValue(block, selectedCodes);
            if (string.IsNullOrWhiteSpace(value) || MatchesPatternBlock(block, value, version))
            {
                continue;
            }

            yield return new CnLegacyValidationIssue(
                BuildPatternMessage(block, value, selectedCodes),
                DeduplicateTargets(BuildPatternTargets(block, selectedCodes)));
        }
    }

    private static IReadOnlyList<CnLegacyValidationTargetViewModel> DeduplicateTargets(
        IEnumerable<CnLegacyValidationTargetViewModel> targets)
    {
        return targets
            .Where(target => !string.IsNullOrWhiteSpace(target.GroupPosition))
            .GroupBy(target => $"{target.GroupPosition}|{target.OptionCode}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public void JumpToMessage(CnLegacyValidationMessageViewModel message)
    {
        if (message.PrimaryTarget is not null)
        {
            JumpToTarget(message.PrimaryTarget);
        }
    }

    public void JumpToTarget(CnLegacyValidationTargetViewModel target)
    {
        var group = Groups.FirstOrDefault(item =>
            item.Position.Equals(target.GroupPosition, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            group.IsExpanded = true;
        }
    }

    private CnLegacyEvaluationResult EvaluateSelectionRules(CnLegacyOptionViewModel option)
    {
        // 交互可用性只检查能直接解释为候选前置条件的版本、RequiredSelections 和排除组合；
        // pattern 规则通常描述完整代码块，选择尚未完成时不应用它提前禁用候选。
        var messages = new List<string>();
        var selectedCodes = SelectedCodesWith(option);
        var version = CurrentVersionCode(option);

        if (!option.Model.SupportsVersion(version))
        {
            messages.Add(IsEnglish
                ? $"{option.Code} is not available for product version {version}."
                : $"{option.Code} 不适用于产品版本 {version}。");
        }

        foreach (var requirement in option.Model.RequiredSelections)
        {
            if (!ShouldEvaluateRequirement(requirement))
            {
                continue;
            }

            selectedCodes.TryGetValue(requirement.Position, out var selectedCode);

            var matches = !string.IsNullOrWhiteSpace(selectedCode) &&
                          requirement.Codes.Any(code => code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase));
            var isValid = requirement.Mode.Equals("NoneOf", StringComparison.OrdinalIgnoreCase)
                ? !matches
                : matches;

            if (!isValid)
            {
                var targetGroup = Groups.FirstOrDefault(group =>
                    group.Position.Equals(requirement.Position, StringComparison.OrdinalIgnoreCase));
                var expected = string.Join("/", requirement.Codes);
                var targetName = targetGroup?.Name ?? requirement.Position;
                messages.Add(BuildRequirementMessage(requirement, targetName, expected, selectedCode));
            }
        }

        foreach (var exclusion in option.Model.ExcludedCombinedSelections)
        {
            var combined = string.Concat(exclusion.Positions.Select(position =>
                selectedCodes.TryGetValue(position, out var code) ? code : ""));
            if (exclusion.Codes.Any(code => code.Equals(combined, StringComparison.OrdinalIgnoreCase)))
            {
                messages.Add(BuildExclusionMessage(exclusion.Message, combined));
            }
        }

        return new CnLegacyEvaluationResult(messages.Count == 0, messages);
    }

    private IReadOnlyDictionary<string, string> SelectedCodesWith(CnLegacyOptionViewModel option)
    {
        // 建立当前选择快照后覆盖候选所在位号，用于无副作用地回答“如果选它会怎样”。
        var selectedCodes = Groups
            .Select(group => (group.Position, Code: group.SelectedOption?.Code ?? ""))
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .ToDictionary(item => item.Position, item => item.Code, StringComparer.OrdinalIgnoreCase);
        selectedCodes[option.Group.Position] = option.Code;
        return selectedCodes;
    }

    private string CurrentVersionCode(CnLegacyOptionViewModel? candidate = null)
    {
        if (candidate?.Group.Position.Equals("17-18", StringComparison.OrdinalIgnoreCase) == true)
        {
            return candidate.Code;
        }

        return Groups.FirstOrDefault(group => group.Position.Equals("17-18", StringComparison.OrdinalIgnoreCase))
            ?.SelectedOption
            ?.Code ?? "";
    }

    private static string BuildBlockValue(
        CnLegacyValidationBlock block,
        IReadOnlyDictionary<string, string> selectedCodes)
    {
        // pattern 必须针对完整连续逻辑块判断；任一参与位号尚未选择时返回空值并暂缓校验，
        // 防止把“不完整”误报为“组合不允许”。
        var parts = new List<string>();
        foreach (var position in block.Positions)
        {
            if (!selectedCodes.TryGetValue(position, out var code) || string.IsNullOrWhiteSpace(code))
            {
                return "";
            }

            parts.Add(code);
        }

        return string.Concat(parts);
    }

    private static bool MatchesPatternBlock(CnLegacyValidationBlock block, string value, string version) =>
        block.Rules.Any(rule => rule.SupportsVersion(version) && rule.Matches(value));

    private string BuildPatternMessage(
        CnLegacyValidationBlock block,
        string value,
        IReadOnlyDictionary<string, string> selectedCodes)
    {
        var displayName = string.IsNullOrWhiteSpace(block.DisplayName) ? block.Name : block.DisplayName;
        var details = string.Join(
            IsEnglish ? ", " : "，",
            block.Positions.Select(position =>
            {
                var group = Groups.FirstOrDefault(item => item.Position.Equals(position, StringComparison.OrdinalIgnoreCase));
                var name = group?.Name ?? position;
                return $"{name}={selectedCodes.GetValueOrDefault(position, "")}";
            }));

        return IsEnglish
            ? $"{displayName} combination does not match the XML ordering rules: {value} ({details})."
            : $"{displayName}组合不满足 XML 订货规则：{value}（{details}）。";
    }

    private IEnumerable<CnLegacyValidationTargetViewModel> BuildPatternTargets(
        CnLegacyValidationBlock block,
        IReadOnlyDictionary<string, string> selectedCodes)
    {
        foreach (var position in block.Positions)
        {
            var group = Groups.FirstOrDefault(item => item.Position.Equals(position, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                continue;
            }

            yield return new CnLegacyValidationTargetViewModel(
                group.Position,
                group.Name,
                selectedCodes.TryGetValue(position, out var code) ? code : group.SelectedOption?.Code);
        }
    }

    private bool ShouldEvaluateRequirement(CnLegacySelectionRequirement requirement)
    {
        return requirement.WhenSelections.Count == 0 ||
               requirement.WhenSelections.All(SelectionConditionMatches);
    }

    private bool SelectionConditionMatches(CnLegacySelectionCondition condition)
    {
        var selectedCode = Groups.FirstOrDefault(group =>
                group.Position.Equals(condition.Position, StringComparison.OrdinalIgnoreCase))
            ?.SelectedOption
            ?.Code;

        var matches = !string.IsNullOrWhiteSpace(selectedCode) &&
                      condition.Codes.Any(code => code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase));
        return condition.Mode.Equals("NoneOf", StringComparison.OrdinalIgnoreCase)
            ? !matches
            : matches;
    }

    private string BuildRequirementMessage(
        CnLegacySelectionRequirement requirement,
        string targetName,
        string expected,
        string? selectedCode)
    {
        var current = selectedCode ?? (IsEnglish ? "not selected" : "未选");
        if (IsEnglish)
        {
            return requirement.Mode.Equals("NoneOf", StringComparison.OrdinalIgnoreCase)
                ? $"This combination is not allowed. Current {targetName}={current}."
                : $"{targetName} must be {expected}; current value is {current}.";
        }

        return requirement.Mode.Equals("NoneOf", StringComparison.OrdinalIgnoreCase)
            ? $"{requirement.Message}，当前 {targetName}={current}。"
            : $"{requirement.Message}，{targetName} 需选择 {expected}，当前为 {current}。";
    }

    private string BuildExclusionMessage(string? message, string combined)
    {
        if (IsEnglish)
        {
            return $"Cannot be selected together with combination {combined}.";
        }

        return string.IsNullOrWhiteSpace(message)
            ? $"不能与组合 {combined} 同时选择。"
            : message;
    }

    private void CopyOrderingCode()
    {
        if (string.IsNullOrWhiteSpace(OrderingCode))
        {
            return;
        }

        if (ClipboardService.TrySetText(OrderingCode, "615/620 CN", IsEnglish))
        {
            Status = IsEnglish ? "Order code copied." : "订货号已复制。";
        }
    }

    private void ImportOrderingCode()
    {
        var window = new CombinationCodeImportWindow(
            IsEnglish ? "Import 615/620 CN order code" : "导入 615/620 CN 订货号",
            IsEnglish
                ? "Enter a complete 18-character 615 CN 5.1 or 620 CN 2.1 order code. The tool will detect the series and device type automatically."
                : "请输入完整 18 位 615 CN 5.1 或 620 CN 2.1 订货号，软件会自动识别系列和装置类型。",
            IsEnglish ? "Import" : "导入",
            IsEnglish ? "Example: HCFCACABNBC2ACN11G or NBFNAANNABC2DNN11G" : "例如：HCFCACABNBC2ACN11G 或 NBFNAANNABC2DNN11G")
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        ImportOrderingCodeValue(window.CombinationCode);
    }

    private void ImportOrderingCodeValue(string value, bool showMessages = true)
    {
        // 导入先规范化为纯大写字母数字，再依次确认固定长度、产品系列和第 3 位主要应用；
        // 在这些结构条件成立前不会清空当前页面选择。
        var code = NormalizeOrderingCode(value);
        if (code.Length != 18)
        {
            var message = IsEnglish ? "The order code must contain 18 characters." : "订货号必须为 18 位代码。";
            ShowImportWarning(message, showMessages);
            return;
        }

        var series = DetectSeries(code);
        if (series is null)
        {
            var message = IsEnglish
                ? "The order code series cannot be identified. 615 usually starts with H or 1; 620 usually starts with N or 5."
                : "无法识别订货号系列。615 通常以 H 或 1 开头，620 通常以 N 或 5 开头。";
            ShowImportWarning(message, showMessages);
            return;
        }

        var applicationCode = code[2].ToString();
        var device = series.Devices.FirstOrDefault(item => item.Model.Groups
            .FirstOrDefault(group => group.Position.Equals("3", StringComparison.OrdinalIgnoreCase))
            ?.Options
            .Any(option => option.Code.Equals(applicationCode, StringComparison.OrdinalIgnoreCase)) == true);

        if (device is null)
        {
            var message = IsEnglish
                ? $"No device type for main application code {applicationCode} was found in the current data package."
                : $"当前数据包中没有找到主要应用代码 {applicationCode} 对应的装置类型。";
            ShowImportWarning(message, showMessages);
            return;
        }

        SelectedSeries = series;
        SelectedDevice = device;

        // 先静默写入全部位号，再统一刷新可用性和校验，避免半导入状态用旧选择误判后续位号。
        // 单个位号无法匹配时继续解析其余位号，并在末尾汇总报告，便于识别数据包版本差异。
        var index = 0;
        var notFound = new List<string>();
        foreach (var group in Groups)
        {
            var length = group.CodeLength;
            var part = code.Substring(index, length);
            index += length;

            if (!group.SelectByCode(part, refreshOwner: false))
            {
                notFound.Add($"{group.Position} {group.Name}={part}");
            }
        }

        RefreshFromSelection();
        Status = notFound.Count == 0
            ? IsEnglish ? "Order code imported." : "订货号已导入。"
            : IsEnglish
                ? $"Order code imported, but these positions were not matched: {string.Join("; ", notFound)}"
                : $"订货号已导入，但以下位号未匹配：{string.Join("；", notFound)}";
    }

    private void ShowImportWarning(string message, bool showMessages)
    {
        Status = message;
        if (!showMessages)
        {
            return;
        }

        MessageBox.Show(
            message,
            IsEnglish ? "615/620 CN Configurator" : "615/620 CN 选型",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private CnLegacySeriesViewModel? DetectSeries(string code)
    {
        var first = code[0];
        var targetId = first is 'H' or '1'
            ? "615_CN_5_1"
            : first is 'N' or '5'
                ? "620_CN_2_1"
                : null;

        return targetId is null
            ? null
            : Series.FirstOrDefault(item => item.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeOrderingCode(string value) =>
        new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private void ShowDeviceDescription()
    {
        var window = new DeviceDescriptionWindow(BuildDeviceDescription())
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };
        window.ShowDialog();
    }

    private string BuildDeviceDescription()
    {
        var lines = new List<string>
        {
            IsEnglish ? "615/620 CN device configuration description" : "615/620 CN 装置选型描述",
            IsEnglish ? $"Product series: {SelectedSeries?.Name ?? ""}" : $"产品系列：{SelectedSeries?.Name ?? ""}",
            IsEnglish ? $"Device type: {SelectedDevice?.Name ?? ""}" : $"装置类型：{SelectedDevice?.Name ?? ""}",
            IsEnglish ? $"Order code: {OrderingCode}" : $"订货号：{OrderingCode}",
            IsEnglish ? $"Status: {Status}" : $"状态：{Status}",
            ""
        };

        lines.Add(IsEnglish ? "Current selection:" : "当前选择：");
        lines.AddRange(SummaryItems.Select(item => IsEnglish
            ? $"{item.Position} {item.GroupName}: {item.Code} - {item.Description}"
            : $"{item.Position} {item.GroupName}：{item.Code} - {item.Description}"));

        lines.Add("");
        lines.Add(IsEnglish ? "I/O summary:" : "I/O 摘要：");
        lines.Add(IoSummaryItems.Count == 0
            ? IsEnglish ? "None" : "无"
            : string.Join(IsEnglish ? "; " : "；", IoSummaryItems.Select(item => $"{item.Name}={item.Value}")));

        if (ValidationMessages.Count > 0)
        {
            lines.Add("");
            lines.Add(IsEnglish ? "Validation messages:" : "校验提示：");
            lines.AddRange(ValidationMessages.Select(message => message.Message));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void PushToConversion()
    {
        if (string.IsNullOrWhiteSpace(OrderingCode))
        {
            return;
        }

        PushToConversionRequested?.Invoke(this, OrderingCode);
        Status = IsEnglish ? "Sent to the 615/620 conversion page." : "已推送到 615/620 转换页面。";
    }
}

public sealed class CnLegacySeriesViewModel
{
    public CnLegacySeriesViewModel(CnLegacyProductSeries model)
    {
        Model = model;
        Devices = model.Devices.Select(device => new CnLegacyDeviceViewModel(device)).ToList();
    }

    public CnLegacyProductSeries Model { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string Description => Model.Description;
    public IReadOnlyList<string> SourceDocuments => Model.SourceDocuments;
    public IReadOnlyList<CnLegacyDeviceViewModel> Devices { get; }
}

public sealed class CnLegacyDeviceViewModel(CnLegacyDevice model)
{
    public CnLegacyDevice Model { get; } = model;
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string Description => Model.Description;
}

public sealed class CnLegacyGroupViewModel : ObservableObject
{
    private CnLegacyOptionViewModel? _selectedOption;
    private bool _isExpanded = true;
    private int _errorCount;

    public CnLegacyGroupViewModel(CnLegacyCodeGroup model, CnLegacySelectorViewModel owner)
    {
        Model = model;
        Owner = owner;
        Options = new ObservableCollection<CnLegacyOptionViewModel>(
            model.Options.Select(option => new CnLegacyOptionViewModel(option, this)));
        SelectDefault(refreshOwner: false);
    }

    public CnLegacyCodeGroup Model { get; }
    public CnLegacySelectorViewModel Owner { get; }
    public string Position => Model.Position;
    public string Name => Model.Name;
    public int CodeLength => Position is "5-6" or "7-8" or "9-10" or "17-18" ? 2 : 1;
    public ObservableCollection<CnLegacyOptionViewModel> Options { get; }

    public CnLegacyOptionViewModel? SelectedOption
    {
        get => _selectedOption;
        private set
        {
            if (SetProperty(ref _selectedOption, value))
            {
                OnPropertyChanged(nameof(SelectedSummary));
            }
        }
    }

    public string SelectedSummary => SelectedOption is null
        ? Owner.IsEnglish ? "Not selected" : "未选择"
        : $"{SelectedOption.Code}：{SelectedOption.ShortDescription}";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set
        {
            if (SetProperty(ref _errorCount, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ErrorSummary));
            }
        }
    }

    public bool HasError => ErrorCount > 0;
    public string ErrorSummary => Owner.IsEnglish ? $"{ErrorCount} issue(s)" : $"需处理 {ErrorCount}";

    internal void RefreshValidationState() => ErrorCount = Options.Count(option => option.HasError);

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(ErrorSummary));
    }

    public void Select(CnLegacyOptionViewModel option)
    {
        if (SelectedOption == option)
        {
            option.SetSelected(true);
            return;
        }

        // 每个位号是严格单选；先静默同步同组所有 IsSelected，再设置 SelectedOption，
        // 最后只通知 owner 刷新一次完整状态。
        foreach (var item in Options)
        {
            item.SetSelected(ReferenceEquals(item, option));
        }

        SelectedOption = option;
        Owner.RefreshFromSelection();
    }

    public void SelectDefault(bool refreshOwner = true)
    {
        var option = Options.FirstOrDefault(item => item.Model.IsDefault) ?? Options.FirstOrDefault();
        SelectOption(option, refreshOwner);
    }

    public bool SelectByCode(string code, bool refreshOwner = true)
    {
        var option = Options.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            return false;
        }

        SelectOption(option, refreshOwner);
        return true;
    }

    private void SelectOption(CnLegacyOptionViewModel? option, bool refreshOwner)
    {
        if (option is not null)
        {
            foreach (var item in Options)
            {
                item.SetSelected(ReferenceEquals(item, option));
            }

            SelectedOption = option;
        }

        if (refreshOwner)
        {
            Owner.RefreshFromSelection();
        }
    }
}

public sealed class CnLegacyOptionViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isAvailable = true;
    private bool _hasError;

    public CnLegacyOptionViewModel(CnLegacyCodeOption model, CnLegacyGroupViewModel group)
    {
        Model = model;
        Group = group;
    }

    public CnLegacyCodeOption Model { get; }
    public CnLegacyGroupViewModel Group { get; }
    public string Code => Model.Code;
    public string Description => Model.Description;
    public string ShortDescription => string.IsNullOrWhiteSpace(Model.ShortDescription) ? Model.Description : Model.ShortDescription;
    public string DescriptionSource => string.IsNullOrWhiteSpace(Model.Description) ? ShortDescription : Model.Description;
    public string DisplayText => $"{Code}：{Description}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value)
            {
                Group.Select(this);
            }
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set => SetProperty(ref _isAvailable, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    internal void SetSelected(bool value)
    {
        SetProperty(ref _isSelected, value, nameof(IsSelected));
    }

    internal void SetAvailability(bool value) => IsAvailable = value;

    internal void SetError(bool value) => HasError = value;
}

public sealed class CnLegacySelectionSummaryItemViewModel(
    string position,
    string groupName,
    string code,
    string description)
{
    public string Position { get; } = position;
    public string GroupName { get; } = groupName;
    public string Code { get; } = code;
    public string Description { get; } = description;
}

public sealed class CnLegacyValidationTargetViewModel(string groupPosition, string groupName, string? optionCode)
{
    public string GroupPosition { get; } = groupPosition;
    public string GroupName { get; } = groupName;
    public string? OptionCode { get; } = optionCode;
    public string Label => string.IsNullOrWhiteSpace(OptionCode) ? GroupName : $"{GroupName} / {OptionCode}";
}

public sealed class CnLegacyValidationMessageViewModel(
    string message,
    IEnumerable<CnLegacyValidationTargetViewModel>? targets = null)
{
    public string Message { get; } = message;
    public IReadOnlyList<CnLegacyValidationTargetViewModel> Targets { get; } = targets?.ToList() ?? [];
    public bool HasTargets => Targets.Count > 0;
    public CnLegacyValidationTargetViewModel? PrimaryTarget => Targets.FirstOrDefault();
}

public sealed class CnLegacyFunctionSuggestionViewModel(CnLegacyFunctionEntry function)
{
    public CnLegacyFunctionEntry Function { get; } = function;
    public string AbbCode => Function.AbbCode;
    public string AnsiCode => Function.AnsiCode;
    public string Name => Function.DisplayName;
    public string Category => Function.Category;
    public string DisplayText => $"{Function.CodeSummary}：{Name}";
}

public sealed class CnLegacyRequestedFunctionViewModel(CnLegacyFunctionEntry function)
{
    public CnLegacyFunctionEntry Function { get; } = function;
    public string AbbCode => Function.AbbCode;
    public string AnsiCode => Function.AnsiCode;
    public string Name => Function.DisplayName;
    public string DisplayText => $"{Function.CodeSummary}：{Name}";
}

public sealed class CnLegacyStandardConfigurationRecommendationViewModel(
    CnLegacyStandardConfigurationRecommendation recommendation,
    bool isEnglish)
{
    public CnLegacyStandardConfigurationRecommendation Recommendation { get; } = recommendation;
    public string DeviceName => Recommendation.DeviceName;
    public string ConfigCode => Recommendation.ConfigCode;
    public string ConfigDescription => Recommendation.ConfigDescription;
    public string MatchStatus => IsFullMatch
        ? isEnglish ? $"Full coverage for {Recommendation.CoveredFunctions.Count} function(s)" : Recommendation.MatchStatus
        : isEnglish
            ? $"Covers {Recommendation.CoveredFunctions.Count}; missing {Recommendation.MissingFunctions.Count}"
            : Recommendation.MatchStatus;
    public string CoveredSummary => Recommendation.CoveredSummary;
    public string MissingSummary => Recommendation.MissingSummary;
    public string CoveredText => isEnglish ? $"Covered: {CoveredSummary}" : $"覆盖：{CoveredSummary}";
    public string MissingText => isEnglish ? $"Missing: {MissingSummary}" : $"缺少：{MissingSummary}";
    public bool HasMissing => Recommendation.MissingFunctions.Count > 0;
    public bool IsFullMatch => Recommendation.IsFullMatch;
    public bool CanApply => Recommendation.CanApply;
    public string ApplyHint => CanApply
        ? isEnglish ? "Apply configuration" : Recommendation.ApplyHint
        : isEnglish ? "Current order code does not have this standard configuration position" : Recommendation.ApplyHint;
}

internal sealed record CnLegacyValidationIssue(
    string Message,
    IReadOnlyList<CnLegacyValidationTargetViewModel> Targets);

internal sealed record CnLegacyEvaluationResult(bool IsValid, IReadOnlyList<string> Messages);
internal sealed record CnLegacyEvaluationResultWithTargets(bool IsValid, IReadOnlyList<CnLegacyValidationIssue> Issues);
