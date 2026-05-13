using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using Rex615OfflineConfigurator.Models;
using Rex615OfflineConfigurator.Services;

namespace Rex615OfflineConfigurator.ViewModels;

public sealed class CnLegacySelectorViewModel : ObservableObject
{
    private CnLegacySeriesViewModel? _selectedSeries;
    private CnLegacyDeviceViewModel? _selectedDevice;
    private string _orderingCode = "";
    private string _status = "";
    private bool _hasErrors;

    public CnLegacySelectorViewModel()
    {
        var rules = new CnLegacySelectionRuleLoader().Load();
        Series = new ObservableCollection<CnLegacySeriesViewModel>(
            rules.Series.Select(series => new CnLegacySeriesViewModel(series)));
        Devices = [];
        Groups = [];
        SummaryItems = [];
        IoSummaryItems = [];
        ValidationMessages = [];

        CopyOrderingCodeCommand = new RelayCommand(CopyOrderingCode, () => !string.IsNullOrWhiteSpace(OrderingCode));
        ImportOrderingCodeCommand = new RelayCommand(ImportOrderingCode);
        ShowDeviceDescriptionCommand = new RelayCommand(ShowDeviceDescription, () => !string.IsNullOrWhiteSpace(OrderingCode));
        PushToConversionCommand = new RelayCommand(PushToConversion, () => !string.IsNullOrWhiteSpace(OrderingCode));
        ExpandAllCommand = new RelayCommand(() => SetAllGroupsExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllGroupsExpanded(false));
        ResetCommand = new RelayCommand(ResetSelections, () => SelectedDevice is not null);

        SelectedSeries = Series.FirstOrDefault();
    }

