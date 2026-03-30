using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Xps;
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

    /// <summary>
    /// Prints the report visual without <see cref="System.Windows.Controls.PrintDialog.ShowDialog"/>, which on Windows 11
    /// opens the unified print UI and surfaces "This app does not support print preview" for WPF. Uses the same
    /// <see cref="XpsDocumentWriter"/> path as <see cref="System.Windows.Controls.PrintDialog.PrintVisual"/>.
    /// </summary>
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

            var owner = Window.GetWindow(documentPrintRoot) ?? Application.Current.MainWindow;

            // Keep LocalPrintServer alive until the job is submitted; disposing it earlier can invalidate queues.
            using (var printServer = new LocalPrintServer())
            {
                if (!TryEnumerateQueues(printServer, out var queues))
                    return;

                var preferredIndex = GetPreferredPrinterIndex(printServer, queues);
                if (!TryPickPrintQueue(owner, queues, preferredIndex, jobName, out var printQueue) || printQueue == null)
                    return;

                printQueue.Refresh();
                var printTicket = GetValidatedPrintTicket(printQueue);
                var writer = PrintQueue.CreateXpsDocumentWriter(printQueue);
                writer.Write(root, printTicket);
            }
        }
        catch (PrintQueueException ex)
        {
            MessageBox.Show("Print / PDF failed (printer queue).\n\n" + ex.Message, "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    static PrintTicket GetValidatedPrintTicket(PrintQueue printQueue)
    {
        var baseline = printQueue.DefaultPrintTicket ?? new PrintTicket();
        PrintTicket candidate;
        try
        {
            candidate = baseline.Clone();
        }
        catch
        {
            candidate = baseline;
        }

        var merged = printQueue.MergeAndValidatePrintTicket(baseline, candidate);
        return merged.ValidatedPrintTicket ?? baseline;
    }

    static bool TryEnumerateQueues(LocalPrintServer printServer, out List<PrintQueue> queues)
    {
        queues = new List<PrintQueue>();
        try
        {
            queues = printServer
                .GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                .Where(q =>
                {
                    try
                    {
                        q.Refresh();
                        return !q.IsOffline;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not list printers.\n\n" + ex.Message,
                "Export PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (queues.Count == 0)
        {
            MessageBox.Show("No printers are available.", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    static int GetPreferredPrinterIndex(LocalPrintServer printServer, List<PrintQueue> queues)
    {
        var preferredIndex = queues.FindIndex(IsLikelyMicrosoftPrintToPdf);
        if (preferredIndex >= 0)
            return preferredIndex;

        try
        {
            var def = printServer.DefaultPrintQueue;
            if (def != null)
            {
                var name = def.Name;
                preferredIndex = queues.FindIndex(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch
        {
            // ignore; fall back to first queue
        }

        return preferredIndex >= 0 ? preferredIndex : 0;
    }

    static bool TryPickPrintQueue(Window? owner, List<PrintQueue> queues, int preferredIndex, string jobName, out PrintQueue? selectedQueue)
    {
        selectedQueue = null;
        PrintQueue? chosen = null;

        var combo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 12),
            DisplayMemberPath = "Name",
            ItemsSource = queues,
            SelectedIndex = preferredIndex,
            MinHeight = 28
        };

        var printBtn = new Button
        {
            Content = "Print",
            IsDefault = true,
            MinWidth = 88,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(16, 6, 16, 6)
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 88,
            Padding = new Thickness(16, 6, 16, 6)
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(printBtn);
        btnRow.Children.Add(cancelBtn);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose a printer. For a PDF file, select Microsoft Print to PDF — Windows will ask where to save.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(combo);
        panel.Children.Add(btnRow);

        var dlg = new Window
        {
            Title = string.IsNullOrWhiteSpace(jobName) ? "Print report" : $"Print — {jobName}",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = panel
        };

        var accepted = false;
        printBtn.Click += (_, _) =>
        {
            if (combo.SelectedItem is PrintQueue pq)
            {
                chosen = pq;
                accepted = true;
                dlg.DialogResult = true;
            }
        };
        cancelBtn.Click += (_, _) =>
        {
            dlg.DialogResult = false;
        };

        dlg.ShowDialog();
        if (accepted && chosen != null)
        {
            selectedQueue = chosen;
            return true;
        }

        return false;
    }

    static bool IsLikelyMicrosoftPrintToPdf(PrintQueue q)
    {
        var name = q.Name ?? "";
        if (name.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return name.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("print to pdf", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
