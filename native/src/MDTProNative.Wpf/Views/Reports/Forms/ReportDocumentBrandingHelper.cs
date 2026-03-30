using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MDTProNative.Client;
using MDTProNative.Wpf.Helpers;
using MDTProNative.Wpf.Services;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports.Forms;

public static class ReportDocumentBrandingHelper
{
    public static void ApplyChrome(
        JObject? active,
        string titleJsonProperty,
        string defaultTitle,
        ReportDocumentHeader header,
        TextBlock? templateHint,
        string templateIdHint,
        TextBlock? footerBlock = null)
    {
        active ??= ReportBrandingFallback.ActiveTemplate;
        header.Apply(active, titleJsonProperty, defaultTitle);
        if (templateHint != null)
            templateHint.Text = $"Branding: {templateIdHint}";
        if (footerBlock != null)
            footerBlock.Text = active["footer"]?.ToString() ?? "";
    }

    public static async Task LoadBrandingAsync(
        MdtConnectionManager? connection,
        string reportTypeForQuery,
        string titleJsonProperty,
        string defaultTitle,
        ReportDocumentHeader header,
        TextBlock? templateHint,
        System.Windows.Threading.Dispatcher dispatcher,
        TextBlock? footerBlock = null)
    {
        if (connection?.Http == null)
        {
            await dispatcher.InvokeAsync(() =>
                ApplyChrome(null, titleJsonProperty, defaultTitle, header, templateHint, "offline", footerBlock));
            return;
        }

        try
        {
            var tok = await connection.Http.GetDataJsonAsync("reportBranding?reportType=" + Uri.EscapeDataString(reportTypeForQuery)).ConfigureAwait(false);
            if (tok is not JObject root)
            {
                var fb = ReportBrandingFallback.ActiveTemplate;
                await dispatcher.InvokeAsync(() =>
                    ApplyChrome(fb, titleJsonProperty, defaultTitle, header, templateHint, "fallback", footerBlock));
                await header.TryLoadSealAsync(connection.Http, fb).ConfigureAwait(false);
                return;
            }

            var active = root["activeTemplate"] as JObject ?? ReportBrandingFallback.ActiveTemplate;
            var id = root["activeTemplateId"]?.ToString() ?? "regional_crime_lab";
            await dispatcher.InvokeAsync(() => ApplyChrome(active, titleJsonProperty, defaultTitle, header, templateHint, id, footerBlock));
            await header.TryLoadSealAsync(connection.Http, active).ConfigureAwait(false);
        }
        catch
        {
            var fb = ReportBrandingFallback.ActiveTemplate;
            await dispatcher.InvokeAsync(() =>
                ApplyChrome(fb, titleJsonProperty, defaultTitle, header, templateHint, "fallback", footerBlock));
            await header.TryLoadSealAsync(connection.Http, fb).ConfigureAwait(false);
        }
    }

    public static void PrintToPdf(ScrollViewer documentBodyScroll, FrameworkElement documentPrintRoot, string jobName)
    {
        var prevClip = documentBodyScroll.ClipToBounds;
        documentBodyScroll.ClipToBounds = false;

        try
        {
            var root = documentPrintRoot;
            var paperWidth = double.IsNaN(root.MaxWidth) || root.MaxWidth <= 0 ? 920 : root.MaxWidth;
            root.Measure(new Size(paperWidth, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, root.DesiredSize.Width, Math.Max(root.DesiredSize.Height, 1)));
            root.UpdateLayout();

            var pd = new System.Windows.Controls.PrintDialog();
            if (pd.ShowDialog() != true)
                return;

            pd.PrintVisual(root, jobName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Print / PDF failed.\n\n" + ex.Message, "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            documentBodyScroll.ClipToBounds = prevClip;
            documentPrintRoot.InvalidateMeasure();
            documentPrintRoot.InvalidateArrange();
            documentBodyScroll.InvalidateMeasure();
        }
    }
}
