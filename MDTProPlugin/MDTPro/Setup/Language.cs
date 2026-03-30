// Ignore Spelling: Taskbar Newtonsoft Ffl Ccw Yankton Cayo Perico Ip

namespace MDTPro.Setup {
    internal class Language {
        public InGame inGame = new InGame();
        public Index index = new Index();
        public PedSearch pedSearch = new PedSearch();
        public VehicleSearch vehicleSearch = new VehicleSearch();
        public BoloNoticeboard boloNoticeboard = new BoloNoticeboard();
        public FirearmsSearch firearmsSearch = new FirearmsSearch();
        public Values values = new Values();
        public Reports reports = new Reports();
        public Units units = new Units();
        public ShiftHistory shiftHistory = new ShiftHistory();
        public Court court = new Court();
        public Customization customization = new Customization();
        public Map map = new Map();
        public Callout callout = new Callout();
        public Alpr alpr = new Alpr();
        public QuickActions quickActions = new QuickActions();

        public class QuickActions {
            public string panicSuccess = "Panic backup requested.";
            public string backupSuccess = "Backup requested.";
            public string gpsSuccess = "GPS set to callout.";
            public string alprCleared = "ALPR cleared.";
            public string error = "Action failed.";
            /// <summary>Title for the narcotics/drugs cheat sheet popup.</summary>
            public string narcoticsCheatsheet = "Narcotics & Drugs Cheat Sheet";
            /// <summary>Close button label for narcotics cheat sheet.</summary>
            public string narcoticsCheatsheetClose = "Close";
            /// <summary>Shown at the top of the backup Quick Actions menu when Ultimate Backup is the active provider (PR-only actions are hidden).</summary>
            public string backupUltimateBackupNote = "Ultimate Backup is active. Only actions the mod exposes to plugins are shown. Use Code 2 or Code 3 for patrol-style units.";
            /// <summary>Replaces “Response code” when Ultimate Backup is active (Code 1 is hidden).</summary>
            public string backupResponseCodeLabelUb = "Patrol code";
        }

        public class Alpr {
            public string alertTitle = "ALPR Alert";
            public string owner = "Owner";
            public string model = "Model";
            public string openVehicleLookup = "Open Vehicle Lookup";
            public string clearAlpr = "Clear ALPR";
            public string inGameNotEnabled = "In-game ALPR is not enabled.";
        }

        public class InGame {
            public string loaded = "MDT Pro is ready.";
            public string unloaded = "MDT Pro has been shut down.";
            public string listeningOnIpAddress = "MDT Pro is available at: ";
            public string serverFail = "Server failed to start. Restart the game and try again.";
            /// <summary>{0} = installed version, {1} = available version from GitHub. Shown when an update is available.</summary>
            public string updateAvailable = "Installed Version: v{0}~n~Available Version: v{1} - Update Available";
            /// <summary>{0} = version. Shown when already up to date.</summary>
            public string updateUpToDate = "Installed Version: v{0}~n~Available Version: v{0} - Up to Date";
            /// <summary>Shown when a closed citation is saved so the officer can hand it to the suspect. {0} = ped full name.</summary>
            public string handCitationTo = "Hand citation to {0}";
            /// <summary>Shown when citation was saved but the person is not in range or was not identified this stop (e.g. vehicle stop / ID check).</summary>
            public string handCitationPersonNotPresent = "Citation saved. Have the person present and identified (e.g. run the vehicle or ask for ID) so you can hand them the citation from the ped menu.";
            /// <summary>Shown when citation was saved but the person was not found in the database. {0} = offender name.</summary>
            public string handCitationPersonNotFound = "Citation saved for {0}, but they were not found. Make sure the person was identified (e.g. vehicle stop or ID check) and the name matches exactly.";
            /// <summary>Resolved ped handle did not match the citation offender (CDF/persona name differed). {0} = offender name from citation.</summary>
            public string handCitationIdentityMismatch = "Citation saved for {0}, but the ped in front of you doesn't match that person. Identify the right suspect or close the citation again.";
            /// <summary>Appended to StopThePed citation notifications (not used when Policing Redefined handles handoff). {0} = one or two MDT URLs (with ~n~ between if both).</summary>
            public string stpCitationMdtBrowserLine = "~n~~n~Open the MDT in your browser to review or finish:~n~{0}";
            /// <summary>StopThePed: no public ticket API found via reflection.</summary>
            public string stpCitationNoPluginApi = "Citation saved in the MDT. StopThePed did not expose a matching ticket API to plugins — issue the ticket from the StopThePed menu if needed, or use Policing Redefined for direct MDT handoff.";
            /// <summary>StopThePed: reflection handoff failed.</summary>
            public string stpCitationHandoffRejected = "Citation saved in the MDT, but StopThePed did not accept the handoff. Issue the ticket from the StopThePed menu if needed.";
            /// <summary>Subtitle for the in-game citation handoff menu (StopThePed path). {0} = offender name.</summary>
            public string stpCitationHandoffMenuSubtitle = "~b~Citation~s~ — {0}";
            /// <summary>Description on disabled charge lines in the handoff menu.</summary>
            public string stpCitationHandoffChargeDescription = "Charge on this citation.";
            /// <summary>Primary action: confirm handing the citation to the suspect.</summary>
            public string stpCitationHandoffDeliver = "Deliver citation";
            /// <summary>Tooltip for the deliver action.</summary>
            public string stpCitationHandoffDeliverDescription = "Confirms in-game delivery. StopThePed does not expose a plugin ticket API, so MDT Pro completes the handoff here.";
            /// <summary>Shown when the officer closes the handoff menu without choosing Deliver.</summary>
            public string stpCitationHandoffMenuCancelled = "Citation saved in the MDT. You closed the menu without confirming delivery.";
            /// <summary>After closing a citation (StopThePed path). {0} = key name (e.g. F10).</summary>
            public string stpCitationHandoffQueued = "Citation saved. When you are ~g~close to the suspect~s~, press ~b~{0}~s~ to open the handoff menu.";
            /// <summary>Keybind pressed but player too far. {0} = max distance in meters (formatted).</summary>
            public string stpCitationHandoffTooFar = "Move closer to the suspect (within ~{0}m) to hand the citation.";
            /// <summary>Queued handoff was not completed before the time limit.</summary>
            public string stpCitationHandoffPendingExpired = "The pending citation handoff expired. Close the citation again from the MDT if you still need to hand it over.";
            /// <summary>After paperwork animation (if any), suspect reaction line, and short pause — confirms delivery. {0} = first and last name (e.g. from CDF), {1} = formatted total fine (symbol per config).</summary>
            public string citationHandedSuccess = "You successfully handed {0} a citation for {1}";
            /// <summary>Prefix before suspect reaction subtitle after citation handoff. GTA color codes OK (~o~ ~s~).</summary>
            public string citationPedReactionSpeakerPrefix = "~o~Suspect:~s~ ";
            /// <summary>Optional notification when a suspect turns hostile after a citation (empty = silent).</summary>
            public string citationPostHandoffViolenceNotify = "~r~Hostile:~s~ The suspect is attacking!";
        }

