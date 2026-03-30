using System;
using System.Reflection;
using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;

namespace MDTPro.Utility {
    /// <summary>Gets the live <see cref="Callout"/> for an <see cref="LHandle"/> — LSPDFR API first, then Callout Interface API’s <c>GetCalloutFromHandle</c> when present (resolved at runtime so an outdated <c>CalloutInterfaceAPI.dll</c> does not crash the game).</summary>
    internal static class CalloutHandleResolver {
        static readonly object CiResolverLock = new object();
        static Func<LHandle, Callout> _ciGetCallout;
        static bool _ciResolverInitialized;

        static Func<LHandle, Callout> GetCiGetCalloutFromHandle() {
            if (_ciResolverInitialized) return _ciGetCallout;
            lock (CiResolverLock) {
                if (_ciResolverInitialized) return _ciGetCallout;
                _ciGetCallout = BuildCiGetCalloutFromHandle();
                _ciResolverInitialized = true;
            }
            return _ciGetCallout;
        }

        static Func<LHandle, Callout> BuildCiGetCalloutFromHandle() {
            try {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                    if (!string.Equals(asm.GetName().Name, "CalloutInterfaceAPI", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var t = asm.GetType("CalloutInterfaceAPI.Functions");
                    if (t == null) continue;
                    var m = t.GetMethod("GetCalloutFromHandle", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(LHandle) }, null);
                    if (m == null) continue;
                    return h => {
                        if (h == null) return null;
                        try {
                            return m.Invoke(null, new object[] { h }) as Callout;
                        } catch {
                            return null;
                        }
                    };
                }
            } catch {
            }
            Helper.Log("MDT Pro: CalloutInterfaceAPI has no GetCalloutFromHandle(LHandle). Deploy the CalloutInterfaceAPI that ships with your Callout Interface build, or update CI — active call details may be missing until LSPDFR resolves the handle.", true, Helper.LogSeverity.Warning);
            return _ => null;
        }

        internal static Callout TryGetCallout(LHandle handle) {
            if (handle == null) return null;
            var c = LspdfrCalloutFromHandle.TryGet(handle);
            if (c != null) return c;
            return GetCiGetCalloutFromHandle()(handle);
        }
    }
}
