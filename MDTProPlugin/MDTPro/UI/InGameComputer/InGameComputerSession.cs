using LemonUI.Menus;
using MDTPro.Cloud;
using MDTPro.Data;
using MDTPro.Data.Reports;
using MDTPro.Setup;
using MDTPro.Utility;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MDTPro.UI.InGameComputer {
    internal sealed class InGameComputerSession {
        private const int MouseWheelScrollRows = 1;
        private const int ActionTitleMax = 48;
        private const int ActionAltTitleMax = 24;
        private const int ReadOnlyTitleMax = 48;
        private const int ReadOnlyValueMax = 40;
        private const int ChargeGroupTitleMax = 48;
        private const int ChargeTitleMax = 48;
        private const ulong IsDisabledControlJustPressedHash = 0x91AEF906BCA88877;

        private volatile bool _quit;
        private volatile bool _abortFromHost;
        private bool _scrollWheelUnavailable;
        private LemonUI.ObjectPool _pool;
        private NativeMenu _menu;
        private InGameComputerBanner _banner;
        private InGameComputerTheme _theme;
        private readonly List<InlineLemonTextInput> _inputs = new List<InlineLemonTextInput>();
        private readonly List<(NativeItem Item, System.Drawing.Color Color)> _readOnlyRows = new List<(NativeItem, System.Drawing.Color)>();
        private readonly List<NativeItem> _disabledActions = new List<NativeItem>();
        private MDTProPedData _currentPed;
        private MDTProVehicleData _currentVehicle;
        private readonly List<CitationGroup.Charge> _selectedCharges = new List<CitationGroup.Charge>();

        internal void AbortFromHost() {
            _abortFromHost = true;
        }

        internal void Run() {
            _pool = new LemonUI.ObjectPool();
            _theme = InGameComputerTheme.Resolve(CurrentOfficer());
            _banner = new InGameComputerBanner(_theme);
            _menu = new NativeMenu("", _theme.NavTitle, "Quick lookup - citations", _banner) { MaxItems = 12 };
            InGameComputerStyle.ApplyShell(_menu, _theme);
            _pool.Add(_menu);
            _pool.ResolutionChanged += (_, __) => ApplyLayout();
            _pool.SafezoneChanged += (_, __) => ApplyLayout();

            BuildMain();
            _menu.Visible = true;

            bool savedPause = false;
            bool capturedPause = false;
            bool appliedPause = false;
            try {
                try {
                    savedPause = Game.IsPaused;
                    capturedPause = true;
                    Game.IsPaused = true;
                    appliedPause = true;
                } catch { /* ignore */ }

                while (!_quit && !_abortFromHost) {
                    GameFiber.Yield();
                    InGameComputerLayout.RefreshScreenMetrics();
                    bool suppressInput = ProcessInlineInputs();
                    if (suppressInput && _menu != null)
                        _menu.AcceptsInput = false;
                    try {
                        _pool.Process();
                    } catch (Exception ex) {
                        Helper.Log($"In-game MDT UI: {ex.Message}", false, Helper.LogSeverity.Warning);
                    }
                    if (!suppressInput)
                        ProcessScrollWheelNavigation();
                    if (suppressInput && _menu != null && !_inputs.Any(x => x.IsEditing))
                        _menu.AcceptsInput = true;
                    if (_menu != null && !_menu.Visible)
                        _quit = true;
                }
            } finally {
                try { _pool?.HideAll(); } catch { /* ignore */ }
                try { _banner?.Dispose(); } catch { /* ignore */ }
                _banner = null;
                if (appliedPause && capturedPause) {
                    try { Game.IsPaused = savedPause; } catch { /* ignore */ }
                }
            }
        }

        private void ProcessScrollWheelNavigation() {
            if (_menu == null || !_menu.Visible || !_menu.AcceptsInput || _scrollWheelUnavailable)
                return;

            int direction = 0;
            try {
                if (NativeFunction.CallByHash<bool>(IsDisabledControlJustPressedHash, 0, (int)GameControl.CursorScrollUp))
                    direction = -1;
                else if (NativeFunction.CallByHash<bool>(IsDisabledControlJustPressedHash, 0, (int)GameControl.CursorScrollDown))
                    direction = 1;
            } catch (Exception ex) {
                _scrollWheelUnavailable = true;
                Helper.Log($"In-game MDT UI: mouse wheel input unavailable ({ex.Message})", false, Helper.LogSeverity.Warning);
                return;
            }

            for (int i = 0; i < MouseWheelScrollRows; i++) {
                if (direction < 0)
                    _menu.Previous();
                else if (direction > 0)
                    _menu.Next();
            }
        }

        private bool ProcessInlineInputs() {
            bool consumed = false;
            foreach (InlineLemonTextInput input in _inputs.ToArray())
                consumed |= input.Process();
            return consumed;
        }

        private void BuildMain() {
            ResetMenu(_theme.NavTitle, "Quick lookup - citations");
            AddStatusRows();
            AddSection("Person");
            AddAction("Current PED", "Open current person.", () => ShowPed(MdtCompanionService.GetContextPed()));
            AddInputAction("Search PED", "Type name.", 64, text => ShowPed(MdtCompanionService.LookupPed(text, true)));
            AddAction("Recent IDs", "Latest identified people.", () => BuildRecentIds());
            AddSection("Vehicle");
            AddAction("Current Vehicle", "Open current vehicle.", () => ShowVehicle(MdtCompanionService.GetContextVehicle()));
            AddInputAction("Search Plate/VIN", "Type plate or VIN.", 32, text => ShowVehicle(MdtCompanionService.LookupVehicle(text, true)));
            AddAction("Nearby Vehicles", "Nearby plates.", () => BuildNearbyVehicles(explicitScan: false, pickForCitation: false));
            AddSection("Reports");
            AddAction("New Citation", "Current person and vehicle.", BuildCitation);
            AddAction("~r~Close", "", Close);
            FinishMenuColors();
        }

        private void AddStatusRows() {
            var mode = MdtCompanionService.GetModeStatus();
            OfficerInformationData officer = CurrentOfficer();
            string unit = FormatOfficerUnit(officer);
            AddReadOnly(_theme.DepartmentName, mode.DisplayMode, mode.UsesCloud ? _theme.Success : _theme.Muted);
            if (mode.CloudConfigured && !mode.UsesCloud)
                AddReadOnly("Cloud", mode.HasSavedLogin ? mode.Detail : "Not logged in", _theme.Warning);
            AddReadOnly("Unit", unit, _theme.BannerAccent);
            AddReadOnly("PED", SafeValue(_currentPed?.Name, "No active person"), _theme.Muted);
            AddReadOnly("Vehicle", SafeValue(_currentVehicle?.LicensePlate, "No active vehicle"), _theme.Muted);
        }

        private void BuildRecentIds() {
            ResetMenu("Recent IDs", "Select a person to search");
            var rows = MdtCompanionService.GetRecentIds(8);
            if (rows.Count == 0)
                AddReadOnly("No Recent IDs", "Identify a ped first", _theme.Muted);
            foreach (var row in rows) {
                string alt = Ellipsize(row.Type, 14);
                AddAction(Ellipsize(row.Name, 28), "Search this recent ID.", () => ShowPed(MdtCompanionService.LookupPed(row.Name, true)), alt);
            }
            AddSection("Navigation");
            AddAction("Back", "", BuildMain);
            FinishMenuColors();
        }

        private void BuildNearbyVehicles(bool explicitScan, bool pickForCitation) {
            ResetMenu("Nearby Vehicles", explicitScan ? "Explicit scan" : "Cached scan");
            bool completed;
            var rows = MdtCompanionService.GetNearbyVehicles(8, explicitScan, out completed);
            AddReadOnly("Scan", completed ? "Ready" : "Deferred", completed ? _theme.Success : _theme.Warning);
            if (rows.Count == 0)
                AddReadOnly("No vehicles", "Refresh or move closer", _theme.Muted);
            foreach (var row in rows) {
                string title = Ellipsize(SafeValue(row.LicensePlate, "Unknown plate"), 18);
                string alt = row.Distance.HasValue ? row.Distance.Value.ToString("0.0") + "m" : "";
                string desc = SafeValue(row.ModelDisplayName, "Unknown model");
                if (row.IsStolen) desc = "STOLEN - " + desc;
                bool hasPlate = !string.IsNullOrWhiteSpace(row.LicensePlate);
                AddAction(row.IsStolen ? "~r~" + title : title, Ellipsize(desc, 80), () => {
                    var lookup = MdtCompanionService.LookupVehicle(row.LicensePlate, true);
                    if (pickForCitation) {
                        _currentVehicle = lookup.VehicleData;
                        BuildCitation();
                    } else {
                        ShowVehicle(lookup);
                    }
                }, alt, enabled: hasPlate);
            }
            AddSection("Navigation");
            AddAction("Refresh", "Explicit scan.", () => BuildNearbyVehicles(explicitScan: true, pickForCitation: pickForCitation));
            AddAction("Back", "", pickForCitation ? new Action(BuildCitation) : new Action(BuildMain));
            FinishMenuColors();
        }

        private void ShowPed(MdtCompanionService.PedLookupResult result) {
            _currentPed = result?.PedData;
            BuildPedDetail(result);
        }

        private void BuildPedDetail(MdtCompanionService.PedLookupResult result) {
            MDTProPedData ped = result?.PedData;
            ResetMenu("PED Lookup", ped == null ? "No match" : Ellipsize(ped.Name, 40));
            if (ped == null) {
                AddReadOnly("No match", "Try another name", _theme.Danger);
            } else {
                AddReadOnly("Name", ped.Name, _theme.Text);
                AddReadOnly("DOB", SafeValue(ped.Birthday, "N/A"), _theme.Muted);
                AddReadOnly("License", SafeValue(ped.LicenseStatus, "N/A"), StatusColor(ped.LicenseStatus));
                AddReadOnly("Wanted", ped.IsWanted ? SafeValue(ped.WarrantText, "Active") : "No", ped.IsWanted ? _theme.Danger : _theme.Success);
                AddReadOnly("Supervision", BuildSupervisionText(ped), (ped.IsOnProbation || ped.IsOnParole) ? _theme.Warning : _theme.Muted);
                AddSummaryAction("Citations", CountText(ped.Citations), "View citation history.", () => BuildPedCitations(ped, result), HasRows(ped.Citations));
                AddSummaryAction("Arrests", CountText(ped.Arrests), "View arrest history.", () => BuildPedArrests(ped, result), HasRows(ped.Arrests));
                AddSummaryAction("Court", CountText(result.CourtCases), "View court cases.", () => BuildPedCourtCases(ped, result), HasRows(result.CourtCases));
            }
            AddSection("Actions");
            AddAction("Create Citation", ped == null ? "Search a person first." : "Create citation.", BuildCitation, enabled: ped != null);
            AddAction("Refresh Current PED", "", () => ShowPed(MdtCompanionService.GetContextPed()));
            AddAction("Back", "", BuildMain);
            FinishMenuColors();
        }

        private void BuildPedCitations(MDTProPedData ped, MdtCompanionService.PedLookupResult result) {
            ResetMenu("Citations", ped == null ? "No person" : Ellipsize(ped.Name, 40));
            var citations = ped?.Citations ?? new List<CitationGroup.Charge>();
            if (citations.Count == 0) {
                AddReadOnly("No Citations", "None on record", _theme.Muted);
            } else {
                foreach (var charge in citations) {
                    AddReadOnly(SafeValue(charge?.name, "Unknown citation"), FineRange(charge), _theme.Muted);
                }
            }
            AddSection("Navigation");
            AddAction("Back", "", () => BuildPedDetail(result));
            FinishMenuColors();
        }

        private void BuildPedArrests(MDTProPedData ped, MdtCompanionService.PedLookupResult result) {
            ResetMenu("Arrests", ped == null ? "No person" : Ellipsize(ped.Name, 40));
            var arrests = ped?.Arrests ?? new List<ArrestGroup.Charge>();
            if (arrests.Count == 0) {
                AddReadOnly("No Arrests", "None on record", _theme.Muted);
            } else {
                foreach (var charge in arrests) {
                    AddReadOnly(SafeValue(charge?.name, "Unknown arrest"), JailRange(charge), _theme.Muted);
                }
            }
            AddSection("Navigation");
            AddAction("Back", "", () => BuildPedDetail(result));
            FinishMenuColors();
        }

        private void BuildPedCourtCases(MDTProPedData ped, MdtCompanionService.PedLookupResult result) {
            ResetMenu("Court", ped == null ? "No person" : Ellipsize(ped.Name, 40));
            var cases = result?.CourtCases ?? new List<CourtData>();
            if (cases.Count == 0) {
                AddReadOnly("No Court Cases", "None on record", _theme.Muted);
            } else {
                foreach (var courtCase in cases) {
                    CourtData captured = courtCase;
                    string title = "Case " + SafeValue(captured?.Number, "Unknown");
                    string alt = CourtStatus(captured?.Status ?? 0);
                    string desc = BuildCourtCaseSummary(captured);
                    AddAction(title, desc, () => BuildCourtCaseDetail(captured, result), alt);
                }
            }
            AddSection("Navigation");
            AddAction("Back", "", () => BuildPedDetail(result));
            FinishMenuColors();
        }

        private void BuildCourtCaseDetail(CourtData courtCase, MdtCompanionService.PedLookupResult result) {
            ResetMenu("Court Case", "Case " + SafeValue(courtCase?.Number, "Unknown"));
            if (courtCase == null) {
                AddReadOnly("No case", "Court record unavailable", _theme.Danger);
            } else {
                AddReadOnly("Status", CourtStatus(courtCase.Status), CourtStatusColor(courtCase.Status));
                AddReadOnly("Report", SafeValue(courtCase.ReportId, "N/A"), _theme.Muted);
                AddReadOnly("Court", SafeValue(courtCase.CourtName, "N/A"), _theme.Muted);
                AddReadOnly("Hearing", FormatDate(courtCase.HearingDateUtc), _theme.Muted);
                if (!string.IsNullOrWhiteSpace(courtCase.Plea))
                    AddReadOnly("Plea", courtCase.Plea, _theme.Muted);
                if (!string.IsNullOrWhiteSpace(courtCase.OutcomeNotes))
                    AddReadOnly("Outcome", courtCase.OutcomeNotes, _theme.Muted);
                AddSection("Charges");
                var charges = courtCase.Charges ?? new List<CourtData.Charge>();
                if (charges.Count == 0) {
                    AddReadOnly("No Charges", "None listed", _theme.Muted);
                } else {
                    foreach (var charge in charges) {
                        AddReadOnly(SafeValue(charge?.Name, "Unknown charge"), CourtChargeSummary(charge), CourtChargeColor(charge));
                    }
                }
            }
            AddSection("Navigation");
            AddAction("Back", "", () => BuildPedCourtCases(_currentPed, result));
            FinishMenuColors();
        }

        private void ShowVehicle(MdtCompanionService.VehicleLookupResult result) {
            _currentVehicle = result?.VehicleData;
            BuildVehicleDetail(result);
        }

        private void BuildVehicleDetail(MdtCompanionService.VehicleLookupResult result) {
            MDTProVehicleData vehicle = result?.VehicleData;
            ResetMenu("Vehicle Lookup", vehicle == null ? "No match" : Ellipsize(vehicle.LicensePlate, 40));
            if (vehicle == null) {
                AddReadOnly("No match", "Try another plate/VIN", _theme.Danger);
            } else {
                AddReadOnly("Plate", vehicle.LicensePlate, _theme.Text);
                AddReadOnly("Model", SafeValue(vehicle.ModelDisplayName, vehicle.ModelName ?? "N/A"), _theme.Muted);
                AddReadOnly("VIN", SafeValue(vehicle.VehicleIdentificationNumber, "N/A"), _theme.Muted);
                AddReadOnly("Owner", SafeValue(vehicle.Owner, "N/A"), _theme.Muted);
                AddReadOnly("Stolen", vehicle.IsStolen ? "Yes" : "No", vehicle.IsStolen ? _theme.Danger : _theme.Success);
                AddReadOnly("Reg", SafeValue(vehicle.RegistrationStatus, "N/A"), StatusColor(vehicle.RegistrationStatus));
                AddReadOnly("Ins", SafeValue(vehicle.InsuranceStatus, "N/A"), StatusColor(vehicle.InsuranceStatus));
            }
            AddSection("Actions");
            bool hasOwner = !string.IsNullOrWhiteSpace(vehicle?.Owner);
            AddAction("Lookup Owner", hasOwner ? "" : "No owner available.", () => {
                if (!string.IsNullOrWhiteSpace(vehicle?.Owner))
                    ShowPed(MdtCompanionService.LookupVehicleOwner(vehicle, true));
            }, enabled: hasOwner);
            AddAction("Create Citation", _currentPed == null ? "Search a person first." : "Use this vehicle.", BuildCitation, enabled: _currentPed != null);
            AddAction("Back", "", BuildMain);
            FinishMenuColors();
        }

        private void BuildCitation() {
            ResetMenu("Create Citation", _currentPed == null ? "Person required" : Ellipsize(_currentPed.Name, 40));
            if (_currentPed == null) {
                AddReadOnly("PED", "Search or select a recent ID first", _theme.Danger);
                AddAction("Recent IDs", "", BuildRecentIds);
                AddInputAction("Search PED", "Type name.", 64, text => ShowPed(MdtCompanionService.LookupPed(text, true)), BuildCitation);
                AddAction("Back", "", BuildMain);
                FinishMenuColors();
                return;
            }
            AddReadOnly("Offender", _currentPed.Name, _theme.Text);
            AddReadOnly("Vehicle", SafeValue(_currentVehicle?.LicensePlate, "None"), _theme.Muted);
            AddReadOnly("Charges", _selectedCharges.Count.ToString(), _selectedCharges.Count > 0 ? _theme.Success : _theme.Muted);
            foreach (var charge in _selectedCharges.Take(4))
                AddReadOnly(charge.name, FineRange(charge), _theme.Muted);
            if (_selectedCharges.Count > 4)
                AddReadOnly("More", (_selectedCharges.Count - 4).ToString(), _theme.Muted);

            AddSection("Vehicle");
            AddAction("Use Current Vehicle", "Attach current vehicle.", () => {
                var lookup = MdtCompanionService.GetContextVehicle();
                if (lookup.VehicleData == null) {
                    RageNotification.Show("No current vehicle found.", RageNotification.NotificationType.Info);
                } else {
                    _currentVehicle = lookup.VehicleData;
                }
                BuildCitation();
            });
            AddAction("Nearby Vehicles", "Pick a vehicle.", () => BuildNearbyVehicles(explicitScan: false, pickForCitation: true));
            AddAction("Clear Vehicle", "", () => { _currentVehicle = null; BuildCitation(); }, enabled: _currentVehicle != null);
            AddSection("Charges");
            AddInputAction("Search Charges", "Search all citation charges.", 48, BuildCitationChargeSearchResults, BuildCitation);
            foreach (var group in GetCitationOptionGroups()) {
                CitationGroup captured = group;
                AddAction(captured.name, "Select charges.", () => BuildChargeGroup(captured), CountText(captured.charges));
            }
            AddSection("Submit");
            bool hasCharges = _selectedCharges.Count > 0;
            AddAction("Save Open Citation", hasCharges ? "Save as open." : "Select a charge first.", () => SaveCitation(closeNow: false), enabled: hasCharges);
            AddAction("Close / Issue Citation", hasCharges ? "Save closed and issue." : "Select a charge first.", () => SaveCitation(closeNow: true), enabled: hasCharges);
            AddAction("Clear Charges", "", () => { _selectedCharges.Clear(); BuildCitation(); }, enabled: hasCharges);
            AddAction("Back", "", BuildMain);
            FinishMenuColors();
        }

        private void BuildChargeGroup(CitationGroup group) {
            ResetMenu(Ellipsize(group?.name, ChargeGroupTitleMax), "Toggle charges");
            foreach (var charge in (group?.charges ?? new List<CitationGroup.Charge>())) {
                CitationGroup.Charge captured = charge;
                var item = new NativeCheckboxItem(Ellipsize(captured.name, ChargeTitleMax), FineRange(captured), IsSelected(captured));
                item.CheckboxChanged += (_, __) => {
                    if (item.Checked) AddSelectedCharge(captured);
                    else RemoveSelectedCharge(captured);
                    item.Description = FineRange(captured);
                };
                InGameComputerStyle.ApplyAction(item, _theme);
                _menu.Add(item);
            }
            AddSection("Navigation");
            AddAction("Done", "", BuildCitation);
            FinishMenuColors();
        }

        private void BuildCitationChargeSearchResults(string query) {
            string search = (query ?? "").Trim();
            ResetMenu("Citation Search", string.IsNullOrWhiteSpace(search) ? "All citation charges" : Ellipsize(search, 40));
            var matches = GetCitationOptionGroups()
                .SelectMany(group => (group.charges ?? new List<CitationGroup.Charge>())
                    .Where(charge => charge != null && (string.IsNullOrWhiteSpace(search) || SafeValue(charge.name, "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(charge => new { Group = group, Charge = charge }))
                .ToList();

            if (matches.Count == 0) {
                AddReadOnly("No matches", "Try another search", _theme.Muted);
            } else {
                foreach (var match in matches) {
                    CitationGroup.Charge captured = match.Charge;
                    var item = new NativeCheckboxItem(Ellipsize(captured.name, ChargeTitleMax), BuildCitationChargeDescription(match.Group, captured), IsSelected(captured));
                    item.CheckboxChanged += (_, __) => {
                        if (item.Checked) AddSelectedCharge(captured);
                        else RemoveSelectedCharge(captured);
                        item.Description = BuildCitationChargeDescription(match.Group, captured);
                    };
                    InGameComputerStyle.ApplyAction(item, _theme);
                    _menu.Add(item);
                }
            }
            AddSection("Navigation");
            AddInputAction("Search Again", "Search all citation charges.", 48, BuildCitationChargeSearchResults, BuildCitation);
            AddAction("Done", "", BuildCitation);
            FinishMenuColors();
        }

        private void SaveCitation(bool closeNow) {
            if (_currentPed == null) {
                RageNotification.Show("Search a person before creating a citation.", RageNotification.NotificationType.Info);
                BuildCitation();
                return;
            }
            if (_selectedCharges.Count == 0) {
                RageNotification.Show("Select at least one citation charge.", RageNotification.NotificationType.Info);
                BuildCitation();
                return;
            }
            var charges = _selectedCharges.Select(c => new CitationReport.Charge {
                name = c.name,
                minFine = c.minFine,
                maxFine = c.maxFine,
                canRevokeLicense = c.canRevokeLicense,
                isArrestable = c.isArrestable
            }).ToList();
            CitationReport report = MdtCompanionService.BuildQuickCitationReport(_currentPed, _currentVehicle, charges, closeNow);
            var result = MdtCompanionService.SaveCitationReport(report);
            if (result.Success) {
                string status = closeNow ? "issued" : "saved";
                RageNotification.ShowSuccess($"Citation {status}: {report.Id}");
                _selectedCharges.Clear();
                BuildMain();
            } else {
                RageNotification.ShowError(result.Error ?? "Failed to save citation.");
                BuildCitation();
            }
        }

        private void ResetMenu(string name, string description) {
            _inputs.Clear();
            _readOnlyRows.Clear();
            _disabledActions.Clear();
            if (_menu == null) return;
            _menu.Name = name ?? "MDT Computer";
            _menu.Description = description ?? "";
            _menu.Clear();
            _menu.MaxItems = 12;
            ApplyLayout();
        }

        private void ApplyLayout() {
            InGameComputerLayout.ApplyMenuColumn(_menu);
        }

        private NativeItem AddAction(string title, string description, Action action, string altTitle = null, bool enabled = true) {
            var item = new NativeItem(Ellipsize(title, ActionTitleMax), description ?? "");
            if (altTitle != null) item.AltTitle = Ellipsize(altTitle, ActionAltTitleMax);
            item.Enabled = enabled;
            item.Activated += (_, __) => action?.Invoke();
            if (enabled) InGameComputerStyle.ApplyAction(item, _theme);
            else {
                InGameComputerStyle.ApplyReadOnly(item, _theme, _theme.Muted);
                item.Enabled = false;
                _disabledActions.Add(item);
            }
            _menu.Add(item);
            return item;
        }

        private NativeItem AddSummaryAction(string title, string count, string description, Action action, bool enabled) {
            return AddAction(title, enabled ? description : "None on record.", action, CountTextValue(count), enabled);
        }

        private void AddInputAction(string title, string description, int maxLength, Action<string> completed, Action cancelled = null) {
            var row = new NativeItem(Ellipsize(title, ActionTitleMax), description ?? "");
            row.AltTitle = "Type";
            var input = new InlineLemonTextInput(_menu, row, maxLength);
            input.Completed += value => {
                string text = (value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text)) {
                    RageNotification.Show("Enter search text.", RageNotification.NotificationType.Info);
                    (cancelled ?? BuildMain).Invoke();
                    return;
                }
                completed?.Invoke(text);
            };
            input.Cancelled += _ => (cancelled ?? BuildMain).Invoke();
            row.Activated += (_, __) => {
                CancelInputs();
                input.Start("", masked: false, editDescription: "Type text. Enter to search, Esc to cancel.");
            };
            _inputs.Add(input);
            InGameComputerStyle.ApplyAction(row, _theme);
            _menu.Add(row);
        }

        private void AddReadOnly(string title, string value, System.Drawing.Color color) {
            var item = new NativeItem(Ellipsize(title, ReadOnlyTitleMax), "") { Enabled = true };
            item.AltTitle = Ellipsize(SafeValue(value, "N/A"), ReadOnlyValueMax);
            InGameComputerStyle.ApplyReadOnly(item, _theme, color);
            _readOnlyRows.Add((item, color));
            _menu.Add(item);
        }

        private void AddSeparator() {
            AddSection("");
        }

        private void AddSection(string title) {
            var item = new NativeSeparatorItem((title ?? "").ToUpperInvariant());
            InGameComputerStyle.ApplySeparator(item, _theme);
            _menu.Add(item);
        }

        private void FinishMenuColors() {
            if (_menu == null) return;
            foreach (NativeItem item in _menu.Items) {
                if (item is NativeSeparatorItem) InGameComputerStyle.ApplySeparator((NativeSeparatorItem)item, _theme);
                else InGameComputerStyle.ApplyAction(item, _theme);
            }
            foreach (var row in _readOnlyRows)
                InGameComputerStyle.ApplyReadOnly(row.Item, _theme, row.Color);
            foreach (NativeItem item in _disabledActions) {
                InGameComputerStyle.ApplyReadOnly(item, _theme, _theme.Muted);
                item.Enabled = false;
            }
        }

        private void CancelInputs() {
            foreach (InlineLemonTextInput input in _inputs.ToArray())
                input.CancelActiveEdit();
        }

        private void Close() {
            _quit = true;
            if (_menu != null) _menu.Visible = false;
            try { _pool?.HideAll(); } catch { /* ignore */ }
        }

        private bool IsSelected(CitationGroup.Charge charge) {
            return _selectedCharges.Any(c => SameCharge(c, charge));
        }

        private void AddSelectedCharge(CitationGroup.Charge charge) {
            if (charge == null || IsSelected(charge)) return;
            _selectedCharges.Add(charge);
        }

        private void RemoveSelectedCharge(CitationGroup.Charge charge) {
            _selectedCharges.RemoveAll(c => SameCharge(c, charge));
        }

        private static bool SameCharge(CitationGroup.Charge a, CitationGroup.Charge b) {
            if (a == null || b == null) return false;
            return string.Equals(a.name, b.name, StringComparison.OrdinalIgnoreCase)
                && a.minFine == b.minFine
                && a.maxFine == b.maxFine;
        }

        private static string SafeValue(string value, string fallback) {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string CountText<T>(ICollection<T> list) {
            return list == null ? "0" : list.Count.ToString();
        }

        private static string CountTextValue(string value) {
            return string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
        }

        private static bool HasRows<T>(ICollection<T> list) {
            return list != null && list.Count > 0;
        }

        private static string BuildSupervisionText(MDTProPedData ped) {
            if (ped == null) return "N/A";
            if (ped.IsOnProbation && ped.IsOnParole) return "Probation + parole";
            if (ped.IsOnProbation) return "Probation";
            if (ped.IsOnParole) return "Parole";
            return "No";
        }

        private static string FineRange(CitationGroup.Charge charge) {
            if (charge == null) return "";
            if (charge.minFine == charge.maxFine) return "$" + charge.minFine;
            return "$" + charge.minFine + "-$" + charge.maxFine;
        }

        private static List<CitationGroup> GetCitationOptionGroups() {
            return SetupController.GetCitationOptions()
                .Where(g => g?.charges != null && g.charges.Count > 0)
                .ToList();
        }

        private static string BuildCitationChargeDescription(CitationGroup group, CitationGroup.Charge charge) {
            string category = SafeValue(group?.name, "Citation");
            string fine = FineRange(charge);
            return string.IsNullOrWhiteSpace(fine) ? category : category + " / " + fine;
        }

        private static string JailRange(ArrestGroup.Charge charge) {
            if (charge == null) return "";
            if (charge.maxDays == null) return charge.minDays > 0 ? charge.minDays + "d-life" : "Life";
            if (charge.minDays == charge.maxDays.Value) return charge.minDays + "d";
            return charge.minDays + "-" + charge.maxDays.Value + "d";
        }

        private static string CourtChargeSummary(CourtData.Charge charge) {
            if (charge == null) return "";
            string outcome = CourtChargeOutcome(charge.Outcome);
            string sentence = charge.SentenceDaysServed.HasValue ? " / " + charge.SentenceDaysServed.Value + "d" : "";
            if (charge.Fine > 0) return outcome + " / $" + charge.Fine + sentence;
            if (charge.Time.HasValue && charge.Time.Value > 0) return outcome + " / " + charge.Time.Value + "d" + sentence;
            return outcome + sentence;
        }

        private static string CourtChargeOutcome(int outcome) {
            switch (outcome) {
                case 1: return "Convicted";
                case 2: return "Acquitted";
                case 3: return "Dismissed";
                default: return "Pending";
            }
        }

        private static string CourtStatus(int status) {
            switch (status) {
                case 1: return "Convicted";
                case 2: return "Acquitted";
                case 3: return "Dismissed";
                default: return "Pending";
            }
        }

        private static string BuildCourtCaseSummary(CourtData courtCase) {
            if (courtCase == null) return "Open court case.";
            int chargeCount = courtCase.Charges?.Count ?? 0;
            string hearing = FormatDate(courtCase.HearingDateUtc);
            if (!string.Equals(hearing, "N/A", StringComparison.OrdinalIgnoreCase))
                return chargeCount + " charge" + (chargeCount == 1 ? "" : "s") + " / " + hearing;
            return chargeCount + " charge" + (chargeCount == 1 ? "" : "s");
        }

        private static string FormatDate(string value) {
            if (string.IsNullOrWhiteSpace(value)) return "N/A";
            DateTime parsed;
            if (DateTime.TryParse(value, out parsed))
                return parsed.ToString("yyyy-MM-dd");
            return value.Trim();
        }

        private System.Drawing.Color StatusColor(string value) {
            string s = (value ?? "").ToLowerInvariant();
            if (s.Contains("suspended") || s.Contains("revoked") || s.Contains("expired") || s.Contains("none") || s.Contains("invalid"))
                return _theme.Danger;
            if (s.Contains("valid") || s.Contains("active") || s.Contains("current"))
                return _theme.Success;
            return _theme.Muted;
        }

        private System.Drawing.Color CourtStatusColor(int status) {
            switch (status) {
                case 1: return _theme.Danger;
                case 2:
                case 3: return _theme.Success;
                default: return _theme.Warning;
            }
        }

        private System.Drawing.Color CourtChargeColor(CourtData.Charge charge) {
            if (charge == null) return _theme.Muted;
            switch (charge.Outcome) {
                case 1: return _theme.Danger;
                case 2:
                case 3: return _theme.Success;
                default: return _theme.Muted;
            }
        }

        private static string FormatOfficerUnit(OfficerInformationData officer) {
            if (officer == null) return "Unassigned";
            string callsign = SafeValue(officer.callSign, "").Trim();
            string name = (SafeValue(officer.rank, "") + " " + SafeValue(officer.lastName, "")).Trim();
            string badge = officer.badgeNumber.HasValue ? "#" + officer.badgeNumber.Value : "";
            string combined = (callsign + " " + name + " " + badge).Trim();
            return string.IsNullOrWhiteSpace(combined) ? "Unassigned" : combined;
        }

        private static OfficerInformationData CurrentOfficer() {
            OfficerInformationData saved = DataController.OfficerInformationData;
            OfficerInformationData live = DataController.OfficerInformation;
            if (saved != null && (!string.IsNullOrWhiteSpace(saved.agency) || !string.IsNullOrWhiteSpace(saved.agencyScriptName) || !string.IsNullOrWhiteSpace(saved.callSign)))
                return saved;
            return live ?? saved;
        }

        private static string Ellipsize(string value, int max) {
            if (string.IsNullOrEmpty(value)) return "";
            if (max < 4) max = 4;
            return value.Length <= max ? value : value.Substring(0, max - 3) + "...";
        }
    }
}
