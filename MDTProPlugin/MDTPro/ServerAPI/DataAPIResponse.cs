using MDTPro.Data;
using MDTPro.Data.Reports;
using MDTPro.Utility;
using Newtonsoft.Json;
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
                    pedData = DataController.PedDatabase.FirstOrDefault(o => o.Name?.ToLower() == name.ToLower() || o.Name?.ToLower() == reversedName.ToLower());
                }
                if (pedData == null && (name == "context" || name == "%context" || name.Equals("current", StringComparison.OrdinalIgnoreCase))) {
                    pedData = DataController.GetContextPedIfValid();
                }

                Database.SaveSearchHistoryEntry("ped", name, pedData?.Name);
                if (pedData != null) {
                    DataController.KeepPedInDatabase(pedData);
                    if (MDTProPedData.IsMinimalIdentity(pedData)) {
                        Utility.Helper.Log($"[MDTPro] Person Search returning minimal-identity ped (will show N/A): {pedData.Name}", false, Utility.Helper.LogSeverity.Info);
                    }
                }

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(pedData));
                contentType = "text/json";
                status = 200;
            } else if (path == "contextPed") {
                MDTProPedData pedData = DataController.GetContextPedIfValid();
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(pedData));
                contentType = "text/json";
                status = 200;
            } else if (path == "specificVehicle") {
                string licensePlateOrVin = Helper.GetRequestBodyAsString(req);
                if (!string.IsNullOrEmpty(licensePlateOrVin)) licensePlateOrVin = licensePlateOrVin.Trim();

                MDTProVehicleData vehicleData = DataController.GetVehicleByPlateOrVin(licensePlateOrVin);
                if (vehicleData != null && vehicleData.CDFVehicleData != null)
                    DataController.MergeBOLOsFromStubByPlate(vehicleData);

                Database.SaveSearchHistoryEntry("vehicle", licensePlateOrVin, vehicleData?.LicensePlate);

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(vehicleData));
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
            } else if (path == "playerLocation") {
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataController.PlayerLocation));
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
                        .Select(r => new { r.Id, r.TimeStamp, r.Status })
                };

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));
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
                if (firearms != null && firearms.Count > 0)
                    Database.TouchFirearmRecordsByOwner(pedName);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(firearms));
                status = 200;
                contentType = "text/json";
            } else if (path == "firearmBySerial") {
                string serial = Helper.GetRequestBodyAsString(req);
                var firearm = Database.LoadFirearmBySerial(serial);
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
                var firearms = Database.LoadRecentFirearms(12);
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
                    summaries.Add(new { id = tid, type = (string)null, typeLabel = "—", date = (string)null, subtitle = (string)null });
                }
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(summaries));
                status = 200;
                contentType = "text/json";
            }
        }
    }
}
