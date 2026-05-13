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
        Clipboard.SetText(DescriptionTextBox.Text);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
