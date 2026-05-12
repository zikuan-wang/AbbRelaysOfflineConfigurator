using System.IO;
using PdfSharp.Fonts;

namespace Rex615OfflineConfigurator.Services;

public sealed class WindowsFontResolver : IFontResolver
{
    private const string RegularFace = "deng";
    private const string BoldFace = "dengb";

    private static readonly string FontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFace : RegularFace);

    public byte[] GetFont(string faceName)
    {
        var fileName = faceName.Equals(BoldFace, StringComparison.OrdinalIgnoreCase) ? "Dengb.ttf" : "Deng.ttf";
        var path = Path.Combine(FontsDirectory, fileName);
        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        var fallback = Path.Combine(FontsDirectory, "simhei.ttf");
        return File.ReadAllBytes(fallback);
    }
}