    public ObservableCollection<CnLegacySeriesViewModel> Series { get; }
    public ObservableCollection<CnLegacyDeviceViewModel> Devices { get; }
    public ObservableCollection<CnLegacyGroupViewModel> Groups { get; }
    public ObservableCollection<CnLegacySelectionSummaryItemViewModel> SummaryItems { get; }
    public ObservableCollection<IoSummaryItemViewModel> IoSummaryItems { get; }
    public ObservableCollection<CnLegacyValidationMessageViewModel> ValidationMessages { get; }
    public RelayCommand CopyOrderingCodeCommand { get; }
    public RelayCommand ImportOrderingCodeCommand { get; }
    public RelayCommand ShowDeviceDescriptionCommand { get; }
    public RelayCommand PushToConversionCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public RelayCommand ResetCommand { get; }
    public event EventHandler<string>? PushToConversionRequested;

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
        }
    }

    public string DeviceDescription => SelectedDevice?.Description ?? "";
    public string SourceDocumentsText => SelectedSeries is null
        ? ""
        : string.Join("；", SelectedSeries.SourceDocuments);

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
        RefreshAvailability();
        RefreshSummary();
        RefreshIoSummary();
        RefreshOrderingCode();
        RefreshValidationMessages();
    }

    private void LoadDevice(CnLegacyDeviceViewModel? device)
    {
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
            yield return new IoSummaryItemViewModel("通讯模块", string.Join("；", communication));
        }

        var selectedDescriptions = Groups
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

    private static string? BuildCommunicationSummaryPart(CnLegacyGroupViewModel group)
    {
        var option = group.SelectedOption;
        if (option is null || IsNoneOption(option))
        {
            return null;
        }

        return $"{group.Name}: {option.ShortDescription}";
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
                var result = Evaluate(option);
                option.SetAvailability(result.IsValid);
                option.SetError(option.IsSelected && !result.IsValid);
            }
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
                    $"{group.Name} 必须选择一项。"));
                continue;
            }

            if (group.SelectedOption is not null)
            {
                var result = Evaluate(group.SelectedOption);
                if (!result.IsValid)
                {
                    foreach (var reason in result.Messages)
                    {
                        ValidationMessages.Add(new CnLegacyValidationMessageViewModel(
                            $"{group.Name} / {group.SelectedOption.Code}：{reason}"));
                    }
                }
            }
        }

        HasErrors = ValidationMessages.Count > 0;
        Status = HasErrors ? "订货号需要调整" : "离线规则校验通过";
    }

    private CnLegacyEvaluationResult Evaluate(CnLegacyOptionViewModel option)
    {
        var messages = new List<string>();

        foreach (var requirement in option.Model.RequiredSelections)
        {
            var selectedCode = Groups.FirstOrDefault(group =>
                    group.Position.Equals(requirement.Position, StringComparison.OrdinalIgnoreCase))
                ?.SelectedOption
                ?.Code;

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
                messages.Add(requirement.Mode.Equals("NoneOf", StringComparison.OrdinalIgnoreCase)
                    ? $"{requirement.Message}，当前 {targetName}={selectedCode ?? "未选"}。"
                    : $"{requirement.Message}，{targetName} 需选择 {expected}，当前为 {selectedCode ?? "未选"}。");
            }
        }

        foreach (var exclusion in option.Model.ExcludedCombinedSelections)
        {
            var combined = string.Concat(exclusion.Positions.Select(position =>
                Groups.FirstOrDefault(group => group.Position.Equals(position, StringComparison.OrdinalIgnoreCase))
                    ?.SelectedOption
                    ?.Code ?? ""));
            if (exclusion.Codes.Any(code => code.Equals(combined, StringComparison.OrdinalIgnoreCase)))
            {
                messages.Add(string.IsNullOrWhiteSpace(exclusion.Message)
                    ? $"不能与组合 {combined} 同时选择。"
                    : exclusion.Message);
            }
        }

        return new CnLegacyEvaluationResult(messages.Count == 0, messages);
    }

    private void CopyOrderingCode()
    {
        if (string.IsNullOrWhiteSpace(OrderingCode))
        {
            return;
        }

        Clipboard.SetText(OrderingCode);
        Status = "订货号已复制。";
    }

    private void ImportOrderingCode()
    {
        var window = new CombinationCodeImportWindow(
            "导入 615/620 CN 订货号",
            "请输入完整 18 位 615 CN 5.0 FP1 或 620 CN 2.0 FP1 订货号，软件会自动识别系列和装置类型。",
            "导入",
            "例如：HCFCACABNBC2ACN11G 或 NBFNAANNABC2DNN11G")
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        ImportOrderingCodeValue(window.CombinationCode);
    }

    private void ImportOrderingCodeValue(string value)
    {
        var code = NormalizeOrderingCode(value);
        if (code.Length != 18)
        {
            MessageBox.Show("订货号必须为 18 位代码。", "615/620 CN 选型", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var series = DetectSeries(code);
        if (series is null)
        {
            MessageBox.Show("无法识别订货号系列。615 通常以 H 或 1 开头，620 通常以 N 或 5 开头。", "615/620 CN 选型", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var applicationCode = code[2].ToString();
        var device = series.Devices.FirstOrDefault(item => item.Model.Groups
            .FirstOrDefault(group => group.Position.Equals("3", StringComparison.OrdinalIgnoreCase))
            ?.Options
            .Any(option => option.Code.Equals(applicationCode, StringComparison.OrdinalIgnoreCase)) == true);

        if (device is null)
        {
            MessageBox.Show($"当前数据包中没有找到主要应用代码 {applicationCode} 对应的装置类型。", "615/620 CN 选型", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedSeries = series;
        SelectedDevice = device;

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
            ? "订货号已导入。"
            : $"订货号已导入，但以下位号未匹配：{string.Join("；", notFound)}";
    }

    private CnLegacySeriesViewModel? DetectSeries(string code)
    {
        var first = code[0];
        var targetId = first is 'H' or '1'
            ? "615_CN_5_0_FP1"
            : first is 'N' or '5'
                ? "620_CN_2_0_FP1"
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
            "615/620 CN 装置选型描述",
            $"产品系列：{SelectedSeries?.Name ?? ""}",
            $"装置类型：{SelectedDevice?.Name ?? ""}",
            $"订货号：{OrderingCode}",
            $"状态：{Status}",
            ""
        };

        lines.Add("当前选择：");
        lines.AddRange(SummaryItems.Select(item => $"{item.Position} {item.GroupName}：{item.Code} - {item.Description}"));

        lines.Add("");
        lines.Add("I/O 摘要：");
        lines.Add(IoSummaryItems.Count == 0
            ? "无"
            : string.Join("；", IoSummaryItems.Select(item => $"{item.Name}={item.Value}")));

        if (ValidationMessages.Count > 0)
        {
            lines.Add("");
            lines.Add("校验提示：");
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
        Status = "已推送到 615/620 转换页面。";
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
        ? "未选择"
        : $"{SelectedOption.Code}：{SelectedOption.ShortDescription}";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void Select(CnLegacyOptionViewModel option)
    {
        if (SelectedOption == option)
        {
            option.SetSelected(true);
            return;
        }

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

public sealed class CnLegacyValidationMessageViewModel(string message)
{
    public string Message { get; } = message;
}

internal sealed record CnLegacyEvaluationResult(bool IsValid, IReadOnlyList<string> Messages);
