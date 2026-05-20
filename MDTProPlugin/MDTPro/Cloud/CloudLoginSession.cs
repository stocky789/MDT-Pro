using System;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LemonUI;
using LemonUI.Menus;
using MDTPro.Setup;
using MDTPro.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rage;
using static MDTPro.Setup.SetupController;

namespace MDTPro.Cloud
{
    /// <summary>LemonUI cloud sign-in session (dedicated fiber + ObjectPool; patterns from MDT Pro Lite).</summary>
    internal sealed class CloudLoginSession
    {
        volatile bool _quit;
        volatile bool _abortFromHost;
        LemonUI.ObjectPool _pool;
        NativeMenu _menu;
        NativeItem _emailRow;
        NativeItem _passwordRow;
        NativeItem _statusRow;
        NativeItem _signInItem;
        NativeItem _logoutItem;
        NativeItem _exitItem;
        InlineLemonTextInput _emailInput;
        InlineLemonTextInput _passwordInput;
        CloudBusyOverlay _busy;
        CloudBrandedBanner _banner;
        string _email = "";
        string _password = "";
        string _status = "";
        bool _hasSavedLogin;
        bool _showingConnectionStatus;
        DateTime _nextStatusRefreshUtc = DateTime.MinValue;
        static readonly TimeSpan LogoutConfirmationWindow = TimeSpan.FromSeconds(5);
        DateTime _logoutConfirmUntilUtc = DateTime.MinValue;
        bool _logoutArmed;
        bool _logoutRequested;
        int _loginGeneration;
        System.Threading.Tasks.Task<(bool ok, string err)> _loginTask;
        CancellationTokenSource _loginCts;

        internal void AbortFromHost()
        {
            _abortFromHost = true;
            try
            {
                _loginCts?.Cancel();
            }
            catch
            {
                /* ignore */
            }
        }

        internal void Run()
        {
            _pool = new LemonUI.ObjectPool();
            _busy = new CloudBusyOverlay();
            _pool.Add(_busy);

            _banner = new CloudBrandedBanner();
            _menu = new NativeMenu("", "MDT Cloud", "Sign in · " + CloudPublicApi.NormalizedBase(), _banner) { MaxItems = 12 };
            CloudLemonUiStyle.ApplyCloudMenuShell(_menu);
            _pool.Add(_menu);

            _emailRow = new NativeItem("Email", "Select to edit");
            _passwordRow = new NativeItem("Password", "Select to edit (masked in row)");
            _statusRow = new NativeItem("Status", "") { Enabled = true };
            _signInItem = new NativeItem("Sign in", "Link this PC and enable cloud sync");
            _logoutItem = new NativeItem("~r~Log out of MDT Cloud", "Use local MDT on this PC");
            _exitItem = new NativeItem("~r~Exit");

            CloudLemonUiStyle.ApplyInteractiveMenuItem(_emailRow);
            CloudLemonUiStyle.ApplyInteractiveMenuItem(_passwordRow);
            CloudLemonUiStyle.ApplyReadOnlyRow(_statusRow, CloudLemonUiStyle.TextMuted);
            CloudLemonUiStyle.ApplyInteractiveMenuItem(_signInItem);
            CloudLemonUiStyle.ApplyInteractiveMenuItem(_logoutItem);
            CloudLemonUiStyle.ApplyInteractiveMenuItem(_exitItem);

            _emailInput = new InlineLemonTextInput(_menu, _emailRow, 64);
            _emailInput.Completed += value =>
            {
                _email = value?.Trim() ?? "";
                RefreshCredentialRows();
            };
            _emailInput.Cancelled += value =>
            {
                _email = value?.Trim() ?? "";
                RefreshCredentialRows();
            };
            _passwordInput = new InlineLemonTextInput(_menu, _passwordRow, 64);
            _passwordInput.Completed += value =>
            {
                _password = value ?? "";
                RefreshCredentialRows();
            };
            _passwordInput.Cancelled += value =>
            {
                _password = value ?? "";
                RefreshCredentialRows();
            };

            _emailRow.Activated += (_, __) =>
            {
                _passwordInput.CancelActiveEdit();
                _emailInput.Start(_email, masked: false, editDescription: "Type email. Enter to finish, Esc to cancel, Backspace to delete.");
            };
            _passwordRow.Activated += (_, __) =>
            {
                _emailInput.CancelActiveEdit();
                _passwordInput.Start(_password, masked: true, editDescription: "Type password. Enter to finish, Esc to cancel, Backspace to delete.");
            };
            _signInItem.Activated += (_, __) => BeginSignIn();
            _logoutItem.Activated += (_, __) => LogoutCloud();
            _exitItem.Activated += (_, __) =>
            {
                _quit = true;
                _menu.Visible = false;
                try { _pool?.HideAll(); } catch { /* ignore */ }
            };

            _menu.Add(_emailRow);
            _menu.Add(_passwordRow);
            _menu.Add(new NativeSeparatorItem());
            _menu.Add(_statusRow);
            _menu.Add(_signInItem);
            _menu.Add(_logoutItem);
            _menu.Add(_exitItem);
            CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);

