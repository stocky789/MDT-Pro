// Ignore Spelling: Enroute

using System;
using System.Threading;
using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;
using Rage;
using LspdFunc = LSPD_First_Response.Mod.API.Functions;
using CiApi = CalloutInterfaceAPI.Functions;
using MDTPro.Utility;

namespace MDTPro.EventListeners {
    /// <summary>Runs LSPDFR / Callout Interface callout actions on the game fiber (HTTP must not call Rage/LSPDFR directly).</summary>
    internal static class CalloutActionHelper {
        internal enum CalloutActionResult {
            Ok,
            NotFound,
            BadState,
            Error
        }

        internal sealed class CalloutActionOutcome {
            public CalloutActionResult Result;
            public string Message;
        }

        /// <summary>Accept a pending callout, mark en route to scene, or send a line to the in-game Callout Interface MDT for the active callout.</summary>
        internal static CalloutActionOutcome RunOnGameThread(string action, string calloutId, string message) {
            var gate = new ManualResetEventSlim(false);
            CalloutActionOutcome outcome = new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Unknown error." };
            GameFiber.StartNew(() => {
                try {
                    outcome = RunCore(action, calloutId, message);
                } catch (Exception ex) {
                    outcome = new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = ex.Message };
                } finally {
                    gate.Set();
                }
            });
            if (!gate.Wait(TimeSpan.FromSeconds(20)))
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Timed out waiting for game thread (is the game loading or paused?)." };
            return outcome;
        }

        static CalloutActionOutcome RunCore(string action, string calloutId, string message) {
            if (!CalloutEvents.TryGetHandleForCalloutId(calloutId, out var handle) || handle == null)
                return new CalloutActionOutcome { Result = CalloutActionResult.NotFound, Message = "Callout not found or expired. Refresh the callout list." };

            var act = (action ?? "").Trim().ToLowerInvariant();
            switch (act) {
                case "accept":
                    return TryAccept(handle);
                case "enroute":
                case "en_route":
                    return TryEnRoute(handle);
                case "sendmessage":
                case "send_message":
                    return TrySendMessage(handle, message);
                default:
                    return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Unknown action." };
            }
        }

        static CalloutActionOutcome TryAccept(LHandle handle) {
            var state = LspdFunc.GetCalloutAcceptanceState(handle);
            if (state != CalloutAcceptanceState.Pending)
                return new CalloutActionOutcome { Result = CalloutActionResult.BadState, Message = "Callout is not waiting for acceptance (already accepted or finished)." };
            LspdFunc.AcceptPendingCallout(handle);
            return new CalloutActionOutcome { Result = CalloutActionResult.Ok, Message = "Accepted." };
        }

        static CalloutActionOutcome TryEnRoute(LHandle handle) {
            var state = LspdFunc.GetCalloutAcceptanceState(handle);
            if (state == CalloutAcceptanceState.Pending)
                return new CalloutActionOutcome { Result = CalloutActionResult.BadState, Message = "Accept the callout first." };
            try {
                var callout = CalloutHandleResolver.TryGetCallout(handle);
                if (callout == null)
                    return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Could not resolve callout from handle." };
                var t = callout.GetType();
                const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var m = t.GetMethod("SetPlayerEnRoute", bf)
                    ?? t.GetMethod("PlayerEnRoute", bf)
                    ?? t.GetMethod("MarkPlayerAsEnRoute", bf)
                    ?? t.GetMethod("SetEnRoute", bf)
                    ?? t.GetMethod("MarkEnRoute", bf);
                if (m != null && m.GetParameters().Length == 0) {
                    m.Invoke(callout, null);
                    return new CalloutActionOutcome { Result = CalloutActionResult.Ok, Message = "En route." };
                }
            } catch (Exception ex) {
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "En route failed: " + ex.Message };
            }
            return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "This LSPDFR / callout build does not expose an en-route API for programmatic use. Use the in-game Callout Interface keybind." };
        }

        static CalloutActionOutcome TrySendMessage(LHandle handle, string message) {
            if (string.IsNullOrWhiteSpace(message))
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "message is required for sendMessage." };
            if (!CiApi.IsCalloutInterfaceAvailable)
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "CalloutInterface is not available in-game." };
            try {
                var callout = CalloutHandleResolver.TryGetCallout(handle);
                if (callout == null)
                    return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = "Could not resolve callout from handle." };
                CiApi.SendMessage(callout, message.Trim());
                return new CalloutActionOutcome { Result = CalloutActionResult.Ok, Message = "Message sent to Callout Interface." };
            } catch (Exception ex) {
                return new CalloutActionOutcome { Result = CalloutActionResult.Error, Message = ex.Message };
            }
        }
    }
}
