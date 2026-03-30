using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using MDTProNative.Client;
using MDTProNative.Wpf.Helpers;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Views.Reports;

public partial class ReportDocumentHeader : UserControl
{
    public ReportDocumentHeader() => InitializeComponent();

    public void Apply(JObject? active, string titleJsonProperty, string defaultTitle)
    {
        active ??= ReportBrandingFallback.ActiveTemplate;
        BrandingLeftColumn.Text = (active["leftColumn"]?.ToString() ?? "").Replace("\n", Environment.NewLine);
        BrandingCenterTitle.Text = active["centerTitle"]?.ToString() ?? "";
        BrandingRightTitle.Text = (active["rightTitle"]?.ToString() ?? "").Replace("\n", Environment.NewLine);
        var t = active[titleJsonProperty]?.ToString();
        DocumentTitleBlock.Text = string.IsNullOrWhiteSpace(t) ? defaultTitle : t.Trim();
        ReportDocumentSealImages.ClearSeal(SealImage, BrandingCenterTitle);
    }

    public Task TryLoadSealAsync(MdtHttpClient? http, JObject? active) =>
        ReportDocumentSealImages.TryLoadDepartmentBadgeAsync(SealImage, BrandingCenterTitle, http, active, Dispatcher);
}
