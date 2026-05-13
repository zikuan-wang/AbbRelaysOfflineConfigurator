using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Rex615OfflineConfigurator.Services;

namespace Rex615OfflineConfigurator;

public partial class Rex615AccessoryCatalogWindow : Window
{
    private readonly Rex615AccessoryCatalogService _catalogService = new();
    private readonly ObservableCollection<Rex615AccessoryCatalogItem> _rows = [];

    public Rex615AccessoryCatalogWindow()
    {
        InitializeComponent();
        AccessoryDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var rows = _catalogService.GetItems(SearchTextBox.Text.Trim());

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }

        CountTextBlock.Text = $"共 {_rows.Count} 项 / {_rows.Count} items";
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void CopyProductButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string product } && !string.IsNullOrWhiteSpace(product))
        {
            Clipboard.SetText(product);
        }
    }

    private void OpenImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string imageUrl } || string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(imageUrl) { UseShellExecute = true });
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