        public class Index {
            public Static @static = new Static();
            public Settings settings = new Settings();
            public Notifications notifications = new Notifications();

            public class Static {
                public string title = "MDT Pro";
                public Desktop desktop = new Desktop();
                public Taskbar taskbar = new Taskbar();
                public Settings settings = new Settings();

                public class Desktop {
                    public string pedSearch = "Person Search";
                    public string vehicleSearch = "Vehicle Search";
                    public string boloNoticeboard = "BOLOs";
                    public string firearmsSearch = "Firearms Check";
                    public string reports = "Reports";
                    public string shiftHistory = "Shift History";
                    public string court = "Court";
                    public string map = "GPS";
                    public string callout = "Active Call";
                }

                public class Taskbar {
                    public string settings = "Control Panel";
                }

                public class Settings {
                    public string customization = "Config and Plugins";
                    public string customizationInfo = "Change config and manage installed plugins. Opens in a new tab.";
                    public OfficerInformation officerInformation = new OfficerInformation();
                    public CurrentShift currentShift = new CurrentShift();
                    public OfficerMetrics officerMetrics = new OfficerMetrics();

                    public class OfficerInformation {
                        public string title = "Officer Information";
                        public string firstName = "First Name";
                        public string lastName = "Last Name";
                        public string rank = "Rank";
                        public string callSign = "Call Sign";
                        public string agency = "Department";
                        public string badgeNumber = "Badge Number";
                        public string autoFill = "Fill from Game";
                        public string save = "Save";
                        public Info info = new Info();
                        public class Info {
                            public string title = "Your character details. Used to pre-fill reports and show who is on duty.";
                            public string firstName = "Your character's first name.";
                            public string lastName = "Your character's last name.";
                            public string badgeNumber = "Your badge or employee number.";
                            public string rank = "e.g. Officer, Sergeant, Lieutenant.";
                            public string callSign = "Radio call sign or unit number (e.g. Adam-12).";
                            public string agency = "Your department or agency name.";
                            public string autoFill = "Pull your current character info from the game (LSPDFR).";
                            public string save = "Save these details to the MDT. They will be used on reports and when you start a shift.";
                        }
                    }

                    public class CurrentShift {
                        public string title = "Current Shift";
                        public string startShift = "Start Shift";
                        public string endShift = "End Shift";
                        public Info info = new Info();
                        public class Info {
                            public string title = "Track your on-duty time. Start when you go on patrol, end when you finish.";
                            public string startShift = "Mark the start of your shift. Your info above is shown in notifications.";
                            public string endShift = "End your current shift. Duration is saved to your statistics.";
                        }
                    }

                    public class OfficerMetrics {
                        public string title = "Career Statistics";
                        public Info info = new Info();
                        public class Info {
                            public string title = "Totals from your completed shifts and reports. Read-only.";
                        }
                    }
                }
            }

