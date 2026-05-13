using System.Windows;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator;

public partial class Rio600ModuleDetailWindow : Window
{
    private double _zoom = 1.0;

    public Rio600ModuleDetailWindow(Rio600ModuleDetail detail)
    {
        InitializeComponent();
        DataContext = detail;
        Title = $"RIO600 {detail.Code} 模块详情";
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ZoomOutButton_OnClick(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(0.35, _zoom - 0.15);
        ApplyZoom();
    }

    private void ZoomInButton_OnClick(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(3.0, _zoom + 0.15);
        ApplyZoom();
    }

    private void FitWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        ConnectionImageScaleTransform.ScaleX = _zoom;
        ConnectionImageScaleTransform.ScaleY = _zoom;
        DimensionImageScaleTransform.ScaleX = _zoom;
        DimensionImageScaleTransform.ScaleY = _zoom;
        ZoomTextBlock.Text = $"{_zoom:P0}";
    }
}
