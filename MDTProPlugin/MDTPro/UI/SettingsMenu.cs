using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Rage;
using RAGENativeUI;
using RAGENativeUI.Elements;
using MDTPro.Utility;
using MDTPro.ALPR;
using static MDTPro.Setup.SetupController;

namespace MDTPro.UI {
    /// <summary>
    /// In-game settings menu (RAGENativeUI). Open with F7 to enable/disable ALPR and other options.
    /// </summary>
    internal static class SettingsMenu {
        /// <summary>Key to open the MDT Pro settings menu.</summary>
        public static Keys MenuKey { get; set; } = Keys.F7;

        private static readonly string[] AnchorValues = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
        private static readonly float[] ScaleValues = { 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f };
        private const int OffsetMin = 0;
        private const int OffsetMax = 400;
        private const int OffsetStep = 10;

        private static MenuPool _pool;
        private static UIMenu _mainMenu;
        private static UIMenu _alprPositionMenu;
        private static UIMenuCheckboxItem _alprEnabledItem;
        private static UIMenuListItem _alprAnchorItem;
        private static UIMenuListItem _alprScaleItem;
        private static UIMenuListItem _alprOffsetXItem;
        private static UIMenuListItem _alprOffsetYItem;
        private static GameFiber _menuFiber;
        private static GameFiber _moveHudFiber;
        private static bool _stopRequested;
        private static bool _menuKeyWasDown;
        /// <summary>True while syncing ALPR position list indices from config so Apply* handlers do not write back.</summary>
        private static bool _syncingAlprPositionFromConfig;
        /// <summary>True while syncing ALPR enabled checkbox from config so CheckboxEvent does not trigger ApplyAlprEnabled.</summary>
        private static bool _syncingAlprEnabledFromConfig;

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

        internal static void Stop() {
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
                _alprEnabledItem = null;
                _alprAnchorItem = null;
                _alprScaleItem = null;
                _alprOffsetXItem = null;
                _alprOffsetYItem = null;
            }

            _pool = new MenuPool();
            _mainMenu = new UIMenu("MDT Pro", "~b~Settings");
            _pool.Add(_mainMenu);

            var cfg = GetConfig();
            _alprEnabledItem = new UIMenuCheckboxItem("Enable ALPR (MDT scanner + HUD)", cfg != null && cfg.alprEnabled,
                "Built-in plate scan and on-screen panel (on duty, police vehicle). Browser ALPR popups can use Callout Interface without turning this on.");
            _mainMenu.AddItem(_alprEnabledItem);

            _alprEnabledItem.CheckboxEvent += (sender, @checked) => {
                ApplyAlprEnabled(@checked);
            };

            _mainMenu.OnMenuOpen += (sender) => {
                var c = GetConfig();
                if (c == null || _alprEnabledItem == null) return;
                _syncingAlprEnabledFromConfig = true;
                try {
                    _alprEnabledItem.Checked = c.alprEnabled;
                } finally {
                    _syncingAlprEnabledFromConfig = false;
                }
            };

            // ALPR HUD Position submenu
            var positionItem = new UIMenuItem("ALPR HUD Position", "Change where the ALPR panel appears. Use list options or move with arrow keys.");
            _mainMenu.AddItem(positionItem);
            _alprPositionMenu = new UIMenu("MDT Pro", "~b~ALPR HUD Position");
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

            var moveWithArrowsItem = new UIMenuItem("Move panel", "~b~Enter~s~ to drag the panel with the mouse. Enter to save, Backspace to cancel.");
            _alprPositionMenu.AddItem(moveWithArrowsItem);
            moveWithArrowsItem.Activated += (menu, item) => StartMoveHudMode();

            _alprPositionMenu.OnMenuOpen += (sender) => SyncAlprPositionFromConfig();
        }

        /// <summary>Snap offset to the menu step so arrow-key and list mode stay in sync.</summary>
        private static int SnapOffsetToStep(int value) {
            int stepped = (int)Math.Round((double)value / OffsetStep) * OffsetStep;
            return Math.Max(OffsetMin, Math.Min(OffsetMax, stepped));
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
            RageNotification.Show("~b~Move ALPR panel:~s~ Click and drag the panel. ~g~Enter~s~ = save. ~r~Backspace~s~ = cancel.", RageNotification.NotificationType.Info);
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
            // Same as CitationHandoffKeybind: RunServer flips true ~120ms after the server thread starts; don't exit before that.
            while (!_stopRequested) {
                try {
                    bool keyDown = Game.IsKeyDownRightNow(MenuKey);
                    if (keyDown && !_menuKeyWasDown && _mainMenu != null && !_mainMenu.Visible) {
                        _mainMenu.Visible = true;
                    }
                    _menuKeyWasDown = keyDown;
                    _pool?.ProcessMenus();
                } catch (System.Exception ex) {
                    Helper.Log($"SettingsMenu loop: {ex.Message}", true, Helper.LogSeverity.Warning);
                }
                GameFiber.Yield();
            }
        }
    }
}
