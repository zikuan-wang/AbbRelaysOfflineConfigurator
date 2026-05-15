using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Rex600FunctionCatalogWindow : Window
{
    private readonly Rex600FunctionCatalogService _catalogService = new();
    private readonly ObservableCollection<Rex600FunctionEntry> _rows = [];

    public Rex600FunctionCatalogWindow()
    {
        InitializeComponent();
        FunctionDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var rows = _catalogService.Search(SearchTextBox.Text)
            .OrderBy(function => CategoryOrder(function.Category))
            .ThenBy(function => function.Iec61850, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }
    }

    private static int CategoryOrder(string category) => category switch
    {
        "Protection" => 1,
        "Control" => 2,
        "Measurement" => 3,
        "IED configuration" => 4,
        "Logging" => 5,
        "Communication" => 6,
        "Local HMI" => 7,
        "Other" => 8,
        _ => 99
    };

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
