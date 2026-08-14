using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Re611SelectorView : UserControl
{
    public Re611SelectorView()
    {
        InitializeComponent();
    }

    private void FunctionCatalogButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedDevice = DataContext is Re611SelectionViewModel viewModel
            ? viewModel.SelectedDeviceFilter
            : null;
        var selectedVersion = DataContext is Re611SelectionViewModel versionViewModel
            ? versionViewModel.SelectedVersion?.Code
            : null;

        var window = new Re611FunctionCatalogWindow(selectedDevice, selectedVersion)
        {
            Owner = System.Windows.Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    private void Re611OptionRow_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Re611OptionViewModel { IsAvailable: true } option })
        {
            option.IsSelected = true;
        }
    }

    private void FunctionSuggestion_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is Re611SelectionViewModel viewModel &&
            sender is System.Windows.FrameworkElement { DataContext: Re611FunctionSuggestionViewModel suggestion })
        {
            viewModel.AddRequestedFunction(suggestion.Function);
        }
    }

    private void RequestedFunctionRemove_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is Re611SelectionViewModel viewModel &&
            sender is System.Windows.FrameworkElement { DataContext: Re611RequestedFunctionViewModel function })
        {
            viewModel.RemoveRequestedFunction(function);
        }
    }

    private void StandardConfigurationRecommendationApply_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is Re611SelectionViewModel viewModel &&
            sender is System.Windows.FrameworkElement { DataContext: Re611StandardConfigurationRecommendationViewModel recommendation })
        {
            viewModel.ApplyStandardConfigurationRecommendation(recommendation);
        }
    }
}
