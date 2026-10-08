// StreamEmber: Audio additions.

using RDR2.Native;

namespace RDR2
{
	public static partial class Audio
	{
		/// <summary>Plays a sound that follows <paramref name="entity"/> (call <see cref="PrepareSoundset"/> first for script sounds).</summary>
		public static void PlaySoundFromEntity(string soundName, Entity entity, string soundset)
		{
			if (entity == null)
			{
				return;
			}
			AUDIO.PLAY_SOUND_FROM_ENTITY(soundName, entity.Handle, soundset, false, 0, 0);
		}
	}
}
