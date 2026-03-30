using System;
using System.Reflection;
using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;
using LspdFunc = LSPD_First_Response.Mod.API.Functions;

namespace MDTPro.Utility {
    /// <summary>Resolves <see cref="Callout"/> from <see cref="LHandle"/> using whatever static API the installed LSPDFR exposes (name varies by version).</summary>
    internal static class LspdfrCalloutFromHandle {
        static readonly Func<LHandle, Callout> Invoke = Build();

        static Func<LHandle, Callout> Build() {
            var handleType = typeof(LHandle);
            var calloutType = typeof(Callout);
            MethodInfo best = null;
            var bestScore = -1;
            foreach (var m in typeof(LspdFunc).GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                var ps = m.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType != handleType) continue;
                if (!calloutType.IsAssignableFrom(m.ReturnType)) continue;
                int score = 0;
                var n = m.Name;
                if (n.IndexOf("Callout", StringComparison.OrdinalIgnoreCase) >= 0) score += 2;
                if (n.IndexOf("Handle", StringComparison.OrdinalIgnoreCase) >= 0) score += 2;
                if (n.IndexOf("Get", StringComparison.OrdinalIgnoreCase) >= 0) score += 1;
                if (n.IndexOf("From", StringComparison.OrdinalIgnoreCase) >= 0) score += 1;
                if (score > bestScore) {
                    bestScore = score;
                    best = m;
                }
            }
            if (best != null) {
                return h => {
                    if (h == null) return null;
                    try {
                        return best.Invoke(null, new object[] { h }) as Callout;
                    } catch {
                        return null;
                    }
                };
            }
            Helper.Log("MDT Pro: No LSPDFR Functions API maps LHandle to Callout (and CalloutInterface may be required). Active Call / accept from MDT may fail until LSPDFR exposes such a method.", true, Helper.LogSeverity.Warning);
            return _ => null;
        }

        internal static Callout TryGet(LHandle handle) => Invoke(handle);
    }
}
