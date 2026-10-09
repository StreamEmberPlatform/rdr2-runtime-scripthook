//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using StreamEmber.Live.Internal;

namespace StreamEmber.Live
{
    /// <summary>
    /// State of the live session of the active module (identity, connection, settings). Call from the script thread.
    /// (Not named "Live": a type with its namespace's name breaks lookups inside StreamEmber.* namespaces.)
    /// </summary>
    public static class LiveSession
    {
        /// <summary>A live module is active in this game session.</summary>
        public static bool IsActive => LiveService.ActiveManifest != null;

        /// <summary>The active module's manifest; null when none is active.</summary>
        public static LiveManifest Manifest => LiveService.ActiveManifest;

        public static string ApplicationUuid => LiveService.ActiveManifest?.ApplicationUuid ?? string.Empty;

        /// <summary>The streamer's customer UUID; empty until the launcher (or a token) told it.</summary>
        public static string CustomerUuid => LiveService.CustomerUuid ?? string.Empty;

        /// <summary>The EventFabric connection is open.</summary>
        public static bool IsConnected => LiveService.IsConnected;

        /// <summary>A runtime token is at hand, so writes to EventFabric (presence, GCore reset) are possible.</summary>
        public static bool CanWrite => LiveService.CanWrite;

        /// <summary>Schema defaults merged with the streamer's Falcon settings.</summary>
        public static LiveValues Settings => LiveService.Settings;

        /// <summary>Falcon revision of <see cref="Settings"/>; "schema" before the first read.</summary>
        public static string SettingsRevision => LiveService.SettingsRevision;

        /// <summary>Game language code: en, fr, de, it, es, pt, pl, ru, ko, zh-TW, ja, zh (for schema labels).</summary>
        public static string Language => LiveService.Language;

        /// <summary>Reads the settings from Falcon now (SettingsChanged follows if they changed).</summary>
        public static void ReloadSettings() => LiveService.RequestSettings();

        /// <summary>
        /// Clears GCore's counters, aggregations and pending actions for this customer and game (e.g. when a round ends).
        /// Needs a runtime token. <paramref name="done"/> runs on the script thread with the result.
        /// </summary>
        public static void ResetGCore(Action<bool> done = null) => LiveService.ResetGCore(done);
    }
}
