// Ignore Spelling: Taskbar Newtonsoft Ffl Ccw Yankton Cayo Perico Ip

namespace MDTPro.Setup {
    internal class Language {
        public InGame inGame = new InGame();
        public Index index = new Index();
        public PedSearch pedSearch = new PedSearch();
        public VehicleSearch vehicleSearch = new VehicleSearch();
        public Values values = new Values();
        public Reports reports = new Reports();
        public Units units = new Units();
        public ShiftHistory shiftHistory = new ShiftHistory();
        public Court court = new Court();
        public Customization customization = new Customization();
        public Map map = new Map();
        public Callout callout = new Callout();
        public Alpr alpr = new Alpr();

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
            public string updateAvailable = "A newer version (v{0}) is available. Download from GitHub releases.";
            /// <summary>Shown when a closed citation is saved so the officer can hand it to the suspect. {0} = ped full name.</summary>
            public string handCitationTo = "Hand citation to {0}";
            /// <summary>Shown when citation was saved but the person is not in range or was not identified this stop (e.g. vehicle stop / ID check).</summary>
            public string handCitationPersonNotPresent = "Citation saved. Have the person present and identified (e.g. run the vehicle or ask for ID) so you can hand them the citation from the ped menu.";
            /// <summary>Shown when citation was saved but the person was not found in the database. {0} = offender name.</summary>
            public string handCitationPersonNotFound = "Citation saved for {0}, but they were not found. Make sure the person was identified (e.g. vehicle stop or ID check) and the name matches exactly.";
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

            public class Static {
                public string title = "Person Search";
                public string search = "Search";
                public string searchInput = "Enter name to search";
                public string basicInfoTitle = "Basic Information";
                public string legalInfoTitle = "Legal Information";
                public string licensesTitle = "Licenses & Permits";
                public string historyTitle = "History";
                public Labels labels = new Labels();

                public class Labels {
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
                public Labels labels = new Labels();

                public class Labels {
                    public string licensePlate = "License Plate";
                    public string owner = "Owner";
                    public string isStolen = "Stolen";
                    public string color = "Color";
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
                "Canceled"
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
                public string invalidTime = "Invalid time.";
                public string invalidDate = "Invalid date.";
                public string noCharges = "Add at least one charge.";
                public string noOffender = "Offender name required.";
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
                }
            }

            public class IdTypeMap {
                public string incident = "I";
                public string citation = "C";
                public string arrest = "A";
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
            public string evidenceResisted = "Resisted Arrest";
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
            public string outcomeReasoning = "Outcome Reasoning";
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
            public Static @static = new Static();
            public CalloutInfo calloutInfo = new CalloutInfo();

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
            }
        }
    }
}
