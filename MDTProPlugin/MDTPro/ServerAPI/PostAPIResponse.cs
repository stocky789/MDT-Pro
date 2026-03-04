using MDTPro.Data;
using MDTPro.Data.Reports;
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

            string body = Helper.GetRequestPostData(req);
            if (string.IsNullOrEmpty(body)) {
                buffer = Encoding.UTF8.GetBytes("Bad Request - Empty Body");
                contentType = "text/plain";
                status = 400;
                return;
            } else if (path == "updatePedData") {
                MDTProPedData pedData = JsonConvert.DeserializeObject<MDTProPedData>(body);

                DataController.UpdatePedData(pedData);

                DataController.SyncPedDatabaseWithCDF();

                Database.SavePed(pedData);

                buffer = Encoding.UTF8.GetBytes("OK");
                contentType = "text/plain";
                status = 200;
            } else if (path == "updateVehicleData") {
                MDTProVehicleData vehicleData = JsonConvert.DeserializeObject<MDTProVehicleData>(body);

                DataController.UpdateVehicleData(vehicleData);

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

                CourtData courtCase = DataController.courtDatabase.Find(x => x.Number == report.CourtCaseNumber);
                if (courtCase != null) Database.SaveCourtCase(courtCase);

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
            }
        }
    }
}
