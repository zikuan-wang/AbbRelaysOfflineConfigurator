using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows;
using AbbRelaysOfflineConfigurator.Models;
using AbbRelaysOfflineConfigurator.Services;
using Microsoft.Win32;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class Rex640SelectionViewModel : ObservableObject
{
    private readonly Rex640RuleSet _rules = new Rex640RuleLoader().Load();
    private readonly Rex640AppFunctionCatalogService _functionCatalog = new();
    private readonly OnlineValidationService _onlineValidationService = new();
    private bool _isRefreshing;
    private string _orderCode = "";
    private string _status = "";
    private string _onlineStatus = "未校验";
    private string _onlineOrderingNumber = "";
    private string _functionSearchText = "";
    private string _appRecommendationSummary = "";
    private string _appRecommendationVersion = "PCL7";
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private bool _isValid;
    private bool _isOnlineValidationBusy;
    private bool _isOnlineValidationSuccess;
    private bool _isOnlineValidationError;

    public Rex640SelectionViewModel()
    {
        Groups = new ObservableCollection<Rex640GroupViewModel>(
            _rules.MainGroups.Concat(_rules.OptionGroups)
                .Select(group => new Rex640GroupViewModel(this, group)));
        Messages = [];
        SelectedSummaryItems = [];
        Slots = [];
        IoSummaryItems = [];
        FunctionSuggestions = [];
        RequestedFunctions = [];
        AppRecommendations = [];
        VersionOptions = AppRecommendationVersions
            .Select(version => new Rex640VersionOptionViewModel(version, version))
            .ToList();

        CopyOrderCodeCommand = new RelayCommand(CopyOrderCode, () => !string.IsNullOrWhiteSpace(OrderCode));
        CopyOrderingNumberCommand = new RelayCommand(CopyOrderingNumber, () => HasOnlineOrderingNumber);
        OnlineValidateCommand = new RelayCommand(
            () => _ = ValidateOnlineAsync(),
            () => !IsOnlineValidationBusy && !string.IsNullOrWhiteSpace(OrderCode));
        ImportOrderCodeCommand = new RelayCommand(ImportOrderCode);
        ImportOrderingNumberCommand = new RelayCommand(() => _ = ImportOrderingNumberAsync(), () => !IsOnlineValidationBusy);
        ExportWordCommand = new RelayCommand(() => Export("Word"), CanExport);
        ExportExcelCommand = new RelayCommand(() => Export("Excel"), CanExport);
        ExportPdfCommand = new RelayCommand(() => Export("PDF"), CanExport);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription, () => !string.IsNullOrWhiteSpace(OrderCode));
        AddFunctionInputCommand = new RelayCommand(AddFunctionInput, () => !string.IsNullOrWhiteSpace(FunctionSearchText));
        ClearFunctionRecommendationCommand = new RelayCommand(ClearFunctionRecommendation, () => RequestedFunctions.Count > 0);
        ApplyRecommendedAppsCommand = new RelayCommand(ApplyRecommendedApps, () => AppRecommendations.Count > 0);
        ResetCommand = new RelayCommand(Reset);
        ExpandAllCommand = new RelayCommand(() => SetAllGroupsExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllGroupsExpanded(false));

        AppRecommendationSummary = DefaultAppRecommendationSummary();
        Reset();
    }

    public ObservableCollection<Rex640GroupViewModel> Groups { get; }
    public ObservableCollection<ValidationMessageViewModel> Messages { get; }
    public ObservableCollection<Rex640SelectedSummaryItemViewModel> SelectedSummaryItems { get; }
    public ObservableCollection<Rex640SlotViewModel> Slots { get; }
    public ObservableCollection<IoSummaryItemViewModel> IoSummaryItems { get; }
    public ObservableCollection<Rex640FunctionSuggestionViewModel> FunctionSuggestions { get; }
    public ObservableCollection<Rex640RequestedFunctionViewModel> RequestedFunctions { get; }
    public ObservableCollection<Rex640AppRecommendationViewModel> AppRecommendations { get; }
    public IReadOnlyList<string> AppRecommendationVersions { get; } = ["PCL5", "PCL6", "PCL7"];
    public IReadOnlyList<Rex640VersionOptionViewModel> VersionOptions { get; }
    public RelayCommand CopyOrderCodeCommand { get; }
    public RelayCommand CopyOrderingNumberCommand { get; }
    public RelayCommand OnlineValidateCommand { get; }
    public RelayCommand ImportOrderCodeCommand { get; }
    public RelayCommand ImportOrderingNumberCommand { get; }
    public RelayCommand ExportWordCommand { get; }
    public RelayCommand ExportExcelCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }
    public RelayCommand AddFunctionInputCommand { get; }
    public RelayCommand ClearFunctionRecommendationCommand { get; }
    public RelayCommand ApplyRecommendedAppsCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }

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
                RefreshStaticText();
                foreach (var group in Groups)
                {
                    group.RefreshLanguage();
                }

                Recalculate();
                OnlineStatus = OnlineValidationService.LocalizeMessage(OnlineStatus, IsEnglish);
                RefreshFunctionDisplay();
                RefreshRecommendations();
            }
        }
    }

    public string PageTitle => IsEnglish ? "REX640 Configuration Rules" : "REX640 选型规则";

    public string SourceSummary => IsEnglish
        ? "Selection items are limited to REX640 2.0 PCL5/PCL6/PCL7 and validated with REX640.xml."
        : "选型项限定为 REX640 2.0 的 PCL5/PCL6/PCL7，并结合 REX640.xml 做有效性校验。";

    public string VersionText => IsEnglish ? "Product version" : "产品版本";
    public Rex640VersionOptionViewModel? SelectedVersion
    {
        get
        {
            var current = CurrentConnectivityLevel(SelectedByGroup(includeUnavailable: true));
            return VersionOptions.FirstOrDefault(version => version.Id.Equals(current, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (value is null)
            {
                return;
            }

            var group = Groups.FirstOrDefault(group => group.Rule.Name.Equals("ConnectivityLevel", StringComparison.OrdinalIgnoreCase));
            var option = group?.Options.FirstOrDefault(option => option.Id.Equals(value.Id, StringComparison.OrdinalIgnoreCase));
            if (option is not null && !option.IsSelected)
            {
                option.IsSelected = true;
            }
        }
    }
    public string ExpandAllText => IsEnglish ? "Expand" : "展开";
    public string CollapseAllText => IsEnglish ? "Collapse" : "折叠";
    public string OrderCodeTitle => IsEnglish ? "REX640 combination code" : "REX640 组合代码";
    public string ImportOrderCodeText => IsEnglish ? "Import code" : "导入代码";
    public string ImportOrderingNumberText => IsEnglish ? "Import ordering number" : "导入订货号";
    public string CopyOrderCodeText => IsEnglish ? "Copy code" : "复制代码";
    public string OnlineValidateText => IsEnglish ? "Online check" : "在线校验";
    public string OnlineStatusTitle => IsEnglish ? "Online check" : "在线校验";
    public string OnlineStatusLabel => IsEnglish ? "Online status: " : "在线状态：";
    public string OrderingNumberTitle => IsEnglish ? "Ordering number" : "订货号";
    public string OrderingNumberLabel => IsEnglish ? "Ordering number: " : "订货号：";
    public string CopyText => IsEnglish ? "Copy" : "复制";
    public string CopyOrderingNumberText => IsEnglish ? "Copy ordering number" : "复制订货号";
    public string DeviceDescriptionText => IsEnglish ? "Device description" : "装置描述";
    public string AccessoriesText => IsEnglish ? "Accessories / extra items" : "附件/额外功能";
    public string ExportWordText => IsEnglish ? "Export Word" : "导出 Word";
    public string ExportExcelText => IsEnglish ? "Export Excel" : "导出 Excel";
    public string ExportPdfText => IsEnglish ? "Export PDF" : "导出 PDF";
    public string ResetText => IsEnglish ? "Reset" : "重置";
    public string IoSummaryTitle => IsEnglish ? "I/O summary" : "I/O 摘要";
    public string SelectedSummaryTitle => IsEnglish ? "Current selection" : "当前选型";
    public string SlotAllocationTitle => IsEnglish ? "Slot allocation" : "槽位分配";
    public string ValidationMessagesTitle => IsEnglish ? "Validation messages" : "校验消息";
    public string AppRecommendationTitle => IsEnglish ? "APP recommendation" : "APP 推荐";
    public string AppRecommendationVersionText => IsEnglish ? "Recommendation version" : "推荐版本";
    public string FunctionCatalogText => IsEnglish ? "APP function table" : "APP 功能对照表";
    public string FunctionCatalogShortText => IsEnglish ? "APP function table" : "APP 功能对照表";
    public string FunctionInputHint => IsEnglish ? "ANSI code / ABB code / protection function" : "ANSI CODE / ABB CODE / 保护功能";
    public string RecommendFunctionText => IsEnglish ? "Recommend" : "应用推荐";
    public string AddFunctionText => IsEnglish ? "Add" : "添加";
    public string ClearFunctionText => IsEnglish ? "Clear" : "清空";
    public string ClearFunctionsText => IsEnglish ? "Clear functions" : "清空功能";
    public string ApplyRecommendedAppsText => IsEnglish ? "Apply recommended APPs" : "推送推荐到选型";
    public string PushRecommendedAppsText => IsEnglish ? "Apply to selection" : "推送到选型";

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
                RaiseExportCanExecuteChanged();
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
                ImportOrderingNumberCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsOnlineValidationSuccess
    {
        get => _isOnlineValidationSuccess;
        private set
        {
            if (SetProperty(ref _isOnlineValidationSuccess, value))
            {
                RaiseExportCanExecuteChanged();
            }
        }
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

    public string AppRecommendationVersion
    {
        get => _appRecommendationVersion;
        set
        {
            var normalized = AppRecommendationVersions.Contains(value, StringComparer.OrdinalIgnoreCase)
                ? value.ToUpperInvariant()
                : "PCL7";
            if (SetProperty(ref _appRecommendationVersion, normalized))
            {
                RefreshFunctionSuggestions();
                RefreshRecommendations();
            }
        }
    }

    public bool HasFunctionSuggestions => FunctionSuggestions.Count > 0;
    public bool HasRequestedFunctions => RequestedFunctions.Count > 0;
    public bool HasAppRecommendations => AppRecommendations.Count > 0;

    public void Reset()
    {
        _isRefreshing = true;
        try
        {
            foreach (var group in Groups)
            {
                foreach (var option in group.Options)
                {
                    option.SetSelectedSilently(false);
                }
            }

            SelectDefaults();
        }
        finally
        {
            _isRefreshing = false;
        }

        Recalculate();
    }

    internal void HandleSelectionChanged(Rex640GroupViewModel changedGroup, Rex640OptionViewModel changedOption)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            if (changedOption.IsSelected)
            {
                if (!changedGroup.IsMultiple)
                {
                    foreach (var option in changedGroup.Options.Where(option => !ReferenceEquals(option, changedOption)))
                    {
                        option.SetSelectedSilently(false);
                    }
                }
                else if (changedOption.Id.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var option in changedGroup.Options.Where(option => !ReferenceEquals(option, changedOption)))
                    {
                        option.SetSelectedSilently(false);
                    }
                }
                else
                {
                    foreach (var option in changedGroup.Options.Where(option => option.Id.Equals("None", StringComparison.OrdinalIgnoreCase)))
                    {
                        option.SetSelectedSilently(false);
                    }
                }
            }
        }
        finally
        {
            _isRefreshing = false;
        }

        Recalculate();
    }

    internal void Recalculate()
    {
        _isRefreshing = true;
        try
        {
            EnsureMandatorySelections();
            UpdateOptionStates();
        }
        finally
        {
            _isRefreshing = false;
        }

        OrderCode = BuildOrderCode();
        var messages = Validate().ToList();
        IsValid = messages.Count == 0;
        Status = IsValid
            ? IsEnglish ? "REX640 combination code valid" : "REX640 组合代码有效"
            : IsEnglish ? "REX640 combination code needs adjustment" : "REX640 组合代码需要调整";

        Replace(Messages, IsValid
            ? [new ValidationMessageViewModel(IsEnglish ? "Offline validation passed" : "离线校验通过", [], isSuccess: true)]
            : messages);
        Replace(SelectedSummaryItems, BuildSelectedSummary());
        Replace(Slots, BuildSlots());
        RefreshIoSummary();
        RefreshGroupErrors();
        OnPropertyChanged(nameof(SelectedVersion));
    }

    internal void JumpToMessage(ValidationMessageViewModel message)
    {
        if (message.PrimaryTarget is not null)
        {
            JumpToTarget(message.PrimaryTarget);
        }
    }

    internal void JumpToTarget(ValidationMessageTargetViewModel target)
    {
        var group = Groups.FirstOrDefault(group => group.Rule.Name.Equals(target.GroupName, StringComparison.OrdinalIgnoreCase) ||
                                                   group.DisplayName.Equals(target.GroupName, StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return;
        }

        group.IsExpanded = true;
        group.HasError = true;
        if (!string.IsNullOrWhiteSpace(target.OptionId))
        {
            var option = group.Options.FirstOrDefault(option => option.Id.Equals(target.OptionId, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                option.HasError = true;
            }
        }
    }

    private void SelectDefaults()
    {
        foreach (var group in Groups)
        {
            var defaultId = DefaultIdForGroup(group.Rule.Name);
            var option = group.Options.FirstOrDefault(option => option.Id.Equals(defaultId, StringComparison.OrdinalIgnoreCase)) ??
                group.Options.FirstOrDefault(option => !option.Rule.Hidden) ??
                group.Options.FirstOrDefault();
            option?.SetSelectedSilently(true);
        }
    }

    private static string DefaultIdForGroup(string groupName) => groupName switch
    {
        "REX640Product" => "REX640",
        "Housing" => "B",
        "ProductVersion" => "2",
        "InterfaceLevel" => "0",
        "CustomerSpecific" => "G",
        "ConformalCoating" => "C",
        "ArcModule" => "None",
        "CommunicationModule" => "COM1",
        "BIO1Module" => "1x BIO1",
        "BIO2Module" => "None",
        "RTD1Module" => "None",
        "RTD2Module" => "None",
        "BIM1Module" => "None",
        "WideSlotEModule" => "None",
        "AnalogModule" => "1x AIM1",
        "PSM" => "PSM1",
        "Application" => "APP1",
        "Protocol" => "CMP1",
        "Language" => "LNG1",
        "Signal_Connectors" => "SCT1",
        "Current_Connectors" => "MCT1",
        "ConnectivityLevel" => "PCL6",
        _ => "None"
    };

    private void EnsureMandatorySelections()
    {
        var selectedByGroup = SelectedByGroup(includeUnavailable: true);
        var connectivityLevel = CurrentConnectivityLevel(selectedByGroup);

        foreach (var group in Groups)
        {
            group.IsMultiple = group.Rule.AllowsMultiple(connectivityLevel);
            if (!group.IsMultiple)
            {
                var selected = group.Options.Where(option => option.IsSelected).ToList();
                foreach (var option in selected.Skip(1))
                {
                    option.SetSelectedSilently(false);
                }
            }

            if (group.SelectedOptions.Count > 0)
            {
                group.RefreshSelectedSummary();
                continue;
            }

            if (!group.Rule.IsMandatory)
            {
                group.RefreshSelectedSummary();
                continue;
            }

            var preferred = PreferredOptionForGroup(group, connectivityLevel);
            preferred?.SetSelectedSilently(true);
            group.RefreshSelectedSummary();
        }
    }

    private Rex640OptionViewModel? PreferredOptionForGroup(Rex640GroupViewModel group, string connectivityLevel)
    {
        var defaultId = DefaultIdForGroup(group.Rule.Name);
        return group.Options.FirstOrDefault(option =>
                   option.Id.Equals(defaultId, StringComparison.OrdinalIgnoreCase) &&
                   IsOptionVisible(group, option, connectivityLevel, SelectedByGroup(includeUnavailable: true)) &&
                   IsOptionAvailable(group, option, connectivityLevel, SelectedByGroup(includeUnavailable: true))) ??
               group.Options.FirstOrDefault(option =>
                   IsOptionVisible(group, option, connectivityLevel, SelectedByGroup(includeUnavailable: true)) &&
                   IsOptionAvailable(group, option, connectivityLevel, SelectedByGroup(includeUnavailable: true))) ??
               group.Options.FirstOrDefault();
    }

    private void UpdateOptionStates()
    {
        var selectedByGroup = SelectedByGroup(includeUnavailable: true);
        var connectivityLevel = CurrentConnectivityLevel(selectedByGroup);

        foreach (var group in Groups)
        {
            var groupVisible = !IsGroupInvalid(group, selectedByGroup);
            group.IsVisible = groupVisible;
            group.IsMultiple = group.Rule.AllowsMultiple(connectivityLevel);

            foreach (var option in group.Options)
            {
                var visible = groupVisible && IsOptionVisible(group, option, connectivityLevel, selectedByGroup);
                var available = visible && IsOptionAvailable(group, option, connectivityLevel, selectedByGroup);
                option.SetState(visible, available);
            }

            group.RefreshSelectedSummary();
        }
    }

    private bool IsGroupInvalid(Rex640GroupViewModel group, IReadOnlyDictionary<string, IReadOnlyList<string>> selectedByGroup) =>
        !string.IsNullOrWhiteSpace(group.Rule.InvalidSlot) && EvaluateExpression(group.Rule.InvalidSlot, selectedByGroup);

    private bool IsOptionVisible(
        Rex640GroupViewModel group,
        Rex640OptionViewModel option,
        string connectivityLevel,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selectedByGroup)
    {
        if (option.Rule.Hidden || !option.Rule.SupportsVersion(connectivityLevel))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(option.Rule.Visibility) ||
            EvaluateExpression(option.Rule.Visibility, selectedByGroup);
    }

    private static bool IsOptionAvailable(
        Rex640GroupViewModel group,
        Rex640OptionViewModel option,
        string connectivityLevel,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selectedByGroup) =>
        string.IsNullOrWhiteSpace(option.Rule.Validity) ||
        EvaluateExpression(option.Rule.Validity, selectedByGroup);

    private IEnumerable<ValidationMessageViewModel> Validate()
    {
        foreach (var group in Groups.Where(group => group.IsVisible))
        {
            if (group.Rule.IsMandatory && group.SelectedOptions.Count == 0)
            {
                yield return new ValidationMessageViewModel(
                    IsEnglish
                        ? $"{group.DisplayName}: at least one option must be selected."
                        : $"{group.DisplayName}：必须选择一个选项。",
                    [new ValidationMessageTargetViewModel(group.Rule.Name, null)]);
            }

            if (!group.IsMultiple && group.SelectedOptions.Count > 1)
            {
                yield return new ValidationMessageViewModel(
                    IsEnglish
                        ? $"{group.DisplayName}: only one option can be selected."
                        : $"{group.DisplayName}：只能选择一个选项。",
                    [new ValidationMessageTargetViewModel(group.Rule.Name, null)]);
            }

            foreach (var option in group.SelectedOptions)
            {
                if (!option.IsVisible)
                {
                    yield return new ValidationMessageViewModel(
                        IsEnglish
                            ? $"{group.DisplayName} / {option.Id}: not applicable to the current PCL or housing."
                            : $"{group.DisplayName} / {option.Id}：不适用于当前 PCL 或机箱。",
                        [new ValidationMessageTargetViewModel(group.Rule.Name, option.Id)]);
                }
                else if (!option.IsAvailable)
                {
                    var reason = string.IsNullOrWhiteSpace(option.Rule.Validity)
                        ? ""
                        : (IsEnglish ? " Required: " : " 需要：") + FriendlyExpression(option.Rule.Validity);
                    yield return new ValidationMessageViewModel(
                        IsEnglish
                            ? $"{group.DisplayName} / {option.Id}: condition not met.{reason}"
                            : $"{group.DisplayName} / {option.Id}：条件不满足。{reason}",
                        [new ValidationMessageTargetViewModel(group.Rule.Name, option.Id)]);
                }
            }
        }

        foreach (var message in ValidateModuleComposition())
        {
            yield return message;
        }
    }

    private IEnumerable<ValidationMessageViewModel> ValidateModuleComposition()
    {
        var selected = ExpandedSelectedOptionIds().ToList();
        var housing = SelectedSingle("Housing");
        var pcl = SelectedSingle("ConnectivityLevel");
        var smallDiscrete = selected.Count(IsSmallDiscreteModule);
        var slotBModules = selected.Count(id =>
            id.Equals("BIO1", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("BIO2", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("BIM1", StringComparison.OrdinalIgnoreCase));
        var analogCount = selected.Count(IsAnalogModule);
        var wideSlotECount = selected.Count(IsWideSlotEModule);

        if (slotBModules == 0)
        {
            yield return new ValidationMessageViewModel(
                IsEnglish
                    ? "At least one BIO/BIM module is required for slot B."
                    : "至少需要 1 块 BIO/BIM 模块用于 B 插槽。",
                [new ValidationMessageTargetViewModel("BIO1Module", null)]);
        }

        if (analogCount == 0)
        {
            yield return new ValidationMessageViewModel(
                IsEnglish
                    ? "At least one analog or sensor module is required."
                    : "至少需要 1 块模拟量或传感器模块。",
                [new ValidationMessageTargetViewModel("AnalogModule", null)]);
        }

        var eSlotDemand = Math.Max(0, analogCount - 1) + wideSlotECount;
        if (housing.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            if (smallDiscrete > 2)
            {
                yield return new ValidationMessageViewModel(
                    IsEnglish
                        ? "Narrow housing A can fit at most two BIO/BIM/RTD modules."
                        : "窄机箱 A 最多可放置 2 块 BIO/BIM/RTD 模块。",
                    [new ValidationMessageTargetViewModel("Housing", "A")]);
            }

            if (eSlotDemand > 0)
            {
                yield return new ValidationMessageViewModel(
                    IsEnglish
                        ? "Narrow housing A has no slot E, so a second analog module or wide slot E module cannot be selected."
                        : "窄机箱 A 没有 E 插槽，不能选择第二块模拟量模块或宽模块槽 E 模块。",
                    [new ValidationMessageTargetViewModel("Housing", "A")]);
            }
        }
        else
        {
            if (eSlotDemand > 1)
            {
                yield return new ValidationMessageViewModel(
                    IsEnglish
                        ? "Slot E can be used only once. Do not select both a second analog module and a wide slot E module."
                        : "E 插槽只能使用一次，不能同时选择第二块模拟量模块和宽模块槽 E 模块。",
                    [new ValidationMessageTargetViewModel("AnalogModule", null), new ValidationMessageTargetViewModel("WideSlotEModule", null)]);
            }

            var maxSmallDiscrete = eSlotDemand == 0 ? 4 : 3;
            if (smallDiscrete > maxSmallDiscrete)
            {
                yield return new ValidationMessageViewModel(
                    IsEnglish
                        ? $"Standard housing B can fit at most {maxSmallDiscrete} BIO/BIM/RTD modules with the current slot E usage."
                        : $"标准机箱 B 在当前 E 插槽占用下最多可放置 {maxSmallDiscrete} 块 BIO/BIM/RTD 模块。",
                    [new ValidationMessageTargetViewModel("BIO1Module", null)]);
            }
        }

        _ = pcl;
    }

    private void RefreshGroupErrors()
    {
        foreach (var group in Groups)
        {
            group.HasError = false;
            group.ErrorSummary = "";
            foreach (var option in group.Options)
            {
                option.HasError = false;
            }
        }

        foreach (var message in Messages.Where(message => !message.IsSuccess))
        {
            foreach (var target in message.Targets)
            {
                var group = Groups.FirstOrDefault(group => group.Rule.Name.Equals(target.GroupName, StringComparison.OrdinalIgnoreCase));
                if (group is null)
                {
                    continue;
                }

                group.HasError = true;
                group.ErrorSummary = IsEnglish ? "Check" : "需检查";
                if (!string.IsNullOrWhiteSpace(target.OptionId))
                {
                    var option = group.Options.FirstOrDefault(option => option.Id.Equals(target.OptionId, StringComparison.OrdinalIgnoreCase));
                    if (option is not null)
                    {
                        option.HasError = true;
                    }
                }
            }
        }
    }

    private string BuildOrderCode()
    {
        var mainCode = BuildMainCode();
        var optionCodes = Groups
            .Where(group => !group.Rule.IsMainGroup && group.IsVisible)
            .SelectMany(group => group.SelectedOptions)
            .Where(option => !option.Id.Equals("None", StringComparison.OrdinalIgnoreCase))
            .SelectMany(option => ExpandOrderCodeOption(option.Id))
            .ToList();

        return optionCodes.Count == 0 ? mainCode : $"{mainCode}+{string.Join("+", optionCodes)}";
    }

    private static IEnumerable<string> ExpandOrderCodeOption(string optionId)
    {
        var match = Regex.Match(optionId, @"^(?<count>\d+)x\s+(?<code>[A-Z0-9]+)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            yield return optionId;
            yield break;
        }

        var count = int.Parse(match.Groups["count"].Value);
        var code = match.Groups["code"].Value.ToUpperInvariant();
        for (var index = 0; index < count; index++)
        {
            yield return code;
        }
    }

    private string BuildMainCode()
    {
        var maxPosition = _rules.MainGroups
            .SelectMany(group => group.Location.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(position => int.TryParse(position, out var value) ? value : 0)
            .DefaultIfEmpty(11)
            .Max();
        var chars = Enumerable.Repeat('#', maxPosition).ToArray();

        foreach (var group in Groups.Where(group => group.Rule.IsMainGroup))
        {
            var option = group.SelectedOptions.FirstOrDefault();
            if (option is null)
            {
                continue;
            }

            var positions = group.Rule.Location
                .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(position => int.TryParse(position, out var value) ? value : 0)
                .Where(position => position > 0)
                .ToList();
            if (positions.Count == 0)
            {
                continue;
            }

            if (positions.Count == 1)
            {
                chars[positions[0] - 1] = option.Id[0];
                continue;
            }

            for (var index = 0; index < positions.Count && index < option.Id.Length; index++)
            {
                chars[positions[index] - 1] = option.Id[index];
            }
        }

        return new string(chars);
    }

    private void AddFunctionInput()
    {
        var tokens = Regex.Split(FunctionSearchText, @"[\r\n,;，；、]+")
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
        var unresolved = new List<string>();
        var candidateFunctions = new List<Rex640FunctionEntry>();

        foreach (var token in tokens)
        {
            var function = _functionCatalog.ResolveExact(AppRecommendationVersion, token);
            if (function is not null)
            {
                AddFunction(function);
                continue;
            }

            var candidates = _functionCatalog.Search(AppRecommendationVersion, token, 20)
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

        if (unresolved.Count > 0)
        {
            Replace(FunctionSuggestions, candidateFunctions
                .DistinctBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
                .Select(function => new Rex640FunctionSuggestionViewModel(function, this)));
            FunctionSearchText = "";
            RefreshRecommendations();
            var prefix = RequestedFunctions.Count > 0 ? AppRecommendationSummary + "；" : "";
            AppRecommendationSummary = IsEnglish
                ? $"{prefix}Some inputs were not unique, select from candidates: {string.Join(", ", unresolved)}"
                : $"{prefix}以下输入未能唯一匹配，请从候选中选择：{string.Join("，", unresolved)}";
            RefreshFunctionStateProperties();
            return;
        }

        FunctionSearchText = "";
        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    internal void AddSuggestedFunction(Rex640FunctionEntry function)
    {
        AddFunction(function);
        FunctionSearchText = "";
        FunctionSuggestions.Clear();
        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    private void AddFunction(Rex640FunctionEntry function)
    {
        if (RequestedFunctions.Any(item => item.Code.Equals(function.Code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        RequestedFunctions.Add(new Rex640RequestedFunctionViewModel(function, this));
    }

    internal void AddRequestedFunction(Rex640FunctionEntry function)
    {
        AddFunction(function);
        OnPropertyChanged(nameof(HasRequestedFunctions));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        RefreshRecommendations();
    }

    internal void RemoveRequestedFunction(Rex640RequestedFunctionViewModel function)
    {
        RequestedFunctions.Remove(function);
        OnPropertyChanged(nameof(HasRequestedFunctions));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        RefreshRecommendations();
    }

    private void ClearFunctionRecommendation()
    {
        RequestedFunctions.Clear();
        FunctionSuggestions.Clear();
        AppRecommendations.Clear();
        OnPropertyChanged(nameof(HasRequestedFunctions));
        OnPropertyChanged(nameof(HasFunctionSuggestions));
        OnPropertyChanged(nameof(HasAppRecommendations));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        ApplyRecommendedAppsCommand.RaiseCanExecuteChanged();
        AppRecommendationSummary = DefaultAppRecommendationSummary();
    }

    private void RefreshFunctionSuggestions()
    {
        var token = Regex.Split(FunctionSearchText, @"[\r\n,;，；、]+").LastOrDefault()?.Trim() ?? "";
        Replace(FunctionSuggestions, _functionCatalog
            .Search(AppRecommendationVersion, token, 20)
            .Where(function => RequestedFunctions.All(selected => !selected.Code.Equals(function.Code, StringComparison.OrdinalIgnoreCase)))
            .Select(function => new Rex640FunctionSuggestionViewModel(function, this)));
        OnPropertyChanged(nameof(HasFunctionSuggestions));
    }

    private void RefreshRecommendations()
    {
        if (RequestedFunctions.Count == 0)
        {
            AppRecommendations.Clear();
            OnPropertyChanged(nameof(HasAppRecommendations));
            ApplyRecommendedAppsCommand.RaiseCanExecuteChanged();
            AppRecommendationSummary = DefaultAppRecommendationSummary();
            return;
        }

        var result = _functionCatalog.Recommend(AppRecommendationVersion, RequestedFunctions.Select(function => function.Code).ToList());
        Replace(AppRecommendations, result.Apps.Select(app => new Rex640AppRecommendationViewModel(app, this)));
        OnPropertyChanged(nameof(HasAppRecommendations));
        ApplyRecommendedAppsCommand.RaiseCanExecuteChanged();

        var details = new List<string>();
        if (result.Apps.Count > 0)
        {
            details.Add(IsEnglish
                ? $"{AppRecommendationVersion} recommended package(s): {string.Join(" + ", result.Apps.Select(app => app.Id))}"
                : $"{AppRecommendationVersion} 推荐应用包：{string.Join(" + ", result.Apps.Select(app => app.Id))}");
        }

        if (result.BaseFunctions.Count > 0)
        {
            details.Add(IsEnglish
                ? $"Base functionality: {string.Join(", ", result.BaseFunctions)}"
                : $"基础功能：{string.Join("，", result.BaseFunctions)}");
        }

        AppRecommendationSummary = string.Join(IsEnglish ? "; " : "；", details);
    }

    private void RefreshFunctionStateProperties()
    {
        AddFunctionInputCommand.RaiseCanExecuteChanged();
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        ApplyRecommendedAppsCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasFunctionSuggestions));
        OnPropertyChanged(nameof(HasRequestedFunctions));
        OnPropertyChanged(nameof(HasAppRecommendations));
    }

    private void ApplyRecommendedApps()
    {
        var recommended = AppRecommendations.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var group = Groups.FirstOrDefault(group => group.Rule.Name.Equals("Application", StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            foreach (var option in group.Options)
            {
                option.SetSelectedSilently(recommended.Contains(option.Id));
            }
        }
        finally
        {
            _isRefreshing = false;
        }

        Recalculate();
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
    }

    private string DefaultAppRecommendationSummary() => IsEnglish
        ? $"{AppRecommendationVersion}: enter ANSI code, ABB function code, Chinese name or English name, then add it."
        : $"{AppRecommendationVersion}：输入 ANSI CODE、ABB 功能码、中文或英文保护功能名称后添加。";

    private IReadOnlyList<Rex640SelectedSummaryItemViewModel> BuildSelectedSummary() =>
        Groups
            .Where(group => group.IsVisible)
            .SelectMany(group => group.SelectedOptions.Select(option => new Rex640SelectedSummaryItemViewModel(
                group.DisplayName,
                option.Id,
                option.Description)))
            .ToList();

    private IReadOnlyList<Rex640SlotViewModel> BuildSlots()
    {
        var housing = SelectedSingle("Housing");
        var slots = new Dictionary<string, Rex640SlotViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var slotId in new[] { "A1", "A2", "B", "C", "D", "E", "F", "G" })
        {
            slots[slotId] = EmptySlot(slotId);
        }

        AssignFixed(slots, "A1", "ArcModule");
        AssignFixed(slots, "A2", "CommunicationModule");
        AssignFixed(slots, "G", "PSM");

        if (housing.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            slots["D"] = NotApplicableSlot("D");
            slots["E"] = NotApplicableSlot("E");
        }

        var analogUnits = SelectedModuleUnits("AnalogModule").ToList();
        if (analogUnits.Count > 0)
        {
            slots["F"] = SlotFromUnit("F", analogUnits[0]);
        }

        if (analogUnits.Count > 1 && !slots["E"].IsNotApplicable)
        {
            slots["E"] = SlotFromUnit("E", analogUnits[1]);
        }

        var wideUnits = SelectedModuleUnits("WideSlotEModule").ToList();
        if (wideUnits.Count > 0 && !slots["E"].IsNotApplicable)
        {
            slots["E"] = SlotFromUnit("E", wideUnits[0]);
        }

        var smallUnits = new[] { "BIO1Module", "BIO2Module", "RTD1Module", "RTD2Module", "BIM1Module" }
            .SelectMany(SelectedModuleUnits)
            .ToList();
        var smallSlots = housing.Equals("A", StringComparison.OrdinalIgnoreCase)
            ? new[] { "B", "C" }
            : new[] { "B", "C", "D", "E" };
        var unitIndex = 0;
        foreach (var slotId in smallSlots)
        {
            if (unitIndex >= smallUnits.Count)
            {
                break;
            }

            if (slots[slotId].IsAssigned)
            {
                continue;
            }

            slots[slotId] = SlotFromUnit(slotId, smallUnits[unitIndex++]);
        }

        if (unitIndex < smallUnits.Count)
        {
            var remaining = smallUnits.Skip(unitIndex).Select(unit => unit.Code).ToList();
            slots["E"] = new Rex640SlotViewModel("E", IsEnglish ? "Unallocated" : "未分配", string.Join(", ", remaining), false, true);
        }

        return slots.Values.ToList();
    }

    private void AssignFixed(IDictionary<string, Rex640SlotViewModel> slots, string slotId, string groupName)
    {
        var option = Groups.FirstOrDefault(group => group.Rule.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            ?.SelectedOptions.FirstOrDefault();
        if (option is null || option.Id.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            slots[slotId] = EmptySlot(slotId);
            return;
        }

        slots[slotId] = new Rex640SlotViewModel(slotId, option.Id, option.ShortDescription, true, false, groupName, option.Id);
    }

    private IEnumerable<Rex640ModuleUnit> SelectedModuleUnits(string groupName)
    {
        var option = Groups.FirstOrDefault(group => group.Rule.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            ?.SelectedOptions.FirstOrDefault();
        if (option is null || option.Id.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var match = Regex.Match(option.Id, @"^(?<count>\d+)x\s+(?<code>[A-Z0-9]+)$", RegexOptions.IgnoreCase);
        var count = match.Success ? int.Parse(match.Groups["count"].Value) : 1;
        var code = match.Success ? match.Groups["code"].Value.ToUpperInvariant() : option.Id;
        var description = SingleModuleDescription(option, code);
        for (var index = 0; index < count; index++)
        {
            yield return new Rex640ModuleUnit(code, description, groupName, option.Id);
        }
    }

    private string SingleModuleDescription(Rex640OptionViewModel option, string code)
    {
        var description = option.ShortDescription;
        description = Regex.Replace(description, @"^\d+x\s+", "", RegexOptions.IgnoreCase);
        description = description
            .Replace("each ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("每块", "", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', ':', '：');
        return string.IsNullOrWhiteSpace(description) ? code : description;
    }

    private Rex640SlotViewModel EmptySlot(string slotId) =>
        new(slotId, "N/A", IsEnglish ? "Not configured" : "未配置", false, false);

    private Rex640SlotViewModel NotApplicableSlot(string slotId) =>
        new(slotId, "N/A", IsEnglish ? "Not applicable" : "不适用", false, true);

    private static Rex640SlotViewModel SlotFromUnit(string slotId, Rex640ModuleUnit unit) =>
        new(slotId, unit.Code, unit.Description, true, false, unit.GroupName, unit.OptionId);

    private void RefreshIoSummary()
    {
        IoSummaryItems.Clear();
        var selected = ExpandedSelectedOptionIds().ToList();

        var communication = Groups.FirstOrDefault(group => group.Rule.Name.Equals("CommunicationModule", StringComparison.OrdinalIgnoreCase))
            ?.SelectedOptions.FirstOrDefault();
        if (communication is not null)
        {
            IoSummaryItems.Add(new IoSummaryItemViewModel(
                IsEnglish ? "Communication module" : "通讯模块",
                communication.ShortDescription));
        }

        var moduleCounts = selected
            .Where(id => Rex640ModuleIo.TryGetValue(id, out _))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var key in new[] { "CT", "VT", "BI", "BO", "HSO", "RTD", "mA", "Sensor" })
        {
            var total = moduleCounts.Sum(item => Rex640ModuleIo.Get(item.Key).Count(key) * item.Value);
            if (total > 0)
            {
                IoSummaryItems.Add(new IoSummaryItemViewModel(key, total.ToString()));
            }
        }

        var protocol = selected.Where(id => id.StartsWith("CMP", StringComparison.OrdinalIgnoreCase)).ToList();
        if (protocol.Count > 0)
        {
            IoSummaryItems.Add(new IoSummaryItemViewModel(IsEnglish ? "Protocol" : "通信协议", string.Join(", ", protocol)));
        }

        var pcl = selected.FirstOrDefault(id => id.StartsWith("PCL", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(pcl))
        {
            IoSummaryItems.Add(new IoSummaryItemViewModel("PCL", pcl));
        }
    }

    private IEnumerable<string> ExpandedSelectedOptionIds() =>
        Groups
            .Where(group => group.IsVisible)
            .SelectMany(group => group.SelectedOptions)
            .Where(option => !option.Id.Equals("None", StringComparison.OrdinalIgnoreCase))
            .Select(option => option.Id)
            .SelectMany(ExpandOrderCodeOption);

    private string SelectedSingle(string groupName) =>
        Groups.FirstOrDefault(group => group.Rule.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            ?.SelectedOptions.FirstOrDefault()?.Id ?? "";

    private static bool IsSmallDiscreteModule(string id) =>
        id is "BIO1" or "BIO2" or "BIM1" or "RTD1" or "RTD2";

    private static bool IsWideSlotEModule(string id) =>
        id is "BIO3" or "BIO4" or "BIM3";

    private static bool IsAnalogModule(string id) =>
        id is "AIM1" or "AIM2" or "AIM3" or "SIM1" or "SIM2" or "SIM3";

    private void AddCount(string chineseName, IReadOnlyList<string> selected, Func<string, bool> predicate)
    {
        var values = selected.Where(predicate).ToList();
        if (values.Count == 0)
        {
            return;
        }

        var name = IsEnglish ? chineseName switch
        {
            "弧光模块" => "Arc module",
            "通信模块" => "Communication module",
            "开关量 I/O 模块" => "Binary I/O module",
            "开关量输入模块" => "Binary input module",
            "RTD/mA 模块" => "RTD/mA module",
            "模拟量输入模块" => "Analog input module",
            "传感器输入模块" => "Sensor input module",
            "电源模块" => "Power supply module",
            _ => chineseName
        } : chineseName;
        IoSummaryItems.Add(new IoSummaryItemViewModel(name, $"{values.Count} ({string.Join(", ", values)})"));
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> SelectedByGroup(bool includeUnavailable = false) =>
        Groups.ToDictionary(
            group => group.Rule.Name,
            group => (IReadOnlyList<string>)group.Options
                .Where(option => option.IsSelected && (includeUnavailable || option.IsVisible))
                .Select(option => option.Id)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

    private static bool IsSupportedConnectivityLevel(string value) =>
        value.Equals("PCL5", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("PCL6", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("PCL7", StringComparison.OrdinalIgnoreCase);

    private static string CurrentConnectivityLevel(IReadOnlyDictionary<string, IReadOnlyList<string>> selectedByGroup) =>
        selectedByGroup.TryGetValue("ConnectivityLevel", out var selected) &&
        selected.FirstOrDefault(id => id.StartsWith("PCL", StringComparison.OrdinalIgnoreCase)) is { } pcl
            ? pcl
            : "PCL6";

    private static bool EvaluateExpression(
        string expression,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selectedByGroup)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        var parts = expression.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var equalsIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex <= 0)
            {
                continue;
            }

            var groupName = part[..equalsIndex].Trim();
            var values = part[(equalsIndex + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            selectedByGroup.TryGetValue(groupName, out var selectedValues);
            selectedValues ??= [];

            var forbidden = values
                .Where(value => value.StartsWith('!'))
                .Select(value => value[1..])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allowed = values
                .Where(value => !value.StartsWith('!'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (forbidden.Count > 0 && selectedValues.Any(value => forbidden.Contains(value)))
            {
                return false;
            }

            if (allowed.Count > 0 && !selectedValues.Any(value => allowed.Contains(value)))
            {
                return false;
            }
        }

        return true;
    }

    private string FriendlyExpression(string expression)
    {
        var selectedByGroup = SelectedByGroup(includeUnavailable: true);
        var parts = expression.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var readable = parts.Select(part =>
        {
            var equalsIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex <= 0)
            {
                return part;
            }

            var groupName = part[..equalsIndex].Trim();
            var group = Groups.FirstOrDefault(group => group.Rule.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            var displayName = group?.DisplayName ?? groupName;
            var values = part[(equalsIndex + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var forbidden = values.Where(value => value.StartsWith('!')).Select(value => value[1..]).ToList();
            var allowed = values.Where(value => !value.StartsWith('!')).ToList();

            if (forbidden.Count > 0 && allowed.Count == 0)
            {
                return IsEnglish
                    ? $"{displayName} must not include {string.Join("/", forbidden)}"
                    : $"{displayName} 不能包含 {string.Join("/", forbidden)}";
            }

            var selected = selectedByGroup.TryGetValue(groupName, out var selectedValues)
                ? string.Join("/", selectedValues)
                : "-";
            return IsEnglish
                ? $"{displayName} must be {string.Join("/", allowed)} (current: {selected})"
                : $"{displayName} 应为 {string.Join("/", allowed)}（当前：{selected}）";
        });

        return string.Join(IsEnglish ? "; " : "；", readable);
    }

    private bool CanExport() => IsOnlineValidationSuccess && HasOnlineOrderingNumber;

    private void RaiseExportCanExecuteChanged()
    {
        ExportWordCommand.RaiseCanExecuteChanged();
        ExportExcelCommand.RaiseCanExecuteChanged();
        ExportPdfCommand.RaiseCanExecuteChanged();
    }

    private void ImportOrderCode()
    {
        var window = new CombinationCodeImportWindow(
            IsEnglish ? "Import REX640 combination code" : "导入 REX640 组合代码",
            IsEnglish
                ? "The main code must be first. Option codes after + can be in any order."
                : "主代码必须在开头，后续选项代码可乱序，用 + 分隔。",
            IsEnglish ? "Import" : "导入",
            "REX640B20GC+APP1+COM1+BIO1+...+PCL6");

        window.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(item => item.IsActive);
        if (window.ShowDialog() != true)
        {
            return;
        }

        var notFound = ApplyOrderCode(window.CombinationCode);
        Recalculate();
        if (notFound.Count > 0)
        {
            MessageBox.Show(
                window.Owner,
                string.Join(Environment.NewLine, notFound),
                IsEnglish ? "Import warnings" : "导入提示",
                MessageBoxButton.OK,
            MessageBoxImage.Warning);
        }
    }

    private async Task ImportOrderingNumberAsync()
    {
        if (IsOnlineValidationBusy)
        {
            return;
        }

        var window = new CombinationCodeImportWindow(
            IsEnglish ? "Import REX640 ordering number" : "导入 REX640 订货号",
            IsEnglish
                ? "Enter an ordering number. The tool will reverse-look up the REX640 combination code with the current PCL version. Example: REX640B."
                : "输入订货号，系统会按当前 PCL 版本在线反查 REX640 组合代码。例如：REX640B。",
            IsEnglish ? "Lookup" : "反查",
            "REX640B")
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(item => item.IsActive)
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        var orderingNumber = window.CombinationCode.Trim();
        if (string.IsNullOrWhiteSpace(orderingNumber))
        {
            MessageBox.Show(
                window.Owner,
                IsEnglish ? "Ordering number is empty." : "订货号为空。",
                "REX640",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsOnlineValidationBusy = true;
        IsOnlineValidationSuccess = false;
        IsOnlineValidationError = false;
        OnlineOrderingNumber = "";
        OnlineStatus = IsEnglish ? "Reverse lookup in progress..." : "订货号反查中...";
        var requestedOrderingNumber = EnsureOrderingNumberCurrentVersion(orderingNumber);

        try
        {
            var pclVersion = CurrentConnectivityLevel(SelectedByGroup(includeUnavailable: true));
            var result = await _onlineValidationService.ReverseLookupAsync(requestedOrderingNumber, pclVersion);
            if (!result.IsValid || string.IsNullOrWhiteSpace(result.CompositionCode))
            {
                IsOnlineValidationSuccess = false;
                IsOnlineValidationError = true;
                OnlineOrderingNumber = result.OrderingNumber ?? requestedOrderingNumber;
                OnlineStatus = string.IsNullOrWhiteSpace(result.Message)
                    ? IsEnglish ? "Reverse lookup failed" : "订货号反查失败"
                    : OnlineValidationService.LocalizeMessage(result.Message.TrimEnd('。'), IsEnglish);
                return;
            }

            if (!result.CompositionCode.Trim().StartsWith("REX640", StringComparison.OrdinalIgnoreCase))
            {
                IsOnlineValidationSuccess = false;
                IsOnlineValidationError = true;
                OnlineOrderingNumber = result.OrderingNumber ?? requestedOrderingNumber;
                OnlineStatus = IsEnglish
                    ? "Reverse lookup did not return a REX640 combination code."
                    : "订货号反查未返回 REX640 组合代码。";
                return;
            }

            var notFound = ApplyOrderCode(result.CompositionCode);
            Recalculate();
            OnlineOrderingNumber = result.OrderingNumber ?? requestedOrderingNumber;
            if (notFound.Count > 0)
            {
                IsOnlineValidationSuccess = false;
                IsOnlineValidationError = true;
                OnlineStatus = IsEnglish
                    ? "Reverse lookup returned a code, but some options could not be matched."
                    : "订货号反查已返回组合代码，但部分选项未能匹配。";
                MessageBox.Show(
                    window.Owner,
                    string.Join(Environment.NewLine, notFound),
                    IsEnglish ? "Import warnings" : "导入提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            IsOnlineValidationSuccess = IsValid;
            IsOnlineValidationError = !IsOnlineValidationSuccess;
            OnlineStatus = IsOnlineValidationSuccess
                ? IsEnglish ? "Reverse lookup passed" : "订货号反查通过"
                : IsEnglish ? "Reverse lookup returned a code that needs adjustment." : "订货号反查返回的组合代码需要调整。";
        }
        catch (Exception ex)
        {
            IsOnlineValidationSuccess = false;
            IsOnlineValidationError = true;
            OnlineOrderingNumber = requestedOrderingNumber;
            OnlineStatus = IsEnglish ? $"Order number reverse lookup failed: {ex.Message}" : $"订货号反查失败：{ex.Message}";
        }
        finally
        {
            IsOnlineValidationBusy = false;
        }
    }

    private string EnsureOrderingNumberCurrentVersion(string orderingNumber)
    {
        var value = orderingNumber.Trim();
        if (value.EndsWith("_PCL5", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("_PCL6", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("_PCL7", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var version = CurrentConnectivityLevel(SelectedByGroup(includeUnavailable: true)).ToUpperInvariant();
        if (!IsSupportedConnectivityLevel(version))
        {
            version = "PCL7";
        }

        return $"{value}_{version}";
    }

    private IReadOnlyList<string> ApplyOrderCode(string orderCode)
    {
        var code = (orderCode ?? "").Trim().ToUpperInvariant();
        var parts = code.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return [IsEnglish ? "No code was entered." : "未输入组合代码。"];
        }

        var mainCode = parts[0];
        var optionTokens = parts.Skip(1).ToList();
        var notFound = new List<string>();

        _isRefreshing = true;
        try
        {
            foreach (var group in Groups)
            {
                foreach (var option in group.Options)
                {
                    option.SetSelectedSilently(false);
                }
            }

            foreach (var group in Groups.Where(group => group.Rule.IsMainGroup))
            {
                var segment = SegmentForLocation(mainCode, group.Rule.Location);
                var target = group.Options.FirstOrDefault(option => option.Id.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    notFound.Add($"{group.DisplayName}: {segment}");
                    target = group.Options.FirstOrDefault();
                }

                target?.SetSelectedSilently(true);
            }

            var pclToken = optionTokens.FirstOrDefault(token => token.StartsWith("PCL", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(pclToken))
            {
                if (IsSupportedConnectivityLevel(pclToken))
                {
                    SelectOptionByToken("ConnectivityLevel", pclToken, notFound);
                }
                else
                {
                    notFound.Add(IsEnglish
                        ? $"{pclToken}: unsupported REX640 connectivity level. This page supports only PCL5/PCL6/PCL7."
                        : $"{pclToken}：不支持的 REX640 PCL 版本。本页面仅支持 PCL5/PCL6/PCL7。");
                }

                optionTokens.Remove(pclToken);
            }

            var tokenCounts = optionTokens
                .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var (token, count) in tokenCounts)
            {
                if (TrySelectToken(token, count, notFound))
                {
                    continue;
                }

                if (count == 1)
                {
                    notFound.Add(token);
                }
                else
                {
                    notFound.Add($"{count}x {token}");
                }
            }
        }
        finally
        {
            _isRefreshing = false;
        }

        return notFound;
    }

    private bool TrySelectToken(string token, int count, ICollection<string> notFound)
    {
        var quantityGroup = token.ToUpperInvariant() switch
        {
            "BIO1" => "BIO1Module",
            "BIO2" => "BIO2Module",
            "RTD1" => "RTD1Module",
            "RTD2" => "RTD2Module",
            "BIM1" => "BIM1Module",
            "AIM1" or "AIM2" or "AIM3" or "SIM1" or "SIM2" or "SIM3" => "AnalogModule",
            "BIO3" or "BIO4" or "BIM3" => "WideSlotEModule",
            _ => ""
        };

        if (!string.IsNullOrWhiteSpace(quantityGroup))
        {
            return SelectOptionByToken(quantityGroup, $"{count}x {token.ToUpperInvariant()}", notFound);
        }

        var groupName = token.ToUpperInvariant() switch
        {
            "ARC1" => "ArcModule",
            "COM1" or "COM2" or "COM3" or "COM4" or "COM5" => "CommunicationModule",
            "PSM1" or "PSM2" or "PSM3" => "PSM",
            "SCT1" or "SCT2" or "SCT3" => "Signal_Connectors",
            "MCT1" or "MCT2" => "Current_Connectors",
            _ when token.StartsWith("CMP", StringComparison.OrdinalIgnoreCase) => "Protocol",
            _ when token.StartsWith("LNG", StringComparison.OrdinalIgnoreCase) => "Language",
            _ when token.StartsWith("APP", StringComparison.OrdinalIgnoreCase) || token.StartsWith("ADD", StringComparison.OrdinalIgnoreCase) => "Application",
            _ => ""
        };

        return !string.IsNullOrWhiteSpace(groupName) && SelectOptionByToken(groupName, token.ToUpperInvariant(), notFound, allowMultiple: groupName is "Application" or "Protocol");
    }

    private bool SelectOptionByToken(string groupName, string token, ICollection<string> notFound, bool allowMultiple = false)
    {
        var group = Groups.FirstOrDefault(group => group.Rule.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        var target = group?.Options.FirstOrDefault(option =>
            option.Id.Equals(token, StringComparison.OrdinalIgnoreCase) &&
            !option.Rule.Hidden);
        if (group is null || target is null)
        {
            notFound.Add(token);
            return false;
        }

        if (!allowMultiple && !group.IsMultiple)
        {
            foreach (var option in group.Options)
            {
                option.SetSelectedSilently(false);
            }
        }

        target.SetSelectedSilently(true);
        return true;
    }

    private static string SegmentForLocation(string code, string location)
    {
        var positions = location
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(position => int.TryParse(position, out var value) ? value : 0)
            .Where(position => position > 0)
            .ToList();
        if (positions.Count == 0)
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var position in positions)
        {
            if (position <= code.Length)
            {
                builder.Append(code[position - 1]);
            }
        }

        return builder.ToString();
    }

    private void CopyOrderCode()
    {
        ClipboardService.TrySetText(OrderCode, "REX640", IsEnglish);
    }

    private void CopyOrderingNumber()
    {
        ClipboardService.TrySetText(OnlineOrderingNumber, "REX640", IsEnglish);
    }

    private async Task ValidateOnlineAsync()
    {
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
                : IsEnglish ? "Combination code is invalid, or no ordering number was returned." : "组合代码错误，或未返回订货号。";
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

    private void Export(string format)
    {
        if (!CanExport())
        {
            MessageBox.Show(
                IsEnglish ? "Run online validation and confirm the combination code is correct before exporting." : "请先完成在线校验，并确认组合代码正确后再导出。",
                "REX640",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var extension = format switch
        {
            "Word" => "docx",
            "Excel" => "xlsx",
            _ => "pdf"
        };

        var dialog = new SaveFileDialog
        {
            FileName = $"{SanitizeFileName(OrderCode)}.{extension}",
            Filter = format switch
            {
                "Word" => IsEnglish ? "Word document (*.docx)|*.docx" : "Word 文档 (*.docx)|*.docx",
                "Excel" => IsEnglish ? "Excel workbook (*.xlsx)|*.xlsx" : "Excel 工作簿 (*.xlsx)|*.xlsx",
                _ => IsEnglish ? "PDF file (*.pdf)|*.pdf" : "PDF 文件 (*.pdf)|*.pdf"
            }
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var snapshot = BuildExportSnapshot();
            switch (format)
            {
                case "Word":
                    ExportService.ExportWord(snapshot, dialog.FileName);
                    break;
                case "Excel":
                    ExportService.ExportExcel(snapshot, dialog.FileName);
                    break;
                default:
                    ExportService.ExportPdf(snapshot, dialog.FileName);
                    break;
            }

            MessageBox.Show(IsEnglish ? "Export completed." : "导出完成。", "REX640", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(IsEnglish ? $"Export failed: {ex.Message}" : $"导出失败：{ex.Message}", "REX640", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ExportSnapshot BuildExportSnapshot() =>
        new(
            OrderCode,
            OnlineOrderingNumber,
            Status,
            OnlineStatus,
            IsValid,
            SelectedSummaryItems.Select(item => new SelectedOptionSummary(item.GroupName, item.Code, item.Description)).ToList(),
            IoSummaryItems.Select(item => new ExportIoSummary(item.Name, item.Value)).ToList(),
            BuildSelectedAppSummaryText(),
            BuildSelectedAppFunctionSummaries(),
            Slots.Select(slot => new ExportSlotSummary(slot.SlotId, slot.Code, slot.Description)).ToList(),
            Messages.Select(message => message.Text).ToList(),
            BuildDeviceDescription(),
            IsEnglish ? "ABB REX640 configuration" : "ABB REX640 配置");

    private IReadOnlyList<ExportAppFunctionSummary> BuildSelectedAppFunctionSummaries()
    {
        var selectedApps = SelectedAppPackageIds().ToList();
        if (selectedApps.Count == 0)
        {
            return [];
        }

        var functions = _functionCatalog.GetFunctions(CurrentConnectivityLevel(SelectedByGroup(includeUnavailable: true)));
        return selectedApps
            .SelectMany(app => functions
                .Where(function => function.Apps.Contains(app, StringComparer.OrdinalIgnoreCase))
                .OrderBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
                .Select(function => new ExportAppFunctionSummary(
                    app,
                    function.Code,
                    function.Ansi,
                    function.ChineseName,
                    function.EnglishName)))
            .ToList();
    }

    private string BuildSelectedAppSummaryText()
    {
        var selectedApps = SelectedAppPackageIds().ToList();
        if (selectedApps.Count == 0)
        {
            return IsEnglish ? "None" : "无";
        }

        var functions = _functionCatalog.GetFunctions(CurrentConnectivityLevel(SelectedByGroup(includeUnavailable: true)));
        var summaries = selectedApps.Select(app =>
        {
            var functionTexts = functions
                .Where(function => function.Apps.Contains(app, StringComparer.OrdinalIgnoreCase))
                .Select(function => string.IsNullOrWhiteSpace(function.Ansi) ? function.Code : function.Ansi.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var text = functionTexts.Count == 0
                ? IsEnglish ? "no catalog functions" : "无功能清单"
                : string.Join(", ", functionTexts);
            return $"{app} ({text})";
        });

        return string.Join(IsEnglish ? "; " : "；", summaries);
    }

    private IEnumerable<string> SelectedAppPackageIds()
    {
        var group = Groups.FirstOrDefault(group => group.Rule.Name.Equals("Application", StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return [];
        }

        return group.SelectedOptions
            .Where(option => option.IsSelected && IsAppPackageId(option.Id))
            .Select(option => option.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(AppExportPriorityIndex)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private int AppExportPriorityIndex(string app)
    {
        var index = _functionCatalog.AppPriority
            .Select((value, position) => new { value, position })
            .FirstOrDefault(item => item.value.Equals(app, StringComparison.OrdinalIgnoreCase))
            ?.position;
        return index ?? int.MaxValue / 2;
    }

    private static bool IsAppPackageId(string id) =>
        Regex.IsMatch(id, @"^(APP\d+|ADD\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        var selected = SelectedSummaryItems.ToList();
        var lines = new List<string>
        {
            IsEnglish ? "ABB REX640 protection and control relay device description" : "ABB REX640 保护和控制继电器装置描述",
            IsEnglish ? $"Combination code: {OrderCode}" : $"组合代码：{OrderCode}",
            IsEnglish ? $"Status: {Status}" : $"状态：{Status}",
            ""
        };

        if (HasOnlineOrderingNumber)
        {
            lines.Insert(2, IsEnglish ? $"Ordering number: {OnlineOrderingNumber}" : $"订货号：{OnlineOrderingNumber}");
        }

        foreach (var group in selected.GroupBy(selection => selection.GroupName))
        {
            lines.Add(IsEnglish
                ? $"{group.Key}: {string.Join("; ", group.Select(selection => $"{selection.Code} ({selection.Description})"))}"
                : $"{group.Key}：{string.Join("；", group.Select(selection => $"{selection.Code}({selection.Description})"))}");
        }

        lines.Add("");
        lines.Add(IsEnglish ? "I/O summary:" : "I/O 摘要：");
        lines.Add(IoSummaryItems.Count == 0
            ? IsEnglish ? "None" : "无"
            : string.Join(IsEnglish ? "; " : "；", IoSummaryItems.Select(item => $"{item.Name}={item.Value}")));

        lines.Add("");
        lines.Add(IsEnglish ? "Selected APP summary:" : "当前已选择 APP 摘要：");
        lines.Add(BuildSelectedAppSummaryText());

        lines.Add("");
        lines.Add(IsEnglish ? "Slot allocation:" : "槽位配置：");
        lines.AddRange(Slots.Select(slot => $"{slot.SlotId} {slot.Code} - {slot.Description}"));

        if (Messages.Count > 0)
        {
            lines.Add("");
            lines.Add(IsEnglish ? "Validation messages:" : "校验提示：");
            lines.AddRange(Messages.Select(message => message.Text));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void SetAllGroupsExpanded(bool isExpanded)
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = isExpanded;
        }
    }

    private void RefreshStaticText()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(SelectedVersion));
        OnPropertyChanged(nameof(ExpandAllText));
        OnPropertyChanged(nameof(CollapseAllText));
        OnPropertyChanged(nameof(OrderCodeTitle));
        OnPropertyChanged(nameof(ImportOrderCodeText));
        OnPropertyChanged(nameof(ImportOrderingNumberText));
        OnPropertyChanged(nameof(CopyOrderCodeText));
        OnPropertyChanged(nameof(OnlineValidateText));
        OnPropertyChanged(nameof(OnlineStatusTitle));
        OnPropertyChanged(nameof(OnlineStatusLabel));
        OnPropertyChanged(nameof(OrderingNumberTitle));
        OnPropertyChanged(nameof(OrderingNumberLabel));
        OnPropertyChanged(nameof(CopyText));
        OnPropertyChanged(nameof(CopyOrderingNumberText));
        OnPropertyChanged(nameof(DeviceDescriptionText));
        OnPropertyChanged(nameof(AccessoriesText));
        OnPropertyChanged(nameof(ExportWordText));
        OnPropertyChanged(nameof(ExportExcelText));
        OnPropertyChanged(nameof(ExportPdfText));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(IoSummaryTitle));
        OnPropertyChanged(nameof(SelectedSummaryTitle));
        OnPropertyChanged(nameof(SlotAllocationTitle));
        OnPropertyChanged(nameof(ValidationMessagesTitle));
        OnPropertyChanged(nameof(AppRecommendationTitle));
        OnPropertyChanged(nameof(AppRecommendationVersionText));
        OnPropertyChanged(nameof(FunctionCatalogText));
        OnPropertyChanged(nameof(FunctionCatalogShortText));
        OnPropertyChanged(nameof(FunctionInputHint));
        OnPropertyChanged(nameof(RecommendFunctionText));
        OnPropertyChanged(nameof(AddFunctionText));
        OnPropertyChanged(nameof(ClearFunctionText));
        OnPropertyChanged(nameof(ClearFunctionsText));
        OnPropertyChanged(nameof(ApplyRecommendedAppsText));
        OnPropertyChanged(nameof(PushRecommendedAppsText));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "REX640" : safe;
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }
}

public sealed class Rex640GroupViewModel : ObservableObject
{
    private readonly Rex640SelectionViewModel _owner;
    private bool _isExpanded;
    private bool _hasError;
    private bool _isVisible = true;
    private bool _isMultiple;
    private string _errorSummary = "";
    private string _selectedSummary = "";
    private string _displayName = "";
    private string _selectionMode = "";

    public Rex640GroupViewModel(Rex640SelectionViewModel owner, Rex640GroupRule rule)
    {
        _owner = owner;
        Rule = rule;
        Options = new ObservableCollection<Rex640OptionViewModel>(
            rule.Options.Select(option => new Rex640OptionViewModel(owner, this, option)));
        IsExpanded = rule.IsMainGroup || rule.Name.Equals("ConnectivityLevel", StringComparison.OrdinalIgnoreCase);
        IsMultiple = rule.BaseIsMultiple;
        RefreshLanguage();
        RefreshSelectedSummary();
    }

    public Rex640GroupRule Rule { get; }
    public ObservableCollection<Rex640OptionViewModel> Options { get; }
    public IReadOnlyList<Rex640OptionViewModel> SelectedOptions => Options.Where(option => option.IsSelected).ToList();

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string SelectionMode
    {
        get => _selectionMode;
        private set => SetProperty(ref _selectionMode, value);
    }

    public string SelectedSummary
    {
        get => _selectedSummary;
        private set => SetProperty(ref _selectedSummary, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsMultiple
    {
        get => _isMultiple;
        set
        {
            if (SetProperty(ref _isMultiple, value))
            {
                RefreshSelectionMode();
            }
        }
    }

    public string ErrorSummary
    {
        get => _errorSummary;
        set => SetProperty(ref _errorSummary, value);
    }

    public void RefreshLanguage()
    {
        DisplayName = _owner.IsEnglish ? Rule.DisplayNameEnglish : Rule.DisplayName;
        RefreshSelectionMode();
        foreach (var option in Options)
        {
            option.RefreshLanguage();
        }

        RefreshSelectedSummary();
    }

    public void RefreshSelectedSummary()
    {
        var selected = SelectedOptions;
        SelectedSummary = selected.Count == 0
            ? _owner.IsEnglish ? "Not selected" : "未选择"
            : string.Join(_owner.IsEnglish ? "; " : "；", selected.Select(option => $"{option.Id}: {option.ShortDescription}"));
    }

    private void RefreshSelectionMode()
    {
        SelectionMode = _owner.IsEnglish
            ? $"{(Rule.IsMandatory ? "Required" : "Optional")} · {(IsMultiple ? "multi select" : "single select")}"
            : $"{(Rule.IsMandatory ? "必选" : "可选")} · {(IsMultiple ? "多选" : "单选")}";
    }
}

public sealed class Rex640OptionViewModel : ObservableObject
{
    private readonly Rex640SelectionViewModel _owner;
    private readonly Rex640GroupViewModel _group;
    private bool _isSelected;
    private bool _isAvailable = true;
    private bool _isVisible = true;
    private bool _hasError;
    private string _description = "";
    private string _shortDescription = "";

    public Rex640OptionViewModel(
        Rex640SelectionViewModel owner,
        Rex640GroupViewModel group,
        Rex640OptionRule rule)
    {
        _owner = owner;
        _group = group;
        Rule = rule;
        RefreshLanguage();
    }

    public Rex640OptionRule Rule { get; }
    public string Id => Rule.Id;

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string ShortDescription
    {
        get => _shortDescription;
        private set => SetProperty(ref _shortDescription, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CanToggle));
                _group.RefreshSelectedSummary();
                _owner.HandleSelectionChanged(_group, this);
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

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (SetProperty(ref _isVisible, value))
            {
                OnPropertyChanged(nameof(CanToggle));
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public bool CanToggle => IsVisible && (IsAvailable || IsSelected);

    public void RefreshLanguage()
    {
        Description = _owner.IsEnglish ? Rule.DescriptionEnglish : Rule.Description;
        ShortDescription = _owner.IsEnglish ? Rule.ShortDescriptionEnglish : Rule.ShortDescription;
    }

    public void SetSelectedSilently(bool isSelected)
    {
        if (_isSelected == isSelected)
        {
            return;
        }

        _isSelected = isSelected;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(CanToggle));
        _group.RefreshSelectedSummary();
    }

    public void SetState(bool isVisible, bool isAvailable)
    {
        IsVisible = isVisible;
        IsAvailable = isAvailable;
    }
}

public sealed record Rex640VersionOptionViewModel(string Id, string DisplayName);

public sealed record Rex640SelectedSummaryItemViewModel(string GroupName, string Code, string Description);

public sealed record Rex640SlotViewModel(
    string SlotId,
    string Code,
    string Description,
    bool IsAssigned,
    bool IsNotApplicable,
    string? TargetGroupName = null,
    string? TargetOptionId = null)
{
    public bool CanJump => IsAssigned &&
        !string.IsNullOrWhiteSpace(TargetGroupName) &&
        !string.IsNullOrWhiteSpace(TargetOptionId);

    public bool HasTerminalDiagram => TerminalDiagramService.HasDiagram("REX640", Code);

    public string TerminalDiagramToolTip => Application.Current?.MainWindow?.DataContext is ConfiguratorViewModel { IsEnglish: true }
        ? HasTerminalDiagram ? "View terminal diagram" : "No terminal diagram configured"
        : HasTerminalDiagram ? "查看接线图" : "未配置接线图";
}

internal sealed record Rex640ModuleUnit(string Code, string Description, string GroupName, string OptionId);

internal sealed record Rex640IoProfile(int CT = 0, int VT = 0, int BI = 0, int BO = 0, int HSO = 0, int RTD = 0, int mA = 0, int Sensor = 0)
{
    public int Count(string key) => key switch
    {
        "CT" => CT,
        "VT" => VT,
        "BI" => BI,
        "BO" => BO,
        "HSO" => HSO,
        "RTD" => RTD,
        "mA" => mA,
        "Sensor" => Sensor,
        _ => 0
    };

    public string Describe(int multiplier)
    {
        var parts = new List<string>();
        Add(parts, CT, multiplier, "CT");
        Add(parts, VT, multiplier, "VT");
        Add(parts, BI, multiplier, "BI");
        Add(parts, BO, multiplier, "BO");
        Add(parts, HSO, multiplier, "HSO");
        Add(parts, RTD, multiplier, "RTD");
        Add(parts, mA, multiplier, "mA");
        Add(parts, Sensor, multiplier, "Sensor");
        return string.Join(" + ", parts);
    }

    private static void Add(ICollection<string> parts, int value, int multiplier, string label)
    {
        if (value > 0)
        {
            parts.Add($"{value * multiplier}{label}");
        }
    }
}

internal static class Rex640ModuleIo
{
    private static readonly Dictionary<string, Rex640IoProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BIO1"] = new(BI: 14, BO: 8),
        ["BIO2"] = new(BI: 9, BO: 8),
        ["BIO3"] = new(BI: 14, BO: 8),
        ["BIO4"] = new(BI: 9, BO: 8),
        ["BIM1"] = new(BI: 24),
        ["BIM3"] = new(BI: 24),
        ["RTD1"] = new(RTD: 10, mA: 2),
        ["RTD2"] = new(BI: 12, RTD: 3, mA: 6),
        ["AIM1"] = new(CT: 5, VT: 5),
        ["AIM2"] = new(CT: 6, VT: 4),
        ["AIM3"] = new(CT: 7, VT: 3),
        ["SIM1"] = new(CT: 1, VT: 1, Sensor: 3),
        ["SIM2"] = new(CT: 1, VT: 1, Sensor: 3),
        ["SIM3"] = new(Sensor: 6),
        ["PSM1"] = new(BO: 8, HSO: 2),
        ["PSM2"] = new(BO: 8, HSO: 2),
        ["PSM3"] = new(BO: 8, HSO: 2)
    };

    public static bool TryGetValue(string key, out Rex640IoProfile profile) =>
        Profiles.TryGetValue(key, out profile!);

    public static Rex640IoProfile Get(string key) => Profiles[key];
}

public sealed class Rex640FunctionSuggestionViewModel(Rex640FunctionEntry function, Rex640SelectionViewModel owner) : ObservableObject
{
    public string Code => function.Code;
    public string Ansi => function.Ansi;
    public string EnglishName => function.EnglishName;
    public string ChineseName => function.ChineseName;
    public string CodeWithAnsi => string.IsNullOrWhiteSpace(function.Ansi)
        ? function.Code
        : $"{function.Code} / ANSI {function.Ansi}";
    public string DisplayText => owner.IsEnglish
        ? $"{function.Code}  {function.Ansi}  {function.EnglishName}".Trim()
        : $"{function.Code}  {function.Ansi}  {function.ChineseName}".Trim();
    public RelayCommand AddCommand { get; } = new(() => owner.AddSuggestedFunction(function));

    public void RefreshLanguage() => OnPropertyChanged(nameof(DisplayText));
}

public sealed class Rex640RequestedFunctionViewModel : ObservableObject
{
    private readonly Rex640FunctionEntry _function;
    private readonly Rex640SelectionViewModel _owner;

    public Rex640RequestedFunctionViewModel(Rex640FunctionEntry function, Rex640SelectionViewModel owner)
    {
        _function = function;
        _owner = owner;
        RemoveCommand = new RelayCommand(() => owner.RemoveRequestedFunction(this));
    }

    public string Code => _function.Code;
    public string Ansi => _function.Ansi;
    public string CodeWithAnsi => string.IsNullOrWhiteSpace(_function.Ansi)
        ? _function.Code
        : $"{_function.Code} / ANSI {_function.Ansi}";
    public string EnglishName => _function.EnglishName;
    public string ChineseName => _function.ChineseName;
    public string DisplayName => _owner.IsEnglish ? EnglishName : ChineseName;
    public string SecondaryName => _owner.IsEnglish ? ChineseName : EnglishName;
    public string DisplayText => _owner.IsEnglish
        ? $"{_function.Code} / {_function.Ansi}: {_function.EnglishName}"
        : $"{_function.Code} / {_function.Ansi}: {_function.ChineseName}";
    public RelayCommand RemoveCommand { get; }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SecondaryName));
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed class Rex640AppRecommendationViewModel(Rex640RecommendedApp recommendation, Rex640SelectionViewModel owner) : ObservableObject
{
    public string Id => recommendation.Id;
    public string CoveredFunctionsText => owner.IsEnglish
        ? $"Covers: {string.Join(", ", recommendation.CoveredFunctions)}"
        : $"覆盖功能：{string.Join("，", recommendation.CoveredFunctions)}";

    public void RefreshLanguage() => OnPropertyChanged(nameof(CoveredFunctionsText));
}
