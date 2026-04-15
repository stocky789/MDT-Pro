using MDTPro.Data;
using MDTPro.Data.Reports;
using MDTPro.EventListeners;
using System;
using System.Collections.Generic;
using System.Linq;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;

namespace MDTPro.ServerAPI {
    internal class PostAPIResponse : APIResponse {
        private static string FormatBackupError(string action, bool ok, string whenAvailableFail) {
            if (ok) return null;
            if (!BackupHelper.IsAvailable)
                return "No backup integration available. Install Policing Redefined or Ultimate Backup.";
            string p = ModIntegration.ActiveBackupProviderId ?? "";
            if (p.Equals("UltimateBackup", StringComparison.OrdinalIgnoreCase)) {
                switch (action) {
                    case "tow":
                    case "transport":
                    case "coroner":
                    case "animalcontrol":
                        return "This Quick Action needs Policing Redefined’s backup API. Ultimate Backup does not support it from the MDT.";
                }
            }
            return whenAvailableFail;
        }

        internal PostAPIResponse(HttpListenerRequest req) : base(null) {
            string rawPath = req.Url?.AbsolutePath ?? "";
            if (!rawPath.StartsWith("/post/", StringComparison.OrdinalIgnoreCase)) return;
            string path = rawPath.Substring("/post/".Length).Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(path)) return;

            if (path.Equals("alprClear", StringComparison.OrdinalIgnoreCase)) {
                GameFiberHttpBridge.EnqueueFireAndForget(() => ALPR.ALPRController.Clear());
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
                return;
            }

            if (path.Equals("calloutAction", StringComparison.OrdinalIgnoreCase)) {
                string bodyCallout = Helper.GetRequestPostData(req);
                string action = null;
                string calloutId = null;
                string sendMessage = null;
                if (!string.IsNullOrEmpty(bodyCallout)) {
                    try {
                        var data = JsonConvert.DeserializeAnonymousType(bodyCallout, new { action = (string)null, calloutId = (string)null, message = (string)null });
                        action = data?.action?.Trim().ToLowerInvariant();
                        calloutId = data?.calloutId?.Trim();
                        sendMessage = data?.message;
                    } catch { }
                }
                if (string.IsNullOrEmpty(calloutId)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "calloutId is required (from the active callout list)." }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "accept", "enroute", "en_route", "sendmessage", "send_message" };
                if (string.IsNullOrEmpty(action) || !allowed.Contains(action)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "action must be 'accept', 'enRoute', or 'sendMessage' (optional message)." }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                var outcome = CalloutActionHelper.RunOnGameThread(action, calloutId, sendMessage);
                bool ok = outcome.Result == CalloutActionHelper.CalloutActionResult.Ok;
                int httpStatus = outcome.Result switch {
                    CalloutActionHelper.CalloutActionResult.Ok => 200,
                    CalloutActionHelper.CalloutActionResult.NotFound => 404,
                    CalloutActionHelper.CalloutActionResult.BadState => 409,
                    _ => 500
                };
                buffer = Encoding.UTF8.GetBytes(ok
                    ? JsonConvert.SerializeObject(new { success = true })
                    : JsonConvert.SerializeObject(new { success = false, error = outcome.Message }));
                contentType = "application/json";
                status = httpStatus;
                return;
            }

