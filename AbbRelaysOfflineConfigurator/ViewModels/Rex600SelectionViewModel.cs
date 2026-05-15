using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class Rex600SelectionViewModel : ObservableObject
{
    private readonly Rex600RuleSet _rules = new Rex600RuleLoader().Load();
    private readonly OnlineValidationService _onlineValidationService = new();
    private string _orderCode = "";
    private string _status = "";
    private string _onlineStatus = "未校验";
    private string _onlineOrderingNumber = "";
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private bool _isValid;
    private bool _isOnlineValidationBusy;
    private bool _isOnlineValidationSuccess;
    private bool _isOnlineValidationError;

    public Rex600SelectionViewModel()
    {
        Groups = new ObservableCollection<Rex600GroupViewModel>(
            _rules.Groups.Select(group => new Rex600GroupViewModel(this, group)));
        Messages = [];
        SelectedSummaryItems = [];
        IoSummaryItems = [];

        CopyOrderCodeCommand = new RelayCommand(CopyOrderCode, () => !string.IsNullOrWhiteSpace(OrderCode));
        CopyOrderingNumberCommand = new RelayCommand(CopyOrderingNumber, () => HasOnlineOrderingNumber);
        OnlineValidateCommand = new RelayCommand(
            () => _ = ValidateOnlineAsync(),
            () => !IsOnlineValidationBusy && !string.IsNullOrWhiteSpace(OrderCode));
        ImportOrderCodeCommand = new RelayCommand(ImportOrderCode);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription, () => !string.IsNullOrWhiteSpace(OrderCode));
        ResetCommand = new RelayCommand(Reset);
        ExpandAllCommand = new RelayCommand(() => SetAllGroupsExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllGroupsExpanded(false));

        Reset();
    }

    public ObservableCollection<Rex600GroupViewModel> Groups { get; }
    public ObservableCollection<ValidationMessageViewModel> Messages { get; }
    public ObservableCollection<Rex600SelectedSummaryItemViewModel> SelectedSummaryItems { get; }
    public ObservableCollection<IoSummaryItemViewModel> IoSummaryItems { get; }
    public RelayCommand CopyOrderCodeCommand { get; }
    public RelayCommand CopyOrderingNumberCommand { get; }
    public RelayCommand OnlineValidateCommand { get; }
    public RelayCommand ImportOrderCodeCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }
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
            }
        }
    }

    public string PageTitle => IsEnglish ? "REX600 Configuration Rules" : "REX600 选型规则";

    public string SourceSummary => IsEnglish
        ? "The order code logic is based on REX600_1.0.xml. Select each position to generate the complete REX600 order code."
        : "订货码逻辑基于 REX600_1.0.xml。按位选择后实时生成完整 REX600 订货号。";

    public string ExpandAllText => IsEnglish ? "Expand all" : "全部展开";
    public string CollapseAllText => IsEnglish ? "Collapse all" : "全部折叠";
    public string OrderCodeTitle => IsEnglish ? "REX600 order code" : "REX600 订货号";
    public string ImportOrderCodeText => IsEnglish ? "Import order code" : "导入订货号";
    public string CopyOrderCodeText => IsEnglish ? "Copy order code" : "复制订货号";
    public string OnlineValidateText => IsEnglish ? "Online check" : "在线校验";
    public string OnlineStatusTitle => IsEnglish ? "Online check" : "在线校验";
    public string OrderingNumberTitle => IsEnglish ? "Ordering number" : "订货号";
    public string CopyOrderingNumberText => IsEnglish ? "Copy ordering number" : "复制订货号";
    public string DeviceDescriptionText => IsEnglish ? "Device description" : "装置描述";
    public string ResetText => IsEnglish ? "Reset" : "重置";
    public string FunctionCatalogText => IsEnglish ? "Function catalog" : "功能清单";
    public string IoSummaryTitle => IsEnglish ? "I/O summary" : "I/O 摘要";
    public string SelectedSummaryTitle => IsEnglish ? "Current selection summary" : "当前选型摘要";
    public string ValidationMessagesTitle => IsEnglish ? "Validation messages" : "校验消息";

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

    public void Reset()
    {
        ApplyOrderCode(_rules.DefaultOrderCode);
        Recalculate();
    }

    internal void HandleSelectionChanged(Rex600GroupViewModel changedGroup, Rex600OptionViewModel changedOption)
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
        EnsureSingleSelection();
        NormalizeUnsupportedSelections();
        OrderCode = BuildOrderCode();
        var selectedByGroup = SelectedByGroup();
        var selectedVersion = CurrentVersion(selectedByGroup);
        var messages = Validate(selectedVersion).ToList();

        IsValid = messages.Count == 0;
        Status = IsValid
            ? IsEnglish ? "REX600 order code valid" : "REX600 订货号有效"
            : IsEnglish ? "REX600 order code needs adjustment" : "REX600 订货号需要调整";

        Replace(Messages, IsValid
            ? [new ValidationMessageViewModel(IsEnglish ? "Offline validation passed" : "离线校验通过", [], isSuccess: true)]
            : messages);
        Replace(SelectedSummaryItems, BuildSelectedSummary(selectedVersion));
        RefreshIoSummary(selectedByGroup);
        UpdateOptionStates(selectedVersion);
    }

    private void RefreshIoSummary(IReadOnlyDictionary<string, Rex600OptionViewModel> selectedByGroup)
    {
        IoSummaryItems.Clear();

        if (selectedByGroup.TryGetValue("Aios", out var analogInputs) &&
            analogInputs.Id.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            IoSummaryItems.Add(new IoSummaryItemViewModel(
                IsEnglish ? "Combi sensors" : "组合传感器",
                IsEnglish ? "3 channels, Rogowski/LPCT + capacitive/resistive divider" : "3 路，Rogowski/LPCT + 电容/电阻分压器"));
            IoSummaryItems.Add(new IoSummaryItemViewModel(
                IsEnglish ? "Residual current input" : "零序电流输入",
                "I0 x 1"));
        }

        if (selectedByGroup.TryGetValue("Bios", out var binaryIo) &&
            binaryIo.Id.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            IoSummaryItems.Add(new IoSummaryItemViewModel("BI", "6"));
            IoSummaryItems.Add(new IoSummaryItemViewModel("BO", "3"));
        }

        if (selectedByGroup.TryGetValue("Languages", out var communication) &&
            communication.Id.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            IoSummaryItems.Add(new IoSummaryItemViewModel(
                IsEnglish ? "Communication" : "通信接口",
                "3 x RJ-45 LAN"));
        }

        if (selectedByGroup.TryGetValue("Reserved2", out var powerSupply) &&
            powerSupply.Id.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            IoSummaryItems.Add(new IoSummaryItemViewModel(
                IsEnglish ? "Power supply" : "电源",
                "24 VDC"));
        }
    }

    private IReadOnlyList<string> ApplyOrderCode(string orderCode)
    {
        var code = (orderCode ?? "").Trim().ToUpperInvariant();
        var notFound = new List<string>();

        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                option.SetSelectedSilently(false);
            }

            var segment = SegmentForLocation(code, group.Location);
            var target = group.Options.FirstOrDefault(option => option.Id.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                notFound.Add(IsEnglish
                    ? $"Position {group.Location} {group.DisplayName}: {segment}"
                    : $"第 {group.Location} 位 {group.DisplayName}: {segment}");
                target = PreferredOptionForGroup(group, CurrentVersion(SelectedByGroup())) ?? group.Options.FirstOrDefault();
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

    private Rex600OptionViewModel? PreferredOptionForGroup(Rex600GroupViewModel group, string version)
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

    private Dictionary<string, Rex600OptionViewModel> SelectedByGroup() =>
        Groups
            .Select(group => (group.Name, Option: group.SelectedOption))
            .Where(item => item.Option is not null)
            .ToDictionary(item => item.Name, item => item.Option!, StringComparer.OrdinalIgnoreCase);

    private string CurrentVersion(IReadOnlyDictionary<string, Rex600OptionViewModel> selectedByGroup) =>
        selectedByGroup.TryGetValue("Versions", out var selectedVersion) ? selectedVersion.Id : "1G";

    private IEnumerable<ValidationMessageViewModel> Validate(string version)
    {
        foreach (var group in Groups)
        {
            var selected = group.SelectedOption;
            if (selected is null)
            {
                yield return CreateValidationMessage(
                    IsEnglish ? $"{group.DisplayName} is not selected." : $"{group.DisplayName} 未选择。",
                    group);
                continue;
            }

            if (!selected.Option.SupportsVersion(version))
            {
                yield return CreateValidationMessage(
                    IsEnglish
                        ? $"{group.DisplayName} / {selected.Id} is not available for product version {version}."
                        : $"{group.DisplayName} / {selected.Id} 不适用于产品版本 {version}。",
                    group);
            }
        }

        if (OrderCode.Length != 18)
        {
            yield return new ValidationMessageViewModel(
                IsEnglish ? "The REX600 order code must contain 18 characters." : "REX600 订货号必须为 18 位。",
                Groups.Select(group => new ValidationMessageTargetViewModel(group.DisplayName, null)));
        }
    }

    private ValidationMessageViewModel CreateValidationMessage(string text, Rex600GroupViewModel group) =>
        new(text, [new ValidationMessageTargetViewModel(group.DisplayName, null)]);

    private void UpdateOptionStates(string version)
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

    private IEnumerable<Rex600SelectedSummaryItemViewModel> BuildSelectedSummary(string version)
    {
        yield return new Rex600SelectedSummaryItemViewModel(
            IsEnglish ? "Product version" : "产品版本",
            $"{version} / IED {_rules.Version(version)?.IedVersion}");

        foreach (var group in Groups.Where(group => !group.Name.Equals("Versions", StringComparison.OrdinalIgnoreCase)))
        {
            var option = group.SelectedOption;
            if (option is null)
            {
                continue;
            }

            if (option.Id.Equals("N", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new Rex600SelectedSummaryItemViewModel(group.DisplayName, $"{option.Id}: {option.Description}");
        }
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
        if (!string.IsNullOrWhiteSpace(OrderCode))
        {
            Clipboard.SetText(OrderCode);
        }
    }

    private void CopyOrderingNumber()
    {
        if (!string.IsNullOrWhiteSpace(OnlineOrderingNumber))
        {
            Clipboard.SetText(OnlineOrderingNumber);
        }
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
            IsEnglish ? "REX600 device description" : "REX600 装置描述",
            IsEnglish ? $"Order code: {OrderCode}" : $"订货号：{OrderCode}",
            IsEnglish ? $"Online check: {OnlineStatus}" : $"在线校验：{OnlineStatus}",
            IsEnglish ? $"Status: {Status}" : $"状态：{Status}",
            ""
        };

        lines.Add(IsEnglish ? "Current selection:" : "当前选型：");
        lines.AddRange(SelectedSummaryItems.Select(item => $"{item.Name}: {item.Value}"));

        lines.Add("");
        lines.Add(IsEnglish ? "I/O summary:" : "I/O 摘要：");
        lines.Add(IoSummaryItems.Count == 0
            ? IsEnglish ? "None" : "无"
            : string.Join(IsEnglish ? "; " : "；", IoSummaryItems.Select(item => $"{item.Name}={item.Value}")));

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
        var window = new CombinationCodeImportWindow(
            IsEnglish ? "Import REX600 order code" : "导入 REX600 订货号",
            IsEnglish
                ? "Enter a complete 18-character REX600 order code."
                : "请输入完整 18 位 REX600 订货号。",
            IsEnglish ? "Import" : "导入",
            _rules.DefaultOrderCode);

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
                IsEnglish ? "REX600 order code must contain 18 characters." : "REX600 订货号必须为 18 位。",
                IsEnglish ? "REX600 Configurator" : "REX600 选型",
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
                    : $"订货号已导入，但以下位段未匹配，已按默认项回填：{Environment.NewLine}{string.Join(Environment.NewLine, notFound)}",
                IsEnglish ? "REX600 Configurator" : "REX600 选型",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RefreshStaticText()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(ExpandAllText));
        OnPropertyChanged(nameof(CollapseAllText));
        OnPropertyChanged(nameof(OrderCodeTitle));
        OnPropertyChanged(nameof(ImportOrderCodeText));
        OnPropertyChanged(nameof(CopyOrderCodeText));
        OnPropertyChanged(nameof(OnlineValidateText));
        OnPropertyChanged(nameof(OnlineStatusTitle));
        OnPropertyChanged(nameof(OrderingNumberTitle));
        OnPropertyChanged(nameof(CopyOrderingNumberText));
        OnPropertyChanged(nameof(DeviceDescriptionText));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(FunctionCatalogText));
        OnPropertyChanged(nameof(IoSummaryTitle));
        OnPropertyChanged(nameof(SelectedSummaryTitle));
        OnPropertyChanged(nameof(ValidationMessagesTitle));
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

public sealed class Rex600GroupViewModel : ObservableObject
{
    private readonly Rex600SelectionViewModel _owner;
    private bool _isExpanded = true;
    private int _errorCount;

    public Rex600GroupViewModel(Rex600SelectionViewModel owner, Rex600GroupRule group)
    {
        _owner = owner;
        Group = group;
        Options = new ObservableCollection<Rex600OptionViewModel>(
            group.Options.Select(option => new Rex600OptionViewModel(this, option)));
    }

    public Rex600GroupRule Group { get; }
    public string Name => Group.Name;
    public string DisplayName => _owner.IsEnglish ? Group.DisplayNameEnglish : Group.DisplayName;
    public string Location => Group.Location;
    public int SortOrder => Group.SortOrder;
    public ObservableCollection<Rex600OptionViewModel> Options { get; }
    public Rex600OptionViewModel? SelectedOption => Options.FirstOrDefault(option => option.IsSelected);
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

    internal void HandleSelectionChanged(Rex600OptionViewModel option)
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

public sealed class Rex600OptionViewModel(Rex600GroupViewModel group, Rex600OptionRule option) : ObservableObject
{
    private bool _isSelected;
    private bool _isAvailable = true;
    private bool _hasError;

    public Rex600GroupViewModel Group { get; } = group;
    public Rex600OptionRule Option { get; } = option;
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

public sealed record Rex600SelectedSummaryItemViewModel(string Name, string Value);
