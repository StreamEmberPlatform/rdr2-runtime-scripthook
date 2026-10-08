// StreamEmber: Game additions.

using RDR2.Native;

namespace RDR2
{
	public static partial class Game
	{
		/// <summary>
		/// Switches the control context for this frame (e.g. Game.Joaat("OnMount") lets a bird-model player fly).
		/// <paramref name="control"/>: 0 player, 1 camera, 2 frontend.
		/// </summary>
		public static void SetControlContext(int control, uint contextHash)
		{
			PAD._SET_CONTROL_CONTEXT(control, contextHash);
		}
	}
}
