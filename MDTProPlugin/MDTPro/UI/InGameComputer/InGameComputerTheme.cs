using MDTPro.Data;
using System;
using System.Drawing;

namespace MDTPro.UI.InGameComputer {
    internal sealed class InGameComputerTheme {
        internal string ThemeId;
        internal string NavTitle;
        internal string DepartmentName;
        internal string BadgeFile;
        internal Color Banner;
        internal Color BannerAccent;
        internal Color Row;
        internal Color RowHover;
        internal Color Text;
        internal Color TextOnHover;
        internal Color Muted;
        internal Color Success;
        internal Color Warning;
        internal Color Danger;

        internal static InGameComputerTheme Resolve(OfficerInformationData officer) {
            string script = (officer?.agencyScriptName ?? "").Trim().ToUpperInvariant();
            string agency = (officer?.agency ?? "").Trim();
            string probe = (script + " " + agency).ToUpperInvariant();

            if (script == "LSPD" || probe.Contains("LOS SANTOS POLICE") || probe.Contains("LSPD"))
                return Create("lspd", "LSPD MDT", "Los Santos Police Department", "lspd-badge.png",
                    Color.FromArgb(255, 10, 32, 70), Color.FromArgb(255, 210, 168, 62));
            if (script == "BCSO" || probe.Contains("BLAINE COUNTY") || probe.Contains("BCSO"))
                return Create("bcso", "BCSO MDT", "Blaine County Sheriff's Office", "bcso-badge.png",
                    Color.FromArgb(255, 30, 58, 42), Color.FromArgb(255, 198, 153, 69));
            if (script == "LSSD" || probe.Contains("LOS SANTOS COUNTY") || probe.Contains("LSSD") || probe.Contains("SHERIFF"))
                return Create("lssd", "LSSD MDT", "Los Santos County Sheriff's Department", "lssd-badge.png",
                    Color.FromArgb(255, 42, 58, 46), Color.FromArgb(255, 204, 174, 110));
            if (script == "SAHP" || probe.Contains("HIGHWAY PATROL") || probe.Contains("SAHP"))
                return Create("sahp", "SAHP MDT", "San Andreas Highway Patrol", "sahp-badge.png",
                    Color.FromArgb(255, 14, 35, 58), Color.FromArgb(255, 225, 190, 86));
            if (script == "FIB" || probe.Contains("FEDERAL") || probe.Contains("FIB") || probe.Contains("IAA"))
                return Create("fib", "FIB MDT", "Federal Investigation Bureau", "fib-badge.png",
                    Color.FromArgb(255, 16, 24, 36), Color.FromArgb(255, 72, 136, 210));
            if (script == "SAFD" || probe.Contains("FIRE"))
                return Create("safd", "SAFD MDT", "San Andreas Fire Department", "safd-badge.png",
                    Color.FromArgb(255, 90, 22, 24), Color.FromArgb(255, 238, 188, 84));

            return Create("default", "MDT PRO", string.IsNullOrWhiteSpace(agency) ? "San Andreas Public Safety" : agency, "sagov-badge.png",
                Color.FromArgb(255, 18, 24, 32), Color.FromArgb(255, 88, 146, 206));
        }

        private static InGameComputerTheme Create(string id, string navTitle, string department, string badge, Color banner, Color accent) {
            return new InGameComputerTheme {
                ThemeId = id,
                NavTitle = navTitle,
                DepartmentName = department,
                BadgeFile = badge,
                Banner = banner,
                BannerAccent = accent,
                Row = Color.FromArgb(210, 18, 26, 36),
                RowHover = accent,
                Text = Color.FromArgb(255, 245, 248, 252),
                TextOnHover = Color.FromArgb(255, 10, 12, 16),
                Muted = Color.FromArgb(255, 162, 174, 188),
                Success = Color.FromArgb(255, 89, 220, 143),
                Warning = Color.FromArgb(255, 238, 184, 76),
                Danger = Color.FromArgb(255, 238, 84, 84)
            };
        }
    }
}
