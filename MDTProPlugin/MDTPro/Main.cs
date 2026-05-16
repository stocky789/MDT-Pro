using LSPD_First_Response.Mod.API;
using Rage;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MDTPro.EventListeners;
using MDTPro.Cloud;
using MDTPro.UI.InGameComputer;
using MDTPro.Setup;
using MDTPro.Utility;
using MDTPro.Utility.Backup;
using static MDTPro.Setup.SetupController;
using static MDTPro.Utility.Helper;

namespace MDTPro {
    internal class Main : Plugin {

        public static readonly string Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

        internal static Ped Player => Game.LocalPlayer.Character;
        internal static bool usePR = false;
        internal static bool useSTP = false;
        internal static bool useCI = false;

        public override void Initialize() {
            LSPD_First_Response.Mod.API.Functions.OnOnDutyStateChanged += Functions_OnOnDutyStateChanged;
            Game.LogTrivial("MDT Pro has been initialized.");
        }

        public override void Finally() {
            CloudPluginBridge.TryEndTrackedLspdfrShiftBlocking();
            CloudIngameEntry.RequestAbortFromHost();
            InGameComputerEntry.RequestAbortFromHost();
            ShutdownRuntime(isFinalUnload: true);
            Data.Database.Close();
            ClearCache();
            RageNotification.Show(GetLanguage().inGame.unloaded, RageNotification.NotificationType.Info);
        }

