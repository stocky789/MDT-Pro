using System.Windows;
using System.Windows.Controls;

namespace MDTProNative.Wpf.Helpers;

/// <summary>Wires small COPY buttons next to report fields (clipboard).</summary>
internal static class ReportFormCopyButtons
{
    public static void Wire(Button button, TextBox source)
    {
        button.Click += (_, _) =>
        {
            var t = source.Text?.Trim() ?? "";
            if (t.Length == 0) return;
            try
            {
                Clipboard.SetText(t);
            }
            catch
            {
                /* clipboard can be locked by another app */
            }
        };
    }
}
