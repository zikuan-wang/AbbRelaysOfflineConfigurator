using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Rex615OfflineConfigurator.Services;

namespace Rex615OfflineConfigurator;

public partial class AppFunctionCatalogWindow : Window
{
    private readonly AppFunctionCatalogService _catalogService = new();
    private readonly ObservableCollection<AppFunctionCatalogRow> _rows = [];

    public AppFunctionCatalogWindow(string version = "PCL1")
    {
        InitializeComponent();
        VersionComboBox.ItemsSource = new[] { "PCL1", "PCL2" };
        VersionComboBox.SelectedItem = string.IsNullOrWhiteSpace(version) ? "PCL1" : version;
        FunctionDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var version = VersionComboBox.SelectedItem?.ToString() ?? "PCL1";
        var filter = SearchTextBox.Text.Trim();
        var rows = _catalogService.GetFunctions(version)
            .Where(function => MatchesFilter(function, filter))
            .OrderBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
            .Select(function => new AppFunctionCatalogRow(
                function.Code,
                function.Ansi,
                function.ChineseName,
                function.EnglishName,
                function.Apps.Count == 0 ? "基础功能" : string.Join(", ", function.Apps),
                function.Pcs,
                function.PrincipleSummary,
                string.IsNullOrWhiteSpace(function.FunctionalityCode) ? function.Code : function.FunctionalityCode,
                function.PrincipleSourceUrl))
            .ToList();

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }
    }

    private static bool MatchesFilter(AppFunctionEntry function, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return function.Code.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.Ansi.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.ChineseName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.EnglishName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.PrincipleSummary.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            function.PrincipleSource.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
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

public sealed record AppFunctionCatalogRow(
    string AbbCode,
    string AnsiCode,
    string ChineseName,
    string EnglishName,
    string Apps,
    int Pcs,
    string PrincipleSummary,
    string FunctionalityCode,
    string SourceUrl);
