using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Rex615Licensing;
using Rex615OfflineConfigurator.Services;
using Rex615OfflineConfigurator.ViewModels;

namespace Rex615OfflineConfigurator;

public partial class MainWindow : Window
{
    private readonly UpdateCheckService _updateCheckService = new();
    private string? _updateReleaseUrl;
    private string? _updateDownloadUrl;
    private string? _updateDownloadAssetName;

    public MainWindow()
    {
        InitializeComponent();
        AboutVersionTextBlock.Text = $"版本 {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";
        if (DataContext is ConfiguratorViewModel viewModel)
        {
            viewModel.CnLegacySelection.PushToConversionRequested += CnLegacySelection_OnPushToConversionRequested;
        }

        MainTabControl.SelectedIndex = 0;
        ApplyLicenseGate();
        Loaded += MainWindow_OnLoaded;
    }

    private void CnLegacySelection_OnPushToConversionRequested(object? sender, string orderingCode)
    {
        if (DataContext is not ConfiguratorViewModel viewModel)
        {
            return;
        }

        viewModel.LegacyConversion.InputCodes = orderingCode;
        NavigateToProtectedTab(2);
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!LicenseService.GetStatus(LicenseKeyProvider.PublicKeyXmlBase64).IsLicensed)
        {
            _ = Dispatcher.BeginInvoke(() => MainTabControl.SelectedIndex = 3, DispatcherPriority.Background);
        }

