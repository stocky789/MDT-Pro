using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MDTProNative.Wpf.Services;

namespace MDTProNative.Wpf.Views.Controls;

public partial class MdtBusyOverlay : UserControl
{
    public MdtBusyOverlay()
    {
        InitializeComponent();
    }

    public void Show(string title, string? detail = null)
    {
        TitleBlock.Text = title;
        if (string.IsNullOrWhiteSpace(detail))
        {
            DetailBlock.Visibility = Visibility.Collapsed;
            DetailBlock.Text = "";
        }
        else
        {
            DetailBlock.Text = detail;
            DetailBlock.Visibility = Visibility.Visible;
        }

        BusyBar.IsIndeterminate = true;
        IsHitTestVisible = true;
        if (ImmersionStore.Current.SubtleAnimations)
        {
            OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);
            OverlayRoot.Opacity = 0;
            OverlayRoot.Visibility = Visibility.Visible;
            OverlayRoot.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.HoldEnd });
        }
        else
        {
            OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);
            OverlayRoot.Opacity = 1;
            OverlayRoot.Visibility = Visibility.Visible;
        }
    }

    public void Hide()
    {
        BusyBar.IsIndeterminate = false;
        IsHitTestVisible = false;
        if (ImmersionStore.Current.SubtleAnimations && OverlayRoot.Visibility == Visibility.Visible)
        {
            var anim = new DoubleAnimation(OverlayRoot.Opacity, 0, TimeSpan.FromMilliseconds(100))
            {
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (_, _) =>
            {
                OverlayRoot.Visibility = Visibility.Collapsed;
                OverlayRoot.Opacity = 1;
            };
            OverlayRoot.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        else
        {
            OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);
            OverlayRoot.Opacity = 1;
            OverlayRoot.Visibility = Visibility.Collapsed;
        }
    }
}
