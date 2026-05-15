using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class CnLegacyFunctionCatalogWindow : Window
{
    private readonly CnLegacyFunctionCatalogService _catalogService = new();
    private readonly ObservableCollection<CnLegacyFunctionCatalogRow> _rows = [];

    public CnLegacyFunctionCatalogWindow(string? selectedDeviceId = null)
    {
        InitializeComponent();

        var devices = new List<CnLegacyDeviceFilterItem>
        {
            new("", "全部")
        };
        devices.AddRange(_catalogService.Devices
            .OrderBy(device => device.SeriesId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(device => new CnLegacyDeviceFilterItem(device.DeviceId, device.DeviceName)));

        DeviceComboBox.ItemsSource = devices;
        DeviceComboBox.DisplayMemberPath = nameof(CnLegacyDeviceFilterItem.Name);
        DeviceComboBox.SelectedItem = devices.FirstOrDefault(device =>
            !string.IsNullOrWhiteSpace(selectedDeviceId) &&
            device.DeviceId.Equals(selectedDeviceId, StringComparison.OrdinalIgnoreCase)) ?? devices[0];

        FunctionDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var filter = SearchTextBox.Text.Trim();
        var selectedDevice = DeviceComboBox.SelectedItem as CnLegacyDeviceFilterItem;
        var deviceId = string.IsNullOrWhiteSpace(selectedDevice?.DeviceId) ? null : selectedDevice.DeviceId;

        var rows = _catalogService.GetFunctions(deviceId)
            .Where(function => MatchesFilter(function, filter))
            .OrderBy(function => function.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(function => function.AbbCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(function => function.AnsiCode, StringComparer.OrdinalIgnoreCase)
            .Select(function => new CnLegacyFunctionCatalogRow(
                function.DeviceName,
                FormatConfigurations(function.Configs),
                function.Category,
                function.AbbCode,
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

    private static bool MatchesFilter(CnLegacyFunctionEntry function, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return function.DeviceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               function.AbbCode.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
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

    private void DeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshRows();
        }
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}

public sealed record CnLegacyDeviceFilterItem(string DeviceId, string Name);

public sealed record CnLegacyFunctionCatalogRow(
    string DeviceName,
    string Configurations,
    string Category,
    string AbbCode,
    string AnsiCode,
    string ChineseName,
    string EnglishName,
    int SourcePage);
