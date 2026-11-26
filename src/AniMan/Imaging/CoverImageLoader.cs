using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace AniMan.Imaging;

/// <summary>
/// Shared BitmapImage construction for cover art: frozen (cross-thread safe),
/// downscaled via DecodePixelWidth, fully loaded (OnLoad) so no file handle is kept,
/// and cached via WeakReference so navigating across pages reuses decoded bitmaps.
/// </summary>
public static class CoverImageLoader
{
    private static readonly ConcurrentDictionary<(string Path, int Width, long WriteTicks), WeakReference<BitmapImage>> Cache = new();
    private static readonly ConcurrentDictionary<(string Url, int Width), WeakReference<BitmapImage>> UrlCache = new();

    // The bitmaps are weakly held, but their dictionary entries are not: sweep dead
    // entries every PruneInterval inserts so paging through lists can't grow the maps forever.
    private const int PruneInterval = 200;
    private static int _insertsSincePrune;

    /// <summary>Checks whether a remote URL cover was already decoded at the specified width.</summary>
    public static BitmapImage? TryGetFromUrlCache(string? url, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return UrlCache.TryGetValue((url, decodePixelWidth), out var weakRef) && weakRef.TryGetTarget(out var cached)
            ? cached
            : null;
    }

    /// <summary>Loads a cover from a local file; returns null when missing, empty, or unreadable.</summary>
    public static BitmapImage? FromFile(string? path, int decodePixelWidth = 200)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0) return null;

            var key = (info.FullName, decodePixelWidth, info.LastWriteTimeUtc.Ticks);
            if (Cache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var cached))
                return cached;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            // A refreshed cover is written to the same path; without this WPF's URI-keyed
            // image cache can keep serving the old picture.
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(info.FullName, UriKind.Absolute);
            bmp.DecodePixelWidth = decodePixelWidth;
            bmp.EndInit();
            bmp.Freeze();

            Cache[key] = new WeakReference<BitmapImage>(bmp);
            PruneIfDue();
            return bmp;
        }
        catch
        {
            return null; // cover stays blank — non-critical
        }
    }

    /// <summary>Builds a cover from downloaded bytes. Must run on the UI (STA) thread.</summary>
    public static BitmapImage? FromBytes(byte[] bytes, int decodePixelWidth, string? urlKey = null)
    {
        if (bytes.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(urlKey))
        {
            var cached = TryGetFromUrlCache(urlKey, decodePixelWidth);
            if (cached is not null) return cached;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad; // set before StreamSource
            img.StreamSource = stream;
            img.DecodePixelWidth = decodePixelWidth;
            img.EndInit();
            img.Freeze();

            if (!string.IsNullOrWhiteSpace(urlKey))
            {
                UrlCache[(urlKey, decodePixelWidth)] = new WeakReference<BitmapImage>(img);
                PruneIfDue();
            }

            return img;
        }
        catch
        {
            return null;
        }
    }

    private static void PruneIfDue()
    {
        if (Interlocked.Increment(ref _insertsSincePrune) < PruneInterval)
            return;
        Interlocked.Exchange(ref _insertsSincePrune, 0);

        foreach (var (key, weakRef) in Cache)
        {
            if (!weakRef.TryGetTarget(out _))
                Cache.TryRemove(key, out _);
        }

        foreach (var (key, weakRef) in UrlCache)
        {
            if (!weakRef.TryGetTarget(out _))
                UrlCache.TryRemove(key, out _);
        }
    }
}
