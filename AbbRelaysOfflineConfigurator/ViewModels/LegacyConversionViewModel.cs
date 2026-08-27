using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.ViewModels;

// 615/620 -> REX615 批量转换页的工作流协调器。离线模式先用本地工作簿规则生成组合代码，
// 在线模式由 ABB 接口生成；两条路径最终都调用在线校验，并用接口返回的订货号形成最终行状态。
public sealed class LegacyConversionViewModel : ObservableObject
{
    private readonly LegacyOrderCodeConversionService _offlineConversionService = new();
    private readonly OnlineValidationService _onlineValidationService;
    private string _inputCodes = "";
    private string _status = "请输入 615/620 系列订货号，每行一个。";
    private string _displayLanguage = ConfiguratorViewModel.ChineseLanguage;
    private bool _useOnlineConversion;
    private bool _isBusy;

    public LegacyConversionViewModel(OnlineValidationService onlineValidationService)
    {
        _onlineValidationService = onlineValidationService;
        Results = [];
        Results.CollectionChanged += ResultsOnCollectionChanged;

        ConvertCommand = new RelayCommand(() => _ = ConvertBatchAsync(), CanConvert);
        ClearCommand = new RelayCommand(Clear, () => !IsBusy && (Results.Count > 0 || !string.IsNullOrWhiteSpace(InputCodes)));
        ExportCommand = new RelayCommand(Export, () => !IsBusy && Results.Count > 0);
    }

    public ObservableCollection<LegacyConversionRowViewModel> Results { get; }
    public RelayCommand ConvertCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ExportCommand { get; }
    private bool IsEnglish => DisplayLanguage.Equals(ConfiguratorViewModel.EnglishLanguage, StringComparison.OrdinalIgnoreCase);

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
                Status = TranslateConversionMessage(Status);
                foreach (var row in Results)
                {
                    row.Status = TranslateConversionMessage(row.Status);
                    row.ConversionMode = TranslateConversionMessage(row.ConversionMode);
                    if (row.DeviceType.Equals("自动识别", StringComparison.OrdinalIgnoreCase) ||
                        row.DeviceType.Equals("Auto-detect", StringComparison.OrdinalIgnoreCase))
                    {
                        row.DeviceType = IsEnglish ? "Auto-detect" : "自动识别";
                    }
                }

                if (Results.Count == 0 && string.IsNullOrWhiteSpace(InputCodes))
                {
                    Status = EmptyStatus();
                }

