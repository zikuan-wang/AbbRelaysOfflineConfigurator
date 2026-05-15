using System.Windows;

namespace AbbRelaysOfflineConfigurator.ViewModels;

public sealed class LegacyConversionRowViewModel : ObservableObject
{
    private string _sourceOrderingCode = "";
    private string _deviceType = "";
    private string _conversionMode = "";
    private string _compositionCode = "";
    private string _rexOrderingNumber = "";
    private string _status = "";
    private bool _isSuccess;

    public LegacyConversionRowViewModel()
    {
        CopyCompositionCodeCommand = new RelayCommand(() => CopyText(CompositionCode), () => !string.IsNullOrWhiteSpace(CompositionCode));
        CopyRexOrderingNumberCommand = new RelayCommand(() => CopyText(RexOrderingNumber), () => !string.IsNullOrWhiteSpace(RexOrderingNumber));
    }

    public RelayCommand CopyCompositionCodeCommand { get; }
    public RelayCommand CopyRexOrderingNumberCommand { get; }

    public string SourceOrderingCode
    {
        get => _sourceOrderingCode;
        set => SetProperty(ref _sourceOrderingCode, value);
    }

    public string DeviceType
    {
        get => _deviceType;
        set => SetProperty(ref _deviceType, value);
    }

    public string ConversionMode
    {
        get => _conversionMode;
        set => SetProperty(ref _conversionMode, value);
    }

    public string CompositionCode
    {
        get => _compositionCode;
        set
        {
            if (SetProperty(ref _compositionCode, value))
            {
                CopyCompositionCodeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RexOrderingNumber
    {
        get => _rexOrderingNumber;
        set
        {
            if (SetProperty(ref _rexOrderingNumber, value))
            {
                CopyRexOrderingNumberCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsSuccess
    {
        get => _isSuccess;
        set => SetProperty(ref _isSuccess, value);
    }

    private static void CopyText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            Clipboard.SetText(value);
        }
        catch (Exception ex)
        {
            var isEnglish = Application.Current?.MainWindow?.DataContext is ConfiguratorViewModel { IsEnglish: true };
            MessageBox.Show(
                isEnglish ? $"Copy failed: {ex.Message}" : $"复制失败：{ex.Message}",
                "REX615",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
