using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Rex615OfflineConfigurator.Views;

public sealed class SlotDescriptionText : FrameworkElement
{
    private const double HorizontalPadding = 12;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SlotDescriptionText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground),
        typeof(Brush),
        typeof(SlotDescriptionText),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize),
        typeof(double),
        typeof(SlotDescriptionText),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily),
        typeof(FontFamily),
        typeof(SlotDescriptionText),
        new FrameworkPropertyMetadata(
            new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
        nameof(FontWeight),
        typeof(FontWeight),
        typeof(SlotDescriptionText),
        new FrameworkPropertyMetadata(FontWeights.SemiBold, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsNaN(Width) ? 82 : Width;
        var height = double.IsNaN(Height) ? 256 : Height;

        if (!double.IsInfinity(availableSize.Width))
        {
            width = Math.Min(width, availableSize.Width);
        }

        if (!double.IsInfinity(availableSize.Height))
        {
            height = Math.Min(height, availableSize.Height);
        }

        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (string.IsNullOrWhiteSpace(Text) || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface(
            FontFamily,
            FontStyles.Normal,
            FontWeight,
            FontStretches.Normal);
        var formattedText = new FormattedText(
            Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            Foreground,
            dpi.PixelsPerDip)
        {
            MaxLineCount = 1,
            MaxTextWidth = Math.Max(1, ActualHeight - (HorizontalPadding * 2)),
            Trimming = TextTrimming.CharacterEllipsis
        };

        var x = -(ActualHeight - HorizontalPadding);
        var y = Math.Max(0, (ActualWidth - formattedText.Height) / 2);

        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        drawingContext.PushTransform(new RotateTransform(-90));
        drawingContext.DrawText(formattedText, new Point(x, y));
        drawingContext.Pop();
        drawingContext.Pop();
    }
}
