using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Rage;
using RAGENativeUI;
using RAGENativeUI.Elements;
using RAGENativeUI.PauseMenu;
using MDTPro.Utility;
using MDTPro.ALPR;
using MDTPro.Cloud;
using MDTPro.UI.InGameComputer;
using MDTPro;
using static MDTPro.Setup.SetupController;

namespace MDTPro.UI {
    /// <summary>
    /// In-game settings menu (RAGENativeUI). Open with the configured key (default F10) for ALPR, StopThePed citation handoff, and other options.
    /// </summary>
    internal static class SettingsMenu {
        /// <summary>Key to open the MDT Pro settings menu.</summary>
        public static Keys MenuKey { get; set; } = Keys.F10;

        /// <summary>Human-readable key name for notifications (e.g. citation queued).</summary>
        internal static string CurrentMenuKeyLabel => MenuKey.ToString();

        private static readonly string[] AnchorValues = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
        private static readonly float[] ScaleValues = { 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f };
        private const int OffsetMin = 0;
        private const int OffsetMax = 400;
        private const int OffsetStep = 10;

        private static MenuPool _pool;
        private static UIMenu _mainMenu;
        private static UIMenu _alprPositionMenu;
        private static readonly string[] GameWorkModeValues = { "Performance", "Balanced", "Live" };
        private static UIMenuListItem _gameWorkModeItem;
        private static UIMenuCheckboxItem _alprEnabledItem;
        private static UIMenuCheckboxItem _citationSuspectLinesItem;
        private static UIMenuListItem _alprAnchorItem;
        private static UIMenuListItem _alprScaleItem;
        private static UIMenuListItem _alprOffsetXItem;
        private static UIMenuListItem _alprOffsetYItem;
        private static UIMenuItem _citationHandoffItem;
        private static GameFiber _menuFiber;
        private static GameFiber _moveHudFiber;
        private static bool _stopRequested;
        private static bool _menuKeyWasDown;
        /// <summary>True while syncing ALPR position list indices from config so Apply* handlers do not write back.</summary>
        private static bool _syncingAlprPositionFromConfig;
        /// <summary>True while syncing ALPR enabled checkbox from config so CheckboxEvent does not trigger ApplyAlprEnabled.</summary>
        private static bool _syncingAlprEnabledFromConfig;
        /// <summary>True while syncing citation suspect-lines checkbox from config so CheckboxEvent does not write config.</summary>
        private static bool _syncingCitationSuspectLinesFromConfig;
        /// <summary>True while building the menu or syncing the list index from disk so <see cref="UIMenuListItem.OnListChanged"/> does not write the wrong mode (RAGENativeUI can fire on construction / programmatic index changes).</summary>
        private static bool _suppressGameWorkModeListEvents;

        internal static void Start() {
            if (_menuFiber != null && _menuFiber.IsAlive) return; // already running
            // Wait for previous fibers to exit after Stop() so we never run two MenuLoops or reset _stopRequested while old fibers are still running
            if (_menuFiber != null) {
                while (_menuFiber.IsAlive) GameFiber.Yield();
            }
            var moveFiber = _moveHudFiber;
            if (moveFiber != null) {
                moveFiber.Abort();
                while (moveFiber.IsAlive) GameFiber.Yield();
                _moveHudFiber = null;
            }
            _menuFiber = null;
            _stopRequested = false;
            BuildMenu();
            _menuKeyWasDown = Game.IsKeyDownRightNow(MenuKey); // avoid debounce treating a held key across Stop/Start as no new press
            _menuFiber = GameFiber.StartNew(MenuLoop);
            Helper.Log($"MDT Pro settings menu started (press {MenuKey} to open)", true, Helper.LogSeverity.Info);
        }

        /// <summary>Hides RAGENativeUI menus without stopping the settings fiber (avoids stacking with LemonUI cloud login).</summary>
        internal static void CloseAllMenusForCloudOverlay() {
            try {
                if (_mainMenu != null) _mainMenu.Visible = false;
                _pool?.CloseAllMenus();
            } catch { /* ignore */ }
        }