            public class Settings {
                public string version = "Version";
                public CurrentShift currentShift = new CurrentShift();
                public OfficerMetrics officerMetrics = new OfficerMetrics();

                public class CurrentShift {
                    public string startTime = "Start";
                    public string duration = "Duration";
                    public string offDuty = "Off duty";
                }

                public class OfficerMetrics {
                    public string totalShifts = "Total Shifts";
                    public string avgDuration = "Avg. Shift Duration";
                    public string incidents = "Incidents";
                    public string citations = "Citations";
                    public string arrests = "Arrests";
                    public string totalReports = "Total Reports";
                    public string reportsPerShift = "Reports per Shift";
                }
            }

            public class Notifications {
                public string webSocketOnClose = "Connection lost. Reload the page to reconnect.";
                public string officerInformationSaved = "Officer information saved.";
                public string officerInformationError = "Failed to save officer information.";
                public string currentShiftStartedOfficerInformationExists = "Now on duty as";
                public string currentShiftStarted = "Shift started.";
                public string currentShiftStartedError = "Failed to start shift.";
                public string currentShiftEnded = "Shift ended.";
                public string currentShiftEndedError = "Failed to end shift.";
            }
        }

        public class PedSearch {
            public Static @static = new Static();
            public Notifications notifications = new Notifications();
            public string createInjuryReport = "New Injury Report";

            public class Static {
                public string title = "Person Search";
                public string search = "Search";
                public string searchInput = "Enter name to search";
                public string basicInfoTitle = "Basic Information";
                public string legalInfoTitle = "Legal Information";
                public string licensesTitle = "Licenses & Permits";
                public string historyTitle = "History";
                public string noPhoto = "No photo available";
                public string vehiclesOwnedTitle = "Vehicles Owned";
                public string registeredFirearmsTitle = "Registered Firearms";
                public string reportsTitle = "Associated Reports";
                public Labels labels = new Labels();

                public class Labels {
                    public string idPhoto = "Photo";
                    public string firstName = "First Name";
                    public string lastName = "Last Name";
                    public string gender = "Gender";
                    public string birthday = "Date Of Birth";
                    public string age = "Age";
                    public string address = "Address";
                    public string gangAffiliation = "Gang Affiliation";
                    public string advisory = "Advisory";
                    public string wantedStatus = "Warrant Status";
                    public string timesStopped = "Times Stopped";
                    public string probationStatus = "Probation Status";
                    public string paroleStatus = "Parole Status";
                    public string licenseStatus = "Driving License Status";
                    public string licenseExpiration = "Driving License Expiration";
                    public string weaponPermitStatus = "Weapon Permit Status";
                    public string weaponPermitExpiration = "Weapon Permit Expiration";
                    public string fishingPermitStatus = "Fishing Permit Status";
                    public string fishingPermitExpiration = "Fishing Permit Expiration";
                    public string huntingPermitStatus = "Hunting Permit Status";
                    public string huntingPermitExpiration = "Hunting Permit Expiration";
                    public string citations = "Citations";
                    public string arrests = "Arrests";
                }
            }

            public class Notifications {
                public string emptySearchInput = "Enter a name to search.";
                public string pedNotFound = "No person found with that name.";
            }
        }

        public class FirearmsSearch {
            public Static @static = new Static();
            public Notifications notifications = new Notifications();

            public class Static {
                public string title = "Firearms Check";
                public string search = "Search";
                public string searchInput = "Serial number or owner name";
                public string resultTitle = "Result";
                public string recentIdsTitle = "Recent IDs";
            }

            public class Notifications {
                public string emptySearchInput = "Enter serial number or owner name.";
                public string notFound = "No firearm or owner found.";
                public string searchError = "Search failed. Please try again.";
            }
        }

        public class VehicleSearch {
            public Static @static = new Static();
            public Notifications notifications = new Notifications();

            public class Static {
                public string title = "Vehicle Search";
                public string search = "Search";
                public string searchInput = "Enter plate or VIN";
                public string nearbyPlatesTitle = "Nearby Vehicles";
                public string refreshNearby = "Refresh";
                public string basicInfoTitle = "Basic Information";
                public string documentsTitle = "Documents";
                public string bolosTitle = "BOLOs (Be On the Look-Out)";
                public string boloVehicleRequired = "Vehicle must be nearby to add or remove BOLOs.";
                public string addBOLO = "Add BOLO";
                public string removeBOLO = "Remove";
                public string boloReasonPrompt = "Enter BOLO reason:";
                public string boloExpiresPrompt = "Expires in how many days? (default 7):";
                public string searchResultsTitle = "Search Results (Contraband)";
                public string impoundReportsTitle = "Impound Reports";
                public string createImpoundReport = "Create Impound Report";
                public Labels labels = new Labels();

