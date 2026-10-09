//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Concurrent;

namespace StreamEmber.Live.Internal
{
    internal enum LiveLogLevel
    {
        Error,
        Warning,
        Info,
        Debug,
    }

    /// <summary>
    /// "[Live]" lines in the runtime log (StreamEmber\Logs\Runtime.log + console). Network threads only enqueue;
    /// <see cref="Flush"/> writes on the script thread, because the runtime console is not thread-safe.
    /// Never log tokens.
    /// </summary>
    internal static class LiveLog
    {
        private static readonly ConcurrentQueue<Tuple<LiveLogLevel, string>> Pending = new ConcurrentQueue<Tuple<LiveLogLevel, string>>();

        public static bool DebugEnabled { get; set; }

        public static void Error(string message, Exception error = null) =>
            Write(LiveLogLevel.Error, error == null ? message : message + ": " + error);

        public static void Warn(string message) => Write(LiveLogLevel.Warning, message);

        public static void Info(string message) => Write(LiveLogLevel.Info, message);

        public static void Debug(string message)
        {
            if (DebugEnabled) Write(LiveLogLevel.Debug, message);
        }

        private static void Write(LiveLogLevel level, string message) => Raw(level, "[Live] " + message);

        /// <summary>A line with its own prefix (used by <see cref="ModLog"/>).</summary>
        public static void Raw(LiveLogLevel level, string line)
        {
            Pending.Enqueue(Tuple.Create(level, line));
            // Bounded: if the script thread is not ticking (loading screen), drop the oldest lines.
            while (Pending.Count > 500 && Pending.TryDequeue(out _)) { }
        }

        /// <summary>Script thread only.</summary>
        public static void Flush()
        {
            while (Pending.TryDequeue(out Tuple<LiveLogLevel, string> line))
            {
                try
                {
                    GameBridge.Log(line.Item1, line.Item2);
                }
                catch (Exception)
                {
                    // Logging must never break the tick.
                }
            }
        }
    }
}
