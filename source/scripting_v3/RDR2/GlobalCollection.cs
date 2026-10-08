//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/scripthookvdotnet#license
//

namespace RDR2
{
	public class GlobalCollection
	{
		internal GlobalCollection()
		{
		}

		public Global this[int globalId]
		{
			get => new Global(globalId);
			set
			{
				unsafe
				{
					System.IntPtr target = RDR2DN.NativeMemory.GetGlobalPtr(globalId);
					if (target == System.IntPtr.Zero)
					{
						throw new System.ArgumentOutOfRangeException(nameof(globalId), "Script global " + globalId + " is not available.");
					}
					if (value.MemoryAddress == null)
					{
						throw new System.ArgumentException("Source global is not initialized.", nameof(value));
					}
					*(ulong*)target.ToPointer() = *value.MemoryAddress;
				}
			}
		}

		/// <summary>
		/// Returns an instance of <see cref="Global"/> to a script global.
		/// </summary>
		/// <remarks>Make sure that you check the game version before accessing globals. ID's may differ between patches.</remarks>
		/// <param name="globalId">The script global index</param>
		public Global Get(int globalId)
		{
			return new Global(globalId);
		}
	}
}
