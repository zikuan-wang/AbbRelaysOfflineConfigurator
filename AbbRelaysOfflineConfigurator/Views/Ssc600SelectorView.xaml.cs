using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Ssc600SelectorView : UserControl
{
    public Ssc600SelectorView()
    {
        InitializeComponent();
    }

    private void FunctionSuggestion_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is Ssc600SelectionViewModel viewModel &&
            sender is FrameworkElement { DataContext: Ssc600FunctionSuggestionViewModel suggestion })
        {
            viewModel.AddSuggestedFunction(suggestion);
        }
    }

    private void RequestedFunctionRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is Ssc600SelectionViewModel viewModel &&
            sender is FrameworkElement { DataContext: Ssc600RequestedFunctionViewModel function })
        {
            viewModel.RemoveRequestedFunction(function);
        }
    }

    private void FunctionCatalogButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Ssc600SelectionViewModel viewModel)
        {
            return;
        }

        var window = new Ssc600FunctionCatalogWindow(viewModel.FunctionCatalogItems)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    private void ValidationMessage_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Ssc600SelectionViewModel viewModel ||
            sender is not FrameworkElement { DataContext: ValidationMessageViewModel message } ||
            message.IsSuccess)
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
        if (DataContext is not Ssc600SelectionViewModel viewModel ||
            sender is not FrameworkElement { DataContext: ValidationMessageTargetViewModel target })
        {
            return;
        }

        viewModel.JumpToTarget(target);
        Dispatcher.BeginInvoke(() => BringValidationTargetIntoView(target), DispatcherPriority.Background);
    }

    private void BringValidationTargetIntoView(ValidationMessageTargetViewModel targetModel)
    {
        SelectionScrollViewer.UpdateLayout();
        var target = FindDataContext<Ssc600GroupViewModel>(
            SelectionScrollViewer,
            group => group.DisplayName.Equals(targetModel.GroupName, StringComparison.OrdinalIgnoreCase));
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
}
