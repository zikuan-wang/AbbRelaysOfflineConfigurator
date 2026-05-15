using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Rex600SelectorView : UserControl
{
    public Rex600SelectorView()
    {
        InitializeComponent();
    }

    private void ValidationMessage_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Rex600SelectionViewModel viewModel ||
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
        if (DataContext is not Rex600SelectionViewModel viewModel ||
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
        var target = FindDataContext<Rex600GroupViewModel>(
            SelectionScrollViewer,
            group => group.DisplayName.Equals(targetModel.GroupName, StringComparison.OrdinalIgnoreCase));
        target?.BringIntoView();
    }

    private void FunctionCatalogButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new Rex600FunctionCatalogWindow
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
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
