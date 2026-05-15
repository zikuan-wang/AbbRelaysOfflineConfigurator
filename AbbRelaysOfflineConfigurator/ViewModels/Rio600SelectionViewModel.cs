using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using AbbRelaysOfflineConfigurator.Services;
using Microsoft.Win32;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class Rio600SelectionViewModel : ObservableObject
{
    private readonly Rio600RuleSet _rules = new Rio600RuleLoader().Load();
    private bool _isRefreshing;
    private string _orderCode = "";
    private string _status = "";
    private string _configurationSummary = "";
    private string _assemblyWidthText = "";
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private int _configuredChannels;
    private int _configuredPoints;
    private bool _isValid;

    public Rio600SelectionViewModel()
    {
        Rows =
        [
            new Rio600CompositionRowViewModel(this, "电源 1", "Power supply 1", "PowerSupply1", "PowerSupply1HW", "PowerSupply1SW", false, true),
            new Rio600CompositionRowViewModel(this, "电源 2", "Power supply 2", "PowerSupply2", "PowerSupply2HW", "PowerSupply2SW", false, false),
            new Rio600CompositionRowViewModel(this, "通信模块", "Communication module", "CommunicationModule", "CommunicationModuleHW", "CommunicationModuleSW", false, true)
        ];

        for (var position = 1; position <= 10; position++)
        {
            Rows.Add(new Rio600CompositionRowViewModel(
                this,
                $"位置 {position}",
                $"Position {position}",
                $"Postion{position}",
                $"Postion{position}HW",
                $"Postion{position}SW",
                true,
                false));
        }

        IoSummaryItems = [];
        SelectedModules = [];
        OrderListItems = [];
        Messages = [];
        ResetCommand = new RelayCommand(Reset);
        CopyOrderCodeCommand = new RelayCommand(CopyOrderList, () => OrderListItems.Count > 0);
        ExportExcelCommand = new RelayCommand(ExportExcel, () => OrderListItems.Count > 0);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription, () => OrderListItems.Count > 0);
        Reset();
    }

    public ObservableCollection<Rio600CompositionRowViewModel> Rows { get; }
    public ObservableCollection<Rio600IoSummaryItemViewModel> IoSummaryItems { get; }
    public ObservableCollection<Rio600SelectedModuleViewModel> SelectedModules { get; }
    public ObservableCollection<Rio600OrderListItemViewModel> OrderListItems { get; }
    public ObservableCollection<string> Messages { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand CopyOrderCodeCommand { get; }
    public RelayCommand ExportExcelCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }
    public string SourceFile => Path.GetFileName(_rules.SourcePath);
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
                foreach (var row in Rows)
                {
                    row.RefreshLanguage();
                }

                RefreshSelections(null);
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
                // RIO600 uses this internally for rule validation only; ordering is by module order numbers.
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string ConfigurationSummary
    {
        get => _configurationSummary;
        private set => SetProperty(ref _configurationSummary, value);
    }

    public string AssemblyWidthText
    {
        get => _assemblyWidthText;
        private set => SetProperty(ref _assemblyWidthText, value);
    }

    public int ConfiguredChannels
    {
        get => _configuredChannels;
        private set => SetProperty(ref _configuredChannels, value);
    }

    public int ConfiguredPoints
    {
        get => _configuredPoints;
        private set => SetProperty(ref _configuredPoints, value);
    }

    public bool IsValid
    {
        get => _isValid;
        private set => SetProperty(ref _isValid, value);
    }

    internal void OnRowChanged(Rio600CompositionRowViewModel row)
    {
        if (_isRefreshing)
        {
            return;
        }

        RefreshSelections(row);
    }

    private void Reset()
    {
        RefreshSelections(null, useDefaults: true);
    }

    private void RefreshSelections(Rio600CompositionRowViewModel? changedRow, bool useDefaults = false)
    {
        _isRefreshing = true;
        try
        {
            var version = CurrentVersion();
            var communicationCode = CurrentCommunicationCode();
            var configuration = _rules.FindConfiguration(communicationCode) ??
                _rules.FindConfiguration("LAG") ??
                _rules.Configurations.FirstOrDefault();

            var previousPositionConfigured = true;
            foreach (var row in Rows)
            {
                if (row.IsPosition)
                {
                    row.IsModuleEnabled = previousPositionConfigured;
                    row.IsHardwareEnabled = previousPositionConfigured && row.SelectedModuleValue != "-";
                    row.IsVersionEnabled = row.IsHardwareEnabled;
                }
                else
                {
                    row.IsModuleEnabled = !row.IsFixedModule;
                    row.IsHardwareEnabled = row.SelectedModuleValue != "-";
                    row.IsVersionEnabled = row.IsCommunicationModule;
                }

                var moduleOptions = BuildModuleOptions(row, version, configuration, previousPositionConfigured);
                var selectedModuleValue = useDefaults ? DefaultValue(row.ModuleGroup) : row.SelectedModuleValue;
                if (row.IsPosition && !previousPositionConfigured)
                {
                    selectedModuleValue = "-";
                }

                row.SetModuleOptions(moduleOptions, selectedModuleValue);

                var hardwareOptions = BuildHardwareOptions(row, version);
                row.SetHardwareOptions(hardwareOptions, SelectHardwareValue(row, useDefaults));

                var softwareOptions = BuildSoftwareOptions(row);
                row.SetSoftwareOptions(softwareOptions, SelectSoftwareValue(row, useDefaults));

                if (row.IsPosition)
                {
                    previousPositionConfigured = row.SelectedModuleValue != "-";
                    row.IsHardwareEnabled = previousPositionConfigured;
                    row.IsVersionEnabled = previousPositionConfigured;
                }
            }
        }
        finally
        {
            _isRefreshing = false;
        }

        Recalculate();
    }

    private IReadOnlyList<Rio600SelectionOptionViewModel> BuildModuleOptions(
        Rio600CompositionRowViewModel row,
        string version,
        Rio600Configuration? configuration,
        bool previousPositionConfigured)
    {
        if (row.IsPosition && !previousPositionConfigured)
        {
            return [Rio600SelectionOptionViewModel.NotConfigured(IsEnglish)];
        }

        var options = _rules.Digit(row.ModuleGroup).Options
            .Where(option => option.SupportsVersion(version))
            .Where(option => !row.IsPosition ||
                option.Value == "-" ||
                configuration?.Modules.ContainsKey(option.Value) == true)
            .Select(option => Rio600SelectionOptionViewModel.FromRule(option, IsEnglish))
            .ToList();

        return options.Count > 0 ? options : [Rio600SelectionOptionViewModel.NotConfigured(IsEnglish)];
    }

    private IReadOnlyList<Rio600SelectionOptionViewModel> BuildHardwareOptions(Rio600CompositionRowViewModel row, string version)
    {
        if (row.SelectedModuleValue == "-")
        {
            return [Rio600SelectionOptionViewModel.NotConfigured(IsEnglish)];
        }

        var allowedHardwareValues = AllowedHardwareValues(row, version).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = _rules.Digit(row.HardwareGroup).Options
            .Where(option => option.SupportsVersion(version))
            .Where(option => allowedHardwareValues.Count == 0 || allowedHardwareValues.Contains(option.Value))
            .Select(option => Rio600SelectionOptionViewModel.FromRule(option, IsEnglish))
            .ToList();

        return options.Count > 0 ? options : [Rio600SelectionOptionViewModel.NotConfigured(IsEnglish)];
    }

    private IReadOnlyList<Rio600SelectionOptionViewModel> BuildSoftwareOptions(Rio600CompositionRowViewModel row)
    {
        if (row.SelectedModuleValue == "-")
        {
            return [Rio600SelectionOptionViewModel.NotConfigured(IsEnglish)];
        }

        if (row.IsPosition)
        {
            var rule = PositionRule(row, CurrentVersion(), row.SelectedHardwareValue);
            if (rule is null)
            {
                return [Rio600SelectionOptionViewModel.NotConfigured(IsEnglish)];
            }

            var softwareOption = _rules.Digit(row.SoftwareGroup).Options
                .FirstOrDefault(option => option.Value.Equals(rule.SoftwareChar, StringComparison.OrdinalIgnoreCase));
            return [softwareOption is null
                ? new Rio600SelectionOptionViewModel(rule.SoftwareChar, rule.SoftwareChar, rule.SoftwareChar)
                : Rio600SelectionOptionViewModel.FromRule(softwareOption, IsEnglish)];
        }

        var options = _rules.Digit(row.SoftwareGroup).Options
            .Where(option => !row.IsCommunicationModule ||
                row.SelectedHardwareValue != "B" ||
                string.Compare(option.Value, "E", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(option => Rio600SelectionOptionViewModel.FromRule(option, IsEnglish))
            .ToList();

        return options.Count > 0 ? options : [Rio600SelectionOptionViewModel.NotConfigured(IsEnglish)];
    }

    private IEnumerable<string> AllowedHardwareValues(Rio600CompositionRowViewModel row, string version)
    {
        if (row.IsPosition)
        {
            return _rules.ValidPositionRules
                .Where(rule => rule.SupportsVersion(version) &&
                    rule.ModuleChar.Equals(row.SelectedModuleValue, StringComparison.OrdinalIgnoreCase))
                .Select(rule => rule.HardwareChar);
        }

        return [];
    }

    private Rio600ValidCodeRule? PositionRule(Rio600CompositionRowViewModel row, string version, string hardwareValue) =>
        _rules.ValidPositionRules.FirstOrDefault(rule =>
            rule.SupportsVersion(version) &&
            rule.ModuleChar.Equals(row.SelectedModuleValue, StringComparison.OrdinalIgnoreCase) &&
            rule.HardwareChar.Equals(hardwareValue, StringComparison.OrdinalIgnoreCase));

    private string SelectHardwareValue(Rio600CompositionRowViewModel row, bool useDefaults)
    {
        if (row.SelectedModuleValue == "-")
        {
            return "-";
        }

        if (row.IsPosition)
        {
            return PositionRule(row, CurrentVersion(), row.SelectedHardwareValue)?.HardwareChar ??
                _rules.ValidPositionRules.FirstOrDefault(rule =>
                    rule.SupportsVersion(CurrentVersion()) &&
                    rule.ModuleChar.Equals(row.SelectedModuleValue, StringComparison.OrdinalIgnoreCase))?.HardwareChar ??
                row.HardwareOptions.FirstOrDefault()?.Value ??
                "-";
        }

        return useDefaults ? DefaultValue(row.HardwareGroup) : row.SelectedHardwareValue;
    }

    private string SelectSoftwareValue(Rio600CompositionRowViewModel row, bool useDefaults)
    {
        if (row.SelectedModuleValue == "-")
        {
            return "-";
        }

        if (row.IsPosition)
        {
            return PositionRule(row, CurrentVersion(), row.SelectedHardwareValue)?.SoftwareChar ?? "A";
        }

        return useDefaults ? DefaultValue(row.SoftwareGroup) : row.SelectedSoftwareValue;
    }

    private string DefaultValue(string group)
    {
        var defaultOrderCode = _rules.DefaultOrderCode;
        var digit = _rules.Digit(group);
        if (digit.Location <= 0 || digit.Location > defaultOrderCode.Length)
        {
            return digit.Options.FirstOrDefault()?.Value ?? "-";
        }

        return defaultOrderCode.Substring(digit.Location - 1, 1);
    }

    private string CurrentVersion() =>
        Rows.FirstOrDefault(row => row.IsCommunicationModule)
            ?.SelectedSoftwareValue is { Length: > 0 } version && version != "-"
            ? version
            : "G";

    private string CurrentCommunicationCode()
    {
        var row = Rows.FirstOrDefault(item => item.IsCommunicationModule);
        return row is null ? "LAG" : row.SelectedModuleValue + row.SelectedHardwareValue + row.SelectedSoftwareValue;
    }

    private void Recalculate()
    {
        var chars = _rules.DefaultOrderCode.ToCharArray();
        foreach (var row in Rows)
        {
            SetChar(chars, row.ModuleGroup, row.SelectedModuleValue);
            SetChar(chars, row.HardwareGroup, row.SelectedHardwareValue);
            SetChar(chars, row.SoftwareGroup, row.SelectedSoftwareValue);
        }

        OrderCode = new string(chars);

        var configuration = _rules.FindConfiguration(CurrentCommunicationCode());
        var psm2Configured = Rows.First(row => row.ModuleGroup == "PowerSupply2").SelectedModuleValue != "-";
        var totals = CalculateTotals(configuration);
        ConfiguredChannels = totals.TotalChannels;
        ConfiguredPoints = totals.TotalPoints;

        var channelLimit = psm2Configured ? configuration?.MaxChannels ?? 0 : configuration?.ChannelLimit ?? 0;
        var pointLimit = psm2Configured ? configuration?.MaxPoints ?? 0 : configuration?.PointsLimit ?? 0;

        Messages.Clear();
        if (configuration is null)
        {
            Messages.Add(IsEnglish ? "The current communication module combination does not match a RIO600 configuration." : "当前通讯模块组合没有匹配到 RIO600 配置。");
        }

        if (channelLimit > 0 && totals.TotalChannels > channelLimit)
        {
            Messages.Add(IsEnglish
                ? $"The current channel count {totals.TotalChannels} exceeds the limit {channelLimit}. Configure power supply 2 or reduce I/O modules."
                : $"当前通道数 {totals.TotalChannels} 超过限制 {channelLimit}；如需扩展，请配置电源 2 或减少 I/O 模块。");
        }

        if (pointLimit > 0 && totals.TotalPoints > pointLimit)
        {
            Messages.Add(IsEnglish
                ? $"The current point count {totals.TotalPoints} exceeds the limit {pointLimit}. Configure power supply 2 or reduce I/O modules."
                : $"当前点数 {totals.TotalPoints} 超过限制 {pointLimit}；如需扩展，请配置电源 2 或减少 I/O 模块。");
        }

        IsValid = Messages.Count == 0;
        Status = IsValid
            ? IsEnglish ? "RIO600 combination valid" : "RIO600 组合有效"
            : IsEnglish ? "RIO600 combination needs adjustment" : "RIO600 组合需要调整";
        ConfigurationSummary = configuration is null
            ? IsEnglish ? "No matching configuration" : "未匹配配置"
            : IsEnglish
                ? $"Communication {configuration.CommunicationCode}, channels {totals.TotalChannels}/{channelLimit}, points {totals.TotalPoints}/{(pointLimit == 0 ? "-" : pointLimit)}"
                : $"通信组合 {configuration.CommunicationCode}，通道 {totals.TotalChannels}/{channelLimit}，点数 {totals.TotalPoints}/{(pointLimit == 0 ? "-" : pointLimit)}";

        RefreshIoSummary(totals, channelLimit, pointLimit);
        RefreshSelectedModules();
    }

    private void SetChar(char[] chars, string group, string value)
    {
        var digit = _rules.Digit(group);
        if (digit.Location <= 0 || digit.Location > chars.Length || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        chars[digit.Location - 1] = value[0];
    }

    private Rio600Totals CalculateTotals(Rio600Configuration? configuration)
    {
        var totals = new Rio600Totals();
        if (configuration is null)
        {
            return totals;
        }

        foreach (var row in Rows.Where(row => row.IsPosition && row.SelectedModuleValue != "-"))
        {
            if (!configuration.Modules.TryGetValue(row.SelectedModuleValue, out var module))
            {
                continue;
            }

            totals.TotalChannels += module.Channels;
            totals.TotalPoints += module.Points;
            totals.AddModule(row.SelectedModuleName, module.Channels, module.Points);
        }

        return totals;
    }

    private void RefreshIoSummary(Rio600Totals totals, int channelLimit, int pointLimit)
    {
        IoSummaryItems.Clear();
        AddSummary(IsEnglish ? "Channels" : "通道数", $"{totals.TotalChannels}/{(channelLimit == 0 ? "-" : channelLimit)}");
        if (pointLimit > 0 || totals.TotalPoints > 0)
        {
            AddSummary(IsEnglish ? "Points" : "点数", $"{totals.TotalPoints}/{(pointLimit == 0 ? "-" : pointLimit)}");
        }

        foreach (var module in totals.Modules.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddSummary(module.Key, IsEnglish
                ? $"{module.Value.Count} pcs / {module.Value.Channels} channels"
                : $"{module.Value.Count} 块 / {module.Value.Channels} 通道");
        }
    }

    private void AddSummary(string name, string value)
    {
        IoSummaryItems.Add(new Rio600IoSummaryItemViewModel(name, value));
    }

    private void RefreshSelectedModules()
    {
        SelectedModules.Clear();
        foreach (var row in Rows.Where(row => row.SelectedModuleValue != "-"))
        {
            var detailKey = ResolveDetailKey(row);
            if (string.IsNullOrWhiteSpace(detailKey))
            {
                continue;
            }

            var detail = Rio600ModuleCatalogService.GetDetail(detailKey);
            if (detail is null)
            {
                continue;
            }

            SelectedModules.Add(new Rio600SelectedModuleViewModel(
                row.Label,
                detailKey,
                detail.Code,
                detail.Name,
                detail.OrderNumber,
                detail.Description,
                detail.Dimensions.A));
        }

        var moduleWidth = SelectedModules.Sum(module => module.WidthMillimeters);
        var totalWidth = moduleWidth + 17.0;
        AssemblyWidthText = IsEnglish
            ? $"Assembly width approx. {totalWidth:g} mm (including two 8.5 mm end stops)"
            : $"组件宽度约 {totalWidth:g} mm（含两端 8.5 mm 端夹）";
        RefreshOrderList();
        CopyOrderCodeCommand.RaiseCanExecuteChanged();
        ExportExcelCommand.RaiseCanExecuteChanged();
        ShowDeviceDescriptionCommand.RaiseCanExecuteChanged();
    }

    private void RefreshOrderList()
    {
        OrderListItems.Clear();
        foreach (var group in SelectedModules
                     .Where(module => !string.IsNullOrWhiteSpace(module.OrderNumber))
                     .GroupBy(module => module.OrderNumber, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.First().ModuleCode, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            OrderListItems.Add(new Rio600OrderListItemViewModel(
                first.ModuleCode,
                first.Description,
                first.OrderNumber,
                group.Count(),
                string.Join(", ", group.Select(module => module.SlotName))));
        }
    }

    private static string ResolveDetailKey(Rio600CompositionRowViewModel row)
    {
        if (row.ModuleGroup.StartsWith("PowerSupply", StringComparison.OrdinalIgnoreCase))
        {
            return row.SelectedHardwareValue.Equals("B", StringComparison.OrdinalIgnoreCase) ? "PSML" : "PSMH";
        }

        if (row.IsCommunicationModule)
        {
            return row.SelectedHardwareValue.Equals("B", StringComparison.OrdinalIgnoreCase) ? "LECMFO" : "LECMIR";
        }

        return row.SelectedModuleValue.ToUpperInvariant() switch
        {
            "C" => row.SelectedHardwareValue.Equals("B", StringComparison.OrdinalIgnoreCase) ? "DIM8L" : "DIM8H",
            "B" => "DOM4",
            "D" => "RTD4",
            "E" => "AOM4",
            "F" => "SIM8F",
            "H" => "SIM4F",
            "G" => row.SelectedHardwareValue.Equals("B", StringComparison.OrdinalIgnoreCase) ? "SCM8L" : "SCM8H",
            _ => ""
        };
    }

    private void CopyOrderList()
    {
        if (OrderListItems.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, OrderListItems.Select(item =>
            $"{item.ModuleCode}\t{item.Description}\t{item.OrderNumber}\t{item.Quantity}")));
    }

    private void ExportExcel()
    {
        if (OrderListItems.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = IsEnglish ? "Export RIO600 module order list" : "导出 RIO600 模块订货清单",
            FileName = $"RIO600_order_list_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            Filter = IsEnglish ? "Excel workbook (*.xlsx)|*.xlsx" : "Excel 工作簿 (*.xlsx)|*.xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Rio600OrderListExportService.ExportExcel(OrderListItems, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                IsEnglish ? $"Failed to export RIO600 module order list: {ex.Message}" : $"导出 RIO600 模块订货清单失败：{ex.Message}",
                IsEnglish ? "RIO600 Configurator" : "RIO600 选型",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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

    private string BuildDeviceDescription()
    {
        var lines = new List<string>
        {
            IsEnglish ? "RIO600 device description" : "RIO600 装置描述",
            $"{(IsEnglish ? "Status" : "状态")}：{Status}",
            $"{(IsEnglish ? "Configuration" : "配置")}：{ConfigurationSummary}",
            $"{(IsEnglish ? "Assembly width" : "装配宽度")}：{AssemblyWidthText}",
            ""
        };

        lines.Add(IsEnglish ? "Module order list:" : "模块订货清单：");
        lines.AddRange(OrderListItems.Select(item =>
            IsEnglish
                ? $"{item.ModuleCode} - {item.Description}, order number {item.OrderNumber}, quantity {item.Quantity}"
                : $"{item.ModuleCode} - {item.Description}，订货号 {item.OrderNumber}，数量 {item.Quantity}"));

        lines.Add("");
        lines.Add(IsEnglish ? "Module list:" : "模块清单：");
        lines.AddRange(SelectedModules.Select(module =>
            IsEnglish
                ? $"{module.SlotName}: {module.ModuleCode} - {module.Description}, order number {module.OrderNumber}"
                : $"{module.SlotName}: {module.ModuleCode} - {module.Description}，订货号 {module.OrderNumber}"));

        lines.Add("");
        lines.Add(IsEnglish ? "I/O summary:" : "I/O 摘要：");
        lines.Add(IoSummaryItems.Count == 0
            ? IsEnglish ? "None" : "无"
            : string.Join(IsEnglish ? "; " : "；", IoSummaryItems.Select(item => $"{item.Name}={item.Value}")));

        if (Messages.Count > 0)
        {
            lines.Add("");
            lines.Add(IsEnglish ? "Validation messages:" : "校验提示：");
            lines.AddRange(Messages);
        }

        return string.Join(Environment.NewLine, lines);
    }
}

public sealed class Rio600CompositionRowViewModel : ObservableObject
{
    private readonly Rio600SelectionViewModel _owner;
    private Rio600SelectionOptionViewModel? _selectedModuleOption;
    private Rio600SelectionOptionViewModel? _selectedHardwareOption;
    private Rio600SelectionOptionViewModel? _selectedSoftwareOption;
    private bool _isModuleEnabled;
    private bool _isHardwareEnabled;
    private bool _isVersionEnabled;

    public Rio600CompositionRowViewModel(
        Rio600SelectionViewModel owner,
        string label,
        string labelEnglish,
        string moduleGroup,
        string hardwareGroup,
        string softwareGroup,
        bool isPosition,
        bool isFixedModule)
    {
        _owner = owner;
        LabelChinese = label;
        LabelEnglish = labelEnglish;
        ModuleGroup = moduleGroup;
        HardwareGroup = hardwareGroup;
        SoftwareGroup = softwareGroup;
        IsPosition = isPosition;
        IsFixedModule = isFixedModule;
        ModuleOptions = [];
        HardwareOptions = [];
        SoftwareOptions = [];
    }

    public string Label => _owner.IsEnglish ? LabelEnglish : LabelChinese;
    private string LabelChinese { get; }
    private string LabelEnglish { get; }
    public string ModuleGroup { get; }
    public string HardwareGroup { get; }
    public string SoftwareGroup { get; }
    public bool IsPosition { get; }
    public bool IsFixedModule { get; }
    public bool IsCommunicationModule => ModuleGroup.Equals("CommunicationModule", StringComparison.OrdinalIgnoreCase);
    public ObservableCollection<Rio600SelectionOptionViewModel> ModuleOptions { get; }
    public ObservableCollection<Rio600SelectionOptionViewModel> HardwareOptions { get; }
    public ObservableCollection<Rio600SelectionOptionViewModel> SoftwareOptions { get; }
    public string SelectedModuleValue => SelectedModuleOption?.Value ?? "-";
    public string SelectedHardwareValue => SelectedHardwareOption?.Value ?? "-";
    public string SelectedSoftwareValue => SelectedSoftwareOption?.Value ?? "-";
    public string SelectedModuleName => SelectedModuleOption?.Name ?? (_owner.IsEnglish ? "Not configured" : "未配置");

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(SelectedModuleName));
    }

    public Rio600SelectionOptionViewModel? SelectedModuleOption
    {
        get => _selectedModuleOption;
        set
        {
            if (SetProperty(ref _selectedModuleOption, value))
            {
                OnPropertyChanged(nameof(SelectedModuleValue));
                OnPropertyChanged(nameof(SelectedModuleName));
                _owner.OnRowChanged(this);
            }
        }
    }

    public Rio600SelectionOptionViewModel? SelectedHardwareOption
    {
        get => _selectedHardwareOption;
        set
        {
            if (SetProperty(ref _selectedHardwareOption, value))
            {
                OnPropertyChanged(nameof(SelectedHardwareValue));
                _owner.OnRowChanged(this);
            }
        }
    }

    public Rio600SelectionOptionViewModel? SelectedSoftwareOption
    {
        get => _selectedSoftwareOption;
        set
        {
            if (SetProperty(ref _selectedSoftwareOption, value))
            {
                OnPropertyChanged(nameof(SelectedSoftwareValue));
                _owner.OnRowChanged(this);
            }
        }
    }

    public bool IsModuleEnabled
    {
        get => _isModuleEnabled;
        set => SetProperty(ref _isModuleEnabled, value);
    }

    public bool IsHardwareEnabled
    {
        get => _isHardwareEnabled;
        set => SetProperty(ref _isHardwareEnabled, value);
    }

    public bool IsVersionEnabled
    {
        get => _isVersionEnabled;
        set => SetProperty(ref _isVersionEnabled, value);
    }

    internal void SetModuleOptions(IReadOnlyList<Rio600SelectionOptionViewModel> options, string preferredValue)
    {
        ReplaceOptions(ModuleOptions, options);
        SelectedModuleOption = SelectOption(ModuleOptions, preferredValue);
    }

    internal void SetHardwareOptions(IReadOnlyList<Rio600SelectionOptionViewModel> options, string preferredValue)
    {
        ReplaceOptions(HardwareOptions, options);
        SelectedHardwareOption = SelectOption(HardwareOptions, preferredValue);
    }

    internal void SetSoftwareOptions(IReadOnlyList<Rio600SelectionOptionViewModel> options, string preferredValue)
    {
        ReplaceOptions(SoftwareOptions, options);
        SelectedSoftwareOption = SelectOption(SoftwareOptions, preferredValue);
    }

    private static Rio600SelectionOptionViewModel? SelectOption(
        IEnumerable<Rio600SelectionOptionViewModel> options,
        string preferredValue) =>
        options.FirstOrDefault(option => option.Value.Equals(preferredValue, StringComparison.OrdinalIgnoreCase)) ??
        options.FirstOrDefault();

    private static void ReplaceOptions(
        ObservableCollection<Rio600SelectionOptionViewModel> target,
        IEnumerable<Rio600SelectionOptionViewModel> options)
    {
        target.Clear();
        foreach (var option in options)
        {
            target.Add(option);
        }
    }
}

public sealed record Rio600SelectionOptionViewModel(string Value, string Name, string Description)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Value : Name;

    public static Rio600SelectionOptionViewModel FromRule(Rio600Option option, bool isEnglish) =>
        new(option.Value, TranslateName(option.Name, isEnglish), option.Description);

    public static Rio600SelectionOptionViewModel NotConfigured(bool isEnglish) =>
        new("-", isEnglish ? "Not configured" : "未配置", isEnglish ? "Not configured" : "未配置");

    private static string TranslateName(string name, bool isEnglish)
    {
        if (isEnglish)
        {
            return name;
        }

        return name switch
        {
            "Not configured" => "未配置",
            "Not applicable" => "不适用",
            "High voltage" => "高电压",
            "Low Voltage" => "低电压",
            "Electrical" => "电口",
            "Optical" => "光口",
            "Contact" => "触点",
            "RTD/mA in" => "RTD/mA 输入",
            "mA out" => "mA 输出",
            "Sensor input" => "传感器输入",
            _ => name
        };
    }
}

public sealed record Rio600IoSummaryItemViewModel(string Name, string Value);

public sealed record Rio600SelectedModuleViewModel(
    string SlotName,
    string DetailKey,
    string ModuleCode,
    string ModuleName,
    string OrderNumber,
    string Description,
    double WidthMillimeters)
{
    public double VisualWidth => Math.Max(58, WidthMillimeters * 2.2);
    public string WidthText => $"{WidthMillimeters:g} mm";
    public string DisplayTitle => $"{SlotName}: {ModuleCode}";
}

public sealed record Rio600OrderListItemViewModel(
    string ModuleCode,
    string Description,
    string OrderNumber,
    int Quantity,
    string Slots)
{
    public string QuantityText => $"x {Quantity}";
}

internal sealed class Rio600Totals
{
    public int TotalChannels { get; set; }
    public int TotalPoints { get; set; }
    public Dictionary<string, Rio600ModuleTotal> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddModule(string name, int channels, int points)
    {
        if (!Modules.TryGetValue(name, out var total))
        {
            total = new Rio600ModuleTotal();
            Modules[name] = total;
        }

        total.Count++;
        total.Channels += channels;
        total.Points += points;
    }
}

internal sealed class Rio600ModuleTotal
{
    public int Count { get; set; }
    public int Channels { get; set; }
    public int Points { get; set; }
}
