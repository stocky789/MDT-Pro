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
                var data = JsonConvert.DeserializeAnonymousType(body, new { Number = "" });
                if (data == null || string.IsNullOrWhiteSpace(data.Number)) {
                    buffer = Encoding.UTF8.GetBytes("Bad Request");
                    contentType = "text/plain";
                    status = 400;
                    return;
                }
                if (DataController.ForceResolveCourtCase(data.Number)) {
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
                Config config = JsonConvert.DeserializeObject<Config>(body);

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
