using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

// 负责 SSC600/SSC600 SW 的 18 位订货码选择、版本兼容、组合规则、在线校验及 AppPack 功能推荐。
// 选型规则和功能目录分别提供“可订组合”与“功能覆盖”证据，推荐结果不会绕过订货码离线校验。
public sealed class Ssc600SelectionViewModel : ObservableObject
{
    private readonly Ssc600RuleSet _rules = new Ssc600RuleLoader().Load();
    private readonly Ssc600FunctionCatalogService _functionCatalog = new();
    private readonly OnlineValidationService _onlineValidationService = new();
    private string _orderCode = "";
    private string _status = "";
    private string _onlineStatus = "未校验";
    private string _onlineOrderingNumber = "";
    private string _functionSearchText = "";
    private string _appRecommendationSummary = "输入 ANSI code、ABB 功能码、中文或英文功能名称后添加；系统会推荐应选择的 SSC600 应用包。";
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private bool _isValid;
    private bool _isOnlineValidationBusy;
    private bool _isOnlineValidationSuccess;
    private bool _isOnlineValidationError;

    public Ssc600SelectionViewModel()
    {
        Groups = new ObservableCollection<Ssc600GroupViewModel>(
            _rules.Groups.Select(group => new Ssc600GroupViewModel(this, group)));
        Messages = [];
        SelectedSummaryItems = [];
        VersionOptions = _rules.Versions
            .Select(version => new Ssc600VersionOptionViewModel(version.Id, $"{version.Id} / IED {version.IedVersion}"))
            .ToList();
        FunctionCatalogItems = new ObservableCollection<Ssc600FunctionCatalogItemViewModel>(
            _functionCatalog.GetFunctions().Select(function => new Ssc600FunctionCatalogItemViewModel(
                Package: function.IsBase ? "基础功能" : function.Category,
                PackageEnglish: function.IsBase ? "Base functionality" : PackageCategoryEnglish(function.Category),
                AbbCode: function.Code,
                AnsiCode: function.Ansi,
                ChineseName: function.ChineseName,
                EnglishName: function.EnglishName)));
        FunctionSuggestions = [];
        RequestedFunctions = [];
        AppRecommendations = [];

        CopyOrderCodeCommand = new RelayCommand(CopyOrderCode, () => !string.IsNullOrWhiteSpace(OrderCode));
        CopyOrderingNumberCommand = new RelayCommand(CopyOrderingNumber, () => HasOnlineOrderingNumber);
        OnlineValidateCommand = new RelayCommand(
            () => _ = ValidateOnlineAsync(),
            () => !IsOnlineValidationBusy && !string.IsNullOrWhiteSpace(OrderCode));
        ImportOrderCodeCommand = new RelayCommand(ImportOrderCode);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription, () => !string.IsNullOrWhiteSpace(OrderCode));
        AddFunctionInputCommand = new RelayCommand(AddFunctionInput, () => !string.IsNullOrWhiteSpace(FunctionSearchText));
        ClearFunctionRecommendationCommand = new RelayCommand(ClearFunctionRecommendation, () => RequestedFunctions.Count > 0);
        ResetCommand = new RelayCommand(Reset);
        ExpandAllCommand = new RelayCommand(() => SetAllGroupsExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllGroupsExpanded(false));

