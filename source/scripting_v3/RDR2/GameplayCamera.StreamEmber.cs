// StreamEmber: GameplayCamera additions.

using RDR2.Native;

namespace RDR2
{
	public static partial class GameplayCamera
	{
		/// <summary>Forces the first person camera for this frame (call every frame).</summary>
		public static void ForceFirstPersonThisFrame()
		{
			CAM._FORCE_FIRST_PERSON_CAM_THIS_FRAME();
		}

		/// <summary>Starts a named camera shake, e.g. "DRUNK_SHAKE", "HAND_SHAKE", "JOLT_SHAKE".</summary>
		public static void Shake(string shakeName, float amplitude = 1f)
		{
			CAM.SHAKE_GAMEPLAY_CAM(shakeName, amplitude);
		}

		/// <summary>Stops the camera shake.</summary>
		public static void StopShaking(bool instantly = true)
		{
			CAM.STOP_GAMEPLAY_CAM_SHAKING(instantly);
		}
	}
}
