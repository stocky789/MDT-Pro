using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using MDTProNative.Client;

namespace MDTProNative.Wpf.Helpers;

/// <summary>
/// Person / vehicle catalogue stills — same sources as browser <c>pedSearch.js</c> and <c>vehicleSearch.js</c>
/// (bundled <c>/image/peds/*</c> then FiveM docs CDN). Not mugshots; model catalogue art only.
/// </summary>
static class NativeCatalogueImageLoader
{
    static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    /// <summary>Aligned with plugin <c>PedPortraitModelHelper.IsSuitableForCatalogueIdPhoto</c> and web <c>isCataloguePortraitModelSuitable</c>.</summary>
    public static bool IsPedPortraitModelSuitable(string? modelName)
    {
        var n = (modelName ?? "").Trim().ToLowerInvariant();
        if (n.Length < 3) return false;
        if (n is "null" or "undefined") return false;
        if (n.StartsWith("a_c_", StringComparison.Ordinal)) return false;
        if (n.StartsWith("prop_", StringComparison.Ordinal)) return false;
        return true;
    }

    public static BitmapImage? TryDecodeToBitmap(byte[] data)
    {
        if (data == null || data.Length < 32) return null;
        try
        {
            using var ms = new MemoryStream(data, writable: false);
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = ms;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            if (img.PixelWidth < 4 || img.PixelHeight < 4)
                return null;
            if (img.CanFreeze)
                img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    static async Task<byte[]?> HttpGetBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SharedHttp.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tries MDT <c>/image/peds/</c> then FiveM CDN (webp, png). Order matches <c>pedSearch.js</c>.</summary>
    public static async Task<BitmapImage?> LoadPedIdPhotoAsync(MdtHttpClient? http, string? modelName, CancellationToken cancellationToken = default)
    {
        var raw = (modelName ?? "").Trim();
        if (!IsPedPortraitModelSuitable(raw)) return null;
        var m = raw.ToLowerInvariant();

        if (http != null)
        {
            foreach (var ext in new[] { "webp", "png" })
            {
                var bytes = await http.GetOptionalBytesAsync($"image/peds/{m}.{ext}", cancellationToken).ConfigureAwait(false);
                if (bytes == null) continue;
                var bmp = TryDecodeToBitmap(bytes);
                if (bmp != null) return bmp;
            }
        }

        foreach (var ext in new[] { "webp", "png" })
        {
            var uri = new Uri($"https://docs.fivem.net/peds/{m}.{ext}", UriKind.Absolute);
            var bytes = await HttpGetBytesAsync(uri, cancellationToken).ConfigureAwait(false);
            if (bytes == null) continue;
            var bmp = TryDecodeToBitmap(bytes);
            if (bmp != null) return bmp;
        }

        return null;
    }

    /// <summary>FiveM vehicle catalogue (webp then png), same as <c>vehicleSearch.js</c> (CDN only in web).</summary>
    public static async Task<BitmapImage?> LoadVehicleCataloguePhotoAsync(string? modelName, CancellationToken cancellationToken = default)
    {
        var raw = (modelName ?? "").Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        var m = raw.ToLowerInvariant();

        foreach (var ext in new[] { "webp", "png" })
        {
            var uri = new Uri($"https://docs.fivem.net/vehicles/{m}.{ext}", UriKind.Absolute);
            var bytes = await HttpGetBytesAsync(uri, cancellationToken).ConfigureAwait(false);
            if (bytes == null) continue;
            var bmp = TryDecodeToBitmap(bytes);
            if (bmp != null) return bmp;
        }

        return null;
    }
}
