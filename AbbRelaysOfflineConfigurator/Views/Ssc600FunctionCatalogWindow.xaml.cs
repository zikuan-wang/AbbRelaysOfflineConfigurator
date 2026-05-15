using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Ssc600FunctionCatalogWindow : Window
{
    private readonly IReadOnlyList<Ssc600FunctionCatalogItemViewModel> _items;
    private readonly ObservableCollection<Ssc600FunctionCatalogRow> _rows = [];

    public Ssc600FunctionCatalogWindow(ObservableCollection<Ssc600FunctionCatalogItemViewModel> items)
    {
        InitializeComponent();
        _items = items.ToList();
        FunctionDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var filter = SearchTextBox.Text.Trim();
        var rows = _items
            .Where(item => MatchesFilter(item, filter))
            .OrderBy(item => item.Package, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AbbCode, StringComparer.OrdinalIgnoreCase)
            .Select(item => new Ssc600FunctionCatalogRow(
                item.Package,
                item.AbbCode,
                item.AnsiCode,
                item.ChineseName,
                item.EnglishName,
                string.IsNullOrWhiteSpace(item.AnsiCode)
                    ? "该功能用于 SSC600 应用包推荐，未配置 ANSI Code。"
                    : $"该功能用于 SSC600 应用包推荐，ANSI Code：{item.AnsiCode}。"))
            .ToList();

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }
    }

    private static bool MatchesFilter(Ssc600FunctionCatalogItemViewModel item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return item.Package.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.PackageEnglish.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.AbbCode.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.AnsiCode.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.ChineseName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.EnglishName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}

public sealed record Ssc600FunctionCatalogRow(
    string Package,
    string AbbCode,
    string AnsiCode,
    string ChineseName,
    string EnglishName,
    string Description);
