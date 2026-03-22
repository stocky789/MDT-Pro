using LSPD_First_Response.Mod.API;
using Rage;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MDTPro.Utility;
using static MDTPro.Setup.SetupController;
using static MDTPro.Utility.Helper;

namespace MDTPro {
    internal class Main : Plugin {

        public static readonly string Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

        internal static Ped Player => Game.LocalPlayer.Character;
        internal static bool usePR = false;
        internal static bool useCI = false;

        public override void Initialize() {
            LSPD_First_Response.Mod.API.Functions.OnOnDutyStateChanged += Functions_OnOnDutyStateChanged;
            Game.LogTrivial("MDT Pro has been initialized.");
        }

        public override void Finally() {
            UI.SettingsMenu.Stop();
            ALPR.ALPRController.Stop();
            Data.DataController.EndCurrentShift();
            Server.Stop();
            Data.Database.Close();
            ClearCache();
            RageNotification.Show(GetLanguage().inGame.unloaded, RageNotification.NotificationType.Info);
        }

        private static void Functions_OnOnDutyStateChanged(bool OnDuty) {
            if (!OnDuty) {
                UI.SettingsMenu.Stop();
                ALPR.ALPRController.Stop();
                return;
            }
            {
                GameFiber.StartNew(() => {
                    if (!Directory.Exists(MDTProPath)) {
                        RageNotification.ShowError("MDT Pro failed to load. Missing MDT Pro files. Please reinstall and follow the installation instructions.");
                        Game.LogTrivial("MDT Pro: [Error] Loading aborted. Missing MDT Pro files.");
                        return;
                    }

                    if (!DependencyCheck.IsNewtonsoftJsonAvailable()) {
                        RageNotification.ShowError("MDT Pro failed to load. Couldn't find Newtonsoft.Json.");
                        Game.LogTrivial("MDT Pro: [Error] Loading aborted. Couldn't find Newtonsoft.Json.");
                        return;
                    }

                    if (!DependencyCheck.IsCIAPIAvailable()) {
                        RageNotification.ShowError("MDT Pro failed to load. Couldn't find CalloutInterfaceAPI.");
                        Game.LogTrivial("MDT Pro: [Error] Loading aborted. Couldn't find CalloutInterfaceAPI.");
                        return;
                    }

                    if (!DependencyCheck.IsCDFAvailable()) {
                        RageNotification.ShowError("MDT Pro failed to load. Couldn't find CommonDataFramework.");
                        Game.LogTrivial("MDT Pro: [Error] Loading aborted. Couldn't find CommonDataFramework.");
                        return;
                    }

                    if (!UrlAclExists($"http://+:{GetConfig().port}/") && !AddUrlAcl($"http://+:{GetConfig().port}/")) {
                        RageNotification.ShowError("MDT Pro failed to load. Failed to add URL ACL.");
                        Game.LogTrivial("MDT Pro: [Error] Loading aborted. Failed to add URL ACL.");
                        return;
                    }

                    GameFiber.StartNew(() => {
                        GameFiber.WaitUntil(CommonDataFramework.API.CDFFunctions.IsPluginReady, 30000);
                        if (!CommonDataFramework.API.CDFFunctions.IsPluginReady()) {
                            Server.Stop();
                            RageNotification.ShowError("MDT Pro failed to load. CommonDataFramework did not initialize in time.");
                            Log("Loading aborted. CommonDataFramework did not initialize in time.", true, LogSeverity.Error);
                            return;
                        }

                        SetupDirectory();
                        GetLanguage(); // load language file into cache to prevent server thread issues

                        Thread serverThread = new Thread(Server.Start) {
                            IsBackground = true
                        };
                        serverThread.Start();

                        useCI = DependencyCheck.IsCIAvailable();
                        usePR = DependencyCheck.IsPRAvailable();

                        Log($"CI: {useCI}", true, useCI ? LogSeverity.Info : LogSeverity.Warning);
                        Log($"PR: {usePR}", true, usePR ? LogSeverity.Info : LogSeverity.Warning);

                        if (useCI) EventListeners.CalloutEvents.AddCalloutEventWithCI();

                        if (usePR) {
                            EventListeners.PREvents.SubscribeToPREvents();
                            if (GetConfig().firearmDebugLogging)
                                Data.DataController.LogPRAssemblyFirearmDiagnostics();
                        }
                        EventListeners.CDFEvents.Subscribe();
                        // Always subscribe to LSPDFR OnPedArrested: PR's OnPedArrested only fires for arrests through PR.
                        // LSPDFR's fires for all arrests (including those done via LSPDFR or other plugins).
                        EventListeners.LSPDFREvents.SubscribeToLSPDFREvents();

                        RageNotification.ShowSuccess($"{GetLanguage().inGame.loaded} v{Version}");

                        string iniPath = Path.Combine(MDTProPath, "MDTPro.ini");
                        string menuKeyStr = ReadIniValue(iniPath, "MDTPro", "SettingsMenuKey");
                        Keys parsedKey;
                        if (!string.IsNullOrWhiteSpace(menuKeyStr) && Enum.TryParse<Keys>(menuKeyStr.Trim(), true, out parsedKey))
                            UI.SettingsMenu.MenuKey = parsedKey;

                        ALPR.ALPRController.Start();
                        UI.SettingsMenu.Start();

                        var cfg = GetConfig();
                        if (cfg.checkForUpdates && !string.IsNullOrWhiteSpace(cfg.githubReleasesRepo)) {
                            string repo = cfg.githubReleasesRepo;
                            string ver = Version;
                            bool checkEnabled = cfg.checkForUpdates;
                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                                UpdateChecker.CheckForUpdates(ver, repo, checkEnabled));
                        }
                    });
                });
            } 
        }
    }
}