        internal static void Stop() {
            CloudIngameEntry.RequestAbortFromHost();
            InGameComputerEntry.RequestAbortFromHost();
            _stopRequested = true;
            _menuFiber?.Abort();
            if (_moveHudFiber != null) {
                _moveHudFiber.Abort();
                _moveHudFiber = null; // aborted fiber's finally never runs, so clear ref here to avoid re-Abort() in StartMoveHudMode
                ALPRHUD.EndPreviewMode(); // aborted fiber won't run cleanup; clear preview so HUD shows real hits when back on duty
            }
            // Do not set _menuFiber = null here; Start() needs the reference to wait for the fiber to exit
            if (_mainMenu != null) _mainMenu.Visible = false;
            _pool?.CloseAllMenus();
            // Keep _pool and menu refs so BuildMenu() can tear down the old pool before creating a new one;
            // tearing down in BuildMenu() avoids duplicate handlers and ensures fresh menu state after Stop/Start.
        }

        private static void BuildMenu() {
            // If we have an existing pool (e.g. after Stop()), tear it down first so we create fresh menus
            // with a single set of handlers. Reused UIMenu instances after CloseAllMenus() can have broken state.
            if (_pool != null) {
                _pool.CloseAllMenus();
                _pool = null;
                _mainMenu = null;
                _alprPositionMenu = null;
                _gameWorkModeItem = null;
                _alprEnabledItem = null;
                _citationSuspectLinesItem = null;
                _alprAnchorItem = null;
                _alprScaleItem = null;
                _alprOffsetXItem = null;
                _alprOffsetYItem = null;
                _citationHandoffItem = null;
            }

            _pool = new MenuPool();
            _mainMenu = new UIMenu("MDT Pro", "~b~Settings");
            // RAGENativeUI defaults ResetCursorOnOpen=true (centers the mouse via natives when the menu opens).
            // That has caused instant game freezes with some setups (overlays, capture tools, other UI mods); we do not need it for MDT Pro.
            _mainMenu.ResetCursorOnOpen = false;
            _pool.Add(_mainMenu);

            _citationHandoffItem = null;
            if (!Main.usePR) {
                var langHand = GetLanguage().inGame;
                string handTitle = string.IsNullOrWhiteSpace(langHand.stpCitationHandoffSettingsItemTitle)
                    ? "Hand pending citation"
                    : langHand.stpCitationHandoffSettingsItemTitle;
                string handIdle = string.IsNullOrWhiteSpace(langHand.stpCitationHandoffSettingsItemIdleDescription)
                    ? ""
                    : langHand.stpCitationHandoffSettingsItemIdleDescription;
                _citationHandoffItem = new UIMenuItem(handTitle, handIdle) { Enabled = false };
                _citationHandoffItem.Activated += (sender, e) => {
                    if (!StpCitationHandoffQueue.TryPeekPending(out _))
                        return;
                    if (_mainMenu == null) return;
                    _mainMenu.Visible = false;
                    try {
                        GameFiber.Yield();
                        StpCitationHandoffQueue.TryProcessKeyPress();
                    } finally {
                        if (_mainMenu != null && !_stopRequested)
                            _mainMenu.Visible = true;
                    }
                };
                _mainMenu.AddItem(_citationHandoffItem);
            }

            var cfg = GetConfig();
            _alprEnabledItem = new UIMenuCheckboxItem("Enable ALPR (MDT scanner + HUD)", cfg != null && cfg.alprEnabled,
                "Built-in plate scan, HUD, and MDT popups.");
            _mainMenu.AddItem(_alprEnabledItem);

            _alprEnabledItem.CheckboxEvent += (sender, @checked) => {
                ApplyAlprEnabled(@checked);
            };

            int gwmIdx = GameWorkModeToIndex(cfg?.gameWorkMode);
            var gwmDisplay = new List<object> { "Performance", "Balanced", "Live" };
#pragma warning disable CS0618
            _suppressGameWorkModeListEvents = true;
            try {
                _gameWorkModeItem = new UIMenuListItem("Game work mode", gwmDisplay, gwmIdx,
                    "Background refresh load. Performance is lightest; Live does the most.");
                _mainMenu.AddItem(_gameWorkModeItem);
                _gameWorkModeItem.OnListChanged += (sender, newIndex) => ApplyGameWorkMode(GameWorkModeValues[newIndex]);
            } finally {
                _suppressGameWorkModeListEvents = false;
            }
#pragma warning restore CS0618

            _citationSuspectLinesItem = new UIMenuCheckboxItem("Suspect lines after citation", cfg != null && cfg.citationPedReactionEnabled,
                "Short suspect subtitle after citation handoff.");
            _mainMenu.AddItem(_citationSuspectLinesItem);
            _citationSuspectLinesItem.CheckboxEvent += (sender, @checked) => {
                ApplyCitationSuspectLinesEnabled(@checked);
            };

            _mainMenu.OnMenuOpen += (sender) => {
                try {
                    if (_citationHandoffItem != null) {
                        StpCitationHandoffQueue.ClearExpiredPendingWhenMenuOpens();
                        var langH = GetLanguage().inGame;
                        if (StpCitationHandoffQueue.TryPeekPending(out string pedPending)) {
                            _citationHandoffItem.Enabled = true;
                            string ready = langH.stpCitationHandoffSettingsItemReadyDescription ?? "";
                            _citationHandoffItem.Description = ready.Contains("{0}")
                                ? string.Format(ready, pedPending ?? "?")
                                : ready;
                        } else {
                            _citationHandoffItem.Enabled = false;
                            _citationHandoffItem.Description = langH.stpCitationHandoffSettingsItemIdleDescription ?? "";
                        }
                    }
                    var c = GetConfig();
                    if (c == null) return;
                    int idx = GameWorkModeToIndex(c.gameWorkMode);
                    if (_gameWorkModeItem != null && _gameWorkModeItem.Index != idx) {
                        _suppressGameWorkModeListEvents = true;
                        try {
                            _gameWorkModeItem.Index = idx;
                        } finally {
                            _suppressGameWorkModeListEvents = false;
                        }
                    }
                    if (_alprEnabledItem != null) {
                        _syncingAlprEnabledFromConfig = true;
                        try {
                            _alprEnabledItem.Checked = c.alprEnabled;
                        } finally {
                            _syncingAlprEnabledFromConfig = false;
                        }
                    }
                    if (_citationSuspectLinesItem != null) {
                        _syncingCitationSuspectLinesFromConfig = true;
                        try {
                            _citationSuspectLinesItem.Checked = c.citationPedReactionEnabled;
                        } finally {
                            _syncingCitationSuspectLinesFromConfig = false;
                        }
                    }
                } catch (Exception ex) {
                    Helper.Log($"SettingsMenu OnMenuOpen: {ex.Message}", true, Helper.LogSeverity.Error);
                }
            };

            // ALPR HUD Position submenu
            var positionItem = new UIMenuItem("ALPR HUD Position", "Change where the ALPR panel appears.");
            _mainMenu.AddItem(positionItem);
            _alprPositionMenu = new UIMenu("MDT Pro", "~b~ALPR HUD Position");
            _alprPositionMenu.ResetCursorOnOpen = false;
            _pool.Add(_alprPositionMenu);
            _mainMenu.BindMenuToItem(_alprPositionMenu, positionItem);

            var anchorDisplay = new List<object> { "Top Left", "Top Right", "Bottom Left", "Bottom Right" };
            int anchorIdx = AnchorToIndex(cfg?.alprHudAnchor ?? "TopRight");
#pragma warning disable CS0618
            _alprAnchorItem = new UIMenuListItem("Anchor", anchorDisplay, anchorIdx, "Screen corner for the ALPR panel.");
            _alprPositionMenu.AddItem(_alprAnchorItem);
            _alprAnchorItem.OnListChanged += (sender, newIndex) => ApplyAlprHudAnchor(AnchorValues[newIndex]);

            var scaleDisplay = new List<object> { "75%", "100%", "125%", "150%", "175%", "200%" };
            int scaleIdx = ScaleToIndex(cfg?.alprHudScale ?? 1f);
            _alprScaleItem = new UIMenuListItem("Scale", scaleDisplay, scaleIdx, "Panel size. Default is 100%.");
            _alprPositionMenu.AddItem(_alprScaleItem);
            _alprScaleItem.OnListChanged += (sender, newIndex) => ApplyAlprHudScale(ScaleValues[newIndex]);

            var offsetOptions = new List<object>();
            for (int i = OffsetMin; i <= OffsetMax; i += OffsetStep) offsetOptions.Add(i.ToString());
            int ox = Math.Max(OffsetMin, Math.Min(OffsetMax, cfg?.alprHudOffsetX ?? 20));
            int oy = Math.Max(OffsetMin, Math.Min(OffsetMax, cfg?.alprHudOffsetY ?? 150));
            _alprOffsetXItem = new UIMenuListItem("Offset X", offsetOptions, SnapOffsetToStep(ox) / OffsetStep, "Horizontal offset in pixels from the anchor corner.");
            _alprOffsetYItem = new UIMenuListItem("Offset Y", offsetOptions, SnapOffsetToStep(oy) / OffsetStep, "Vertical offset in pixels from the anchor corner.");
#pragma warning restore CS0618
            _alprPositionMenu.AddItem(_alprOffsetXItem);
            _alprPositionMenu.AddItem(_alprOffsetYItem);
            _alprOffsetXItem.OnListChanged += (sender, newIndex) => ApplyAlprHudOffsetX(OffsetMin + newIndex * OffsetStep);
            _alprOffsetYItem.OnListChanged += (sender, newIndex) => ApplyAlprHudOffsetY(OffsetMin + newIndex * OffsetStep);

            var moveWithArrowsItem = new UIMenuItem("Move panel", "Enter to save. Backspace to cancel.");
            _alprPositionMenu.AddItem(moveWithArrowsItem);
            moveWithArrowsItem.Activated += (menu, item) => StartMoveHudMode();

            _alprPositionMenu.OnMenuOpen += (sender) => {
                try { SyncAlprPositionFromConfig(); } catch (Exception ex) {
                    Helper.Log($"SettingsMenu ALPR submenu OnMenuOpen: {ex.Message}", true, Helper.LogSeverity.Error);
                }
            };

            // Keep this item enabled so RAGENativeUI still lists it; disabled items are omitted from the menu.
            // Missing LemonUI is handled when the item is activated (notification + log).
            var cloudItem = new UIMenuItem("MDT Cloud...", "Sign in to MDT Cloud.");
            _mainMenu.AddItem(cloudItem);
            cloudItem.Activated += (menu, item) => {
                if (!CloudIngameEntry.IsLemonUiDllPresent()) {
                    RageNotification.ShowError("LemonUI.RagePluginHook.dll missing beside MDTPro.dll or in GTA V folder.");
                    return;
                }
                if (CloudIngameEntry.IsSessionRunning) {
                    RageNotification.Show("MDT Cloud sign-in is already open.", RageNotification.NotificationType.Info);
                    return;
                }
                if (_mainMenu != null) _mainMenu.Visible = false;
                _pool?.CloseAllMenus();
                CloudIngameEntry.Start();
            };

            var computerItem = new UIMenuItem("In-game MDT...", "Open the MDT.");
            _mainMenu.AddItem(computerItem);
            computerItem.Activated += (menu, item) => {
                if (!CloudIngameEntry.IsLemonUiDllPresent()) {
                    RageNotification.ShowError("LemonUI.RagePluginHook.dll missing beside MDTPro.dll or in GTA V folder.");
                    return;
                }
                if (CloudIngameEntry.IsSessionRunning) {
                    RageNotification.Show("Close MDT Cloud sign-in before opening the in-game MDT.", RageNotification.NotificationType.Info);
                    return;
                }
                if (InGameComputerEntry.IsSessionRunning) {
                    RageNotification.Show("The in-game MDT is already open.", RageNotification.NotificationType.Info);
                    return;
                }
                if (_mainMenu != null) _mainMenu.Visible = false;
                _pool?.CloseAllMenus();
                InGameComputerEntry.Start();
            };
        }

