using System.Diagnostics;

namespace MDTProNative.Wpf.Views.Controls;

/// <summary>Shows a CAD-style busy overlay for async work; optional minimum visible time (e.g. save confirmations).</summary>
public static class MdtBusyUi
{
    public static async Task RunAsync(MdtBusyOverlay? overlay, string title, string? detail, Func<Task> work, int minimumVisibleMs = 0)
    {
        if (overlay == null)
        {
            await work().ConfigureAwait(false);
            return;
        }

        var disp = overlay.Dispatcher;
        await disp.InvokeAsync(() => overlay.Show(title, detail));
        var sw = Stopwatch.StartNew();
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            if (minimumVisibleMs > 0)
            {
                var left = minimumVisibleMs - (int)sw.ElapsedMilliseconds;
                if (left > 0)
                    await Task.Delay(left).ConfigureAwait(false);
            }

            await disp.InvokeAsync(() => overlay.Hide());
        }
    }
}
