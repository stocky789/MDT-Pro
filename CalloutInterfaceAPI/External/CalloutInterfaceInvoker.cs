using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;

namespace CalloutInterfaceAPI.External
{
    /// <summary>
    /// Forwards to <c>CalloutInterface.API.Functions</c>. Only call when <see cref="CalloutInterfaceAPI.Functions.IsCalloutInterfaceAvailable"/> is true.
    /// </summary>
    internal static class CalloutInterfaceInvoker
    {
        /// <summary>
        /// Sends the callout details to the Callout Interface.
        /// </summary>
        /// <param name="callout">The callout to send the details for.</param>
        /// <param name="priority">The priority of the callout.</param>
        /// <param name="agency">The agency associated with the callout.</param>
        internal static void SendCalloutDetails(LSPD_First_Response.Mod.Callouts.Callout callout, string priority, string agency)
        {
            CalloutInterface.API.Functions.SendCalloutDetails(callout, priority, agency);
        }

        /// <summary>
        /// Sends a message to the Callout Interface.
        /// </summary>
        /// <param name="callout">The callout associated with the message.</param>
        /// <param name="message">The message to send.</param>
        internal static void SendMessage(LSPD_First_Response.Mod.Callouts.Callout callout, string message)
        {
            CalloutInterface.API.Functions.SendMessage(callout, message);
        }

        /// <summary>
        /// Sends a vehicle for the plate display.
        /// </summary>
        /// <param name="vehicle">The targeted vehicle.</param>
        internal static void SendVehicle(Rage.Vehicle vehicle)
        {
            CalloutInterface.API.Functions.SendVehicle(vehicle);
        }

        /// <summary>
        /// Resolves the LSPDFR <see cref="Callout"/> for a callout handle (same as Callout Interface’s in-game MDT).
        /// </summary>
        internal static Callout GetCalloutFromHandle(LHandle handle)
        {
            return CalloutInterface.API.Functions.GetCalloutFromHandle(handle);
        }
    }
}
