using System.Collections.ObjectModel;
using System.Windows;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

// 负责 RE_630 系列装置/版本规则切换、18 位订货码导入、依赖选项收敛和离线完整性校验。
// _preferredCodes 仅表达重置或导入时希望保留的位段，最终选择必须经过当前 BasicCode 下的依赖规则过滤。
public sealed class Re630SelectionViewModel : ObservableObject
{
    private const string DefaultOrderCode = "TBMNAAAAAAAZNNNAXD";
    private readonly Re630RuleCatalog _catalog = new Re630RuleLoader().Load();
    private readonly Dictionary<string, string> _preferredCodes = new(StringComparer.OrdinalIgnoreCase);
    private Re630RuleSet? _selectedRuleSet;
    private string _selectedDeviceFilter = "";
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private string _orderCode = "";
    private string _status = "";
    private bool _isValid;
    private bool _isRefreshing;

    public Re630SelectionViewModel()
    {
        RuleSets = new ObservableCollection<Re630RuleSet>(_catalog.RuleSets);
        DeviceFilters = new ObservableCollection<string>(
            _catalog.RuleSets.Select(ruleSet => ruleSet.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase));
        Groups = [];
        OrderCodeSegments = [];
        Messages = [];

        CopyOrderCodeCommand = new RelayCommand(CopyOrderCode, () => !string.IsNullOrWhiteSpace(OrderCode));
        ImportOrderCodeCommand = new RelayCommand(ImportOrderCode);
        ResetCommand = new RelayCommand(Reset);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription, () => !string.IsNullOrWhiteSpace(OrderCode));

