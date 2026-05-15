using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class CnLegacySelectorView : UserControl
{
    public CnLegacySelectorView()
    {
        InitializeComponent();
    }

    private void ValidationMessage_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CnLegacySelectorViewModel viewModel ||
            sender is not FrameworkElement { DataContext: CnLegacyValidationMessageViewModel message })
        {
            return;
        }

        viewModel.JumpToMessage(message);
        if (message.PrimaryTarget is not null)
        {
            Dispatcher.BeginInvoke(() => BringValidationTargetIntoView(message.PrimaryTarget), DispatcherPriority.Background);
        }
    }

    private void ValidationTarget_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not CnLegacySelectorViewModel viewModel ||
            sender is not FrameworkElement { DataContext: CnLegacyValidationTargetViewModel target })
        {
            return;
        }

        viewModel.JumpToTarget(target);
        Dispatcher.BeginInvoke(() => BringValidationTargetIntoView(target), DispatcherPriority.Background);
    }

    private void BringValidationTargetIntoView(CnLegacyValidationTargetViewModel targetModel)
    {
        SelectionScrollViewer.UpdateLayout();

        FrameworkElement? target = null;
        if (!string.IsNullOrWhiteSpace(targetModel.OptionCode))
        {
            target = FindDataContext<CnLegacyOptionViewModel>(
                SelectionScrollViewer,
                option => option.Code.Equals(targetModel.OptionCode, StringComparison.OrdinalIgnoreCase) &&
                          option.Group.Position.Equals(targetModel.GroupPosition, StringComparison.OrdinalIgnoreCase));
        }

        target ??= FindDataContext<CnLegacyGroupViewModel>(
            SelectionScrollViewer,
            group => group.Position.Equals(targetModel.GroupPosition, StringComparison.OrdinalIgnoreCase));

        target?.BringIntoView();
    }

    private static FrameworkElement? FindDataContext<T>(DependencyObject root, Func<T, bool> predicate)
        where T : class
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element &&
                element.DataContext is T dataContext &&
                predicate(dataContext))
            {
                return element;
            }

            var result = FindDataContext(child, predicate);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private void FunctionSuggestion_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CnLegacySelectorViewModel viewModel &&
            sender is FrameworkElement { DataContext: CnLegacyFunctionSuggestionViewModel suggestion })
        {
            viewModel.AddRequestedFunction(suggestion.Function);
        }
    }

    private void RequestedFunctionRemove_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is CnLegacySelectorViewModel viewModel &&
            sender is FrameworkElement { DataContext: CnLegacyRequestedFunctionViewModel function })
        {
            viewModel.RemoveRequestedFunction(function);
        }
    }

    private void StandardConfigurationRecommendationApply_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CnLegacySelectorViewModel viewModel &&
            sender is FrameworkElement { DataContext: CnLegacyStandardConfigurationRecommendationViewModel recommendation })
        {
            viewModel.ApplyStandardConfigurationRecommendation(recommendation);
        }
    }

    private void FunctionCatalogButton_OnClick(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var selectedDeviceId = (DataContext as CnLegacySelectorViewModel)?.SelectedDevice?.Id;
        var window = new CnLegacyFunctionCatalogWindow(selectedDeviceId)
        {
            Owner = owner
        };
        window.ShowDialog();
    }
}
