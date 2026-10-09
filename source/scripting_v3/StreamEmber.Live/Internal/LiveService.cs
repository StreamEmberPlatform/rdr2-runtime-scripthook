//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StreamEmber.Live.Internal
{
    /// <summary>
    /// The live core: module selection, identity, EventFabric connection, Falcon settings, presence, action dispatch.
    /// Not a script of its own: the first active <see cref="LiveScript"/> drives it from its Tick, so script load
    /// order does not matter. All game and script calls happen on the script thread; network results arrive
    /// through <see cref="MainThread"/> / <see cref="Incoming"/>. One instance per script domain (a reload starts over).
    /// </summary>
    internal static class LiveService
    {
        private const int RememberedIds = 4096;
        private const int ActionsPerTick = 25;
        private const int MaxPendingActions = 2048;
        private static int _overflowCount;

        private static readonly object Gate = new object();
        private static readonly List<LiveScript> Registered = new List<LiveScript>();
        private static readonly List<LiveScript> Active = new List<LiveScript>();
        private static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();
        private static readonly ConcurrentQueue<Dictionary<string, object>> Incoming = new ConcurrentQueue<Dictionary<string, object>>();
        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> SeenOrder = new Queue<string>();
        private static readonly HashSet<string> UnknownWarned = new HashSet<string>(StringComparer.Ordinal);

        private static LiveConfig _config;
        private static LiveManifest _manifest;
        private static LiveIdentity _identity;
        private static EventFabricClient _connection;
        private static volatile string _customerUuid;
        private static string _customerSource = string.Empty;
        private static string _language = "en";
        private static Dictionary<string, object> _settings = Json.NewObject();
        private static string _settingsFingerprint = string.Empty;
        private static string _settingsRevision = "schema";
        private static bool _activated;
        private static bool _disabled;
        private static bool _resolving;
        private static bool _warnedNoCustomer;
        private static bool _warnedNoToken;
        private static bool _announcedConnection;
        private static DateTime _nextPresence = DateTime.MinValue;
        private static DateTime _nextSettings = DateTime.MaxValue;

        static LiveService()
        {
            AppDomain.CurrentDomain.DomainUnload += (sender, args) => CloseConnection();
        }

        // ─── State for Live ─────────────────────────────────────────────────

        public static LiveManifest ActiveManifest => _activated ? _manifest : null;
        public static string CustomerUuid => _customerUuid;
        public static bool IsConnected => _connection != null && _connection.IsConnected;
        public static bool CanWrite => _identity?.Peek() != null;
        public static LiveValues Settings => new LiveValues(Json.CloneObject(_settings));
        public static string SettingsRevision => _settingsRevision;
        public static string Language => _language;

        public static void Register(LiveScript script)
        {
            lock (Gate) Registered.Add(script);
        }

        // ─── Script events (script thread) ──────────────────────────────────

        public static void OnTick(LiveScript script)
        {
            if (!_activated && !_disabled) Activate();
            if (!_activated)
            {
                LiveLog.Flush();
                return;
            }

            if (script.LiveState == LiveScript.StatePending)
            {
                if (string.Equals(script.Manifest.ApplicationUuid, _manifest.ApplicationUuid, StringComparison.Ordinal)) Attach(script);
                else script.LiveState = LiveScript.StateIgnored;
            }
            if (Active.Count == 0 || script != Active[0]) return;

            LiveLog.Flush();
            for (int budget = 0; budget < 32 && MainThread.TryDequeue(out Action work); budget++)
            {
                try
                {
                    work();
                }
                catch (Exception error)
                {
                    LiveLog.Error("Live main-thread task failed", error);
                }
            }

            DateTime now = DateTime.UtcNow;
            if (now >= _nextPresence)
            {
                _nextPresence = now.AddSeconds(_config.PresenceSeconds);
                Heartbeat();
            }
            if (now >= _nextSettings)
            {
                _nextSettings = now.AddSeconds(_config.SettingsSeconds);
                RefreshSettings(false);
            }

            for (int i = 0; i < ActionsPerTick && Incoming.TryDequeue(out Dictionary<string, object> json); i++)
                Dispatch(new LiveAction(json, false));
            LiveLog.Flush();
        }

        public static void OnKeyDown(LiveScript script, KeyEventArgs key)
        {
            if (!_activated || Active.Count == 0 || script != Active[0] || !_config.TestHotkeys) return;
            if (key.Control && key.Shift && key.KeyCode >= Keys.F1 && key.KeyCode <= Keys.F12) HandleHotkey(key.KeyCode - Keys.F1);
        }

        public static void OnAborted(LiveScript script)
        {
            lock (Gate) Registered.Remove(script);
            if (script.LiveState != LiveScript.StateActive) return;
            Active.Remove(script);
            script.LiveState = LiveScript.StateIgnored;
            script.IsLiveActive = false;
            script.RaiseStopped();
            if (Active.Count > 0) return;

            LiveLog.Info("Module '" + _manifest?.Code + "' stopped.");
            LiveLog.Flush();
            Reset();
        }

        // ─── Activation ─────────────────────────────────────────────────────

        private static void Activate()
        {
            List<LiveScript> scripts;
            lock (Gate) scripts = Registered.ToList();
            if (scripts.Count == 0) return;

            _config = LiveConfig.Load();
            LiveLog.DebugEnabled = _config.Debug;
            if (!_config.Enabled)
            {
                _disabled = true;
                LiveLog.Info("Live is off (LiveEnabled=false in Runtime.ini).");
                return;
            }

            LiveManifest chosen = scripts[0].Manifest;
            if (_config.Module.Length > 0)
            {
                chosen = scripts.Select(s => s.Manifest).FirstOrDefault(m =>
                    string.Equals(m.Code, _config.Module, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(m.ApplicationUuid, _config.Module, StringComparison.OrdinalIgnoreCase));
                if (chosen == null)
                {
                    _disabled = true;
                    LiveLog.Error("LiveModule '" + _config.Module + "' is not installed. Installed: "
                                  + string.Join(", ", scripts.Select(s => s.Manifest.Code).Distinct()));
                    GameBridge.Notify("'" + _config.Module + "' modülü yüklü değil");
                    return;
                }
            }
            string[] codes = scripts.Select(s => s.Manifest.Code).Distinct().ToArray();
            if (codes.Length > 1)
                LiveLog.Warn("Several live modules installed (" + string.Join(", ", codes) + "); active: " + chosen.Code
                             + ". Set LiveModule in Runtime.ini.");

            _manifest = chosen;
            _language = LanguageCode(GameBridge.LanguageId());
            _settings = chosen.SettingDefaultsRaw();
            _settingsFingerprint = Json.Write(_settings);
            _settingsRevision = "schema";
            _identity = new LiveIdentity(_config, chosen.ApplicationUuid);
            _activated = true;
            LiveLog.Info("Module '" + chosen.Code + "' (" + chosen.ApplicationUuid + ") active; runtime " + GameBridge.ProductVersion + ".");

            foreach (LiveScript script in scripts)
            {
                if (string.Equals(script.Manifest.ApplicationUuid, chosen.ApplicationUuid, StringComparison.Ordinal)) Attach(script);
                else script.LiveState = LiveScript.StateIgnored;
            }
            ValidateHandlers();
            ResolveIdentity();
        }

        private static void Attach(LiveScript script)
        {
            script.LiveState = LiveScript.StateActive;
            script.IsLiveActive = true;
            Active.Add(script);
            script.RaiseStarted();
            script.RaiseSettingsChanged(new LiveSettingsEventArgs(Settings, _settingsRevision));
            if (IsConnected) script.RaiseConnectionChanged(true);
        }

        private static void ValidateHandlers()
        {
            var handled = new HashSet<string>(Active.SelectMany(s => s.HandlerIds), StringComparer.Ordinal);
            if (!Active.Any(s => s.ListensToAllActions))
                foreach (string id in _manifest.ActionIds.Where(id => !handled.Contains(id)))
                    LiveLog.Warn("Module '" + _manifest.Code + "': schema action '" + id + "' has no handler.");
            foreach (string id in handled.Where(id => !_manifest.ActionIds.Contains(id)))
                LiveLog.Warn("Module '" + _manifest.Code + "': handler '" + id + "' is not in game-schema.json.");
        }

        /// <summary>Last active script stopped: close everything so the next started script activates again.</summary>
        private static void Reset()
        {
            CloseConnection();
            _activated = false;
            _disabled = false;
            _manifest = null;
            _identity = null;
            _customerUuid = null;
            _customerSource = string.Empty;
            _resolving = false;
            _warnedNoCustomer = false;
            _warnedNoToken = false;
            _announcedConnection = false;
            _nextPresence = DateTime.MinValue;
            _nextSettings = DateTime.MaxValue;
            while (MainThread.TryDequeue(out _)) { }
            while (Incoming.TryDequeue(out _)) { }
            lock (Gate)
            {
                foreach (LiveScript script in Registered) script.LiveState = LiveScript.StatePending;
            }
        }

        private static void CloseConnection()
        {
            EventFabricClient connection = _connection;
            _connection = null;
            connection?.Dispose();
        }

        // ─── Identity and connection ────────────────────────────────────────

        private static void ResolveIdentity()
        {
            LiveIdentity identity = _identity;
            if (_resolving || identity == null) return;
            _resolving = true;
            Task.Run(async () =>
            {
                KeyValuePair<string, string> found = new KeyValuePair<string, string>(null, string.Empty);
                try
                {
                    found = await identity.ResolveCustomerAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    LiveLog.Warn("Customer lookup failed: " + Http.Describe(error));
                }
                MainThread.Enqueue(() => IdentityResolved(identity, found.Key, found.Value));
            });
        }

        private static void IdentityResolved(LiveIdentity identity, string customerUuid, string source)
        {
            if (identity != _identity) return;
            _resolving = false;
            if (customerUuid == null)
            {
                if (!_warnedNoCustomer)
                {
                    _warnedNoCustomer = true;
                    bool offline = identity.CustomerProblem == "launcher_offline";
                    LiveLog.Warn("No customer UUID (" + (offline ? "launcher not reachable at " + _config.LauncherUrl : "launcher not signed in")
                                 + "). Retrying every " + _config.PresenceSeconds + " s.");
                    GameBridge.Notify(offline
                        ? "Canlı aksiyonlar için StreamEmber Launcher'ı aç ve giriş yap"
                        : "Canlı aksiyonlar için Launcher'da giriş yap");
                }
                return;
            }
            if (customerUuid == _customerUuid && _connection != null) return;

            _warnedNoCustomer = false;
            _customerSource = source;
            _customerUuid = customerUuid;
            LiveLog.Info("Customer " + customerUuid + " (" + source + ").");

            CloseConnection();
            _announcedConnection = false;
            var connection = new EventFabricClient(_config.EventFabricWsUrl, customerUuid, _manifest.ApplicationUuid, identity.Peek, FromNetwork);
            connection.ConnectionChanged += connected => MainThread.Enqueue(() => ConnectionChanged(connection, connected));
            _connection = connection;
            connection.Start();

            _nextPresence = DateTime.MinValue;
            RefreshSettings(true);
            _nextSettings = DateTime.UtcNow.AddSeconds(_config.SettingsSeconds);
        }

        private static void ConnectionChanged(EventFabricClient connection, bool connected)
        {
            if (connection != _connection || !_activated) return;
            foreach (LiveScript script in Active.ToArray()) script.RaiseConnectionChanged(connected);
            if (connected && !_announcedConnection)
            {
                _announcedConnection = true;
                GameBridge.Notify(_manifest.Name + " canlı yayına bağlandı");
            }
        }

        /// <summary>Presence (needs a runtime token), launcher notification, and a fresh customer lookup.</summary>
        private static void Heartbeat()
        {
            if (_customerUuid == null || _customerSource != "config") ResolveIdentity();
            string customer = _customerUuid;
            LiveIdentity identity = _identity;
            if (customer == null || identity == null) return;

            string application = _manifest.ApplicationUuid;
            string presenceUrl = _config.EventFabricUrl + "/presence?" + Http.Query(new[]
            {
                new KeyValuePair<string, string>("customerUuid", customer),
                new KeyValuePair<string, string>("applicationUuid", application),
                new KeyValuePair<string, string>("source", GameBridge.Source)
            });
            string launcherUrl = _config.LauncherUrl;
            bool notifyLauncher = identity.CustomerProblem != "launcher_offline";
            Task.Run(async () =>
            {
                RuntimeToken token = await identity.GetTokenAsync().ConfigureAwait(false);
                if (token == null)
                {
                    MainThread.Enqueue(() => NoWriteAccess(identity));
                }
                else if (!string.Equals(token.Subject, customer, StringComparison.OrdinalIgnoreCase))
                {
                    LiveLog.Warn("The runtime token is for customer " + token.Subject + ", not " + customer + "; presence skipped.");
                }
                else
                {
                    try
                    {
                        await Http.GetJsonAsync(presenceUrl, token.Value).ConfigureAwait(false);
                        MainThread.Enqueue(() => _warnedNoToken = false);
                    }
                    catch (Exception error)
                    {
                        if (Http.IsStatus(error, 401)) identity.Invalidate();
                        LiveLog.Debug("Presence failed: " + Http.Describe(error));
                    }
                }

                if (!notifyLauncher) return;
                try
                {
                    var body = Json.NewObject();
                    body["applicationUuid"] = application;
                    await Http.PostJsonAsync(launcherUrl + "/api/active-application", body).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The launcher notification only feeds its UI.
                }
            });
        }

        private static void NoWriteAccess(LiveIdentity identity)
        {
            if (identity != _identity || _warnedNoToken) return;
            _warnedNoToken = true;
            LiveLog.Warn("No runtime token (" + identity.TokenProblem + "): presence and other EventFabric writes are skipped. "
                         + "Live actions still arrive.");
        }

        // ─── Settings ───────────────────────────────────────────────────────

        public static void RequestSettings()
        {
            if (_activated) RefreshSettings(true);
        }

        /// <summary>Falcon public config → settings. Reading needs no token; a cached one is sent along when present.</summary>
        private static void RefreshSettings(bool notifyAlways)
        {
            string customer = _customerUuid;
            LiveManifest manifest = _manifest;
            if (customer == null || manifest == null) return;
            string url = _config.FalconUrl + "/public/game/config/" + Http.Encode(customer) + "/" + Http.Encode(manifest.ApplicationUuid);
            string bearer = _identity?.Peek()?.Value;
            Task.Run(async () =>
            {
                Dictionary<string, object> config = null;
                try
                {
                    try
                    {
                        config = await Http.GetJsonAsync(url, bearer).ConfigureAwait(false);
                    }
                    catch (Exception error) when (bearer != null && (Http.IsStatus(error, 401) || Http.IsStatus(error, 403)))
                    {
                        config = await Http.GetJsonAsync(url).ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    LiveLog.Warn("Game settings could not be loaded from Falcon (" + Http.Describe(error) + "); keeping current settings.");
                }
                MainThread.Enqueue(() =>
                {
                    if (customer != _customerUuid || manifest != _manifest) return;
                    bool changed = config != null && ApplyConfig(config);
                    if (changed) LiveLog.Info("Settings revision " + _settingsRevision + " applied.");
                    if (!changed && !notifyAlways) return;
                    var args = new LiveSettingsEventArgs(Settings, _settingsRevision);
                    foreach (LiveScript script in Active.ToArray()) script.RaiseSettingsChanged(args);
                });
            });
        }

        private static bool ApplyConfig(Dictionary<string, object> config)
        {
            Dictionary<string, object> merged = Json.Merge(_manifest.SettingDefaultsRaw(), Json.Obj(config, "settings"));
            string revision = Json.Text(config, "revision");
            _settingsRevision = revision.Length > 0 ? revision : "unknown";
            string fingerprint = Json.Write(merged);
            if (fingerprint == _settingsFingerprint) return false;
            _settingsFingerprint = fingerprint;
            _settings = merged;
            return true;
        }

        // ─── GCore ──────────────────────────────────────────────────────────

        public static void ResetGCore(Action<bool> done)
        {
            string customer = _customerUuid;
            LiveIdentity identity = _identity;
            if (!_activated || customer == null || identity == null)
            {
                done?.Invoke(false);
                return;
            }
            string url = _config.EventFabricUrl + "/gcore/reset?" + Http.Query(new[]
            {
                new KeyValuePair<string, string>("customerUuid", customer),
                new KeyValuePair<string, string>("applicationUuid", _manifest.ApplicationUuid)
            });
            Task.Run(async () =>
            {
                bool ok = false;
                RuntimeToken token = await identity.GetTokenAsync().ConfigureAwait(false);
                if (token == null)
                {
                    LiveLog.Warn("GCore reset needs a runtime token (" + identity.TokenProblem + ").");
                }
                else
                {
                    try
                    {
                        await Http.GetJsonAsync(url, token.Value).ConfigureAwait(false);
                        ok = true;
                    }
                    catch (Exception error)
                    {
                        if (Http.IsStatus(error, 401)) identity.Invalidate();
                        LiveLog.Warn("GCore reset failed: " + Http.Describe(error));
                    }
                }
                if (done != null) MainThread.Enqueue(() => done(ok));
            });
        }

        // ─── Actions ────────────────────────────────────────────────────────

        /// <summary>Network thread: drop duplicates, queue for the script thread.</summary>
        private static void FromNetwork(Dictionary<string, object> json)
        {
            string id = Json.Text(json, "id");
            if (id.Length == 0) return;
            lock (Seen)
            {
                if (Seen.Contains(id)) return;
                // StreamEmber: bound replay/event bursts; keep accepted actions in FIFO order.
                if (Incoming.Count >= MaxPendingActions)
                {
                    if (++_overflowCount == 1 || _overflowCount % 100 == 0)
                        LiveLog.Warn("Live action queue full; rejected " + _overflowCount + " action(s). Reduce event rate.");
                    return;
                }
                Seen.Add(id);
                SeenOrder.Enqueue(id);
                while (SeenOrder.Count > RememberedIds) Seen.Remove(SeenOrder.Dequeue());
                Incoming.Enqueue(json);
            }
        }

        /// <summary>Script thread.</summary>
        private static void Dispatch(LiveAction action)
        {
            LiveManifest manifest = _manifest;
            if (manifest == null) return;
            if (action.ApplicationUuid.Length > 0
                && !string.Equals(action.ApplicationUuid, manifest.ApplicationUuid, StringComparison.OrdinalIgnoreCase))
                return;

            // Falcon/EventFabric carry only the arguments the streamer saved; the schema fills the rest.
            action.ApplyArgumentDefaults(manifest.ArgumentDefaults(action.Action));
            LiveLog.Debug(action + " " + action.Arguments);
            if (_config.AnnounceActions)
            {
                try
                {
                    GameBridge.Announce(action.Describe() + "  >  " + manifest.ActionLabel(action.Action, _language)
                                        + (action.Quantity > 1 ? " x" + action.Quantity : string.Empty) + (action.IsTest ? " (test)" : string.Empty));
                }
                catch (Exception error)
                {
                    LiveLog.Debug("Announce failed: " + error.Message);
                }
            }

            bool delivered = false;
            foreach (LiveScript script in Active.ToArray()) delivered |= script.Deliver(action);
            if (!delivered && UnknownWarned.Add(action.Action))
                LiveLog.Warn("Module '" + manifest.Code + "' has no handler for action '" + action.Action
                             + "'. Check game-schema.json and the script's On(…) / [LiveAction] handlers.");
        }

        /// <summary>Ctrl+Shift+F1…F9: schema action (test), F10 status, F11 reload settings, F12 GCore reset.</summary>
        private static void HandleHotkey(int index)
        {
            if (index <= 8)
            {
                if (index >= _manifest.ActionIds.Count) return;
                var trigger = Json.NewObject();
                trigger["platform"] = "streamember";
                trigger["kind"] = "manual";
                trigger["event"] = "streamember.manual";
                var viewer = Json.NewObject();
                viewer["id"] = "streamember";
                viewer["username"] = "streamember";
                viewer["nickname"] = "Test";
                var json = Json.NewObject();
                json["schemaVersion"] = 2;
                json["id"] = "test-" + Guid.NewGuid().ToString("N");
                json["customerUuid"] = _customerUuid ?? string.Empty;
                json["applicationUuid"] = _manifest.ApplicationUuid;
                json["action"] = _manifest.ActionIds[index];
                json["arguments"] = Json.NewObject();
                json["quantity"] = 1;
                json["ruleId"] = "test";
                json["trigger"] = trigger;
                json["viewer"] = viewer;
                Dispatch(new LiveAction(json, true));
            }
            else if (index == 9)
            {
                GameBridge.Notify(_manifest.Name
                                  + " | müşteri: " + (_customerUuid != null ? _customerUuid.Substring(0, 8) + "…" : "yok")
                                  + " | EventFabric: " + (IsConnected ? "bağlı" : "bağlı değil")
                                  + " | yazma: " + (CanWrite ? "var" : "yok")
                                  + " | ayarlar: " + _settingsRevision);
            }
            else if (index == 10)
            {
                RefreshSettings(true);
                GameBridge.Notify("Ayarlar yeniden yükleniyor");
            }
            else if (index == 11)
            {
                ResetGCore(ok => GameBridge.Notify(ok ? "GCore sıfırlandı" : "GCore sıfırlanamadı"));
            }
        }

        /// <summary>Game language id (same order in GTA V and RDR2) → code.</summary>
        private static string LanguageCode(int id)
        {
            switch (id)
            {
                case 1: return "fr";
                case 2: return "de";
                case 3: return "it";
                case 4: return "es";
                case 5: return "pt";
                case 6: return "pl";
                case 7: return "ru";
                case 8: return "ko";
                case 9: return "zh-TW";
                case 10: return "ja";
                case 11: return "es";
                case 12: return "zh";
                default: return "en";
            }
        }
    }
}
