using System.Windows.Controls;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Re630SelectorView : UserControl
{
    public Re630SelectorView()
    {
        InitializeComponent();
    }

    private void FunctionCatalogButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var selectedDevice = DataContext is Re630SelectionViewModel viewModel
            ? viewModel.SelectedDeviceFilter
            : null;
        var window = new Re630FunctionCatalogWindow(selectedDevice)
        {
            Owner = System.Windows.Window.GetWindow(this)
        };
        window.ShowDialog();
    }
}
