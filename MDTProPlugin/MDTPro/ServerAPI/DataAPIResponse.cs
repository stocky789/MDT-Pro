using MDTPro.Data;
using MDTPro.Data.Reports;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;

namespace MDTPro.ServerAPI {
    internal class DataAPIResponse : APIResponse {
        internal DataAPIResponse(HttpListenerRequest req) : base(null) {
            string path = req.Url.AbsolutePath.Substring("/data/".Length);
            if (string.IsNullOrEmpty(path)) return;
            else if (path == "peds") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.PedDatabase));
                status = 200;
                contentType = "text/json";
            } else if (path == "vehicles") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.VehicleDatabase));
                status = 200;
                contentType = "text/json";
            } else if (path == "nearbyVehicles") {
                string body = Helper.GetRequestPostData(req);
                int limit = 5;
                if (int.TryParse(body, out int parsedLimit)) {
                    limit = parsedLimit;
                }
                if (limit < 1) limit = 1;
                if (limit > 20) limit = 20;

                DataController.RefreshCachedNearbyVehiclesOnGameFiberBlocking();
                var cached = DataController.GetCachedNearbyVehicles(limit);
                var nearbyVehicles = cached.Select(x => new {
                    x.LicensePlate,
                    x.ModelDisplayName,
                    Distance = x.Distance,
                    x.IsStolen
                });

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(nearbyVehicles));
                status = 200;
                contentType = "text/json";
            } else if (path == "specificPed") {
                string body = Helper.GetRequestPostData(req);
                string name = !string.IsNullOrEmpty(body) ? body.Trim() : "";
                string reversedName = string.Join(" ", name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Reverse());

                // Prefer context ped when it matches the search name (person in front of you just got ID)
                MDTProPedData pedData = null;
                if (!string.IsNullOrEmpty(name) && name != "context" && name != "%context" && !name.Equals("current", StringComparison.OrdinalIgnoreCase)) {
                    var contextPed = DataController.GetContextPedIfValid();
                    if (contextPed != null && (contextPed.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true || contextPed.Name?.Equals(reversedName, StringComparison.OrdinalIgnoreCase) == true)) {
                        pedData = contextPed;
                    }
                }
                if (pedData == null) {
                    pedData = DataController.GetPedDataByName(name) ?? DataController.GetPedDataByName(reversedName);
                }
                if (pedData == null) {
                    pedData = DataController.PedDatabase.FirstOrDefault(o => o.Name?.ToLower() == name.ToLower() || o.Name?.ToLower() == reversedName.ToLower());
                }
                if (pedData == null && (name == "context" || name == "%context" || name.Equals("current", StringComparison.OrdinalIgnoreCase))) {
                    pedData = DataController.GetContextPedIfValid();
                }

                Database.SaveSearchHistoryEntry("ped", name, pedData?.Name);
                if (pedData != null) {
                    DataController.TryRefreshPedModelFromLiveWorld(pedData, name, reversedName);
                    DataController.TryRefreshSupervisionFromLiveWorld(pedData, name, reversedName);
                    DataController.KeepPedInDatabase(pedData);
                    if (MDTProPedData.IsMinimalIdentity(pedData)) {
                        Utility.Helper.Log($"[MDTPro] Person Search returning minimal-identity ped (will show N/A): {pedData.Name}", false, Utility.Helper.LogSeverity.Info);
                    }
                }

                if (pedData != null) {
                    var cases = DataController.GetCourtCasesForPedName(pedData.Name);
                    var jo = JObject.Parse(JsonConvert.SerializeObject(pedData));
                    jo["CourtCases"] = JArray.FromObject(cases ?? new System.Collections.Generic.List<CourtData>());
                    buffer = Encoding.UTF8.GetBytes(jo.ToString(Formatting.None));
                } else {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(pedData));
                }
                contentType = "text/json";
                status = 200;
            } else if (path == "contextPed") {
                MDTProPedData pedData = DataController.GetContextPedIfValid();
                if (pedData != null) {
                    string ctxName = pedData.Name?.Trim() ?? "";
                    string ctxReversed = string.Join(" ", ctxName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Reverse());
                    DataController.TryRefreshSupervisionFromLiveWorld(pedData, ctxName, ctxReversed);
                    var ctxCases = DataController.GetCourtCasesForPedName(pedData.Name);
                    var ctxJo = JObject.Parse(JsonConvert.SerializeObject(pedData));
                    ctxJo["CourtCases"] = JArray.FromObject(ctxCases ?? new System.Collections.Generic.List<CourtData>());
                    buffer = Encoding.UTF8.GetBytes(ctxJo.ToString(Formatting.None));
                } else {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(pedData));
                }
                contentType = "text/json";
                status = 200;
            } else if (path == "specificVehicle") {
                string licensePlateOrVin = Helper.GetRequestBodyAsString(req);
                if (!string.IsNullOrEmpty(licensePlateOrVin)) licensePlateOrVin = licensePlateOrVin.Trim();

                MDTProVehicleData vehicleData = null;
                bool wantContextOnly = string.Equals(licensePlateOrVin, "context", StringComparison.OrdinalIgnoreCase)
                    || licensePlateOrVin == "%context"
                    || string.Equals(licensePlateOrVin, "current", StringComparison.OrdinalIgnoreCase);
                if (wantContextOnly) {
                    vehicleData = DataController.GetContextVehicleIfValid();
                } else if (!string.IsNullOrEmpty(licensePlateOrVin)) {
                    var contextVeh = DataController.GetContextVehicleIfValid();
                    if (contextVeh != null) {
                        string key = DataController.NormalizeVehiclePlateKey(licensePlateOrVin);
                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(contextVeh.LicensePlate)
                            && DataController.NormalizeVehiclePlateKey(contextVeh.LicensePlate) == key)
                            vehicleData = contextVeh;
                        else if (!string.IsNullOrEmpty(contextVeh.VehicleIdentificationNumber)
                            && string.Equals(contextVeh.VehicleIdentificationNumber.Trim(), licensePlateOrVin, StringComparison.OrdinalIgnoreCase))
                            vehicleData = contextVeh;
                    }
                }
                if (vehicleData == null && !string.IsNullOrEmpty(licensePlateOrVin) && !wantContextOnly)
                    vehicleData = DataController.GetVehicleByPlateOrVin(licensePlateOrVin);
                if (vehicleData == null && !string.IsNullOrEmpty(licensePlateOrVin) && !wantContextOnly)
                    vehicleData = DataController.TryResolveVehicleFromLiveWorldByPlateOrVinBlocking(licensePlateOrVin);

                if (vehicleData != null && vehicleData.CDFVehicleData != null)
                    DataController.MergeBOLOsFromStubByPlate(vehicleData);
                if (vehicleData != null && ModIntegration.SubscribedStopThePedStopEvents)
                    DataController.TryRefreshVehicleDocumentsFromLiveWorld(vehicleData);
                if (vehicleData != null)
                    DataController.TrySyncVehicleOwnerWantedFromCdf(vehicleData);

                string historyQuery = wantContextOnly ? (vehicleData?.LicensePlate ?? "context") : licensePlateOrVin;
                Database.SaveSearchHistoryEntry("vehicle", historyQuery, vehicleData?.LicensePlate);

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(vehicleData));
                contentType = "text/json";
                status = 200;
            } else if (path == "contextVehicle") {
                MDTProVehicleData ctxV = DataController.GetContextVehicleIfValid();
                if (ctxV != null && ctxV.CDFVehicleData != null)
                    DataController.MergeBOLOsFromStubByPlate(ctxV);
                if (ctxV != null && ModIntegration.SubscribedStopThePedStopEvents)
                    DataController.TryRefreshVehicleDocumentsFromLiveWorld(ctxV);
                if (ctxV != null)
                    DataController.TrySyncVehicleOwnerWantedFromCdf(ctxV);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ctxV));
                contentType = "text/json";
                status = 200;
            } else if (path == "activeBolos") {
                var bolos = DataController.GetActiveBOLOs();
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(bolos));
                contentType = "text/json";
                status = 200;
            } else if (path == "officerInformation") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.OfficerInformation));
                contentType = "text/json";
                status = 200;
            } else if (path == "officerInformationData") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.OfficerInformationData));
                status = 200;
                contentType = "text/json";
            } else if (path == "court") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.courtDatabase));
                status = 200;
                contentType = "text/json";
            } else if (path == "currentShift") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.CurrentShiftData));
                status = 200;
                contentType = "text/json";
            } else if (path == "shiftHistory") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.shiftHistoryData));
                status = 200;
                contentType = "text/json";
            } else if (path == "incidentReports") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.incidentReports));
                status = 200;
                contentType = "text/json";
            } else if (path == "citationReports") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.citationReports));
                status = 200;
                contentType = "text/json";
            } else if (path == "arrestReports") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.arrestReports));
                status = 200;
                contentType = "text/json";
            } else if (path == "impoundReports") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.impoundReports));
                status = 200;
                contentType = "text/json";
            } else if (path == "trafficIncidentReports") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.trafficIncidentReports));
                status = 200;
                contentType = "text/json";
            } else if (path == "injuryReports") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.injuryReports));
                status = 200;
                contentType = "text/json";
            } else if (path == "propertyEvidenceReports" || path == "propertyEvidenceReceiptReports") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.propertyEvidenceReports));
                status = 200;
                contentType = "text/json";
            } else if (path == "playerLocation") {
                DataController.RefreshMdtLocationOnGameFiberBlocking(1500);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.MdtPreferredLocation));
                status = 200;
                contentType = "text/json";
            } else if (path == "currentTime") {
                buffer = Encoding.UTF8.GetBytes(DataController.CurrentTime);
                status = 200;
                contentType = "text/plain";
            } else if (path == "searchHistory") {
                string body = Helper.GetRequestPostData(req);
                string type = !string.IsNullOrEmpty(body) ? body : "ped";
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Database.LoadSearchHistory(type)));
                status = 200;
                contentType = "text/json";
            } else if (path == "pedReports") {
                string pedName = Helper.GetRequestBodyAsString(req);
                if (!string.IsNullOrEmpty(pedName)) pedName = pedName.ToLower();

                var result = new {
                    citations = DataController.citationReports
                        .Where(r => r.OffenderPedName?.ToLower() == pedName)
                        .Select(r => new { r.Id, r.TimeStamp, r.Status }),
                    arrests = DataController.arrestReports
                        .Where(r => r.OffenderPedName?.ToLower() == pedName)
                        .Select(r => new { r.Id, r.TimeStamp, r.Status }),
                    incidents = DataController.incidentReports
                        .Where(r => (r.OffenderPedsNames != null && r.OffenderPedsNames.Any(n => n.ToLower() == pedName))
                                 || (r.WitnessPedsNames != null && r.WitnessPedsNames.Any(n => n.ToLower() == pedName)))
                        .Select(r => new { r.Id, r.TimeStamp, r.Status }),
                    propertyEvidence = DataController.PropertyEvidenceReports
                        ?.Where(r => r.SubjectPedNames != null && r.SubjectPedNames.Any(n => (n ?? "").ToLower() == pedName))
                        .Select(r => new { r.Id, r.TimeStamp, r.Status }),
                    injuries = (DataController.InjuryReports ?? Enumerable.Empty<InjuryReport>())
                        .Where(r => r.InjuredPartyName != null && r.InjuredPartyName.ToLower() == pedName)
                        .Select(r => new { r.Id, r.TimeStamp, r.Status }),
                    impounds = (DataController.ImpoundReports ?? Enumerable.Empty<ImpoundReport>())
                        .Where(r => (r.PersonAtFaultName != null && r.PersonAtFaultName.ToLower() == pedName)
                                 || (r.Owner != null && r.Owner.ToLower() == pedName))
                        .Select(r => new { r.Id, r.TimeStamp, r.Status })
                };

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));
                status = 200;
                contentType = "text/json";
            } else if (path == "recentReports") {
                string body = Helper.GetRequestPostData(req);
                int withinMinutes = 60;
                string pedName = null;
                if (!string.IsNullOrEmpty(body)) {
                    try {
                        var parsed = JsonConvert.DeserializeAnonymousType(body, new { withinMinutes = 60, pedName = (string)null });
                        if (parsed != null) {
                            if (parsed.withinMinutes > 0 && parsed.withinMinutes <= 120)
                                withinMinutes = parsed.withinMinutes;
                            pedName = !string.IsNullOrEmpty(parsed.pedName) ? parsed.pedName.Trim().ToLowerInvariant() : null;
                        }
                    } catch { }
                }
                var cutoff = DateTime.UtcNow.AddMinutes(-withinMinutes);
                var list = new System.Collections.Generic.List<(string id, string type, DateTime timeStamp)>();
                bool MatchesPed(string name) => !string.IsNullOrEmpty(pedName) && !string.IsNullOrEmpty(name) && name.Trim().ToLowerInvariant() == pedName;
                bool MatchesPedInArray(string[] arr) => arr != null && arr.Any(n => MatchesPed(n ?? ""));
                void AddIfRecent(System.Collections.IEnumerable reports, string type, Func<Report, bool> matchesPed = null) {
                    if (reports == null) return;
                    foreach (Report r in reports) {
                        if (string.IsNullOrEmpty(r.Id)) continue;
                        var createdAt = DataController.GetReportRealCreatedAt(r.Id);
                        var useForFilter = createdAt ?? r.TimeStamp.ToUniversalTime();
                        if (useForFilter >= cutoff && (string.IsNullOrEmpty(pedName) || (matchesPed != null && matchesPed(r))))
                            list.Add((r.Id, type, r.TimeStamp));
                    }
                }
                AddIfRecent(DataController.IncidentReports, "incident", r => MatchesPedInArray((r as IncidentReport)?.OffenderPedsNames) || MatchesPedInArray((r as IncidentReport)?.WitnessPedsNames));
                AddIfRecent(DataController.InjuryReports, "injury", r => MatchesPed((r as InjuryReport)?.InjuredPartyName));
                AddIfRecent(DataController.CitationReports, "citation", r => MatchesPed((r as CitationReport)?.OffenderPedName));
                AddIfRecent(DataController.TrafficIncidentReports, "trafficIncident", r => MatchesPedInArray((r as TrafficIncidentReport)?.DriverNames) || MatchesPedInArray((r as TrafficIncidentReport)?.PassengerNames) || MatchesPedInArray((r as TrafficIncidentReport)?.PedestrianNames));
                AddIfRecent(DataController.ImpoundReports, "impound", r => MatchesPed((r as ImpoundReport)?.PersonAtFaultName) || MatchesPed((r as ImpoundReport)?.Owner));
                AddIfRecent(DataController.PropertyEvidenceReports, "propertyEvidence", r => {
                    var per = r as PropertyEvidenceReceiptReport;
                    return MatchesPed(per?.SubjectPedName) || (per?.SubjectPedNames != null && per.SubjectPedNames.Any(n => MatchesPed(n ?? "")));
                });
                var sorted = list.OrderByDescending(x => x.timeStamp)
                    .Select(x => new { id = x.id, type = x.type, timeStamp = x.timeStamp }).ToList();
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(sorted));
                status = 200;
                contentType = "text/json";
            } else if (path == "pedVehicles") {
                string pedName = Helper.GetRequestBodyAsString(req);

                var vehicles = DataController.VehicleDatabase
                    .Where(v => v.Owner != null && v.Owner.ToLower() == pedName.ToLower())
                    .Select(v => new { v.LicensePlate, v.ModelDisplayName, v.IsStolen, v.Color });

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(vehicles));
                status = 200;
                contentType = "text/json";
            } else if (path == "officerMetrics") {
                var shifts = DataController.shiftHistoryData;
                int totalShifts = shifts.Count;

                double avgShiftMs = 0;
                if (totalShifts > 0) {
                    var completedShifts = shifts.Where(s => s.startTime.HasValue && s.endTime.HasValue).ToList();
                    if (completedShifts.Count > 0) {
                        avgShiftMs = completedShifts.Average(s => (s.endTime.Value - s.startTime.Value).TotalMilliseconds);
                    }
                }

                int totalIncident = DataController.incidentReports.Count;
                int totalCitation = DataController.citationReports.Count;
                int totalArrest = DataController.arrestReports.Count;
                int totalReports = totalIncident + totalCitation + totalArrest;

                var metrics = new {
                    totalShifts,
                    averageShiftDurationMs = avgShiftMs,
                    totalIncidentReports = totalIncident,
                    totalCitationReports = totalCitation,
                    totalArrestReports = totalArrest,
                    totalReports,
                    reportsPerShift = totalShifts > 0 ? Math.Round((double)totalReports / totalShifts, 1) : 0
                };

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metrics));
                status = 200;
                contentType = "text/json";
            } else if (path == "repeatOffenders") {
                var offenders = DataController.PedDatabase
                    .Where(p => (p.Citations != null ? p.Citations.Count : 0) + (p.Arrests != null ? p.Arrests.Count : 0) > 1)
                    .OrderByDescending(p => (p.Citations != null ? p.Citations.Count : 0) + (p.Arrests != null ? p.Arrests.Count : 0))
                    .Take(20)
                    .Select(p => new {
                        p.Name,
                        p.TimesStopped,
                        CitationCount = p.Citations != null ? p.Citations.Count : 0,
                        ArrestCount = p.Arrests != null ? p.Arrests.Count : 0,
                        p.IsWanted,
                        p.IsOnProbation,
                        p.IsOnParole
                    });

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(offenders));
                status = 200;
                contentType = "text/json";
            } else if (path == "activePostalCodeSet") {
                buffer = Encoding.UTF8.GetBytes(DataController.ActivePostalCodeSet ?? "null");
                status = 200;
                contentType = "text/plain";
            } else if (path == "recentIds") {
                var recentIds = DataController.PedDatabase
                    .Where(p => p.IdentificationHistory != null && p.IdentificationHistory.Count > 0)
                    .Select(p => new { p.Name, Latest = p.IdentificationHistory[0], p.IsDeceased })
                    .OrderByDescending(x => x.Latest.Timestamp)
                    .Take(8)
                    .Select(x => new { x.Name, x.Latest.Type, x.Latest.Timestamp, x.IsDeceased });
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(recentIds));
                status = 200;
                contentType = "text/json";
            } else if (path == "firearmsForPed") {
                string pedName = Helper.GetRequestBodyAsString(req);
                var firearms = Database.LoadFirearmsByOwner(pedName);
                firearms = DataController.FilterToActualFirearms(firearms ?? new System.Collections.Generic.List<FirearmRecord>());
                if (firearms != null && firearms.Count > 0)
                    Database.TouchFirearmRecordsByOwner(pedName);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(firearms));
                status = 200;
                contentType = "text/json";
            } else if (path == "firearmBySerial") {
                string serial = Helper.GetRequestBodyAsString(req);
                var firearm = Database.LoadFirearmBySerial(serial);
                if (firearm != null && DataController.FilterToActualFirearms(new System.Collections.Generic.List<FirearmRecord> { firearm }).Count == 0)
                    firearm = null; // Exclude melee/knives from serial lookup
                if (firearm != null && !string.IsNullOrWhiteSpace(firearm.OwnerPedName))
                    Database.TouchFirearmRecordsByOwner(firearm.OwnerPedName);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(firearm ?? new object()));
                status = 200;
                contentType = "text/json";
            } else if (path == "recentFirearmOwners") {
                var owners = Database.LoadRecentFirearmOwnerNames(12);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(owners));
                status = 200;
                contentType = "text/json";
            } else if (path == "recentFirearms") {
                var firearms = Database.LoadRecentFirearms(30);
                firearms = DataController.FilterToActualFirearms(firearms ?? new System.Collections.Generic.List<FirearmRecord>(), maxCount: 12);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(firearms));
                status = 200;
                contentType = "text/json";
            } else if (path == "drugsByOwner") {
                string pedName = Helper.GetRequestBodyAsString(req);
                var drugs = string.IsNullOrWhiteSpace(pedName) ? new System.Collections.Generic.List<DrugRecord>() : Database.LoadDrugsByOwner(pedName);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(drugs ?? new System.Collections.Generic.List<DrugRecord>()));
                status = 200;
                contentType = "text/json";
            } else if (path == "vehicleSearchByPlate") {
                string plate = Helper.GetRequestBodyAsString(req)?.Trim() ?? "";
                var records = string.IsNullOrWhiteSpace(plate)
                    ? new System.Collections.Generic.List<VehicleSearchRecord>()
                    : Database.LoadVehicleSearchRecordsByPlate(plate, limit: 12);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(records ?? new System.Collections.Generic.List<VehicleSearchRecord>()));
                status = 200;
                contentType = "text/json";
            } else if (path == "impoundReportsByPlate") {
                string plate = Helper.GetRequestBodyAsString(req);
                plate = plate?.Trim();
                var source = DataController.impoundReports ?? new System.Collections.Generic.List<ImpoundReport>();
                System.Collections.Generic.IEnumerable<ImpoundReport> reports;

                if (string.IsNullOrWhiteSpace(plate)) {
                    reports = new System.Collections.Generic.List<ImpoundReport>();
                } else {
                    string plateLower = plate.ToLower();
                    reports = source
                        .Where(r => r != null && !string.IsNullOrEmpty(r.LicensePlate) && r.LicensePlate.Trim().ToLower() == plateLower)
                        .OrderByDescending(r => r.TimeStamp);
                }

                var result = reports.Select(r => new {
                    r.Id,
                    r.TimeStamp,
                    r.Status,
                    r.LicensePlate,
                    r.VehicleModel,
                    r.Owner,
                    r.ImpoundReason,
                    r.TowCompany,
                    r.ImpoundLot
                });

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));
                status = 200;
                contentType = "text/json";
            } else if (path == "reportSummaries") {
                string body = Helper.GetRequestPostData(req);
                var ids = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(body)) {
                    try {
                        var parsed = JsonConvert.DeserializeObject<System.Collections.Generic.List<string>>(body);
                        if (parsed != null) ids = parsed;
                    } catch { }
                }
                var summaries = new System.Collections.Generic.List<object>();
                foreach (string id in ids) {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    string tid = id.Trim();
                    var inc = DataController.IncidentReports?.FirstOrDefault(r => r.Id == tid);
                    if (inc != null) {
                        string sub = (inc.OffenderPedsNames != null && inc.OffenderPedsNames.Length > 0) ? string.Join(", ", inc.OffenderPedsNames) : null;
                        summaries.Add(new { id = inc.Id, type = "incident", typeLabel = "Incident", date = inc.TimeStamp.ToString("yyyy-MM-dd"), subtitle = sub });
                        continue;
                    }
                    var inj = DataController.InjuryReports?.FirstOrDefault(r => r.Id == tid);
                    if (inj != null) {
                        summaries.Add(new { id = inj.Id, type = "injury", typeLabel = "Injury", date = inj.TimeStamp.ToString("yyyy-MM-dd"), subtitle = inj.InjuredPartyName });
                        continue;
                    }
                    var cit = DataController.CitationReports?.FirstOrDefault(r => r.Id == tid);
                    if (cit != null) {
                        summaries.Add(new { id = cit.Id, type = "citation", typeLabel = "Citation", date = cit.TimeStamp.ToString("yyyy-MM-dd"), subtitle = cit.OffenderPedName });
                        continue;
                    }
                    var tra = DataController.TrafficIncidentReports?.FirstOrDefault(r => r.Id == tid);
                    if (tra != null) {
                        summaries.Add(new { id = tra.Id, type = "trafficIncident", typeLabel = "Traffic Incident", date = tra.TimeStamp.ToString("yyyy-MM-dd"), subtitle = tra.CollisionType });
                        continue;
                    }
                    var imp = DataController.ImpoundReports?.FirstOrDefault(r => r.Id == tid);
                    if (imp != null) {
                        string sub = !string.IsNullOrEmpty(imp.LicensePlate) ? imp.LicensePlate : imp.VehicleModel;
                        summaries.Add(new { id = imp.Id, type = "impound", typeLabel = "Impound", date = imp.TimeStamp.ToString("yyyy-MM-dd"), subtitle = sub });
                        continue;
                    }
                    var per = DataController.PropertyEvidenceReports?.FirstOrDefault(r => r.Id == tid);
                    if (per != null) {
                        string sub = (per.SubjectPedNames != null && per.SubjectPedNames.Count > 0) ? string.Join(", ", per.SubjectPedNames.Where(s => !string.IsNullOrEmpty(s))) : (per.SeizedDrugTypes?.Count > 0 || per.SeizedFirearmTypes?.Count > 0 ? "Contraband seized" : null);
                        var items = new System.Collections.Generic.List<string>();
                        if (per.SeizedDrugs != null && per.SeizedDrugs.Count > 0) {
                            foreach (var d in per.SeizedDrugs) {
                                if (d == null || string.IsNullOrEmpty(d.DrugType)) continue;
                                items.Add(string.IsNullOrEmpty(d.Quantity) ? d.DrugType : $"{d.DrugType} ({d.Quantity})");
                            }
                        } else if (per.SeizedDrugTypes != null && per.SeizedDrugTypes.Count > 0) {
                            foreach (var t in per.SeizedDrugTypes) { if (!string.IsNullOrEmpty(t)) items.Add(t); }
                        }
                        if (per.SeizedFirearmTypes != null) foreach (var f in per.SeizedFirearmTypes) { if (!string.IsNullOrEmpty(f)) items.Add(f); }
                        if (!string.IsNullOrWhiteSpace(per.OtherContrabandNotes)) items.Add(per.OtherContrabandNotes);
                        summaries.Add(new { id = per.Id, type = "propertyEvidence", typeLabel = "Property & Evidence", date = per.TimeStamp.ToString("yyyy-MM-dd"), subtitle = sub, items = items });
                        continue;
                    }
                    summaries.Add(new { id = tid, type = (string)null, typeLabel = "—", date = (string)null, subtitle = (string)null });
                }
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(summaries));
                status = 200;
                contentType = "text/json";
            }
        }
    }
}