        await Dispatcher.Yield(DispatcherPriority.Background);
        await CheckForStartupUpdateAsync();
    }

    private async Task CheckForStartupUpdateAsync()
    {
        try
        {
            var result = await _updateCheckService.CheckLatestAsync();
            if (!result.IsSuccess || !result.HasUpdate)
            {
                return;
            }

            _updateReleaseUrl = string.IsNullOrWhiteSpace(result.ReleaseUrl)
                ? UpdateCheckService.ReleaseRepositoryUrl + "/releases"
                : result.ReleaseUrl;
            _updateDownloadUrl = result.DownloadUrl;
            _updateDownloadAssetName = result.DownloadAssetName;
            OpenUpdateReleaseButton.IsEnabled = true;
            DownloadInstallUpdateButton.IsEnabled = !string.IsNullOrWhiteSpace(result.DownloadUrl);

            var assetText = string.IsNullOrWhiteSpace(result.DownloadAssetName)
                ? "可打开发布页面下载最新安装包。"
                : $"可下载安装包：{result.DownloadAssetName}";
            UpdateStatusTextBlock.Text =
                $"发现新版本 {result.LatestVersion}，当前版本 {result.CurrentVersion}。{assetText}";

            var message =
                $"发现新版本 {result.LatestVersion}。\n当前版本 {result.CurrentVersion}。\n{assetText}\n\n是否打开更新页面？";
            if (MessageBox.Show(this, message, "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Information) ==
                MessageBoxResult.Yes)
            {
                MainTabControl.SelectedIndex = 3;
            }
        }
        catch
        {
            // Startup update checks must stay silent unless an update is actually available.
        }
    }

    private void AboutLicensePageButton_OnClick(object sender, RoutedEventArgs e) => MainTabControl.SelectedIndex = 3;

    private async void CheckUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        OpenUpdateReleaseButton.IsEnabled = false;
        DownloadInstallUpdateButton.IsEnabled = false;
        _updateDownloadUrl = null;
        _updateDownloadAssetName = null;
        UpdateStatusTextBlock.Text = "正在检查 GitHub Release 更新...";

        try
        {
            var result = await _updateCheckService.CheckLatestAsync();
            if (!result.IsSuccess)
            {
                _updateReleaseUrl = UpdateCheckService.ReleaseRepositoryUrl + "/releases";
                UpdateStatusTextBlock.Text = $"检查失败：{result.ErrorMessage}";
                OpenUpdateReleaseButton.IsEnabled = true;
                return;
            }

            _updateReleaseUrl = string.IsNullOrWhiteSpace(result.ReleaseUrl)
                ? UpdateCheckService.ReleaseRepositoryUrl + "/releases"
                : result.ReleaseUrl;
            OpenUpdateReleaseButton.IsEnabled = true;
            _updateDownloadUrl = result.DownloadUrl;
            _updateDownloadAssetName = result.DownloadAssetName;

            if (result.HasUpdate)
            {
                var assetText = string.IsNullOrWhiteSpace(result.DownloadAssetName)
                    ? "请打开发布页下载最新安装包。"
                    : $"可下载：{result.DownloadAssetName}";
                DownloadInstallUpdateButton.IsEnabled = !string.IsNullOrWhiteSpace(result.DownloadUrl);
                UpdateStatusTextBlock.Text =
                    $"发现新版本 {result.LatestVersion}，当前版本 {result.CurrentVersion}。{assetText}";
            }
            else
            {
                UpdateStatusTextBlock.Text =
                    $"已是最新版本。当前版本 {result.CurrentVersion}，最新发布 {result.LatestVersion}。";
            }
        }
        catch (Exception ex)
        {
            _updateReleaseUrl = UpdateCheckService.ReleaseRepositoryUrl + "/releases";
            UpdateStatusTextBlock.Text = $"检查失败：{ex.Message}";
            OpenUpdateReleaseButton.IsEnabled = true;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenUpdateReleaseButton_OnClick(object sender, RoutedEventArgs e) =>
        UpdateCheckService.OpenReleasePage(_updateReleaseUrl);

    private async void DownloadInstallUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_updateDownloadUrl))
        {
            UpdateStatusTextBlock.Text = "没有可下载的安装包，请先检查更新。";
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        OpenUpdateReleaseButton.IsEnabled = false;
        DownloadInstallUpdateButton.IsEnabled = false;

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                var percentText = value.Percent is null ? "" : $" {value.Percent}%";
                UpdateStatusTextBlock.Text = $"正在下载安装包{percentText}...";
            });
            var installerPath = await _updateCheckService.DownloadInstallerAsync(
                _updateDownloadUrl,
                _updateDownloadAssetName,
                progress);

            UpdateStatusTextBlock.Text = $"安装包已下载：{installerPath}。正在启动安装程序...";
            UpdateCheckService.StartInstaller(installerPath);
            UpdateStatusTextBlock.Text = "安装程序已启动。如安装器提示文件占用，请先关闭本工具后继续安装。";
        }
        catch (Exception ex)
        {
            UpdateStatusTextBlock.Text = $"下载或安装失败：{ex.Message}";
            OpenUpdateReleaseButton.IsEnabled = true;
            DownloadInstallUpdateButton.IsEnabled = true;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void AppFunctionCatalogButton_OnClick(object sender, RoutedEventArgs e)
    {
        var version = DataContext is ConfiguratorViewModel viewModel
            ? viewModel.AppRecommendationVersion
            : "PCL1";
        var window = new AppFunctionCatalogWindow(version)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void Rex615AccessoriesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new Rex615AccessoryCatalogWindow
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void ApplyLicenseGate()
    {
        var status = LicenseService.GetStatus(LicenseKeyProvider.PublicKeyXmlBase64);
        ConfiguratorTabItem.IsEnabled = status.IsLicensed;
        CnLegacyTabItem.IsEnabled = status.IsLicensed;
        LegacyConversionTabItem.IsEnabled = status.IsLicensed;

        LicenseStatusTextBlock.Text = status.IsLicensed ? "授权有效" : "未授权或授权无效";
        LicenseDetailTextBlock.Text = $"{status.Message}\n授权文件位置：{status.LicensePath}";
        LicenseStatusBorder.Background = BrushFrom(status.IsLicensed ? "#ECFDF5" : "#FFF7ED");
        LicenseStatusBorder.BorderBrush = BrushFrom(status.IsLicensed ? "#10B981" : "#FDBA74");
        LicenseStatusTextBlock.Foreground = BrushFrom(status.IsLicensed ? "#047857" : "#9A3412");

        if (!status.IsLicensed && MainTabControl.SelectedIndex is >= 0 and < 3)
        {
            MainTabControl.SelectedIndex = 3;
        }
    }

    private void ExportRequestButton_OnClick(object sender, RoutedEventArgs e)
    {
        var request = LicenseService.CreateCurrentRequest();
        var dialog = new SaveFileDialog
        {
            FileName = $"REX615_{Environment.MachineName}_{DateTime.Now:yyyyMMddHHmm}{LicenseService.RequestExtension}",
            Filter = $"授权申请文件 (*{LicenseService.RequestExtension})|*{LicenseService.RequestExtension}"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, LicenseService.CreateRequestFileText(request), Encoding.UTF8);
        MessageBox.Show(this, "授权申请文件已导出。", "授权管理", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportActivationButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"激活文件 (*{LicenseService.ActivationExtension})|*{LicenseService.ActivationExtension}|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(dialog.FileName);
            if (!fileInfo.Exists)
            {
                throw new InvalidOperationException("激活文件不存在。");
            }

            if (fileInfo.Length > 1024 * 1024)
            {
                throw new InvalidOperationException("激活文件大小异常。");
            }

            if (!fileInfo.Extension.Equals(LicenseService.ActivationExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"激活文件扩展名必须为 {LicenseService.ActivationExtension}。");
            }

            LicenseService.InstallActivationFile(dialog.FileName, LicenseKeyProvider.PublicKeyXmlBase64);
            ApplyLicenseGate();
            MessageBox.Show(this, "激活文件已导入。", "授权管理", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"激活失败：{ex.Message}", "授权管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfiguratorPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(0);
    }

    private void LegacyConversionPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(2);
    }

    private void CnLegacySelectionPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(1);
    }

    private void NavigateToProtectedTab(int index)
    {
        if (!LicenseService.GetStatus(LicenseKeyProvider.PublicKeyXmlBase64).IsLicensed)
        {
            MainTabControl.SelectedIndex = 3;
            ApplyLicenseGate();
            return;
        }

        MainTabControl.SelectedIndex = index;
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private void RightScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void ValidationMessage_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfiguratorViewModel viewModel ||
            sender is not FrameworkElement { DataContext: ValidationMessageViewModel message })
        {
            return;
        }

        viewModel.JumpToMessage(message);
        if (message.PrimaryTarget is not null)
        {
            Dispatcher.BeginInvoke(() => BringValidationTargetIntoView(message.PrimaryTarget), DispatcherPriority.Background);
        }
    }

    private void ValidationTarget_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ConfiguratorViewModel viewModel ||
            sender is not FrameworkElement { DataContext: ValidationMessageTargetViewModel target })
        {
            return;
        }

        viewModel.JumpToTarget(target);
        Dispatcher.BeginInvoke(() => BringValidationTargetIntoView(target), DispatcherPriority.Background);
    }

    private void SlotCode_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ConfiguratorViewModel viewModel ||
            sender is not FrameworkElement { DataContext: SlotViewModel slot } ||
            !slot.CanJump ||
            string.IsNullOrWhiteSpace(slot.TargetGroupName))
        {
            return;
        }

        var target = new ValidationMessageTargetViewModel(slot.TargetGroupName, slot.TargetOptionId);
        viewModel.JumpToTarget(target);
        Dispatcher.BeginInvoke(() => BringValidationTargetIntoView(target), DispatcherPriority.Background);
    }

    private void SlotDiagram_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: SlotViewModel slot })
        {
            return;
        }

        var diagrams = TerminalDiagramService.GetDiagrams(slot.Code);
        if (diagrams.Count == 0)
        {
            return;
        }

        var window = new TerminalDiagramWindow(slot.Code, diagrams)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void FunctionSuggestion_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguratorViewModel viewModel &&
            sender is FrameworkElement { DataContext: FunctionSuggestionViewModel suggestion })
        {
            viewModel.AddSuggestedFunction(suggestion);
        }
    }

    private void RequestedFunctionRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguratorViewModel viewModel &&
            sender is FrameworkElement { DataContext: RequestedFunctionViewModel function })
        {
            viewModel.RemoveRequestedFunction(function);
        }
    }

    private void BringValidationTargetIntoView(ValidationMessageTargetViewModel targetModel)
    {
        LeftScrollViewer.UpdateLayout();

        FrameworkElement? target = null;
        if (!string.IsNullOrWhiteSpace(targetModel.OptionId))
        {
            target = FindDataContext<OptionViewModel>(
                LeftScrollViewer,
                option => option.Id.Equals(targetModel.OptionId, StringComparison.OrdinalIgnoreCase) &&
                          option.Group.Name.Equals(targetModel.GroupName, StringComparison.OrdinalIgnoreCase));
        }

        target ??= FindDataContext<GroupViewModel>(
            LeftScrollViewer,
            group => group.Name.Equals(targetModel.GroupName, StringComparison.OrdinalIgnoreCase));

        target?.BringIntoView();
    }

    private static FrameworkElement? FindDataContext<T>(DependencyObject root, Func<T, bool> predicate)
        where T : class
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element &&
                element.DataContext is T dataContext &&
                predicate(dataContext))
            {
                return element;
            }

            var result = FindDataContext(child, predicate);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