                public class Labels {
                    public string licensePlate = "License Plate";
                    public string modelDisplayName = "Display name";
                    public string model = "Model (CDF)";
                    public string make = "Make";
                    public string owner = "Owner";
                    public string isStolen = "Stolen";
                    public string color = "Color";
                    public string primaryColor = "Primary color";
                    public string secondaryColor = "Secondary color";
                    public string primaryColorGta = "Primary (GTA)";
                    public string secondaryColorGta = "Secondary (GTA)";
                    public string vinStatus = "VIN status";
                    public string vehicleModel = "Vehicle Model";
                    public string registrationStatus = "Registration Status";
                    public string registrationExpiration = "Registration Expiration";
                    public string insuranceStatus = "Insurance Status";
                    public string insuranceExpiration = "Insurance Expiration";
                }
            }

            public class Notifications {
                public string emptySearchInput = "Enter a license plate or VIN.";
                public string vehicleNotFound = "No vehicle found.";
                public string noNearbyVehicles = "No vehicles detected nearby.";
                public string stolen = "WARNING";
                public string vehicleStolen = "Vehicle";
                public string reportedStolen = "is in the stolen vehicle database";
                public string boloAdded = "BOLO added.";
                public string boloRemoved = "BOLO removed.";
            }
        }

        public class BoloNoticeboard {
            public Static @static = new Static();
            public string stolenBadge = "STOLEN";
            public string viewInVehicleSearch = "View in Vehicle Search";
            public string expires = "Expires";
            public string boloCreated = "BOLO created.";
            public class Static {
                public string title = "BOLO Noticeboard";
                public string subtitle = "Be On the Look-Out — vehicles to watch for";
                public string refresh = "Refresh";
                public string createBOLO = "Create BOLO";
                public string noBolos = "No active BOLOs. Add BOLOs from Vehicle Search when a vehicle is nearby, or create one above.";
                public string createBOLOTitle = "Create BOLO";
                public string createBOLOSubtitle = "Enter vehicle and BOLO details. Vehicle does not need to be nearby.";
                public string createBOLOPlate = "License Plate *";
                public string createBOLOModel = "Vehicle Model (optional)";
                public string createBOLOReason = "Reason *";
                public string createBOLOExpires = "Expires in (days)";
                public string createBOLOSubmit = "Create BOLO";
                public string cancel = "Cancel";
            }
        }

        public class Values {
            public string wanted = "Wanted";
            public string notWanted = "Clear";
            public string @true = "Yes";
            public string @false = "No";
            public string empty = "N/A";
            public string Valid = "Valid";
            public string Expired = "Expired";
            public string Revoked = "Revoked";
            public string Suspended = "Suspended";
            public string Unlicensed = "Unlicensed";
            public string None = "None";
            public string CcwPermit = "Carrying a concealed weapon (CCW)";
            public string CCWPermit = "CCW Permit";
            public string FflPermit = "Federal firearms license (FFL)";
            public string Government = "Government";
            public string LosSantos = "Los Santos";
            public string LosSantosCounty = "Los Santos County";
            public string BlaineCounty = "Blaine County";
            public string SanAndreas = "San Andreas";
            public string NorthYankton = "North Yankton";
            public string CayoPerico = "Cayo Perico";
        }

        public class Reports {
            public string newReportTitle = "New Report";
            public string editReportTitle = "Edit Report";
            public string[] statusMap = {
                "Closed",
                "Open",
                "Canceled",
                "Pending"
            };
            public Static @static = new Static();
            public Notifications notifications = new Notifications();
            public Sections sections = new Sections();
            public IdTypeMap idTypeMap = new IdTypeMap();
            public List list = new List();

            public class Static {
                public string title = "Reports";
                public ListPage listPage = new ListPage();
                public CreatePage createPage = new CreatePage();

                public class ListPage {
                    public string createButton = "New Report";
                    public ReportType reportType = new ReportType();

                    public class ReportType {
                        public string incident = "Incident Reports";
                        public string citation = "Citation Reports";
                        public string arrest = "Arrest Reports";
                        public string impound = "Impound Reports";
                        public string trafficIncident = "Traffic Incident Reports";
                        public string injury = "Injury Reports";
                        public string propertyEvidence = "Property and Evidence Receipts";
                    }
                }

                public class CreatePage {
                    public string saveButton = "Save";
                    public string cancelButton = "Cancel";
                    public ReportType reportType = new ReportType();

                    public class ReportType {
                        public string select = "Select Report Type";
                        public string incident = "Incident Report";
                        public string citation = "Citation Report";
                        public string arrest = "Arrest Report";
                        public string impound = "Impound Report";
                        public string trafficIncident = "Traffic Incident Report";
                        public string injury = "Injury Report";
                        public string propertyEvidence = "Property and Evidence Receipt";
                    }
                }
            }

