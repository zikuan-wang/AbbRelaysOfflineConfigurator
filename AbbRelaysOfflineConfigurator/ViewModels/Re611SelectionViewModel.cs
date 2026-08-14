using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class Re611SelectionViewModel : ObservableObject
{
    private const int OrderCodeLength = 18;
    private const string DefaultOrderCode = "REF611HCAAAA2AN11G";
    private const int FixedBinaryOutputCount = 6;
    private static readonly IReadOnlySet<string> RemovedLanguageCodes =
        new HashSet<string>(["Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"], StringComparer.OrdinalIgnoreCase);
    private readonly Re611RuleCatalog _catalog = new Re611RuleLoader().Load();
    private readonly Re611FunctionCatalogService _functionCatalog = new();
    private readonly Dictionary<string, string> _preferredCodes = new(StringComparer.OrdinalIgnoreCase);
    private Re611RuleSet? _selectedRuleSet;
    private string _selectedDeviceFilter = "";
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private string _orderCode = "";
    private string _status = "";
    private string _functionSearchText = "";
    private string _functionRecommendationStatus = "输入 IEC 61850、ANSI Code 或保护功能名称，推荐可覆盖这些功能的标准配置。";
    private bool _isValid;
    private bool _isRefreshing;

    public Re611SelectionViewModel()
    {
        RuleSets = new ObservableCollection<Re611RuleSet>(_catalog.RuleSets);
        DeviceFilters = new ObservableCollection<string>(
            _catalog.RuleSets.Select(ruleSet => ruleSet.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase));
        VersionOptions = [];
        Groups = [];
        OrderCodeSegments = [];
        IoSummaryItems = [];
        Messages = [];
        FunctionSuggestions = [];
        RequestedFunctions = [];
        StandardConfigurationRecommendations = [];

        CopyOrderCodeCommand = new RelayCommand(CopyOrderCode, () => !string.IsNullOrWhiteSpace(OrderCode));
        ImportOrderCodeCommand = new RelayCommand(ImportOrderCode);
        ResetCommand = new RelayCommand(Reset);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription, () => !string.IsNullOrWhiteSpace(OrderCode));
        ExpandAllCommand = new RelayCommand(() => SetAllGroupsExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllGroupsExpanded(false));
        AddFunctionSearchInputCommand = new RelayCommand(AddFunctionSearchInput, () => !string.IsNullOrWhiteSpace(FunctionSearchText));
        ClearFunctionRecommendationCommand = new RelayCommand(ClearFunctionRecommendation, () => RequestedFunctions.Count > 0);

        SelectedDeviceFilter = DeviceFilters.FirstOrDefault(device => DefaultOrderCode.StartsWith(device, StringComparison.OrdinalIgnoreCase))
                               ?? DeviceFilters.FirstOrDefault()
                               ?? "";
        SelectedRuleSet = RuleSets.FirstOrDefault(ruleSet => ruleSet.DeviceId.Equals(SelectedDeviceFilter, StringComparison.OrdinalIgnoreCase))
                          ?? RuleSets.FirstOrDefault();
        ApplyOrderCode(DefaultOrderCode);
    }

    public ObservableCollection<Re611RuleSet> RuleSets { get; }
    public ObservableCollection<string> DeviceFilters { get; }
    public ObservableCollection<Re611VersionRule> VersionOptions { get; }
    public ObservableCollection<Re611GroupViewModel> Groups { get; }
    public ObservableCollection<Re611OrderCodeSegmentViewModel> OrderCodeSegments { get; }
    public ObservableCollection<IoSummaryItemViewModel> IoSummaryItems { get; }
    public ObservableCollection<ValidationMessageViewModel> Messages { get; }
    public ObservableCollection<Re611FunctionSuggestionViewModel> FunctionSuggestions { get; }
    public ObservableCollection<Re611RequestedFunctionViewModel> RequestedFunctions { get; }
    public ObservableCollection<Re611StandardConfigurationRecommendationViewModel> StandardConfigurationRecommendations { get; }
    public RelayCommand CopyOrderCodeCommand { get; }
    public RelayCommand ImportOrderCodeCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public RelayCommand AddFunctionSearchInputCommand { get; }
    public RelayCommand ClearFunctionRecommendationCommand { get; }

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
                RefreshStaticText();
                RefreshStatus();
                ValidateCurrentSelection();
                if (RequestedFunctions.Count == 0)
                {
                    FunctionRecommendationStatus = DefaultFunctionRecommendationStatus();
                }

                RefreshFunctionSuggestions();
                RefreshStandardConfigurationRecommendations();
            }
        }
    }

    public string SelectedDeviceFilter
    {
        get => _selectedDeviceFilter;
        set
        {
            if (!SetProperty(ref _selectedDeviceFilter, value ?? ""))
            {
                return;
            }

            if (SelectedRuleSet is null ||
                !SelectedRuleSet.DeviceId.Equals(_selectedDeviceFilter, StringComparison.OrdinalIgnoreCase))
            {
                SelectedRuleSet = RuleSets.FirstOrDefault(ruleSet =>
                                      ruleSet.DeviceId.Equals(_selectedDeviceFilter, StringComparison.OrdinalIgnoreCase))
                                  ?? RuleSets.FirstOrDefault();
            }
        }
    }

    public Re611RuleSet? SelectedRuleSet
    {
        get => _selectedRuleSet;
        set
        {
            if (!SetProperty(ref _selectedRuleSet, value) || value is null)
            {
                return;
            }

            if (!value.DeviceId.Equals(SelectedDeviceFilter, StringComparison.OrdinalIgnoreCase))
            {
                _selectedDeviceFilter = value.DeviceId;
                OnPropertyChanged(nameof(SelectedDeviceFilter));
            }

            LoadRuleSet(value);
            ApplyOrderCode(DefaultOrderCodeFor(value));
            RefreshStaticText();
            ClearFunctionRecommendation();
        }
    }

    public Re611VersionRule? SelectedVersion
    {
        get
        {
            var versionCode = CurrentVersionCode();
            return VersionOptions.FirstOrDefault(version => version.Code.Equals(versionCode, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (value is null)
            {
                return;
            }

            var versionGroup = VersionGroupName();
            if (string.IsNullOrWhiteSpace(versionGroup))
            {
                return;
            }

            _preferredCodes[versionGroup] = value.Code;
            RefreshSelection();
        }
    }

    public string PageTitle => IsEnglish ? "RE_611 Configurator" : "RE_611 选型";
    public string SourceSummary => IsEnglish
        ? $"Rule source: {SelectedRuleSet?.FileName ?? ""}"
        : $"规则来源：{SelectedRuleSet?.FileName ?? ""}";
    public string DeviceText => IsEnglish ? "Device" : "装置";
    public string VersionText => IsEnglish ? "Product version" : "产品版本";
    public string ImportText => IsEnglish ? "Import" : "导入";
    public string CopyText => IsEnglish ? "Copy" : "复制";
    public string ResetText => IsEnglish ? "Reset" : "重置";
    public string DeviceDescriptionText => IsEnglish ? "Device description" : "装置描述";
    public string FunctionCatalogText => IsEnglish ? "Protection function list" : "保护功能清单";
    public string FunctionRecommendationTitle => IsEnglish ? "Standard configuration recommendation" : "标准配置推荐";
    public string FunctionRecommendationScope => SelectedRuleSet is null
        ? IsEnglish ? "No device selected" : "当前未选择装置"
        : IsEnglish
            ? $"{SelectedRuleSet.DeviceId} {SelectedVersion?.ProductVersion ?? ""} standard configuration recommendation"
            : $"{SelectedRuleSet.DeviceId} {SelectedVersion?.ProductVersion ?? ""} 标准配置推荐";
    public string FunctionSearchHint => IsEnglish ? "IEC 61850 / ANSI Code / protection function" : "IEC 61850 / ANSI Code / 保护功能";
    public string AddText => IsEnglish ? "Add" : "加入";
    public string ClearText => IsEnglish ? "Clear" : "清空";
    public string IoSummaryTitle => IsEnglish ? "I/O summary" : "I/O 摘要";
    public string ExpandAllText => IsEnglish ? "Expand" : "展开";
    public string CollapseAllText => IsEnglish ? "Collapse" : "折叠";
    public string OrderCodeTitle => IsEnglish ? "Order Code" : "订货号";
    public string SelectionTitle => IsEnglish ? "Order code selection" : "订货号选型";
    public string ValidationTitle => IsEnglish ? "Validation" : "校验";
    public string CurrentSelectionTitle => IsEnglish ? "Current selection" : "当前选择";

    public string FunctionSearchText
    {
        get => _functionSearchText;
        set
        {
            if (SetProperty(ref _functionSearchText, value))
            {
                AddFunctionSearchInputCommand.RaiseCanExecuteChanged();
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

    public string OrderCode
    {
        get => _orderCode;
        private set
        {
            if (SetProperty(ref _orderCode, value))
            {
                CopyOrderCodeCommand.RaiseCanExecuteChanged();
                ShowDeviceDescriptionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsValid
    {
        get => _isValid;
        private set => SetProperty(ref _isValid, value);
    }

    internal void HandleSelectionChanged(Re611GroupViewModel changedGroup)
    {
        if (_isRefreshing)
        {
            return;
        }

        if (changedGroup.SelectedOption is not null)
        {
            _preferredCodes[changedGroup.Rule.GroupName] = changedGroup.SelectedOption.Code;
        }

        RefreshSelection();
    }

    internal int CodeLengthForGroup(string groupName)
    {
        if (SelectedRuleSet is null)
        {
            return 1;
        }

        return Math.Max(1, SelectedRuleSet.Options
            .Where(option => option.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            .Select(option => option.Code.Length)
            .DefaultIfEmpty(1)
            .Max());
    }

    private void LoadRuleSet(Re611RuleSet ruleSet)
    {
        VersionOptions.Clear();
        foreach (var version in ruleSet.Versions.OrderBy(version => version.SortOrder))
        {
            VersionOptions.Add(version);
        }

        Groups.Clear();
        foreach (var group in ruleSet.Groups.OrderBy(group => group.SortOrder))
        {
            Groups.Add(new Re611GroupViewModel(this, group));
        }

        OnPropertyChanged(nameof(SelectedVersion));
    }

    private void Reset()
    {
        if (SelectedRuleSet is null)
        {
            return;
        }

        ApplyOrderCode(DefaultOrderCodeFor(SelectedRuleSet));
    }

    private void SetAllGroupsExpanded(bool isExpanded)
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = isExpanded;
        }
    }

    private void ImportOrderCode()
    {
        var window = new CombinationCodeImportWindow(
            IsEnglish ? "Import RE_611 order code" : "导入 RE_611 订货号",
            IsEnglish
                ? "Enter an 18-character RE_611 order code. Example: REF611HBAAAA1AN11G."
                : "输入 18 位 RE_611 订货号。例如：REF611HBAAAA1AN11G。",
            IsEnglish ? "Import" : "导入",
            SelectedRuleSet is null ? DefaultOrderCode : DefaultOrderCodeFor(SelectedRuleSet))
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        var code = NormalizeOrderCode(window.CombinationCode);
        if (code.Length != OrderCodeLength)
        {
            MessageBox.Show(
                IsEnglish ? "RE_611 order code must contain 18 characters." : "RE_611 订货号必须为 18 位。",
                IsEnglish ? "Import RE_611 order code" : "导入 RE_611 订货号",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ApplyOrderCode(code);
            return;
        }

        ApplyOrderCode(code);
    }

    private void ApplyOrderCode(string value)
    {
        var code = NormalizeOrderCode(value);
        if (code.Length != OrderCodeLength)
        {
            ReplaceMessages([new ValidationMessageViewModel(
                IsEnglish ? "RE_611 order code must contain 18 characters." : "RE_611 订货号必须为 18 位。",
                [],
                isSuccess: false)]);
            IsValid = false;
            RefreshStatus();
            return;
        }

        var targetRuleSet = RuleSets.FirstOrDefault(ruleSet =>
            code.StartsWith(ruleSet.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (targetRuleSet is not null && !ReferenceEquals(targetRuleSet, SelectedRuleSet))
        {
            _selectedRuleSet = targetRuleSet;
            OnPropertyChanged(nameof(SelectedRuleSet));
            _selectedDeviceFilter = targetRuleSet.DeviceId;
            OnPropertyChanged(nameof(SelectedDeviceFilter));
            LoadRuleSet(targetRuleSet);
        }

        if (SelectedRuleSet is null)
        {
            return;
        }

        _preferredCodes.Clear();
        var index = 0;
        foreach (var group in Groups)
        {
            var length = group.CodeLength;
            if (index + length > code.Length)
            {
                break;
            }

            _preferredCodes[group.Rule.GroupName] = code.Substring(index, length);
            index += length;
        }

        RefreshSelection(preservePreferredCodes: true);
    }

    private void RefreshSelection(bool preservePreferredCodes = false)
    {
        if (SelectedRuleSet is null)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            for (var pass = 0; pass < 20; pass++)
            {
                var changed = false;
                foreach (var group in Groups)
                {
                    var preferredCode = _preferredCodes.TryGetValue(group.Rule.GroupName, out var preferred)
                        ? preferred
                        : group.SelectedOption?.Code;
                    var options = AvailableOptions(group.Rule.GroupName).ToList();
                    changed |= group.ReplaceOptions(options);
                    changed |= group.RefreshAvailability(option => IsCandidateAllowed(group.Rule.GroupName, option.Code));
                    changed |= group.SelectPreferredAvailable(preferredCode, preservePreferredCodes);
                }

                if (!changed)
                {
                    break;
                }
            }
        }
        finally
        {
            _isRefreshing = false;
        }

        foreach (var group in Groups.Where(group => group.SelectedOption is not null))
        {
            _preferredCodes[group.Rule.GroupName] = group.SelectedOption!.Code;
        }

        OrderCode = BuildOrderCode();
        RefreshSegments();
        RefreshIoSummary();
        ValidateCurrentSelection();
        RefreshStatus();
        RefreshFunctionSuggestions();
        RefreshStandardConfigurationRecommendations();
        OnPropertyChanged(nameof(SelectedVersion));
        OnPropertyChanged(nameof(FunctionRecommendationScope));
    }

    private IEnumerable<Re611OptionRule> AvailableOptions(string groupName)
    {
        if (SelectedRuleSet is null)
        {
            return [];
        }

        var selectedVersion = groupName.Equals("Versions", StringComparison.OrdinalIgnoreCase)
            ? ""
            : CurrentVersionCode();

        return SelectedRuleSet.Options
            .Where(option => option.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            .Where(option => groupName.Equals("Versions", StringComparison.OrdinalIgnoreCase) ||
                             option.AppliesToVersion(selectedVersion))
            .Where(option => !IsRemovedLanguageOption(groupName, option.Code))
            .GroupBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(option => option.SortOrder).First())
            .OrderBy(option => option.SortOrder)
            .ToList();
    }

    private static string DefaultOrderCodeFor(Re611RuleSet ruleSet) =>
        ruleSet.DeviceId.Equals("REF611", StringComparison.OrdinalIgnoreCase)
            ? DefaultOrderCode
            : ruleSet.DefaultOrderCode;

    private static bool IsRemovedLanguageOption(string groupName, string code) =>
        groupName.Equals("Languages", StringComparison.OrdinalIgnoreCase) &&
        RemovedLanguageCodes.Contains(code);

    private bool IsCandidateAllowed(string groupName, string candidateCode)
    {
        if (SelectedRuleSet is null)
        {
            return true;
        }

        if (groupName.Equals("Standards", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var selectedCodes = SelectedCodes();
        selectedCodes[groupName] = candidateCode;
        var versionCode = groupName.Equals("Versions", StringComparison.OrdinalIgnoreCase)
            ? candidateCode
            : CurrentVersionCode(selectedCodes);

        return ValidationCategoriesPass(selectedCodes, versionCode, skipIncomplete: true);
    }

    private void ValidateCurrentSelection()
    {
        var messages = new List<ValidationMessageViewModel>();
        ClearValidationErrors();
        if (OrderCode.Length != OrderCodeLength)
        {
            messages.Add(new ValidationMessageViewModel(
                IsEnglish ? "The current RE_611 order code is incomplete." : "当前 RE_611 订货号不完整。",
                [],
                isSuccess: false));
        }

        foreach (var group in Groups.Where(group => group.SelectedOption is null))
        {
            messages.Add(new ValidationMessageViewModel(
                IsEnglish
                    ? $"{group.Title} has no valid option for the selected version."
                    : $"{group.Title} 在当前版本下无有效选项。",
                [],
                isSuccess: false));
        }

        var selectedCodes = SelectedCodes();
        var versionCode = CurrentVersionCode(selectedCodes);
        AddCategoryValidationMessage(
            messages,
            "FunctionalApplication",
            FunctionalApplicationValue(selectedCodes),
            versionCode,
            ["FunctionalApps", "Options_1", "Options_2"],
            IsEnglish ? "Standard configuration and option combination" : "标准配置与选项组合");
        AddCategoryValidationMessage(
            messages,
            "Communication",
            CommunicationValue(selectedCodes),
            versionCode,
            ["Mountings", "CommEthernets", "CommProtocols"],
            IsEnglish ? "Communication combination" : "通信组合");
        AddCategoryValidationMessage(
            messages,
            "Language",
            LanguageValue(selectedCodes),
            versionCode,
            ["Standards", "Languages"],
            IsEnglish ? "Standard and language combination" : "标准与语言组合");

        RefreshGroupValidationState();
        IsValid = messages.Count == 0;
        ReplaceMessages(IsValid
            ? [new ValidationMessageViewModel(IsEnglish ? "Offline validation passed" : "离线校验通过", [], isSuccess: true)]
            : messages);
    }

    private void ClearValidationErrors()
    {
        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                option.SetError(false);
            }
        }
    }

    private void RefreshGroupValidationState()
    {
        foreach (var group in Groups)
        {
            group.RefreshValidationState();
        }
    }

    private void AddCategoryValidationMessage(
        ICollection<ValidationMessageViewModel> messages,
        string category,
        string? value,
        string versionCode,
        IReadOnlyCollection<string> targetGroupNames,
        string displayName)
    {
        if (CategoryPasses(category, value, versionCode, skipIncomplete: false))
        {
            return;
        }

        foreach (var groupName in targetGroupNames)
        {
            var group = Groups.FirstOrDefault(item =>
                item.Rule.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            group?.SelectedOption?.SetError(true);
        }

        var relatedGroups = targetGroupNames
            .Select(groupName => Groups.FirstOrDefault(item =>
                item.Rule.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
            .Where(group => group?.SelectedOption is not null)
            .Cast<Re611GroupViewModel>()
            .ToList();
        var selectedText = string.Join(IsEnglish ? "; " : "；", relatedGroups
            .Select(group => $"{group.Title}={group.SelectedCode}"));
        var targets = relatedGroups
            .Select(group => new ValidationMessageTargetViewModel(group.Rule.GroupName, group.SelectedCode, group.Title))
            .ToList();

        messages.Add(new ValidationMessageViewModel(
            BuildCategoryValidationText(category, value, versionCode, displayName, selectedText),
            targets,
            isSuccess: false));
    }

    private string BuildCategoryValidationText(
        string category,
        string? value,
        string versionCode,
        string displayName,
        string selectedText)
    {
        var currentValue = string.IsNullOrWhiteSpace(value)
            ? IsEnglish ? "incomplete" : "未完整"
            : value;
        var versionText = VersionOptions
            .FirstOrDefault(version => version.Code.Equals(versionCode, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? versionCode;
        var allowedPatterns = AllowedPatternsText(category, versionCode);

        return IsEnglish
            ? $"{displayName}: value {currentValue} is invalid. Reason: the related selection is not included in the allowed patterns for product version {versionText}. Related selections: {selectedText}. Allowed patterns: {allowedPatterns}."
            : $"{displayName}：当前值 {currentValue} 无效。原因：相关选择不在产品版本 {versionText} 的允许模式中。相关选择：{selectedText}。允许模式：{allowedPatterns}。";
    }

    private string AllowedPatternsText(string category, string versionCode)
    {
        if (SelectedRuleSet is null)
        {
            return IsEnglish ? "not configured" : "未配置";
        }

        var patterns = SelectedRuleSet.ValidationRules
            .Where(rule => rule.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                           rule.AppliesToVersion(versionCode))
            .Select(rule => rule.Pattern)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return patterns.Count == 0
            ? IsEnglish ? "not configured" : "未配置"
            : string.Join(IsEnglish ? ", " : "，", patterns);
    }

    private bool ValidationCategoriesPass(
        IReadOnlyDictionary<string, string> selectedCodes,
        string versionCode,
        bool skipIncomplete)
    {
        return CategoryPasses("FunctionalApplication", FunctionalApplicationValue(selectedCodes), versionCode, skipIncomplete) &&
               CategoryPasses("Communication", CommunicationValue(selectedCodes), versionCode, skipIncomplete) &&
               CategoryPasses("Language", LanguageValue(selectedCodes), versionCode, skipIncomplete);
    }

    private bool CategoryPasses(string category, string? value, string versionCode, bool skipIncomplete)
    {
        if (SelectedRuleSet is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return skipIncomplete;
        }

        var rules = SelectedRuleSet.ValidationRules
            .Where(rule => rule.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                           rule.AppliesToVersion(versionCode))
            .ToList();
        return rules.Count == 0 || rules.Any(rule => PatternMatches(rule.Pattern, value));
    }

    private static bool PatternMatches(string pattern, string value)
    {
        if (pattern.Length != value.Length)
        {
            return false;
        }

        for (var index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] != '#' &&
                !char.ToUpperInvariant(pattern[index]).Equals(char.ToUpperInvariant(value[index])))
            {
                return false;
            }
        }

        return true;
    }

    private static string? FunctionalApplicationValue(IReadOnlyDictionary<string, string> selectedCodes)
    {
        var functional = ValueAt(selectedCodes, "FunctionalApps");
        var option1 = ValueAt(selectedCodes, "Options_1");
        var option2 = ValueAt(selectedCodes, "Options_2");
        return functional.Length == 2 && option1.Length == 1 && option2.Length == 1
            ? functional + option1 + option2
            : null;
    }

    private static string? CommunicationValue(IReadOnlyDictionary<string, string> selectedCodes)
    {
        var mounting = ValueAt(selectedCodes, "Mountings");
        var ethernet = ValueAt(selectedCodes, "CommEthernets");
        var protocol = ValueAt(selectedCodes, "CommProtocols");
        return mounting.Length == 1 && ethernet.Length == 1 && protocol.Length == 1
            ? mounting + ethernet + protocol
            : null;
    }

    private static string? LanguageValue(IReadOnlyDictionary<string, string> selectedCodes)
    {
        var standard = ValueAt(selectedCodes, "Standards");
        var language = ValueAt(selectedCodes, "Languages");
        return standard.Length == 1 && language.Length == 1
            ? standard + language
            : null;
    }

    private Dictionary<string, string> SelectedCodes()
    {
        return Groups.ToDictionary(
            group => group.Rule.GroupName,
            group => group.SelectedOption?.Code ?? "",
            StringComparer.OrdinalIgnoreCase);
    }

    private string CurrentVersionCode(IReadOnlyDictionary<string, string>? selectedCodes = null)
    {
        var versionGroup = VersionGroupName();
        if (selectedCodes is not null && selectedCodes.TryGetValue(versionGroup, out var selectedVersion) &&
            !string.IsNullOrWhiteSpace(selectedVersion))
        {
            return selectedVersion;
        }

        if (_preferredCodes.TryGetValue(versionGroup, out var preferredVersion) &&
            !string.IsNullOrWhiteSpace(preferredVersion))
        {
            return preferredVersion;
        }

        var selected = Groups.FirstOrDefault(group => group.Rule.GroupName.Equals("Versions", StringComparison.OrdinalIgnoreCase))
            ?.SelectedOption
            ?.Code;
        return !string.IsNullOrWhiteSpace(selected)
            ? selected
            : SelectedRuleSet?.Versions.LastOrDefault()?.Code ?? "";
    }

    private string VersionGroupName() =>
        SelectedRuleSet?.Groups.FirstOrDefault(group => group.GroupName.Equals("Versions", StringComparison.OrdinalIgnoreCase))?.GroupName ?? "";

    private static string ValueAt(IReadOnlyDictionary<string, string> selectedCodes, string groupName) =>
        selectedCodes.TryGetValue(groupName, out var value) ? value : "";

    private string BuildOrderCode() => string.Concat(Groups.Select(group => group.SelectedOption?.Code ?? ""));

    private void RefreshSegments()
    {
        OrderCodeSegments.Clear();
        foreach (var group in Groups)
        {
            OrderCodeSegments.Add(new Re611OrderCodeSegmentViewModel(
                group.Rule.Location,
                group.SelectedOption?.Code ?? "",
                group.Title,
                group.SelectedDescription));
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
            yield return new IoSummaryItemViewModel(
                IsEnglish ? "Communication module" : "通讯模块",
                string.Join(IsEnglish ? "; " : "；", communication));
        }

        var selectedDescriptions = Groups
            .Where(IsIoCountSourceGroup)
            .Select(group => group.SelectedOption)
            .Where(option => option is not null)
            .Select(option => option!.DescriptionSource)
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .ToList();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["BO"] = FixedBinaryOutputCount
        };

        foreach (var key in new[] { "CT", "VT", "BI", "BO", "HSO", "RTD", "mA" })
        {
            var value = selectedDescriptions.Sum(description => GetIoCount(description, key));
            if (counts.TryGetValue(key, out var baseValue))
            {
                value += baseValue;
            }

            if (value > 0)
            {
                yield return new IoSummaryItemViewModel(key, value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static bool IsCommunicationHardwareGroup(Re611GroupViewModel group) =>
        group.Rule.GroupName.Equals("CommEthernets", StringComparison.OrdinalIgnoreCase) ||
        group.Rule.GroupName.Equals("CommProtocols", StringComparison.OrdinalIgnoreCase);

    private static bool IsIoCountSourceGroup(Re611GroupViewModel group) =>
        group.Rule.GroupName.Equals("FunctionalApps", StringComparison.OrdinalIgnoreCase) ||
        group.Rule.GroupName.Equals("Options_2", StringComparison.OrdinalIgnoreCase);

    private string? BuildCommunicationSummaryPart(Re611GroupViewModel group)
    {
        var option = group.SelectedOption;
        if (option is null || IsNoneOption(option))
        {
            return null;
        }

        return IsEnglish
            ? $"{group.Title}: {option.Description}"
            : $"{group.Title}：{option.Description}";
    }

    private static bool IsNoneOption(Re611OptionViewModel option)
    {
        var text = $"{option.Code} {option.DescriptionSource} {option.Description}";
        return option.Code.Equals("N", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("None", StringComparison.OrdinalIgnoreCase) ||
                text.Contains('无'));
    }

    private static int GetIoCount(string source, string key)
    {
        source = NormalizeIoSource(source);
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

    private static string NormalizeIoSource(string source)
    {
        var normalized = Regex.Replace(
            source,
            @"(?<![A-Za-z0-9])41(\s*\+\s*4U)",
            "4I$1",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Regex.Replace(
            normalized,
            @"(?<![A-Za-z0-9])U(?![A-Za-z0-9])",
            "1U",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private void RefreshStatus()
    {
        Status = IsValid
            ? IsEnglish ? "RE_611 order code valid" : "RE_611 订货号有效"
            : IsEnglish ? "RE_611 order code needs adjustment" : "RE_611 订货号需要调整";
    }

    private void CopyOrderCode()
    {
        if (string.IsNullOrWhiteSpace(OrderCode))
        {
            return;
        }

        ClipboardService.TrySetText(OrderCode, "RE_611", IsEnglish);
        Status = IsEnglish ? "RE_611 order code copied." : "RE_611 订货号已复制。";
    }

    private void RefreshFunctionSuggestions()
    {
        FunctionSuggestions.Clear();
        if (SelectedRuleSet is not null && !string.IsNullOrWhiteSpace(FunctionSearchText))
        {
            var versionCode = CurrentVersionCode();
            foreach (var function in _functionCatalog.Search(SelectedRuleSet.DeviceId, versionCode, FunctionSearchText, 10)
                         .Where(function => RequestedFunctions.All(requested =>
                             !Re611FunctionCatalogService.FunctionKey(requested.Function)
                                 .Equals(Re611FunctionCatalogService.FunctionKey(function), StringComparison.OrdinalIgnoreCase))))
            {
                FunctionSuggestions.Add(new Re611FunctionSuggestionViewModel(function));
            }
        }

        OnPropertyChanged(nameof(HasFunctionSuggestions));
    }

    private void AddFunctionSearchInput()
    {
        if (SelectedRuleSet is null)
        {
            return;
        }

        var versionCode = CurrentVersionCode();
        var inputs = Re611FunctionCatalogService.SplitSearchInput(FunctionSearchText);
        if (inputs.Count == 0 && FunctionSuggestions.FirstOrDefault() is { } firstSuggestion)
        {
            AddRequestedFunction(firstSuggestion.Function);
            FunctionSearchText = "";
            return;
        }

        var unresolved = new List<string>();
        foreach (var input in inputs)
        {
            var exact = _functionCatalog.ResolveExact(SelectedRuleSet.DeviceId, versionCode, input);
            if (exact is not null)
            {
                AddRequestedFunction(exact);
                continue;
            }

            var candidates = _functionCatalog.Search(SelectedRuleSet.DeviceId, versionCode, input, 3);
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

    public void AddRequestedFunction(Re611FunctionEntry function)
    {
        if (SelectedRuleSet is null ||
            !function.DeviceId.Equals(SelectedRuleSet.DeviceId, StringComparison.OrdinalIgnoreCase) ||
            !function.ProductVersion.Equals(Re611FunctionCatalogService.VersionCodeToProductVersion(CurrentVersionCode()), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = Re611FunctionCatalogService.FunctionKey(function);
        if (RequestedFunctions.Any(item =>
                Re611FunctionCatalogService.FunctionKey(item.Function).Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            FunctionRecommendationStatus = IsEnglish ? "This protection function is already in the recommendation criteria." : "该保护功能已在推荐条件中。";
            return;
        }

        RequestedFunctions.Add(new Re611RequestedFunctionViewModel(function));
        OnPropertyChanged(nameof(HasRequestedFunctions));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        RefreshFunctionSuggestions();
        RefreshStandardConfigurationRecommendations();
    }

    public void RemoveRequestedFunction(Re611RequestedFunctionViewModel function)
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
        if (SelectedRuleSet is null || RequestedFunctions.Count == 0)
        {
            if (RequestedFunctions.Count == 0)
            {
                FunctionRecommendationStatus = DefaultFunctionRecommendationStatus();
            }

            OnPropertyChanged(nameof(HasStandardConfigurationRecommendations));
            return;
        }

        var functionalGroup = Groups.FirstOrDefault(group =>
            group.Rule.GroupName.Equals("FunctionalApps", StringComparison.OrdinalIgnoreCase));
        var selectableOptions = functionalGroup?.Options.ToList() ?? [];
        var selectableCodes = selectableOptions
            .Select(option => ConfigurationCodeFromFunctionalCode(option.Code))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requested = RequestedFunctions.Select(function => function.Function).ToList();
        var configCodes = requested
            .SelectMany(function => function.Configs.Keys)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var configCode in configCodes)
        {
            var covered = requested.Where(function => function.Configs.ContainsKey(configCode)).ToList();
            var missing = requested.Where(function => !function.Configs.ContainsKey(configCode)).ToList();
            if (covered.Count == 0)
            {
                continue;
            }

            StandardConfigurationRecommendations.Add(new Re611StandardConfigurationRecommendationViewModel(
                new Re611StandardConfigurationRecommendation(
                    SelectedRuleSet.DeviceId,
                    configCode,
                    BuildConfigurationDescription(configCode, selectableOptions),
                    covered,
                    missing,
                    selectableCodes.Contains(configCode)),
                IsEnglish));
        }

        var ordered = StandardConfigurationRecommendations
            .OrderBy(item => item.Recommendation.MissingFunctions.Count)
            .ThenByDescending(item => item.Recommendation.CoveredFunctions.Count)
            .ThenBy(item => item.ConfigCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        StandardConfigurationRecommendations.Clear();
        foreach (var item in ordered)
        {
            StandardConfigurationRecommendations.Add(item);
        }

        FunctionRecommendationStatus = StandardConfigurationRecommendations.Count switch
        {
            0 => IsEnglish
                ? "No standard configuration for the current RE_611 device covers the entered protection functions."
                : "当前 RE_611 装置的标准配置不能覆盖已输入的保护功能。",
            _ when StandardConfigurationRecommendations.Any(item => item.IsFullMatch) => IsEnglish
                ? "A standard configuration with full coverage was found."
                : "已找到可完整覆盖的标准配置。",
            _ => IsEnglish
                ? "No single standard configuration covers all functions. The best coverage candidates are listed below."
                : "没有单个标准配置可完整覆盖，以下为覆盖度最高的配置。"
        };
        OnPropertyChanged(nameof(HasStandardConfigurationRecommendations));
    }

    public void ApplyStandardConfigurationRecommendation(Re611StandardConfigurationRecommendationViewModel recommendation)
    {
        var group = Groups.FirstOrDefault(item =>
            item.Rule.GroupName.Equals("FunctionalApps", StringComparison.OrdinalIgnoreCase));
        if (group is null || !recommendation.CanApply)
        {
            FunctionRecommendationStatus = IsEnglish
                ? "This recommendation is not available in the current product version and is shown only as a manual reference."
                : "该推荐配置不在当前产品版本可选项中，仅作为手册配置参考。";
            return;
        }

        var suffix = group.SelectedCode.Length > 1 ? group.SelectedCode[1..] : "";
        var preferredCode = recommendation.ConfigCode + suffix;
        var target = group.Options.FirstOrDefault(option =>
                         option.Code.Equals(preferredCode, StringComparison.OrdinalIgnoreCase))
                     ?? group.Options.FirstOrDefault(option =>
                         ConfigurationCodeFromFunctionalCode(option.Code).Equals(recommendation.ConfigCode, StringComparison.OrdinalIgnoreCase));

        if (target is not null && group.SelectByCode(target.Code))
        {
            FunctionRecommendationStatus = IsEnglish
                ? $"Standard configuration {recommendation.ConfigCode} applied."
                : $"已应用标准配置 {recommendation.ConfigCode}。";
        }
    }

    private string DefaultFunctionRecommendationStatus() => IsEnglish
        ? "Enter IEC 61850, ANSI Code or protection function name to recommend standard configurations that cover the selected functions."
        : "输入 IEC 61850、ANSI Code 或保护功能名称，推荐可覆盖这些功能的标准配置。";

    private string BuildConfigurationDescription(string configCode, IReadOnlyList<Re611OptionViewModel> selectableOptions)
    {
        var descriptions = selectableOptions
            .Where(option => ConfigurationCodeFromFunctionalCode(option.Code).Equals(configCode, StringComparison.OrdinalIgnoreCase))
            .Select(option => option.Description)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return descriptions.Count == 0
            ? configCode
            : string.Join(IsEnglish ? " / " : " / ", descriptions);
    }

    private static string ConfigurationCodeFromFunctionalCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? "" : code[..1].ToUpperInvariant();

    private void RefreshStaticText()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(DeviceText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ImportText));
        OnPropertyChanged(nameof(CopyText));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(DeviceDescriptionText));
        OnPropertyChanged(nameof(FunctionCatalogText));
        OnPropertyChanged(nameof(FunctionRecommendationTitle));
        OnPropertyChanged(nameof(FunctionRecommendationScope));
        OnPropertyChanged(nameof(FunctionSearchHint));
        OnPropertyChanged(nameof(AddText));
        OnPropertyChanged(nameof(ClearText));
        OnPropertyChanged(nameof(IoSummaryTitle));
        OnPropertyChanged(nameof(ExpandAllText));
        OnPropertyChanged(nameof(CollapseAllText));
        OnPropertyChanged(nameof(OrderCodeTitle));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(ValidationTitle));
        OnPropertyChanged(nameof(CurrentSelectionTitle));
        OnPropertyChanged(nameof(SelectedVersion));

        foreach (var group in Groups)
        {
            group.RefreshLanguage();
        }

        RefreshSegments();
    }

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
            IsEnglish ? "RE_611 device description" : "RE_611 装置描述",
            IsEnglish ? $"Order code: {OrderCode}" : $"订货号：{OrderCode}",
            IsEnglish ? $"Device: {SelectedRuleSet?.DeviceId ?? ""}" : $"装置：{SelectedRuleSet?.DeviceId ?? ""}",
            IsEnglish ? $"Version: {SelectedVersion?.DisplayName ?? ""}" : $"版本：{SelectedVersion?.DisplayName ?? ""}",
            IsEnglish ? $"Status: {Status}" : $"状态：{Status}",
            ""
        };

        lines.Add(IsEnglish ? "Selected options:" : "选型配置：");
        foreach (var group in Groups)
        {
            lines.Add(IsEnglish
                ? $"{group.Title}: {group.SelectedCode} ({group.SelectedDescription})"
                : $"{group.Title}：{group.SelectedCode}（{group.SelectedDescription}）");
        }

        lines.Add("");
        lines.Add(IsEnglish ? "I/O summary:" : "I/O 摘要：");
        lines.Add(IoSummaryItems.Count == 0
            ? IsEnglish ? "None" : "无"
            : string.Join(IsEnglish ? "; " : "；", IoSummaryItems.Select(item => $"{item.Name}={item.Value}")));

        if (Messages.Count > 0)
        {
            lines.Add("");
            lines.Add(IsEnglish ? "Validation messages:" : "校验提示：");
            lines.AddRange(Messages.Select(message => message.Text));
        }

        return string.Join(Environment.NewLine, lines);
    }

    internal string LocalizeGroupTitle(string groupName, string title)
    {
        if (IsEnglish)
        {
            return Re611EnglishGroupTitles.TryGetValue(groupName, out var english) ? english : title;
        }

        return Re611ChineseGroupTitles.TryGetValue(groupName, out var chinese) ? chinese : title;
    }

    internal string LocalizeOptionDescription(string description)
    {
        if (IsEnglish || string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        return Re611OptionDescriptionTranslations.TryGetValue(description, out var localized)
            ? localized
            : description;
    }

    private void ReplaceMessages(IEnumerable<ValidationMessageViewModel> messages)
    {
        Messages.Clear();
        foreach (var message in messages)
        {
            Messages.Add(message);
        }
    }

    private static string NormalizeOrderCode(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static readonly IReadOnlyDictionary<string, string> Re611EnglishGroupTitles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MainApps"] = "Product series",
            ["Mountings"] = "Mounting",
            ["Standards"] = "Standard",
            ["FunctionalApps"] = "Standard configuration / analog inputs",
            ["CommEthernets"] = "Communication module",
            ["CommProtocols"] = "Communication protocol",
            ["Languages"] = "Language",
            ["Options_1"] = "Option 1",
            ["Options_2"] = "Option 2",
            ["PowerSupplies"] = "Power supply",
            ["Versions"] = "Product version"
        };

    private static readonly IReadOnlyDictionary<string, string> Re611ChineseGroupTitles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MainApps"] = "产品系列",
            ["Mountings"] = "安装方式",
            ["Standards"] = "标准",
            ["FunctionalApps"] = "标准配置 / 模拟量输入",
            ["CommEthernets"] = "通信模块",
            ["CommProtocols"] = "通信协议",
            ["Languages"] = "语言",
            ["Options_1"] = "选项 1",
            ["Options_2"] = "选项 2",
            ["PowerSupplies"] = "电源",
            ["Versions"] = "产品版本"
        };

    private static readonly IReadOnlyDictionary<string, string> Re611OptionDescriptionTranslations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["611 series Feeder protection and control"] = "611 系列馈线保护与控制",
            ["611 series Motor protection and control"] = "611 系列电机保护与控制",
            ["611 series Busbar and multipurpose differential protection and control"] = "611 系列母线及多功能差动保护与控制",
            ["611 Series Voltage protection and control"] = "611 系列电压保护与控制",
            ["Complete Relay"] = "完整继电器",
            ["Complete Relay with test switch installed and wired in 19\" cover plate"] = "完整继电器，带已安装和接线的 19 英寸盖板试验开关",
            ["Complete Relay with test switch installed and wired for CombiFlex rack mounting"] = "完整继电器，带适用于 CombiFlex 机架安装的已接线试验开关",
            ["IEC"] = "IEC",
            ["CN"] = "CN",
            ["High impedance differential [4I + 4BI (Io 1/5A)]"] = "高阻差动保护 [4I + 4BI（Io 1/5A）]",
            ["High impedance differential [4I + 4BI (Io 0.2/1A)]"] = "高阻差动保护 [4I + 4BI（Io 0.2/1A）]",
            ["Non-directional O/C and directional E/F [4I + U + 3BI (Io 1/5A)]"] = "无方向过流和方向接地保护 [4I + U + 3BI（Io 1/5A）]",
            ["Non-directional O/C and directional E/F [4I + U + 4BI (Io 0.2/1A)]"] = "无方向过流和方向接地保护 [4I + U + 4BI（Io 0.2/1A）]",
            ["Non-directional O/C and non-directional E/F [4I + 4BI (Io 1/5A)]"] = "无方向过流和无方向接地保护 [4I + 4BI（Io 1/5A）]",
            ["Non-directional O/C and non-directional E/F [4I + 4BI (Io 0.2/1A)]"] = "无方向过流和无方向接地保护 [4I + 4BI（Io 0.2/1A）]",
            ["Directional O/C and directional E/F [4I + 4U + 8 BI (Io 1/5A)]"] = "方向过流和方向接地保护 [4I + 4U + 8BI（Io 1/5A）]",
            ["Directional O/C and directional E/F [41 + 4U + 8 BI (Io 0.2/1A)]"] = "方向过流和方向接地保护 [4I + 4U + 8BI（Io 0.2/1A）]",
            ["Motor protection [4I + 4BI (Io 1/5A)]"] = "电机保护 [4I + 4BI（Io 1/5A）]",
            ["Motor protection [4I + 4BI (Io 0.2/1A)]"] = "电机保护 [4I + 4BI（Io 0.2/1A）]",
            ["Voltage & Frequency protection [5U + 4BI]"] = "电压及频率保护 [5U + 4BI]",
            ["Ethernet 100Base FX (LC)"] = "以太网 100Base-FX（LC）",
            ["Ethernet 100Base TX (RJ45)"] = "以太网 100Base-TX（RJ45）",
            ["RS485 (including IRIG-B)"] = "RS485（含 IRIG-B）",
            ["Ethernet 100Base TX (3xRJ45)"] = "以太网 100Base-TX（3xRJ45）",
            ["Ethernet 100Base TX (3xRJ45) with HSR/PRP"] = "以太网 100Base-TX（3xRJ45），支持 HSR/PRP",
            ["None"] = "无",
            ["IEC 61850"] = "IEC 61850",
            ["Modbus"] = "Modbus",
            ["IEC 61850+Modbus"] = "IEC 61850 + Modbus",
            ["IEC 61850 + Modbus"] = "IEC 61850 + Modbus",
            ["English"] = "英文",
            ["English and Chinese"] = "英文和中文",
            ["English and German"] = "英文和德文",
            ["English and Swedish"] = "英文和瑞典文",
            ["English and Spanish"] = "英文和西班牙文",
            ["English and Russian"] = "英文和俄文",
            ["English and Polish"] = "英文和波兰文",
            ["English and Portuguese (Brasilian)"] = "英文和葡萄牙文（巴西）",
            ["English and Italian"] = "英文和意大利文",
            ["English and French"] = "英文和法文",
            ["English and Czech"] = "英文和捷克文",
            ["English and Turkish"] = "英文和土耳其文",
            ["English and Croatia"] = "英文和克罗地亚文",
            ["English and Ukrainian"] = "英文和乌克兰文",
            ["English and Hungarian"] = "英文和匈牙利文",
            ["Reclosing"] = "重合闸",
            ["Optional I/O [BIO 6 BI + 3 BO]"] = "可选 I/O [BIO 6BI + 3BO]",
            ["48-250 Vdc; 100-240 Vac"] = "48-250 VDC；100-240 VAC",
            ["24-60 Vdc"] = "24-60 VDC",
            ["Product version - 1.0"] = "产品版本 1.0",
            ["Product version - 2.0"] = "产品版本 2.0",
            ["Product Version 2.0"] = "产品版本 2.0"
        };
}

public sealed class Re611GroupViewModel : ObservableObject
{
    private readonly Re611SelectionViewModel _owner;
    private Re611OptionViewModel? _selectedOption;
    private bool _isExpanded = true;
    private int _errorCount;

    public Re611GroupViewModel(Re611SelectionViewModel owner, Re611GroupRule rule)
    {
        _owner = owner;
        Rule = rule;
        CodeLength = owner.CodeLengthForGroup(rule.GroupName);
        Options = [];
    }

    public Re611GroupRule Rule { get; }
    public int CodeLength { get; }
    public string Title => _owner.LocalizeGroupTitle(Rule.GroupName, Rule.Title);
    public string Position => Rule.Location;
    public ObservableCollection<Re611OptionViewModel> Options { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public Re611OptionViewModel? SelectedOption
    {
        get => _selectedOption;
        set
        {
            var previous = _selectedOption;
            if (SetProperty(ref _selectedOption, value))
            {
                previous?.RefreshSelectionState();
                _selectedOption?.RefreshSelectionState();
                OnPropertyChanged(nameof(SelectedCode));
                OnPropertyChanged(nameof(SelectedDescription));
                OnPropertyChanged(nameof(SelectedDisplayText));
                _owner.HandleSelectionChanged(this);
            }
        }
    }

    public string SelectedCode
    {
        get => SelectedOption?.Code ?? "";
        set
        {
            var option = Options.FirstOrDefault(option =>
                option.Code.Equals(value ?? "", StringComparison.OrdinalIgnoreCase));
            if (option is not null && option.IsAvailable)
            {
                SelectedOption = option;
            }
        }
    }

    public string SelectedDescription => SelectedOption?.Description ?? "";
    public string SelectedDisplayText => string.IsNullOrWhiteSpace(SelectedCode)
        ? ""
        : $"{SelectedCode}: {SelectedDescription}";
    public bool HasError => ErrorCount > 0;
    public string ErrorSummary => _owner.IsEnglish ? $"{ErrorCount} issue(s)" : $"需处理 {ErrorCount}";

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

    internal void RefreshValidationState() => ErrorCount = Options.Count(option => option.HasError);

    public bool SelectByCode(string code)
    {
        var option = Options.FirstOrDefault(option =>
            option.Code.Equals(code ?? "", StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            return false;
        }

        if (!option.IsAvailable)
        {
            return false;
        }

        SelectedOption = option;
        return true;
    }

    public bool ReplaceOptions(IReadOnlyList<Re611OptionRule> options)
    {
        var oldCodes = Options.Select(option => option.Code).ToList();
        var newCodes = options.Select(option => option.Code).ToList();
        var collectionChanged = oldCodes.Count != newCodes.Count ||
                                oldCodes.Where((code, index) => !code.Equals(newCodes[index], StringComparison.OrdinalIgnoreCase)).Any();

        if (collectionChanged)
        {
            Options.Clear();
            foreach (var option in options)
            {
                Options.Add(new Re611OptionViewModel(_owner, this, option));
            }
        }

        return collectionChanged;
    }

    public bool RefreshAvailability(Func<Re611OptionViewModel, bool> isAvailable)
    {
        var changed = false;
        foreach (var option in Options)
        {
            changed |= option.SetAvailability(isAvailable(option));
        }

        return changed;
    }

    public bool SelectPreferredAvailable(string? preferredCode, bool preserveUnavailablePreferred)
    {
        var selected = Options.FirstOrDefault(option =>
                           !string.IsNullOrWhiteSpace(preferredCode) &&
                           option.Code.Equals(preferredCode, StringComparison.OrdinalIgnoreCase) &&
                           (option.IsAvailable || preserveUnavailablePreferred))
                       ?? (SelectedOption?.IsAvailable == true && Options.Contains(SelectedOption)
                           ? SelectedOption
                           : null)
                       ?? Options.FirstOrDefault(option => option.IsAvailable)
                       ?? Options.FirstOrDefault();

        var previous = _selectedOption;
        var selectedChanged = !ReferenceEquals(previous, selected);
        _selectedOption = selected;
        if (selectedChanged)
        {
            previous?.RefreshSelectionState();
            _selectedOption?.RefreshSelectionState();
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(SelectedCode));
            OnPropertyChanged(nameof(SelectedDescription));
            OnPropertyChanged(nameof(SelectedDisplayText));
        }

        return selectedChanged;
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ErrorSummary));
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(SelectedDisplayText));
        foreach (var option in Options)
        {
            option.RefreshLanguage();
        }
    }
}

public sealed class Re611OptionViewModel : ObservableObject
{
    private readonly Re611SelectionViewModel _owner;
    private readonly Re611GroupViewModel _group;
    private bool _isAvailable = true;
    private bool _hasError;

    public Re611OptionViewModel(Re611SelectionViewModel owner, Re611GroupViewModel group, Re611OptionRule rule)
    {
        _owner = owner;
        _group = group;
        Rule = rule;
    }

    internal Re611OptionRule Rule { get; }
    public string Code => Rule.Code;
    public string DescriptionSource => Rule.Description;
    public string Description => _owner.LocalizeOptionDescription(Rule.Description);
    public string DisplayText => $"{Rule.Code} - {Description}";
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

    public bool IsSelected
    {
        get => ReferenceEquals(_group.SelectedOption, this);
        set
        {
            if (value && IsAvailable)
            {
                _group.SelectedOption = this;
            }

            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public override string ToString() => Code;

    internal void RefreshSelectionState() => OnPropertyChanged(nameof(IsSelected));

    internal bool SetAvailability(bool value) => SetProperty(ref _isAvailable, value, nameof(IsAvailable));

    internal bool SetError(bool value) => SetProperty(ref _hasError, value, nameof(HasError));

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed class Re611OrderCodeSegmentViewModel(string position, string code, string title, string description)
{
    public string Position { get; } = position;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public int Width => Code.Length > 1 ? Math.Max(48, Code.Length * 16 + 18) : 36;
}

public sealed class Re611FunctionSuggestionViewModel(Re611FunctionEntry function)
{
    public Re611FunctionEntry Function { get; } = function;
    public string Iec61850 => Function.Iec61850;
    public string AnsiCode => Function.AnsiCode;
    public string Name => string.IsNullOrWhiteSpace(Function.ChineseName) ? Function.EnglishName : Function.ChineseName;
    public string DisplayText => $"{Function.Iec61850} / {Function.AnsiCode}：{Name}";
}

public sealed class Re611RequestedFunctionViewModel(Re611FunctionEntry function)
{
    public Re611FunctionEntry Function { get; } = function;
    public string Iec61850 => Function.Iec61850;
    public string AnsiCode => Function.AnsiCode;
    public string Name => string.IsNullOrWhiteSpace(Function.ChineseName) ? Function.EnglishName : Function.ChineseName;
    public string DisplayText => $"{Function.Iec61850} / {Function.AnsiCode}：{Name}";
}

public sealed class Re611StandardConfigurationRecommendationViewModel(
    Re611StandardConfigurationRecommendation recommendation,
    bool isEnglish)
{
    public Re611StandardConfigurationRecommendation Recommendation { get; } = recommendation;
    public string DeviceId => Recommendation.DeviceId;
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
        : isEnglish ? "Current product version does not have this standard configuration" : Recommendation.ApplyHint;
}

public sealed record Re611StandardConfigurationRecommendation(
    string DeviceId,
    string ConfigCode,
    string ConfigDescription,
    IReadOnlyList<Re611FunctionEntry> CoveredFunctions,
    IReadOnlyList<Re611FunctionEntry> MissingFunctions,
    bool CanApply)
{
    public bool IsFullMatch => MissingFunctions.Count == 0;
    public string MatchStatus => IsFullMatch
        ? $"完整覆盖 {CoveredFunctions.Count} 个功能"
        : $"覆盖 {CoveredFunctions.Count} 个，缺少 {MissingFunctions.Count} 个";
    public string CoveredSummary => string.Join("；", CoveredFunctions.Select(FunctionSummary));
    public string MissingSummary => string.Join("；", MissingFunctions.Select(FunctionSummary));
    public string ApplyHint => CanApply ? "应用配置" : "当前版本不可直接应用";

    private static string FunctionSummary(Re611FunctionEntry function) =>
        $"{function.Iec61850}/{function.AnsiCode} {function.ChineseName}";
}
