using System;
using System.Linq;
using System.Reflection;

namespace CalloutInterfaceAPI.External
{
    /// <summary>
    /// Callout Interface does not expose CAD status on <c>CalloutInterface.API.Functions</c> in all versions; discover <c>SetStatus(string)</c> / <c>SetPlayerStatus(string)</c> on the CI assembly when present.
    /// </summary>
    internal static class CalloutInterfaceCadStatusInvoker
    {
        static readonly object Gate = new object();
        static MethodInfo _setStatusMethod;
        static bool _resolved;
        static bool _loggedMissingApi;

        internal static bool TrySetStatusLine(string line)
        {
            MethodInfo method;
            lock (Gate)
            {
                if (!_resolved)
                {
                    _setStatusMethod = ResolveSetStatusMethod();
                    _resolved = true;
                }
                method = _setStatusMethod;
            }
            if (method == null)
            {
                if (!_loggedMissingApi)
                {
                    _loggedMissingApi = true;
                    Rage.Game.LogTrivial("CalloutInterfaceAPI: no SetStatus(string)/SetPlayerStatus(string) found on CalloutInterface — CAD unit line cannot sync in-game.");
                }
                return false;
            }
            if (method.IsStatic)
            {
                method.Invoke(null, new object[] { line });
                return true;
            }
            object target = TryGetSingleton(method.DeclaringType);
            if (target == null) return false;
            method.Invoke(target, new object[] { line });
            return true;
        }

        static MethodInfo ResolveSetStatusMethod()
        {
            Assembly asm = typeof(CalloutInterface.API.Functions).Assembly;
            const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }
            foreach (var t in types)
            {
                foreach (var name in new[] { "SetStatus", "SetPlayerStatus" })
                {
                    foreach (var m in t.GetMethods(bf))
                    {
                        if (m.Name != name) continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType == typeof(string) && m.IsStatic)
                            return m;
                    }
                }
            }
            foreach (var t in types)
            {
                foreach (var name in new[] { "SetStatus", "SetPlayerStatus" })
                {
                    foreach (var m in t.GetMethods(bf))
                    {
                        if (m.Name != name) continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType == typeof(string) && !m.IsStatic)
                            return m;
                    }
                }
            }
            return null;
        }

        static object TryGetSingleton(Type t)
        {
            if (t == null) return null;
            const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var propName in new[] { "Instance", "Default", "Main" })
            {
                var p = t.GetProperty(propName, bf);
                if (p?.GetMethod?.IsStatic == true)
                {
                    try
                    {
                        var v = p.GetValue(null);
                        if (v != null && t.IsInstanceOfType(v)) return v;
                    }
                    catch { }
                }
                var f = t.GetField(propName, bf);
                if (f?.IsStatic == true)
                {
                    try
                    {
                        var v = f.GetValue(null);
                        if (v != null && t.IsInstanceOfType(v)) return v;
                    }
                    catch { }
                }
            }
            return null;
        }
    }
}
