using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;

namespace MDTPro.Utility {
    /// <summary>Gets the live <see cref="Callout"/> for an <see cref="LHandle"/> — LSPDFR API first, then <see cref="CalloutInterfaceAPI.Functions.GetCalloutFromHandle"/> (per CalloutInterfaceAPI docs).</summary>
    internal static class CalloutHandleResolver {
        internal static Callout TryGetCallout(LHandle handle) {
            if (handle == null) return null;
            var c = LspdfrCalloutFromHandle.TryGet(handle);
            if (c != null) return c;
            return CalloutInterfaceAPI.Functions.GetCalloutFromHandle(handle);
        }
    }
}
