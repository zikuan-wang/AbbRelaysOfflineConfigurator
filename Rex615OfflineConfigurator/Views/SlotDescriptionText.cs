using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Rex615OfflineConfigurator.Views;

public sealed class SlotDescriptionText : FrameworkElement
{
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
            MaxTextWidth = Math.Max(1, ActualHeight - 28),
            Trimming = TextTrimming.CharacterEllipsis
        };

        var x = Math.Max(0, (ActualWidth - formattedText.Height) / 2);
        var y = ActualHeight - 16;
        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        drawingContext.PushTransform(new TranslateTransform(x, y));
        drawingContext.PushTransform(new RotateTransform(-90));
        drawingContext.DrawText(formattedText, new Point(0, 0));
        drawingContext.Pop();
        drawingContext.Pop();
        drawingContext.Pop();
    }
}
