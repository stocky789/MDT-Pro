using MDTPro.Data;
using LSPD_First_Response.Mod.API;
using Rage;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MDTPro.EventListeners {
    internal static class LSPDFREvents {
        private static Delegate _onPedArrestedHandler;

        internal static void SubscribeToLSPDFREvents() {
            if (_onPedArrestedHandler != null) return;
            try {
                EventInfo eventInfo = typeof(Events).GetEvent("OnPedArrested", BindingFlags.Public | BindingFlags.Static);
                if (eventInfo == null) return;

                Type delegateType = eventInfo.EventHandlerType;
                MethodInfo invoke = delegateType.GetMethod("Invoke");
                ParameterInfo[] parameters = invoke.GetParameters();
                ParameterExpression[] paramExpressions = parameters
                    .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                    .ToArray();

                NewArrayExpression argsArray = Expression.NewArrayInit(
                    typeof(object),
                    paramExpressions.Select(p => Expression.Convert(p, typeof(object))));

                MethodCallExpression body = Expression.Call(
                    typeof(LSPDFREvents).GetMethod(nameof(HandleArrestEvent), BindingFlags.NonPublic | BindingFlags.Static),
                    argsArray);

                Delegate handler = Expression.Lambda(delegateType, body, paramExpressions).Compile();
                eventInfo.AddEventHandler(null, handler);
                _onPedArrestedHandler = handler;
            } catch (Exception e) {
                Game.LogTrivial($"MDT Pro: Failed to hook OnPedArrested: {e.Message}");
            }
        }

        internal static void UnsubscribeAll() {
            if (_onPedArrestedHandler == null) return;
            try {
                EventInfo eventInfo = typeof(Events).GetEvent("OnPedArrested", BindingFlags.Public | BindingFlags.Static);
                eventInfo?.RemoveEventHandler(null, _onPedArrestedHandler);
            } catch {
                /* ignore */
            }
            _onPedArrestedHandler = null;
        }

        private static void HandleArrestEvent(object[] args) {
            if (args == null) return;
            foreach (object arg in args) {
                if (arg is Ped ped && ped.IsValid()) {
                    DataController.ResolvePedForReEncounter(ped);
                    DataController.CaptureEvidenceForPed(ped);
                    break;
                }
            }
        }
    }
}
