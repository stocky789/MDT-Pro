using MDTPro.Setup;
using Rage;
using System;

namespace MDTPro.Utility {
    /// <summary>
    /// Centralized helper for displaying in-game notifications via Rage/GTA 5.
    /// Uses the full DisplayNotification API with proper textures and formatting.
    /// Thread-safe: can be called from any thread; ensures display runs on the game fiber.
    /// </summary>
    internal static class RageNotification {
        private const string TextureDefault = "CHAR_CALL911";
        private const string TextureError = "CHAR_BLOCKED";
        private const string TitlePrefix = "MDT Pro";

        /// <summary>
        /// Displays a notification. Safe to call from any thread.
        /// </summary>
        /// <param name="message">Main notification text</param>
        /// <param name="type">Notification type (affects icon and styling)</param>
        /// <param name="subtitle">Optional subtitle; if null, uses a default based on type</param>
        public static void Show(string message, NotificationType type = NotificationType.Info, string subtitle = null) {
            if (string.IsNullOrEmpty(message)) return;

            if (GameFiber.CanSleepNow) {
                ShowInternal(message, type, subtitle);
            } else {
                GameFiber.StartNew(() => ShowInternal(message, type, subtitle));
            }
        }

        /// <summary>
        /// Simple notification with just a message. Uses default info styling.
        /// </summary>
        public static void Show(string message) => Show(message, NotificationType.Info);

        /// <summary>
        /// Display an error notification (e.g., load failure).
        /// </summary>
        public static void ShowError(string message) => Show(message, NotificationType.Error);

        /// <summary>
        /// Display a success/ready notification.
        /// </summary>
        public static void ShowSuccess(string message) => Show(message, NotificationType.Success);

        /// <summary>
        /// Display a notification with the MDT access address. Shown when server starts.
        /// </summary>
        public static void ShowAddressNotification(string localIp, string machineName, int port) {
            if (string.IsNullOrEmpty(localIp)) localIp = "localhost";
            if (string.IsNullOrEmpty(machineName)) machineName = "localhost";
            string localUrl = $"http://{localIp}:{port}";
            string machineUrl = $"http://{machineName}:{port}";
            string text = localIp == machineName
                ? localUrl
                : $"{localUrl}~n~{machineUrl}";
            string subtitle = GetLanguage().listeningOnIpAddress?.TrimEnd(' ', ':') ?? "Available at";
            Show(text, NotificationType.Info, subtitle);
        }

        /// <summary>Same URL line(s) as the listening-address notification, for embedding in other messages.</summary>
        internal static string BuildMdtBrowserUrlLinesForNotification() {
            string localIp = Helper.GetLocalIPAddress();
            if (string.IsNullOrEmpty(localIp)) localIp = "localhost";
            int port = SetupController.GetConfig()?.port ?? 0;
            if (port <= 0) port = 9000;
            string machineName = string.IsNullOrWhiteSpace(Environment.MachineName) ? "localhost" : Environment.MachineName;
            string localUrl = $"http://{localIp}:{port}";
            string machineUrl = $"http://{machineName}:{port}";
            return string.Equals(localIp, machineName, StringComparison.OrdinalIgnoreCase)
                ? localUrl
                : $"{localUrl}~n~{machineUrl}";
        }

        /// <summary>Appends the MDT browser hint used for StopThePed citation flows (skipped when Policing Redefined handles handoff).</summary>
        internal static string AppendStpCitationMdtBrowserHint(string message) {
            if (string.IsNullOrEmpty(message)) return message;
            try {
                if (SetupController.GetConfig()?.citationStpAppendMdtBrowserLink == false) return message;
            } catch {
                /* ignore */
            }
            string urls = BuildMdtBrowserUrlLinesForNotification();
            string fmt = GetLanguage().stpCitationMdtBrowserLine;
            if (string.IsNullOrWhiteSpace(fmt)) fmt = "~n~~n~Open the MDT in your browser to review or finish:~n~{0}";
            return message + string.Format(fmt, urls);
        }

        private static void ShowInternal(string message, NotificationType type, string subtitle) {
            (string textureDict, string textureName) = GetTextureForType(type);
            string sub = subtitle ?? GetDefaultSubtitle(type);

            Game.DisplayNotification(textureDict, textureName, TitlePrefix, sub, SanitizeForNotification(message));
        }

        private static (string dict, string name) GetTextureForType(NotificationType type) {
            return type switch {
                NotificationType.Error => (TextureError, TextureError),
                _ => (TextureDefault, TextureDefault),
            };
        }

        private static string GetDefaultSubtitle(NotificationType type) {
            return type switch {
                NotificationType.Error => "Error",
                NotificationType.Success => "Ready",
                NotificationType.Info => "Information",
                _ => "MDT Pro",
            };
        }

        /// <summary>
        /// Strips characters that can break or clutter the notification display.
        /// </summary>
        private static string SanitizeForNotification(string text) {
            if (string.IsNullOrEmpty(text)) return text;
            // Keep ~n~ for line breaks (GTA notification HTML)
            return text.Replace("<", " ").Replace(">", " ");
        }

        private static Language.InGame GetLanguage() =>
            SetupController.GetLanguage().inGame;

        public enum NotificationType {
            Info,
            Success,
            Error,
        }
    }
}
