using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Rio600SelectorView : UserControl
{
    public Rio600SelectorView()
    {
        InitializeComponent();
    }

    private void ModuleDetailCard_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string detailKey } ||
            string.IsNullOrWhiteSpace(detailKey))
        {
            return;
        }

        OpenModuleDetail(detailKey);
    }

    private void CopyModuleOrderNumberButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string orderNumber } &&
            !string.IsNullOrWhiteSpace(orderNumber))
        {
            Clipboard.SetText(orderNumber);
            e.Handled = true;
        }
    }

    private void OpenModuleDetail(string detailKey)
    {
        var detail = Rio600ModuleCatalogService.GetDetail(detailKey);
        if (detail is null)
        {
            return;
        }

        var window = new Rio600ModuleDetailWindow(detail)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }
}