            public class Notifications {
                public string createPageAlreadyOpen = "Finish or cancel the current report first.";
                public string invalidPedName = "No person found with this name.";
                public string invalidVehicleLicensePlate = "No vehicle found with this plate.";
                public string saveSuccess = "Report saved.";
                public string saveError = "Failed to save report.";
                public string invalidTimeStamp = "Invalid date or time.";
                /// <summary>Title for the caution dialog shown after saving an arrest report.</summary>
                public string arrestSaveCautionTitle = "Reminder";
                /// <summary>Caution text reminding the player to attach relevant reports to the arrest for court evidence.</summary>
                public string arrestSaveCautionMessage = "Remember to attach relevant reports (e.g. incident, injury) to this arrest report. The arrest report alone may not be enough evidence to secure a conviction in court—this depends on the case.";
                public string invalidTime = "Invalid time.";
                public string invalidDate = "Invalid date.";
                public string noCharges = "Add at least one charge.";
                public string noOffender = "Offender name required.";
                public string prefilledFromPersonSearch = "Prefilled from Person Search";
                public string prefilledFromVehicleSearch = "Prefilled from Vehicle Search";
                public string prefilledFromArrest = "Prefilled from arrest. Report will be attached after save.";
                public string savedAndAttachedToArrest = "Report saved and attached to arrest.";
                public string closeArrestSuccess = "Arrest closed and submitted for court.";
                public string closeArrestError = "Failed to close arrest.";
            }

            public class Sections {
                public string notes = "Notes";
                public string fine = "Fine";
                public string incarceration = "Incarceration";
                public GeneralInformation generalInformation = new GeneralInformation();
                public Location location = new Location();
                public OfficerInformation officerInformation = new OfficerInformation();
                public Incident incident = new Incident();
                public Offender offender = new Offender();
                public Citation citation = new Citation();
                public Arrest arrest = new Arrest();

                public class GeneralInformation {
                    public string title = "General Information";
                    public string date = "Date";
                    public string time = "Time";
                    public string reportId = "Report ID";
                    public string status = "Status";
                    public string copyReportId = "Copy";
                    public string copiedToClipboard = "Report ID copied to clipboard.";
                    public string copyFailed = "Could not copy.";
                }

                public class Location {
                    public string title = "Location";
                    public string area = "Area";
                    public string street = "Street";
                    public string county = "County";
                    public string postal = "Postal Code";
                }

                public class OfficerInformation {
                    public string title = "Officer Information";
                    public string firstName = "First Name";
                    public string lastName = "Last Name";
                    public string rank = "Rank";
                    public string callSign = "Call Sign";
                    public string agency = "Agency";
                    public string badgeNumber = "Badge Number";
                }

                public class Incident {
                    public string titleOffenders = "Offenders";
                    public string titleWitnesses = "Witnesses & Victims";
                    public string labelOffenders = "Offender Name";
                    public string labelWitnesses = "Witness Name";
                    public string addOffender = "Add Offender";
                    public string addWitness = "Add Witness";
                    public string removeOffender = "Remove";
                    public string removeWitness = "Remove";
                }

                public class Offender {
                    public string title = "Offender";
                    public string pedName = "Name";
                    public string vehicleLicensePlate = "Vehicle Plate";
                }

                public class Citation {
                    public string title = "Citation Charges";
                    public string searchChargesPlaceholder = "Search charges";
                }

                public class Arrest {
                    public string title = "Arrest Charges";
                    public string searchChargesPlaceholder = "Search charges";
                    public string evidenceSeized = "Evidence seized";
                    public string evidenceSeizedHelp = "Document drugs and firearms via a Property and Evidence Receipt report. Attach it below to support court evidence.";
                    public string documentSeizedContraband = "Document seized contraband";
                    public string createPropertyEvidenceReceipt = "Create Property and Evidence Receipt";
                    public string importRecentReports = "Import recent reports";
                    public string importRecentReportsHelp = "Attaches reports created in the last 60 minutes that involve the arrested person (incident, injury, citation, traffic, impound with person at fault, property/evidence).";
                    public string importRecentReportsNone = "No new recent reports to import (last 60 min).";
                    public string attachedReports = "Attached reports (evidence for court)";
                    /// <summary>Explains that attached reports count as evidence; relevant ones carry full weight, others still count but less.</summary>
                    public string attachedReportsHelp = "Reports you attach here are used as evidence when this arrest goes to court. Reports that directly support the case (Incident/Citation naming this defendant, Injury documenting harm, Traffic Incident with defendant as driver or vehicle-related charges, Impound for vehicle-related charges) carry full weight. Other attached reports (e.g. impound on a drug case, incident that doesn't name the defendant) still count but carry less weight—so tangential evidence like a stolen firearm in a drug case is not ignored.";
                    public string attachReport = "Attach report";
                    public string attachReportIdPlaceholder = "Report ID (e.g. INC-25-0001, INJ-25-0001)";
                    public string attachReportDraftHint = "Report will be attached when you save the arrest.";
                    public string detach = "Detach";
                    public string closeArrestSubmit = "Save and close (submit for court)";
                }

                public UseOfForce useOfForce = new UseOfForce();

