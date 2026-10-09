//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Generic;
using System.IO;

namespace StreamEmber.Live.Internal
{
    /// <summary>
    /// "Live*" keys of StreamEmber\Config\Runtime.ini. Missing keys use the defaults below, so an older
    /// Runtime.ini (kept on update) still works.
    /// </summary>
    internal sealed class LiveConfig
    {
        public const string DefaultEventFabricUrl = "https://eventfabric.streamember.com";
        public const string DefaultFalconUrl = "https://falcon.streamember.com";
        public const string DefaultLauncherUrl = "http://127.0.0.1:47880";

        /// <summary>Set by the launcher for every game it starts.</summary>
        public const string LauncherUrlVariable = "STREAMEMBER_LOCAL_API";

        public bool Enabled { get; private set; } = true;
        public string EventFabricUrl { get; private set; } = DefaultEventFabricUrl;
        public string FalconUrl { get; private set; } = DefaultFalconUrl;
        public string LauncherUrl { get; private set; } = DefaultLauncherUrl;

        /// <summary>Development only: customer UUID to read events and settings for when the launcher is not running.</summary>
        public string CustomerUuid { get; private set; } = string.Empty;

        /// <summary>Code or application UUID of the module to activate when several are installed; empty = first.</summary>
        public string Module { get; private set; } = string.Empty;

        public int PresenceSeconds { get; private set; } = 15;
        public int SettingsSeconds { get; private set; } = 60;
        public bool AnnounceActions { get; private set; } = true;
        public bool TestHotkeys { get; private set; } = true;
        public bool Debug { get; private set; }

        public string EventFabricWsUrl
        {
            get
            {
                string url = EventFabricUrl;
                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "wss://" + url.Substring(8);
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return "ws://" + url.Substring(7);
                return url;
            }
        }

        public static LiveConfig Load()
        {
            var config = new LiveConfig();
            Dictionary<string, string> values = ReadIni(GameBridge.ConfigFile);

            config.Enabled = Bool(values, "LiveEnabled", true);
            config.EventFabricUrl = Url(values, "LiveEventFabricUrl", DefaultEventFabricUrl);
            config.FalconUrl = Url(values, "LiveFalconUrl", DefaultFalconUrl);
            string launcher = Url(values, "LiveLauncherUrl", string.Empty);
            if (launcher.Length == 0) launcher = Url(Environment.GetEnvironmentVariable(LauncherUrlVariable), DefaultLauncherUrl);
            config.LauncherUrl = launcher;
            config.CustomerUuid = Text(values, "LiveCustomerUuid");
            config.Module = Text(values, "LiveModule");
            config.PresenceSeconds = Int(values, "LivePresenceSeconds", 15, 5, 300);
            config.SettingsSeconds = Int(values, "LiveSettingsSeconds", 60, 10, 3600);
            config.AnnounceActions = Bool(values, "LiveAnnounceActions", true);
            config.TestHotkeys = Bool(values, "LiveTestHotkeys", true);
            config.Debug = Bool(values, "LiveDebug", false);
            return config;
        }

        private static Dictionary<string, string> ReadIni(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return values;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[' || line.StartsWith("//", StringComparison.Ordinal))
                        continue;
                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;
                    string value = line.Substring(equals + 1).Trim();
                    if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"') value = value.Substring(1, value.Length - 2);
                    values[line.Substring(0, equals).Trim()] = value;
                }
            }
            catch (Exception error)
            {
                LiveLog.Warn("Runtime.ini could not be read; using defaults. " + error.Message);
            }
            return values;
        }

        private static string Text(Dictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string value) ? value.Trim() : string.Empty;

        private static string Url(Dictionary<string, string> values, string key, string fallback) => Url(Text(values, key), fallback);

        private static string Url(string value, string fallback)
        {
            string url = (value ?? string.Empty).Trim().TrimEnd('/');
            return url.Length > 0 ? url : fallback;
        }

        private static bool Bool(Dictionary<string, string> values, string key, bool fallback)
        {
            string text = Text(values, key);
            if (bool.TryParse(text, out bool parsed)) return parsed;
            if (text == "1") return true;
            if (text == "0") return false;
            return fallback;
        }

        private static int Int(Dictionary<string, string> values, string key, int fallback, int min, int max)
        {
            int value = int.TryParse(Text(values, key), out int parsed) ? parsed : fallback;
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