            _pool.ResolutionChanged += (_, __) => CloudLemonLayout.ApplyMenuColumn(_menu);
            _pool.SafezoneChanged += (_, __) => CloudLemonLayout.ApplyMenuColumn(_menu);
            CloudLemonLayout.ApplyMenuColumn(_menu);

            RefreshSavedLoginState();
            RefreshCredentialRows();
            SetConnectionStatusFromBridge();
            _menu.Visible = true;

            bool savedPause = false;
            bool capturedPause = false;
            bool appliedPause = false;
            try
            {
                try
                {
                    savedPause = Game.IsPaused;
                    capturedPause = true;
                    Game.IsPaused = true;
                    appliedPause = true;
                }
                catch { /* ignore */ }

                while (!_quit && !_abortFromHost)
                {
                    GameFiber.Yield();
                    if (_abortFromHost) break;
                    DrainLoginTask();
                    RefreshConnectionStatusIfDue();
                    CloudLemonLayout.RefreshScreenMetrics();
                    bool suppressMenuInput = (_emailInput?.Process() ?? false) | (_passwordInput?.Process() ?? false);
                    if (suppressMenuInput && _menu != null)
                        _menu.AcceptsInput = false;
                    try
                    {
                        _pool.Process();
                    }
                    catch (Exception ex)
                    {
                        Helper.Log($"MDT Cloud login UI: {ex.Message}", false, Helper.LogSeverity.Warning);
                    }
                    if (suppressMenuInput && _menu != null && !IsInlineInputActive())
                        _menu.AcceptsInput = true;
                    TryExitIfMenuDismissed();
                }
            }
            finally
            {
                try
                {
                    _loginCts?.Cancel();
                }
                catch
                {
                    /* ignore */
                }
                if (_loginTask == null || _loginTask.IsCompleted)
                {
                    try
                    {
                        _loginCts?.Dispose();
                    }
                    catch
                    {
                        /* ignore */
                    }
                    _loginCts = null;
                }
                ClearPasswordInput();
                try { _busy?.Hide(); } catch { /* ignore */ }
                try { _pool?.HideAll(); } catch { /* ignore */ }
                try { _banner?.Dispose(); } catch { /* ignore */ }
                _banner = null;
                if (appliedPause && capturedPause)
                {
                    try { Game.IsPaused = savedPause; } catch { /* ignore */ }
                }
            }
        }

        void TryExitIfMenuDismissed()
        {
            if (_quit || _abortFromHost) return;
            if (_menu != null && !_menu.Visible && (_loginTask == null || _loginTask.IsCompleted))
                _quit = true;
        }

        bool IsInlineInputActive()
        {
            return (_emailInput != null && _emailInput.IsEditing) || (_passwordInput != null && _passwordInput.IsEditing);
        }

        void RefreshCredentialRows()
        {
            if (_emailInput == null || !_emailInput.IsEditing)
                _emailRow.AltTitle = string.IsNullOrEmpty(_email) ? (_hasSavedLogin ? "Saved login" : "(not set)") : Ellipsize(_email, 36);
            if (_passwordInput == null || !_passwordInput.IsEditing)
                _passwordRow.AltTitle = string.IsNullOrEmpty(_password) ? string.Empty : new string('*', _password.Length);
            _signInItem.Title = _hasSavedLogin ? "Relink account" : "Sign in";
            _logoutItem.Enabled = _hasSavedLogin;
            _logoutItem.Title = "~r~Log out of MDT Cloud";
            _logoutItem.AltTitle = _hasSavedLogin ? "Use local MDT on this PC" : "Not signed in";
            if (!_hasSavedLogin)
            {
                _logoutConfirmUntilUtc = DateTime.MinValue;
                _logoutArmed = false;
            }
            CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
        }

