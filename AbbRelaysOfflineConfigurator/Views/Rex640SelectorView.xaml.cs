using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AbbRelaysOfflineConfigurator.Services;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Rex640SelectorView : UserControl
{
    public Rex640SelectorView()
    {
        InitializeComponent();
    }

    private void ValidationMessage_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Rex640SelectionViewModel viewModel ||
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
        if (DataContext is not Rex640SelectionViewModel viewModel ||
            sender is not FrameworkElement { DataContext: ValidationMessageTargetViewModel target })
        {
            return;
        }

        viewModel.JumpToTarget(target);
        Dispatcher.BeginInvoke(() => BringValidationTargetIntoView(target), DispatcherPriority.Background);
    }

    private void SlotCode_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not Rex640SelectionViewModel viewModel ||
            sender is not FrameworkElement { DataContext: Rex640SlotViewModel slot } ||
            !slot.CanJump ||
            string.IsNullOrWhiteSpace(slot.TargetGroupName))
        {
            return;
        }

        var target = new ValidationMessageTargetViewModel(slot.TargetGroupName, slot.TargetOptionId);
        viewModel.JumpToTarget(target);
        Dispatcher.BeginInvoke(() => BringValidationTargetIntoView(target), DispatcherPriority.Background);
    }

    private void SlotDiagram_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: Rex640SlotViewModel slot })
        {
            return;
        }

        var diagrams = TerminalDiagramService.GetDiagrams("REX640", slot.Code);
        if (diagrams.Count == 0)
        {
            return;
        }

        var window = new TerminalDiagramWindow(slot.Code, diagrams)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    private void Rex640FunctionCatalogButton_OnClick(object sender, RoutedEventArgs e)
    {
        var version = DataContext is Rex640SelectionViewModel viewModel
            ? viewModel.AppRecommendationVersion
            : "PCL6";
        var window = new Rex640FunctionCatalogWindow(version)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    private void BringValidationTargetIntoView(ValidationMessageTargetViewModel targetModel)
    {
        SelectionScrollViewer.UpdateLayout();
        var target = FindDataContext<Rex640GroupViewModel>(
            SelectionScrollViewer,
            group => group.Rule.Name.Equals(targetModel.GroupName, StringComparison.OrdinalIgnoreCase));
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
