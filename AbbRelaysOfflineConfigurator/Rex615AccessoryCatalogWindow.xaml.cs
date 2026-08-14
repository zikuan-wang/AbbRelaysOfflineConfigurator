using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator;

public partial class Rex615AccessoryCatalogWindow : Window
{
    private const string AllCategoryFilter = "全部分类 / All categories";
    private readonly Rex615AccessoryCatalogService _catalogService = new();
    private readonly ObservableCollection<Rex615AccessoryCatalogItem> _rows = [];

    public Rex615AccessoryCatalogWindow()
    {
        InitializeComponent();
        CategoryFilterComboBox.ItemsSource = BuildCategoryFilters();
        CategoryFilterComboBox.SelectedIndex = 0;
        AccessoryDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private IReadOnlyList<string> BuildCategoryFilters() =>
        new[] { AllCategoryFilter }
            .Concat(_catalogService.GetItems()
                .Select(item => item.CategoryDisplay)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
            .ToList();

    private void RefreshRows()
    {
        var rows = _catalogService.GetItems(SearchTextBox.Text.Trim());
        var category = CategoryFilterComboBox.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(category) &&
            !category.Equals(AllCategoryFilter, StringComparison.OrdinalIgnoreCase))
        {
            rows = rows
                .Where(item => item.CategoryDisplay.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }

        CountTextBlock.Text = $"共 {_rows.Count} 项 / {_rows.Count} items";
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void CategoryFilterComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshRows();

    private void CopyProductButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string product } && !string.IsNullOrWhiteSpace(product))
        {
            ClipboardService.TrySetText(product, "REX615");
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
