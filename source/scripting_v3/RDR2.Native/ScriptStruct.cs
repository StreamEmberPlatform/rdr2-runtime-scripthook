// StreamEmber: helper for natives that take or fill a script struct (Any* / ulong*).
//
// Script structs are arrays of 8-byte slots: an int, a float, a hash or a BOOL sits in the low 4 bytes of its slot,
// a const char* or an entity handle uses the whole slot. Natives such as GET_PED_NEARBY_PEDS
// ({ int size; int handles[] }), _UI_FEED_POST_SAMPLE_TOAST and _GET_SHOP_ITEM_COMPONENT_AT_INDEX need one.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace RDR2.Native
{
	/// <summary>
	/// An unmanaged script struct made of 8-byte slots, for natives that take an <c>Any*</c> argument.
	/// Strings written with <see cref="SetString"/> stay alive until the struct is disposed.
	/// </summary>
	/// <example>
	/// <code>
	/// using (var buffer = new ScriptStruct(1 + 32))
	/// {
	///     buffer.SetInt(0, 32);
	///     int found = PED.GET_PED_NEARBY_PEDS(ped, buffer.Pointer, -1, 0);
	///     int first = buffer.GetInt(1);
	/// }
	/// </code>
	/// </example>
	public sealed unsafe class ScriptStruct : IDisposable
	{
		const int SlotSize = 8;

		ulong* _data;
		List<IntPtr> _strings;

		/// <summary>Allocates a zeroed struct of <paramref name="slotCount"/> 8-byte slots.</summary>
		public ScriptStruct(int slotCount)
		{
			if (slotCount <= 0 || slotCount > 4096)
			{
				throw new ArgumentOutOfRangeException(nameof(slotCount));
			}
			SlotCount = slotCount;
			_data = (ulong*)Marshal.AllocHGlobal(slotCount * SlotSize).ToPointer();
			Clear();
		}

		~ScriptStruct()
		{
			Free();
		}

		/// <summary>Number of 8-byte slots.</summary>
		public int SlotCount { get; }

		/// <summary>The pointer to pass to the native (<c>Any*</c>).</summary>
		public ulong* Pointer
		{
			get
			{
				if (_data == null)
				{
					throw new ObjectDisposedException(nameof(ScriptStruct));
				}
				return _data;
			}
		}

		/// <summary>Sets every slot to zero (strings stay allocated until <see cref="Dispose"/>).</summary>
		public void Clear()
		{
			for (int i = 0; i < SlotCount; i++)
			{
				_data[i] = 0;
			}
		}

		ulong* Slot(int index)
		{
			if ((uint)index >= (uint)SlotCount)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}
			return Pointer + index;
		}

		public void SetInt(int index, int value) => *Slot(index) = unchecked((uint)value);
		public void SetUInt(int index, uint value) => *Slot(index) = value;
		public void SetHash(int index, string name) => *Slot(index) = string.IsNullOrEmpty(name) ? 0u : Game.Joaat(name);
		public void SetBool(int index, bool value) => *Slot(index) = value ? 1UL : 0UL;
		public void SetULong(int index, ulong value) => *Slot(index) = value;

		public void SetFloat(int index, float value)
		{
			*Slot(index) = *(uint*)&value;
		}

		/// <summary>Writes a <c>const char*</c> (UTF-8, owned by this struct). <c>null</c> writes a null pointer.</summary>
		public void SetString(int index, string value)
		{
			ulong* slot = Slot(index);
			if (value == null)
			{
				*slot = 0;
				return;
			}
			byte[] bytes = Encoding.UTF8.GetBytes(value);
			IntPtr text = Marshal.AllocHGlobal(bytes.Length + 1);
			Marshal.Copy(bytes, 0, text, bytes.Length);
			Marshal.WriteByte(text, bytes.Length, 0);
			(_strings ??= new List<IntPtr>()).Add(text);
			*slot = (ulong)text.ToInt64();
		}

		public int GetInt(int index) => unchecked((int)(uint)*Slot(index));
		public uint GetUInt(int index) => unchecked((uint)*Slot(index));
		public bool GetBool(int index) => (uint)*Slot(index) != 0;
		public ulong GetULong(int index) => *Slot(index);

		public float GetFloat(int index)
		{
			uint bits = unchecked((uint)*Slot(index));
			return *(float*)&bits;
		}

		/// <summary>Reads a <c>const char*</c> slot (null when the slot is zero).</summary>
		public string GetString(int index)
		{
			ulong value = *Slot(index);
			return value == 0 ? null : RDR2DN.NativeMemory.PtrToStringUTF8(new IntPtr(unchecked((long)value)));
		}

		public void Dispose()
		{
			Free();
			GC.SuppressFinalize(this);
		}

		void Free()
		{
			if (_strings != null)
			{
				foreach (IntPtr text in _strings)
				{
					Marshal.FreeHGlobal(text);
				}
				_strings = null;
			}
			if (_data != null)
			{
				Marshal.FreeHGlobal(new IntPtr(_data));
				_data = null;
			}
		}
	}
}
