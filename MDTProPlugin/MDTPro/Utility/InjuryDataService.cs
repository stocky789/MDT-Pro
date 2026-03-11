using CommonDataFramework.Modules.PedDatabase;
using MDTPro.Data;
using Rage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace MDTPro.Utility {
    /// <summary>Provides in-game injury data for injury reports: optional DamageTrackerFramework integration plus ped health fallback.</summary>
    internal static class InjuryDataService {
        private static readonly object _lock = new object();
        private static bool _dtfStarted;
        private static readonly Dictionary<uint, CachedDamageEntry> _damageByVictimHandle = new Dictionary<uint, CachedDamageEntry>();
        /// <summary>Cache by victim name so we can return injury data after the ped has despawned (e.g. killed).</summary>
        private static readonly Dictionary<string, CachedDamageEntry> _damageByVictimName = new Dictionary<string, CachedDamageEntry>(StringComparer.OrdinalIgnoreCase);
        private static Delegate _pedDamageHandler;
        private static Delegate _playerDamageHandler;
        private const int MaxCachedEntries = 50;
        private const int CacheExpireSeconds = 300;

        private sealed class CachedDamageEntry {
            public int Damage;
            public int ArmourDamage;
            public string WeaponType;
            public string WeaponGroup;
            public string BodyRegion;
            public bool VictimAlive;
            public DateTime At;

            public bool IsExpired => (DateTime.UtcNow - At).TotalSeconds > CacheExpireSeconds;
        }

        /// <summary>Call when going on duty. Starts DTF if available; no-op if not.</summary>
        internal static void Start() {
            lock (_lock) {
                if (_dtfStarted) return;
                TryStartDamageTrackerFramework();
                _dtfStarted = true;
            }
        }

        /// <summary>Call when going off duty. Stops DTF if it was started.</summary>
        internal static void Stop() {
            lock (_lock) {
                TryStopDamageTrackerFramework();
                _damageByVictimHandle.Clear();
                _damageByVictimName.Clear();
                _dtfStarted = false;
            }
        }

        /// <summary>Gets injury data for the given ped (by name). Uses DTF buffer if available and we have recent damage for that ped; otherwise falls back to current health/armor.</summary>
        internal static InjuryGameData GetInjuryGameDataForPed(string pedName) {
            if (string.IsNullOrWhiteSpace(pedName)) {
                Helper.Log("Injury data: no ped name provided.", false, Helper.LogSeverity.Info);
                return null;
            }

            var pedData = Data.DataController.GetPedDataByName(pedName);
            Rage.Ped ped = pedData?.Holder;
            if (ped != null && ped.IsValid()) {
                lock (_lock) {
                    if (_damageByVictimHandle.TryGetValue(ped.Handle, out var entry) && !entry.IsExpired) {
                        return BuildGameDataFromDamage(entry, ped);
                    }
                }
                return BuildGameDataFromHealth(ped);
            }

            // Ped in database but not in world (e.g. killed/despawned): try in-memory cache then permanent SQL cache
            lock (_lock) {
                if (_damageByVictimName.TryGetValue(pedName, out var nameEntry) && !nameEntry.IsExpired) {
                    return BuildGameDataFromDamageEntryOnly(nameEntry);
                }
            }
            var sqlEntry = Database.LoadDamageCacheByVictimName(pedName);
            if (sqlEntry.HasValue) {
                var e = sqlEntry.Value;
                return BuildGameDataFromDamageEntryOnly(new CachedDamageEntry {
                    Damage = e.Damage,
                    ArmourDamage = e.ArmourDamage,
                    WeaponType = e.WeaponType,
                    WeaponGroup = e.WeaponGroup,
                    BodyRegion = e.BodyRegion,
                    VictimAlive = e.VictimAlive,
                    At = e.At
                });
            }

            if (pedData == null)
                Helper.Log($"Injury data: no ped in database for '{pedName}'. Collect their ID first (e.g. traffic stop).", false, Helper.LogSeverity.Info);
            else
                Helper.Log($"Injury data: ped '{pedName}' is in database but not in world (Holder invalid/despawned). No recent DamageTracker cache for this name.", false, Helper.LogSeverity.Info);
            return null;
        }

        /// <summary>Uses context ped when pedName is null/empty and context is available.</summary>
        internal static InjuryGameData GetInjuryGameDataForContext() {
            var context = Data.DataController.GetContextPedIfValid();
            return context != null ? GetInjuryGameDataForPed(context.Name) : null;
        }

        private static InjuryGameData BuildGameDataFromDamage(CachedDamageEntry e, Ped ped) {
            string injuryType = MapWeaponToInjuryType(e.WeaponType, e.WeaponGroup);
            string severity = !ped.IsAlive ? "Critical" : MapDamageToSeverity(e.Damage, e.ArmourDamage, e.BodyRegion);
            string treatment = MapSeverityToTreatment(severity, ped.IsAlive);

            string desc = string.Join(", ",
                new[] { injuryType, e.BodyRegion, $"{e.Damage} dmg" }.Where(s => !string.IsNullOrEmpty(s)));

            return new InjuryGameData {
                Source = "DamageTracker",
                InjuryType = injuryType,
                Severity = severity,
                Treatment = treatment,
                BodyRegion = e.BodyRegion,
                WeaponGroup = e.WeaponGroup,
                DamageAmount = e.Damage,
                ArmourDamage = e.ArmourDamage,
                Health = ped.Health,
                MaxHealth = ped.MaxHealth,
                Armor = ped.Armor,
                VictimAlive = ped.IsAlive,
                Description = desc
            };
        }

        /// <summary>Build injury data from cached damage only (ped no longer in world).</summary>
        private static InjuryGameData BuildGameDataFromDamageEntryOnly(CachedDamageEntry e) {
            string injuryType = MapWeaponToInjuryType(e.WeaponType, e.WeaponGroup);
            string severity = !e.VictimAlive ? "Critical" : MapDamageToSeverity(e.Damage, e.ArmourDamage, e.BodyRegion);
            string treatment = MapSeverityToTreatment(severity, e.VictimAlive);
            string desc = string.Join(", ",
                new[] { injuryType, e.BodyRegion, $"{e.Damage} dmg" }.Where(s => !string.IsNullOrEmpty(s)));
            if (!string.IsNullOrEmpty(desc) && !e.VictimAlive) desc += " (victim deceased)";
            return new InjuryGameData {
                Source = "DamageTracker",
                InjuryType = injuryType,
                Severity = severity,
                Treatment = treatment,
                BodyRegion = e.BodyRegion,
                WeaponGroup = e.WeaponGroup,
                DamageAmount = e.Damage,
                ArmourDamage = e.ArmourDamage,
                Health = null,
                MaxHealth = null,
                Armor = null,
                VictimAlive = e.VictimAlive,
                Description = desc
            };
        }

        private static InjuryGameData BuildGameDataFromHealth(Ped ped) {
            if (ped == null || !ped.IsValid()) return null;
            int health = ped.Health;
            int max = ped.MaxHealth;
            if (max <= 0) max = 200;
            int armor = ped.Armor;
            float pct = max > 0 ? (float)health / max : 0f;
            string severity = pct >= 0.76f ? "Minor" : pct >= 0.51f ? "Moderate" : pct >= 0.26f ? "Serious" : "Critical";
            string treatment = MapSeverityToTreatment(severity, ped.IsAlive);

            return new InjuryGameData {
                Source = "Health",
                InjuryType = null,
                Severity = severity,
                Treatment = treatment,
                Health = health,
                MaxHealth = max,
                Armor = armor,
                VictimAlive = ped.IsAlive,
                Description = $"Health {health}/{max}, Armor {armor} (inferred severity: {severity})"
            };
        }

        /// <summary>Fall often overwrites gunshot when victim hits ground; don't let it replace more significant causes.</summary>
        private static bool IsFallOrMinor(string injuryType) {
            if (string.IsNullOrEmpty(injuryType)) return true;
            return string.Equals(injuryType, "Fall", StringComparison.OrdinalIgnoreCase);
        }

        private static string MapWeaponToInjuryType(string weaponType, string weaponGroup) {
            if (string.IsNullOrEmpty(weaponType)) weaponType = "";
            if (string.IsNullOrEmpty(weaponGroup)) weaponGroup = "";
            var t = weaponType.ToUpperInvariant();
            var g = weaponGroup.ToUpperInvariant();

            if (g.Contains("BULLET") || t.Contains("PISTOL") || t.Contains("RIFLE") || t.Contains("SMG") || t.Contains("SHOTGUN") || t.Contains("SNIPER") || t.Contains("MG"))
                return "Gunshot";
            if (t.Contains("MELEE") && t.Contains("STAB")) return "Stab wound";
            if (t.Contains("MELEE") && t.Contains("BLUNT")) return "Blunt trauma";
            if (t.Contains("UNARMED")) return "Assault (unarmed)";
            if (t.Contains("LESS") && t.Contains("LETHAL")) return "Less lethal";
            if (t.Contains("FIRE")) return "Burns";
            if (t.Contains("VEHICLE")) return "Vehicle impact";
            if (t.Contains("FALL")) return "Fall";
            if (t.Contains("ANIMAL")) return "Animal attack";
            if (t.Contains("DROWNING")) return "Drowning";
            if (t.Contains("EXPLOSION") || t.Contains("EXPLOSIVE") || t.Contains("LAUNCHER")) return "Explosion";
            if (g.Contains("MELEE")) return "Blunt trauma";

            return !string.IsNullOrEmpty(weaponType) ? weaponType : "Injury";
        }

        private static string MapDamageToSeverity(int damage, int armourDamage, string bodyRegion) {
            bool head = !string.IsNullOrEmpty(bodyRegion) && bodyRegion.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0;
            int total = damage + armourDamage;
            if (head && total >= 20) return "Critical";
            if (!head && total >= 60) return "Critical";
            if (total >= 40) return "Serious";
            if (total >= 20) return "Moderate";
            return "Minor";
        }

        private static string MapSeverityToTreatment(string severity, bool victimAlive) {
            if (!victimAlive) return "Pronounced deceased on scene";
            if (string.IsNullOrEmpty(severity)) return "None";
            switch (severity.ToUpperInvariant()) {
                case "CRITICAL": return "Transported to hospital";
                case "SERIOUS": return "EMS on scene";
                case "MODERATE": return "First aid on scene";
                case "MINOR": return "First aid on scene";
                default: return "None";
            }
        }

        #region DamageTrackerFramework (optional, reflection-based)

        private static void TryStartDamageTrackerFramework() {
            try {
                Type serviceType = FindDamageTrackerServiceType();
                if (serviceType == null) return;

                MethodInfo start = serviceType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (start != null) start.Invoke(null, null);

                Type damageInfoType = FindPedDamageInfoType();
                if (damageInfoType == null) return;

                _pedDamageHandler = CreateDamageHandler(damageInfoType, false);
                _playerDamageHandler = CreateDamageHandler(damageInfoType, true);
                if (_pedDamageHandler == null && _playerDamageHandler == null) return;

                EventInfo pedEvent = serviceType.GetEvent("OnPedTookDamage", BindingFlags.Public | BindingFlags.Static);
                EventInfo playerEvent = serviceType.GetEvent("OnPlayerTookDamage", BindingFlags.Public | BindingFlags.Static);
                if (pedEvent != null && _pedDamageHandler != null) pedEvent.AddEventHandler(null, _pedDamageHandler);
                if (playerEvent != null && _playerDamageHandler != null) playerEvent.AddEventHandler(null, _playerDamageHandler);

                Helper.Log("DamageTrackerFramework detected and subscribed for injury report data.", false, Helper.LogSeverity.Info);
            } catch (Exception ex) {
                Helper.Log($"DamageTrackerFramework not available for injury data: {ex.Message}", false, Helper.LogSeverity.Info);
            }
        }

        private static void TryStopDamageTrackerFramework() {
            try {
                Type serviceType = FindDamageTrackerServiceType();
                if (serviceType == null) return;
                EventInfo pedEvent = serviceType.GetEvent("OnPedTookDamage", BindingFlags.Public | BindingFlags.Static);
                EventInfo playerEvent = serviceType.GetEvent("OnPlayerTookDamage", BindingFlags.Public | BindingFlags.Static);
                if (pedEvent != null && _pedDamageHandler != null) { pedEvent.RemoveEventHandler(null, _pedDamageHandler); _pedDamageHandler = null; }
                if (playerEvent != null && _playerDamageHandler != null) { playerEvent.RemoveEventHandler(null, _playerDamageHandler); _playerDamageHandler = null; }
                MethodInfo stop = serviceType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (stop != null) stop.Invoke(null, null);
            } catch { /* ignore */ }
        }

        private static Type FindDamageTrackerServiceType() {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    if (asm.GetName().Name?.Equals("DamageTrackerLib", StringComparison.OrdinalIgnoreCase) != true) continue;
                    Type t = asm.GetType("DamageTrackerLib.DamageTrackerService");
                    if (t != null) return t;
                } catch { }
            }
            return null;
        }

        private static Type FindPedDamageInfoType() {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    if (asm.GetName().Name?.Equals("DamageTrackerLib", StringComparison.OrdinalIgnoreCase) != true) continue;
                    Type t = asm.GetType("DamageTrackerLib.DamageInfo.PedDamageInfo");
                    if (t != null) return t;
                } catch { }
            }
            return null;
        }

        private static Delegate CreateDamageHandler(Type damageInfoType, bool forPlayer) {
            try {
                Type serviceType = FindDamageTrackerServiceType();
                if (serviceType == null) return null;
                EventInfo ev = forPlayer
                    ? serviceType.GetEvent("OnPlayerTookDamage", BindingFlags.Public | BindingFlags.Static)
                    : serviceType.GetEvent("OnPedTookDamage", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) return null;

                Type delegateType = ev.EventHandlerType;
                MethodInfo invoke = delegateType.GetMethod("Invoke");
                ParameterInfo[] ps = invoke.GetParameters();
                if (ps.Length != 3 || ps[0].ParameterType != typeof(Ped) || ps[1].ParameterType != typeof(Ped) || ps[2].ParameterType != damageInfoType)
                    return null;

                MethodInfo ourHandler = typeof(InjuryDataService).GetMethod(nameof(OnDamageEvent), BindingFlags.NonPublic | BindingFlags.Static);
                if (ourHandler == null) return null;

                var dm = new DynamicMethod("DTF_DamageHandler", null, new[] { typeof(Ped), typeof(Ped), damageInfoType }, typeof(InjuryDataService).Module, true);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Box, damageInfoType);
                il.Emit(OpCodes.Call, ourHandler);
                il.Emit(OpCodes.Ret);
                return dm.CreateDelegate(delegateType);
            } catch {
                return null;
            }
        }

        private static void OnDamageEvent(Ped victim, Ped attacker, object damageInfo) {
            if (victim == null || !victim.IsValid()) return;
            try {
                Type t = damageInfo?.GetType();
                if (t == null) return;

                int damage = GetStructInt(t, damageInfo, "Damage");
                int armourDamage = GetStructInt(t, damageInfo, "ArmourDamage");
                string weaponType = GetWeaponTypeString(t, damageInfo);
                string weaponGroup = GetWeaponGroupString(t, damageInfo);
                string bodyRegion = GetBodyRegionString(t, damageInfo);
                bool victimAlive = victim.IsAlive;
                var atUtc = DateTime.UtcNow;

                var entry = new CachedDamageEntry {
                    Damage = damage,
                    ArmourDamage = armourDamage,
                    WeaponType = weaponType,
                    WeaponGroup = weaponGroup,
                    BodyRegion = bodyRegion,
                    VictimAlive = victimAlive,
                    At = atUtc
                };

                string victimName = GetPedName(victim);
                if (!string.IsNullOrWhiteSpace(victimName)) {
                    Database.SaveDamageCacheEntry(victimName, damage, armourDamage, weaponType, weaponGroup, bodyRegion, victimAlive, atUtc);
                    if (!victimAlive && Data.DataController.GetPedDataByName(victimName) != null) {
                        string attackerName = attacker != null && attacker.IsValid() ? GetPedName(attacker) : null;
                        string weaponInfo = string.Join("/", new[] { weaponType, weaponGroup }.Where(s => !string.IsNullOrEmpty(s)));
                        Data.DataController.MarkPedDeceased(victimName, attackerName, weaponInfo);
                    }
                }

                lock (_lock) {
                    PruneCache();
                    bool overwrite = true;
                    if (!string.IsNullOrWhiteSpace(victimName) && _damageByVictimName.TryGetValue(victimName, out var existing)) {
                        string newType = MapWeaponToInjuryType(weaponType, weaponGroup);
                        string existingType = MapWeaponToInjuryType(existing.WeaponType, existing.WeaponGroup);
                        if (IsFallOrMinor(newType) && !IsFallOrMinor(existingType))
                            overwrite = false;
                    }
                    if (overwrite) {
                        _damageByVictimHandle[victim.Handle] = entry;
                        if (!string.IsNullOrWhiteSpace(victimName))
                            _damageByVictimName[victimName] = entry;
                    }
                }
            } catch (Exception ex) {
                Helper.Log($"InjuryDataService OnDamageEvent: {ex.Message}", false, Helper.LogSeverity.Warning);
            }
        }

        private static string GetPedName(Ped ped) {
            if (ped == null || !ped.IsValid()) return null;
            try {
                var cdf = ped.GetPedData();
                if (cdf != null && !string.IsNullOrWhiteSpace(cdf.FullName)) return cdf.FullName.Trim();
            } catch { }
            try {
                var persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
                if (persona != null && !string.IsNullOrEmpty(persona.FullName)) return persona.FullName.Trim();
            } catch { }
            return null;
        }

        private static int GetStructInt(Type structType, object obj, string fieldName) {
            var f = structType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (f?.FieldType == typeof(int)) return (int)f.GetValue(obj);
            return 0;
        }

        private static string GetWeaponTypeString(Type damageInfoType, object damageInfo) {
            object weaponInfo = GetStructField(damageInfoType, damageInfo, "WeaponInfo");
            if (weaponInfo == null) return null;
            Type wi = weaponInfo.GetType();
            object typeEnum = GetStructField(wi, weaponInfo, "Type");
            return typeEnum?.ToString();
        }

        private static string GetWeaponGroupString(Type damageInfoType, object damageInfo) {
            object weaponInfo = GetStructField(damageInfoType, damageInfo, "WeaponInfo");
            if (weaponInfo == null) return null;
            Type wi = weaponInfo.GetType();
            object groupEnum = GetStructField(wi, weaponInfo, "Group");
            return groupEnum?.ToString();
        }

        private static string GetBodyRegionString(Type damageInfoType, object damageInfo) {
            object boneInfo = GetStructField(damageInfoType, damageInfo, "BoneInfo");
            if (boneInfo == null) return null;
            Type bi = boneInfo.GetType();
            object regionEnum = GetStructField(bi, boneInfo, "BodyRegion");
            return regionEnum?.ToString();
        }

        private static object GetStructField(Type structType, object obj, string fieldName) {
            var f = structType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            return f?.GetValue(obj);
        }

        private static void PruneCache() {
            var expired = _damageByVictimHandle.Where(kv => kv.Value.IsExpired).Select(kv => kv.Key).ToList();
            foreach (var k in expired) _damageByVictimHandle.Remove(k);
            while (_damageByVictimHandle.Count > MaxCachedEntries) {
                var oldest = _damageByVictimHandle.OrderBy(kv => kv.Value.At).First().Key;
                _damageByVictimHandle.Remove(oldest);
            }
            var expiredNames = _damageByVictimName.Where(kv => kv.Value.IsExpired).Select(kv => kv.Key).ToList();
            foreach (var k in expiredNames) _damageByVictimName.Remove(k);
            while (_damageByVictimName.Count > MaxCachedEntries) {
                var oldest = _damageByVictimName.OrderBy(kv => kv.Value.At).First().Key;
                _damageByVictimName.Remove(oldest);
            }
        }

        #endregion
    }
}
