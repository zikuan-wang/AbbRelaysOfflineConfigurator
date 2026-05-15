using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using AbbRelaysLicensing;

namespace AbbRelaysAuthorizationTool;

public partial class MainWindow : Window
{
    private LicenseRequest? _request;

    public MainWindow()
    {
        InitializeComponent();
        ExpiresDatePicker.SelectedDate = DateTime.Today.AddYears(1);
        RefreshAuthorizationRecords();
    }

    private void ImportRequestButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"授权申请文件 (*{LicenseService.RequestExtension})|*{LicenseService.RequestExtension}|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _request = LicenseService.ReadRequestFile(dialog.FileName);
            RequestPathTextBlock.Text = dialog.FileName;
            MachineNameTextBlock.Text = _request.MachineName;
            UserNameTextBlock.Text = _request.UserName;
            MachineIdTextBlock.Text = _request.MachineId;
            CreatedAtTextBlock.Text = _request.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
            LicensedToTextBox.Text = _request.UserName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导入申请文件失败：{ex.Message}", "授权工具", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportActivationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_request is null)
        {
            MessageBox.Show(this, "请先导入授权申请文件。", "授权工具", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"ABBRelays_{_request.MachineName}_{DateTime.Now:yyyyMMddHHmm}{LicenseService.ActivationExtension}",
            Filter = $"激活文件 (*{LicenseService.ActivationExtension})|*{LicenseService.ActivationExtension}"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            DateTimeOffset? expiresAt = PermanentCheckBox.IsChecked == true || ExpiresDatePicker.SelectedDate is null
                ? null
                : new DateTimeOffset(ExpiresDatePicker.SelectedDate.Value.Date.AddDays(1).AddTicks(-1));
            var issuedAt = DateTimeOffset.Now;
            var activationText = LicenseService.CreateActivationFileText(
                _request,
                LicensedToTextBox.Text,
                expiresAt,
                AuthorizationKeyProvider.PrivateKeyXmlBase64);
            File.WriteAllText(dialog.FileName, activationText, Encoding.UTF8);
            AuthorizationRecordStore.SaveIssuedActivation(
                _request,
                LicensedToTextBox.Text,
                issuedAt,
                expiresAt,
                dialog.FileName);
            RefreshAuthorizationRecords();
            MessageBox.Show(this, "激活文件已导出。", "授权工具", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出激活文件失败：{ex.Message}", "授权工具", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PermanentCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (ExpiresDatePicker is not null)
        {
            ExpiresDatePicker.IsEnabled = PermanentCheckBox.IsChecked != true;
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void RefreshRecordsButton_OnClick(object sender, RoutedEventArgs e) => RefreshAuthorizationRecords();

    private void OpenRecordFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var directory = Path.GetDirectoryName(AuthorizationRecordStore.RecordsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void RefreshAuthorizationRecords()
    {
        AuthorizationRecordsDataGrid.ItemsSource = AuthorizationRecordStore.Load();
        RecordFilePathTextBlock.Text = $"记录文件：{AuthorizationRecordStore.RecordsPath}";
    }
}