        static string Ellipsize(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        void SetStatus(string msg)
        {
            _showingConnectionStatus = false;
            _status = msg ?? "";
            if (_status.Length > 48) _status = _status.Substring(0, 45) + "…";
            _statusRow.AltTitle = string.IsNullOrEmpty(_status) ? "—" : _status;
            CloudLemonUiStyle.ApplyReadOnlyRow(_statusRow, CloudLemonUiStyle.TextMuted);
        }

        void SetConnectionStatusFromBridge()
        {
            bool cloudEnabled = CloudMode.IsEnabled();
            bool connected = cloudEnabled && CloudPluginBridge.ConnectionState == CloudConnectionState.Connected;
            _showingConnectionStatus = true;
            if (!cloudEnabled)
            {
                _status = "Logged out. Local MDT is active.";
                _statusRow.AltTitle = _status;
                CloudLemonUiStyle.ApplyReadOnlyRow(_statusRow, CloudLemonUiStyle.TextMuted);
            }
            else if (!_hasSavedLogin)
            {
                _status = "Not signed in.";
                _statusRow.AltTitle = _status;
                CloudLemonUiStyle.ApplyReadOnlyRow(_statusRow, CloudLemonUiStyle.TextMuted);
            }
            else
            {
                _status = connected ? CloudPluginBridge.ConnectionStatusText : CloudPluginBridge.ConnectionDetail;
                if (string.IsNullOrWhiteSpace(_status)) _status = "Disconnected";
                _statusRow.AltTitle = _status;
                CloudLemonUiStyle.ApplyConnectionStatusRow(_statusRow, connected);
            }
            CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
        }

        void RefreshSavedLoginState()
        {
            _hasSavedLogin = CloudPluginBridge.HasSavedLogin();
        }

        void RefreshConnectionStatusIfDue()
        {
            if (_loginTask != null && !_loginTask.IsCompleted) return;
            if (DateTime.UtcNow < _nextStatusRefreshUtc) return;
            _nextStatusRefreshUtc = DateTime.UtcNow.AddSeconds(1);
            bool hadSavedLogin = _hasSavedLogin;
            RefreshSavedLoginState();
            if (hadSavedLogin != _hasSavedLogin) RefreshCredentialRows();
            if (_showingConnectionStatus || string.IsNullOrEmpty(_status))
                SetConnectionStatusFromBridge();
        }

        void BeginSignIn()
        {
            if (_loginTask != null && !_loginTask.IsCompleted) return;
            if (_logoutArmed || _logoutRequested) return;
            if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrWhiteSpace(_password))
            {
                SetStatus(_hasSavedLogin ? "Enter email and password to relink." : "Set email and password first.");
                CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                return;
            }
            string email = _email.Trim();
            string password = _password;
            string installId = EnsureInstallIdOnDisk();
            string deviceName = BuildDeviceName();
            try
            {
                _loginCts?.Cancel();
            }
            catch
            {
                /* ignore */
            }
            try
            {
                _loginCts?.Dispose();
            }
            catch
            {
                /* ignore */
            }
            _loginCts = new CancellationTokenSource();
            CancellationToken ct = _loginCts.Token;
            _loginGeneration = CloudPluginBridge.AuthGeneration;
            _busy.Show("");
            _loginTask = System.Threading.Tasks.Task.Run(() => TryLoginAndLink(email, password, installId, deviceName, _loginGeneration, ct), ct);
            ClearPasswordInput();
        }

        void DrainLoginTask()
        {
            if (_loginTask == null) return;
            if (!_loginTask.IsCompleted) return;
            _busy.Hide();
            try
            {
                bool logoutArmed = _logoutArmed;
                bool logoutRequested = _logoutRequested;
                (bool ok, string err) = _loginTask.GetAwaiter().GetResult();
                bool staleLogin = !CloudPluginBridge.IsAuthGenerationUnchanged(_loginGeneration);
                if (logoutRequested)
                {
                    FinalizeLogout();
                }
                else if (staleLogin)
                {
                    return;
                }
                else if (logoutArmed)
                {
                    SetStatus("Select logout again to confirm.");
                    CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                }
                else if (ok)
                {
                    RefreshSavedLoginState();
                    RefreshCredentialRows();
                    CloudPluginBridge.MarkConnected();
                    SetConnectionStatusFromBridge();
                    CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                    RunPostLoginBootstrapInBackground();
                    RageNotification.Show("MDT Cloud ~g~signed in~s~.", RageNotification.NotificationType.Success);
                    _quit = true;
                    try { _menu.Visible = false; } catch { /* ignore */ }
                    try { _pool?.HideAll(); } catch { /* ignore */ }
                }
                else
                {
                    SetStatus(err ?? "Failed");
                    CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                }
            }
            catch (OperationCanceledException)
            {
                if (_logoutRequested)
                {
                    FinalizeLogout();
                }
                else if (_logoutArmed)
                {
                    SetStatus("Select logout again to confirm.");
                    CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                }
                else
                {
                    SetStatus("Cancelled.");
                    CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                }
            }
            catch (Exception ex)
            {
                if (_logoutRequested)
                {
                    FinalizeLogout();
                }
                else if (_logoutArmed)
                {
                    SetStatus("Select logout again to confirm.");
                    CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                }
                else
                {
                    SetStatus(ex.Message);
                    CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                }
            }
            finally
            {
                ClearPasswordInput();
                _loginTask = null;
                try
                {
                    _loginCts?.Dispose();
                }
                catch
                {
                    /* ignore */
                }
                _loginCts = null;
            }
        }

