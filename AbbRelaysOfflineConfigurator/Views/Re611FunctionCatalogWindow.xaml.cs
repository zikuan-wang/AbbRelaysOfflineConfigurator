using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.Services;
using Microsoft.Win32;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Re611FunctionCatalogWindow : Window
{
    private readonly Re611FunctionCatalogService _catalogService = new();
    private readonly ObservableCollection<Re611FunctionCatalogRow> _rows = [];

    public Re611FunctionCatalogWindow(string? selectedDeviceId = null, string? selectedVersionCode = null)
    {
        InitializeComponent();

        var devices = new List<Re611CatalogFilterItem>
        {
            new("", "全部")
        };
        devices.AddRange(_catalogService.GetFunctions()
            .Select(function => function.DeviceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(device => device, StringComparer.OrdinalIgnoreCase)
            .Select(device => new Re611CatalogFilterItem(device, device)));

        DeviceComboBox.ItemsSource = devices;
        DeviceComboBox.DisplayMemberPath = nameof(Re611CatalogFilterItem.Name);
        DeviceComboBox.SelectedItem = devices.FirstOrDefault(device =>
            !string.IsNullOrWhiteSpace(selectedDeviceId) &&
            device.Code.Equals(selectedDeviceId, StringComparison.OrdinalIgnoreCase)) ?? devices[0];

        var versions = new List<Re611CatalogFilterItem>
        {
            new("", "全部"),
            new("XE", "1.0"),
            new("1G", "2.0")
        };
        VersionComboBox.ItemsSource = versions;
        VersionComboBox.DisplayMemberPath = nameof(Re611CatalogFilterItem.Name);
        VersionComboBox.SelectedItem = versions.FirstOrDefault(version =>
            !string.IsNullOrWhiteSpace(selectedVersionCode) &&
            version.Code.Equals(selectedVersionCode, StringComparison.OrdinalIgnoreCase)) ?? versions[0];

        FunctionDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var filter = SearchTextBox.Text.Trim();
        var selectedDevice = DeviceComboBox.SelectedItem as Re611CatalogFilterItem;
        var selectedVersion = VersionComboBox.SelectedItem as Re611CatalogFilterItem;
        var deviceId = string.IsNullOrWhiteSpace(selectedDevice?.Code) ? null : selectedDevice.Code;
        var versionCode = string.IsNullOrWhiteSpace(selectedVersion?.Code) ? null : selectedVersion.Code;

        var rows = _catalogService.GetFunctions(deviceId, versionCode)
            .Where(function => MatchesFilter(function, filter))
            .OrderBy(function => function.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(function => function.ProductVersion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(function => FormatConfigurations(function.Configs), StringComparer.OrdinalIgnoreCase)
            .ThenBy(function => function.Iec61850, StringComparer.OrdinalIgnoreCase)
            .Select(function => new Re611FunctionCatalogRow(
                function.DeviceId,
                function.ProductVersion,
                FormatConfigurations(function.Configs),
                function.Category,
                function.Iec61850,
                function.AnsiCode,
                function.ChineseName,
                function.EnglishName,
                function.SourcePage))
            .ToList();

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }
    }

    private static bool MatchesFilter(Re611FunctionEntry function, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return function.DeviceId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.ProductVersion.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.Iec61850.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.AnsiCode.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.ChineseName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.EnglishName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.Configs.Any(config =>
                   config.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   config.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatConfigurations(IReadOnlyDictionary<string, string> configs) =>
        configs.Count == 0
            ? "-"
            : string.Join("；", configs
                .OrderBy(config => config.Key, StringComparer.OrdinalIgnoreCase)
                .Select(config => string.IsNullOrWhiteSpace(config.Value)
                    ? config.Key
                    : $"{config.Key}={config.Value}"));

    private void FilterComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshRows();
        }
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void ExportExcelButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedDevice = DeviceComboBox.SelectedItem as Re611CatalogFilterItem;
        var selectedVersion = VersionComboBox.SelectedItem as Re611CatalogFilterItem;
        var deviceText = string.IsNullOrWhiteSpace(selectedDevice?.Code) ? "All" : selectedDevice.Code;
        var versionText = string.IsNullOrWhiteSpace(selectedVersion?.Code) ? "All" : selectedVersion.Name;

        var dialog = new SaveFileDialog
        {
            Title = "Export RE_611 function catalog",
            FileName = $"RE_611_function_catalog_{deviceText}_{versionText}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            Filter = "Excel workbook (*.xlsx)|*.xlsx"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CatalogExcelExportService.Export(
                dialog.FileName,
                "RE_611 Functions",
                new[]
                {
                    "Product",
                    "Product version",
                    "Standard configuration",
                    "Category",
                    "IEC 61850",
                    "ANSI Code",
                    "Chinese name",
                    "English name",
                    "Source page"
                },
                _rows.Select(row => new[]
                {
                    row.DeviceId,
                    row.ProductVersion,
                    row.Configurations,
                    row.Category,
                    row.Iec61850,
                    row.AnsiCode,
                    row.ChineseName,
                    row.EnglishName,
                    row.SourcePage.ToString()
                }),
                new[] { 12d, 12d, 22d, 14d, 16d, 16d, 34d, 38d, 10d });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Export Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}

public sealed record Re611CatalogFilterItem(string Code, string Name);

public sealed record Re611FunctionCatalogRow(
    string DeviceId,
    string ProductVersion,
    string Configurations,
    string Category,
    string Iec61850,
    string AnsiCode,
    string ChineseName,
    string EnglishName,
    int SourcePage);