                public Impound impound = new Impound();
                public TrafficIncident trafficIncident = new TrafficIncident();
                public Injury injury = new Injury();
                public PropertyEvidence propertyEvidence = new PropertyEvidence();

                public class Impound {
                    public string title = "Vehicle & Impound Details";
                    public string personAtFault = "Person at fault";
                    public string selectFromRecentIds = "Select person at fault (Recent IDs)";
                    public string noRecentIds = "No recent IDs. Collect an ID from a ped to show them here.";
                    public string recentIdsError = "Could not load Recent IDs.";
                    public string nearbyVehiclesTitle = "Nearby vehicles";
                    public string refreshNearby = "Refresh";
                    public string noNearbyVehicles = "No vehicles detected nearby.";
                    public string prefilledFromNearby = "Vehicle details filled from nearby.";
                    public string licensePlate = "License Plate";
                    public string model = "Model";
                    public string owner = "Owner";
                    public string vin = "VIN";
                    public string impoundReason = "Impound Reason";
                    public string towCompany = "Tow Company";
                    public string impoundLot = "Impound Lot";
                }

                public class TrafficIncident {
                    public string title = "Traffic Incident Details";
                    public string drivers = "Drivers";
                    public string driver = "Driver";
                    public string addDriver = "Add driver";
                    public string removeDriver = "Remove";
                    public string passengers = "Passengers";
                    public string passenger = "Passenger";
                    public string addPassenger = "Add passenger";
                    public string removePassenger = "Remove";
                    public string pedestrians = "Pedestrians";
                    public string pedestrian = "Pedestrian";
                    public string addPedestrian = "Add pedestrian";
                    public string removePedestrian = "Remove";
                    public string vehicles = "Vehicles";
                    public string vehiclePlate = "Plate";
                    public string addVehicle = "Add vehicle";
                    public string removeVehicle = "Remove";
                    public string vehicleModels = "Vehicle Models";
                    public string model = "Model";
                    public string addModel = "Add model";
                    public string removeModel = "Remove";
                    public string injuryReported = "Injury reported";
                    public string injuryDetails = "Injury details";
                    public string collisionType = "Collision type";
                }

                public class Injury {
                    public string title = "Injury Details";
                    public string injuredParty = "Injured party";
                    public string injuryType = "Injury type";
                    public string severity = "Severity";
                    public string treatment = "Treatment";
                    public string incidentContext = "Incident context";
                    public string linkedReportId = "Linked report ID";
                    public string selectFromRecentIds = "Select injured party (Recent IDs)";
                    public string noRecentIds = "No recent IDs. Collect an ID from a ped (e.g. traffic stop) to show them here.";
                    public string recentIdsError = "Could not load Recent IDs.";
                }

                public class PropertyEvidence {
                    public string title = "Property and Evidence Details";
                    public string subjectsTitle = "Subjects (persons from whom seized)";
                    public string subjectPedName = "Subject (person from whom seized)";
                    public string subjectPedNamePlaceholder = "Full name";
                    public string selectFromRecentIds = "Select from Recent IDs";
                    public string noRecentIds = "No recent IDs. Collect an ID from a ped to show them here.";
                    public string recentIdsError = "Could not load Recent IDs.";
                    public string addSubjectPlaceholder = "Add subject name";
                    public string addSubject = "Add";
                    public string drugsSeized = "Drugs seized";
                    public string addDrugsHelp = "Select drug type and quantity, then click Add";
                    public string firearmsSeized = "Firearms seized";
                    public string addFirearmsHelp = "Select firearm type, then click Add";
                    public string add = "Add";
                    public string otherContrabandNotes = "Other contraband (optional)";
                    public string otherContrabandPlaceholder = "Describe other items seized";
                    public string seizedSummary = "Seized";
                    public string attachPropertyReceiptHint = "Attach a Property and Evidence Receipt to document seized contraband.";
                }

                public class UseOfForce {
                    public string title = "Use of Force";
                    public string type = "Type";
                    public string typeOther = "Type (if Other)";
                    public string justification = "Justification";
                    public string justificationPlaceholder = "Describe circumstances requiring use of force";
                    public string injuryToSuspect = "Injury to suspect";
                    public string injuryToOfficer = "Injury to officer";
                    public string witnesses = "Witnesses";
                }
            }

            public class IdTypeMap {
                public string incident = "I";
                public string citation = "C";
                public string arrest = "A";
                public string impound = "IMP";
                public string trafficIncident = "TIR";
                public string injury = "INJ";
                public string propertyEvidence = "PER";
            }

            public class List {
                public string viewButton = "View";
                public string editButton = "Edit";
                public string empty = "No reports yet.";
                public string reportId = "Report ID";
                public string date = "Date";
                public string location = "Location";
                public string involvedParties = "Involved Parties";
                public string offender = "Offender";
                public string vehicle = "Vehicle";
                public string finalAmount = "Final Amount";
                public Filter filter = new Filter();