        void ClearPasswordInput()
        {
            _password = string.Empty;
            if (_passwordInput != null && !_passwordInput.IsEditing)
                _passwordInput.SetValue(string.Empty, masked: true);
            if (_passwordRow != null)
                _passwordRow.AltTitle = string.Empty;
            CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
        }

        void LogoutCloud()
        {
            if (_logoutRequested) return;
            DateTime now = DateTime.UtcNow;
            if (now > _logoutConfirmUntilUtc)
            {
                _logoutConfirmUntilUtc = now.Add(LogoutConfirmationWindow);
                _logoutArmed = true;
                try
                {
                    _loginCts?.Cancel();
                }
                catch
                {
                    /* ignore */
                }
                SetStatus("Select logout again to confirm.");
                CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                return;
            }
            _logoutConfirmUntilUtc = DateTime.MinValue;
            _logoutArmed = false;
            _logoutRequested = true;
            try
            {
                _loginCts?.Cancel();
            }
            catch
            {
                /* ignore */
            }
            FinalizeLogout();
        }

        void FinalizeLogout()
        {
            if (!_logoutRequested) return;
            _logoutRequested = false;
            _logoutConfirmUntilUtc = DateTime.MinValue;
            _logoutArmed = false;
            Config cfg = Helper.ReadFromJsonFile<Config>(ConfigPath) ?? new Config();
            CloudCredentials credentials = CloudCredentialStore.Load(cfg);
            try
            {
                CloudPluginBridge.MarkLoggedOut(cfg, credentials);
            }
            catch { /* ignore */ }

            bool credentialsCleared = CloudCredentialStore.Clear();
            bool configPersisted = false;
            try
            {
                cfg.storageMode = "Local";
                cfg.cloudAccessToken = "";
                cfg.cloudRefreshToken = "";
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ClearCache();
                configPersisted = true;
            }
            catch (Exception ex)
            {
                Helper.Log($"Failed to persist MDT Cloud logout: {ex.Message}", false, Helper.LogSeverity.Warning);
            }

            if (!credentialsCleared || !configPersisted)
            {
                RefreshSavedLoginState();
                RefreshCredentialRows();
                SetStatus("Logout failed. Check MDT Pro log.");
                CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
                RageNotification.Show("MDT Cloud logout failed. Check MDT Pro log.", RageNotification.NotificationType.Error);
                return;
            }

            _hasSavedLogin = false;
            _email = string.Empty;
            _password = string.Empty;
            ClearPasswordInput();
            RefreshCredentialRows();
            SetStatus("Logged out. Local MDT is active.");
            CloudLemonUiStyle.FinishMenuItemColors(_menu, _statusRow);
            RageNotification.Show("MDT Cloud signed out. Local MDT active.", RageNotification.NotificationType.Info);
        }

