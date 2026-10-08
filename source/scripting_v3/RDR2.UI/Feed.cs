// StreamEmber: the game's own notification feed (UIFEED), built on ScriptStruct.

using RDR2.Native;

namespace RDR2.UI
{
	/// <summary>
	/// Posts messages to the game's notification feed.
	/// </summary>
	public static class Feed
	{
		/// <summary>
		/// Shows a sample toast (top right; title, subtitle and an icon from a texture dictionary).
		/// Struct layouts from ChaosModRDR (duration: ms, sound dict, sound; data: title, subtitle, icon dict/name/colour hashes).
		/// </summary>
		/// <param name="iconDict">Texture dictionary, e.g. "toast_mp_daily_objective_small" or "scoretimer_textures".</param>
		/// <param name="iconName">Texture name, e.g. "scoretimer_generic_tick".</param>
		/// <param name="iconColor">Colour name, e.g. "COLOR_PURE_WHITE", "COLOR_RED".</param>
		/// <returns>The feed item id (0 when the game rejected it).</returns>
		public static int ShowToast(string title, string subtitle, string iconDict, string iconName,
			int durationMs = 3000, string iconColor = "COLOR_PURE_WHITE", string soundset = null, string sound = null)
		{
			if (!string.IsNullOrEmpty(iconDict) && !TXD.HAS_STREAMED_TEXTURE_DICT_LOADED(iconDict))
			{
				TXD.REQUEST_STREAMED_TEXTURE_DICT(iconDict, false);
			}
			unsafe
			{
				using (var duration = new ScriptStruct(4))
				using (var data = new ScriptStruct(8))
				{
					duration.SetInt(0, durationMs);
					duration.SetString(1, soundset);
					duration.SetString(2, sound);

					data.SetString(1, MISC.VAR_STRING(10, "LITERAL_STRING", title ?? string.Empty));
					data.SetString(2, MISC.VAR_STRING(10, "LITERAL_STRING", subtitle ?? string.Empty));
					data.SetHash(4, iconDict);
					data.SetHash(5, iconName);
					data.SetHash(6, string.IsNullOrEmpty(iconColor) ? "COLOR_PURE_WHITE" : iconColor);

					return UIFEED._UI_FEED_POST_SAMPLE_TOAST(duration.Pointer, data.Pointer, true, true);
				}
			}
		}

		/// <summary>Removes every message from the feed.</summary>
		public static void ClearAll()
		{
			UIFEED._UI_FEED_CLEAR_ALL_CHANNELS();
		}
	}
}
