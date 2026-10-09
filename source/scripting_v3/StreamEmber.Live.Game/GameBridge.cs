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

        /// <summary>Feed toast. Script thread.</summary>
        public static void Notify(string message) =>
            RDR2.UI.Feed.ShowToast("StreamEmber", message ?? string.Empty, "scoretimer_textures", "scoretimer_generic_tick", 4000);

        /// <summary>Short on-screen line for an incoming action. Script thread.</summary>
        public static void Announce(string message) => RDR2.UI.Screen.PrintSubtitle(message ?? string.Empty);

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
