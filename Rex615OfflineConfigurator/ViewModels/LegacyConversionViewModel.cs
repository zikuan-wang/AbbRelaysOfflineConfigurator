using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Rex615OfflineConfigurator.Services;

namespace Rex615OfflineConfigurator.ViewModels;

public sealed class LegacyConversionViewModel : ObservableObject
{
    private readonly LegacyOrderCodeConversionService _offlineConversionService = new();
    private readonly OnlineValidationService _onlineValidationService;
    private string _inputCodes = "";
    private string _status = "请输入 615/620 系列订货号，每行一个。";
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
    public string ResultSummary => Results.Count == 0 ? "暂无转换结果" : $"共 {Results.Count} 条结果";

    private bool CanConvert() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(InputCodes);

    private async Task ConvertBatchAsync()
    {
        var codes = ParseInputCodes(InputCodes).ToList();
        if (codes.Count == 0)
        {
            Status = "请输入至少一个 615/620 系列订货号。";
            return;
        }

        IsBusy = true;
        Results.Clear();
        Status = $"正在转换 {codes.Count} 条订货号...";

        try
        {
            if (UseOnlineConversion)
            {
                foreach (var code in codes)
                {
                    var row = CreateRow(code, "自动识别", "在线转换");
                    Results.Add(row);
                    await ConvertOnlineAsync(row);
                }
            }
            else
            {
                var offlineResults = await _offlineConversionService.ConvertOfflineBatchAsync(codes);
                foreach (var offlineResult in offlineResults)
                {
                    var row = CreateRow(offlineResult.SourceOrderingCode, offlineResult.DeviceType, "离线转换");
                    row.CompositionCode = offlineResult.CompositionCode ?? "";
                    if (!offlineResult.IsSuccess || string.IsNullOrWhiteSpace(offlineResult.CompositionCode))
                    {
                        row.Status = offlineResult.Message;
                        row.IsSuccess = false;
                        Results.Add(row);
                        continue;
                    }

                    Results.Add(row);
                    await ValidateRexCodeAsync(row);
                }
            }

            var successCount = Results.Count(row => row.IsSuccess);
            Status = $"转换完成：成功 {successCount} 条，失败 {Results.Count - successCount} 条。";
        }
        catch (Exception ex)
        {
            Status = $"转换失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConvertOnlineAsync(LegacyConversionRowViewModel row)
    {
        try
        {
            var result = await _onlineValidationService.ConvertLegacyCodeAsync(row.SourceOrderingCode);
            row.CompositionCode = result.CompositionCode ?? "";
            if (!result.IsValid || string.IsNullOrWhiteSpace(result.CompositionCode))
            {
                row.Status = string.IsNullOrWhiteSpace(result.Message) ? "在线转换未返回 REX615 组合代码。" : result.Message;
                row.IsSuccess = false;
                return;
            }

            await ValidateRexCodeAsync(row);
        }
        catch (Exception ex)
        {
            row.Status = $"在线转换失败：{ex.Message}";
            row.IsSuccess = false;
        }
    }

    private async Task ValidateRexCodeAsync(LegacyConversionRowViewModel row)
    {
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
                ? "转换并在线校验通过。"
                : "REX615 组合代码在线校验未通过。";
        }
        catch (Exception ex)
        {
            row.Status = $"REX615 在线校验失败：{ex.Message}";
            row.IsSuccess = false;
        }
    }

    private static LegacyConversionRowViewModel CreateRow(string sourceOrderingCode, string deviceType, string conversionMode) =>
        new()
        {
            SourceOrderingCode = sourceOrderingCode,
            DeviceType = deviceType,
            ConversionMode = conversionMode,
            Status = "等待处理..."
        };

    private static bool ShouldUseOnlineComposition(string currentCompositionCode, string? onlineCompositionCode)
    {
        if (string.IsNullOrWhiteSpace(onlineCompositionCode) ||
            !onlineCompositionCode.StartsWith("REX615", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mainCode = currentCompositionCode.Split('+', 2)[0];
        return !mainCode.EndsWith("CN", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentCompositionCode, onlineCompositionCode, StringComparison.OrdinalIgnoreCase);
    }

    private void Export()
    {
        if (Results.Count == 0)
        {
            MessageBox.Show("没有可导出的转换结果。", "REX615", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"615_620_to_REX615_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            Filter = "Excel 清单 (*.xlsx)|*.xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            LegacyConversionExportService.ExportExcel(Results, dialog.FileName);
            Status = $"清单已导出：{Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "REX615", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Clear()
    {
        InputCodes = "";
        Results.Clear();
        Status = "请输入 615/620 系列订货号，每行一个。";
    }

    private void ResultsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ExportCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ResultSummary));
    }

    private static IEnumerable<string> ParseInputCodes(string value) =>
        value.Split(new[] { '\r', '\n', ',', ';', '，', '；', '\t', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => code.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