        void RunPostLoginBootstrapInBackground()
        {
            int generation = CloudPluginBridge.AuthGeneration;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!CloudPluginBridge.IsCloudWorkCurrent(generation)) return;
                    CloudAuthorityClient.ApplyEffectiveConfig(generation);
                }
                catch (Exception ex)
                {
                    Helper.Log($"MDT Cloud policy after login: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
                try
                {
                    if (!CloudPluginBridge.IsCloudWorkCurrent(generation)) return;
                    CloudAuthorityClient.HydrateLocalCache(generation);
                }
                catch (Exception ex)
                {
                    Helper.Log($"MDT Cloud hydrate after login: {ex.Message}", false, Helper.LogSeverity.Warning);
                }
                try
                {
                    if (!CloudPluginBridge.IsCloudWorkCurrent(generation)) return;
                    CloudSyncQueue.TryFlushInBackground(respectFlushInterval: false);
                }
                catch { /* ignore */ }
            });
        }

        static string EnsureInstallIdOnDisk()
        {
            Config cfg = GetConfig();
            if (!string.IsNullOrWhiteSpace(cfg.cloudInstallId)) return cfg.cloudInstallId;
            cfg.cloudInstallId = Guid.NewGuid().ToString("N");
            try
            {
                Helper.WriteToJsonFile(ConfigPath, cfg);
                ClearCache();
            }
            catch { /* ignore */ }
            return cfg.cloudInstallId;
        }

        static string BuildDeviceName()
        {
            try
            {
                string n = Environment.MachineName ?? "MDT-Pro";
                if (n.Length > 120) n = n.Substring(0, 120);
                return n;
            }
            catch
            {
                return "MDT-Pro";
            }
        }

        /// <summary>
        /// Runs outside the game fiber on a worker task. Contract: POST <c>api/auth/login</c> JSON <c>{ "email", "password" }</c> (camelCase; server <c>LoginRequest</c>).
        /// Then POST <c>api/devices/link</c> with <c>Authorization: Bearer &lt;accessToken&gt;</c> and JSON <c>{ "deviceName", "installId" }</c> (server <c>DeviceLinkRequest</c>).
        /// On 401 from later calls, POST <c>api/auth/refresh</c> with JSON <c>{ "refreshToken": "&lt;stored refresh&gt;" }</c>; response shape matches login (<c>accessToken</c>, <c>refreshToken</c>, …).
        /// </summary>
        static (bool ok, string err) TryLoginAndLink(string email, string password, string installId, string deviceName, int generation, CancellationToken ct)
        {
            string baseUrl = CloudPublicApi.NormalizedBase();
            try
            {
                using (var http = new HttpClient { BaseAddress = new Uri(baseUrl + "/"), Timeout = TimeSpan.FromSeconds(30) })
                {
                    string loginBody = JsonConvert.SerializeObject(new { email, password });
                    using (HttpResponseMessage res = http.PostAsync("api/auth/login", new StringContent(loginBody, Encoding.UTF8, "application/json"), ct).GetAwaiter().GetResult())
                    {
                        string text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        ct.ThrowIfCancellationRequested();
                        if (!res.IsSuccessStatusCode)
                            return (false, res.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Invalid email or password." : "Login failed (" + (int)res.StatusCode + ").");
                        JObject o = JObject.Parse(text);
                        string access = o.Value<string>("accessToken");
                        string refresh = o.Value<string>("refreshToken");
                        if (string.IsNullOrWhiteSpace(access))
                            return (false, "Server did not return a token.");

                        string deviceToken = "";
                        using (var linkReq = new HttpRequestMessage(HttpMethod.Post, "api/devices/link"))
                        {
                            linkReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
                            string linkJson = JsonConvert.SerializeObject(new { deviceName, installId, refreshToken = refresh });
                            linkReq.Content = new StringContent(linkJson, Encoding.UTF8, "application/json");
                            using (HttpResponseMessage linkRes = http.SendAsync(linkReq, ct).GetAwaiter().GetResult())
                            {
                                string linkText = linkRes.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                ct.ThrowIfCancellationRequested();
                                if (!linkRes.IsSuccessStatusCode)
                                    return (false, "Device link failed (" + (int)linkRes.StatusCode + "). " + Truncate(linkText, 80));
                                deviceToken = JObject.Parse(linkText).Value<string>("deviceToken") ?? "";
                            }
                        }

                        ct.ThrowIfCancellationRequested();
                        if (!CloudPluginBridge.IsAuthGenerationUnchanged(generation))
                            throw new OperationCanceledException(ct);
                        Config cfg = Helper.ReadFromJsonFile<Config>(ConfigPath) ?? new Config();
                        cfg.storageMode = "Cloud";
                        cfg.cloudApiBaseUrl = baseUrl;
                        cfg.cloudAccessToken = "";
                        cfg.cloudRefreshToken = "";
                        cfg.cloudInstallId = installId;
                        if (!CloudPluginBridge.IsAuthGenerationUnchanged(generation))
                            throw new OperationCanceledException(ct);
                        CloudCredentialStore.Save(access, refresh ?? "", deviceToken);
                        if (!CloudPluginBridge.IsAuthGenerationUnchanged(generation))
                        {
                            CloudCredentialStore.Clear();
                            throw new OperationCanceledException(ct);
                        }
                        Helper.WriteToJsonFile(ConfigPath, cfg);
                        ClearCache();
                        return (true, null);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
