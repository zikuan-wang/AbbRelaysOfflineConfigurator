using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.Services;
using Microsoft.Win32;

namespace AbbRelaysOfflineConfigurator;

public partial class AppFunctionCatalogWindow : Window
{
    private readonly AppFunctionCatalogService _catalogService = new();
    private readonly ObservableCollection<AppFunctionCatalogRow> _rows = [];

    public AppFunctionCatalogWindow(string version = "PCL3")
    {
        InitializeComponent();
        VersionComboBox.ItemsSource = new[] { "PCL1", "PCL2", "PCL3" };
        VersionComboBox.SelectedItem = string.IsNullOrWhiteSpace(version) ? "PCL3" : version;
        FunctionDataGrid.ItemsSource = _rows;
        RefreshRows();
    }

    private void RefreshRows()
    {
        var version = VersionComboBox.SelectedItem?.ToString() ?? "PCL3";
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

    private void ExportExcelButton_OnClick(object sender, RoutedEventArgs e)
    {
        var version = VersionComboBox.SelectedItem?.ToString() ?? "PCL3";
        var dialog = new SaveFileDialog
        {
            Title = "Export REX615 APP function table",
            FileName = $"REX615_APP_function_table_{version}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
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
                $"REX615 {version}",
                new[]
                {
                    "ABB Code",
                    "ANSI Code",
                    "Chinese name",
                    "English name",
                    "APP",
                    "PCS",
                    "Principle summary",
                    "Functionality Code",
                    "Source URL"
                },
                _rows.Select(row => new[]
                {
                    row.AbbCode,
                    row.AnsiCode,
                    row.ChineseName,
                    row.EnglishName,
                    row.Apps,
                    row.Pcs.ToString(),
                    row.PrincipleSummary,
                    row.FunctionalityCode,
                    row.SourceUrl
                }),
                new[] { 14d, 14d, 26d, 34d, 20d, 8d, 72d, 22d, 48d });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Export Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
