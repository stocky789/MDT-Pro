using MDTPro.Data;
using MDTPro.Data.Reports;
using System;
using System.Linq;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using System.Net;
using System.Text;

namespace MDTPro.ServerAPI {
    internal class PostAPIResponse : APIResponse {
        internal PostAPIResponse(HttpListenerRequest req) : base(null) {
            string rawPath = req.Url?.AbsolutePath ?? "";
            if (!rawPath.StartsWith("/post/", StringComparison.OrdinalIgnoreCase)) return;
            string path = rawPath.Substring("/post/".Length).Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(path)) return;

            if (path.Equals("alprClear", StringComparison.OrdinalIgnoreCase)) {
                Rage.GameFiber.StartNew(() => ALPR.ALPRController.Clear());
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
                return;
            }

            if (path.Equals("calloutAction", StringComparison.OrdinalIgnoreCase)) {
                string bodyCallout = Helper.GetRequestPostData(req);
                string action = null;
                if (!string.IsNullOrEmpty(bodyCallout)) {
                    try {
                        var data = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(bodyCallout, new { action = (string)null });
                        action = data?.action?.Trim().ToLowerInvariant();
                    } catch { }
                }
                if (action != "accept" && action != "enroute") {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "action must be 'accept' or 'enRoute'." }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new {
                    success = false,
                    error = "CalloutInterface and LSPDFR do not expose an API to accept callouts or set status (En Route) programmatically. Use the in-game Callout Interface to accept and respond to callouts."
                }));
                contentType = "application/json";
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
                try {
                    IncidentReport report = JsonConvert.DeserializeObject<IncidentReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    DataController.AddReport(report);
                    Database.SaveIncidentReport(report);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createIncidentReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createIncidentReport:\n{ex}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
            } else if (path == "createCitationReport") {
                try {
                    CitationReport report = JsonConvert.DeserializeObject<CitationReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    DataController.AddReport(report);
                    Database.SaveCitationReport(report);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createCitationReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createCitationReport:\n{ex}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
            } else if (path == "createArrestReport") {
                try {
                    ArrestReport report = JsonConvert.DeserializeObject<ArrestReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    if (report.AttachedReportIds == null) report.AttachedReportIds = new System.Collections.Generic.List<string>();

                    var existing = DataController.ArrestReports?.FirstOrDefault(x => x.Id == report.Id);
                    if (existing != null && !string.IsNullOrEmpty(existing.CourtCaseNumber) && report.Status == ReportStatus.Pending) {
                        buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Arrest already closed; cannot reopen." }));
                        contentType = "application/json";
                        status = 400;
                        return;
                    }

                    DataController.AddReport(report);
                    Database.SaveArrestReport(report);

                    CourtData courtCase = DataController.courtDatabase.Find(x => x.Number == report.CourtCaseNumber);
                    if (courtCase != null) Database.SaveCourtCase(courtCase);

                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createArrestReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createArrestReport:\n{ex}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
            } else if (path == "attachReportToArrest") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { arrestReportId = "", reportId = "" });
                if (data == null || string.IsNullOrWhiteSpace(data.arrestReportId) || string.IsNullOrWhiteSpace(data.reportId)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "arrestReportId and reportId required" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (data.reportId == data.arrestReportId) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Cannot attach the arrest report to itself" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (!ReportExistsAndIsAttachable(data.reportId)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Report not found or not an incident, injury, or citation report" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                var arrest = DataController.ArrestReports?.FirstOrDefault(x => x.Id == data.arrestReportId);
                bool arrestCanAttach = arrest != null && (arrest.Status == ReportStatus.Pending || arrest.Status == ReportStatus.Open);
                if (!arrestCanAttach) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Arrest not found or already closed for court" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (arrest.AttachedReportIds == null) arrest.AttachedReportIds = new System.Collections.Generic.List<string>();
                if (!arrest.AttachedReportIds.Contains(data.reportId)) arrest.AttachedReportIds.Add(data.reportId);
                Database.SaveArrestReport(arrest);
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "detachReportFromArrest") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { arrestReportId = "", reportId = "" });
                if (data == null || string.IsNullOrWhiteSpace(data.arrestReportId) || string.IsNullOrWhiteSpace(data.reportId)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "arrestReportId and reportId required" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                var arrest = DataController.ArrestReports?.FirstOrDefault(x => x.Id == data.arrestReportId);
                bool arrestCanDetach = arrest != null && (arrest.Status == ReportStatus.Pending || arrest.Status == ReportStatus.Open);
                if (!arrestCanDetach) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Arrest not found or already closed for court" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (arrest.AttachedReportIds != null) arrest.AttachedReportIds.Remove(data.reportId);
                Database.SaveArrestReport(arrest);
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "attachReportToCourtCase") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { courtCaseNumber = "", reportId = "" });
                if (data == null || string.IsNullOrWhiteSpace(data.courtCaseNumber) || string.IsNullOrWhiteSpace(data.reportId)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "courtCaseNumber and reportId required" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (!ReportExistsAndIsAttachable(data.reportId)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Report not found or not an incident, injury, or citation report" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                var courtCase = DataController.CourtDatabase?.FirstOrDefault(x => x.Number == data.courtCaseNumber);
                if (courtCase == null || courtCase.Status != 0) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Case not found or already resolved" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (!string.IsNullOrEmpty(courtCase.ResolveAtUtc) && DateTime.TryParse(courtCase.ResolveAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var resolveAt) && DateTime.UtcNow >= resolveAt) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Court date has passed" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (courtCase.AttachedReportIds == null) courtCase.AttachedReportIds = new System.Collections.Generic.List<string>();
                if (!courtCase.AttachedReportIds.Contains(data.reportId)) courtCase.AttachedReportIds.Add(data.reportId);
                Database.SaveCourtCase(courtCase);
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "detachReportFromCourtCase") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { courtCaseNumber = "", reportId = "" });
                if (data == null || string.IsNullOrWhiteSpace(data.courtCaseNumber) || string.IsNullOrWhiteSpace(data.reportId)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "courtCaseNumber and reportId required" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                var courtCase = DataController.CourtDatabase?.FirstOrDefault(x => x.Number == data.courtCaseNumber);
                if (courtCase == null || courtCase.Status != 0) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Case not found or already resolved" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (!string.IsNullOrEmpty(courtCase.ResolveAtUtc) && DateTime.TryParse(courtCase.ResolveAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var resolveAt2) && DateTime.UtcNow >= resolveAt2) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Court date has passed" }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                if (courtCase.AttachedReportIds != null) courtCase.AttachedReportIds.Remove(data.reportId);
                Database.SaveCourtCase(courtCase);
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "createImpoundReport") {
                try {
                    ImpoundReport report = JsonConvert.DeserializeObject<ImpoundReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    DataController.AddReport(report);
                    Database.SaveImpoundReport(report);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createImpoundReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createImpoundReport:\n{ex}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
            } else if (path == "createTrafficIncidentReport") {
                try {
                    TrafficIncidentReport report = JsonConvert.DeserializeObject<TrafficIncidentReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    DataController.AddReport(report);
                    Database.SaveTrafficIncidentReport(report);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createTrafficIncidentReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createTrafficIncidentReport:\n{ex}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
            } else if (path == "createInjuryReport") {
                try {
                    InjuryReport report = JsonConvert.DeserializeObject<InjuryReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    DataController.AddReport(report);
                    Database.SaveInjuryReport(report);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createInjuryReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createInjuryReport:\n{ex}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
            } else if (path == "createPropertyEvidenceReceiptReport") {
                try {
                    PropertyEvidenceReceiptReport report = JsonConvert.DeserializeObject<PropertyEvidenceReceiptReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    DataController.AddReport(report);
                    Database.SavePropertyEvidenceReceiptReport(report);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createPropertyEvidenceReceiptReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createPropertyEvidenceReceiptReport:\n{ex}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
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
                var data = JsonConvert.DeserializeAnonymousType(body, new { LicensePlate = "", Reason = "", ExpiresAt = default(DateTime), IssuedBy = "LSPD", ModelDisplayName = (string)null });
                if (data == null || string.IsNullOrWhiteSpace(data.LicensePlate) || string.IsNullOrWhiteSpace(data.Reason)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "License plate and reason are required." }));
                    contentType = "text/json";
                    status = 400;
                    return;
                }
                var expires = data.ExpiresAt != default(DateTime) ? data.ExpiresAt : System.DateTime.UtcNow.AddDays(7);
                if (DataController.TryAddBOLOByPlate(data.LicensePlate.Trim(), data.Reason.Trim(), expires, data.IssuedBy ?? "LSPD", data.ModelDisplayName)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = true }));
                    contentType = "text/json";
                    status = 200;
                } else {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Failed to add BOLO." }));
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

        /// <summary>True if reportId exists and is an incident, injury, citation, traffic incident, or impound report (attachable as evidence).</summary>
        private static bool ReportExistsAndIsAttachable(string reportId) {
            if (string.IsNullOrWhiteSpace(reportId)) return false;
            return (DataController.IncidentReports?.Any(r => r.Id == reportId) ?? false)
                || (DataController.InjuryReports?.Any(r => r.Id == reportId) ?? false)
                || (DataController.CitationReports?.Any(r => r.Id == reportId) ?? false)
                || (DataController.TrafficIncidentReports?.Any(r => r.Id == reportId) ?? false)
                || (DataController.ImpoundReports?.Any(r => r.Id == reportId) ?? false)
                || (DataController.PropertyEvidenceReports?.Any(r => r.Id == reportId) ?? false);
        }
    }
}
