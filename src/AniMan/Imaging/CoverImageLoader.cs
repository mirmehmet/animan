using System.IO;
using System.Windows.Media.Imaging;

namespace AniMan.Imaging;

/// <summary>
/// Shared BitmapImage construction for cover art: frozen (cross-thread safe),
/// downscaled via DecodePixelWidth, fully loaded (OnLoad) so no file handle is kept.
/// </summary>
public static class CoverImageLoader
{
    /// <summary>Loads a cover from a local file; returns null when missing or unreadable.</summary>
    public static BitmapImage? FromFile(string? path, int decodePixelWidth = 360)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = decodePixelWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null; // cover stays blank — non-critical
        }
    }

    /// <summary>Builds a cover from downloaded bytes. Must run on the UI (STA) thread.</summary>
    public static BitmapImage FromBytes(byte[] bytes, int decodePixelWidth)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad; // set before StreamSource
        img.StreamSource = new MemoryStream(bytes);
        img.DecodePixelWidth = decodePixelWidth;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
