// StopThePed: injectPedSearchItems / injectVehicleSearchItems + reflection discovery of search-result getters.
// Verified method names: STP/StopThePed.dll (see STP/StopThePed.API.cs and scripts/dump-stp-api.ps1).
using Rage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MDTPro.Utility {
    internal static class StpReflectionHelper {
        private static List<MethodInfo> _vehicleSearchGetters;
        private static List<MethodInfo> _pedSearchGetters;

        internal static Assembly TryGetStopThePedAssembly() {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.IsDynamic) continue;
                if (string.Equals(asm.GetName().Name, "StopThePed", StringComparison.OrdinalIgnoreCase))
                    return asm;
            }
            return null;
        }

        internal static bool TryInvokeInjectVehicleSearchItems(Vehicle vehicle) {
            return TryInvokeSingleArg("StopThePed.API.Functions", "injectVehicleSearchItems", vehicle);
        }

        internal static bool TryInvokeInjectPedSearchItems(Ped ped) {
            return TryInvokeSingleArg("StopThePed.API.Functions", "injectPedSearchItems", ped);
        }

        /// <summary>STP driver's licence UI aligns with injected ped-search item state; Functions date accessors can lag until inject runs.</summary>
        internal static void PrepareStpPedSearchInjectionForDocumentRead(Ped ped) {
            if (ped == null || !ped.IsValid()) return;
            TryInvokeInjectPedSearchItems(ped);
            GameFiber.Wait(175);
            TryInvokeInjectPedSearchItems(ped);
            GameFiber.Wait(150);
        }

        /// <summary>
        /// STP driver's licence card / ped search UI is backed by injected search items — often not the same as raw API Functions date getters.
        /// Call after <see cref="PrepareStpPedSearchInjectionForDocumentRead"/> so the enumerator is populated (game fiber).
        /// </summary>
        internal static bool TryGetDriverLicenseFieldsFromPedSearchEnumerable(Ped ped, out string status, out string expiration) {
            status = null;
            expiration = null;
            if (ped == null || !ped.IsValid()) return false;
            List<object> bag = AggregateStpPedSearchItemObjects(ped);
            if (bag.Count == 0)
                return false;

            if (HarvestLicenseAcrossBag(bag, preferDriverPattern: true, out status, out expiration))
                return true;
            return HarvestLicenseAcrossBag(bag, preferDriverPattern: false, out status, out expiration);
        }

        internal static bool TryGetWeaponPermitFieldsFromPedSearchEnumerable(Ped ped, out string status, out string expiration, out string permitType) {
            status = null;
            expiration = null;
            permitType = null;
            if (ped == null || !ped.IsValid()) return false;
            List<object> bag = AggregateStpPedSearchItemObjects(ped);
            if (bag.Count == 0) return false;

            foreach (object item in bag) {
                if (item == null) continue;
                List<string> fragments = CollectObjectTextFragments(item, item.GetType(), 2)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (fragments.Count == 0) continue;

                string combined = string.Join(" | ", fragments);
                string lower = combined.ToLowerInvariant();
                bool weaponContext = lower.Contains("weapon permit") || lower.Contains("weapons permit")
                    || lower.Contains("gun permit") || lower.Contains("firearm permit")
                    || lower.Contains("ccw") || lower.Contains("conceal");
                if (!weaponContext) continue;
                if ((lower.Contains("hunting") || lower.Contains("fishing")) && !lower.Contains("weapon") && !lower.Contains("firearm") && !lower.Contains("gun") && !lower.Contains("ccw"))
                    continue;
                if ((lower.Contains("registration") || lower.Contains("insurance") || lower.Contains("vehicle") || lower.Contains("driver license") || lower.Contains("driver licence"))
                    && !lower.Contains("weapon") && !lower.Contains("firearm") && !lower.Contains("gun") && !lower.Contains("ccw"))
                    continue;

                foreach (string fragment in fragments) {
                    if (string.IsNullOrWhiteSpace(status) && TryExtractLicenseStatusFromText(fragment, out string st))
                        status = st;
                    if (string.IsNullOrWhiteSpace(expiration)
                        && (fragment.IndexOf("expir", StringComparison.OrdinalIgnoreCase) >= 0
                            || fragment.IndexOf("valid until", StringComparison.OrdinalIgnoreCase) >= 0
                            || fragment.IndexOf("valid thru", StringComparison.OrdinalIgnoreCase) >= 0
                            || fragment.IndexOf("valid through", StringComparison.OrdinalIgnoreCase) >= 0)
                        && TryExtractDateFromText(fragment, out string iso))
                        expiration = iso;
                }

                if (string.IsNullOrWhiteSpace(permitType))
                    permitType = TryExtractWeaponPermitTypeFromText(combined);
                if (string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(permitType))
                    status = "Valid";
                if (!string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(expiration) || !string.IsNullOrWhiteSpace(permitType))
                    return true;
            }
            return false;
        }

        static string TryExtractWeaponPermitTypeFromText(string text) {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string lower = text.ToLowerInvariant();
            if (Regex.IsMatch(lower, @"\bnone\b") || lower.Contains("no gun permit") || lower.Contains("no weapon permit") || lower.Contains("no firearm permit"))
                return null;
            if (lower.Contains("ccw") || lower.Contains("conceal"))
                return "CCWPermit";
            if (lower.Contains("ffl") || lower.Contains("federal firearm"))
                return "FflPermit";
            return null;
        }

        static bool HarvestLicenseAcrossBag(List<object> bag, bool preferDriverPattern, out string bestStatus, out string bestExpiry) {
            bestStatus = null;
            bestExpiry = null;
            foreach (object item in bag) {
                if (item == null || IsDangerousNoiseSearchItem(item)) continue;
                Type t = item.GetType();
                if (preferDriverPattern && !LooksLikeDriversLicensePedSearchItem(t, item))
                    continue;
                TryHarvestDocFieldsFromPedSearchItem(item, t, out string ist, out string iex);
                TryHarvestDocTextFromPedSearchItem(item, t, out string textStatus, out string textExpiry);
                if (string.IsNullOrWhiteSpace(ist))
                    ist = textStatus;
                if (string.IsNullOrWhiteSpace(iex))
                    iex = textExpiry;
                if (!string.IsNullOrWhiteSpace(iex))
                    bestExpiry = iex;
                if (!string.IsNullOrWhiteSpace(ist))
                    bestStatus = ist;
                if (!preferDriverPattern && string.IsNullOrWhiteSpace(bestExpiry) && MightHoldTrafficLicencePaperwork(t))
                    SweepAnyDateLooksLikeLicenceExpiry(item, t, ref bestExpiry);

                if (preferDriverPattern && !string.IsNullOrWhiteSpace(bestExpiry))
                    break;
            }
            if (!string.IsNullOrWhiteSpace(bestStatus) && string.IsNullOrWhiteSpace(bestExpiry))
                TryHarvestLooseExpiryFromTextBag(bag, ref bestExpiry);
            return !string.IsNullOrWhiteSpace(bestStatus) || !string.IsNullOrWhiteSpace(bestExpiry);
        }

        static bool IsDangerousNoiseSearchItem(object item) {
            try {
                Type t = item.GetType();
                string n = (t.FullName ?? t.Name ?? "").ToLowerInvariant();
                bool mentionsDriver = n.Contains("driver");
                if (n.Contains("weapon") || n.Contains("firearm")) return true;
                if (n.Contains("drug") || n.Contains("contrab")) return true;
                if (!mentionsDriver && (n.Contains("registration") || n.Contains("insurance") || n.Contains("vehicle")))
                    return true;
                if (!mentionsDriver && (n.Contains("hunting") || n.Contains("fishing") || n.Contains("permit")))
                    return true;
                string s = item.ToString()?.ToLowerInvariant() ?? "";
                foreach (string fragment in CollectObjectTextFragments(item, item.GetType(), 1))
                    s += " " + fragment.ToLowerInvariant();
                bool textMentionsDriver = s.Contains("driver");
                if (s.Contains("weapon") || s.Contains("firearm")) return true;
                if (!textMentionsDriver && (s.Contains("registration") || s.Contains("insurance") || s.Contains("vehicle")))
                    return true;
                if (!textMentionsDriver && (s.Contains("hunting") || s.Contains("fishing") || s.Contains("permit")))
                    return true;
                return false;
            } catch {
                return false;
            }
        }

        static bool MightHoldTrafficLicencePaperwork(Type t) {
            string n = (t.FullName ?? t.Name ?? "").ToLowerInvariant();
            return n.IndexOf("document", StringComparison.Ordinal) >= 0 || n.IndexOf("ident", StringComparison.Ordinal) >= 0
                || (n.IndexOf("search", StringComparison.Ordinal) >= 0 && n.IndexOf("item", StringComparison.Ordinal) >= 0)
                || n.IndexOf("traffic", StringComparison.Ordinal) >= 0 || n.IndexOf("stopped", StringComparison.Ordinal) >= 0
                || n.IndexOf("dashboard", StringComparison.Ordinal) >= 0;
        }

        static void SweepAnyDateLooksLikeLicenceExpiry(object item, Type t, ref string bestExpiry) {
            foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                try {
                    string pn = (prop.Name ?? "").ToLowerInvariant();
                    object val = prop.GetValue(item);
                    if (val == null || (!(val is string) && val is IEnumerable)) continue;
                    bool likelyDlField = pn.Contains("license") && (pn.Contains("exp") || pn.Contains("valid"))
                        || pn == "expiration" || pn == "expiry" || pn == "expirationdate";
                    if (!likelyDlField) continue;
                    if (TryFormatStpDateResult(val, out string iso) && !string.IsNullOrWhiteSpace(iso))
                        bestExpiry = iso;
                } catch { }
            }
        }

        internal static string DescribePedSearchItemTypes(Ped ped, int limit = 12) {
            try {
                List<object> bag = AggregateStpPedSearchItemObjects(ped);
                if (bag.Count == 0) return "none";
                return string.Join("; ", bag
                    .Take(Math.Max(1, limit))
                    .Select(item => {
                        if (item == null) return "<null>";
                        string text = "";
                        try {
                            text = item.ToString();
                            if (string.IsNullOrWhiteSpace(text) || text == item.GetType().FullName || text == item.GetType().Name)
                                text = string.Join(" | ", CollectObjectTextFragments(item, item.GetType(), 1).Take(4));
                            if (text != null && text.Length > 90)
                                text = text.Substring(0, 90);
                        } catch { }
                        return (item.GetType().FullName ?? item.GetType().Name) + (string.IsNullOrWhiteSpace(text) ? "" : "=" + text);
                    }));
            } catch (Exception ex) {
                return "error:" + ex.Message;
            }
        }

        static bool LooksLikeDriversLicensePedSearchItem(Type t, object item) {
            string n = (t.FullName ?? t.Name ?? "").ToLowerInvariant();
            if (n.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("firearm", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if ((n.IndexOf("ccw", StringComparison.OrdinalIgnoreCase) >= 0 || (n.IndexOf("conceal", StringComparison.OrdinalIgnoreCase) >= 0))
                && n.IndexOf("driver", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            bool typeLooksDl =
                n.Contains("driverslicense") || n.Contains("driverlicense")
                || (n.Contains("driver") && (n.Contains("license") || n.Contains("licen")));

            if (typeLooksDl)
                return true;

            try {
                string s = item.ToString();
                foreach (string fragment in CollectObjectTextFragments(item, t, 1))
                    s += " " + fragment;
                if (string.IsNullOrWhiteSpace(s)) return false;
                string sl = s.ToLowerInvariant();
                if (sl.Contains("registration") || sl.Contains("insurance"))
                    return false;
                if ((sl.Contains("ccw") || sl.Contains("conceal") || sl.Contains("weapon permit")) && !sl.Contains("driver"))
                    return false;
                return sl.Contains("driver") && (sl.Contains("licen") || sl.Contains("license"));
            } catch {
                return false;
            }
        }

        /// <summary>Pull status + expiration from heterogeneous STP/PR ped search item blobs.</summary>
        static void TryHarvestDocFieldsFromPedSearchItem(object item, Type t, out string status, out string expiration) {
            status = null;
            expiration = null;
            foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                try {
                    string pn = (prop.Name ?? "").ToLowerInvariant();
                    bool statusHint = pn.Contains("status") || (pn.Contains("state") && !pn.Contains("licenseplate"));
                    bool dateHint = pn.IndexOf("expir", StringComparison.Ordinal) >= 0 || pn.IndexOf("validuntil", StringComparison.Ordinal) >= 0
                        || pn.IndexOf("expiry", StringComparison.Ordinal) >= 0;
                    object val = prop.GetValue(item);
                    if (val == null) continue;

                    if (dateHint) {
                        if (TryFormatStpDateResult(val, out string iso) && !string.IsNullOrWhiteSpace(iso))
                            expiration = iso;
                    }
                    if (statusHint && string.IsNullOrWhiteSpace(status)) {
                        if (TryFormatStpVehicleStatusResult(val, out string st) && !string.IsNullOrWhiteSpace(st))
                            status = st;
                        else if (val is Enum en)
                            status = Enum.GetName(en.GetType(), en)?.Trim();
                        else if (val is string sx && sx.Trim().Length > 0)
                            status = sx.Trim();
                    }
                } catch { }
            }

            if (string.IsNullOrWhiteSpace(expiration)) {
                foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    try {
                        object val = prop.GetValue(item);
                        if (val == null || (!(val is string) && val is IEnumerable)) continue;
                        string pn = (prop.Name ?? "").ToLowerInvariant();
                        if (pn.Contains("birth") || pn.Contains("dob") || pn.Contains("timestamp")
                            || pn.Contains("issued") || pn.Contains("created") || pn.Contains("identified"))
                            continue;
                        if (TryFormatStpDateResult(val, out string iso) && !string.IsNullOrWhiteSpace(iso)) {
                            expiration = iso;
                            break;
                        }
                    } catch { }
                }
            }

            foreach (FieldInfo fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                try {
                    string pn = (fi.Name ?? "").ToLowerInvariant();
                    bool dateHint = pn.Contains("expir") || pn.Contains("valid");
                    bool statusHint = pn.Contains("status") || (pn.Contains("state") && !pn.Contains("licenseplate"));
                    object val = fi.GetValue(item);
                    if (val == null) continue;
                    if (dateHint && string.IsNullOrWhiteSpace(expiration) && TryFormatStpDateResult(val, out string iso))
                        expiration = iso;
                    if (statusHint && string.IsNullOrWhiteSpace(status)) {
                        if (TryFormatStpVehicleStatusResult(val, out string st))
                            status = st;
                        else if (val is string sx && sx.Trim().Length > 0)
                            status = sx.Trim();
                    }
                } catch { }
            }
        }

        static void TryHarvestDocTextFromPedSearchItem(object item, Type t, out string status, out string expiration) {
            status = null;
            expiration = null;
            List<string> fragments = CollectObjectTextFragments(item, t, 2)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (fragments.Count == 0) return;

            string combined = string.Join(" | ", fragments);
            string lower = combined.ToLowerInvariant();
            bool driverContext = lower.Contains("driver") && (lower.Contains("licen") || lower.Contains("license"));
            if (!driverContext) return;

            foreach (string fragment in fragments) {
                string fl = fragment.ToLowerInvariant();
                if (fl.Contains("registration") || fl.Contains("insurance")
                    || fl.Contains("weapon") || fl.Contains("firearm")
                    || fl.Contains("hunting") || fl.Contains("fishing"))
                    continue;
                if (string.IsNullOrWhiteSpace(status) && TryExtractLicenseStatusFromText(fragment, out string st))
                    status = st;
                if (string.IsNullOrWhiteSpace(expiration)
                    && (fl.Contains("expir") || fl.Contains("expiry") || fl.Contains("valid until") || fl.Contains("valid thru") || fl.Contains("valid through"))
                    && TryExtractDateFromText(fragment, out string iso))
                    expiration = iso;
            }
            if (string.IsNullOrWhiteSpace(expiration)) {
                foreach (string fragment in fragments) {
                    string fl = fragment.ToLowerInvariant();
                    if (fl.Contains("birth") || fl.Contains("dob") || fl.Contains("issued") || fl.Contains("created") || fl.Contains("identified"))
                        continue;
                    if (TryExtractDateFromText(fragment, out expiration))
                        break;
                }
            }
        }

        static void TryHarvestLooseExpiryFromTextBag(List<object> bag, ref string expiration) {
            foreach (object item in bag) {
                if (item == null || IsDangerousNoiseSearchItem(item)) continue;
                foreach (string fragment in CollectObjectTextFragments(item, item.GetType(), 1)) {
                    string fl = fragment.ToLowerInvariant();
                    if (fl.Contains("registration") || fl.Contains("insurance") || fl.Contains("vehicle")
                        || fl.Contains("weapon") || fl.Contains("firearm") || fl.Contains("hunting") || fl.Contains("fishing")
                        || fl.Contains("birth") || fl.Contains("dob") || fl.Contains("issued") || fl.Contains("created") || fl.Contains("identified"))
                        continue;
                    if (!(fl.Contains("expir") || fl.Contains("expiry") || fl.Contains("valid until") || fl.Contains("valid thru") || fl.Contains("valid through")))
                        continue;
                    if (TryExtractDateFromText(fragment, out expiration))
                        return;
                }
            }
        }

        static List<string> CollectObjectTextFragments(object item, Type t, int depth) {
            var output = new List<string>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            CollectObjectTextFragments(item, t, depth, output, seen);
            return output;
        }

        static void CollectObjectTextFragments(object item, Type t, int depth, List<string> output, HashSet<object> seen) {
            if (item == null || t == null || depth < 0 || !seen.Add(item)) return;
            if (item is string s) {
                if (!string.IsNullOrWhiteSpace(s)) output.Add(s.Trim());
                return;
            }

            string own = null;
            try { own = item.ToString(); } catch { }
            if (!string.IsNullOrWhiteSpace(own) && own != t.FullName && own != t.Name)
                output.Add(own.Trim());

            foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                if (prop.GetIndexParameters().Length != 0 || !prop.CanRead) continue;
                try {
                    object val = prop.GetValue(item);
                    AppendTextValue(val, depth, output, seen);
                } catch { }
            }
            foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                try {
                    object val = field.GetValue(item);
                    AppendTextValue(val, depth, output, seen);
                } catch { }
            }
        }

        static void AppendTextValue(object val, int depth, List<string> output, HashSet<object> seen) {
            if (val == null) return;
            if (val is string s) {
                if (!string.IsNullOrWhiteSpace(s)) output.Add(s.Trim());
                return;
            }
            Type vt = val.GetType();
            if (val is DateTime || val is DateTimeOffset || vt.IsPrimitive || vt.IsEnum)
                return;
            if (val is IEnumerable && !(val is string))
                return;
            string ns = vt.Namespace ?? "";
            if (depth > 0 && (ns.StartsWith("RAGENativeUI", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(vt.FullName) && vt.FullName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0)))
                CollectObjectTextFragments(val, vt, depth - 1, output, seen);
        }

        static bool TryExtractLicenseStatusFromText(string text, out string status) {
            status = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string lower = text.ToLowerInvariant();
            if (lower.Contains("suspended")) status = "Suspended";
            else if (lower.Contains("revoked")) status = "Revoked";
            else if (lower.Contains("expired")) status = "Expired";
            else if (Regex.IsMatch(lower, @"\bvalid\b")) status = "Valid";
            else if (Regex.IsMatch(lower, @"\bnone\b")) status = "None";
            return !string.IsNullOrWhiteSpace(status);
        }

        static bool TryExtractDateFromText(string text, out string iso) {
            iso = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (Match match in Regex.Matches(text, @"\b(?:\d{4}[-/]\d{1,2}[-/]\d{1,2}|\d{1,2}[-/]\d{1,2}[-/]\d{2,4})\b")) {
                if (TryFormatStpDateResult(match.Value, out iso))
                    return true;
            }
            return false;
        }

        private static bool TryInvokeSingleArg(string typeName, string methodName, object arg) {
            if (arg == null) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies(typeName);
            if (fn == null) return false;
            Type argType = arg.GetType();
            MethodInfo m = fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { argType }, null);
            if (m == null) return false;
            try {
                m.Invoke(null, new[] { arg });
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>After inject + short yield, try every plausible static Vehicle → IEnumerable getter on StopThePed.</summary>
        internal static bool TryGetVehicleSearchItemsEnumerable(Vehicle vehicle, out IEnumerable items) {
            items = null;
            if (vehicle == null || !vehicle.Exists()) return false;
            foreach (MethodInfo m in GetOrBuildVehicleSearchGetters()) {
                if (m.GetParameters().Length != 1 || m.GetParameters()[0].ParameterType != typeof(Vehicle)) continue;
                try {
                    object r = m.Invoke(null, new object[] { vehicle });
                    if (r != null && !(r is string) && r is IEnumerable en) {
                        items = en;
                        return true;
                    }
                } catch { /* try next */ }
            }
            return false;
        }

        /// <summary>After injectPedSearchItems + short yield, try every plausible static Ped → IEnumerable getter on StopThePed.</summary>
        internal static List<object> AggregateStpPedSearchItemObjects(Ped ped) {
            var bag = new List<object>();
            if (ped == null || !ped.IsValid()) return bag;
            var seenId = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (MethodInfo m in GetOrBuildPedSearchGetters()) {
                ParameterInfo[] ps = m?.GetParameters();
                if (ps == null || ps.Length != 1 || ps[0].ParameterType != typeof(Ped)) continue;
                try {
                    object r = m.Invoke(null, new object[] { ped });
                    if (r == null || !(r is IEnumerable enumerable) || r is string) continue;
                    int added = 0;
                    foreach (object it in enumerable) {
                        if (it == null) continue;
                        if (seenId.Add(it))
                            bag.Add(it);
                        added++;
                    }
                    // Skip empty getters so we collect from every source that yielded rows.
                    if (added == 0) continue;
                } catch {
                    /* try next */
                }
            }
            AppendExplicitStpFunctionPedLists(ped, bag, seenId);
            AppendStaticStpMenuItems(bag, seenId);
            return bag;
        }

        /// <summary>Known STP.Functions entrypoints not always surfaced by heuristic discovery (assembly visibility / naming).</summary>
        static void AppendExplicitStpFunctionPedLists(Ped ped, List<object> bag, HashSet<object> seenId) {
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            if (fn == null || ped == null || !ped.IsValid()) return;
            string[] cand = {
                "GetPedSearchItems", "GetPedStoppedSearchItems", "GetPedStoppedItems", "GetPedSearchResults",
                "GetPedTrafficStopDocuments", "GetPedStoppedDocuments", "GetPedDocumentsForSearch", "GetPedDocuments",
                "getPedSearchItems"
            };
            foreach (string name in cand) {
                MethodInfo m = ResolveStpPedMethod(fn, name);
                if (m == null) continue;
                try {
                    if (!TryInvokeStpPedMethod(ped, m, out object r)) continue;
                    if (r == null || !(r is IEnumerable enumerable) || r is string) continue;
                    foreach (object it in enumerable) {
                        if (it == null) continue;
                        if (seenId.Add(it))
                            bag.Add(it);
                    }
                } catch {
                    /* next */
                }
            }
        }

        /// <summary>STP's decompiled internals keep search/card rows as private static UIMenuItem fields.</summary>
        static void AppendStaticStpMenuItems(List<object> bag, HashSet<object> seenId) {
            Assembly asm = TryGetStopThePedAssembly();
            if (asm == null) return;
            foreach (Type t in SafeGetTypes(asm)) {
                try {
                    foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) {
                        Type ft = field.FieldType;
                        bool menuLike = (!string.IsNullOrEmpty(ft.FullName) && ft.FullName.IndexOf("RAGENativeUI", StringComparison.OrdinalIgnoreCase) >= 0)
                            || typeof(IEnumerable).IsAssignableFrom(ft);
                        if (!menuLike) continue;
                        object value = field.GetValue(null);
                        if (value == null || value is string) continue;
                        if (value is IEnumerable enumerable) {
                            foreach (object it in enumerable) {
                                if (it != null && !(it is string) && seenId.Add(it))
                                    bag.Add(it);
                            }
                        } else if (seenId.Add(value)) {
                            bag.Add(value);
                        }
                    }
                } catch {
                    /* private obfuscated type/field may throw */
                }
            }
        }

        /// <summary>First non-null Ped-search IEnumerable legacy hook (prefer <see cref="AggregateStpPedSearchItemObjects"/> for licence).</summary>
        internal static bool TryGetPedSearchItemsEnumerable(Ped ped, out IEnumerable items) {
            var bag = AggregateStpPedSearchItemObjects(ped);
            items = bag;
            return bag.Count > 0;
        }

        sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }


        private static List<MethodInfo> GetOrBuildVehicleSearchGetters() {
            if (_vehicleSearchGetters != null) return _vehicleSearchGetters;
            _vehicleSearchGetters = BuildSearchGetters(typeof(Vehicle));
            return _vehicleSearchGetters;
        }

        private static List<MethodInfo> GetOrBuildPedSearchGetters() {
            if (_pedSearchGetters != null) return _pedSearchGetters;
            _pedSearchGetters = BuildSearchGetters(typeof(Ped));
            return _pedSearchGetters;
        }

        private static bool NameLooksLikeSearchResultGetter(string methodName) {
            if (string.IsNullOrEmpty(methodName)) return false;
            string n = methodName.ToLowerInvariant();
            if (n.Contains("inject") || n.Contains("setvehicle") || n.Contains("setped")) return false;
            if (n.Contains("search") || n.Contains("item") || n.Contains("inventory") || n.Contains("seized")
                || n.Contains("found") || n.Contains("frisk") || n.Contains("contraband") || n.Contains("stash"))
                return true;
            // Docs / traffic-stop ped lists use different naming than plain "SearchItems".
            if (n.Contains("document") || n.Contains("paper") || n.Contains("licence") || n.Contains("license")
                || n.Contains("stopped") || n.Contains("traffic") || n.Contains("identif"))
                return true;
            return false;
        }

        private static List<MethodInfo> BuildSearchGetters(Type entityParam) {
            var list = new List<MethodInfo>();
            Assembly asm = TryGetStopThePedAssembly();
            if (asm == null) return list;
            Type[] types = SafeGetTypes(asm);
            foreach (Type t in types) {
                try {
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                        if (!m.IsStatic || m.IsSpecialName) continue;
                        ParameterInfo[] ps = m.GetParameters();
                        if (ps.Length != 1 || ps[0].ParameterType != entityParam) continue;
                        if (!typeof(IEnumerable).IsAssignableFrom(m.ReturnType) || m.ReturnType == typeof(string)) continue;
                        if (!NameLooksLikeSearchResultGetter(m.Name)) continue;
                        list.Add(m);
                    }
                } catch { /* type load */ }
            }
            return list.OrderByDescending(m => m.Name.Length).ToList();
        }

        private static Type[] SafeGetTypes(Assembly asm) {
            try {
                return asm.GetExportedTypes();
            } catch {
                try {
                    return asm.GetTypes();
                } catch (ReflectionTypeLoadException ex) {
                    return ex.Types?.Where(x => x != null).ToArray() ?? Array.Empty<Type>();
                } catch {
                    return Array.Empty<Type>();
                }
            }
        }

        /// <summary>StopThePed.API.Functions.isPedStopped — used to attach void STP events to a plausible ped.</summary>
        internal static bool TryIsPedStoppedStp(Ped ped) {
            if (ped == null || !ped.IsValid()) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            if (fn == null) return false;
            MethodInfo m = fn.GetMethod("isPedStopped", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Ped) }, null);
            if (m == null) return false;
            try {
                object r = m.Invoke(null, new object[] { ped });
                return r is bool b && b;
            } catch {
                return false;
            }
        }

        /// <summary>StopThePed.API.Functions.isPedAlcoholOverLimit — see STP/StopThePed.API.cs.</summary>
        internal static bool TryIsPedAlcoholOverLimit(Ped ped) {
            return TryInvokePedBool("isPedAlcoholOverLimit", ped);
        }

        /// <summary>StopThePed.API.Functions.isPedUnderDrugsInfluence — see STP/StopThePed.API.cs.</summary>
        internal static bool TryIsPedUnderDrugsInfluence(Ped ped) {
            return TryInvokePedBool("isPedUnderDrugsInfluence", ped);
        }

        private static bool TryInvokePedBool(string methodName, Ped ped) {
            if (ped == null || !ped.IsValid()) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            if (fn == null) return false;
            MethodInfo m = fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Ped) }, null);
            if (m == null) return false;
            try {
                object r = m.Invoke(null, new object[] { ped });
                return r is bool b && b;
            } catch {
                return false;
            }
        }

        internal static bool TryGetDriverLicenseCardFields(Ped ped, out string status, out string expiration, out string fullName) {
            status = null;
            expiration = null;
            fullName = null;
            if (ped == null || !ped.IsValid()) return false;

            TryGetStpPedDoc(
                ped,
                out string stpStatus,
                out string stpExpiration,
                "getPedDriverLicenseStatus",
                "getPedDriversLicenseStatus",
                "getDriverLicenseStatus",
                "getDriversLicenseStatus");
            TryGetStpPedDate(
                ped,
                out string stpExpirationFromMethod,
                "getPedDriverLicenseExpiration",
                "getPedDriverLicenseExpirationDate",
                "getPedDriversLicenseExpiration",
                "getPedDriversLicenseExpirationDate",
                "getDriverLicenseExpiration",
                "getDriverLicenseExpirationDate",
                "getDriversLicenseExpiration",
                "getDriversLicenseExpirationDate",
                "getDriverLicenseExpiry",
                "getDriverLicenseExpiryDate");

            if (!string.IsNullOrWhiteSpace(stpStatus))
                status = stpStatus;
            if (!string.IsNullOrWhiteSpace(stpExpiration))
                expiration = stpExpiration;
            else if (!string.IsNullOrWhiteSpace(stpExpirationFromMethod))
                expiration = stpExpirationFromMethod;

            try {
                var persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
                if (persona != null) {
                    fullName = persona.FullName;
                    if (string.IsNullOrWhiteSpace(status))
                        status = persona.ELicenseState.ToString();
                    if (string.IsNullOrWhiteSpace(expiration) && TryGetPersonaDriverLicenseExpiration(persona, out string personaExpiration))
                        expiration = personaExpiration;
                }
            } catch { }

            return !string.IsNullOrWhiteSpace(status)
                || !string.IsNullOrWhiteSpace(expiration)
                || !string.IsNullOrWhiteSpace(fullName);
        }

        internal static bool TryGetPersonaDriverLicenseExpiration(object persona, out string expiration) {
            expiration = null;
            if (persona == null) return false;
            Type t = persona.GetType();
            foreach (string propName in new[] {
                "DriverLicenseExpiration", "DriversLicenseExpiration",
                "DriverLicenseExpirationDate", "DriversLicenseExpirationDate",
                "LicenseExpiration", "LicenseExpirationDate",
                "LicenceExpiration", "LicenceExpirationDate",
                "DriverLicenceExpiration", "DriverLicenceExpirationDate",
                "DriversLicenceExpiration", "DriversLicenceExpirationDate",
                "LicenseExpiry", "LicenseExpiryDate",
                "LicenceExpiry", "LicenceExpiryDate"
            }) {
                try {
                    PropertyInfo prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null || !prop.CanRead) continue;
                    object value = prop.GetValue(persona);
                    if (TryFormatStpDateResult(value, out expiration)) return true;
                } catch { }
            }

            // STP internals read a DateTime from Persona for the driver licence card. Keep the fallback constrained
            // to licence-named members so Birthday / DOB cannot be mistaken for expiry.
            foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                try {
                    if (!prop.CanRead) continue;
                    string name = prop.Name ?? "";
                    string n = name.ToLowerInvariant();
                    if (!(n.Contains("licen") || n.Contains("licence")) || !(n.Contains("expir") || n.Contains("expiry") || n.Contains("valid")))
                        continue;
                    object value = prop.GetValue(persona);
                    if (TryFormatStpDateResult(value, out expiration)) return true;
                } catch { }
            }
            return false;
        }

        private static bool TryGetStpPedDoc(Ped ped, out string status, out string expiration, params string[] methodNames) {
            status = null;
            expiration = null;
            if (methodNames == null) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            foreach (string methodName in methodNames) {
                if (!TryInvokeStpPedMethod(ped, methodName, out object r)) continue;
                TryFormatStpVehicleStatusResult(r, out status);
                TryFormatStpDateResult(r, out expiration);
                return !string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(expiration);
            }
            if (fn != null) {
                foreach (MethodInfo m in FindCompatibleStpMethods(
                    fn,
                    new[] { typeof(Ped), typeof(Entity), typeof(uint) },
                    new[] { "license" },
                    new[] { "status", "state" })) {
                    if (!TryInvokeStpPedMethod(ped, m, out object r)) continue;
                    TryFormatStpVehicleStatusResult(r, out status);
                    TryFormatStpDateResult(r, out expiration);
                    return !string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(expiration);
                }
            }
            return false;
        }

        private static bool TryGetStpPedDate(Ped ped, out string expiration, params string[] methodNames) {
            expiration = null;
            if (methodNames == null) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            foreach (string methodName in methodNames) {
                if (TryInvokeStpPedMethod(ped, methodName, out object r)
                    && TryFormatStpDateResult(r, out expiration))
                    return true;
            }
            if (fn != null) {
                foreach (MethodInfo m in FindCompatibleStpMethods(
                    fn,
                    new[] { typeof(Ped), typeof(Entity), typeof(uint) },
                    new[] { "license" },
                    DateMethodTokens)) {
                    if (TryInvokeStpPedMethod(ped, m, out object r)
                        && TryFormatStpDateResult(r, out expiration))
                        return true;
                }
            }
            return false;
        }

        private static bool TryInvokeStpPedMethod(Ped ped, string methodName, out object result) {
            result = null;
            if (ped == null || !ped.IsValid()) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            if (fn == null) return false;
            MethodInfo m = ResolveStpPedMethod(fn, methodName);
            if (m == null) return false;
            return TryInvokeStpPedMethod(ped, m, out result);
        }

        private static bool TryInvokeStpPedMethod(Ped ped, MethodInfo m, out object result) {
            result = null;
            if (ped == null || !ped.IsValid() || m == null) return false;
            try {
                Type pt = m.GetParameters()[0].ParameterType;
                object arg = pt == typeof(uint) ? (object)ped.Handle : pt == typeof(Entity) ? (Entity)ped : (object)ped;
                result = m.Invoke(null, new[] { arg });
                return result != null;
            } catch {
                return false;
            }
        }

        private static MethodInfo ResolveStpPedMethod(Type fn, string methodName) {
            MethodInfo m = fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Ped) }, null)
                ?? fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Entity) }, null)
                ?? fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(uint) }, null);
            if (m != null) return m;
            foreach (MethodInfo cand in fn.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                if (!string.Equals(cand.Name, methodName, StringComparison.OrdinalIgnoreCase)) continue;
                ParameterInfo[] ps = cand.GetParameters();
                if (ps.Length != 1) continue;
                Type pt = ps[0].ParameterType;
                if (pt == typeof(Ped) || pt == typeof(Entity) || pt == typeof(uint))
                    return cand;
            }
            return null;
        }

        /// <summary>StopThePed.API.Functions.getVehicleRegistrationStatus → <c>STPVehicleStatus</c> (STP/StopThePed.API.cs). Single source for what STP shows after a check, separate from CDF document enums.</summary>
        internal static bool TryGetVehicleRegistrationStatusStp(Vehicle veh, out string status) {
            return TryGetStpVehicleDocStatus(veh, "getVehicleRegistrationStatus", out status);
        }

        internal static bool TryGetVehicleRegistrationDocumentStp(Vehicle veh, out string status, out string expiration) {
            return TryGetStpVehicleDoc(
                veh,
                "getVehicleRegistrationStatus",
                "registration",
                out status,
                out expiration,
                "getVehicleRegistrationExpiration",
                "getVehicleRegistrationExpirationDate",
                "getVehicleRegistrationExpiry",
                "getVehicleRegistrationExpiryDate",
                "getVehicleRegistrationExpireDate");
        }

        /// <summary>StopThePed.API.Functions.getVehicleInsuranceStatus → <c>STPVehicleStatus</c>.</summary>
        internal static bool TryGetVehicleInsuranceStatusStp(Vehicle veh, out string status) {
            return TryGetStpVehicleDocStatus(veh, "getVehicleInsuranceStatus", out status);
        }

        internal static bool TryGetVehicleInsuranceDocumentStp(Vehicle veh, out string status, out string expiration) {
            return TryGetStpVehicleDoc(
                veh,
                "getVehicleInsuranceStatus",
                "insurance",
                out status,
                out expiration,
                "getVehicleInsuranceExpiration",
                "getVehicleInsuranceExpirationDate",
                "getVehicleInsuranceExpiry",
                "getVehicleInsuranceExpiryDate",
                "getVehicleInsuranceExpireDate");
        }

        private static bool TryGetStpVehicleDocStatus(Vehicle veh, string methodName, out string status) {
            bool ok = TryGetStpVehicleDoc(veh, methodName, null, out status, out _, Array.Empty<string>());
            return ok;
        }

        private static bool TryGetStpVehicleDoc(Vehicle veh, string methodName, string documentToken, out string status, out string expiration, params string[] expirationMethodNames) {
            status = null;
            expiration = null;
            if (veh == null || !veh.Exists()) return false;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            bool hasStatus = false;
            bool hasExpiration = false;
            if (TryInvokeStpVehicleMethod(veh, methodName, out object r)) {
                hasStatus = TryFormatStpVehicleStatusResult(r, out status);
                hasExpiration = TryFormatStpDateResult(r, out expiration);
            }
            if (!hasStatus && fn != null && !string.IsNullOrWhiteSpace(documentToken)) {
                foreach (MethodInfo m in FindCompatibleStpMethods(
                    fn,
                    new[] { typeof(Vehicle), typeof(Entity), typeof(uint) },
                    new[] { documentToken },
                    new[] { "status", "state" })) {
                    if (!TryInvokeStpVehicleMethod(veh, m, out object statusResult)) continue;
                    hasStatus = TryFormatStpVehicleStatusResult(statusResult, out status);
                    if (!hasExpiration)
                        hasExpiration = TryFormatStpDateResult(statusResult, out expiration);
                    if (hasStatus || hasExpiration) break;
                }
            }
            if (string.IsNullOrWhiteSpace(expiration) && expirationMethodNames != null) {
                foreach (string expirationMethodName in expirationMethodNames) {
                    if (TryInvokeStpVehicleMethod(veh, expirationMethodName, out object expResult)
                        && TryFormatStpDateResult(expResult, out expiration)) {
                        hasExpiration = true;
                        break;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(expiration) && fn != null && !string.IsNullOrWhiteSpace(documentToken)) {
                foreach (MethodInfo m in FindCompatibleStpMethods(
                    fn,
                    new[] { typeof(Vehicle), typeof(Entity), typeof(uint) },
                    new[] { documentToken },
                    DateMethodTokens)) {
                    if (TryInvokeStpVehicleMethod(veh, m, out object expResult)
                        && TryFormatStpDateResult(expResult, out expiration)) {
                        hasExpiration = true;
                        break;
                    }
                }
            }
            return hasStatus || hasExpiration;
        }

        private static bool TryInvokeStpVehicleMethod(Vehicle veh, string methodName, out object result) {
            result = null;
            Type fn = ModIntegration.FindTypeInLoadedAssemblies("StopThePed.API.Functions");
            if (fn == null) return false;
            MethodInfo m = ResolveStpVehicleMethod(fn, methodName);
            if (m == null) return false;
            return TryInvokeStpVehicleMethod(veh, m, out result);
        }

        private static bool TryInvokeStpVehicleMethod(Vehicle veh, MethodInfo m, out object result) {
            result = null;
            if (veh == null || !veh.Exists() || m == null) return false;
            try {
                Type pt = m.GetParameters()[0].ParameterType;
                object arg = pt == typeof(uint) ? (object)veh.Handle : pt == typeof(Entity) ? (Entity)veh : (object)veh;
                result = m.Invoke(null, new[] { arg });
                return result != null;
            } catch {
                return false;
            }
        }

        private static MethodInfo ResolveStpVehicleMethod(Type fn, string methodName) {
            MethodInfo m = fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Vehicle) }, null)
                ?? fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(Entity) }, null)
                ?? fn.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase, null, new[] { typeof(uint) }, null);
            if (m != null) return m;
            foreach (MethodInfo cand in fn.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                if (!string.Equals(cand.Name, methodName, StringComparison.OrdinalIgnoreCase)) continue;
                ParameterInfo[] ps = cand.GetParameters();
                if (ps.Length != 1) continue;
                Type pt = ps[0].ParameterType;
                if (pt == typeof(Vehicle) || pt == typeof(Entity) || pt == typeof(uint))
                    return cand;
            }
            return null;
        }

        private static bool TryFormatStpVehicleStatusResult(object r, out string status) {
            status = null;
            if (r == null) return false;
            if (r is string s) {
                status = s.Trim();
                return status.Length > 0;
            }
            Type t = r.GetType();
            if (t.IsEnum) {
                status = Enum.GetName(t, r) ?? r.ToString();
                return !string.IsNullOrEmpty(status);
            }
            try {
                PropertyInfo stProp = t.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance);
                if (stProp != null) {
                    object v = stProp.GetValue(r);
                    if (v != null) {
                        Type vt = v.GetType();
                        if (vt.IsEnum) status = Enum.GetName(vt, v) ?? v.ToString();
                        else status = v.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(status)) return true;
                    }
                }
            } catch { /* ignore */ }
            status = r.ToString()?.Trim();
            return !string.IsNullOrEmpty(status) && status != t.FullName;
        }

        private static readonly string[] DateMethodTokens = { "expiration", "expiry", "expire", "expires", "validuntil", "valid" };

        private static IEnumerable<MethodInfo> FindCompatibleStpMethods(Type fn, Type[] allowedParameterTypes, string[] requiredTokens, string[] anyTokens) {
            if (fn == null || allowedParameterTypes == null) yield break;
            foreach (MethodInfo cand in fn.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                if (cand.IsSpecialName) continue;
                ParameterInfo[] ps = cand.GetParameters();
                if (ps.Length != 1) continue;
                Type pt = ps[0].ParameterType;
                if (!allowedParameterTypes.Contains(pt)) continue;
                string n = cand.Name.ToLowerInvariant();
                if (requiredTokens != null && requiredTokens.Any(token => !n.Contains(token.ToLowerInvariant()))) continue;
                if (anyTokens != null && anyTokens.Length > 0 && !anyTokens.Any(token => n.Contains(token.ToLowerInvariant()))) continue;
                yield return cand;
            }
        }

        private static bool TryFormatStpDateResult(object r, out string date) {
            date = null;
            if (r == null) return false;
            if (r is DateTime dt) {
                date = dt.ToString("s", CultureInfo.InvariantCulture);
                return true;
            }
            if (r is DateTimeOffset dto) {
                date = dto.UtcDateTime.ToString("s", CultureInfo.InvariantCulture);
                return true;
            }
            if (r is string sRaw) {
                string s = sRaw.Trim();
                if (string.IsNullOrEmpty(s)) return false;
                string[] exact = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "M/d/yyyy", "yyyyMMdd", "dd-MM-yyyy", "MM-dd-yyyy" };
                foreach (string fmt in exact) {
                    if (DateTime.TryParseExact(s, fmt, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime parsedInv)
                        || DateTime.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedInv)
                        || DateTime.TryParseExact(s, fmt, CultureInfo.GetCultureInfo("en-AU"), DateTimeStyles.None, out parsedInv)
                        || DateTime.TryParseExact(s, fmt, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out parsedInv)) {
                        date = parsedInv.ToString("s", CultureInfo.InvariantCulture);
                        return true;
                    }
                }
                if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("en-AU"), DateTimeStyles.AllowWhiteSpaces, out DateTime parsedAu)
                    || DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsedAu)) {
                    date = parsedAu.ToString("s", CultureInfo.InvariantCulture);
                    return true;
                }
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out DateTime parsedLoose)
                    || DateTime.TryParse(s, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out parsedLoose)
                    || DateTime.TryParse(s, out parsedLoose)) {
                    date = parsedLoose.ToString("s", CultureInfo.InvariantCulture);
                    return true;
                }
                return false;
            }

            Type t = r.GetType();
            foreach (string propName in new[] {
                "ExpirationDate", "Expiration", "ExpiryDate", "Expiry", "ExpireDate", "ExpiresAt",
                "ValidUntil", "ValidThrough", "ValidTo", "EndDate", "Expires", "ExpiresOn",
                "Expire", "LicenceExpiry", "LicenseExpiry",
            }) {
                try {
                    PropertyInfo prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null) continue;
                    object v = prop.GetValue(r);
                    if (TryFormatStpDateResult(v, out date)) return true;
                } catch { }
            }
            return false;
        }
    }
}
