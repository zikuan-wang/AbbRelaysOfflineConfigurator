using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rex615OfflineConfigurator.Services;

namespace Rex615OfflineConfigurator;

public partial class TerminalDiagramWindow : Window
{
    private const double MaxDiagramDisplayHeight = 560;
    private const double MinDiagramDisplayHeight = 320;
    private const double DiagramViewportPadding = 72;

    private readonly IReadOnlyList<TerminalDiagram> _diagrams;
    private readonly List<Image> _diagramImages = [];

    public TerminalDiagramWindow(string code, IReadOnlyList<TerminalDiagram> diagrams)
    {
        InitializeComponent();
        _diagrams = diagrams;
        Title = $"{code} 接线图";
        TitleTextBlock.Text = $"{code} 接线图";
        LoadDiagrams();
        Loaded += (_, _) => UpdateDiagramImageSizes();
        SizeChanged += (_, _) => UpdateDiagramImageSizes();
        DiagramTabControl.SizeChanged += (_, _) => UpdateDiagramImageSizes();
    }

    private void LoadDiagrams()
    {
        DiagramTabControl.Items.Clear();
        _diagramImages.Clear();
        foreach (var diagram in _diagrams)
        {
            var image = new Image
            {
                Source = new BitmapImage(new Uri(diagram.ImagePath, UriKind.Absolute)),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            _diagramImages.Add(image);

            DiagramTabControl.Items.Add(new TabItem
            {
                Header = diagram.Title,
                Content = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Background = Brushes.White,
                    Content = new Grid
                    {
                        Background = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { image }
                    }
                }
            });
        }

        if (DiagramTabControl.Items.Count > 0)
        {
            DiagramTabControl.SelectedIndex = 0;
        }

        UpdateDiagramImageSizes();
    }

    private void UpdateDiagramImageSizes()
    {
        if (_diagramImages.Count == 0)
        {
            return;
        }

        var availableHeight = DiagramTabControl.ActualHeight > 0
            ? DiagramTabControl.ActualHeight - DiagramViewportPadding
            : MaxDiagramDisplayHeight;
        var displayHeight = Math.Clamp(availableHeight, MinDiagramDisplayHeight, MaxDiagramDisplayHeight);
        var maxWidth = Math.Max(420, DiagramTabControl.ActualWidth - DiagramViewportPadding);

        foreach (var image in _diagramImages)
        {
            image.Height = displayHeight;
            image.MaxHeight = displayHeight;
            image.MaxWidth = maxWidth;
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
