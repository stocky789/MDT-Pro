using LemonUI.Elements;
using LemonUI.Menus;
using System.Drawing;

namespace MDTPro.UI.InGameComputer {
    internal static class InGameComputerStyle {
        internal static void ApplyShell(NativeMenu menu, InGameComputerTheme theme) {
            if (menu == null || theme == null) return;
            if (menu.BannerText != null) {
                // Custom banner draws the title itself so it can be vertically centered.
                menu.BannerText.Text = "";
                menu.BannerText.Font = LemonUI.Elements.Font.ChaletComprimeCologne;
                menu.BannerText.Color = theme.Text;
                menu.BannerText.Scale = 0f;
            }
            menu.KeepNameCasing = true;
            menu.MouseBehavior = MenuMouseBehavior.Movement;
            menu.CloseOnInvalidClick = false;
            menu.ResetCursorWhenOpened = false;
        }

        internal static void ApplyAction(NativeItem item, InGameComputerTheme theme) {
            if (item == null || theme == null) return;
            item.UseCustomBackground = true;
            item.Colors = new ColorSet {
                TitleNormal = theme.Text,
                TitleHovered = theme.TextOnHover,
                TitleDisabled = theme.Muted,
                AltTitleNormal = theme.Muted,
                AltTitleHovered = theme.TextOnHover,
                AltTitleDisabled = theme.Muted,
                ArrowsNormal = theme.Text,
                ArrowsHovered = theme.TextOnHover,
                ArrowsDisabled = theme.Muted,
                BadgeLeftNormal = theme.Text,
                BadgeLeftHovered = theme.TextOnHover,
                BadgeLeftDisabled = theme.Muted,
                BadgeRightNormal = theme.Text,
                BadgeRightHovered = theme.TextOnHover,
                BadgeRightDisabled = theme.Muted,
                BackgroundNormal = theme.Row,
                BackgroundHovered = theme.RowHover,
                BackgroundDisabled = Color.FromArgb(145, 16, 22, 30)
            };
        }

        internal static void ApplyReadOnly(NativeItem item, InGameComputerTheme theme, Color valueColor) {
            if (item == null || theme == null) return;
            item.UseCustomBackground = true;
            item.Colors = new ColorSet {
                TitleNormal = theme.Muted,
                TitleHovered = theme.Muted,
                TitleDisabled = theme.Muted,
                AltTitleNormal = valueColor,
                AltTitleHovered = valueColor,
                AltTitleDisabled = valueColor,
                ArrowsNormal = theme.Muted,
                ArrowsHovered = theme.Muted,
                ArrowsDisabled = theme.Muted,
                BadgeLeftNormal = theme.Muted,
                BadgeLeftHovered = theme.Muted,
                BadgeLeftDisabled = theme.Muted,
                BadgeRightNormal = theme.Muted,
                BadgeRightHovered = theme.Muted,
                BadgeRightDisabled = theme.Muted,
                BackgroundNormal = Color.FromArgb(165, 12, 18, 27),
                BackgroundHovered = Color.FromArgb(165, 12, 18, 27),
                BackgroundDisabled = Color.FromArgb(165, 12, 18, 27)
            };
        }

        internal static void ApplySeparator(NativeSeparatorItem item, InGameComputerTheme theme) {
            if (item == null || theme == null) return;
            item.UseCustomBackground = true;
            item.Colors = new ColorSet {
                TitleNormal = theme.BannerAccent,
                TitleHovered = theme.BannerAccent,
                TitleDisabled = theme.BannerAccent,
                AltTitleNormal = theme.BannerAccent,
                AltTitleHovered = theme.BannerAccent,
                AltTitleDisabled = theme.BannerAccent,
                BackgroundNormal = Color.FromArgb(120, theme.Banner.R, theme.Banner.G, theme.Banner.B),
                BackgroundHovered = Color.FromArgb(120, theme.Banner.R, theme.Banner.G, theme.Banner.B),
                BackgroundDisabled = Color.FromArgb(120, theme.Banner.R, theme.Banner.G, theme.Banner.B)
            };
        }
    }
}
