// StreamEmber: Hud additions.

using RDR2.Native;

namespace RDR2.UI
{
	public static partial class Hud
	{
		/// <summary>Hides the HUD and the radar for this frame (call every frame).</summary>
		public static void HideThisFrame()
		{
			HUD.HIDE_HUD_AND_RADAR_THIS_FRAME();
		}
	}
}
