using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Re630FunctionCatalogWindow : Window
{
    private const string AllDevices = "All";
    private readonly Re630FunctionCatalogService _catalogService = new();
    private readonly ObservableCollection<Re630FunctionCatalogRow> _rows = [];

    public Re630FunctionCatalogWindow(string? selectedDevice = null)
    {
        InitializeComponent();
        var devices = new[] { AllDevices }.Concat(_catalogService.Devices).ToList();
        DeviceComboBox.ItemsSource = devices;
        DeviceComboBox.SelectedItem = devices.FirstOrDefault(device =>
            device.Equals(selectedDevice, StringComparison.OrdinalIgnoreCase)) ?? AllDevices;
        FunctionDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var selectedDevice = DeviceComboBox.SelectedItem?.ToString();
        var filter = SearchTextBox.Text.Trim();
        var rows = _catalogService.Search(selectedDevice, filter)
            .Select(function => new Re630FunctionCatalogRow(
                function.Device,
                LocalizeCategory(function.Category),
                function.Description,
                function.Iec61850,
                function.Iec60617,
                function.Ansi,
                function.Source,
                $"Page {function.Page}"))
            .ToList();

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }
    }

    private static string LocalizeCategory(string category) => category switch
    {
        "Protection" => "Protection / 保护",
        "Protection-related functions" => "Protection-related / 保护相关",
        "Control" => "Control / 控制",
        "Generic process I/O" => "Generic process I/O / 通用过程 I/O",
        "Supervision and monitoring" => "Supervision and monitoring / 监视与监测",
        "Power quality" => "Power quality / 电能质量",
        "Measurement" => "Measurement / 测量",
        "Station communication (GOOSE)" => "Station communication (GOOSE) / 站级通信",
        _ => category
    };

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

public sealed record Re630FunctionCatalogRow(
    string Device,
    string Category,
    string Description,
    string Iec61850,
    string Iec60617,
    string Ansi,
    string Source,
    string PageText);
