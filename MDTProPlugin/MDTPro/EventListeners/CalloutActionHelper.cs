// Ignore Spelling: Enroute

using System;
using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;
using LspdFunc = LSPD_First_Response.Mod.API.Functions;
using CiApi = CalloutInterfaceAPI.Functions;
using MDTPro.Utility;

namespace MDTPro.EventListeners
{
    /// <summary>Runs LSPDFR / Callout Interface callout actions on the game fiber (HTTP must not call Rage/LSPDFR directly).</summary>
    internal static class CalloutActionHelper
    {
        internal enum CalloutActionResult
        {
            Ok,
            NotFound,
            BadState,
            Unsupported,
            Error
        }

        internal sealed class CalloutActionOutcome
        {
            public CalloutActionResult Result;
            public string Message;
        }

        const int GameThreadActionTimeoutMs = 4500;
        const int CalloutMessageMaxLength = 1000;

        /// <summary>Accept/attach a pending callout, mark en route to scene, close the current callout, or send a line to the in-game Callout Interface MDT for the active callout.</summary>
        internal static CalloutActionOutcome RunOnGameThread(string action, string calloutId, string message)
        {
            CalloutActionOutcome outcome = new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Unknown error." };
            if (!GameFiberHttpBridge.TryExecuteBlocking(() =>
            {
                try
                {
                    outcome = RunCore(action, calloutId, message);
                }
                catch (Exception ex)
                {
                    outcome = new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = ex.Message };
                }
            }, GameThreadActionTimeoutMs, out var bridgeEx))
            {
                return new CalloutActionOutcome
                {
                    Result = CalloutActionResult.Error,
                    Message = "MDT Pro is not ready or the game thread is busy. Try again after going on duty or after the game resumes."
                };
            }
            if (bridgeEx != null)
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = bridgeEx.Message };
            return outcome;
        }

        static CalloutActionOutcome RunCore(string action, string calloutId, string message)
        {
            if (!CalloutEvents.TryGetHandleForCalloutId(calloutId, out var handle) || handle == null)
                return new CalloutActionOutcome { Result = CalloutActionResult.NotFound, Message = "Callout not found or expired. Refresh the callout list." };

            switch (NormalizeAction(action))
            {
                case "attach":
                    return TryAccept(calloutId, handle);
                case "enroute":
                    return TryEnRoute(calloutId, handle);
                case "sendmessage":
                    return TrySendMessage(calloutId, handle, message);
                case "close":
                    return TryClose(calloutId, handle);
                default:
                    return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Unknown action." };
            }
        }

        internal static string NormalizeAction(string action)
        {
            var act = (action ?? "").Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
            switch (act)
            {
                case "accept":
                case "attach":
                case "calloutattach":
                    return "attach";
                case "enroute":
                case "calloutenroute":
                    return "enroute";
                case "sendmessage":
                case "calloutsendmessage":
                    return "sendmessage";
                case "close":
                case "calloutclose":
                    return "close";
                default:
                    return act;
            }
        }

        static CalloutActionOutcome TryAccept(string calloutId, LHandle handle)
        {
            CalloutEvents.TryGetCalloutInformation(calloutId, out var tracked);
            if (tracked?.FinishedTime != null)
                return new CalloutActionOutcome { Result = CalloutActionResult.BadState, Message = "Callout has already finished." };
            var callout = CalloutHandleResolver.TryGetCallout(handle);
            var effective = CalloutEvents.GetCiAwareAcceptanceState(handle, callout);
            // Must match what clients use for the Accept button (serialized ClientAcceptanceState); never 409 while MDT still shows Open (Pending).
            bool mdtShowsAccept = tracked == null || tracked.ClientAcceptanceState == CalloutAcceptanceState.Pending;
            if (!mdtShowsAccept && tracked != null && tracked.LspdfrAcceptanceExposedToMdt && effective != CalloutAcceptanceState.Pending)
                return new CalloutActionOutcome { Result = CalloutActionResult.BadState, Message = "Callout is not waiting for acceptance (already accepted or finished)." };
            LspdFunc.AcceptPendingCallout(handle);
            CalloutEvents.MarkLspdfrAcceptanceExposedForCalloutId(calloutId);
            return new CalloutActionOutcome { Result = CalloutActionResult.Ok, Message = "Accepted." };
        }

        static CalloutActionOutcome TryEnRoute(string calloutId, LHandle handle)
        {
            var callout = CalloutHandleResolver.TryGetCallout(handle);
            var state = CalloutEvents.GetCiAwareAcceptanceState(handle, callout);
            if (state == CalloutAcceptanceState.Pending)
                return new CalloutActionOutcome { Result = CalloutActionResult.BadState, Message = "Accept the callout first." };
            try
            {
                if (callout == null)
                    return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Could not resolve callout from handle." };
                var t = callout.GetType();
                const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var m = t.GetMethod("SetPlayerEnRoute", bf)
                    ?? t.GetMethod("PlayerEnRoute", bf)
                    ?? t.GetMethod("MarkPlayerAsEnRoute", bf)
                    ?? t.GetMethod("SetEnRoute", bf)
                    ?? t.GetMethod("MarkEnRoute", bf);
                if (m != null && m.GetParameters().Length == 0)
                {
                    m.Invoke(callout, null);
                    CalloutEvents.MarkCalloutEnRoute(calloutId);
                    return new CalloutActionOutcome { Result = CalloutActionResult.Ok, Message = "En route." };
                }
            }
            catch (Exception ex)
            {
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "En route failed: " + ex.Message };
            }
            return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "This LSPDFR / callout build does not expose an en-route API for programmatic use. Use the in-game Callout Interface keybind." };
        }

        /// <summary>
        /// Close is intentionally current-callout-only. The inspected LSPDFR API exposes <c>Functions.StopCurrentCallout()</c>, not a handle-specific end method, and the checked-in
        /// CalloutInterfaceAPI source only exposes metadata/message helpers. To avoid ending the wrong callout from a historical MDT row, only the newest tracked active callout may call
        /// this path. If a future LSPDFR or Callout Interface build exposes a handle-specific close helper, replace this guarded current-only mode and update the advertised capability to
        /// <c>handleSpecific</c>.
        /// </summary>
        static CalloutActionOutcome TryClose(string calloutId, LHandle handle)
        {
            CalloutEvents.TryGetCalloutInformation(calloutId, out var tracked);
            if (tracked?.FinishedTime != null)
                return new CalloutActionOutcome { Result = CalloutActionResult.BadState, Message = "Callout has already finished." };

            var callout = CalloutHandleResolver.TryGetCallout(handle);
            var state = CalloutEvents.GetCiAwareAcceptanceState(handle, callout);
            if (state == CalloutAcceptanceState.Pending)
                return new CalloutActionOutcome { Result = CalloutActionResult.BadState, Message = "Accept the callout before closing it." };
            if (!LspdFunc.IsCalloutRunning())
                return new CalloutActionOutcome { Result = CalloutActionResult.BadState, Message = "No active LSPDFR callout is running." };
            if (!CalloutEvents.IsCurrentClosableCallout(calloutId))
                return new CalloutActionOutcome { Result = CalloutActionResult.Unsupported, Message = "Close is only supported for the current active LSPDFR callout." };

            try
            {
                LspdFunc.StopCurrentCallout();
                CalloutEvents.MarkCalloutFinished(calloutId);
                return new CalloutActionOutcome { Result = CalloutActionResult.Ok, Message = "Closed." };
            }
            catch (Exception ex)
            {
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Close failed: " + ex.Message };
            }
        }

        static CalloutActionOutcome TrySendMessage(string calloutId, LHandle handle, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "message is required for sendMessage." };
            if (!CiApi.IsCalloutInterfaceAvailable)
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "CalloutInterface is not available in-game." };
            try
            {
                // CI’s log expects the same Callout instance its API resolves from the handle; LSPDFR’s object can fail SendMessage.
                var callout = CalloutHandleResolver.TryGetCalloutFromCalloutInterfaceOnly(handle) ?? CalloutHandleResolver.TryGetCallout(handle);
                if (callout == null)
                    return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Could not resolve callout from handle." };
                var cleanMessage = message.Trim();
                if (cleanMessage.Length > CalloutMessageMaxLength)
                    cleanMessage = cleanMessage.Substring(0, CalloutMessageMaxLength);
                CiApi.SendMessage(callout, cleanMessage);
                CalloutEvents.AddTrackedAdditionalMessage(calloutId, cleanMessage);
                return new CalloutActionOutcome { Result = CalloutActionResult.Ok, Message = "Message sent to Callout Interface." };
            }
            catch (Exception ex)
            {
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = ex.Message };
            }
        }
    }
}