        SelectedDeviceFilter = DeviceFilters.FirstOrDefault(device => device.Equals("REM630", StringComparison.OrdinalIgnoreCase))
                               ?? DeviceFilters.FirstOrDefault()
                               ?? "";
        SelectedRuleSet = RuleSets.FirstOrDefault(ruleSet =>
                              ruleSet.DeviceId.Equals(SelectedDeviceFilter, StringComparison.OrdinalIgnoreCase) &&
                              ruleSet.VersionDescription.Contains("1.3", StringComparison.OrdinalIgnoreCase))
                          ?? RuleSets.FirstOrDefault(ruleSet =>
                              ruleSet.DeviceId.Equals(SelectedDeviceFilter, StringComparison.OrdinalIgnoreCase))
                          ?? RuleSets.FirstOrDefault();
        ApplyOrderCode(DefaultOrderCode);
    }

    public ObservableCollection<Re630RuleSet> RuleSets { get; }
    public ObservableCollection<string> DeviceFilters { get; }
    public ObservableCollection<Re630GroupViewModel> Groups { get; }
    public ObservableCollection<Re630OrderCodeSegmentViewModel> OrderCodeSegments { get; }
    public ObservableCollection<ValidationMessageViewModel> Messages { get; }
    public RelayCommand CopyOrderCodeCommand { get; }
    public RelayCommand ImportOrderCodeCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }

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

            OnPropertyChanged(nameof(FilteredRuleSets));
            if (SelectedRuleSet is null ||
                !SelectedRuleSet.DeviceId.Equals(_selectedDeviceFilter, StringComparison.OrdinalIgnoreCase))
            {
                SelectedRuleSet = FilteredRuleSets.LastOrDefault() ?? RuleSets.FirstOrDefault();
            }
        }
    }

    public Re630RuleSet? SelectedRuleSet
    {
        get => _selectedRuleSet;
        set
        {
            if (!SetProperty(ref _selectedRuleSet, value) || value is null)
            {
                return;
            }

            // 规则集切换会重建全部组选项并应用该版本默认值，不能沿用旧版本中的 Option 实例。
            if (!value.DeviceId.Equals(SelectedDeviceFilter, StringComparison.OrdinalIgnoreCase))
            {
                _selectedDeviceFilter = value.DeviceId;
                OnPropertyChanged(nameof(SelectedDeviceFilter));
                OnPropertyChanged(nameof(FilteredRuleSets));
            }

            LoadGroups(value);
            Reset();
            RefreshStaticText();
        }
    }

    public string PageTitle => IsEnglish ? "RE_630 Configurator" : "RE_630 选型";
    public string SourceSummary => IsEnglish
        ? $"Rule source: {SelectedRuleSet?.FileName ?? ""}"
        : $"规则来源：{SelectedRuleSet?.FileName ?? ""}";
    public string DeviceText => IsEnglish ? "Device" : "装置";
    public string VersionText => IsEnglish ? "Version" : "版本";
    public string ImportText => IsEnglish ? "Import" : "导入";
    public string CopyText => IsEnglish ? "Copy" : "复制";
    public string ResetText => IsEnglish ? "Reset" : "重置";
    public string DeviceDescriptionText => IsEnglish ? "Device description" : "装置描述";
    public string FunctionCatalogShortText => IsEnglish ? "Code table" : "代码表";
    public string OrderCodeTitle => IsEnglish ? "Order Code" : "订货号";
    public string SelectionTitle => IsEnglish ? "Order code selection" : "订货号选型";
    public string ValidationTitle => IsEnglish ? "Validation" : "校验";

    public IEnumerable<Re630RuleSet> FilteredRuleSets =>
        RuleSets.Where(ruleSet => string.IsNullOrWhiteSpace(SelectedDeviceFilter) ||
                                  ruleSet.DeviceId.Equals(SelectedDeviceFilter, StringComparison.OrdinalIgnoreCase));

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

    internal void HandleSelectionChanged()
    {
        if (_isRefreshing)
        {
            return;
        }

        RefreshSelection();
    }

    private void LoadGroups(Re630RuleSet ruleSet)
    {
        Groups.Clear();
        foreach (var group in ruleSet.Groups.OrderBy(group => group.SortOrder))
        {
            Groups.Add(new Re630GroupViewModel(this, group));
        }
    }

    private void Reset()
    {
        // 重置先建立各位段默认偏好，再交给统一收敛管线处理依赖关系，而不是直接逐项强制选中。
        _preferredCodes.Clear();
        if (SelectedRuleSet is not null)
        {
            foreach (var group in SelectedRuleSet.Groups)
            {
                _preferredCodes[group.Digit] = PreferredDefaultCode(group.Digit);
            }
        }

        RefreshSelection();
    }

    private void ImportOrderCode()
    {
        var window = new CombinationCodeImportWindow(
            IsEnglish ? "Import RE_630 order code" : "导入 RE_630 订货号",
            IsEnglish
                ? "Enter an 18-character RE_630 order code. Example: TBMNAAAAAAAZNNNAXD."
                : "输入 18 位 RE_630 订货号。例如：TBMNAAAAAAAZNNNAXD。",
            IsEnglish ? "Import" : "导入",
            DefaultOrderCode)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        var code = NormalizeOrderCode(window.CombinationCode);
        if (code.Length != 18)
        {
            MessageBox.Show(
                IsEnglish ? "RE_630 order code must contain 18 characters." : "RE_630 订货号必须为 18 位。",
                IsEnglish ? "Import RE_630 order code" : "导入 RE_630 订货号",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ApplyOrderCode(code);
            return;
        }

        ApplyOrderCode(code);
    }

    private void ApplyOrderCode(string value)
    {
        // 导入码中的装置位和版本位先决定规则集，再按每组 CodeLength 拆分偏好值，最后一次性刷新选择。
        // 因此跨装置导入不会在旧规则组上解释新代码，也不会把拆分过程的中间态暴露给界面。
        var code = NormalizeOrderCode(value);
        if (code.Length != 18)
        {
            ReplaceMessages([new ValidationMessageViewModel(
                IsEnglish ? "RE_630 order code must contain 18 characters." : "RE_630 订货号必须为 18 位。",
                [],
                isSuccess: false)]);
            IsValid = false;
            RefreshStatus();
            return;
        }

        var targetRuleSet = FindRuleSetForOrderCode(code);
        if (targetRuleSet is not null && !ReferenceEquals(targetRuleSet, SelectedRuleSet))
        {
            _selectedRuleSet = targetRuleSet;
            OnPropertyChanged(nameof(SelectedRuleSet));
            SelectedDeviceFilter = targetRuleSet.DeviceId;
            OnPropertyChanged(nameof(SelectedDeviceFilter));
            OnPropertyChanged(nameof(FilteredRuleSets));
            LoadGroups(targetRuleSet);
        }

        _preferredCodes.Clear();
        var index = 0;
        foreach (var group in Groups)
        {
            _preferredCodes[group.Digit] = code.Substring(index, group.CodeLength);
            index += group.CodeLength;
        }

        RefreshSelection();
    }

    private Re630RuleSet? FindRuleSetForOrderCode(string code)
    {
        // 第 3 位装置和第 18 位版本优先共同定位规则集；未匹配时保留当前规则集，
        // 再由后续位段告警和完整性校验提示用户复核。
        var deviceCode = code[2].ToString();
        var versionCode = code[17].ToString();
        return RuleSets.FirstOrDefault(ruleSet =>
                   ruleSet.DeviceCode.Equals(deviceCode, StringComparison.OrdinalIgnoreCase) &&
                   ruleSet.VersionCode.Equals(versionCode, StringComparison.OrdinalIgnoreCase))
               ?? SelectedRuleSet;
    }

    private void RefreshSelection()
    {
        // 各位段候选会随 BasicCode 和其他依赖位变化，最多迭代 20 轮直至选项集合与选中值都稳定。
        // 收敛过程中抑制组选中事件，结束后统一生成代码、位段摘要、校验和状态，避免递归重算。
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
                var selectedCodes = SelectedCodes();
                var basicCode = BasicCode(selectedCodes);

                foreach (var group in Groups)
                {
                    var preferredCode = _preferredCodes.TryGetValue(group.Digit, out var preferred)
                        ? preferred
                        : group.SelectedOption?.Code;
                    var options = AvailableOptions(group.Rule.Digit, basicCode, selectedCodes).ToList();
                    changed |= group.ReplaceOptions(options, preferredCode);
                    selectedCodes[group.Rule.Digit] = group.SelectedOption?.Code ?? "";
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
            _preferredCodes.Clear();
        }

        OrderCode = BuildOrderCode();
        RefreshSegments();
        ValidateCurrentSelection();
        RefreshStatus();
        OnPropertyChanged(nameof(SourceSummary));
    }

    private IReadOnlyList<Re630OptionRule> AvailableOptions(
        string digit,
        string basicCode,
        IReadOnlyDictionary<string, string> selectedCodes)
    {
        // 先取得 BasicCode 对应的基础候选，再将所有已命中的依赖规则取交集；多条限制必须同时满足。
        if (SelectedRuleSet is null)
        {
            return [];
        }

        var options = SelectedRuleSet.OptionsFor(digit, basicCode);
        var restrictions = SelectedRuleSet.Rules
            .Where(rule => rule.BasicCode.Equals(basicCode, StringComparison.OrdinalIgnoreCase) &&
                           rule.DependentDigit.Equals(digit, StringComparison.OrdinalIgnoreCase) &&
                           selectedCodes.TryGetValue(rule.Digit, out var selectedCode) &&
                           rule.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (restrictions.Count == 0)
        {
            return options;
        }

        var allowed = new HashSet<string>(options.Select(option => option.Code), StringComparer.OrdinalIgnoreCase);
        foreach (var restriction in restrictions)
        {
            allowed.IntersectWith(restriction.PossibleCodes);
        }

        return options.Where(option => allowed.Contains(option.Code)).ToList();
    }

    private void ValidateCurrentSelection()
    {
        // 经过依赖过滤后，最终有效性要求每组都有选项且拼接结果恰好为 18 位。
        var messages = new List<ValidationMessageViewModel>();
        var code = BuildOrderCode();
        if (code.Length != 18)
        {
            messages.Add(new ValidationMessageViewModel(
                IsEnglish ? "The current RE_630 order code is incomplete." : "当前 RE_630 订货号不完整。",
                [],
                isSuccess: false));
        }

        foreach (var group in Groups.Where(group => group.SelectedOption is null))
        {
            messages.Add(new ValidationMessageViewModel(
                IsEnglish
                    ? $"{group.Title} has no valid option for the current combination."
                    : $"{group.Title} 当前组合下无有效选项。",
                [],
                isSuccess: false));
        }

        IsValid = messages.Count == 0;
        ReplaceMessages(IsValid
            ? [new ValidationMessageViewModel(IsEnglish ? "Offline validation passed" : "离线校验通过", [], isSuccess: true)]
            : messages);
    }

    private Dictionary<string, string> SelectedCodes()
    {
        return Groups.ToDictionary(
            group => group.Rule.Digit,
            group => group.SelectedOption?.Code ?? "",
            StringComparer.OrdinalIgnoreCase);
    }

    private static string BasicCode(IReadOnlyDictionary<string, string> selectedCodes) =>
        $"{ValueAt(selectedCodes, "1")}{ValueAt(selectedCodes, "2")}{ValueAt(selectedCodes, "3")}";

    private static string ValueAt(IReadOnlyDictionary<string, string> selectedCodes, string digit) =>
        selectedCodes.TryGetValue(digit, out var value) ? value : "";

    private string BuildOrderCode() => string.Concat(Groups.Select(group => group.SelectedOption?.Code ?? ""));

    private void RefreshSegments()
    {
        OrderCodeSegments.Clear();
        foreach (var group in Groups)
        {
            OrderCodeSegments.Add(new Re630OrderCodeSegmentViewModel(
                group.Rule.Digit,
                group.SelectedOption?.Code ?? "",
                group.Title));
        }
    }

    private void RefreshStatus()
    {
        Status = IsValid
            ? IsEnglish ? "RE_630 order code valid" : "RE_630 订货号有效"
            : IsEnglish ? "RE_630 order code needs adjustment" : "RE_630 订货号需要调整";
    }

    private void CopyOrderCode()
    {
        if (string.IsNullOrWhiteSpace(OrderCode))
        {
            return;
        }

        ClipboardService.TrySetText(OrderCode, "RE_630", IsEnglish);
        Status = IsEnglish ? "RE_630 order code copied." : "RE_630 订货号已复制。";
    }

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
        OnPropertyChanged(nameof(FunctionCatalogShortText));
        OnPropertyChanged(nameof(OrderCodeTitle));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(ValidationTitle));

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
            IsEnglish ? "RE_630 device description" : "RE_630 装置描述",
            IsEnglish ? $"Order code: {OrderCode}" : $"订货号：{OrderCode}",
            IsEnglish ? $"Device: {SelectedRuleSet?.DeviceId ?? ""}" : $"装置：{SelectedRuleSet?.DeviceId ?? ""}",
            IsEnglish ? $"Version: {SelectedRuleSet?.VersionDescription ?? ""}" : $"版本：{LocalizeOptionDescription(SelectedRuleSet?.VersionDescription ?? "")}",
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

        if (Messages.Count > 0)
        {
            lines.Add("");
            lines.Add(IsEnglish ? "Validation messages:" : "校验提示：");
            lines.AddRange(Messages.Select(message => message.Text));
        }

        return string.Join(Environment.NewLine, lines);
    }

    internal string LocalizeGroupTitle(string title)
    {
        if (IsEnglish)
        {
            return title;
        }

        return Re630GroupTitleTranslations.TryGetValue(title, out var localized) ? localized : title;
    }

    internal string LocalizeOptionDescription(string description)
    {
        if (IsEnglish || string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        return Re630OptionDescriptionTranslations.TryGetValue(description, out var localized)
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

    private static readonly IReadOnlyDictionary<string, string> Re630GroupTitleTranslations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Product series, size"] = "产品系列，尺寸",
            ["Market type (Standard)"] = "市场类型（标准）",
            ["Main application"] = "主应用",
            ["Functional application"] = "功能应用",
            ["Analog inputs/outputs"] = "模拟量输入/输出",
            ["Binary inputs/outputs"] = "开关量输入/输出",
            ["Communication serial"] = "串行通信",
            ["Communication Ethernet"] = "以太网通信",
            ["Communication protocol"] = "通信协议",
            ["Language"] = "语言",
            ["Front panel"] = "前面板",
            ["Option 1"] = "选项 1",
            ["Option 2"] = "选项 2",
            ["Power supply"] = "电源",
            ["Vacant"] = "空位",
            ["Version"] = "版本"
        };

    private static readonly IReadOnlyDictionary<string, string> Re630OptionDescriptionTranslations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RE_630, 4U half 19 inch housing"] = "RE_630，4U 半 19 英寸机箱",
            ["RE_630, 6U half 19 inch housing"] = "RE_630，6U 半 19 英寸机箱",
            ["RE_630, 4U half 19 inch housing and connector set"] = "RE_630，4U 半 19 英寸机箱及连接器套件",
            ["RE_630, 6U half 19 inch housing and connector set"] = "RE_630，6U 半 19 英寸机箱及连接器套件",
            ["IEC"] = "IEC",
            ["Feeder protection and control"] = "馈线保护与控制",
            ["Generator protection and control"] = "发电机保护与控制",
            ["Motor protection and control"] = "电动机保护与控制",
            ["Transformer protection and control"] = "变压器保护与控制",
            ["Pre-configuration A"] = "预配置 A",
            ["Pre-configuration B"] = "预配置 B",
            ["Pre-configuration C"] = "预配置 C",
            ["Pre-configuration D"] = "预配置 D",
            ["No pre-configuration"] = "无预配置",
            ["4I + 5U (Io 1/5A)"] = "4I + 5U（Io 1/5A）",
            ["5I + 4U (Io 0.1/0.5A)"] = "5I + 4U（Io 0.1/0.5A）",
            ["7I + 3U (Io 1/5A)"] = "7I + 3U（Io 1/5A）",
            ["8I + 2U (Io 1/5A)"] = "8I + 2U（Io 1/5A）",
            ["4I + 5U (Io 0.1/0.5A)"] = "4I + 5U（Io 0.1/0.5A）",
            ["8I + 2U (Io 0.1/0.5A)"] = "8I + 2U（Io 0.1/0.5A）",
            ["4I + 5U (Io 1/5A) + 8mAin/RTD + 4mAout"] = "4I + 5U（Io 1/5A）+ 8mA 输入/RTD + 4mA 输出",
            ["5I + 4U + 8mAin/RTD + 4mAout"] = "5I + 4U + 8mA 输入/RTD + 4mA 输出",
            ["7I + 3U (Io 1/5A) + 8mAin/RTD + 4mAout"] = "7I + 3U（Io 1/5A）+ 8mA 输入/RTD + 4mA 输出",
            ["5I + 4U (Io 0.1/0.5A) + 8mAin/RTD + 4mAout"] = "5I + 4U（Io 0.1/0.5A）+ 8mA 输入/RTD + 4mA 输出",
            ["8I + 2U (Io 1/5A) + 8mAin/RTD + 4mAout"] = "8I + 2U（Io 1/5A）+ 8mA 输入/RTD + 4mA 输出",
            ["4I + 5U (Io 0.1/0.5A) + 8mAin/RTD + 4mAout"] = "4I + 5U（Io 0.1/0.5A）+ 8mA 输入/RTD + 4mA 输出",
            ["8I + 2U (Io 0.1/0.5A) + 8mAin/RTD + 4mAout"] = "8I + 2U（Io 0.1/0.5A）+ 8mA 输入/RTD + 4mA 输出",
            ["14BI + 9BO"] = "14BI + 9BO",
            ["23BI + 18BO"] = "23BI + 18BO",
            ["32BI + 27BO"] = "32BI + 27BO",
            ["41BI + 36BO"] = "41BI + 36BO",
            ["50BI + 45BO"] = "50BI + 45BO",
            ["Serial glass fibre (ST connector)"] = "串行玻璃光纤（ST 连接器）",
            ["Serial plastic fibre (Snap-in connector)"] = "串行塑料光纤（卡入式连接器）",
            ["Ethernet 100Base-FX (LC connector)"] = "以太网 100Base-FX（LC 连接器）",
            ["Ethernet 100Base-TX (RJ-45 connector)"] = "以太网 100Base-TX（RJ-45 连接器）",
            ["IEC 61850 protocol"] = "IEC 61850 协议",
            ["IEC 61850 and DNP3 protocols"] = "IEC 61850 和 DNP3 协议",
            ["IEC 61850 and IEC 60870-103 protocols"] = "IEC 61850 和 IEC 60870-103 协议",
            ["All released languages"] = "所有已发布语言",
            ["IEC English, Chinese"] = "IEC 英文、中文",
            ["Language package"] = "语言包",
            ["Integrated LHMI"] = "集成本地人机界面",
            ["Detached LHMI + 1 m cable"] = "分离式 LHMI + 1 m 电缆",
            ["Detached LHMI + 2 m cable"] = "分离式 LHMI + 2 m 电缆",
            ["Detached LHMI + 3 m cable"] = "分离式 LHMI + 3 m 电缆",
            ["Detached LHMI + 4 m cable"] = "分离式 LHMI + 4 m 电缆",
            ["Detached LHMI + 5 m cable"] = "分离式 LHMI + 5 m 电缆",
            ["No LHMI"] = "无 LHMI",
            ["Automatic voltage regulator + under impedance"] = "自动电压调节器 + 欠阻抗",
            ["Automatic voltage regulator + over-excitation"] = "自动电压调节器 + 过励磁",
            ["Fault loc. + Synchro-check"] = "故障定位 + 同期检查",
            ["Fault loc. + Distance"] = "故障定位 + 距离保护",
            ["Fault loc. + Ph seq voltage"] = "故障定位 + 相序电压功能",
            ["Fault loc. + Power Quality"] = "故障定位 + 电能质量",
            ["Fault loc. + Synchro-check + Ph seq voltage"] = "故障定位 + 同期检查 + 相序电压功能",
            ["Phase sequence voltage functions"] = "相序电压功能",
            ["Transformer differential protection for two-winding transformers"] = "双绕组变压器差动保护",
            ["Under- / overfrequency + Ph seq voltage"] = "低/过频 + 相序电压功能",
            ["Under-/overfrequency incl. df/dt + Stabilized differential"] = "低/过频含 df/dt + 稳定差动",
            ["Under-/overfrequency incl. df/dt + synchr. motor prot. functions"] = "低/过频含 df/dt + 同步电机保护功能",
            ["Third Harmonic Based Stator Earth Fault Protection"] = "基于三次谐波的定子接地故障保护",
            ["Stabilized differential + synchr. motor prot. functions"] = "稳定差动 + 同步电机保护功能",
            ["Synchro-check + Ph seq voltage"] = "同期检查 + 相序电压功能",
            ["Under impedance + over-excitation"] = "欠阻抗 + 过励磁",
            ["Synchro-check + Distance"] = "同期检查 + 距离保护",
            ["Distance + Fault loc."] = "距离保护 + 故障定位",
            ["Synchro-check + Power Quality"] = "同期检查 + 电能质量",
            ["Distance + Fault loc. + Synchro-check"] = "距离保护 + 故障定位 + 同期检查",
            ["Distance + Power Quality"] = "距离保护 + 电能质量",
            ["No options"] = "无选项",
            ["All options"] = "所有选项",
            ["Fault locator"] = "故障定位",
            ["Automatic voltage regulator"] = "自动电压调节器",
            ["Synchro-check"] = "同期检查",
            ["Under- / overfrequency incl. df/dt"] = "低/过频含 df/dt",
            ["Under- / overfrequency incl. rate of change"] = "低/过频含频率变化率",
            ["Stabilized differential"] = "稳定差动",
            ["Under impedance"] = "欠阻抗",
            ["Distance protection"] = "距离保护",
            ["Over-excitation"] = "过励磁",
            ["Synchr. motor prot. functions"] = "同步电机保护功能",
            ["Power quality"] = "电能质量",
            ["Power supply 48-125 VDC"] = "电源 48-125 VDC",
            ["Power supply 110-250 VDC, 100-240 VAC"] = "电源 110-250 VDC，100-240 VAC",
            ["Undefined"] = "未定义",
            ["Version 1.0"] = "版本 1.0",
            ["Version 1.1"] = "版本 1.1",
            ["Version 1.2"] = "版本 1.2",
            ["Version 1.3"] = "版本 1.3"
        };

    private static string PreferredDefaultCode(string digit) => digit switch
    {
        "1" => "T",
        "2" => "B",
        "4" => "N",
        "5,6" => "AA",
        "7,8" => "AA",
        "9" => "A",
        "10" => "A",
        "11" => "A",
        "12" => "Z",
        "13" => "N",
        "14" => "N",
        "15" => "N",
        "16" => "A",
        "17" => "X",
        _ => ""
    };
}