        /// <summary>Snap offset to the menu step so arrow-key and list mode stay in sync.</summary>
        private static int SnapOffsetToStep(int value) {
            int stepped = (int)Math.Round((double)value / OffsetStep) * OffsetStep;
            return Math.Max(OffsetMin, Math.Min(OffsetMax, stepped));
        }

        private static int GameWorkModeToIndex(string mode) {
            if (string.IsNullOrWhiteSpace(mode)) return 0;
            for (int i = 0; i < GameWorkModeValues.Length; i++) {
                if (string.Equals(GameWorkModeValues[i], mode.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        private static void ApplyGameWorkMode(string mode) {
            if (_suppressGameWorkModeListEvents) return;
            try {
                var cfg = GetConfig();
                if (cfg == null) return;
                string raw = string.IsNullOrWhiteSpace(mode) ? "Performance" : mode.Trim();
                string canonical = null;
                for (int i = 0; i < GameWorkModeValues.Length; i++) {
                    if (string.Equals(GameWorkModeValues[i], raw, StringComparison.OrdinalIgnoreCase)) {
                        canonical = GameWorkModeValues[i];
                        break;
                    }
                }
                if (canonical == null) canonical = "Performance";
                if (string.Equals(cfg.gameWorkMode, canonical, StringComparison.Ordinal)) return;
                cfg.gameWorkMode = canonical;
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ResetConfig();
            } catch (Exception ex) {
                try { ResetConfig(); } catch { }
                Helper.Log($"SettingsMenu ApplyGameWorkMode: {ex.Message}", true, Helper.LogSeverity.Error);
                RageNotification.ShowError("Failed to save game work mode.");
            }
        }

        private static int AnchorToIndex(string anchor) {
            if (string.IsNullOrEmpty(anchor)) return 1;
            switch (anchor.ToLowerInvariant()) {
                case "topleft": return 0;
                case "topright": return 1;
                case "bottomleft": return 2;
                case "bottomright": return 3;
                default: return 1;
            }
        }

        private static int ScaleToIndex(float scale) {
            int best = 1;
            float bestDiff = float.MaxValue;
            for (int i = 0; i < ScaleValues.Length; i++) {
                float d = Math.Abs(ScaleValues[i] - scale);
                if (d < bestDiff) { bestDiff = d; best = i; }
            }
            return best;
        }

        private static void SyncAlprPositionFromConfig() {
            var c = GetConfig();
            if (c == null) return;
            _syncingAlprPositionFromConfig = true;
            try {
                int anchorIdx = AnchorToIndex(c.alprHudAnchor);
                if (_alprAnchorItem != null && _alprAnchorItem.Index != anchorIdx)
                    _alprAnchorItem.Index = anchorIdx;
                int ox = Math.Max(OffsetMin, Math.Min(OffsetMax, c.alprHudOffsetX));
                int oy = Math.Max(OffsetMin, Math.Min(OffsetMax, c.alprHudOffsetY));
                int idxX = SnapOffsetToStep(ox) / OffsetStep;
                int idxY = SnapOffsetToStep(oy) / OffsetStep;
                if (_alprOffsetXItem != null && _alprOffsetXItem.Index != idxX)
                    _alprOffsetXItem.Index = idxX;
                if (_alprOffsetYItem != null && _alprOffsetYItem.Index != idxY)
                    _alprOffsetYItem.Index = idxY;
                int scaleIdx = ScaleToIndex(Math.Max(0.75f, Math.Min(2f, c.alprHudScale)));
                if (_alprScaleItem != null && _alprScaleItem.Index != scaleIdx)
                    _alprScaleItem.Index = scaleIdx;
            } finally {
                _syncingAlprPositionFromConfig = false;
            }
        }

        private static void ApplyAlprHudScale(float value) {
            if (_syncingAlprPositionFromConfig) return;
            try {
                var cfg = GetConfig();
                if (cfg == null) return;
                cfg.alprHudScale = Math.Max(0.75f, Math.Min(2f, value));
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ResetConfig();
            } catch (System.Exception ex) {
                try { ResetConfig(); } catch { }
                Helper.Log($"SettingsMenu ApplyAlprHudScale: {ex.Message}", true, Helper.LogSeverity.Error);
                RageNotification.ShowError("Failed to save ALPR scale setting.");
            }
        }

        private static void ApplyAlprHudAnchor(string anchor) {
            if (_syncingAlprPositionFromConfig) return;
            try {
                var cfg = GetConfig();
                if (cfg == null) return;
                cfg.alprHudAnchor = anchor;
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ResetConfig();
            } catch (System.Exception ex) {
                try { ResetConfig(); } catch { /* reload from disk if write failed */ }
                Helper.Log($"SettingsMenu ApplyAlprHudAnchor: {ex.Message}", true, Helper.LogSeverity.Error);
                RageNotification.ShowError("Failed to save ALPR position setting.");
            }
        }

        private static void ApplyAlprHudOffsetX(int value) {
            if (_syncingAlprPositionFromConfig) return;
            try {
                var cfg = GetConfig();
                if (cfg == null) return;
                cfg.alprHudOffsetX = Math.Max(OffsetMin, Math.Min(OffsetMax, value));
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ResetConfig();
            } catch (System.Exception ex) {
                try { ResetConfig(); } catch { /* reload from disk if write failed */ }
                Helper.Log($"SettingsMenu ApplyAlprHudOffsetX: {ex.Message}", true, Helper.LogSeverity.Error);
                RageNotification.ShowError("Failed to save ALPR position setting.");
            }
        }

        private static void ApplyAlprHudOffsetY(int value) {
            if (_syncingAlprPositionFromConfig) return;
            try {
                var cfg = GetConfig();
                if (cfg == null) return;
                cfg.alprHudOffsetY = Math.Max(OffsetMin, Math.Min(OffsetMax, value));
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ResetConfig();
            } catch (System.Exception ex) {
                try { ResetConfig(); } catch { /* reload from disk if write failed */ }
                Helper.Log($"SettingsMenu ApplyAlprHudOffsetY: {ex.Message}", true, Helper.LogSeverity.Error);
                RageNotification.ShowError("Failed to save ALPR position setting.");
            }
        }

        private static void StartMoveHudMode() {
            _pool?.CloseAllMenus();
            var cfg = GetConfig();
            if (cfg == null) return;
            int x = SnapOffsetToStep(Math.Max(OffsetMin, Math.Min(OffsetMax, cfg.alprHudOffsetX)));
            int y = SnapOffsetToStep(Math.Max(OffsetMin, Math.Min(OffsetMax, cfg.alprHudOffsetY)));
            string anchor = cfg.alprHudAnchor ?? "TopRight";
            ALPRHUD.Start(); // ensure HUD is drawing (e.g. if ALPR was disabled)
            ALPRHUD.StartPreviewMode(anchor, x, y);
            RageNotification.Show("~b~Move ALPR panel:~s~ Click and drag. ~g~Enter~s~ = save. ~r~Backspace~s~ = cancel. Tip: in-game hold ~b~Left Alt~s~ and drag the panel; drag the ~b~SIZE~s~ corner to resize.", RageNotification.NotificationType.Info);
            var prevMove = _moveHudFiber;
            if (prevMove != null) {
                prevMove.Abort();
                while (prevMove.IsAlive) GameFiber.Yield();
                _moveHudFiber = null;
            }
            _moveHudFiber = GameFiber.StartNew(() => {
                try {
                    RunMoveHudLoop(anchor, x, y);
                } finally {
                    _moveHudFiber = null;
                }
            });
        }

        private static void RunMoveHudLoop(string anchor, int startX, int startY) {
            int x = startX;
            int y = startY;
            bool saved = false;
            bool dragging = false;
            float dragOffsetX = 0f, dragOffsetY = 0f;
            while (!_stopRequested) {
                GameFiber.Yield();
                if (_stopRequested) break;
                if (Game.IsKeyDownRightNow(Keys.Enter)) {
                    saved = true;
                    break;
                }
                if (Game.IsKeyDownRightNow(Keys.Back)) break;
                Rage.MouseState mouse = Game.GetMouseState();
                if (mouse.IsRightButtonDown) break;
                bool moved = false;
                if (ALPRHUD.TryGetPreviewBounds(out float px, out float py, out float pw, out float ph)) {
                    float mx = mouse.X;
                    float my = mouse.Y;
                    bool cursorOverPanel = mx >= px && mx <= px + pw && my >= py && my <= py + ph;
                    if (mouse.IsLeftButtonDown) {
                        if (!dragging && cursorOverPanel) {
                            dragging = true;
                            dragOffsetX = mx - px;
                            dragOffsetY = my - py;
                        }
                        if (dragging) {
                            ALPRHUD.ScreenToOffset(anchor, mx - dragOffsetX, my - dragOffsetY, out int ox, out int oy);
                            x = Math.Max(OffsetMin, Math.Min(OffsetMax, ox));
                            y = Math.Max(OffsetMin, Math.Min(OffsetMax, oy));
                            ALPRHUD.UpdatePreviewPosition(x, y);
                            moved = true;
                        }
                    } else {
                        dragging = false;
                    }
                }
                if (!moved) {
                    if (Game.IsKeyDownRightNow(Keys.Left)) { x = Math.Max(OffsetMin, x - OffsetStep); moved = true; }
                    if (Game.IsKeyDownRightNow(Keys.Right)) { x = Math.Min(OffsetMax, x + OffsetStep); moved = true; }
                    if (Game.IsKeyDownRightNow(Keys.Up)) { y = Math.Max(OffsetMin, y - OffsetStep); moved = true; }
                    if (Game.IsKeyDownRightNow(Keys.Down)) { y = Math.Min(OffsetMax, y + OffsetStep); moved = true; }
                    if (moved) ALPRHUD.UpdatePreviewPosition(x, y);
                }
                GameFiber.Sleep(moved ? 16 : 50);
            }
            ALPRHUD.EndPreviewMode();
            if (_stopRequested) return;
            if (saved) {
                try {
                    var cfg = GetConfig();
                    if (cfg == null) return;
                    cfg.alprHudAnchor = anchor;
                    cfg.alprHudOffsetX = x;
                    cfg.alprHudOffsetY = y;
                    Helper.WriteToJsonFile(ConfigPath, cfg);
                    ResetConfig();
                } catch (System.Exception ex) {
                    try { ResetConfig(); } catch { }
                    Helper.Log($"SettingsMenu save HUD position: {ex.Message}", true, Helper.LogSeverity.Error);
                    RageNotification.ShowError("Failed to save position.");
                    return;
                }
                RageNotification.Show("ALPR panel position ~g~saved~s~.", RageNotification.NotificationType.Success);
            } else {
                RageNotification.Show("Position ~r~cancelled~s~.", RageNotification.NotificationType.Info);
            }
        }

        private static void ApplyCitationSuspectLinesEnabled(bool enabled) {
            if (_syncingCitationSuspectLinesFromConfig) return;
            try {
                var cfg = GetConfig();
                if (cfg == null) return;
                cfg.citationPedReactionEnabled = enabled;
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ResetConfig();
            } catch (System.Exception ex) {
                try { ResetConfig(); } catch { }
                Helper.Log($"SettingsMenu ApplyCitationSuspectLinesEnabled: {ex.Message}", true, Helper.LogSeverity.Error);
                RageNotification.ShowError("Failed to save citation suspect lines setting.");
            }
        }

        private static void ApplyAlprEnabled(bool enabled) {
            if (_syncingAlprEnabledFromConfig) return;
            try {
                var cfg = GetConfig();
                if (cfg == null) return;
                cfg.alprEnabled = enabled;
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ResetConfig();
            } catch (System.Exception ex) {
                try { ResetConfig(); } catch { /* reload from disk so cache matches file after write failure */ }
                Helper.Log($"SettingsMenu ApplyAlprEnabled (save): {ex.Message}", true, Helper.LogSeverity.Error);
                RageNotification.ShowError("Failed to save ALPR setting.");
                return;
            }

            // Defer Start/Stop to the next fiber tick — starting/aborting scan fibers from NativeUI checkbox callbacks can crash RPH.
            bool wantEnabled = enabled;
            GameFiber.StartNew(() => {
                try {
                    GameFiber.Yield();
                    var c = GetConfig();
                    if (c == null || c.alprEnabled != wantEnabled) return;
                    if (wantEnabled) {
                        ALPRController.Start();
                        RageNotification.Show("ALPR ~g~enabled~s~. Plate scanning and HUD will run when on duty in a police vehicle.", RageNotification.NotificationType.Success);
                    } else {
                        ALPRController.Stop();
                        RageNotification.Show("ALPR ~r~disabled~s~.", RageNotification.NotificationType.Info);
                    }
                } catch (System.Exception ex) {
                    Helper.Log($"SettingsMenu ApplyAlprEnabled (apply): {ex.Message}", true, Helper.LogSeverity.Error);
                    RageNotification.ShowError("Setting saved but ALPR could not be updated.");
                }
            });
        }

        private static void MenuLoop() {
            // RunServer flips true ~120ms after the server thread starts; don't exit before that.
            while (!_stopRequested) {
                try {
                    // LemonUI sessions run on their own fibers; do not ProcessMenus / settings-menu RNUI on top of them.
                    if (CloudIngameEntry.IsSessionRunning || InGameComputerEntry.IsSessionRunning) {
                        _menuKeyWasDown = Game.IsKeyDownRightNow(MenuKey);
                        GameFiber.Yield();
                        continue;
                    }
                    bool keyDown = Game.IsKeyDownRightNow(MenuKey);
                    // RAGENativeUI pattern (Menus Overview / ragenativeui.com guides): never show a menu while another
                    // RAGENativeUI menu or pause menu is visible — that includes other plugins (e.g. Policing Redefined).
                    if (keyDown && !_menuKeyWasDown && _mainMenu != null && !_mainMenu.Visible && !Game.IsPaused
                        && !UIMenu.IsAnyMenuVisible && !TabView.IsAnyPauseMenuVisible) {
                        // Do not open while there is no valid player ped (RAGENativeUI can touch world/camera state).
                        if (Main.Player != null && Main.Player.Exists()) {
                            // One-tick defer after key edge (avoids same-frame clashes with other input handlers).
                            GameFiber.Yield();
                            if (!_stopRequested && _mainMenu != null && !_mainMenu.Visible && !Game.IsPaused
                                && Main.Player != null && Main.Player.Exists()
                                && !UIMenu.IsAnyMenuVisible && !TabView.IsAnyPauseMenuVisible)
                                _mainMenu.Visible = true;
                        }
                    }
                    _menuKeyWasDown = Game.IsKeyDownRightNow(MenuKey);
                    // Menus Overview: call MenuPool.ProcessMenus each tick on your fiber (not only while open).
                    try {
                        _pool?.ProcessMenus();
                    } catch (Exception ex) {
                        Helper.Log($"SettingsMenu ProcessMenus: {ex.Message}", true, Helper.LogSeverity.Warning);
                    }
                } catch (System.Exception ex) {
                    Helper.Log($"SettingsMenu loop: {ex.Message}", true, Helper.LogSeverity.Warning);
                }
                GameFiber.Yield();
            }
        }
    }
}
