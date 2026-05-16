using System.Drawing;
using LemonUI.Elements;
using LemonUI.Menus;

namespace MDTPro.Cloud {
    /// <summary>LemonUI menu shell and row colors (pattern from MDT Pro Lite, neutral cloud theme).</summary>
    internal static class CloudLemonUiStyle {
        internal static readonly Color TextWhite = Color.FromArgb(255, 255, 255, 255);
        internal static readonly Color TextOnLightSelection = Color.FromArgb(255, 14, 16, 22);
        internal static readonly Color TextMuted = Color.FromArgb(255, 170, 172, 180);
        internal static readonly Color TextConnected = Color.FromArgb(255, 72, 220, 128);
        internal static readonly Color TextDisconnected = Color.FromArgb(255, 235, 88, 88);

        internal static void ApplyCloudMenuShell(NativeMenu menu) {
            if (menu == null) return;
            if (menu.BannerText != null) {
                menu.BannerText.Text = "";
                menu.BannerText.Color = Color.FromArgb(255, 220, 225, 235);
            }
            menu.KeepNameCasing = true;
            menu.MouseBehavior = LemonUI.Menus.MenuMouseBehavior.Movement;
            menu.CloseOnInvalidClick = false;
            menu.ResetCursorWhenOpened = false;
        }

        internal static void ApplyInteractiveMenuItem(NativeItem item) {
            if (item == null) return;
            Color w = TextWhite;
            item.Colors = new ColorSet {
                TitleNormal = w,
                TitleHovered = TextOnLightSelection,
                TitleDisabled = TextMuted,
                AltTitleNormal = w,
                AltTitleHovered = TextOnLightSelection,
                AltTitleDisabled = TextMuted,
                ArrowsNormal = w,
                ArrowsHovered = TextOnLightSelection,
                ArrowsDisabled = TextMuted,
            };
        }

        internal static void ApplyReadOnlyRow(NativeItem item, Color valueAltColor) {
            if (item == null) return;
            item.Enabled = true;
            item.Colors = new ColorSet {
                TitleNormal = TextWhite,
                TitleHovered = TextOnLightSelection,
                TitleDisabled = TextWhite,
                AltTitleNormal = valueAltColor,
                AltTitleHovered = TextOnLightSelection,
                AltTitleDisabled = valueAltColor,
                ArrowsNormal = TextWhite,
                ArrowsHovered = TextOnLightSelection,
                ArrowsDisabled = TextWhite,
            };
        }

        internal static void ApplyConnectionStatusRow(NativeItem item, bool connected) {
            ApplyReadOnlyRow(item, connected ? TextConnected : TextDisconnected);
        }

        internal static void FinishMenuItemColors(NativeMenu menu, NativeItem readOnlyRowOrNull = null) {
            if (menu?.Items == null) return;
            foreach (NativeItem item in menu.Items) {
                if (item == null) continue;
                if (item is NativeSeparatorItem) continue;
                if (readOnlyRowOrNull != null && ReferenceEquals(item, readOnlyRowOrNull))
                    continue;
                if (item.Enabled)
                    ApplyInteractiveMenuItem(item);
                else
                    ApplyReadOnlyRow(item, item.Colors.AltTitleNormal);
            }
            if (readOnlyRowOrNull != null)
                ApplyReadOnlyRow(readOnlyRowOrNull, readOnlyRowOrNull.Colors.AltTitleNormal);
        }
    }
}