                OnPropertyChanged(nameof(ResultSummary));
            }
        }
    }

    public string InputCodes
    {
        get => _inputCodes;
        set
        {
            if (SetProperty(ref _inputCodes, value))
            {
                ConvertCommand.RaiseCanExecuteChanged();
                ClearCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool UseOnlineConversion
    {
        get => _useOnlineConversion;
        set
        {
            if (SetProperty(ref _useOnlineConversion, value))
            {
                ConvertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                ConvertCommand.RaiseCanExecuteChanged();
                ClearCommand.RaiseCanExecuteChanged();
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanEdit => !IsBusy;
    public string ResultSummary => Results.Count == 0
        ? IsEnglish ? "No conversion results" : "暂无转换结果"
        : IsEnglish ? $"{Results.Count} result(s)" : $"共 {Results.Count} 条结果";

    private bool CanConvert() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(InputCodes);

    private async Task ConvertBatchAsync()
    {
        // 输入先按常见中英文分隔符拆分并去重，保证一次批处理中同一订货号只请求/求值一次。
        var codes = ParseInputCodes(InputCodes).ToList();
        if (codes.Count == 0)
        {
            Status = IsEnglish ? "Enter at least one 615/620 series order code." : "请输入至少一个 615/620 系列订货号。";
            return;
        }

        IsBusy = true;
        Results.Clear();
        Status = IsEnglish ? $"Converting {codes.Count} order code(s)..." : $"正在转换 {codes.Count} 条订货号...";

        try
        {
            if (UseOnlineConversion)
            {
                // 在线接口逐条处理并立即更新对应行，既能保持输入顺序，也能让用户看到渐进结果；
                // 当前没有并发发请求，避免批量输入对外部服务形成瞬时压力。
                foreach (var code in codes)
                {
                    var row = CreateRow(
                        code,
                        IsEnglish ? "Auto-detect" : "自动识别",
                        IsEnglish ? "Online conversion" : "在线转换",
                        IsEnglish);
                    Results.Add(row);
                    await ConvertOnlineAsync(row);
                }
            }
            else
            {
                // 本地公式求值整批在后台线程完成；成功生成组合代码后仍逐条向 ABB 校验，
                // 因此“离线转换成功”不等同于本页最终的“转换并在线校验通过”。
                var offlineResults = await _offlineConversionService.ConvertOfflineBatchAsync(codes);
                foreach (var offlineResult in offlineResults)
                {
                    var row = CreateRow(
                        offlineResult.SourceOrderingCode,
                        offlineResult.DeviceType,
                        IsEnglish ? "Offline conversion" : "离线转换",
                        IsEnglish);
                    row.CompositionCode = offlineResult.CompositionCode ?? "";
                    if (!offlineResult.IsSuccess || string.IsNullOrWhiteSpace(offlineResult.CompositionCode))
                    {
                        row.Status = TranslateConversionMessage(offlineResult.Message);
                        row.IsSuccess = false;
                        Results.Add(row);
                        continue;
                    }

                    Results.Add(row);
                    await ValidateRexCodeAsync(row);
                }
            }

            var successCount = Results.Count(row => row.IsSuccess);
            Status = IsEnglish
                ? $"Conversion completed: {successCount} succeeded, {Results.Count - successCount} failed."
                : $"转换完成：成功 {successCount} 条，失败 {Results.Count - successCount} 条。";
        }
        catch (Exception ex)
        {
            Status = IsEnglish ? $"Conversion failed: {ex.Message}" : $"转换失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConvertOnlineAsync(LegacyConversionRowViewModel row)
    {
        // 每行自行捕获异常，单个订货号的网络或响应问题不会中断整批后续项目。
        try
        {
            var result = await _onlineValidationService.ConvertLegacyCodeAsync(row.SourceOrderingCode);
            row.CompositionCode = result.CompositionCode ?? "";
            if (!result.IsValid || string.IsNullOrWhiteSpace(result.CompositionCode))
            {
                row.Status = string.IsNullOrWhiteSpace(result.Message)
                    ? IsEnglish ? "Online conversion did not return a REX615 combination code." : "在线转换未返回 REX615 组合代码。"
                    : TranslateConversionMessage(result.Message);
                row.IsSuccess = false;
                return;
            }

            await ValidateRexCodeAsync(row);
        }
        catch (Exception ex)
        {
            row.Status = IsEnglish ? $"Online conversion failed: {ex.Message}" : $"在线转换失败：{ex.Message}";
            row.IsSuccess = false;
        }
    }

    private async Task ValidateRexCodeAsync(LegacyConversionRowViewModel row)
    {
        // 在线校验是两种转换模式的汇合点：只有服务端确认有效且返回订货号，行才标记为最终成功。
        try
        {
            var validation = await _onlineValidationService.ValidateAsync(row.CompositionCode);
            if (ShouldUseOnlineComposition(row.CompositionCode, validation.CompositionCode))
            {
                row.CompositionCode = validation.CompositionCode!;
            }

            row.RexOrderingNumber = validation.OrderingNumber ?? "";
            row.IsSuccess = validation.IsValid && !string.IsNullOrWhiteSpace(validation.OrderingNumber);
            row.Status = row.IsSuccess
                ? IsEnglish ? "Conversion and online check passed." : "转换并在线校验通过。"
                : IsEnglish ? "REX615 combination code did not pass online validation." : "REX615 组合代码在线校验未通过。";
        }
        catch (Exception ex)
        {
            row.Status = IsEnglish ? $"REX615 online check failed: {ex.Message}" : $"REX615 在线校验失败：{ex.Message}";
            row.IsSuccess = false;
        }
    }

    private static LegacyConversionRowViewModel CreateRow(
        string sourceOrderingCode,
        string deviceType,
        string conversionMode,
        bool isEnglish) =>
        new()
        {
            SourceOrderingCode = sourceOrderingCode,
            DeviceType = deviceType,
            ConversionMode = conversionMode,
            Status = isEnglish ? "Waiting..." : "等待处理..."
        };

    private static bool ShouldUseOnlineComposition(string currentCompositionCode, string? onlineCompositionCode)
    {
        if (string.IsNullOrWhiteSpace(onlineCompositionCode) ||
            !onlineCompositionCode.StartsWith("REX615", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // CN 主代码保留本地规则生成结果，不用在线响应覆盖；其他系列若服务端规范化了组合代码，
        // 则采用服务端版本供后续导出，减少旧规则格式差异。
        var mainCode = currentCompositionCode.Split('+', 2)[0];
        return !mainCode.EndsWith("CN", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentCompositionCode, onlineCompositionCode, StringComparison.OrdinalIgnoreCase);
    }

    private void Export()
    {
        if (Results.Count == 0)
        {
            MessageBox.Show(
                IsEnglish ? "There are no conversion results to export." : "没有可导出的转换结果。",
                "REX615",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"615_620_to_REX615_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            Filter = IsEnglish ? "Excel workbook (*.xlsx)|*.xlsx" : "Excel 清单 (*.xlsx)|*.xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            LegacyConversionExportService.ExportExcel(Results, dialog.FileName);
            Status = IsEnglish
                ? $"List exported: {Path.GetFileName(dialog.FileName)}"
                : $"清单已导出：{Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                IsEnglish ? $"Export failed: {ex.Message}" : $"导出失败：{ex.Message}",
                "REX615",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Clear()
    {
        InputCodes = "";
        Results.Clear();
        Status = EmptyStatus();
    }

    private void ResultsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 集合是导出权限和结果计数的共同来源，集中监听可避免每条处理分支遗漏命令状态刷新。
        ExportCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ResultSummary));
    }

    private static IEnumerable<string> ParseInputCodes(string value) =>
        value.Split(new[] { '\r', '\n', ',', ';', '，', '；', '\t', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => code.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private string EmptyStatus() => IsEnglish
        ? "Enter 615/620 series order codes, one per line."
        : "请输入 615/620 系列订货号，每行一个。";

    private string TranslateConversionMessage(string message)
    {
        var localized = OnlineValidationService.LocalizeMessage(message, IsEnglish);
        if (!IsEnglish)
        {
            return localized switch
            {
                "Waiting..." => "等待处理...",
                "Offline conversion" => "离线转换",
                "Online conversion" => "在线转换",
                "Auto-detect" => "自动识别",
                _ when localized.StartsWith("Conversion completed:", StringComparison.OrdinalIgnoreCase) => localized
                    .Replace("Conversion completed:", "转换完成：", StringComparison.Ordinal)
                    .Replace("succeeded", "成功", StringComparison.Ordinal)
                    .Replace("failed", "失败", StringComparison.Ordinal),
                _ => localized
            };
        }

        var completedMatch = Regex.Match(localized, @"^转换完成：成功 (?<success>\d+) 条，失败 (?<failed>\d+) 条。$");
        if (completedMatch.Success)
        {
            return $"Conversion completed: {completedMatch.Groups["success"].Value} succeeded, {completedMatch.Groups["failed"].Value} failed.";
        }

        var convertingMatch = Regex.Match(localized, @"^正在转换 (?<count>\d+) 条订货号\.\.\.$");
        if (convertingMatch.Success)
        {
            return $"Converting {convertingMatch.Groups["count"].Value} order code(s)...";
        }

        return localized switch
        {
            "等待处理..." => "Waiting...",
            "离线转换" => "Offline conversion",
            "在线转换" => "Online conversion",
            "自动识别" => "Auto-detect",
            "未找到本地 615/620 转换规则包。" => "Local 615/620 conversion rule package was not found.",
            "615/620 订货号为空。" => "615/620 order code is empty.",
            "无法识别装置类型，未生成 REX615 组合代码。" => "Device type cannot be identified, and no REX615 combination code was generated.",
            _ when localized.StartsWith("转换失败：", StringComparison.OrdinalIgnoreCase) =>
                "Conversion failed: " + localized["转换失败：".Length..],
            _ when localized.StartsWith("清单已导出：", StringComparison.OrdinalIgnoreCase) =>
                "List exported: " + localized["清单已导出：".Length..],
            _ when localized.StartsWith("离线转换失败：", StringComparison.OrdinalIgnoreCase) =>
                "Offline conversion failed: " + localized["离线转换失败：".Length..],
            _ when localized.Contains("离线转换通过", StringComparison.OrdinalIgnoreCase) =>
                localized
                    .Replace("根据订货号型号自动识别为", "Detected by order-code model as", StringComparison.Ordinal)
                    .Replace("按规则评分自动识别为", "Detected by rule scoring as", StringComparison.Ordinal)
                    .Replace("，离线转换通过。", "; offline conversion passed.", StringComparison.Ordinal),
            _ when localized.Contains("但未生成 REX615 组合代码", StringComparison.OrdinalIgnoreCase) =>
                localized
                    .Replace("根据订货号型号自动识别为", "Detected by order-code model as", StringComparison.Ordinal)
                    .Replace("按规则评分自动识别为", "Detected by rule scoring as", StringComparison.Ordinal)
                    .Replace("，但未生成 REX615 组合代码。", "; no REX615 combination code was generated.", StringComparison.Ordinal),
            _ when localized.Contains("但规则返回异常结果：", StringComparison.OrdinalIgnoreCase) =>
                localized
                    .Replace("根据订货号型号自动识别为", "Detected by order-code model as", StringComparison.Ordinal)
                    .Replace("按规则评分自动识别为", "Detected by rule scoring as", StringComparison.Ordinal)
                    .Replace("，但规则返回异常结果：", "; rule returned an abnormal result: ", StringComparison.Ordinal)
                    .Replace("自动识别为", "Detected as ", StringComparison.Ordinal),
            _ => localized
        };
    }
}