                public class Filter {
                    public string title = "Filter";
                    public string searchPlaceholder = "Search";
                }
            }
        }

        public class Units {
            public string year = "y";
            public string month = "mo";
            public string day = "d";
            public string hour = "h";
            public string minute = "m";
            public string second = "s";
            public string currencySymbol = "$";
            public string life = "Life";
            public string meters = "m";
            public string kilometers = "km";
            public string feet = "ft";
            public string miles = "mi";
        }

        public class ShiftHistory {
            public string empty = "No shift history.";
            public string reports = "Reports";
            public Static @static = new Static();

            public class Static {
                public string title = "Shift History";
            }
        }

        public class Court {
            /// <summary>Shown as in-game notification when a trial is auto-resolved. {0} = case number, {1} = defendant name.</summary>
            public string trialHeardNotification = "Trial {0} for {1} has been heard - to see the outcome check the MDT.";
            public string empty = "No court cases.";
            public string charges = "Charges";
            public string number = "Case Number";
            public string pedName = "Offender Name";
            public string report = "Report";
            public string totalFine = "Total Fine";
            public string fine = "Fine";
            public string totalIncarceration = "Total Incarceration";
            public string incarceration = "Incarceration";
            public string status = "Status";
            public string statusUpdated = "Case updated.";
            public string statusUpdateError = "Update failed.";
            public string forceResolve = "Force Resolve";
            public string forceResolveSuccess = "Case resolved.";
            public string forceResolveError = "Could not resolve case.";
            public string forceResolveProcessing = "Processing case...";
            public string[] forceResolveStages = {
                "Submitting case to court...",
                "Prosecution and defense present...",
                "Judge deliberating...",
                "Verdict being entered...",
                "Finalizing case record..."
            };
            public string saveCase = "Save Plea & Notes";
            public string saveCaseSuccess = "Case updated.";
            public string saveCaseError = "Failed to save case.";
            public string searchPlaceholder = "Search by case #, name, or report";
            public string allStatuses = "All statuses";
            public string[] statusMap = {
                "Pending",
                "Convicted",
                "Acquitted",
                "Dismissed"
            };
            public string sortUpdated = "Recently Updated";
            public string sortRisk = "Highest Risk";
            public string sortYear = "Newest First";
            public string sectionCaseProfile = "Case Profile";
            public string sectionScoring = "Scoring & Sentencing";
            public string sectionAdversarial = "Trial Model";
            public string sectionDisposition = "Disposition";
            public string repeatOffenderScore = "Repeat Offender Score";
            public string sentenceMultiplier = "Sentence Multiplier";
            public string hearingDate = "Hearing Date";
            public string courtDistrict = "Court District";
            public string courtName = "Court";
            public string judge = "Judge";
            public string severityScore = "Severity Score";
            public string evidenceScore = "Evidence Score";
            public string evidenceWeapon = "Armed at Arrest";
            public string evidenceWanted = "Active Warrant at Encounter";
            public string evidenceAssault = "Assaulted Another Person";
            public string evidenceVehicleDamage = "Damaged Vehicle / Property";
            public string evidenceResisted = "Resisted Arrest";
            public string evidenceDrugs = "Drugs Found on Person";
            public string evidenceUseOfForce = "Use of Force Documented";
            public string evidenceDrunk = "Intoxicated at Encounter";
            public string evidenceFleeing = "Attempted to Flee";
            public string evidenceSupervision = "Supervision Violation";
            public string evidencePatDown = "Pat-Down / Search";
            public string evidenceIllegalWeapon = "Illegal Weapon";
            public string prosecutionStrength = "Prosecution Strength";
            public string defenseStrength = "Defense Strength";
            public string docketPressure = "Docket Pressure";
            public string policyAdjustment = "District Policy Adjustment";
            public string evidenceModelExplanation = "Evidence score estimates prosecution strength from charge severity, arrestability, and sentencing exposure. Calculated when the case is created and stored with the case.";
            public string jury = "Jury";
            public string benchTrial = "Bench Trial";
            public string plea = "Plea";
            public string[] pleaMap = {
                "Not Guilty",
                "Guilty",
                "No Contest"
            };
            public string outcomeNotes = "Outcome Notes";
            public string outcomeReasoning = "Verdict & Outcome Reasoning";
            public string sentenceReasoning = "Sentencing Rationale";
            public string licenseRevocations = "License Revocations Ordered";
            public string attachedReports = "Attached reports (evidence)";
            /// <summary>Explains that relevant reports carry full weight, others still count but less.</summary>
            public string attachedReportsHelp = "Attached reports count as evidence. Those that directly support the case (defendant named, or report type matches charges) carry full weight; other attached reports still count but carry less weight, so tangential evidence is not ignored.";
            public string attachReportToCase = "Attach report to case";
            public string attachReportIdPlaceholder = "Report ID";
            public string detach = "Detach";
            public string chargeOutcomeConvicted = "Convicted";
            public string chargeOutcomeAcquitted = "Acquitted";
            public string chargeOutcomePending = "Pending";
            public string chargeOutcomeDismissed = "Dismissed";
            public string chargeOutcome = "Outcome";
            public string courtDate = "Court";
            public string trialDate = "Trial";
            public string caseTimeline = "Case Timeline";
            public string createdAt = "Created";
            public string lastUpdated = "Last Updated";
            public string resolveAt = "Court Date";
            public string prosecutor = "Prosecutor";
            public string defenseAttorney = "Defense Attorney";
            public string defendant = "Defendant";
            public string courtDistrictCol = "Court District";
            public string dateCol = "Date";
            public string pleaTooltipGuilty = "Guilty: Waives right to trial; conviction entered.";
            public string pleaTooltipNoContest = "No Contest: Same effect as guilty; no admission of guilt.";
            public string pleaTooltipNotGuilty = "Not Guilty: Proceeds to trial.";
            public string docketPressureTooltip = "Court workload factor; high docket pressure may increase likelihood of plea deals.";
            public string policyAdjustmentTooltip = "District policy modifier affecting sentencing or outcome.";
            public string prosecutionStrengthTooltip = "Prosecution's estimated strength based on evidence and case factors.";
            public string defenseStrengthTooltip = "Defense's estimated strength based on representation and case factors.";
            public string evidenceBandLow = "Low";
            public string evidenceBandMedium = "Medium";
            public string evidenceBandStrong = "Strong";
            public string evidenceBandLowNote = "Limited physical evidence – case may rely on officer testimony.";
            public string exhibitFirearm = "Firearm recovered";
            public string exhibitDrugs = "Drugs";
            public string exhibitArrestReport = "Arrest report";
            public string exhibitWarrant = "Active warrant documentation";
            public string exhibitAssault = "Assault evidence";
            public string exhibitVehicleDamage = "Vehicle/property damage evidence";
            public string exhibitResistance = "Resistance evidence";
            public string exhibitUseOfForce = "Use of force documentation";
            public string exhibitIntoxication = "Intoxication evidence";
            public string exhibitFleeing = "Fleeing evidence";
            public string exhibitSupervision = "Supervision violation";
            public string exhibitPatDown = "Pat-down/search evidence";
            public string exhibitIllegalWeapon = "Illegal weapon evidence";
            public string reportTypeIncident = "Incident";
            public string reportTypeCitation = "Citation";
            public string reportTypeArrest = "Arrest";
            public string reportTypeImpound = "Impound";
            public string reportTypeTrafficIncident = "Traffic Incident";
            public string reportTypeInjury = "Injury";
            public string reportTypePropertyEvidence = "Property & Evidence";
            public Static @static = new Static();

