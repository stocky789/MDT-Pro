using System.Windows.Controls;

namespace MDTProNative.Wpf.Helpers;

public static class ReportFormMultilineMerge
{
    public static void AppendLineIfMissing(TextBox box, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var v = value.Trim();
        var body = (box.Text ?? "").TrimEnd();
        if (body.Length == 0)
        {
            box.Text = v;
            return;
        }

        var lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (string.Equals(line, v, StringComparison.OrdinalIgnoreCase))
                return;
        }

        box.Text = body + Environment.NewLine + v;
    }
}
