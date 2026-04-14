using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using MDTProNative.Client;

namespace MDTProNative.Wpf.Helpers;

/// <summary>
/// Person / vehicle catalogue stills — aligned with browser <c>pedSearch.js</c> and <c>vehicleSearch.js</c>.
/// Ped portraits: bundled <c>/image/peds/*</c> only (face-cropped assets). Vehicles: FiveM docs CDN. Not mugshots.
/// </summary>
static class NativeCatalogueImageLoader
{
    static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static readonly Regex BracketPortraitRegex = new(
        @"^\s*\[([^\]]+)\]\s*\[(\d+)\]\s*\[(\d+)\]\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Maps stored model strings to spawn name under <c>images/peds/</c> (matches plugin <c>PedPortraitModelHelper.CanonCatalogueSpawnName</c>).</summary>
    public static string? CanonPedCatalogueSpawnName(string? modelNameRaw)
    {
        if (string.IsNullOrWhiteSpace(modelNameRaw)) return null;
        var t = modelNameRaw.Trim();
        var m = BracketPortraitRegex.Match(t);
        if (m.Success) return m.Groups[1].Value.Trim().ToLowerInvariant();
        return t.ToLowerInvariant();
    }

    static bool TryParseBracketPedCatalogueKey(string raw, out string spawnLower, out int drawable, out int texture)
    {
        spawnLower = "";
        drawable = texture = 0;
        var m = BracketPortraitRegex.Match(raw.Trim());
        if (!m.Success) return false;
        spawnLower = m.Groups[1].Value.Trim().ToLowerInvariant();
        drawable = int.Parse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        texture = int.Parse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return drawable >= 0 && texture >= 0;
    }

    /// <summary>Aligned with plugin <c>PedPortraitModelHelper.IsSuitableForCatalogueIdPhoto</c> and web <c>isCataloguePortraitModelSuitable</c>.</summary>
    public static bool IsPedPortraitModelSuitable(string? modelName)
    {
        var n = CanonPedCatalogueSpawnName(modelName);
        if (string.IsNullOrEmpty(n) || n.Length < 3) return false;
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

    /// <summary>Bundled MDT <c>/image/peds/</c> only (webp then png per candidate). Matches <c>pedSearch.js</c>; no external CDN.</summary>
    public static Task<BitmapImage?> LoadPedIdPhotoAsync(MdtHttpClient? http, string? modelName, CancellationToken cancellationToken = default)
        => LoadPedIdPhotoAsync(http, modelName, null, null, null, null, cancellationToken);

    /// <summary>Hair + optional face variant pairs (plugin ped components 2 and 0).</summary>
    public static Task<BitmapImage?> LoadPedIdPhotoAsync(MdtHttpClient? http, string? modelName, int? portraitHairDrawable, int? portraitHairTexture, CancellationToken cancellationToken = default)
        => LoadPedIdPhotoAsync(http, modelName, portraitHairDrawable, portraitHairTexture, null, null, cancellationToken);

    /// <param name="portraitHairDrawable">Hair (slot 2) drawable for first <c>model__d_t</c> try.</param>
    /// <param name="portraitHairTexture">Hair texture.</param>
    /// <param name="portraitFaceDrawable">Face (slot 0) drawable for second try when different from hair pair.</param>
    /// <param name="portraitFaceTexture">Face texture.</param>
    public static async Task<BitmapImage?> LoadPedIdPhotoAsync(MdtHttpClient? http, string? modelName, int? portraitHairDrawable, int? portraitHairTexture, int? portraitFaceDrawable, int? portraitFaceTexture, CancellationToken cancellationToken = default)
    {
        var raw = (modelName ?? "").Trim();
        if (!IsPedPortraitModelSuitable(raw)) return null;
        var m = CanonPedCatalogueSpawnName(raw);
        if (string.IsNullOrEmpty(m)) return null;

        int? faceD = portraitFaceDrawable;
        int? faceT = portraitFaceTexture;
        if (TryParseBracketPedCatalogueKey(raw, out _, out int bd, out int bt))
        {
            faceD ??= bd;
            faceT ??= bt;
        }

        if (http == null) return null;

        var triedDt = new HashSet<string>();
        var relPaths = new List<string>(10);

        void AddVariantPair(int d, int t)
        {
            string key = $"{d}_{t}";
            if (!triedDt.Add(key)) return;
            relPaths.Add($"image/peds/{m}__{d}_{t}.webp");
            relPaths.Add($"image/peds/{m}__{d}_{t}.png");
        }

        // Face-keyed catalogue files first (common for [model][d][t] dumps), then hair if different pair.
        if (faceD is >= 0 && faceT is >= 0)
            AddVariantPair(faceD.Value, faceT.Value);
        if (portraitHairDrawable is >= 0 && portraitHairTexture is >= 0
            && (portraitHairDrawable != faceD || portraitHairTexture != faceT))
            AddVariantPair(portraitHairDrawable.Value, portraitHairTexture.Value);

        relPaths.Add($"image/peds/{m}.webp");
        relPaths.Add($"image/peds/{m}.png");
        if (!triedDt.Contains("0_0"))
        {
            relPaths.Add($"image/peds/{m}__0_0.webp");
            relPaths.Add($"image/peds/{m}__0_0.png");
        }

        foreach (var rel in relPaths)
        {
            var bytes = await http.GetOptionalBytesAsync(rel, cancellationToken).ConfigureAwait(false);
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