        Reset();
    }

    public ObservableCollection<Ssc600GroupViewModel> Groups { get; }
    public ObservableCollection<ValidationMessageViewModel> Messages { get; }
    public ObservableCollection<Ssc600SelectedSummaryItemViewModel> SelectedSummaryItems { get; }
    public IReadOnlyList<Ssc600VersionOptionViewModel> VersionOptions { get; }
    public ObservableCollection<Ssc600FunctionCatalogItemViewModel> FunctionCatalogItems { get; }
    public ObservableCollection<Ssc600FunctionSuggestionViewModel> FunctionSuggestions { get; }
    public ObservableCollection<Ssc600RequestedFunctionViewModel> RequestedFunctions { get; }
    public ObservableCollection<Ssc600AppRecommendationViewModel> AppRecommendations { get; }
    public RelayCommand CopyOrderCodeCommand { get; }
    public RelayCommand CopyOrderingNumberCommand { get; }
    public RelayCommand OnlineValidateCommand { get; }
    public RelayCommand ImportOrderCodeCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }
    public RelayCommand AddFunctionInputCommand { get; }
    public RelayCommand ClearFunctionRecommendationCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public string SourceSummary => IsEnglish
        ? "SSC600_1.5.xml; descriptions are based on SSC600 product guide 1MRS758725 G."
        : "SSC600_1.5.xml；说明来自 SSC600 产品指南 1MRS758725 G。";
    public string VersionText => IsEnglish ? "Product version" : "产品版本";
    public Ssc600VersionOptionViewModel? SelectedVersion
    {
        get
        {
            var current = CurrentVersion(SelectedByGroup());
            return VersionOptions.FirstOrDefault(version => version.Id.Equals(current, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (value is null)
            {
                return;
            }

            var group = Groups.FirstOrDefault(group => group.Name.Equals("Versions", StringComparison.OrdinalIgnoreCase));
            var option = group?.Options.FirstOrDefault(option => option.Id.Equals(value.Id, StringComparison.OrdinalIgnoreCase));
            if (option is not null && !option.IsSelected)
            {
                option.IsSelected = true;
            }
        }
    }
    public string ExpandAllText => IsEnglish ? "Expand" : "展开";
    public string CollapseAllText => IsEnglish ? "Collapse" : "折叠";
    public string OnlineValidateText => IsEnglish ? "Online check" : "在线校验";
    public string OnlineStatusTitle => IsEnglish ? "Online check" : "在线校验";
    public string OrderingNumberTitle => IsEnglish ? "Ordering number" : "订货号";
    public string CopyOrderingNumberText => IsEnglish ? "Copy ordering number" : "复制订货号";
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
                // 语言变化只重建文案、消息和推荐展示；当前组选项保持不变并通过重算重新生成派生状态。
                OnPropertyChanged(nameof(IsEnglish));
                OnPropertyChanged(nameof(SourceSummary));
                OnPropertyChanged(nameof(VersionText));
                OnPropertyChanged(nameof(SelectedVersion));
                OnPropertyChanged(nameof(ExpandAllText));
                OnPropertyChanged(nameof(CollapseAllText));
                OnPropertyChanged(nameof(OnlineValidateText));
                OnPropertyChanged(nameof(OnlineStatusTitle));
                OnPropertyChanged(nameof(OrderingNumberTitle));
                OnPropertyChanged(nameof(CopyOrderingNumberText));
                foreach (var group in Groups)
                {
                    group.RefreshLanguage();
                }

                Recalculate();
                OnlineStatus = OnlineValidationService.LocalizeMessage(OnlineStatus, IsEnglish);
                RefreshRecommendations();
                RefreshFunctionDisplay();
            }
        }
    }

    public string OrderCode
    {
        get => _orderCode;
        private set
        {
            if (SetProperty(ref _orderCode, value))
            {
                ResetOnlineValidationState();
                CopyOrderCodeCommand.RaiseCanExecuteChanged();
                OnlineValidateCommand.RaiseCanExecuteChanged();
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

    public string OnlineStatus
    {
        get => _onlineStatus;
        private set => SetProperty(ref _onlineStatus, value);
    }

    public string OnlineOrderingNumber
    {
        get => _onlineOrderingNumber;
        private set
        {
            if (SetProperty(ref _onlineOrderingNumber, value))
            {
                OnPropertyChanged(nameof(HasOnlineOrderingNumber));
                CopyOrderingNumberCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasOnlineOrderingNumber => !string.IsNullOrWhiteSpace(OnlineOrderingNumber);

    public bool IsOnlineValidationBusy
    {
        get => _isOnlineValidationBusy;
        private set
        {
            if (SetProperty(ref _isOnlineValidationBusy, value))
            {
                OnlineValidateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsOnlineValidationSuccess
    {
        get => _isOnlineValidationSuccess;
        private set => SetProperty(ref _isOnlineValidationSuccess, value);
    }

    public bool IsOnlineValidationError
    {
        get => _isOnlineValidationError;
        private set => SetProperty(ref _isOnlineValidationError, value);
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

    public string AppRecommendationSummary
    {
        get => _appRecommendationSummary;
        private set => SetProperty(ref _appRecommendationSummary, value);
    }

    public bool HasFunctionSuggestions => FunctionSuggestions.Count > 0;
    public bool HasRequestedFunctions => RequestedFunctions.Count > 0;
    public bool HasAppRecommendations => AppRecommendations.Count > 0;

    public void Reset()
    {
        // 先静默清除全部组，再按规则默认码整体回填，避免重置过程中产生多个互相矛盾的临时订货码。
        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                option.SetSelectedSilently(false);
            }
        }

        ApplyOrderCode(_rules.DefaultOrderCode);
        Recalculate();
    }

    internal void HandleSelectionChanged(Ssc600GroupViewModel changedGroup, Ssc600OptionViewModel changedOption)
    {
        if (changedOption.IsSelected)
        {
            foreach (var option in changedGroup.Options.Where(option => !ReferenceEquals(option, changedOption)))
            {
                option.SetSelectedSilently(false);
            }
        }

        Recalculate();
    }

    internal void Recalculate()
    {
        // 重算顺序固定为单选归一化、版本兼容回退、代码生成、组合校验、摘要和选项状态刷新。
        // 版本切换可能替换不再支持的选项，因此必须在生成订货码之前完成可用项收敛。
        EnsureSingleSelection();
        NormalizeUnsupportedSelections();
        OrderCode = BuildOrderCode();
        var selectedByGroup = SelectedByGroup();
        var selectedVersion = CurrentVersion(selectedByGroup);
        var messages = Validate(selectedByGroup, selectedVersion).ToList();

        IsValid = messages.Count == 0;
        Status = IsValid
            ? IsEnglish ? "SSC600 order code valid" : "SSC600 订货码有效"
            : IsEnglish ? "SSC600 order code needs adjustment" : "SSC600 订货码需要调整";

        Replace(Messages, IsValid
            ? [new ValidationMessageViewModel(IsEnglish ? "Offline validation passed" : "离线校验通过", [], isSuccess: true)]
            : messages);
        Replace(SelectedSummaryItems, BuildSelectedSummary(selectedVersion));
        UpdateOptionStates(selectedByGroup, selectedVersion);
        OnPropertyChanged(nameof(SelectedVersion));
    }

    private IReadOnlyList<string> ApplyOrderCode(string orderCode)
    {
        // 导入按各组 Location 提取位段并静默选中；无法匹配的位段回落到首个规则项并作为告警返回。
        var code = (orderCode ?? "").Trim().ToUpperInvariant();
        if (code.Length < 18)
        {
            return [IsEnglish ? "Order code is shorter than 18 characters." : "订货码长度不足 18 位"];
        }

        var notFound = new List<string>();
        foreach (var group in Groups)
        {
            var segment = SegmentForLocation(code, group.Location);
            var target = group.Options.FirstOrDefault(option => option.Id.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                notFound.Add(IsEnglish
                    ? $"Position {group.Location} {group.DisplayName}: {segment}"
                    : $"第 {group.Location} 位 {group.DisplayName}: {segment}");
                target = group.Options.FirstOrDefault();
            }

            target?.SetSelectedSilently(true);
            group.RefreshSelectedSummary();
        }

        return notFound;
    }

    private void EnsureSingleSelection()
    {
        foreach (var group in Groups)
        {
            var selected = group.Options.Where(option => option.IsSelected).ToList();
            if (selected.Count == 0)
            {
                group.Options.FirstOrDefault()?.SetSelectedSilently(true);
            }
            else if (selected.Count > 1)
            {
                foreach (var option in selected.Skip(1))
                {
                    option.SetSelectedSilently(false);
                }
            }

            group.RefreshSelectedSummary();
        }
    }

    private void NormalizeUnsupportedSelections()
    {
        // 当前选项不支持目标版本时，优先采用默认码对应项，其次采用该版本首个可用项，保证组始终可计算。
        var selectedByGroup = SelectedByGroup();
        var version = CurrentVersion(selectedByGroup);
        foreach (var group in Groups)
        {
            var selected = group.SelectedOption;
            if (selected is not null && selected.Option.SupportsVersion(version))
            {
                continue;
            }

            var preferred = PreferredOptionForGroup(group, version);
            if (preferred is null)
            {
                continue;
            }

            foreach (var option in group.Options)
            {
                option.SetSelectedSilently(ReferenceEquals(option, preferred));
            }

            group.RefreshSelectedSummary();
        }
    }

    private Ssc600OptionViewModel? PreferredOptionForGroup(Ssc600GroupViewModel group, string version)
    {
        var defaultSegment = SegmentForLocation(_rules.DefaultOrderCode, group.Location);
        return group.Options.FirstOrDefault(option =>
                   option.Id.Equals(defaultSegment, StringComparison.OrdinalIgnoreCase) &&
                   option.Option.SupportsVersion(version))
               ?? group.Options.FirstOrDefault(option => option.Option.SupportsVersion(version))
               ?? group.Options.FirstOrDefault();
    }

    private string BuildOrderCode()
    {
        var builder = new StringBuilder();
        foreach (var group in Groups.OrderBy(group => group.SortOrder))
        {
            builder.Append(group.SelectedOption?.Id ?? "");
        }

        return builder.ToString();
    }

    private Dictionary<string, Ssc600OptionViewModel> SelectedByGroup() =>
        Groups
            .Select(group => (group.Name, Option: group.SelectedOption))
            .Where(item => item.Option is not null)
            .ToDictionary(item => item.Name, item => item.Option!, StringComparer.OrdinalIgnoreCase);

    private string CurrentVersion(IReadOnlyDictionary<string, Ssc600OptionViewModel> selectedByGroup) =>
        selectedByGroup.TryGetValue("Versions", out var selectedVersion) ? selectedVersion.Id : "6G";

    private IEnumerable<ValidationMessageViewModel> Validate(
        IReadOnlyDictionary<string, Ssc600OptionViewModel> selectedByGroup,
        string version)
    {
        // 校验分两层：单项版本可用性，以及功能应用/软件位段是否命中对应版本的组合规则块。
        foreach (var group in Groups)
        {
            var selected = group.SelectedOption;
            if (selected is null)
            {
                yield return CreateValidationMessage(
                    IsEnglish ? $"{group.DisplayName} is not selected." : $"{group.DisplayName} 未选择。",
                    group.Name);
                continue;
            }

            if (!selected.Option.SupportsVersion(version))
            {
                yield return CreateValidationMessage(
                    IsEnglish
                        ? $"{group.DisplayName} / {selected.Id} is not available for product version {version}."
                        : $"{group.DisplayName} / {selected.Id} 不适用于产品版本 {version}。",
                    group.Name);
            }
        }

        var functionalApplication = Combine(selectedByGroup, "Mountings", "Languages", "Reserved2");
        if (!string.IsNullOrWhiteSpace(functionalApplication) &&
            !_rules.MatchesValidationBlock("FunctionalApplication", functionalApplication, version))
        {
            yield return CreateValidationMessage(
                IsEnglish
                    ? "The basic product, communication and power supply combination does not meet SSC600 ordering rules. SSC600 SW normally uses N for communication and power supply; SSC600 hardware requires A/B communication and 1/2 power supply."
                    : "产品类型、通信接口和电源组合不满足 SSC600 订货规则。SSC600 SW 通常需要通信接口和电源均为 N；SSC600 硬件需要选择 A/B 通信接口及 1/2 电源。",
                "Mountings",
                "Languages",
                "Reserved2");
        }

        var software = Combine(selectedByGroup, "FunctionalApps", "Aios", "CommEthernets", "CommProtocols");
        if (!string.IsNullOrWhiteSpace(software) &&
            !_rules.MatchesValidationBlock("Software", software, version))
        {
            yield return CreateValidationMessage(
                IsEnglish
                    ? "The AppPack and process bus connectivity combination does not meet SSC600 ordering rules. Smaller process bus capacity limits the selectable cable/line, advanced cable/line and motor AppPack levels."
                    : "应用包和过程总线连接组合不满足 SSC600 订货规则。过程总线容量越小，可选择的线路/高级线路/电动机应用包等级也越受限。",
                "FunctionalApps",
                "Aios",
                "CommEthernets",
                "CommProtocols");
        }
    }

    private ValidationMessageViewModel CreateValidationMessage(string text, params string[] groupNames)
    {
        var targets = groupNames
            .Select(groupName => Groups.FirstOrDefault(group => group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
            .Where(group => group is not null)
            .Cast<Ssc600GroupViewModel>()
            .Select(group => new ValidationMessageTargetViewModel(group.DisplayName, null))
            .ToList();
        return new ValidationMessageViewModel(text, targets);
    }

    private void UpdateOptionStates(
        IReadOnlyDictionary<string, Ssc600OptionViewModel> selectedByGroup,
        string version)
    {
        var selectedErrors = Messages
            .Where(message => !message.IsSuccess)
            .ToList();

        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                var isAvailable = option.Option.SupportsVersion(version);
                var hasError = option.IsSelected && selectedErrors.Any(message =>
                    message.Targets.Any(target => target.GroupName.Equals(group.DisplayName, StringComparison.OrdinalIgnoreCase)));
                option.SetState(isAvailable, hasError);
            }

            group.RefreshValidationState();
            group.RefreshSelectedSummary();
        }
    }

    public void JumpToMessage(ValidationMessageViewModel message)
    {
        if (message.PrimaryTarget is not null)
        {
            JumpToTarget(message.PrimaryTarget);
        }
    }

    public void JumpToTarget(ValidationMessageTargetViewModel target)
    {
        var group = Groups.FirstOrDefault(item => item.DisplayName.Equals(target.GroupName, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            group.IsExpanded = true;
        }
    }

    private IEnumerable<Ssc600SelectedSummaryItemViewModel> BuildSelectedSummary(string version)
    {
        yield return new Ssc600SelectedSummaryItemViewModel(IsEnglish ? "Product version" : "产品版本", $"{version} / IED {_rules.Version(version)?.IedVersion}");
        foreach (var group in Groups.Where(group => !group.Name.Equals("Versions", StringComparison.OrdinalIgnoreCase)))
        {
            var option = group.SelectedOption;
            if (option is null)
            {
                continue;
            }

            if (option.Id.Equals("N", StringComparison.OrdinalIgnoreCase) &&
                !group.Name.Equals("Mountings", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new Ssc600SelectedSummaryItemViewModel(group.DisplayName, $"{option.Id}: {option.Description}");
        }
    }

    private static string Combine(
        IReadOnlyDictionary<string, Ssc600OptionViewModel> selectedByGroup,
        params string[] groups)
    {
        var values = new List<string>();
        foreach (var group in groups)
        {
            if (!selectedByGroup.TryGetValue(group, out var option))
            {
                return "";
            }

            values.Add(option.Id);
        }

        return string.Concat(values);
    }

    private static string SegmentForLocation(string orderCode, string location)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return "";
        }

        if (location.Contains('+', StringComparison.Ordinal))
        {
            var indexes = location.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
                .Where(value => value > 0)
                .ToArray();
            return string.Concat(indexes.Select(index => index <= orderCode.Length ? orderCode[index - 1].ToString() : ""));
        }

        return int.TryParse(location, out var singleIndex) && singleIndex > 0 && singleIndex <= orderCode.Length
            ? orderCode[singleIndex - 1].ToString()
            : "";
    }

    private void SetAllGroupsExpanded(bool isExpanded)
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = isExpanded;
        }
    }

    private void CopyOrderCode()
    {
        ClipboardService.TrySetText(OrderCode, "SSC600", IsEnglish);
    }

    private void CopyOrderingNumber()
    {
        ClipboardService.TrySetText(OnlineOrderingNumber, "SSC600", IsEnglish);
    }

    private async Task ValidateOnlineAsync()
    {
        // 在线请求只接受仍对应当前订货码的响应，用户修改选型后到达的旧结果直接丢弃。
        if (string.IsNullOrWhiteSpace(OrderCode) || IsOnlineValidationBusy)
        {
            return;
        }

        var codeAtRequestStart = OrderCode;
        IsOnlineValidationBusy = true;
        OnlineOrderingNumber = "";
        IsOnlineValidationSuccess = false;
        IsOnlineValidationError = false;
        OnlineStatus = IsEnglish ? "Checking online..." : "在线校验中...";

        try
        {
            var result = await _onlineValidationService.ValidateAsync(codeAtRequestStart);
            if (!codeAtRequestStart.Equals(OrderCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            OnlineOrderingNumber = result.OrderingNumber ?? "";
            IsOnlineValidationSuccess = result.IsValid && HasOnlineOrderingNumber;
            IsOnlineValidationError = !IsOnlineValidationSuccess;
            OnlineStatus = IsOnlineValidationSuccess
                ? IsEnglish ? "Online check passed" : "在线校验通过"
                : IsEnglish ? "Order code is invalid, or no ordering number was returned." : "订货号错误，或未返回订货号。";
        }
        catch (Exception ex)
        {
            OnlineOrderingNumber = "";
            IsOnlineValidationSuccess = false;
            IsOnlineValidationError = true;
            OnlineStatus = IsEnglish ? $"Online check failed: {ex.Message}" : $"在线校验失败：{ex.Message}";
        }
        finally
        {
            IsOnlineValidationBusy = false;
        }
    }

    private void ResetOnlineValidationState()
    {
        OnlineOrderingNumber = "";
        IsOnlineValidationSuccess = false;
        IsOnlineValidationError = false;
        OnlineStatus = IsEnglish ? "Not checked" : "未校验";
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
            IsEnglish ? "SSC600 / SSC600 SW device description" : "SSC600 / SSC600 SW 装置描述",
            IsEnglish ? $"Order code: {OrderCode}" : $"订货码：{OrderCode}",
            IsEnglish ? $"Online check: {OnlineStatus}" : $"在线校验：{OnlineStatus}",
            IsEnglish ? $"Status: {Status}" : $"状态：{Status}",
            ""
        };

        lines.Add(IsEnglish ? "Current selection:" : "当前选型：");
        lines.AddRange(SelectedSummaryItems.Select(item => $"{item.Name}: {item.Value}"));

        if (RequestedFunctions.Count > 0)
        {
            lines.Add("");
            lines.Add(IsEnglish ? "Requested protection functions:" : "已输入保护功能：");
            lines.AddRange(RequestedFunctions.Select(function => $"{function.CodeWithAnsi}: {function.DisplayName}"));
        }

        if (AppRecommendations.Count > 0)
        {
            lines.Add("");
            lines.Add(IsEnglish ? "AppPack recommendation:" : "应用包推荐：");
            lines.Add(AppRecommendationSummary);
            lines.AddRange(AppRecommendations.Select(recommendation =>
                $"{recommendation.GroupName}: {recommendation.OptionId} - {recommendation.DisplayText}"));
        }

        var activeMessages = Messages.Where(message => !message.IsSuccess).ToList();
        if (activeMessages.Count > 0)
        {
            lines.Add("");
            lines.Add(IsEnglish ? "Validation messages:" : "校验提示：");
            lines.AddRange(activeMessages.Select(message => message.Text));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ImportOrderCode()
    {
        // 只有规范化后恰好 18 位才进入批量回填；未匹配位段回退到可用规则项并集中提示用户复核。
        var window = new CombinationCodeImportWindow(
            IsEnglish ? "Import SSC600 order code" : "导入 SSC600 订货码",
            IsEnglish ? "Enter a complete 18-character SSC600 or SSC600 SW order code." : "请输入完整 18 位 SSC600 或 SSC600 SW 订货码。",
            IsEnglish ? "Import" : "导入",
            "SBACANANCAA1ANN16G");

        if (window.ShowDialog() != true)
        {
            return;
        }

        var code = new string((window.CombinationCode ?? "")
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray())
            .Trim()
            .ToUpperInvariant();

        if (code.Length != 18)
        {
            MessageBox.Show(
                IsEnglish ? "SSC600 order code must contain 18 characters." : "SSC600 订货码必须为 18 位。",
                IsEnglish ? "SSC600 Configurator" : "SSC600 选型",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var notFound = ApplyOrderCode(code);
        Recalculate();

        if (notFound.Count > 0)
        {
            MessageBox.Show(
                IsEnglish
                    ? $"The order code was imported, but these segments were not matched and were replaced by default values:{Environment.NewLine}{string.Join(Environment.NewLine, notFound)}"
                    : $"订货码已导入，但以下位段未匹配，已按默认项回填：{Environment.NewLine}{string.Join(Environment.NewLine, notFound)}",
                IsEnglish ? "SSC600 Configurator" : "SSC600 选型",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void AddSuggestedFunction(Ssc600FunctionSuggestionViewModel suggestion)
    {
        AddFunction(suggestion.Function);
        FunctionSearchText = "";
        FunctionSuggestions.Clear();
        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    public void RemoveRequestedFunction(Ssc600RequestedFunctionViewModel function)
    {
        var existing = RequestedFunctions.FirstOrDefault(item => item.Code.Equals(function.Code, StringComparison.OrdinalIgnoreCase));
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
        var tokens = Regex.Split(FunctionSearchText, @"[\r\n,;，；、]+")
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
        var unresolved = new List<string>();
        var candidateFunctions = new List<Ssc600FunctionEntry>();

        foreach (var token in tokens)
        {
            var function = _functionCatalog.ResolveExact(token);
            if (function is not null)
            {
                AddFunction(function);
                continue;
            }

            var candidates = _functionCatalog.Search(token, 20)
                .Where(candidate => RequestedFunctions.All(selected => !selected.Code.Equals(candidate.Code, StringComparison.OrdinalIgnoreCase)))
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
                .DistinctBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
                .Select(function => new Ssc600FunctionSuggestionViewModel(function, this)));
            RefreshRecommendations();
            AppRecommendationSummary = IsEnglish
                ? $"{AppRecommendationSummary}; some inputs were not unique, select from candidates: {string.Join(", ", unresolved)}"
                : $"{AppRecommendationSummary}；以下输入未能唯一匹配，请从候选中选择：{string.Join("，", unresolved)}";
            RefreshFunctionStateProperties();
            return;
        }

        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    private void AddFunction(Ssc600FunctionEntry function)
    {
        if (RequestedFunctions.Any(item => item.Code.Equals(function.Code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        RequestedFunctions.Add(new Ssc600RequestedFunctionViewModel(function, this));
    }

    private void RefreshFunctionSuggestions()
    {
        var token = Regex.Split(FunctionSearchText, @"[\r\n,;，；、]+").LastOrDefault()?.Trim() ?? "";
        Replace(FunctionSuggestions, _functionCatalog
            .Search(token, 20)
            .Where(function => RequestedFunctions.All(selected => !selected.Code.Equals(function.Code, StringComparison.OrdinalIgnoreCase)))
            .Select(function => new Ssc600FunctionSuggestionViewModel(function, this)));
        OnPropertyChanged(nameof(HasFunctionSuggestions));
    }

    private void RefreshRecommendations()
    {
        // 功能需求先由目录求 AppPack 覆盖集合，是否真正可选仍由用户应用后触发订货码规则重算确认。
        if (RequestedFunctions.Count == 0)
        {
            AppRecommendations.Clear();
            AppRecommendationSummary = DefaultAppRecommendationSummary();
            RefreshFunctionStateProperties();
            return;
        }

        var result = _functionCatalog.Recommend(RequestedFunctions.Select(function => function.Code).ToList());
        Replace(AppRecommendations, result.Recommendations.Select(item => new Ssc600AppRecommendationViewModel(item, this)));

        var details = new List<string>();
        if (result.Recommendations.Count > 0)
        {
            details.Add(IsEnglish
                ? $"Recommended AppPack(s): {string.Join(" + ", result.Recommendations.Select(RecommendationDisplayText))}"
                : $"推荐使用应用包：{string.Join(" + ", result.Recommendations.Select(RecommendationDisplayText))}");
        }
        else
        {
            details.Add(IsEnglish
                ? "All selected functions are base functionality; no additional AppPack is required."
                : "所选功能均为基础功能，无需额外应用包。");
        }

        if (result.BaseFunctions.Count > 0)
        {
            details.Add(IsEnglish
                ? $"Base functionality: {string.Join(", ", result.BaseFunctions.Select(function => function.Code))}"
                : $"基础功能：{string.Join(", ", result.BaseFunctions.Select(function => function.Code))}");
        }

        AppRecommendationSummary = string.Join(IsEnglish ? "; " : "；", details);
        RefreshFunctionStateProperties();
    }

    private void ClearFunctionRecommendation()
    {
        RequestedFunctions.Clear();
        AppRecommendations.Clear();
        FunctionSuggestions.Clear();
        AppRecommendationSummary = DefaultAppRecommendationSummary();
        RefreshFunctionStateProperties();
    }

    private void RefreshFunctionStateProperties()
    {
        OnPropertyChanged(nameof(HasFunctionSuggestions));
        OnPropertyChanged(nameof(HasRequestedFunctions));
        OnPropertyChanged(nameof(HasAppRecommendations));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
    }

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

        foreach (var recommendation in AppRecommendations)
        {
            recommendation.RefreshLanguage();
        }

        RefreshFunctionStateProperties();
    }

    internal string RecommendationDisplayText(Ssc600PackageRecommendation recommendation)
    {
        var group = Groups.FirstOrDefault(item => item.Name.Equals(recommendation.GroupName, StringComparison.OrdinalIgnoreCase));
        var option = group?.Options.FirstOrDefault(item => item.Id.Equals(recommendation.OptionId, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            return recommendation.DisplayText;
        }

        return IsEnglish
            ? $"{group!.DisplayName} {option.Id} ({option.Description})"
            : $"{group!.DisplayName} {option.Id}（{option.Description}）";
    }

    private string DefaultAppRecommendationSummary() => IsEnglish
        ? "Enter ANSI code, ABB function code, Chinese name or English name, then add it; the tool recommends the SSC600 AppPack to select."
        : "输入 ANSI code、ABB 功能码、中文或英文功能名称后添加；系统会推荐应选择的 SSC600 应用包。";

    internal static string PackageCategoryEnglish(string category) => category switch
    {
        "基础功能" => "Base functionality",
        "主应用包" => "Main AppPack",
        "线路/电缆应用包" => "Cable/Line AppPack",
        "高级线路/电缆应用包" => "Advanced Cable/Line AppPack",
        "变压器应用包" => "Transformer AppPack",
        "电动机应用包" => "Motor AppPack",
        "附加应用包" => "Spare / Additional Application Package",
        "特殊单间隔应用包" => "Special bay-level AppPack",
        "特殊多间隔应用包" => "Special multi-bay AppPack",
        "过程总线" => "IEC 61850-9-2LE Process Bus Connectivity",
        _ => category
    };

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}

public sealed class Ssc600GroupViewModel : ObservableObject
{
    private readonly Ssc600SelectionViewModel _owner;
    private bool _isExpanded = true;
    private int _errorCount;

    public Ssc600GroupViewModel(Ssc600SelectionViewModel owner, Ssc600GroupRule group)
    {
        _owner = owner;
        Group = group;
        Options = new ObservableCollection<Ssc600OptionViewModel>(
            group.Options.Select(option => new Ssc600OptionViewModel(this, option)));
    }

    public Ssc600GroupRule Group { get; }
    public string Name => Group.Name;
    public string DisplayName => _owner.IsEnglish ? Group.DisplayNameEnglish : Group.DisplayName;
    public string Location => Group.Location;
    public int SortOrder => Group.SortOrder;
    public ObservableCollection<Ssc600OptionViewModel> Options { get; }
    public Ssc600OptionViewModel? SelectedOption => Options.FirstOrDefault(option => option.IsSelected);
    public string SelectionMode => _owner.IsEnglish
        ? $"Position {Location} · required · single select"
        : $"第 {Location} 位 · 必选 · 单选";
    public string SelectedSummary => SelectedOption is null
        ? _owner.IsEnglish ? "Not selected" : "未选择"
        : $"{SelectedOption.Id}: {SelectedOption.Description}";

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
    public string ErrorSummary => _owner.IsEnglish ? $"{ErrorCount} issue(s)" : $"需处理 {ErrorCount}";

    internal void HandleSelectionChanged(Ssc600OptionViewModel option)
    {
        _owner.HandleSelectionChanged(this, option);
        RefreshSelectedSummary();
    }

    internal void RefreshSelectedSummary() => OnPropertyChanged(nameof(SelectedSummary));

    internal void RefreshValidationState() => ErrorCount = Options.Count(option => option.HasError);

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SelectionMode));
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(ErrorSummary));
        foreach (var option in Options)
        {
            option.RefreshLanguage();
        }
    }

    internal bool IsEnglish => _owner.IsEnglish;
}

public sealed class Ssc600OptionViewModel(Ssc600GroupViewModel group, Ssc600OptionRule option) : ObservableObject
{
    private bool _isSelected;
    private bool _isAvailable = true;
    private bool _hasError;

    public Ssc600GroupViewModel Group { get; } = group;
    public Ssc600OptionRule Option { get; } = option;
    public string Id => Option.Id;
    public string Description => Group.IsEnglish ? Option.DescriptionEnglish : Option.Description;
    public string SummaryText => $"{Id}: {Description}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!value && _isSelected && Group.Options.Count(option => option.IsSelected) == 1)
            {
                OnPropertyChanged(nameof(IsSelected));
                return;
            }

            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CanToggle));
                Group.HandleSelectionChanged(this);
            }
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(CanToggle));
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool CanToggle => IsAvailable || IsSelected;

    internal void SetSelectedSilently(bool value)
    {
        if (_isSelected == value)
        {
            return;
        }

        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(CanToggle));
    }

    internal void SetState(bool isAvailable, bool hasError)
    {
        IsAvailable = isAvailable;
        HasError = hasError;
    }

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SummaryText));
    }
}

public sealed record Ssc600VersionOptionViewModel(string Id, string DisplayName);

public sealed record Ssc600SelectedSummaryItemViewModel(string Name, string Value);

public sealed record Ssc600FunctionCatalogItemViewModel(
    string Package,
    string PackageEnglish,
    string AbbCode,
    string AnsiCode,
    string ChineseName,
    string EnglishName);

public sealed class Ssc600FunctionSuggestionViewModel : ObservableObject
{
    private readonly Ssc600SelectionViewModel _owner;

    public Ssc600FunctionSuggestionViewModel(Ssc600FunctionEntry function, Ssc600SelectionViewModel owner)
    {
        Function = function;
        _owner = owner;
    }

    public Ssc600FunctionEntry Function { get; }
    public string Code => Function.Code;
    public string Ansi => Function.Ansi;
    public string EnglishName => Function.EnglishName;
    public string ChineseName => Function.ChineseName;
    public string AppsText => Function.IsBase
        ? _owner.IsEnglish ? "Base functionality" : "基础功能"
        : string.Join(" / ", Function.Requirements.Select(item => item.GroupName).Distinct());
    public string DisplayText => string.IsNullOrWhiteSpace(Function.Ansi)
        ? $"{Function.Code}  {(_owner.IsEnglish ? Function.EnglishName : Function.ChineseName)}"
        : $"{Function.Code}  ANSI {Function.Ansi}  {(_owner.IsEnglish ? Function.EnglishName : Function.ChineseName)}";

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(AppsText));
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed class Ssc600RequestedFunctionViewModel(Ssc600FunctionEntry function, Ssc600SelectionViewModel owner) : ObservableObject
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
    public string AppsText => function.IsBase
        ? owner.IsEnglish ? "Base functionality" : "基础功能"
        : owner.IsEnglish ? Ssc600SelectionViewModel.PackageCategoryEnglish(function.Category) : function.Category;

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SecondaryName));
        OnPropertyChanged(nameof(AppsText));
    }
}

public sealed class Ssc600AppRecommendationViewModel(Ssc600PackageRecommendation recommendation, Ssc600SelectionViewModel owner) : ObservableObject
{
    public string GroupName => recommendation.GroupName;
    public string OptionId => recommendation.OptionId;
    public string DisplayText => owner.RecommendationDisplayText(recommendation);
    public string CoveredFunctionsText => recommendation.CoveredFunctions.Count == 0
        ? owner.IsEnglish ? "Dependency" : "依赖项"
        : string.Join(", ", recommendation.CoveredFunctions);

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(CoveredFunctionsText));
    }
}
