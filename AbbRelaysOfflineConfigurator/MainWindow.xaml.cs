using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AbbRelaysLicensing;
using AbbRelaysOfflineConfigurator.Services;
using AbbRelaysOfflineConfigurator.ViewModels;
using MaterialDesignThemes.Wpf;

namespace AbbRelaysOfflineConfigurator;

public partial class MainWindow : Window
{
    private const string DefaultThemeColor = "#018B8D";
    private const int AboutTabIndex = 6;
    private const int Rex600TabIndex = 7;
    private const int Rex640TabIndex = 8;
    private static readonly IReadOnlySet<string> ThemeColorOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "#6ECC54",
        "#D34947",
        "#018B8D",
        "#002FA7",
        "#470125",
        "#F9D46C",
        "#71E2D1",
        "#C8161D",
        "#492D22",
        "#EB5C20",
        "#0D3A69"
    };

    private static readonly IReadOnlyDictionary<string, string> StaticEnglishText = new Dictionary<string, string>
    {
        ["中文"] = "Chinese",
        ["页面导航"] = "Page navigation",
        ["界面设置"] = "Interface settings",
        ["显示完整描述"] = "Show full descriptions",
        ["显示语言"] = "Display language",
        ["主题颜色"] = "Theme color",
        ["莱姆绿"] = "Lime Green",
        ["提香红"] = "Titian Red",
        ["马尔斯绿"] = "Mars Green",
        ["克莱因蓝"] = "Klein Blue",
        ["勃艮第"] = "Burgundy",
        ["申布伦黄"] = "Schonbrunn Yellow",
        ["蒂芙尼蓝"] = "Tiffany Blue",
        ["中国红"] = "Chinese Red",
        ["凡戴克棕"] = "Van Dyck Brown",
        ["爱马仕橙"] = "Hermes Orange",
        ["普鲁士蓝"] = "Prussian Blue",
        ["就绪"] = "Ready",
        ["本地离线选型 / 在线校验 / 授权管理"] = "Offline configuration / Online validation / License management",
        ["首页"] = "Home",
        ["授权 / 关于 / 更新"] = "License / About / Update",
        ["授权/关于"] = "License / About",
        ["REX615 选型"] = "REX615 Configurator",
        ["REX600 选型"] = "REX600 Configurator",
        ["REX640 选型"] = "REX640 Configurator",
        ["RIO600 选型"] = "RIO600 Configurator",
        ["SSC600 选型"] = "SSC600 Configurator",
        ["615/620 CN 选型"] = "615/620 CN Configurator",
        ["615/620 转换"] = "615/620 Conversion",
        ["ABB 继保离线选型工具"] = "ABB Relays Offline Configurator",
        ["选型规则"] = "Selection rules",
        ["REX615 组合代码选项"] = "REX615 combination code options",
        ["全部展开"] = "Expand all",
        ["全部折叠"] = "Collapse all",
        ["组合代码"] = "Combination code",
        ["导入代码"] = "Import code",
        ["导入订货号"] = "Import ordering number",
        ["重置"] = "Reset",
        ["在线校验"] = "Online check",
        ["装置描述"] = "Device description",
        ["复制代码"] = "Copy code",
        ["附件/额外项目"] = "Accessories / extra items",
        ["导出 Word"] = "Export Word",
        ["导出 Excel"] = "Export Excel",
        ["导出 PDF"] = "Export PDF",
        ["在线状态："] = "Online status:",
        ["订货号："] = "Ordering number:",
        ["复制"] = "Copy",
        ["APP 功能推荐"] = "APP function recommendation",
        ["代码表"] = "Code table",
        ["应用推荐"] = "Recommend APPs",
        ["添加"] = "Add",
        ["推送到选型"] = "Apply to selection",
        ["清空功能"] = "Clear functions",
        ["I/O 摘要"] = "I/O summary",
        ["槽位分配"] = "Slot allocation",
        ["校验消息"] = "Validation messages",
        ["授权管理"] = "License management",
        ["离线导出申请文件，授权工具签发激活文件后导入。"] = "Export a local request file, then import the activation file issued by the authorization tool.",
        ["导出申请文件"] = "Export request file",
        ["导入激活文件"] = "Import activation file",
        ["授权流程"] = "Activation workflow",
        ["1. 在本机导出 .zwreq 授权申请文件。\n2. 将申请文件交给授权管理员，或在授权工具中打开。\n3. 授权工具读取申请文件中的机器指纹，填写授权对象和有效期后导出 .zwlic 激活文件。\n4. 将激活文件发回本机，在本页面导入。\n5. 主程序会校验加密封装、RSA 签名、机器指纹和有效期；校验通过后授权立即生效。"] =
            "1. Export a .zwreq request file on this computer.\n2. Send the request file to the license administrator, or open it in the authorization tool.\n3. The authorization tool reads the machine fingerprint, licensee and expiry date, then exports a .zwlic activation file.\n4. Import the activation file on this page.\n5. The main program verifies encryption, RSA signature, machine fingerprint and validity period before enabling the license.",
        ["免责声明"] = "Disclaimer",
        ["本工具仅作为离线选型和组合代码校验辅助，不构成 ABB 官方报价、订货确认、工程设计结论或技术承诺。最终型号、订货号、价格、交期和技术适用性应以 ABB 官方资料、在线校验结果及正式商务文件为准，使用者需自行复核。"] =
            "This tool is only an offline selection and code validation aid. It does not constitute an ABB official quotation, ordering confirmation, engineering design conclusion or technical commitment. Final types, ordering numbers, prices, lead times and technical applicability should be checked against ABB official materials, online validation results and formal commercial documents.",
        ["工具说明"] = "Tool description",
        ["本工具基于本地数据包进行 ABB 继保产品离线选型、互斥校验、槽位分配、I/O 摘要统计、组合代码生成、SSC600/SSC600 SW 订货码生成、615/620 CN 选型、旧订货号转换、RIO600/REX640 选型，以及在线校验订货号。"] =
            "This tool uses local data packages for ABB relay offline selection, mutual-exclusion checks, slot allocation, I/O summaries, combination code generation, SSC600/SSC600 SW order code generation, 615/620 CN selection, legacy order code conversion, RIO600/REX640 selection and online order number validation.",
        ["在线更新"] = "Online update",
        ["更新源固定为 GitHub Releases：zikuan-wang/AbbRelaysOfflineConfigurator_Release。"] =
            "The update source is fixed to GitHub Releases: zikuan-wang/AbbRelaysOfflineConfigurator_Release.",
        ["尚未检查更新。"] = "No update check has been run.",
        ["检查更新"] = "Check update",
        ["打开发布页"] = "Open release page",
        ["下载并安装"] = "Download and install",
        ["版权信息"] = "Copyright",
        ["版权属于 zikuan wang。ABB、REX615、REX640、SSC600、RIO600 及相关产品名称归其权利人所有。本工具仅在本地实现选型辅助和组合代码生成，不复制 ABB 官方在线配置器页面或受版权保护的表现形式。"] =
            "Copyright belongs to zikuan wang. ABB, REX615, REX640, SSC600, RIO600 and related product names belong to their respective owners. This tool only implements local selection assistance and code generation, and does not copy ABB official online configurator pages or protected presentation forms.",
        ["推荐版本"] = "Recommendation version",
        ["模块类型"] = "Module type",
        ["硬件类型"] = "Hardware type",
        ["模块版本"] = "Module version",
        ["RIO600 模块订货清单"] = "RIO600 module order list",
        ["RIO600 不使用整机订货码，按下列模块订货号和数量订货。"] = "RIO600 is ordered by module order numbers and quantities, not by one complete-device order code.",
        ["模块"] = "Module",
        ["描述"] = "Description",
        ["订货号"] = "Ordering number",
        ["数量"] = "Qty",
        ["复制订货清单"] = "Copy order list",
        ["功能清单"] = "Function list",
        ["校验通过"] = "Validation passed",
        ["SSC600 / SSC600 SW 选型规则"] = "SSC600 / SSC600 SW selection rules",
        ["SSC600 订货码"] = "SSC600 order code",
        ["导入订货码"] = "Import order code",
        ["复制订货码"] = "Copy order code",
        ["当前选型摘要"] = "Current selection summary",
        ["应用包推荐"] = "Application package recommendation",
        ["应用包功能清单"] = "Application package function list",
        ["产品系列"] = "Product series",
        ["装置类型"] = "Device type",
        ["615 CN 5.0FP1 / 620 CN 2.0FP1 订货号选项"] = "615 CN 5.0FP1 / 620 CN 2.0FP1 ordering options",
        ["当前选择"] = "Current selection",
        ["当前无离线校验错误。"] = "No offline validation errors.",
        ["标准配置推荐"] = "Standard configuration recommendation",
        ["加入"] = "Add",
        ["清空"] = "Clear",
        ["推送到转换"] = "Send to conversion",
        ["保护功能清单"] = "Protection function list",
        ["615/620 系列订货号转换"] = "615/620 series order code conversion",
        ["离线使用软件内置转换规则并自动判断装置类型；勾选在线转换时直接调用 ABB ConvertCode 接口。"] =
            "Offline conversion uses built-in rules and automatically detects the device type. When online conversion is checked, the ABB ConvertCode API is called directly.",
        ["615/620 订货号"] = "615/620 order code",
        ["自动判断 615/620 装置类型"] = "Auto-detect 615/620 device type",
        ["在线转换 615/620 订货号"] = "Online conversion for 615/620 order codes",
        ["转换并在线校验"] = "Convert and online check",
        ["导出清单"] = "Export list",
        ["转换结果"] = "Conversion results",
        ["方式"] = "Mode",
        ["状态"] = "Status",
        ["REX615订货号"] = "REX615 order number",
        ["REX615组合代码"] = "REX615 combination code",
        ["615/620订货号"] = "615/620 order code"
    };

    private static readonly IReadOnlyDictionary<string, string> StaticChineseText =
        StaticEnglishText.GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ABBRelaysOfflineConfigurator",
        "settings.json");

    private readonly UpdateCheckService _updateCheckService = new();
    private string? _updateReleaseUrl;
    private string? _updateDownloadUrl;
    private string? _updateDownloadAssetName;
    private bool _isSyncingDisplayLanguage;

    public MainWindow()
    {
        InitializeComponent();
        ApplyThemeColor(DefaultThemeColor);
        LoadUserSettings();
        if (DataContext is ConfiguratorViewModel viewModel)
        {
            viewModel.CnLegacySelection.PushToConversionRequested += CnLegacySelection_OnPushToConversionRequested;
            viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            SyncDisplayLanguageComboBox(viewModel.DisplayLanguage);
        }

        MainTabControl.SelectedIndex = 0;
        ApplyChromeLanguage();
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
        NavigateToProtectedTab(5);
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!LicenseService.GetStatus(LicenseKeyProvider.PublicKeyXmlBase64).IsLicensed)
        {
            _ = Dispatcher.BeginInvoke(() => MainTabControl.SelectedIndex = AboutTabIndex, DispatcherPriority.Background);
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
                ? UiText("Open the release page to download the latest installer.", "可打开发布页面下载最新安装包。")
                : UiText($"Installer available: {result.DownloadAssetName}", $"可下载安装包：{result.DownloadAssetName}");
            UpdateStatusTextBlock.Text =
                UiText(
                    $"New version {result.LatestVersion} found. Current version {result.CurrentVersion}. {assetText}",
                    $"发现新版本 {result.LatestVersion}，当前版本 {result.CurrentVersion}。{assetText}");

            var message =
                UiText(
                    $"New version {result.LatestVersion} found.\nCurrent version {result.CurrentVersion}.\n{assetText}\n\nOpen the update page?",
                    $"发现新版本 {result.LatestVersion}。\n当前版本 {result.CurrentVersion}。\n{assetText}\n\n是否打开更新页面？");
            if (MessageBox.Show(this, message, UiText("New version found", "发现新版本"), MessageBoxButton.YesNo, MessageBoxImage.Information) ==
                MessageBoxResult.Yes)
            {
                MainTabControl.SelectedIndex = AboutTabIndex;
            }
        }
        catch
        {
            // Startup update checks must stay silent unless an update is actually available.
        }
    }

    private void AboutLicensePageButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseTransientPopups();
        MainTabControl.SelectedIndex = AboutTabIndex;
    }

    private void NavigationMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        RootDrawerHost.IsLeftDrawerOpen = !RootDrawerHost.IsLeftDrawerOpen;
    }

    private void ThemeColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string color } || string.IsNullOrWhiteSpace(color))
        {
            return;
        }

        ApplyThemeColor(color);
        SaveThemeColor(color);
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfiguratorViewModel.DisplayLanguage) &&
            sender is ConfiguratorViewModel viewModel)
        {
            SyncDisplayLanguageComboBox(viewModel.DisplayLanguage);
            SaveDisplayLanguage(viewModel.DisplayLanguage);
            ApplyChromeLanguage();
        }
    }

    private void DisplayLanguageComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingDisplayLanguage ||
            DataContext is not ConfiguratorViewModel viewModel ||
            DisplayLanguageComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        var language = selectedItem.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(language))
        {
            viewModel.DisplayLanguage = language;
        }
    }

    private void SyncDisplayLanguageComboBox(string displayLanguage)
    {
        if (DisplayLanguageComboBox is null)
        {
            return;
        }

        try
        {
            _isSyncingDisplayLanguage = true;
            foreach (var item in DisplayLanguageComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), displayLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    DisplayLanguageComboBox.SelectedItem = item;
                    return;
                }
            }
        }
        finally
        {
            _isSyncingDisplayLanguage = false;
        }
    }

    private async void CheckUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        OpenUpdateReleaseButton.IsEnabled = false;
        DownloadInstallUpdateButton.IsEnabled = false;
        _updateDownloadUrl = null;
        _updateDownloadAssetName = null;
        UpdateStatusTextBlock.Text = UiText("Checking GitHub Release updates...", "正在检查 GitHub Release 更新...");

        try
        {
            var result = await _updateCheckService.CheckLatestAsync();
            if (!result.IsSuccess)
            {
                _updateReleaseUrl = UpdateCheckService.ReleaseRepositoryUrl + "/releases";
                UpdateStatusTextBlock.Text = UiText($"Check failed: {result.ErrorMessage}", $"检查失败：{result.ErrorMessage}");
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
                    ? UiText("Open the release page to download the latest installer.", "请打开发布页下载最新安装包。")
                    : UiText($"Download available: {result.DownloadAssetName}", $"可下载：{result.DownloadAssetName}");
                DownloadInstallUpdateButton.IsEnabled = !string.IsNullOrWhiteSpace(result.DownloadUrl);
                UpdateStatusTextBlock.Text =
                    UiText(
                        $"New version {result.LatestVersion} found. Current version {result.CurrentVersion}. {assetText}",
                        $"发现新版本 {result.LatestVersion}，当前版本 {result.CurrentVersion}。{assetText}");
            }
            else
            {
                UpdateStatusTextBlock.Text =
                    UiText(
                        $"Already up to date. Current version {result.CurrentVersion}; latest release {result.LatestVersion}.",
                        $"已是最新版本。当前版本 {result.CurrentVersion}，最新发布 {result.LatestVersion}。");
            }
        }
        catch (Exception ex)
        {
            _updateReleaseUrl = UpdateCheckService.ReleaseRepositoryUrl + "/releases";
            UpdateStatusTextBlock.Text = UiText($"Check failed: {ex.Message}", $"检查失败：{ex.Message}");
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
            UpdateStatusTextBlock.Text = UiText("No installer is available. Check for updates first.", "没有可下载的安装包，请先检查更新。");
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
                UpdateStatusTextBlock.Text = UiText($"Downloading installer{percentText}...", $"正在下载安装包{percentText}...");
            });
            var installerPath = await _updateCheckService.DownloadInstallerAsync(
                _updateDownloadUrl,
                _updateDownloadAssetName,
                progress);

            UpdateStatusTextBlock.Text = UiText($"Installer downloaded: {installerPath}. Starting installer...", $"安装包已下载：{installerPath}。正在启动安装程序...");
            UpdateCheckService.StartInstaller(installerPath);
            UpdateStatusTextBlock.Text = UiText(
                "Installer started. If it reports files in use, close this tool before continuing.",
                "安装程序已启动。如安装器提示文件占用，请先关闭本工具后继续安装。");
        }
        catch (Exception ex)
        {
            UpdateStatusTextBlock.Text = UiText($"Download or installation failed: {ex.Message}", $"下载或安装失败：{ex.Message}");
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
        Ssc600TabItem.IsEnabled = status.IsLicensed;
        Rio600TabItem.IsEnabled = status.IsLicensed;
        CnLegacyTabItem.IsEnabled = status.IsLicensed;
        LegacyConversionTabItem.IsEnabled = status.IsLicensed;
        Rex600TabItem.IsEnabled = status.IsLicensed;
        Rex640TabItem.IsEnabled = status.IsLicensed;

        LicenseStatusTextBlock.Text = IsEnglishChrome
            ? status.IsLicensed ? "License valid" : "Not licensed or license invalid"
            : status.IsLicensed ? "授权有效" : "未授权或授权无效";
        var licenseMessage = LocalizeLicenseMessage(status.Message, IsEnglishChrome);
        LicenseDetailTextBlock.Text = IsEnglishChrome
            ? $"{licenseMessage}\nLicense file: {status.LicensePath}"
            : $"{licenseMessage}\n授权文件位置：{status.LicensePath}";
        LicenseStatusBorder.Background = BrushFrom(status.IsLicensed ? "#ECFDF5" : "#FFF7ED");
        LicenseStatusBorder.BorderBrush = BrushFrom(status.IsLicensed ? "#10B981" : "#FDBA74");
        LicenseStatusTextBlock.Foreground = BrushFrom(status.IsLicensed ? "#018B8D" : "#EB5C20");
        HomeLicenseStatusBorder.Background = BrushFrom(status.IsLicensed ? "#ECFDF5" : "#FFF7ED");
        HomeLicenseStatusBorder.BorderBrush = BrushFrom(status.IsLicensed ? "#10B981" : "#FDBA74");
        HomeLicenseStatusTextBlock.Foreground = BrushFrom(status.IsLicensed ? "#018B8D" : "#EB5C20");
        HomeLicenseStatusTextBlock.Text = LicenseStatusTextBlock.Text;
        HomeLicenseDetailTextBlock.Text = LicenseDetailTextBlock.Text;

        if (!status.IsLicensed && IsProtectedTabIndex(MainTabControl.SelectedIndex))
        {
            MainTabControl.SelectedIndex = AboutTabIndex;
        }
    }

    private void ExportRequestButton_OnClick(object sender, RoutedEventArgs e)
    {
        var request = LicenseService.CreateCurrentRequest();
        var dialog = new SaveFileDialog
        {
            FileName = $"ABBRelays_{Environment.MachineName}_{DateTime.Now:yyyyMMddHHmm}{LicenseService.RequestExtension}",
            Filter = UiText(
                $"License request (*{LicenseService.RequestExtension})|*{LicenseService.RequestExtension}",
                $"授权申请文件 (*{LicenseService.RequestExtension})|*{LicenseService.RequestExtension}")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, LicenseService.CreateRequestFileText(request), Encoding.UTF8);
        MessageBox.Show(this, UiText("License request file exported.", "授权申请文件已导出。"), UiText("License management", "授权管理"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportActivationButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiText(
                $"Activation file (*{LicenseService.ActivationExtension})|*{LicenseService.ActivationExtension}|All files (*.*)|*.*",
                $"激活文件 (*{LicenseService.ActivationExtension})|*{LicenseService.ActivationExtension}|所有文件 (*.*)|*.*")
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
                throw new InvalidOperationException(UiText("Activation file does not exist.", "激活文件不存在。"));
            }

            if (fileInfo.Length > 1024 * 1024)
            {
                throw new InvalidOperationException(UiText("Activation file size is abnormal.", "激活文件大小异常。"));
            }

            if (!fileInfo.Extension.Equals(LicenseService.ActivationExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(UiText(
                    $"Activation file extension must be {LicenseService.ActivationExtension}.",
                    $"激活文件扩展名必须为 {LicenseService.ActivationExtension}。"));
            }

            LicenseService.InstallActivationFile(dialog.FileName, LicenseKeyProvider.PublicKeyXmlBase64);
            ApplyLicenseGate();
            MessageBox.Show(this, UiText("Activation file imported.", "激活文件已导入。"), UiText("License management", "授权管理"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiText($"Activation failed: {ex.Message}", $"激活失败：{ex.Message}"), UiText("License management", "授权管理"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HomePageButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseTransientPopups();
        MainTabControl.SelectedIndex = 0;
    }

    private void ConfiguratorPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(1);
    }

    private void LegacyConversionPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(5);
    }

    private void CnLegacySelectionPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(4);
    }

    private void Rio600SelectionPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(3);
    }

    private void Ssc600SelectionPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(2);
    }

    private void Rex600SelectionPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(Rex600TabIndex);
    }

    private void Rex640SelectionPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToProtectedTab(Rex640TabIndex);
    }

    private void HomeFunctionSuggestion_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguratorViewModel viewModel &&
            sender is FrameworkElement { DataContext: HomeFunctionSuggestionViewModel suggestion })
        {
            viewModel.Home.AddSuggestedFunction(suggestion);
        }
    }

    private void HomeRequestedFunctionRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguratorViewModel viewModel &&
            sender is FrameworkElement { DataContext: HomeRequestedFunctionViewModel function })
        {
            viewModel.Home.RemoveRequestedFunction(function);
        }
    }

    private void HomeProductRecommendation_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguratorViewModel viewModel &&
            sender is FrameworkElement { DataContext: HomeProductRecommendationViewModel recommendation })
        {
            if (recommendation.TargetTabIndex == 1 &&
                !string.IsNullOrWhiteSpace(recommendation.RecommendedVersion))
            {
                viewModel.AppRecommendationVersion = recommendation.RecommendedVersion;
            }
            else if (recommendation.TargetTabIndex == Rex640TabIndex &&
                     !string.IsNullOrWhiteSpace(recommendation.RecommendedVersion))
            {
                viewModel.Rex640Selection.AppRecommendationVersion = recommendation.RecommendedVersion;
            }

            NavigateToProtectedTab(recommendation.TargetTabIndex);
        }
    }

    private void NavigateToProtectedTab(int index)
    {
        CloseTransientPopups();
        if (!LicenseService.GetStatus(LicenseKeyProvider.PublicKeyXmlBase64).IsLicensed)
        {
            MainTabControl.SelectedIndex = AboutTabIndex;
            ApplyLicenseGate();
            return;
        }

        MainTabControl.SelectedIndex = index;
    }

    private void MainTabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == MainTabControl)
        {
            UpdateTopBarTitle();
            ApplyStaticLanguage(MainTabControl, IsEnglishChrome);
        }
    }

    private bool IsEnglishChrome => DataContext is ConfiguratorViewModel { IsEnglish: true };

    private string UiText(string english, string chinese) => IsEnglishChrome ? english : chinese;

    private void ApplyChromeLanguage()
    {
        var english = IsEnglishChrome;
        var versionText = english
            ? $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}"
            : $"版本 {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

        Title = english ? "ABB Relays Offline Configurator" : "ABB 继保离线选型工具";
        DrawerTitleTextBlock.Text = english ? "ABB Relays Offline Configurator" : "ABB 继保离线选型工具";
        DrawerSubtitleTextBlock.Text = "";
        NavHomeTextBlock.Text = english ? "Home" : "首页";
        NavRex615TextBlock.Text = english ? "REX615 Configurator" : "REX615 选型";
        NavRex600TextBlock.Text = english ? "REX600 Configurator" : "REX600 选型";
        NavRex640TextBlock.Text = english ? "REX640 Configurator" : "REX640 选型";
        NavSsc600TextBlock.Text = english ? "SSC600 Configurator" : "SSC600 选型";
        NavRio600TextBlock.Text = english ? "RIO600 Configurator" : "RIO600 选型";
        NavCnLegacyTextBlock.Text = english ? "615/620 CN Configurator" : "615/620 CN 选型";
        NavLegacyConversionTextBlock.Text = english ? "615/620 Conversion" : "615/620 转换";
        NavAboutTextBlock.Text = english ? "License / About / Update" : "授权 / 关于 / 更新";
        NavigationMenuButton.ToolTip = english ? "Page navigation" : "页面导航";
        SettingsPopupBox.ToolTip = english ? "Settings" : "设置";
        SettingsTitleTextBlock.Text = english ? "Interface settings" : "界面设置";
        UseFullDescriptionCheckBox.Content = english ? "Show full descriptions" : "显示完整描述";
        DisplayLanguageLabelTextBlock.Text = english ? "Display language" : "显示语言";
        ThemeColorLabelTextBlock.Text = english ? "Theme color" : "主题颜色";
        ThemeLimeGreenTextBlock.Text = english ? "Lime Green" : "莱姆绿";
        ThemeTitianRedTextBlock.Text = english ? "Titian Red" : "提香红";
        ThemeMarsGreenTextBlock.Text = english ? "Mars Green" : "马尔斯绿";
        ThemeKleinBlueTextBlock.Text = english ? "Klein Blue" : "克莱因蓝";
        ThemeBurgundyTextBlock.Text = english ? "Burgundy" : "勃艮第";
        ThemeSchonbrunnYellowTextBlock.Text = english ? "Schonbrunn Yellow" : "申布伦黄";
        ThemeTiffanyBlueTextBlock.Text = english ? "Tiffany Blue" : "蒂芙尼蓝";
        ThemeChineseRedTextBlock.Text = english ? "Chinese Red" : "中国红";
        ThemeVanDyckBrownTextBlock.Text = english ? "Van Dyck Brown" : "凡戴克棕";
        ThemeHermesOrangeTextBlock.Text = english ? "Hermes Orange" : "爱马仕橙";
        ThemePrussianBlueTextBlock.Text = english ? "Prussian Blue" : "普鲁士蓝";
        StatusReadyTextBlock.Text = english ? "Ready" : "就绪";
        StatusSummaryTextBlock.Text = english
            ? "Offline configuration / Online validation / License management"
            : "本地离线选型 / 在线校验 / 授权管理";
        AboutVersionTextBlock.Text = versionText;
        StatusVersionTextBlock.Text = versionText;

        HomeTabItem.Header = english ? "ABB Relays Offline Configurator" : "ABB 继保离线选型工具";
        ConfiguratorTabItem.Header = english ? "REX615 Configurator" : "REX615 选型";
        Ssc600TabItem.Header = english ? "SSC600 Configurator" : "SSC600 选型";
        Rio600TabItem.Header = english ? "RIO600 Configurator" : "RIO600 选型";
        CnLegacyTabItem.Header = english ? "615/620 CN Configurator" : "615/620 CN 选型";
        LegacyConversionTabItem.Header = english ? "615/620 Conversion" : "615/620 转换";
        AboutLicenseTabItem.Header = english ? "License / About" : "授权/关于";
        Rex600TabItem.Header = english ? "REX600 Configurator" : "REX600 选型";
        Rex640TabItem.Header = english ? "REX640 Configurator" : "REX640 选型";

        ApplyStaticLanguage(this, english);
        UpdateTopBarTitle();
        ApplyLicenseGate();
    }

    private void UpdateTopBarTitle()
    {
        if (TopBarTitleTextBlock is null)
        {
            return;
        }

        TopBarTitleTextBlock.Text = PageTitleForIndex(MainTabControl.SelectedIndex);
    }

    private string PageTitleForIndex(int index)
    {
        var english = IsEnglishChrome;
        return index switch
        {
            0 => english ? "ABB Relays Offline Configurator" : "ABB 继保离线选型工具",
            1 => english ? "REX615 Configurator" : "REX615 选型",
            2 => english ? "SSC600 Configurator" : "SSC600 选型",
            3 => english ? "RIO600 Configurator" : "RIO600 选型",
            4 => english ? "615/620 CN Configurator" : "615/620 CN 选型",
            5 => english ? "615/620 Conversion" : "615/620 转换",
            AboutTabIndex => english ? "License / About / Update" : "授权 / 关于 / 更新",
            Rex600TabIndex => english ? "REX600 Configurator" : "REX600 选型",
            Rex640TabIndex => english ? "REX640 Configurator" : "REX640 选型",
            _ => english ? "ABB Relays Offline Configurator" : "ABB 继保离线选型工具"
        };
    }

    private static bool IsProtectedTabIndex(int index) =>
        index is >= 1 and <= 5 || index == Rex600TabIndex || index == Rex640TabIndex;

    private static string LocalizeLicenseMessage(string message, bool english)
    {
        if (!english)
        {
            return message;
        }

        if (message.Equals("未激活。", StringComparison.OrdinalIgnoreCase))
        {
            return "Not activated.";
        }

        if (message.Equals("激活文件不属于当前电脑。", StringComparison.OrdinalIgnoreCase))
        {
            return "The activation file does not belong to this computer.";
        }

        if (message.StartsWith("授权已过期：", StringComparison.OrdinalIgnoreCase))
        {
            return "License expired: " + message["授权已过期：".Length..].TrimEnd('。') + ".";
        }

        if (message.StartsWith("已授权给 ", StringComparison.OrdinalIgnoreCase))
        {
            var body = message["已授权给 ".Length..].TrimEnd('。');
            var separatorIndex = body.LastIndexOf('，');
            if (separatorIndex > 0)
            {
                var licensedTo = body[..separatorIndex];
                var expireText = body[(separatorIndex + 1)..];
                var localizedExpireText = expireText.StartsWith("有效期至 ", StringComparison.OrdinalIgnoreCase)
                    ? "valid until " + expireText["有效期至 ".Length..]
                    : expireText.Equals("永久授权", StringComparison.OrdinalIgnoreCase) ? "perpetual license" : expireText;
                return $"Licensed to {licensedTo}, {localizedExpireText}.";
            }
        }

        if (message.StartsWith("激活文件无效：", StringComparison.OrdinalIgnoreCase))
        {
            return "Activation file invalid: " + message["激活文件无效：".Length..];
        }

        return message;
    }

    private static void ApplyStaticLanguage(DependencyObject root, bool english)
    {
        var visited = new HashSet<DependencyObject>();
        foreach (var element in EnumerateElements(root, visited))
        {
            switch (element)
            {
                case TextBlock textBlock
                    when BindingOperations.GetBindingExpression(textBlock, TextBlock.TextProperty) is null:
                    textBlock.Text = TranslateStaticText(textBlock.Text, english);
                    break;
                case HeaderedContentControl headeredContentControl
                    when BindingOperations.GetBindingExpression(headeredContentControl, HeaderedContentControl.HeaderProperty) is null &&
                         headeredContentControl.Header is string header:
                    headeredContentControl.Header = TranslateStaticText(header, english);
                    break;
                case ContentControl contentControl
                    when BindingOperations.GetBindingExpression(contentControl, ContentControl.ContentProperty) is null &&
                         contentControl.Content is string content:
                    contentControl.Content = TranslateStaticText(content, english);
                    break;
                case HeaderedItemsControl headeredItemsControl
                    when BindingOperations.GetBindingExpression(headeredItemsControl, HeaderedItemsControl.HeaderProperty) is null &&
                         headeredItemsControl.Header is string header:
                    headeredItemsControl.Header = TranslateStaticText(header, english);
                    break;
                case DataGrid dataGrid:
                    foreach (var column in dataGrid.Columns)
                    {
                        if (BindingOperations.GetBindingExpression(column, DataGridColumn.HeaderProperty) is null &&
                            column.Header is string columnHeader)
                        {
                            column.Header = TranslateStaticText(columnHeader, english);
                        }
                    }
                    break;
            }

            if (element is FrameworkElement frameworkElement &&
                BindingOperations.GetBindingExpression(frameworkElement, HintAssist.HintProperty) is null &&
                HintAssist.GetHint(frameworkElement) is string hint)
            {
                HintAssist.SetHint(frameworkElement, TranslateStaticText(hint, english));
            }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateElements(
        DependencyObject root,
        ISet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            yield break;
        }

        yield return root;

        var visualChildrenCount = 0;
        try
        {
            visualChildrenCount = VisualTreeHelper.GetChildrenCount(root);
        }
        catch (InvalidOperationException)
        {
            // Some content objects are not visual tree participants.
        }

        for (var index = 0; index < visualChildrenCount; index++)
        {
            foreach (var child in EnumerateElements(VisualTreeHelper.GetChild(root, index), visited))
            {
                yield return child;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var nested in EnumerateElements(child, visited))
            {
                yield return nested;
            }
        }
    }

    private static string TranslateStaticText(string text, bool english)
    {
        var source = english ? StaticEnglishText : StaticChineseText;
        return source.TryGetValue(text, out var translated) ? translated : text;
    }

    private void CloseTransientPopups()
    {
        RootDrawerHost.IsLeftDrawerOpen = false;
    }

    private void LoadUserSettings()
    {
        try
        {
            var settings = ReadSettings();
            if (!string.IsNullOrWhiteSpace(settings?.ThemeColor))
            {
                var color = settings.ThemeColor.Equals("#512DA8", StringComparison.OrdinalIgnoreCase) ||
                            !ThemeColorOptions.Contains(settings.ThemeColor)
                    ? DefaultThemeColor
                    : settings.ThemeColor;
                ApplyThemeColor(color);
            }

            if (DataContext is ConfiguratorViewModel viewModel &&
                !string.IsNullOrWhiteSpace(settings?.DisplayLanguage))
            {
                viewModel.DisplayLanguage = settings.DisplayLanguage;
            }
        }
        catch
        {
            // Invalid local UI settings should not block application startup.
        }
    }

    private static AppSettings? ReadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath, Encoding.UTF8));
    }

    private static void SaveThemeColor(string color) =>
        SaveSettings(settings => settings.ThemeColor = color);

    private static void SaveDisplayLanguage(string displayLanguage) =>
        SaveSettings(settings => settings.DisplayLanguage = displayLanguage);

    private static void SaveSettings(Action<AppSettings> update)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = ReadSettings() ?? new AppSettings();
            update(settings);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
        }
        catch
        {
            // Theme color persistence is best-effort; the live theme has already been applied.
        }
    }

    private void ApplyThemeColor(string colorText)
    {
        if (ColorConverter.ConvertFromString(colorText) is not Color accent)
        {
            return;
        }

        SetBrush("AccentBrush", accent);
        SetBrush("AccentSoftBrush", Mix(accent, Colors.White, 0.9));
        SetBrush("AppBarBackgroundBrush", Mix(accent, Colors.Black, 0.18));
        SetBrush("AppBarSearchBackgroundBrush", Colors.White);
        ApplyMaterialDesignPalette(accent);
    }

    private static void ApplyMaterialDesignPalette(Color accent)
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetPrimaryColor(accent);
        theme.SetSecondaryColor(accent);
        paletteHelper.SetTheme(theme);
    }

    private void SetBrush(string key, Color color)
    {
        Resources[key] = new SolidColorBrush(color);
    }

    private static Color Mix(Color source, Color target, double targetWeight)
    {
        targetWeight = Math.Clamp(targetWeight, 0, 1);
        var sourceWeight = 1 - targetWeight;
        return Color.FromRgb(
            (byte)Math.Round(source.R * sourceWeight + target.R * targetWeight),
            (byte)Math.Round(source.G * sourceWeight + target.G * targetWeight),
            (byte)Math.Round(source.B * sourceWeight + target.B * targetWeight));
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private sealed class AppSettings
    {
        public string? ThemeColor { get; set; }
        public string? DisplayLanguage { get; set; }
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
