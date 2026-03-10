using MDTPro.Data;
using MDTPro.Data.Reports;
using System;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using System.Net;
using System.Text;

namespace MDTPro.ServerAPI {
    internal class PostAPIResponse : APIResponse {
        internal PostAPIResponse(HttpListenerRequest req) : base(null) {
            string path = req.Url.AbsolutePath.Substring("/post/".Length);
            if (string.IsNullOrEmpty(path)) return;

            if (path == "alprClear") {
                ALPR.ALPRController.Clear();
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
                return;
            }

            if (path == "setGpsWaypoint") {
                float x = 0, y = 0;
                bool explicitCoords = false;
                string bodySetGps = Helper.GetRequestPostData(req);
                if (!string.IsNullOrEmpty(bodySetGps)) {
                    try {
                        var data = JsonConvert.DeserializeAnonymousType(bodySetGps, new { x = 0f, y = 0f });
                        if (data != null) { x = data.x; y = data.y; explicitCoords = true; }
                    } catch { }
                }
                if (!explicitCoords && EventListeners.CalloutEvents.CalloutInfo != null) {
                    var ci = EventListeners.CalloutEvents.CalloutInfo;
                    if (ci.Coords != null && ci.Coords.Length >= 2) {
                        x = ci.Coords[0];
                        y = ci.Coords[1];
                    }
                }
                if (explicitCoords || x != 0 || y != 0) {
                    Utility.GpsHelper.SetWaypoint(x, y);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } else {
                    buffer = Encoding.UTF8.GetBytes("No coordinates available. Accept a callout first or provide x,y in request body.");
                    contentType = "text/plain";
                    status = 400;
                }
                return;
            }

            string body = Helper.GetRequestPostData(req);
            if (string.IsNullOrEmpty(body)) {
                buffer = Encoding.UTF8.GetBytes("Bad Request - Empty Body");
                contentType = "text/plain";
                status = 400;
                return;
            } else if (path == "updatePedData") {
                MDTProPedData pedData = JsonConvert.DeserializeObject<MDTProPedData>(body);
                if (pedData == null) {
                    buffer = Encoding.UTF8.GetBytes("Bad Request - Invalid ped data");
                    contentType = "text/plain";
                    status = 400;
                    return;
                }
                if (!DataController.UpdatePedData(pedData)) {
                    buffer = Encoding.UTF8.GetBytes("Ped not found or not in world - update requires ped to be nearby");
                    contentType = "text/plain";
                    status = 404;
                    return;
                }
                DataController.SyncPedDatabaseWithCDF();
                Database.SavePed(pedData);
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "updateVehicleData") {
                MDTProVehicleData vehicleData = JsonConvert.DeserializeObject<MDTProVehicleData>(body);
                if (vehicleData == null) {
                    buffer = Encoding.UTF8.GetBytes("Bad Request - Invalid vehicle data");
                    contentType = "text/plain";
                    status = 400;
                    return;
                }
                if (!DataController.UpdateVehicleData(vehicleData)) {
                    buffer = Encoding.UTF8.GetBytes("Vehicle not found or not in world - update requires vehicle to be nearby");
                    contentType = "text/plain";
                    status = 404;
                    return;
                }
                DataController.SyncVehicleDatabaseWithCDF();
                Database.SaveVehicle(vehicleData);
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "updateOfficerInformationData") {
                DataController.OfficerInformationData = JsonConvert.DeserializeObject<OfficerInformationData>(body);

                Database.SaveOfficerInformation(DataController.OfficerInformationData);

                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "modifyCurrentShift") {
                if (body == "start") {
                    DataController.StartCurrentShift();
                } else if (body == "end") {
                    DataController.EndCurrentShift();
                } else {
                    buffer = Encoding.UTF8.GetBytes("Bad Request - Invalid Action");
                    contentType = "text/plain";
                    status = 400;
                    return;
                }

                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "createIncidentReport") {
                IncidentReport report = JsonConvert.DeserializeObject<IncidentReport>(body);

                DataController.AddReport(report);

                Database.SaveIncidentReport(report);

                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "createCitationReport") {
                CitationReport report = JsonConvert.DeserializeObject<CitationReport>(body);

                DataController.AddReport(report);

                Database.SaveCitationReport(report);

                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "createArrestReport") {
                ArrestReport report = JsonConvert.DeserializeObject<ArrestReport>(body);

                DataController.AddReport(report);

                Database.SaveArrestReport(report);

                CourtData courtCase = DataController.courtDatabase.Find(x => x.Number == report.CourtCaseNumber);
                if (courtCase != null) Database.SaveCourtCase(courtCase);

                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "updateCourtCaseStatus") {
                var data = JsonConvert.DeserializeAnonymousType(body, new {
                    Number = "",
                    Status = 0,
                    Plea = "",
                    IsJuryTrial = (bool?)null,
                    JurySize = (int?)null,
                    JuryVotesForConviction = (int?)null,
                    JuryVotesForAcquittal = (int?)null,
                    HasPublicDefender = (bool?)null,
                    OutcomeNotes = "",
                    OutcomeReasoning = ""
                });

                if (data == null || string.IsNullOrWhiteSpace(data.Number)) {
                    buffer = Encoding.UTF8.GetBytes("Bad Request");
                    contentType = "text/plain";
                    status = 400;
                    return;
                }

                if (DataController.UpdateCourtCaseOutcome(
                    data.Number,
                    data.Status,
                    data.Plea,
                    data.IsJuryTrial,
                    data.JurySize,
                    data.JuryVotesForConviction,
                    data.JuryVotesForAcquittal,
                    data.HasPublicDefender,
                    data.OutcomeNotes,
                    data.OutcomeReasoning)) {

                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } else {
                    buffer = Encoding.UTF8.GetBytes("Not Found");
                    contentType = "text/plain";
                    status = 404;
                }
            } else if (path == "forceResolveCourtCase") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { Number = "", Plea = "", OutcomeNotes = "" });
                if (data == null || string.IsNullOrWhiteSpace(data.Number)) {
                    buffer = Encoding.UTF8.GetBytes("Bad Request");
                    contentType = "text/plain";
                    status = 400;
                    return;
                }
                if (DataController.ForceResolveCourtCase(data.Number, data.Plea, data.OutcomeNotes)) {
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } else {
                    buffer = Encoding.UTF8.GetBytes("Not Found");
                    contentType = "text/plain";
                    status = 404;
                }
            } else if (path == "clearSearchHistory") {
                string searchType = string.IsNullOrWhiteSpace(body) ? "ped" : body.Trim();
                Database.ClearSearchHistory(searchType);
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "updateConfig") {
                // Merge body onto current config so keys not sent by the UI (e.g. alprSettingsVersion) are preserved.
                Config config = SetupController.GetConfig();
                JsonConvert.PopulateObject(body, config);

                Helper.WriteToJsonFile(SetupController.ConfigPath, config);

                SetupController.ResetConfig();

                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "addBOLO") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { LicensePlate = "", Reason = "", ExpiresAt = default(DateTime), IssuedBy = "LSPD" });
                if (data == null || string.IsNullOrWhiteSpace(data.LicensePlate) || string.IsNullOrWhiteSpace(data.Reason)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "License plate and reason are required." }));
                    contentType = "text/json";
                    status = 400;
                    return;
                }
                var expires = data.ExpiresAt != default(DateTime) ? data.ExpiresAt : System.DateTime.UtcNow.AddDays(7);
                if (DataController.TryAddBOLOToVehicle(data.LicensePlate.Trim(), data.Reason.Trim(), expires, data.IssuedBy ?? "LSPD")) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = true }));
                    contentType = "text/json";
                    status = 200;
                } else {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Vehicle not found or not in world. The vehicle must be nearby to add a BOLO." }));
                    contentType = "text/json";
                    status = 400;
                }
            } else if (path == "requestBackup") {
                var reqData = JsonConvert.DeserializeAnonymousType(body, new { action = (string)null, unit = (string)null, responseCode = 2 });
                if (reqData == null || string.IsNullOrWhiteSpace(reqData.action)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "action is required. Supported: panic, localPatrol, statePatrol, localSwat, nooseSwat, localK9, stateK9, ambulance, fire, coroner, animalControl, trafficStop, transport, tow, group, airLocal, airNoose, spikeStrips, felonyStop, dismiss" }));
                    contentType = "text/json";
                    status = 400;
                    return;
                }
                string act = reqData.action.Trim().ToLowerInvariant();
                int rc = reqData.responseCode >= 1 && reqData.responseCode <= 4 ? reqData.responseCode : 2;
                bool ok = false;
                string err = null;
                switch (act) {
                    case "panic":
                        ok = Utility.BackupHelper.RequestPanicBackup();
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "localpatrol":
                        ok = Utility.BackupHelper.RequestBackup("LocalPatrol", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "statepatrol":
                        ok = Utility.BackupHelper.RequestBackup("StatePatrol", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "localswat":
                        ok = Utility.BackupHelper.RequestBackup("LocalSWAT", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "nooseswat":
                        ok = Utility.BackupHelper.RequestBackup("NooseSWAT", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "localk9":
                        ok = Utility.BackupHelper.RequestBackup("LocalK9Patrol", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "statek9":
                        ok = Utility.BackupHelper.RequestBackup("StateK9Patrol", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "ambulance":
                        ok = Utility.BackupHelper.RequestBackup("Ambulance", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "fire":
                        ok = Utility.BackupHelper.RequestBackup("FireDepartment", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "coroner":
                        ok = Utility.BackupHelper.RequestBackup("Coroner", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "animalcontrol":
                        ok = Utility.BackupHelper.RequestBackup("AnimalControl", rc);
                        err = ok ? null : "Policing Redefined not available or backup failed.";
                        break;
                    case "trafficstop":
                        var tsUnit = !string.IsNullOrWhiteSpace(reqData.unit) ? reqData.unit.Trim() : "LocalPatrol";
                        ok = Utility.BackupHelper.RequestTrafficStopBackup(tsUnit, rc);
                        err = ok ? null : "Policing Redefined not available, or not on a traffic stop.";
                        break;
                    case "transport":
                        ok = Utility.BackupHelper.RequestPoliceTransport(rc);
                        err = ok ? null : "Policing Redefined not available or transport failed.";
                        break;
                    case "tow":
                        ok = Utility.BackupHelper.RequestTowServiceBackup();
                        err = ok ? null : "Policing Redefined not available or tow menu failed.";
                        break;
                    case "group":
                        ok = Utility.BackupHelper.RequestGroupBackup();
                        err = ok ? null : "Policing Redefined not available or group backup failed.";
                        break;
                    case "airlocal":
                        ok = Utility.BackupHelper.RequestAirBackup("LocalAir");
                        err = ok ? null : "Policing Redefined not available or not in a pursuit.";
                        break;
                    case "airnoose":
                        ok = Utility.BackupHelper.RequestAirBackup("NooseAir");
                        err = ok ? null : "Policing Redefined not available or not in a pursuit.";
                        break;
                    case "spikestrips":
                        ok = Utility.BackupHelper.RequestSpikeStripsBackup();
                        err = ok ? null : "Policing Redefined not available or not in a pursuit.";
                        break;
                    case "felonystop":
                        ok = Utility.BackupHelper.InitiateFelonyStop();
                        err = ok ? null : "Policing Redefined not available or felony stop failed.";
                        break;
                    case "dismiss":
                        Utility.BackupHelper.DismissAllBackupUnits(false);
                        ok = true;
                        break;
                    default:
                        err = "Unknown action. Supported: panic, localPatrol, statePatrol, localSwat, nooseSwat, localK9, stateK9, ambulance, fire, coroner, animalControl, trafficStop, transport, tow, group, airLocal, airNoose, spikeStrips, felonyStop, dismiss";
                        break;
                }
                if (ok) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = true }));
                    contentType = "text/json";
                    status = 200;
                } else {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = err ?? "Backup request failed." }));
                    contentType = "text/json";
                    status = 400;
                }
            } else if (path == "removeBOLO") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { LicensePlate = "", Reason = "" });
                if (data == null || string.IsNullOrWhiteSpace(data.LicensePlate)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "License plate is required." }));
                    contentType = "text/json";
                    status = 400;
                    return;
                }
                if (DataController.TryRemoveBOLOFromVehicle(data.LicensePlate.Trim(), data.Reason ?? "")) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = true }));
                    contentType = "text/json";
                    status = 200;
                } else {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Vehicle not found, not in world, or BOLO not found." }));
                    contentType = "text/json";
                    status = 404;
                }
            }
        }
    }
}
