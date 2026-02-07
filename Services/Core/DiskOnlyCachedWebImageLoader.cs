using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HyPrism.Services.Core;

/// <summary>
/// Disk-cached image loader that decodes all images at thumbnail resolution
/// and does NOT cache decoded Bitmaps in RAM.
///
/// Solves two critical memory problems vs <c>DiskCachedWebImageLoader</c>:
/// 
/// 1. <b>No RAM bitmap cache</b> — the parent <c>RamCachedWebImageLoader</c> stores
///    every decoded <c>Bitmap</c> in a <c>ConcurrentDictionary</c> forever.
///    With 30 news images at ~8 MB each = 240 MB leaked per session.
///    
/// 2. <b>Thumbnail decoding</b> — CDN cover images (1920×1080 = 8.3 MB native)
///    are decoded at <c>decodeWidth</c> pixels (e.g. 240 → 135 px = ~130 KB native).
///    Embedded resources (avares://, e.g. 3 MB app logo) are also thumbnailed.
///    
/// Raw image bytes are still saved to disk at original quality for future re-decode.
/// The caller controls Bitmap lifecycle via <c>IDisposable.Dispose()</c>.
/// </summary>
public class DiskOnlyCachedWebImageLoader : BaseWebImageLoader
{
    private readonly string _cacheFolder;
    private readonly int _decodeWidth;

    /// <param name="cacheFolder">Directory for raw image byte cache on disk.</param>
    /// <param name="decodeWidth">Max pixel width for decoded Bitmaps (height is proportional).</param>
    public DiskOnlyCachedWebImageLoader(string cacheFolder, int decodeWidth = 240)
    {
        _cacheFolder = cacheFolder;
        _decodeWidth = decodeWidth;
    }

    /// <summary>
    /// Load from disk cache, decode at thumbnail width.
    /// </summary>
    protected override Task<Bitmap?> LoadFromGlobalCache(string url)
    {
        var path = Path.Combine(_cacheFolder, CreateMD5(url));
        if (!File.Exists(path))
            return Task.FromResult<Bitmap?>(null);

        try
        {
            using var stream = File.OpenRead(path);
            return Task.FromResult<Bitmap?>(Bitmap.DecodeToWidth(stream, _decodeWidth));
        }
        catch
        {
            return Task.FromResult<Bitmap?>(null);
        }
    }

    /// <summary>
    /// Save raw image bytes to disk at original quality.
    /// </summary>
    protected override Task SaveToGlobalCache(string url, byte[] imageBytes)
    {
        var path = Path.Combine(_cacheFolder, CreateMD5(url));
        Directory.CreateDirectory(_cacheFolder);
        File.WriteAllBytes(path, imageBytes);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Load avares:// and local file resources at thumbnail width.
    /// Called for embedded app resources like the HyPrism logo.
    /// No RAM caching — the assembly resource stream is always available.
    /// </summary>
    protected override Task<Bitmap?> LoadFromInternalAsync(string url)
    {
        try
        {
            var uri = url.StartsWith("/")
                ? new Uri(url, UriKind.Relative)
                : new Uri(url, UriKind.RelativeOrAbsolute);

            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                return Task.FromResult<Bitmap?>(null);

            // Local file on disk
            if (uri is { IsAbsoluteUri: true, IsFile: true })
            {
                using var fileStream = File.OpenRead(uri.LocalPath);
                return Task.FromResult<Bitmap?>(Bitmap.DecodeToWidth(fileStream, _decodeWidth));
            }

            // Avalonia embedded resource (avares://)
            if (!AssetLoader.Exists(uri))
                return Task.FromResult<Bitmap?>(null);

            using var stream = AssetLoader.Open(uri);
            return Task.FromResult<Bitmap?>(Bitmap.DecodeToWidth(stream, _decodeWidth));
        }
        catch
        {
            return Task.FromResult<Bitmap?>(null);
        }
    }

    /// <summary>
    /// Full pipeline: internal → disk cache → web download.
    /// All paths decode at thumbnail width.
    /// 
    /// Overridden because the base <c>LoadAsync</c> creates full-resolution Bitmap
    /// from downloaded bytes (<c>new Bitmap(memoryStream)</c>).
    /// </summary>
    protected override async Task<Bitmap?> LoadAsync(string url)
    {
        // 1. Try internal resource (avares://, local file)
        var bitmap = await LoadFromInternalAsync(url).ConfigureAwait(false);
        if (bitmap != null) return bitmap;

        // 2. Try disk cache (decoded at thumbnail width)
        bitmap = await LoadFromGlobalCache(url).ConfigureAwait(false);
        if (bitmap != null) return bitmap;

        // 3. Download from web → save raw bytes → decode as thumbnail
        try
        {
            var externalBytes = await LoadDataFromExternalAsync(url).ConfigureAwait(false);
            if (externalBytes == null) return null;

            await SaveToGlobalCache(url, externalBytes).ConfigureAwait(false);

            using var memoryStream = new MemoryStream(externalBytes);
            return Bitmap.DecodeToWidth(memoryStream, _decodeWidth);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateMD5(string input)
    {
        using var md5 = MD5.Create();
        var inputBytes = Encoding.ASCII.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);
        return BitConverter.ToString(hashBytes).Replace("-", "");
    }
}
