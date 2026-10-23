using Avalonia.Media;

namespace PhotoSiftReborn;

// Studio / Midnight Jade semantic color tokens. No native authority here.
// The companion XAML resources style platform-standard controls; the regression
// checks keep each paired semantic value in sync until all surfaces use resources.
internal static class StudioPalette
{
    internal const string ShellHex = "#0F1923";
    internal const string SurfaceHex = "#172635";
    internal const string PhotoBedHex = "#101315";
    internal const string TextHex = "#F3F6F8";
    internal const string SecondaryHex = "#B8C6D1";
    internal const string JadeHex = "#71D5C5";
    internal const string JadeInkHex = "#0D1B24";
    internal const string FocusHex = "#91E4D5";
    internal const string StrokeHex = "#728A9D";

    internal static readonly IBrush Shell = Brush(ShellHex);
    internal static readonly IBrush Surface = Brush(SurfaceHex);
    internal static readonly IBrush PhotoBed = Brush(PhotoBedHex);
    internal static readonly IBrush Text = Brush(TextHex);
    internal static readonly IBrush Secondary = Brush(SecondaryHex);
    internal static readonly IBrush Jade = Brush(JadeHex);
    internal static readonly IBrush JadeInk = Brush(JadeInkHex);

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
}