            if (path.Equals("cadUnitStatus", StringComparison.OrdinalIgnoreCase)) {
                string bodyCad = Helper.GetRequestPostData(req);
                string statusText = null;
                if (!string.IsNullOrEmpty(bodyCad)) {
                    try {
                        var data = JsonConvert.DeserializeAnonymousType(bodyCad, new { status = (string)null });
                        statusText = data?.status?.Trim();
                    } catch { }
                }
                if (string.IsNullOrEmpty(statusText)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "status is required (e.g. 10-8, 10-97, Traffic stop)." }));
                    contentType = "application/json";
                    status = 400;
                    return;
                }
                CalloutEvents.CadUnitStatus = statusText;
                CalloutInterfaceCadPublisher.TryPublishCadUnitStatus(statusText);
                WebSocketHandler.BroadcastCalloutPayload();
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = true }));
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
                try {
                    OfficerInformationData parsed = ParseOfficerInformationPostBody(body);
                    if (parsed == null) {
                        buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid officer information JSON." }));
                        contentType = "application/json";
                        status = 400;
                        return;
                    }
                    DataController.OfficerInformationData = parsed;
                    Database.SaveOfficerInformation(parsed);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[updateOfficerInformationData] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] updateOfficerInformationData:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                    return;
                }
            } else if (path == "modifyCurrentShift") {
                string action = Helper.NormalizePlainOrJsonString(body);
                if (string.IsNullOrEmpty(action)) {
                    buffer = Encoding.UTF8.GetBytes("Bad Request - Invalid Action");
                    contentType = "text/plain";
                    status = 400;
                    return;
                }
                Exception shiftFiberError = null;
                bool ran;
                if (action.Equals("start", StringComparison.OrdinalIgnoreCase)) {
                    ran = GameFiberHttpBridge.TryExecuteBlocking(() => {
                        try { DataController.StartCurrentShift(); }
                        catch (Exception ex) { shiftFiberError = ex; }
                    }, 5000, out var bridgeStartErr);
                    if (bridgeStartErr != null) shiftFiberError = shiftFiberError ?? bridgeStartErr;
                } else if (action.Equals("end", StringComparison.OrdinalIgnoreCase)) {
                    ran = GameFiberHttpBridge.TryExecuteBlocking(() => {
                        try { DataController.EndCurrentShift(); }
                        catch (Exception ex) { shiftFiberError = ex; }
                    }, 5000, out var bridgeEndErr);
                    if (bridgeEndErr != null) shiftFiberError = shiftFiberError ?? bridgeEndErr;
                } else {
                    buffer = Encoding.UTF8.GetBytes("Bad Request - Invalid Action");
                    contentType = "text/plain";
                    status = 400;
                    return;
                }
                if (!ran) {
                    buffer = Encoding.UTF8.GetBytes("Shift control timed out (game busy or paused). Try again.");
                    contentType = "text/plain";
                    status = 503;
                    return;
                }
                if (shiftFiberError != null) {
                    Utility.Helper.Log($"[modifyCurrentShift] {shiftFiberError.Message}", true, Utility.Helper.LogSeverity.Error);
                    buffer = Encoding.UTF8.GetBytes("Shift control failed.");
                    contentType = "text/plain";
                    status = 500;
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
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createIncidentReport:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
            } else if (path == "createCitationReport") {
                try {
                    CitationReport report = JsonConvert.DeserializeObject<CitationReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    if (report.Charges == null) report.Charges = new List<CitationReport.Charge>();
                    DataController.AddReport(report);
                    Database.SaveCitationReport(report);
                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createCitationReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createCitationReport:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    contentType = "application/json";
                    status = 500;
                }
            } else if (path == "createArrestReport") {
                try {
                    ArrestReport report = JsonConvert.DeserializeObject<ArrestReport>(body);
                    if (report == null) { buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Invalid report data." })); contentType = "application/json"; status = 400; return; }
                    if (report.Charges == null) report.Charges = new List<ArrestReport.Charge>();
                    if (report.AttachedReportIds == null) report.AttachedReportIds = new List<string>();

                    var existing = DataController.ArrestReports?.FirstOrDefault(x => x.Id == report.Id);
                    if (existing != null && !string.IsNullOrEmpty(existing.CourtCaseNumber) && report.Status == ReportStatus.Pending) {
                        buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Arrest already closed; cannot reopen." }));
                        contentType = "application/json";
                        status = 400;
                        return;
                    }

                    Helper.LogArrestCourtVerbose(
                        $"HTTP createArrestReport: Id={report.Id}, Status={(int)report.Status}, Offender={report.OffenderPedName ?? "?"}, Charges={report.Charges?.Count ?? 0}, CourtCaseNo(before)={report.CourtCaseNumber ?? "(none)"} — queueing game fiber");

                    // RPH / CDF / ped sync expect the game fiber. HTTP uses ThreadPool — run on the shared bridge so work is not lost when script ticks stall (pause / alt-tab).
                    Exception fiberError = null;
                    if (!GameFiberHttpBridge.TryExecuteBlocking(() => {
                        try {
                            Helper.LogArrestCourtVerbose($"GameFiber createArrestReport: start AddReport for {report.Id}");
                            DataController.AddReport(report);
                            Helper.LogArrestCourtVerbose($"GameFiber createArrestReport: AddReport done; SaveArrestReport for {report.Id}");
                            Database.SaveArrestReport(report);
                            Helper.LogArrestCourtVerbose($"GameFiber createArrestReport: SaveArrestReport done; CourtCaseNo(after)={report.CourtCaseNumber ?? "(none)"}");
                            CourtData courtCase = DataController.FindCourtCaseByNumber(report.CourtCaseNumber);
                            if (courtCase != null) {
                                Helper.LogArrestCourtVerbose($"GameFiber createArrestReport: FindCourtCaseByNumber hit — second SaveCourtCase for {report.CourtCaseNumber}");
                                Database.SaveCourtCase(courtCase);
                            } else {
                                Helper.LogArrestCourtVerbose($"GameFiber createArrestReport: FindCourtCaseByNumber returned null for '{report.CourtCaseNumber ?? ""}' (no second SaveCourtCase)");
                            }
                            if (report.Status == ReportStatus.Closed && !string.IsNullOrEmpty(report.CourtCaseNumber)) {
                                Utility.Helper.Log($"[MDTPro] Arrest closed for court: report {report.Id}, case {report.CourtCaseNumber}, status={(int)report.Status}", false, Utility.Helper.LogSeverity.Info);
                            }
                        } catch (Exception ex) {
                            fiberError = ex;
                            Helper.LogArrestCourtVerbose($"GameFiber createArrestReport: EXCEPTION {ex.GetType().Name}: {ex.Message}");
                        }
                    }, Timeout.Infinite, out var bridgeErr)) {
                        Utility.Helper.Log("[createArrestReport] Game fiber bridge stopped before arrest save could run.", true, Utility.Helper.LogSeverity.Warning);
                        buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "MDT Pro server is stopping; try saving the arrest again after going on duty." }));
                        contentType = "application/json";
                        status = 503;
                        return;
                    }
                    if (bridgeErr != null)
                        fiberError = fiberError ?? bridgeErr;
                    Helper.LogArrestCourtVerbose("HTTP createArrestReport: game fiber finished");
                    if (fiberError != null) throw fiberError;

                    buffer = Encoding.UTF8.GetBytes("OK");
                    contentType = "text/plain";
                    status = 200;
                } catch (Exception ex) {
                    Utility.Helper.Log($"[createArrestReport] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createArrestReport:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
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
                // If arrest has linked court case, sync attachment and recalc evidence
                if (!string.IsNullOrEmpty(arrest.CourtCaseNumber)) {
                    var courtCase = DataController.CourtDatabase?.FirstOrDefault(x => x.Number == arrest.CourtCaseNumber);
                    if (courtCase != null && courtCase.Status == 0) {
                        if (courtCase.AttachedReportIds == null) courtCase.AttachedReportIds = new System.Collections.Generic.List<string>();
                        if (!courtCase.AttachedReportIds.Contains(data.reportId)) courtCase.AttachedReportIds.Add(data.reportId);
                        DataController.RecalculateCourtCaseEvidence(courtCase);
                        Database.SaveCourtCase(courtCase);
                    }
                }
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
                // If arrest has linked court case, sync detachment and recalc evidence
                if (!string.IsNullOrEmpty(arrest.CourtCaseNumber)) {
                    var courtCase = DataController.CourtDatabase?.FirstOrDefault(x => x.Number == arrest.CourtCaseNumber);
                    if (courtCase != null && courtCase.Status == 0) {
                        if (courtCase.AttachedReportIds != null) courtCase.AttachedReportIds.Remove(data.reportId);
                        DataController.RecalculateCourtCaseEvidence(courtCase);
                        Database.SaveCourtCase(courtCase);
                    }
                }
                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "attachReportsToArrest") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { arrestReportId = "", reportIds = new string[0] });
                if (data == null || string.IsNullOrWhiteSpace(data.arrestReportId) || data.reportIds == null) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "arrestReportId and reportIds required" }));
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
                int added = 0;
                foreach (var reportId in data.reportIds) {
                    if (string.IsNullOrWhiteSpace(reportId) || reportId == arrest.Id) continue;
                    if (!ReportExistsAndIsAttachable(reportId)) continue;
                    if (!arrest.AttachedReportIds.Contains(reportId)) {
                        arrest.AttachedReportIds.Add(reportId);
                        added++;
                    }
                }
                if (added > 0) {
                    Database.SaveArrestReport(arrest);
                    // If arrest has linked court case, sync attachments and recalc evidence
                    if (!string.IsNullOrEmpty(arrest.CourtCaseNumber)) {
                        var courtCase = DataController.CourtDatabase?.FirstOrDefault(x => x.Number == arrest.CourtCaseNumber);
                        if (courtCase != null && courtCase.Status == 0) {
                            courtCase.AttachedReportIds = arrest.AttachedReportIds != null
                                ? new System.Collections.Generic.List<string>(arrest.AttachedReportIds)
                                : new System.Collections.Generic.List<string>();
                            DataController.RecalculateCourtCaseEvidence(courtCase);
                            Database.SaveCourtCase(courtCase);
                        }
                    }
                }
                buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = true, added }));
                contentType = "application/json";
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
                DataController.RecalculateCourtCaseEvidence(courtCase);
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
                DataController.RecalculateCourtCaseEvidence(courtCase);
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
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createImpoundReport:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
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
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createTrafficIncidentReport:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
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
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createInjuryReport:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
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
                    try { System.IO.File.AppendAllText(Setup.SetupController.LogFilePath, $"\n[{DateTime.Now:O}] [Error] createPropertyEvidenceReceiptReport:\n{Helper.SanitizeExceptionForLog(ex)}"); } catch { }
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
                try {
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
                        ok = BackupHelper.RequestPanicBackup();
                        err = FormatBackupError(act, ok, "Panic backup failed.");
                        break;
                    case "localpatrol":
                        ok = BackupHelper.RequestBackup("LocalPatrol", rc);
                        err = FormatBackupError(act, ok, "Patrol backup failed.");
                        break;
                    case "statepatrol":
                        ok = BackupHelper.RequestBackup("StatePatrol", rc);
                        err = FormatBackupError(act, ok, "State patrol backup failed.");
                        break;
                    case "localswat":
                        ok = BackupHelper.RequestBackup("LocalSWAT", rc);
                        err = FormatBackupError(act, ok, "SWAT backup failed.");
                        break;
                    case "nooseswat":
                        ok = BackupHelper.RequestBackup("NooseSWAT", rc);
                        err = FormatBackupError(act, ok, "NOOSE SWAT backup failed.");
                        break;
                    case "localk9":
                        ok = BackupHelper.RequestBackup("LocalK9Patrol", rc);
                        err = FormatBackupError(act, ok, "K9 backup failed.");
                        break;
                    case "statek9":
                        ok = BackupHelper.RequestBackup("StateK9Patrol", rc);
                        err = FormatBackupError(act, ok, "State K9 backup failed.");
                        break;
                    case "ambulance":
                        ok = BackupHelper.RequestBackup("Ambulance", rc);
                        err = FormatBackupError(act, ok, "Ambulance backup failed.");
                        break;
                    case "fire":
                        ok = BackupHelper.RequestBackup("FireDepartment", rc);
                        err = FormatBackupError(act, ok, "Fire department backup failed.");
                        break;
                    case "coroner":
                        ok = BackupHelper.RequestBackup("Coroner", rc);
                        err = FormatBackupError(act, ok, "Coroner backup failed.");
                        break;
                    case "animalcontrol":
                        ok = BackupHelper.RequestBackup("AnimalControl", rc);
                        err = FormatBackupError(act, ok, "Animal control backup failed.");
                        break;
                    case "trafficstop":
                        var tsUnit = !string.IsNullOrWhiteSpace(reqData.unit) ? reqData.unit.Trim() : "LocalPatrol";
                        ok = BackupHelper.RequestTrafficStopBackup(tsUnit, rc);
                        err = FormatBackupError(act, ok, "Traffic stop backup failed (ensure you are in a traffic stop) or backup mod declined.");
                        break;
                    case "transport":
                        ok = BackupHelper.RequestPoliceTransport(rc);
                        err = FormatBackupError(act, ok, "Police transport failed.");
                        break;
                    case "tow":
                        ok = BackupHelper.RequestTowServiceBackup();
                        err = FormatBackupError(act, ok, "Tow menu could not be opened.");
                        break;
                    case "group":
                        ok = BackupHelper.RequestGroupBackup();
                        err = FormatBackupError(act, ok, "Group backup failed.");
                        break;
                    case "airlocal":
                        ok = BackupHelper.RequestAirBackup("LocalAir");
                        err = FormatBackupError(act, ok, "Air backup failed (often requires an active pursuit).");
                        break;
                    case "airnoose":
                        ok = BackupHelper.RequestAirBackup("NooseAir");
                        err = FormatBackupError(act, ok, "NOOSE air backup failed (often requires an active pursuit).");
                        break;
                    case "spikestrips":
                        ok = BackupHelper.RequestSpikeStripsBackup();
                        err = FormatBackupError(act, ok, "Spike strips backup failed (often requires an active pursuit).");
                        break;
                    case "felonystop":
                        ok = BackupHelper.InitiateFelonyStop();
                        err = FormatBackupError(act, ok, "Felony stop failed.");
                        break;
                    case "dismiss":
                        BackupHelper.DismissAllBackupUnits(false);
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
                } catch (Exception ex) {
                    Utility.Helper.Log($"[requestBackup] {ex.Message}", true, Utility.Helper.LogSeverity.Error);
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Backup error: " + ex.Message }));
                    contentType = "text/json";
                    status = 500;
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
            } else if (path == "firearmCheckResult") {
                var data = JsonConvert.DeserializeAnonymousType(body, new { serialNumber = (string)null, ownerName = (string)null, owner = (string)null, weaponType = (string)null, weapon = (string)null, status = (string)null, weaponModelId = (string)null });
                if (data == null) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Invalid JSON body." }));
                    contentType = "text/json";
                    status = 400;
                    return;
                }
                string owner = !string.IsNullOrWhiteSpace(data.ownerName) ? data.ownerName : data.owner;
                string weapon = !string.IsNullOrWhiteSpace(data.weaponType) ? data.weaponType : data.weapon;
                if (string.IsNullOrWhiteSpace(owner)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "ownerName (or owner) is required." }));
                    contentType = "text/json";
                    status = 400;
                    return;
                }
                if (DataController.SaveFirearmCheckResultFromDispatch(data.serialNumber, owner, weapon, data.status, data.weaponModelId)) {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = true }));
                    contentType = "text/json";
                    status = 200;
                } else {
                    buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Failed to save firearm check result." }));
                    contentType = "text/json";
                    status = 400;
                }
            }
        }

        /// <summary>Parses POST JSON from browser/native MDT; tolerates empty strings and numeric badge variants; keeps <see cref="OfficerInformationData.agencyScriptName"/> when the client omits it.</summary>
        private static OfficerInformationData ParseOfficerInformationPostBody(string body) {
            if (string.IsNullOrWhiteSpace(body)) return null;
            JObject jo;
            try {
                jo = JObject.Parse(body);
            } catch {
                return null;
            }

            string prevScript = DataController.OfficerInformationData?.agencyScriptName;

            static string OptionalString(JToken t) {
                if (t == null || t.Type == JTokenType.Null) return null;
                string s = t.Type == JTokenType.String ? t.Value<string>() : t.ToString();
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            }

            static int? OptionalBadge(JToken t) {
                if (t == null || t.Type == JTokenType.Null) return null;
                if (t.Type == JTokenType.Integer) return t.Value<int>();
                if (t.Type == JTokenType.Float) {
                    double d = t.Value<double>();
                    if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                    long r = (long)Math.Round(d);
                    if (r < int.MinValue || r > int.MaxValue) return null;
                    return (int)r;
                }
                if (t.Type == JTokenType.String) {
                    string s = t.Value<string>()?.Trim();
                    if (string.IsNullOrEmpty(s)) return null;
                    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : (int?)null;
                }
                return null;
            }

            string agencyScriptName = prevScript;
            if (jo.TryGetValue("agencyScriptName", StringComparison.OrdinalIgnoreCase, out JToken scriptTok))
                agencyScriptName = OptionalString(scriptTok);

            return new OfficerInformationData {
                firstName = OptionalString(jo["firstName"]),
                lastName = OptionalString(jo["lastName"]),
                rank = OptionalString(jo["rank"]),
                callSign = OptionalString(jo["callSign"]),
                agency = OptionalString(jo["agency"]),
                agencyScriptName = agencyScriptName,
                badgeNumber = OptionalBadge(jo["badgeNumber"]),
            };
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
