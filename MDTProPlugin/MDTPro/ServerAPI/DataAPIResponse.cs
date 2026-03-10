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

                bool hasPlayer = Main.Player != null && Main.Player.Exists();

                var nearbyVehicles = DataController.VehicleDatabase
                    .Where(vehicleData => !string.IsNullOrEmpty(vehicleData.LicensePlate))
                    .Select(vehicleData => new {
                        vehicleData,
                        distance = hasPlayer && vehicleData.Holder != null && vehicleData.Holder.Exists()
                            ? Main.Player.DistanceTo(vehicleData.Holder)
                            : float.MaxValue
                    })
                    .OrderBy(x => x.distance)
                    .ThenBy(x => x.vehicleData.LicensePlate)
                    .Take(limit)
                    .Select(x => new {
                        x.vehicleData.LicensePlate,
                        x.vehicleData.ModelDisplayName,
                        Distance = x.distance == float.MaxValue ? (float?)null : (float?)Math.Round(x.distance, 1),
                        x.vehicleData.IsStolen
                    });

                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(nearbyVehicles));
                status = 200;
                contentType = "text/json";
            } else if (path == "specificPed") {
                string body = Helper.GetRequestPostData(req);
                string name = !string.IsNullOrEmpty(body) ? body.Trim() : "";
                string reversedName = string.Join(" ", name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Reverse());

                MDTProPedData pedData = DataController.PedDatabase.FirstOrDefault(o => o.Name?.ToLower() == name.ToLower() || o.Name?.ToLower() == reversedName.ToLower());
                if (pedData == null && (name == "context" || name == "%context" || name.Equals("current", StringComparison.OrdinalIgnoreCase))) {
                    pedData = DataController.GetContextPedIfValid();
                }

                Database.SaveSearchHistoryEntry("ped", name, pedData?.Name);
                if (pedData != null) {
                    DataController.KeepPedInDatabase(pedData);
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
                string body = Helper.GetRequestPostData(req);
                string licensePlateOrVin = !string.IsNullOrEmpty(body) ? body.Trim() : "";

                MDTProVehicleData vehicleData = DataController.GetVehicleByPlateOrVin(licensePlateOrVin);

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
            } else if (path == "injuryGameData") {
                string pedName = null;
                if (!string.IsNullOrEmpty(req.Url.Query)) {
                    NameValueCollection q = HttpUtility.ParseQueryString(req.Url.Query);
                    pedName = q["pedName"]?.Trim();
                }
                if (string.IsNullOrEmpty(pedName)) {
                    var contextData = InjuryDataService.GetInjuryGameDataForContext();
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(contextData ?? (object)new { }));
                } else {
                    var gameData = InjuryDataService.GetInjuryGameDataForPed(pedName);
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(gameData ?? (object)new { }));
                }
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
            } else if (path == "drugsByOwner") {
                string pedName = Helper.GetRequestBodyAsString(req);
                var drugs = string.IsNullOrWhiteSpace(pedName) ? new System.Collections.Generic.List<DrugRecord>() : Database.LoadDrugsByOwner(pedName);
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(drugs ?? new System.Collections.Generic.List<DrugRecord>()));
                status = 200;
                contentType = "text/json";
            } else if (path == "vehicleSearchByPlate") {
                string plate = Helper.GetRequestBodyAsString(req);
                var records = string.IsNullOrWhiteSpace(plate)
                    ? new System.Collections.Generic.List<VehicleSearchRecord>()
                    : Database.LoadVehicleSearchRecordsByPlate(plate);
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
            }
        }
    }
}
