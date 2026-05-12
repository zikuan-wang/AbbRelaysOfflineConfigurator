using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Rex615OfflineConfigurator.Services;

namespace Rex615OfflineConfigurator;

public partial class TerminalDiagramWindow : Window
{
    private readonly IReadOnlyList<TerminalDiagram> _diagrams;

    public TerminalDiagramWindow(string code, IReadOnlyList<TerminalDiagram> diagrams)
    {
        InitializeComponent();
        _diagrams = diagrams;
        Title = $"{code} 接线图";
        TitleTextBlock.Text = $"{code} 接线图";
        LoadDiagrams();
    }

    private void LoadDiagrams()
    {
        DiagramTabControl.Items.Clear();
        foreach (var diagram in _diagrams)
        {
            var image = new Image
            {
                Source = new BitmapImage(new Uri(diagram.ImagePath, UriKind.Absolute)),
                Stretch = System.Windows.Media.Stretch.Uniform
            };

            DiagramTabControl.Items.Add(new TabItem
            {
                Header = diagram.Title,
                Content = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = System.Windows.Media.Brushes.White,
                    Content = image
                }
            });
        }

        if (DiagramTabControl.Items.Count > 0)
        {
            DiagramTabControl.SelectedIndex = 0;
        }
    }

    private void OpenImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DiagramTabControl.SelectedIndex < 0 || DiagramTabControl.SelectedIndex >= _diagrams.Count)
        {
            return;
        }

        var path = _diagrams[DiagramTabControl.SelectedIndex].ImagePath;
        if (!File.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