        private static void Functions_OnOnDutyStateChanged(bool OnDuty) {
            if (!OnDuty) {
                CloudPluginBridge.TryEndTrackedLspdfrShiftBlocking();
                Exception bridgeEndErr;
                bool ranEndShift = GameFiberHttpBridge.TryExecuteBlocking(() => {
                    try { Data.DataController.EndCurrentShift(); }
                    catch (Exception ex) { Log($"EndCurrentShift: {ex.Message}", false, LogSeverity.Warning); }
                }, 5000, out bridgeEndErr);
                if (!ranEndShift)
                    Log("EndCurrentShift timed out on off-duty (game busy or bridge stopped).", false, LogSeverity.Warning);
                if (bridgeEndErr != null)
                    Log($"EndCurrentShift bridge: {bridgeEndErr.Message}", false, LogSeverity.Warning);
                ShutdownRuntime(isFinalUnload: false);
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

                        // Release port and end prior listener thread (e.g. after off-duty without plugin unload, or RPH reload).
                        Server.Stop();
                        GameFiber.Wait(400);

                        SetupController.EnableBackgroundWorkers();
                        GameFiberHttpBridge.Start();
                        GameWorkScheduler.Start();
                        GameFiber.Wait(50);

                        Thread serverThread = new Thread(Server.Start) {
                            IsBackground = true
                        };
                        serverThread.Start();

                        useCI = DependencyCheck.IsCIAvailable();
                        usePR = DependencyCheck.IsPRAvailable();
                        useSTP = DependencyCheck.IsStopThePedAvailable();

                        ModIntegration.InitializeOnDuty(usePR, useSTP);
                        BackupService.ReloadFromConfig();

                        Log($"CI: {useCI}", true, useCI ? LogSeverity.Info : LogSeverity.Warning);
                        Log($"PR: {usePR}", true, usePR ? LogSeverity.Info : LogSeverity.Warning);
                        Log($"STP: {useSTP}", true, useSTP ? LogSeverity.Info : LogSeverity.Warning);

                        {
                            Config c = GetConfig();
                            Game.LogTrivial(
                                "MDT Pro gameWork=" + (c.gameWorkMode ?? "Performance") +
                                " scheduler timers (ms): Ws=" + c.webSocketUpdateInterval +
                                " Db=" + c.databaseUpdateInterval +
                                " Ploc=" + c.passiveLocationRefreshIntervalMs +
                                " Pnv=" + c.passiveNearbyVehicleRefreshIntervalMs +
                                " FHeld=" + c.firearmPlayerHeldScanIntervalMs +
                                " FPkup=" + c.firearmPickupScanIntervalMs +
                                " Cloud=" + c.cloudSyncFlushIntervalMs +
                                " ExVehCd=" + c.explicitNearbyVehicleScanCooldownMs +
                                " LiveLocAge=" + c.liveLocationRefreshMaxAgeMs
                            );
                        }

                        // Always track callouts via LSPDFR events; resolve Callout from LSPDFR API first (Callout Interface alone can break after CI updates).
                        CalloutEvents.AddCalloutEventHandlers();

                        if (ModIntegration.SubscribedPolicingRedefinedStopEvents) {
                            PREvents.SubscribeToPREvents();
                            if (GetConfig().firearmDebugLogging)
                                Data.DataController.LogPRAssemblyFirearmDiagnostics();
                        }
                        if (ModIntegration.SubscribedStopThePedStopEvents)
                            STPEvents.SubscribeToStpEvents();
                        CDFEvents.Subscribe();
                        // Always subscribe to LSPDFR OnPedArrested: PR's OnPedArrested only fires for arrests through PR.
                        // LSPDFR's fires for all arrests (including those done via LSPDFR or other plugins).
                        LSPDFREvents.SubscribeToLSPDFREvents();

                        RageNotification.ShowSuccess($"{GetLanguage().inGame.loaded} v{Version}");

                        string iniPath = Path.Combine(MDTProPath, "MDTPro.ini");
                        // Menu key: always from ini when valid (players set SettingsMenuKey to any free F-key they want).
                        // F10 is only the fallback when the ini omits both keys or the values are not valid key names.
                        string menuKeyStr = ReadIniValue(iniPath, "MDTPro", "SettingsMenuKey");
                        string legacyHandoffKeyStr = ReadIniValue(iniPath, "MDTPro", "CitationHandoffKey");
                        if (!string.IsNullOrWhiteSpace(menuKeyStr) && Enum.TryParse<Keys>(menuKeyStr.Trim(), true, out Keys parsedMenu))
                            UI.SettingsMenu.MenuKey = parsedMenu;
                        else if (!string.IsNullOrWhiteSpace(legacyHandoffKeyStr) && Enum.TryParse<Keys>(legacyHandoffKeyStr.Trim(), true, out Keys parsedLegacy))
                            UI.SettingsMenu.MenuKey = parsedLegacy;
                        else
                            UI.SettingsMenu.MenuKey = Keys.F10;

                        // StopThePed-path citation handoff runs from the in-game settings menu (same RAGENativeUI pool); no separate key.
                        if (Main.usePR)
                            StpCitationHandoffQueue.Clear();

                        ALPR.ALPRController.Start();
                        UI.SettingsMenu.Start();

                        CloudPluginBridge.RequestLspdfrDutyShiftStartFromBridge();

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

        private static void ShutdownRuntime(bool isFinalUnload) {
            try {
                Game.LogTrivial("[Lifecycle] MDT Pro runtime shutdown start");
                SetupController.DisableBackgroundWorkers();
                // Stop accepting new game-thread work before tearing down listeners/fibers.
                GameFiberHttpBridge.Stop();
                try {
                    CalloutEvents.RemoveCalloutEventHandlers();
                    STPEvents.UnsubscribeAll();
                    PREvents.UnsubscribeAll();
                    CDFEvents.UnsubscribeAll();
                    LSPDFREvents.UnsubscribeAll();
                } catch {
                    /* do not block unload if a host event API throws */
                }
                UI.SettingsMenu.Stop();
                StpCitationHandoffQueue.Clear();
                ALPR.ALPRController.Stop();
                Data.DataController.EndCurrentShift();
                GameWorkScheduler.Stop();
                Server.Stop();
                if (!isFinalUnload)
                    ClearCache();
            } finally {
                Game.LogTrivial("[Lifecycle] MDT Pro runtime shutdown complete");
            }
        }
    }
}
