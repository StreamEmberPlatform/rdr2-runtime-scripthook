//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using StreamEmber.Live.Internal;

namespace StreamEmber.Live
{
    /// <summary>
    /// Log for mods: lines go to StreamEmber\Logs\Runtime.log (and the runtime console) as "[Mod:&lt;code&gt;] …".
    /// Safe from any thread (lines are written on the next tick). Debug lines only with LiveDebug=true in Runtime.ini.
    /// </summary>
    public static class ModLog
    {
        public static void Info(string message) => Write(LiveLogLevel.Info, message);

        public static void Warn(string message) => Write(LiveLogLevel.Warning, message);

        public static void Error(string message, Exception error = null) =>
            Write(LiveLogLevel.Error, error == null ? message : message + ": " + error);

        public static void Debug(string message)
        {
            if (LiveLog.DebugEnabled) Write(LiveLogLevel.Debug, message);
        }

        private static void Write(LiveLogLevel level, string message)
        {
            string code = LiveService.ActiveManifest?.Code;
            LiveLog.Raw(level, "[Mod" + (string.IsNullOrEmpty(code) ? string.Empty : ":" + code) + "] " + message);
        }
    }
}
