using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using AbbRelaysOfflineConfigurator.Models;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class ConfiguratorViewModel : ObservableObject
{
    public const string ChineseLanguage = "zh-CN";
    public const string EnglishLanguage = "en-US";

    private readonly ProductRuleSet _rules;
    private readonly SelectionValidator _validator;
    private readonly OnlineValidationService _onlineValidationService = new();
    private readonly AppFunctionCatalogService _appFunctionCatalogService = new();
    private string _mainCode = "";
    private string _optionCode = "";
    private string _fullCode = "";
    private string _status = "";
    private string _onlineStatus = "未校验";
    private string _onlineOrderingNumber = "";
    private string _functionSearchText = "";
    private string _appRecommendationVersion = "PCL3";
    private string _appRecommendationSummary = "PCL3：输入 ANSI code、IEC 61850 功能码、中文或英文功能名称后添加。";
    private string _displayLanguage = ChineseLanguage;
    private bool _useFullDescription;
    private bool _isCombinationValid;
    private bool _isOnlineValidationBusy;
    private bool _isOnlineValidationSuccess;
    private bool _isOnlineValidationError;
    private CnLegacySelectorViewModel? _cnLegacySelection;
    private LegacyConversionViewModel? _legacyConversion;
    private Rio600SelectionViewModel? _rio600Selection;
    private Ssc600SelectionViewModel? _ssc600Selection;
    private Rex600SelectionViewModel? _rex600Selection;
    private Rex640SelectionViewModel? _rex640Selection;
    private Re611SelectionViewModel? _re611Selection;
    private Re630SelectionViewModel? _re630Selection;

    public ConfiguratorViewModel()
    {
        var dataPath = ResolveDataPath();
        _rules = new ProductRuleLoader().Load(dataPath);
        _validator = new SelectionValidator(_rules);

        MainGroups = new ObservableCollection<GroupViewModel>(
            _rules.MainGroups.Select(group => new GroupViewModel(group, this)));
        OptionGroups = new ObservableCollection<GroupViewModel>(
            _rules.OptionGroups.Select(group => new GroupViewModel(group, this)));

        Messages = [];
        Slots = [];
        IoSummaryItems = [];
        FunctionSuggestions = [];
        RequestedFunctions = [];
        AppRecommendations = [];
        Home = new HomeViewModel();
        CopyCodeCommand = new RelayCommand(CopyCode, () => !string.IsNullOrWhiteSpace(FullCode));
        CopyOrderingNumberCommand = new RelayCommand(CopyOrderingNumber, () => HasOnlineOrderingNumber);
        ResetCommand = new RelayCommand(Reset);
        ExpandAllCommand = new RelayCommand(() => SetAllGroupsExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllGroupsExpanded(false));
        ImportCodeCommand = new RelayCommand(ImportCode);
        ImportOrderingNumberCommand = new RelayCommand(() => _ = ImportOrderingNumberAsync(), () => !IsOnlineValidationBusy);
        ExportWordCommand = new RelayCommand(() => Export("Word"), CanExport);
        ExportExcelCommand = new RelayCommand(() => Export("Excel"), CanExport);
        ExportPdfCommand = new RelayCommand(() => Export("PDF"), CanExport);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription);
        AddFunctionInputCommand = new RelayCommand(AddFunctionInput, () => !string.IsNullOrWhiteSpace(FunctionSearchText));
        ClearFunctionRecommendationCommand = new RelayCommand(ClearFunctionRecommendation, () => RequestedFunctions.Count > 0);
        ApplyRecommendedAppsCommand = new RelayCommand(ApplyRecommendedApps, () => AppRecommendations.Count > 0);
        OnlineValidateCommand = new RelayCommand(
            () => _ = ValidateOnlineAsync(),
            () => !IsOnlineValidationBusy && !string.IsNullOrWhiteSpace(FullCode));

        Reset();
    }

    public ObservableCollection<GroupViewModel> MainGroups { get; }
    public ObservableCollection<GroupViewModel> OptionGroups { get; }
    public ObservableCollection<ValidationMessageViewModel> Messages { get; }
    public ObservableCollection<SlotViewModel> Slots { get; }
    public ObservableCollection<IoSummaryItemViewModel> IoSummaryItems { get; }
    public ObservableCollection<FunctionSuggestionViewModel> FunctionSuggestions { get; }
    public ObservableCollection<RequestedFunctionViewModel> RequestedFunctions { get; }
    public ObservableCollection<AppRecommendationViewModel> AppRecommendations { get; }
    public IReadOnlyList<string> AppRecommendationVersions { get; } = ["PCL1", "PCL2", "PCL3"];
    public string VersionText => IsEnglish ? "Product version" : "产品版本";
    public IEnumerable<OptionViewModel> VersionOptions =>
        MainGroups.Concat(OptionGroups)
            .FirstOrDefault(group => group.Name.Equals("版本", StringComparison.OrdinalIgnoreCase))
            ?.Options ?? [];
    public OptionViewModel? SelectedVersionOption
    {
        get => MainGroups.Concat(OptionGroups)
            .FirstOrDefault(group => group.Name.Equals("版本", StringComparison.OrdinalIgnoreCase))
            ?.Options.FirstOrDefault(option => option.IsSelected);
        set
        {
            if (value is not null && !value.IsSelected)
            {
                value.IsSelected = true;
            }
        }
    }
    public HomeViewModel Home { get; }
    public CnLegacySelectorViewModel CnLegacySelection =>
        _cnLegacySelection ??= InitializeChild(new CnLegacySelectorViewModel());
    public LegacyConversionViewModel LegacyConversion =>
        _legacyConversion ??= InitializeChild(new LegacyConversionViewModel(_onlineValidationService));
    public Rio600SelectionViewModel Rio600Selection =>
        _rio600Selection ??= InitializeChild(new Rio600SelectionViewModel());
    public Ssc600SelectionViewModel Ssc600Selection =>
        _ssc600Selection ??= InitializeChild(new Ssc600SelectionViewModel());
    public Rex600SelectionViewModel Rex600Selection =>
        _rex600Selection ??= InitializeChild(new Rex600SelectionViewModel());
    public Rex640SelectionViewModel Rex640Selection =>
        _rex640Selection ??= InitializeChild(new Rex640SelectionViewModel());
    public Re611SelectionViewModel Re611Selection =>
        _re611Selection ??= InitializeChild(new Re611SelectionViewModel());
    public Re630SelectionViewModel Re630Selection =>
        _re630Selection ??= InitializeChild(new Re630SelectionViewModel());
    public RelayCommand CopyCodeCommand { get; }
    public RelayCommand CopyOrderingNumberCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public RelayCommand ImportCodeCommand { get; }
    public RelayCommand ImportOrderingNumberCommand { get; }
    public RelayCommand ExportWordCommand { get; }
    public RelayCommand ExportExcelCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }
    public RelayCommand AddFunctionInputCommand { get; }
    public RelayCommand ClearFunctionRecommendationCommand { get; }
    public RelayCommand ApplyRecommendedAppsCommand { get; }
    public RelayCommand OnlineValidateCommand { get; }
    public string DataSource => _rules.SlotConstraintSourceSummary;

    internal bool AllowsMultiple(OptionGroup group) => group.AllowsMultiple(CurrentVersion);
    public bool IsEnglish => DisplayLanguage.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase);

    public string DisplayLanguage
    {
        get => _displayLanguage;
        set
        {
            var normalized = string.Equals(value, EnglishLanguage, StringComparison.OrdinalIgnoreCase)
                ? EnglishLanguage
            : ChineseLanguage;
            if (SetProperty(ref _displayLanguage, normalized))
            {
                OnPropertyChanged(nameof(IsEnglish));
                OnPropertyChanged(nameof(VersionText));
                foreach (var group in MainGroups.Concat(OptionGroups))
                {
                    group.RefreshDisplayMode();
                }

                Home.DisplayLanguage = normalized;
                if (_rio600Selection is not null) _rio600Selection.DisplayLanguage = normalized;
                if (_cnLegacySelection is not null) _cnLegacySelection.DisplayLanguage = normalized;
                if (_legacyConversion is not null) _legacyConversion.DisplayLanguage = normalized;
                if (_ssc600Selection is not null) _ssc600Selection.DisplayLanguage = normalized;
                if (_rex600Selection is not null) _rex600Selection.DisplayLanguage = normalized;
                if (_rex640Selection is not null) _rex640Selection.DisplayLanguage = normalized;
                if (_re611Selection is not null) _re611Selection.DisplayLanguage = normalized;
                if (_re630Selection is not null) _re630Selection.DisplayLanguage = normalized;
                Recalculate();
                OnlineStatus = OnlineValidationService.LocalizeMessage(OnlineStatus, IsEnglish);
                RefreshFunctionDisplay();
            }
        }
    }

    private T InitializeChild<T>(T viewModel)
    {
        switch (viewModel)
        {
            case CnLegacySelectorViewModel vm:
                vm.DisplayLanguage = DisplayLanguage;
                break;
            case LegacyConversionViewModel vm:
                vm.DisplayLanguage = DisplayLanguage;
                break;
            case Rio600SelectionViewModel vm:
                vm.DisplayLanguage = DisplayLanguage;
                break;
            case Ssc600SelectionViewModel vm:
                vm.DisplayLanguage = DisplayLanguage;
                break;
            case Rex600SelectionViewModel vm:
                vm.DisplayLanguage = DisplayLanguage;
                break;
            case Rex640SelectionViewModel vm:
                vm.DisplayLanguage = DisplayLanguage;
                break;
            case Re611SelectionViewModel vm:
                vm.DisplayLanguage = DisplayLanguage;
                break;
            case Re630SelectionViewModel vm:
                vm.DisplayLanguage = DisplayLanguage;
                break;
        }

        return viewModel;
    }

    internal string? CurrentVersion => MainGroups.Concat(OptionGroups)
        .FirstOrDefault(group => group.Name.Equals("版本", StringComparison.OrdinalIgnoreCase))
        ?.SelectedOptions
        .FirstOrDefault()
        ?.Id;

    public bool UseFullDescription
    {
        get => _useFullDescription;
        set
        {
            if (SetProperty(ref _useFullDescription, value))
            {
                foreach (var group in MainGroups.Concat(OptionGroups))
                {
                    group.RefreshDisplayMode();
                }

                Recalculate();
            }
        }
    }

    public string MainCode
    {
        get => _mainCode;
        private set => SetProperty(ref _mainCode, value);
    }

    public string OptionCode
    {
        get => _optionCode;
        private set => SetProperty(ref _optionCode, value);
    }

    public string FullCode
    {
        get => _fullCode;
        private set
        {
            if (SetProperty(ref _fullCode, value))
            {
                ResetOnlineValidationState();
                CopyCodeCommand.RaiseCanExecuteChanged();
                OnlineValidateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsCombinationValid
    {
        get => _isCombinationValid;
        private set => SetProperty(ref _isCombinationValid, value);
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

    public string FunctionSearchText
    {
        get => _functionSearchText;
        set
        {
            if (SetProperty(ref _functionSearchText, value))
            {
                RefreshFunctionSuggestions();
                AddFunctionInputCommand.RaiseCanExecuteChanged();
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
            if (SetProperty(ref _appRecommendationVersion, string.IsNullOrWhiteSpace(value) ? "PCL3" : value))
            {
                RemapRequestedFunctionsForVersion();
                RefreshFunctionSuggestions();
                RefreshRecommendations();
            }
        }
    }

    public bool HasFunctionSuggestions => FunctionSuggestions.Count > 0;
    public bool HasRequestedFunctions => RequestedFunctions.Count > 0;
    public bool HasAppRecommendations => AppRecommendations.Count > 0;

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

    public void Reset()
    {
        foreach (var group in MainGroups.Concat(OptionGroups))
        {
            foreach (var option in group.Options)
            {
                option.SetSelectedSilently(false);
            }

            group.SelectDefault();
        }

        Recalculate();
    }

    private void SetAllGroupsExpanded(bool isExpanded)
    {
        foreach (var group in MainGroups.Concat(OptionGroups))
        {
            group.IsExpanded = isExpanded;
        }
    }

    public void Recalculate()
    {
        var selected = SelectedOptions().ToList();
        var validation = _validator.Validate(selected, UseFullDescription, IsEnglish);

        MainCode = BuildMainCode();
        OptionCode = BuildOptionCode(selected, validation);
        FullCode = string.IsNullOrWhiteSpace(OptionCode) ? MainCode : $"{MainCode}+{OptionCode}";
        IsCombinationValid = validation.IsValid;
        Status = validation.IsValid
            ? IsEnglish ? "Combination code valid" : "组合代码有效"
            : IsEnglish ? "Combination code needs adjustment" : "组合代码需要调整";

        Replace(Messages, validation.IsValid
            ? [new ValidationMessageViewModel(IsEnglish ? "Offline validation passed" : "离线校验通过", [], isSuccess: true)]
            : validation.Messages.Select(message => CreateValidationMessage(message, selected)));
        Replace(Slots, validation.SlotAssignments.Select(assignment => new SlotViewModel(assignment)));
        Replace(IoSummaryItems, BuildIoSummary(selected));
        UpdateOptionStates(selected, validation);
        RefreshRecommendations();
        OnPropertyChanged(nameof(SelectedVersionOption));
    }

    public void AddSuggestedFunction(FunctionSuggestionViewModel suggestion)
    {
        AddFunctionByCode(suggestion.Code);
        FunctionSearchText = "";
        FunctionSuggestions.Clear();
        OnPropertyChanged(nameof(HasFunctionSuggestions));
    }

    public void RemoveRequestedFunction(RequestedFunctionViewModel function)
    {
        var existing = RequestedFunctions.FirstOrDefault(item => item.Code.Equals(function.Code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RequestedFunctions.Remove(existing);
            RefreshRecommendations();
            RefreshFunctionStateProperties();
        }
    }

    private void AddFunctionInput()
    {
        var tokens = Regex.Split(FunctionSearchText, @"[\r\n,;，；、]+")
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
        var unresolved = new List<string>();
        var candidateFunctions = new List<AppFunctionEntry>();

        foreach (var token in tokens)
        {
            var function = _appFunctionCatalogService.ResolveExact(AppRecommendationVersion, token);
            if (function is not null)
            {
                AddFunction(function);
                continue;
            }

            var candidates = _appFunctionCatalogService.Search(AppRecommendationVersion, token, 20)
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
                .Select(function => new FunctionSuggestionViewModel(function, this)));
            FunctionSearchText = "";
            RefreshRecommendations();
            var prefix = RequestedFunctions.Count > 0 ? AppRecommendationSummary + "；" : "";
            AppRecommendationSummary = IsEnglish
                ? $"{prefix}Some inputs were not unique, select from candidates: {string.Join(", ", unresolved)}"
                : $"{prefix}以下输入未能唯一匹配，请从候选中选择：{string.Join("，", unresolved)}";
            RefreshFunctionStateProperties();
            OnPropertyChanged(nameof(HasFunctionSuggestions));
            return;
        }

        FunctionSearchText = "";
        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    private void AddFunctionByCode(string code)
    {
        var function = _appFunctionCatalogService.GetFunctions(AppRecommendationVersion)
            .FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (function is null)
        {
            return;
        }

        AddFunction(function);
        RefreshRecommendations();
        RefreshFunctionStateProperties();
    }

    private void AddFunction(AppFunctionEntry function)
    {
        if (RequestedFunctions.Any(item => item.Code.Equals(function.Code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        RequestedFunctions.Add(new RequestedFunctionViewModel(function, this));
    }

    private void ClearFunctionRecommendation()
    {
        RequestedFunctions.Clear();
        AppRecommendations.Clear();
        FunctionSuggestions.Clear();
        OnPropertyChanged(nameof(HasFunctionSuggestions));
        AppRecommendationSummary = DefaultAppRecommendationSummary();
        RefreshFunctionStateProperties();
    }

    private void RefreshFunctionSuggestions()
    {
        var token = Regex.Split(FunctionSearchText, @"[\r\n,;，；、]+").LastOrDefault()?.Trim() ?? "";
        Replace(FunctionSuggestions, _appFunctionCatalogService
            .Search(AppRecommendationVersion, token, 20)
            .Where(function => RequestedFunctions.All(selected => !selected.Code.Equals(function.Code, StringComparison.OrdinalIgnoreCase)))
            .Select(function => new FunctionSuggestionViewModel(function, this)));
        OnPropertyChanged(nameof(HasFunctionSuggestions));
    }

    private void RefreshRecommendations()
    {
        if (RequestedFunctions.Count == 0)
        {
            AppRecommendations.Clear();
            AppRecommendationSummary = DefaultAppRecommendationSummary();
            RefreshFunctionStateProperties();
            return;
        }

        var result = _appFunctionCatalogService.Recommend(AppRecommendationVersion, RequestedFunctions.Select(function => function.Code).ToList());
        Replace(AppRecommendations, result.Apps.Select(app => new AppRecommendationViewModel(app.Id, app.CoveredFunctions, this)));

        var details = new List<string>();
        if (result.Apps.Count > 0)
        {
            details.Add(IsEnglish
                ? $"{AppRecommendationVersion}: recommended package(s): {string.Join(" + ", result.Apps.Select(app => app.Id))}"
                : $"{AppRecommendationVersion} 推荐 {result.Apps.Count} 个包：{string.Join(" + ", result.Apps.Select(app => app.Id))}");
        }
        else
        {
            details.Add(IsEnglish
                ? $"{AppRecommendationVersion}: selected functions are base functionality; no additional APP is required."
                : $"{AppRecommendationVersion} 所选功能均为基础功能，无需额外 APP。");
        }

        if (result.BaseFunctions.Count > 0)
        {
            details.Add(IsEnglish
                ? $"Base functionality: {string.Join(", ", result.BaseFunctions.Select(function => function.Code))}"
                : $"基础功能：{string.Join(", ", result.BaseFunctions.Select(function => function.Code))}");
        }

        if (result.UnresolvedFunctions.Count > 0)
        {
            details.Add(IsEnglish
                ? $"Unmatched: {string.Join(", ", result.UnresolvedFunctions.Select(function => function.Code))}"
                : $"未匹配：{string.Join(", ", result.UnresolvedFunctions.Select(function => function.Code))}");
        }

        AppRecommendationSummary = string.Join("；", details);
        RefreshFunctionStateProperties();
    }

    private void ApplyRecommendedApps()
    {
        var appGroup = OptionGroups.FirstOrDefault(group => group.Name.Equals("应用包", StringComparison.OrdinalIgnoreCase));
        if (appGroup is null)
        {
            return;
        }

        var recommended = AppRecommendations.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var option in appGroup.Options)
        {
            option.SetSelectedSilently(recommended.Contains(option.Id));
        }

        appGroup.RefreshSelectedSummary();
        Recalculate();
    }

    private void RemapRequestedFunctionsForVersion()
    {
        if (RequestedFunctions.Count == 0)
        {
            return;
        }

        var codes = RequestedFunctions.Select(function => function.Code).ToList();
        RequestedFunctions.Clear();
        foreach (var code in codes)
        {
            var function = _appFunctionCatalogService.GetFunctions(AppRecommendationVersion)
                .FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (function is not null)
            {
                RequestedFunctions.Add(new RequestedFunctionViewModel(function, this));
            }
        }

        RefreshFunctionStateProperties();
    }

    private void RefreshFunctionStateProperties()
    {
        OnPropertyChanged(nameof(HasRequestedFunctions));
        OnPropertyChanged(nameof(HasAppRecommendations));
        ClearFunctionRecommendationCommand.RaiseCanExecuteChanged();
        ApplyRecommendedAppsCommand.RaiseCanExecuteChanged();
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

    private string DefaultAppRecommendationSummary() => IsEnglish
        ? $"{AppRecommendationVersion}: enter ANSI code, IEC 61850 function code, Chinese name or English name, then add it."
        : $"{AppRecommendationVersion}：输入 ANSI code、IEC 61850 功能码、中文或英文功能名称后添加。";

    private IEnumerable<RuleOption> SelectedOptions() =>
        MainGroups.Concat(OptionGroups).SelectMany(group => group.SelectedOptions);

    private string BuildMainCode() =>
        string.Concat(MainGroups
            .OrderBy(group => group.Group.SortOrder)
            .SelectMany(group => group.SelectedOptions)
            .Select(option => option.Id));

    private string BuildOptionCode(IReadOnlyCollection<RuleOption> selectedOptions, ValidationResult validation)
    {
        var parts = new List<string>();

        AppendGroup(parts, selectedOptions, "应用包");

        var hardwareCodes = validation.SlotAssignments
            .Where(slot => slot.IsHardware && slot.IsAssigned)
            .OrderBy(slot => slot.CodeOrder)
            .ThenBy(slot => slot.SlotId, StringComparer.OrdinalIgnoreCase)
            .Select(slot => slot.Code)
            .ToList();

        if (hardwareCodes.Count == 0)
        {
            hardwareCodes = ExpandHardwareOptions(selectedOptions).ToList();
        }

        parts.AddRange(hardwareCodes);
        AppendGroup(parts, selectedOptions, "通讯模块");
        AppendGroup(parts, selectedOptions, "通讯规约");
        AppendGroup(parts, selectedOptions, "LHMI面板");
        AppendGroup(parts, selectedOptions, "电源模块");
        AppendGroup(parts, selectedOptions, "版本");
        AppendGroup(parts, selectedOptions, "信号端子");

        return string.Join("+", parts);
    }

    private static void AppendGroup(ICollection<string> parts, IEnumerable<RuleOption> selectedOptions, string groupName)
    {
        foreach (var option in selectedOptions.Where(option => option.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add(option.Id);
        }
    }

    private static IEnumerable<string> ExpandHardwareOptions(IEnumerable<RuleOption> selectedOptions)
    {
        foreach (var option in selectedOptions.Where(option => !string.IsNullOrWhiteSpace(option.ModuleType) && option.ModuleCount > 0))
        {
            for (var index = 0; index < option.ModuleCount; index++)
            {
                yield return option.ModuleType!;
            }
        }
    }

    private bool CanExport() => IsOnlineValidationSuccess && HasOnlineOrderingNumber;

    private void RaiseExportCanExecuteChanged()
    {
        ExportWordCommand.RaiseCanExecuteChanged();
        ExportExcelCommand.RaiseCanExecuteChanged();
        ExportPdfCommand.RaiseCanExecuteChanged();
    }

    private void CopyCode()
    {
        ClipboardService.TrySetText(FullCode, "REX615", IsEnglish);
    }

    private void CopyOrderingNumber()
    {
        ClipboardService.TrySetText(OnlineOrderingNumber, "REX615", IsEnglish);
    }

    private async Task ValidateOnlineAsync()
    {
        if (string.IsNullOrWhiteSpace(FullCode) || IsOnlineValidationBusy)
        {
            return;
        }

        var codeAtRequestStart = FullCode;
        IsOnlineValidationBusy = true;
        OnlineOrderingNumber = "";
        IsOnlineValidationSuccess = false;
        IsOnlineValidationError = false;
        OnlineStatus = IsEnglish ? "Checking online..." : "在线校验中...";

        try
        {
            var result = await _onlineValidationService.ValidateAsync(codeAtRequestStart);
            if (!codeAtRequestStart.Equals(FullCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            OnlineOrderingNumber = result.OrderingNumber ?? "";
            IsOnlineValidationSuccess = result.IsValid;
            IsOnlineValidationError = !result.IsValid;
            OnlineStatus = result.IsValid
                ? OnlineValidationService.LocalizeMessage(result.Message.TrimEnd('。'), IsEnglish)
                : OnlineValidationService.LocalizeMessage(result.Message, IsEnglish);
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

    private async Task ImportOrderingNumberAsync()
    {
        if (IsOnlineValidationBusy)
        {
            return;
        }

        var window = new CombinationCodeImportWindow(
            IsEnglish ? "Import ordering number" : "导入订货号",
            IsEnglish
                ? "Enter an ordering number. The tool will reverse-look up the combination code with the current PCL version. Example: REX615_11MV5."
                : "输入订货号，系统会按当前 PCL 版本在线反查组合代码。例如：REX615_11MV5。",
            IsEnglish ? "Lookup" : "反查",
            "REX615_11MV5")
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        var orderingNumber = window.CombinationCode.Trim();
        if (string.IsNullOrWhiteSpace(orderingNumber))
        {
            MessageBox.Show(
                IsEnglish ? "Ordering number is empty." : "订货号为空。",
                "REX615",
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
            var result = await _onlineValidationService.ReverseLookupAsync(requestedOrderingNumber, CurrentVersion ?? "PCL1");
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

            ApplyCombinationCode(result.CompositionCode);
            OnlineOrderingNumber = result.OrderingNumber ?? requestedOrderingNumber;
            IsOnlineValidationSuccess = true;
            IsOnlineValidationError = false;
            OnlineStatus = IsEnglish ? "Reverse lookup passed" : "订货号反查通过";
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
        if (value.EndsWith("_PCL1", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("_PCL2", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("_PCL3", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var version = (CurrentVersion ?? "PCL1").Trim().ToUpperInvariant();
        if (version is not "PCL1" and not "PCL2" and not "PCL3")
        {
            version = "PCL1";
        }

        return $"{value}_{version}";
    }

    private void ResetOnlineValidationState()
    {
        OnlineOrderingNumber = "";
        IsOnlineValidationSuccess = false;
        IsOnlineValidationError = false;
        OnlineStatus = IsEnglish ? "Not checked" : "未校验";
    }

    private void ImportCode()
    {
        var window = new CombinationCodeImportWindow
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ApplyCombinationCode(window.CombinationCode);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                IsEnglish ? $"Import failed: {ex.Message}" : $"导入失败：{ex.Message}",
                "REX615",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyCombinationCode(string rawCode)
    {
        var code = rawCode.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException(IsEnglish ? "Combination code is empty." : "组合代码为空。");
        }

        foreach (var group in MainGroups.Concat(OptionGroups))
        {
            foreach (var option in group.Options)
            {
                option.SetSelectedSilently(false);
            }
        }

        var mainPartEnd = code.IndexOf('+');
        var mainPart = mainPartEnd >= 0 ? code[..mainPartEnd] : code;
        var optionPart = mainPartEnd >= 0 ? code[(mainPartEnd + 1)..] : "";
        ParseMainCode(mainPart);
        ParseOptionCodes(optionPart);
        Recalculate();
    }

    private void ParseMainCode(string mainPart)
    {
        var position = 0;
        foreach (var group in MainGroups.OrderBy(group => group.Group.SortOrder))
        {
            var option = group.Options
                .OrderByDescending(option => option.Id.Length)
                .FirstOrDefault(option => mainPart[position..].StartsWith(option.Id, StringComparison.OrdinalIgnoreCase));

            if (option is null)
            {
                throw new InvalidOperationException(IsEnglish
                    ? $"Main code cannot match {GroupDisplayName(group.Name)}."
                    : $"主代码无法匹配 {group.Name}。");
            }

            option.SetSelectedSilently(true);
            position += option.Id.Length;
        }

        if (position != mainPart.Length)
        {
            throw new InvalidOperationException(IsEnglish
                ? $"Main code contains unrecognized content: {mainPart[position..]}."
                : $"主代码存在未识别内容：{mainPart[position..]}。");
        }
    }

    private void ParseOptionCodes(string optionPart)
    {
        var tokens = optionPart
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var exactOptions = OptionGroups
            .SelectMany(group => group.Options)
            .Where(option => string.IsNullOrWhiteSpace(option.Option.ModuleType) || option.Id.Equals(option.Option.ModuleType, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(option => option.Id, StringComparer.OrdinalIgnoreCase);

        var remainingModules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (exactOptions.TryGetValue(token, out var option))
            {
                option.SetSelectedSilently(true);
                continue;
            }

            remainingModules[token] = remainingModules.GetValueOrDefault(token) + 1;
        }

        foreach (var module in remainingModules)
        {
            var option = OptionGroups
                .SelectMany(group => group.Options)
                .FirstOrDefault(option =>
                    option.Option.ModuleType?.Equals(module.Key, StringComparison.OrdinalIgnoreCase) == true &&
                    option.Option.ModuleCount == module.Value);

            if (option is null)
            {
                throw new InvalidOperationException(IsEnglish
                    ? $"Unrecognized option code: {module.Key} x {module.Value}."
                    : $"无法识别选项代码：{module.Key} x {module.Value}。");
            }

            option.SetSelectedSilently(true);
        }
    }

    private void Export(string format)
    {
        if (!CanExport())
        {
            MessageBox.Show(
                IsEnglish ? "Run online validation and confirm the combination code is correct before exporting." : "请先完成在线校验，并确认组合代码正确后再导出。",
                "REX615",
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
            FileName = $"{SanitizeFileName(FullCode)}.{extension}",
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

            MessageBox.Show(IsEnglish ? "Export completed." : "导出完成。", "REX615", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(IsEnglish ? $"Export failed: {ex.Message}" : $"导出失败：{ex.Message}", "REX615", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowDeviceDescription()
    {
        var window = new DeviceDescriptionWindow(BuildDeviceDescription())
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };
        window.ShowDialog();
    }

    private ExportSnapshot BuildExportSnapshot() =>
        new(
            FullCode,
            OnlineOrderingNumber,
            Status,
            OnlineStatus,
            IsCombinationValid,
            BuildSelectedSummaries().ToList(),
            IoSummaryItems.Select(item => new ExportIoSummary(item.Name, item.Value)).ToList(),
            BuildSelectedAppSummaryText(),
            BuildSelectedAppFunctionSummaries(),
            Slots.Select(slot => new ExportSlotSummary(slot.SlotId, slot.Code, slot.Description)).ToList(),
            Messages.Select(message => message.Text).ToList(),
            BuildDeviceDescription());

    private IEnumerable<SelectedOptionSummary> BuildSelectedSummaries() =>
        MainGroups.Concat(OptionGroups)
            .OrderBy(group => group.Group.IsMainCode ? group.Group.SortOrder : group.Group.SortOrder + 1000)
            .SelectMany(group => group.Options
                .Where(option => option.IsSelected)
                .Select(option => new SelectedOptionSummary(group.DisplayName, option.Id, option.DisplayDescription)));

    private IReadOnlyList<ExportAppFunctionSummary> BuildSelectedAppFunctionSummaries()
    {
        var selectedApps = SelectedAppPackageIds().ToList();
        if (selectedApps.Count == 0)
        {
            return [];
        }

        var functions = _appFunctionCatalogService.GetFunctions(CurrentVersion ?? "PCL1");
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

        var functions = _appFunctionCatalogService.GetFunctions(CurrentVersion ?? "PCL1");
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

    private IEnumerable<string> SelectedAppPackageIds() =>
        OptionGroups
            .SelectMany(group => group.Options)
            .Where(option => option.IsSelected && IsAppPackageId(option.Id))
            .Select(option => option.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(AppExportPriorityIndex)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase);

    private int AppExportPriorityIndex(string app)
    {
        var index = _appFunctionCatalogService.AppPriority
            .Select((value, position) => new { value, position })
            .FirstOrDefault(item => item.value.Equals(app, StringComparison.OrdinalIgnoreCase))
            ?.position;
        return index ?? int.MaxValue / 2;
    }

    private static bool IsAppPackageId(string id) =>
        Regex.IsMatch(id, @"^(APP\d+|ADD\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private IEnumerable<IoSummaryItemViewModel> BuildIoSummary(IEnumerable<RuleOption> selectedOptions)
    {
        var selected = selectedOptions.ToList();
        var communication = selected.FirstOrDefault(option => option.GroupName.Equals("通讯模块", StringComparison.OrdinalIgnoreCase));
        if (communication is not null)
        {
            var description = IsEnglish
                ? FirstNonEmpty(communication.EnglishShortDescription, communication.ShortDescription, communication.EnglishDescription, communication.Description)
                : FirstNonEmpty(communication.ShortDescription, communication.Description);
            if (!string.IsNullOrWhiteSpace(description))
            {
                yield return new IoSummaryItemViewModel(IsEnglish ? "Communication module" : "通讯模块", description);
            }
        }

        foreach (var key in new[] { "CT", "VT", "BI", "BO", "HSO", "RTD", "mA" })
        {
            var value = selected.Sum(option => GetIoCount(option, key));
            if (value > 0)
            {
                yield return new IoSummaryItemViewModel(key, value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static int GetIoCount(RuleOption option, string key)
    {
        if (option.Attributes.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return count;
        }

        if (key.Equals("BO", StringComparison.OrdinalIgnoreCase) &&
            option.Attributes.TryGetValue("SO", out var legacyValue) &&
            int.TryParse(legacyValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyCount))
        {
            return legacyCount;
        }

        return 0;
    }

    private string BuildDeviceDescription()
    {
        var selected = BuildSelectedSummaries().ToList();
        var lines = new List<string>
        {
            IsEnglish ? "ABB REX615 protection and control relay device description" : "ABB REX615 保护和控制继电器装置描述",
            IsEnglish ? $"Combination code: {FullCode}" : $"组合代码：{FullCode}",
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
                ? $"{group.Key}: {string.Join("; ", group.Select(selection => $"{selection.Id} ({selection.Description})"))}"
                : $"{group.Key}：{string.Join("；", group.Select(selection => $"{selection.Id}({selection.Description})"))}");
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

    public void JumpToMessage(ValidationMessageViewModel message)
    {
        var target = message.PrimaryTarget;
        if (target is not null)
        {
            JumpToTarget(target);
        }
    }

    public void JumpToTarget(ValidationMessageTargetViewModel target)
    {
        var group = MainGroups.Concat(OptionGroups)
            .FirstOrDefault(group => group.Name.Equals(target.GroupName, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            group.IsExpanded = true;
        }
    }

    private void UpdateOptionStates(IReadOnlyCollection<RuleOption> selectedOptions, ValidationResult validation)
    {
        var selectedByGroup = BuildSelectedByGroup(selectedOptions);
        var slotConstraintFailed = validation.Messages.Any(message =>
            message.Contains("槽位", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("机箱要求", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("不适用于机箱", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("无法装入", StringComparison.OrdinalIgnoreCase));

        foreach (var group in MainGroups.Concat(OptionGroups))
        {
            foreach (var option in group.Options)
            {
                var available = IsOptionAvailable(option.Option, selectedOptions, selectedByGroup);
                var ownError = option.IsSelected &&
                    (!EvaluateValidity(option.Option, selectedByGroup) || !EvaluateRequires(option.Option, selectedByGroup));
                var hardwareError = option.IsSelected && slotConstraintFailed && !string.IsNullOrWhiteSpace(option.Option.ModuleType);
                var missingRequired = IsMissingRequiredOption(option.Option, selectedOptions, selectedByGroup);
                option.SetState(available, ownError || hardwareError || missingRequired);
            }

            group.RefreshSelectedSummary();
            group.RefreshSelectionMode();
            group.RefreshDisplayMode();
            group.RefreshValidationState();
        }
    }

    private ValidationMessageViewModel CreateValidationMessage(string text, IReadOnlyCollection<RuleOption> selectedOptions)
    {
        var targets = new List<ValidationMessageTargetViewModel>();

        var optionSeparator = text.IndexOf(" / ", StringComparison.Ordinal);
        if (optionSeparator > 0)
        {
            var targetGroup = text[..optionSeparator].Trim();
            var remainder = text[(optionSeparator + 3)..];
            var optionEnd = remainder.IndexOf(' ');
            var targetOption = (optionEnd > 0 ? remainder[..optionEnd] : remainder).Trim();
            AddTarget(targets, targetGroup, targetOption);
        }
        else
        {
            foreach (var suffix in new[] { " 必须", " 只能" })
            {
                var index = text.IndexOf(suffix, StringComparison.Ordinal);
                if (index > 0)
                {
                    AddTarget(targets, text[..index].Trim(), null);
                    break;
                }
            }
        }

        foreach (var marker in new[] { "不满足条件：", "要求选择：" })
        {
            var markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                AddConditionTargets(targets, text[(markerIndex + marker.Length)..]);
            }
        }

        var unsuitableMatch = Regex.Match(text, @"^(?<option>[^\s(]+)\s*\((?<module>[^)]+)\)\s*不适用于机箱\s*(?<housing>[^\s。]+)");
        if (unsuitableMatch.Success)
        {
            var optionId = unsuitableMatch.Groups["option"].Value.Trim();
            AddTarget(targets, FindGroupNameForOption(optionId), optionId);
            AddTarget(targets, "机箱", unsuitableMatch.Groups["housing"].Value.Trim());
        }

        var cannotFitMatch = Regex.Match(text, @"无法装入\s*(?<housing>[^\s]+)\s*机箱槽位");
        if (cannotFitMatch.Success)
        {
            AddTarget(targets, "机箱", cannotFitMatch.Groups["housing"].Value.Trim());
            foreach (var option in selectedOptions.Where(option => !string.IsNullOrWhiteSpace(option.ModuleType)))
            {
                AddTarget(targets, option.GroupName, option.Id);
            }
        }

        var requirementMatch = Regex.Match(text, @"^(?<housing>[^\s]+)\s*机箱要求(?:在\s*(?<slots>[^\s]+)\s*)?.*?(?<modules>[A-Z0-9, ]+)?[。]?$");
        if (requirementMatch.Success)
        {
            AddTarget(targets, "机箱", requirementMatch.Groups["housing"].Value.Trim());
            if (text.Contains("模拟量模块", StringComparison.OrdinalIgnoreCase))
            {
                AddTarget(targets, "模拟量模块", null);
            }

            foreach (var module in ExtractModuleTokens(requirementMatch.Groups["modules"].Value))
            {
                foreach (var option in FindOptionsForModule(module))
                {
                    AddTarget(targets, option.GroupName, option.Id);
                }
            }
        }

        var installMarkerIndex = text.IndexOf("安装：", StringComparison.Ordinal);
        if (installMarkerIndex >= 0)
        {
            foreach (var module in ExtractModuleTokens(text[(installMarkerIndex + "安装：".Length)..]))
            {
                foreach (var option in FindOptionsForModule(module))
                {
                    AddTarget(targets, option.GroupName, option.Id);
                }
            }
        }

        if (targets.Count == 0 && text.Contains("机箱", StringComparison.OrdinalIgnoreCase))
        {
            AddTarget(targets, "机箱", null);
        }

        return new ValidationMessageViewModel(BuildReadableValidationText(text), targets);
    }

    private string BuildReadableValidationText(string text)
    {
        var invalidOptionMatch = Regex.Match(
            text,
            @"^(?<group>.+?)\s*/\s*(?<option>.+?)\s+不满足条件：(?<condition>.+)$");
        if (invalidOptionMatch.Success)
        {
            return IsEnglish
                ? $"{FormatOptionLabel(invalidOptionMatch.Groups["group"].Value, invalidOptionMatch.Groups["option"].Value)} cannot be selected now. Reason: {FormatConditionText(invalidOptionMatch.Groups["condition"].Value)}."
                : $"{FormatOptionLabel(invalidOptionMatch.Groups["group"].Value, invalidOptionMatch.Groups["option"].Value)} 当前不能选择，原因：{FormatConditionText(invalidOptionMatch.Groups["condition"].Value)}。";
        }

        var requiredOptionMatch = Regex.Match(
            text,
            @"^(?<group>.+?)\s*/\s*(?<option>.+?)\s+要求选择：(?<condition>.+)$");
        if (requiredOptionMatch.Success)
        {
            return IsEnglish
                ? $"{FormatOptionLabel(requiredOptionMatch.Groups["group"].Value, requiredOptionMatch.Groups["option"].Value)} also requires: {FormatConditionText(requiredOptionMatch.Groups["condition"].Value)}."
                : $"{FormatOptionLabel(requiredOptionMatch.Groups["group"].Value, requiredOptionMatch.Groups["option"].Value)} 还需要满足：{FormatConditionText(requiredOptionMatch.Groups["condition"].Value)}。";
        }

        var unsuitableMatch = Regex.Match(
            text,
            @"^(?<option>[^\s(]+)\s*\((?<module>[^)]+)\)\s*不适用于机箱\s*(?<housing>[^\s。]+)");
        if (unsuitableMatch.Success)
        {
            var optionId = unsuitableMatch.Groups["option"].Value.Trim();
            var housingId = unsuitableMatch.Groups["housing"].Value.Trim();
            return IsEnglish
                ? $"{FormatOptionLabel(FindGroupNameForOption(optionId), optionId)} cannot be installed in {FormatOptionLabel("机箱", housingId)}."
                : $"{FormatOptionLabel(FindGroupNameForOption(optionId), optionId)} 不能安装在 {FormatOptionLabel("机箱", housingId)}。";
        }

        var cannotFitMatch = Regex.Match(text, @"无法装入\s*(?<housing>[^\s]+)\s*机箱槽位");
        if (cannotFitMatch.Success)
        {
            return IsEnglish
                ? $"The selected modules cannot all fit in {FormatOptionLabel("机箱", cannotFitMatch.Groups["housing"].Value)}. Change the housing or reduce/replace modules."
                : $"当前已选模块无法全部安装到 {FormatOptionLabel("机箱", cannotFitMatch.Groups["housing"].Value)} 的槽位中，请调整机箱或减少/更换模块。";
        }

        var analogRequirementMatch = Regex.Match(text, @"^(?<housing>[^\s]+)\s*机箱要求在\s*(?<slots>[^\s]+)\s*至少安装一个模拟量模块。");
        if (analogRequirementMatch.Success)
        {
            return IsEnglish
                ? $"{FormatOptionLabel("机箱", analogRequirementMatch.Groups["housing"].Value)} requires at least one analog module in slot(s) {analogRequirementMatch.Groups["slots"].Value}."
                : $"{FormatOptionLabel("机箱", analogRequirementMatch.Groups["housing"].Value)} 要求 {analogRequirementMatch.Groups["slots"].Value} 槽位中至少安装 1 个模拟量模块。";
        }

        var slotRequirementMatch = Regex.Match(text, @"^(?<housing>[^\s]+)\s*机箱要求\s*(?<slot>[^\s]+)\s*安装：(?<modules>.+)。?");
        if (slotRequirementMatch.Success)
        {
            return IsEnglish
                ? $"{FormatOptionLabel("机箱", slotRequirementMatch.Groups["housing"].Value)} requires one of these modules in slot {slotRequirementMatch.Groups["slot"].Value}: {FormatModuleList(slotRequirementMatch.Groups["modules"].Value)}."
                : $"{FormatOptionLabel("机箱", slotRequirementMatch.Groups["housing"].Value)} 的 {slotRequirementMatch.Groups["slot"].Value} 槽位需要安装以下模块之一：{FormatModuleList(slotRequirementMatch.Groups["modules"].Value)}。";
        }

        return text;
    }

    private string FormatConditionText(string conditionText)
    {
        var parts = conditionText.Trim().TrimEnd('。')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(FormatCondition)
            .Where(part => !string.IsNullOrWhiteSpace(part));

        return string.Join(IsEnglish ? "; " : "；", parts);
    }

    private string FormatCondition(string condition)
    {
        var parts = condition.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return condition;
        }

        var groupName = parts[0];
        var values = parts[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        var positives = values
            .Where(value => !value.StartsWith('!'))
            .Select(NormalizeConditionValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        var negatives = values
            .Where(value => value.StartsWith('!'))
            .Select(NormalizeConditionValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        var clauses = new List<string>();
        if (positives.Length > 0)
        {
            clauses.Add(IsEnglish
                ? $"Select {GroupDisplayName(groupName)}: {FormatOptionList(groupName, positives)}"
                : $"需要选择 {groupName}：{FormatOptionList(groupName, positives)}");
        }

        if (negatives.Length > 0)
        {
            clauses.Add(IsEnglish
                ? $"Do not select {GroupDisplayName(groupName)} at the same time: {FormatOptionList(groupName, negatives)}"
                : $"不能同时选择 {groupName}：{FormatOptionList(groupName, negatives)}");
        }

        return string.Join(IsEnglish ? "; " : "；", clauses);
    }

    private string FormatOptionList(string groupName, IEnumerable<string> optionIds) =>
        string.Join(IsEnglish ? ", " : "、", optionIds.Select(optionId => FormatOptionLabel(groupName, optionId)));

    private string FormatModuleList(string modules)
    {
        var labels = new List<string>();
        foreach (var module in ExtractModuleTokens(modules))
        {
            var moduleOptions = FindOptionsForModule(module).ToList();
            if (moduleOptions.Count == 0)
            {
                labels.Add(module);
                continue;
            }

            labels.Add(IsEnglish
                ? $"{module} ({string.Join(" / ", moduleOptions.Select(option => option.Id))})"
                : $"{module}（{string.Join(" / ", moduleOptions.Select(option => option.Id))}）");
        }

        return labels.Count == 0 ? modules.Trim().TrimEnd('。') : string.Join(IsEnglish ? ", " : "、", labels);
    }

    private string FormatOptionLabel(string? groupName, string optionId)
    {
        var normalizedOptionId = NormalizeConditionValue(optionId) ?? optionId;
        var option = !string.IsNullOrWhiteSpace(groupName)
            ? FindOption(groupName, normalizedOptionId)
            : null;
        option ??= _rules.OptionsById.GetValueOrDefault(normalizedOptionId);

        if (option is null)
        {
            return normalizedOptionId;
        }

        var description = IsEnglish
            ? FirstNonEmpty(option.EnglishShortDescription, option.ShortDescription, option.EnglishDescription, option.Description)
            : FirstNonEmpty(option.ShortDescription, option.Description);

        return string.IsNullOrWhiteSpace(description)
            ? option.Id
            : IsEnglish ? $"{option.Id} ({description})" : $"{option.Id}（{description}）";
    }

    private void AddConditionTargets(ICollection<ValidationMessageTargetViewModel> targets, string conditionText)
    {
        foreach (var condition in conditionText.Trim().TrimEnd('。')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = condition.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            foreach (var value in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddTarget(targets, parts[0], NormalizeConditionValue(value));
            }
        }
    }

    private void AddTarget(
        ICollection<ValidationMessageTargetViewModel> targets,
        string? groupName,
        string? optionId)
    {
        optionId = NormalizeConditionValue(optionId);

        if (string.IsNullOrWhiteSpace(groupName) && !string.IsNullOrWhiteSpace(optionId))
        {
            groupName = FindGroupNameForOption(optionId);
        }

        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        var normalizedGroupName = FindGroup(groupName)?.Name ?? groupName.Trim();
        if (!string.IsNullOrWhiteSpace(optionId) &&
            !GroupContainsOption(normalizedGroupName, optionId))
        {
            var inferredGroupName = FindGroupNameForOption(optionId);
            if (!string.IsNullOrWhiteSpace(inferredGroupName))
            {
                normalizedGroupName = inferredGroupName;
            }
        }

        if (targets.Any(target =>
                target.GroupName.Equals(normalizedGroupName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.OptionId, optionId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        targets.Add(new ValidationMessageTargetViewModel(
            normalizedGroupName,
            string.IsNullOrWhiteSpace(optionId) ? null : optionId,
            GroupDisplayName(normalizedGroupName)));
    }

    private string GroupDisplayName(string groupName)
    {
        var group = FindGroup(groupName);
        if (group is null)
        {
            return groupName;
        }

        return IsEnglish && !string.IsNullOrWhiteSpace(group.EnglishName) ? group.EnglishName : group.Name;
    }

    private string? FindGroupNameForOption(string optionId) =>
        _rules.OptionsById.TryGetValue(optionId, out var option) ? option.GroupName : null;

    private RuleOption? FindOption(string groupName, string optionId) =>
        _rules.MainGroups.Concat(_rules.OptionGroups)
            .Where(group => group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(group => group.Options)
            .FirstOrDefault(option => option.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<RuleOption> FindOptionsForModule(string moduleType) =>
        _rules.OptionGroups
            .SelectMany(group => group.Options)
            .Where(option => option.ModuleType?.Equals(moduleType, StringComparison.OrdinalIgnoreCase) == true);

    private bool GroupContainsOption(string groupName, string optionId) =>
        _rules.MainGroups.Concat(_rules.OptionGroups)
            .Where(group => group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(group => group.Options)
            .Any(option => option.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ExtractModuleTokens(string value) =>
        value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim().TrimEnd('。'))
            .Where(token => Regex.IsMatch(token, "^[A-Z]+[0-9]+$", RegexOptions.IgnoreCase));

    private static string? NormalizeConditionValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimStart('!').Trim().TrimEnd('。', '，', ',', ';', '；');
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private bool IsOptionAvailable(
        RuleOption option,
        IReadOnlyCollection<RuleOption> selectedOptions,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
        var candidate = CloneSelectedByGroup(selectedByGroup);
        var group = FindGroup(option.GroupName);
        if (group is null)
        {
            return true;
        }

        if (group.IsMainCode)
        {
            return true;
        }

        if (group.AllowsMultiple(GetSelectedVersion(candidate)))
        {
            if (!candidate.TryGetValue(option.GroupName, out var selected))
            {
                selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                candidate[option.GroupName] = selected;
            }

            selected.Add(option.Id);
        }
        else
        {
            candidate[option.GroupName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { option.Id };
        }

        if (!EvaluateValidity(option, candidate))
        {
            return false;
        }

        foreach (var selectedOption in selectedOptions)
        {
            if (selectedOption.IsMainCode)
            {
                continue;
            }

            if (!candidate.TryGetValue(selectedOption.GroupName, out var selected) ||
                !selected.Contains(selectedOption.Id))
            {
                continue;
            }

            if (!EvaluateValidity(selectedOption, selectedByGroup))
            {
                continue;
            }

            if (!EvaluateValidity(selectedOption, candidate))
            {
                return false;
            }
        }

        return true;
    }

    private OptionGroup? FindGroup(string groupName) =>
        _rules.MainGroups.Concat(_rules.OptionGroups)
            .FirstOrDefault(group => group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, HashSet<string>> BuildSelectedByGroup(IEnumerable<RuleOption> selectedOptions)
    {
        var selectedByGroup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in selectedOptions)
        {
            if (!selectedByGroup.TryGetValue(option.GroupName, out var selected))
            {
                selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                selectedByGroup[option.GroupName] = selected;
            }

            selected.Add(option.Id);
        }

        return selectedByGroup;
    }

    private static Dictionary<string, HashSet<string>> CloneSelectedByGroup(
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup) =>
        selectedByGroup.ToDictionary(
            pair => pair.Key,
            pair => new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    private static string? GetSelectedVersion(IReadOnlyDictionary<string, HashSet<string>> selectedByGroup) =>
        selectedByGroup.TryGetValue("版本", out var selectedVersion) && selectedVersion.Count == 1
            ? selectedVersion.First()
            : null;

    private static bool EvaluateValidity(
        RuleOption option,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
        if (string.IsNullOrWhiteSpace(option.Validity))
        {
            return true;
        }

        return option.Validity.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(condition => EvaluateCondition(condition, selectedByGroup));
    }

    private static bool EvaluateRequires(
        RuleOption option,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
        if (!option.Attributes.TryGetValue("Requires", out var requires) || string.IsNullOrWhiteSpace(requires))
        {
            return true;
        }

        return requires.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(condition => EvaluateCondition(condition, selectedByGroup));
    }

    private static bool IsMissingRequiredOption(
        RuleOption option,
        IReadOnlyCollection<RuleOption> selectedOptions,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
        if (selectedOptions.Any(selected => selected.Id.Equals(option.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        foreach (var selected in selectedOptions)
        {
            if (!selected.Attributes.TryGetValue("Requires", out var requires) || string.IsNullOrWhiteSpace(requires))
            {
                continue;
            }

            foreach (var condition in requires.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (EvaluateCondition(condition, selectedByGroup))
                {
                    continue;
                }

                var parts = condition.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                var requiredIds = parts[1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(value => !value.StartsWith('!'));

                if (requiredIds.Any(required => required.Equals(option.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool EvaluateCondition(
        string condition,
        IReadOnlyDictionary<string, HashSet<string>> selectedByGroup)
    {
        var parts = condition.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return true;
        }

        selectedByGroup.TryGetValue(parts[0], out var selected);
        selected ??= [];

        var values = parts[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        var positives = values.Where(value => !value.StartsWith('!')).ToArray();
        var negatives = values.Where(value => value.StartsWith('!')).Select(value => value[1..]).ToArray();

        if (positives.Length > 0 && !positives.Any(selected.Contains))
        {
            return false;
        }

        return negatives.All(value => !selected.Contains(value));
    }

    private static void Replace(ObservableCollection<ValidationMessageViewModel> target, IEnumerable<ValidationMessageViewModel> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static void Replace(ObservableCollection<SlotViewModel> target, IEnumerable<SlotViewModel> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static void Replace(ObservableCollection<IoSummaryItemViewModel> target, IEnumerable<IoSummaryItemViewModel> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static void Replace(ObservableCollection<FunctionSuggestionViewModel> target, IEnumerable<FunctionSuggestionViewModel> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static void Replace(ObservableCollection<AppRecommendationViewModel> target, IEnumerable<AppRecommendationViewModel> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static string ResolveDataPath()
    {
        var basePath = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(basePath, "Data", "REX615_ROL.xml"),
            Path.Combine(Environment.CurrentDirectory, "Data", "REX615_ROL.xml"),
            Path.Combine(Environment.CurrentDirectory, "REX615_ROL.xml")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("找不到本地数据包 REX615_ROL.xml。");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "REX615" : safe;
    }
}
