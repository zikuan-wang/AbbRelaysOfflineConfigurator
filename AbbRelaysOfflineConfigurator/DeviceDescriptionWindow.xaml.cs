using System.Windows;

namespace AbbRelaysOfflineConfigurator;

public partial class DeviceDescriptionWindow : Window
{
    public DeviceDescriptionWindow(string description)
    {
        InitializeComponent();
        DescriptionTextBox.Text = description;
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        Services.ClipboardService.TrySetText(DescriptionTextBox.Text, Title);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