public sealed class Re630GroupViewModel : ObservableObject
{
    private readonly Re630SelectionViewModel _owner;
    private Re630OptionViewModel? _selectedOption;

    public Re630GroupViewModel(Re630SelectionViewModel owner, Re630GroupRule rule)
    {
        _owner = owner;
        Rule = rule;
        Options = [];
    }

    public Re630GroupRule Rule { get; }
    public string Digit => Rule.Digit;
    public string Title => _owner.LocalizeGroupTitle(Rule.Title);
    public int CodeLength => Rule.CodeLength;
    public ObservableCollection<Re630OptionViewModel> Options { get; }

    public Re630OptionViewModel? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (SetProperty(ref _selectedOption, value))
            {
                OnPropertyChanged(nameof(SelectedCode));
                OnPropertyChanged(nameof(SelectedDescription));
                OnPropertyChanged(nameof(SelectedDisplayText));
                _owner.HandleSelectionChanged();
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
            if (option is not null)
            {
                SelectedOption = option;
            }
        }
    }

    public string SelectedDescription => SelectedOption?.Description ?? "";
    public string SelectedDisplayText => string.IsNullOrWhiteSpace(SelectedCode)
        ? ""
        : $"{SelectedCode}: {SelectedDescription}";

    public bool ReplaceOptions(IReadOnlyList<Re630OptionRule> options, string? preferredCode)
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
                Options.Add(new Re630OptionViewModel(_owner, option));
            }
        }

        var selected = Options.FirstOrDefault(option =>
                           !string.IsNullOrWhiteSpace(preferredCode) &&
                           option.Code.Equals(preferredCode, StringComparison.OrdinalIgnoreCase))
                       ?? Options.FirstOrDefault(option => option.Code.Equals(Re630SelectionViewModelPreferredDefault(Rule.Digit), StringComparison.OrdinalIgnoreCase))
                       ?? Options.FirstOrDefault();

        var selectedChanged = !ReferenceEquals(_selectedOption, selected);
        if (selectedChanged)
        {
            _selectedOption = selected;
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(SelectedCode));
            OnPropertyChanged(nameof(SelectedDescription));
            OnPropertyChanged(nameof(SelectedDisplayText));
        }

        return collectionChanged || selectedChanged;
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(SelectedDisplayText));
        foreach (var option in Options)
        {
            option.RefreshLanguage();
        }
    }

    private static string Re630SelectionViewModelPreferredDefault(string digit) => digit switch
    {
        "1" => "T",
        "2" => "B",
        "4" => "N",
        "5,6" => "AA",
        "7,8" => "AA",
        "9" => "A",
        "10" => "A",
        "11" => "A",
        "12" => "Z",
        "13" => "N",
        "14" => "N",
        "15" => "N",
        "16" => "A",
        "17" => "X",
        _ => ""
    };
}

public sealed class Re630OptionViewModel(Re630SelectionViewModel owner, Re630OptionRule rule) : ObservableObject
{
    public string Code => rule.Code;
    public string Description => owner.LocalizeOptionDescription(rule.Description);
    public string DisplayText => $"{rule.Code} - {Description}";

    public override string ToString() => Code;

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed class Re630OrderCodeSegmentViewModel(string digit, string code, string title)
{
    public string Digit { get; } = digit;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public int Width => Code.Length > 1 ? 46 : 34;
}
