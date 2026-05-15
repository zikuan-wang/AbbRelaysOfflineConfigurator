using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Rex640FunctionCatalogWindow : Window
{
    private readonly Rex640AppFunctionCatalogService _catalogService = new();
    private readonly ObservableCollection<Rex640FunctionCatalogRow> _rows = [];

    public Rex640FunctionCatalogWindow(string version = "PCL6")
    {
        InitializeComponent();
        VersionComboBox.ItemsSource = new[] { "PCL5", "PCL6" };
        VersionComboBox.SelectedItem = string.IsNullOrWhiteSpace(version) ? "PCL6" : version.ToUpperInvariant();
        FunctionDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var version = VersionComboBox.SelectedItem?.ToString() ?? "PCL6";
        var filter = SearchTextBox.Text.Trim();
        var rows = _catalogService.GetFunctions(version)
            .Where(function => MatchesFilter(function, filter))
            .Select(function => new Rex640FunctionCatalogRow(
                function.Pcl,
                string.IsNullOrWhiteSpace(function.CategoryChinese) ? function.Category : function.CategoryChinese,
                function.Code,
                function.Ansi,
                function.ChineseName,
                function.EnglishName,
                function.Apps.Count == 0 ? "基础功能" : string.Join(", ", function.Apps),
                function.Pcs,
                function.Description,
                $"来源：REX640 Product Guide {function.Pcl}，第 {function.SourcePage} 页"))
            .ToList();

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }
    }

    private static bool MatchesFilter(Rex640FunctionEntry function, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return function.Pcl.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.Code.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.Ansi.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.Iec60617.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.ChineseName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.EnglishName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.CategoryChinese.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.Apps.Any(app => app.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void VersionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshRows();
        }
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}

public sealed record Rex640FunctionCatalogRow(
    string Pcl,
    string Category,
    string AbbCode,
    string AnsiCode,
    string ChineseName,
    string EnglishName,
    string Apps,
    int Pcs,
    string Description,
    string SourceText);
