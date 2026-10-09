//
// StreamEmber Live: the RDR2 specific part. Everything under StreamEmber.Live/ is shared with
// gtav-runtime-scripthook and must stay identical; only this file differs per game.
//

namespace StreamEmber.Live
{
    public abstract partial class LiveScript : RDR2.Script
    {
    }
}

namespace StreamEmber.Live.Internal
{
    internal static class GameBridge
    {
        /// <summary>Presence source sent to EventFabric.</summary>
        public const string Source = "rdr2";

        public static string ConfigFile => RDR2DN.StreamEmberLayout.ConfigFile;

        public static string ProductVersion => RDR2DN.StreamEmberLayout.ProductVersion;

        /// <summary>GET_CURRENT_LANGUAGE (0 English … 12 Simplified Chinese; same order as GTA V). Script thread.</summary>
        public static int LanguageId() => RDR2.Native.LOCALIZATION.GET_CURRENT_LANGUAGE();

        /// <summary>System message: F4 console + small bottom-left status line (warning = orange, error = red). Script thread.</summary>
        public static void Notify(string message, LiveLogLevel level = LiveLogLevel.Info) =>
            RDR2DN.Console.Status(level == LiveLogLevel.Error ? 3 : level == LiveLogLevel.Warning ? 2 : 0, message);

        /// <summary>Incoming live action: F4 console + bottom-left status line instead of a large subtitle. Script thread.</summary>
        public static void Announce(string message) => RDR2DN.Console.Status(1, message);

        public static void Log(LiveLogLevel level, string message)
        {
            RDR2DN.Log.Level mapped;
            switch (level)
            {
                case LiveLogLevel.Error: mapped = RDR2DN.Log.Level.Error; break;
                case LiveLogLevel.Warning: mapped = RDR2DN.Log.Level.Warning; break;
                case LiveLogLevel.Debug: mapped = RDR2DN.Log.Level.Debug; break;
                default: mapped = RDR2DN.Log.Level.Info; break;
            }
            RDR2DN.Log.Message(mapped, message);
        }
    }
}
