using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MDTProNative.Client;
using MDTProNative.Wpf.Helpers;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports;

/// <summary>Loads department seal PNGs from the in-game <c>DepartmentStyling</c> plugin (<c>/plugin/DepartmentStyling/image/*.png</c>).</summary>
public static class ReportDocumentSealImages
{
    public static void ClearSeal(Image sealImage, TextBlock centerTitleFallback)
    {
        sealImage.Source = null;
        sealImage.Visibility = Visibility.Collapsed;
        centerTitleFallback.Visibility = Visibility.Visible;
    }

    public static async Task TryLoadDepartmentBadgeAsync(
        Image sealImage,
        TextBlock centerTitleFallback,
        MdtHttpClient? http,
        JObject? active,
        Dispatcher dispatcher)
    {
        dispatcher.Invoke(() => ClearSeal(sealImage, centerTitleFallback));
        active ??= ReportBrandingFallback.ActiveTemplate;
        var file = active["sealBadgeFile"]?.ToString()?.Trim();
        if (http == null || string.IsNullOrEmpty(file) || file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return;

        byte[]? bytes;
        try
        {
            bytes = await http.GetPluginImageBytesAsync("DepartmentStyling", file).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (bytes == null || bytes.Length == 0)
            return;

        dispatcher.Invoke(() =>
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                sealImage.Source = bmp;
                sealImage.Visibility = Visibility.Visible;
                centerTitleFallback.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ClearSeal(sealImage, centerTitleFallback);
            }
        });
    }
}