            public class Static {
                public string title = "Court";
            }
        }

        public class Customization {
            public string save = "Save";
            public string reset = "Reset";
            public Static @static = new Static();
            public Plugins plugins = new Plugins();

            public class Static {
                public string title = "Customization";
                public Sidebar sidebar = new Sidebar();

                public class Sidebar {
                    public string plugins = "Plugins";
                    public string config = "Config";
                }
            }

            public class Plugins {
                public string version = "Version";
                public string author = "Author";
                public string noPlugins = "No plugins installed.";
            }
        }

        public class Map {
            public string zoomIn = "Zoom In";
            public string zoomOut = "Zoom Out";
            public Static @static = new Static();
            public RouteInstructions routeInstructions = new RouteInstructions();

            public class Static {
                public string title = "GPS";
            }

            public class RouteInstructions {
                public string turnLeft = "In {distance}, turn left onto {street}";
                public string turnRight = "In {distance}, turn right onto {street}";
                public string streetChange = "In {distance}, continue on {street}";
                public string arrive = "Arriving at destination in {distance}";
                public string defaultStreet = "unnamed road";
            }
        }

        public class Callout {
            public string defaultPriority = "Code 2";
            public string noActiveCall = "No active callout";
            public Static @static = new Static();
            public CalloutInfo calloutInfo = new CalloutInfo();
            public Actions actions = new Actions();
            public Status status = new Status();

            public class Actions {
                public string setGps = "Set GPS";
                public string gpsSuccess = "GPS set to callout.";
                public string accept = "Accept";
                public string success = "Status updated.";
                public string error = "Action failed.";
            }

            public class Status {
                public string pending = "Pending";
                public string responded = "Responded";
                public string enRoute = "En Route";
                public string finished = "Finished";
                public string unknown = "—";
                public string displayed = "Displayed";
            }

            public class Static {
                public string title = "Callout";
                public string address = "Address";
                public string area = "Area";
                public string county = "County";
                public string priority = "Priority";
            }

            public class CalloutInfo {
                public string displayedTime = "Dispatched ";
                public string unit = "Unit ";
                public string acceptedTime = " — Assigned ";
                public string finishedTime = "Cleared ";
                public string message = "Message";
                public string advisory = "Advisory";
            }
        }
    }
}
