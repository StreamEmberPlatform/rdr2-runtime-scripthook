//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/scripthookvdotnet#license
//

using System;
using System.Text;
using System.Runtime.InteropServices;
using RDR2.Math;

namespace RDR2
{
	public unsafe struct Global
	{
		readonly IntPtr address;

		internal Global(int globalId)
		{
			address = RDR2DN.NativeMemory.GetGlobalPtr(globalId);
			if (address == System.IntPtr.Zero)
			{
				throw new System.ArgumentOutOfRangeException(nameof(globalId), "Script global " + globalId + " is not available.");
			}
		}

		public unsafe ulong* MemoryAddress => (ulong*)address.ToPointer();

		public unsafe void Set<T>(T value)
		{
			if (typeof(T) == typeof(bool))
			{
				if (Convert.ToBoolean(value) == false)
				{
					RDR2DN.NativeMemory.WriteInt32(address, 0);
				}
				else
				{
					RDR2DN.NativeMemory.WriteInt32(address, 1);
				}
			}
			else if (typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte))
			{
				RDR2DN.NativeMemory.WriteByte(address, Convert.ToByte(value));
			}
			else if (typeof(T) == typeof(short) || typeof(T) == typeof(ushort))
			{
				RDR2DN.NativeMemory.WriteInt16(address, Convert.ToInt16(value));
			}
			else if (typeof(T) == typeof(float) || typeof(T) == typeof(double))
			{
				RDR2DN.NativeMemory.WriteFloat(address, Convert.ToSingle(value));
			}
			else if (typeof(T) == typeof(string))
			{
				string _value = Convert.ToString(value);
				int size = Encoding.UTF8.GetByteCount(_value);
				Marshal.Copy(Encoding.UTF8.GetBytes(_value), 0, address, size);
				*((byte*)MemoryAddress + size) = 0;
			}
			else if (typeof(T) == typeof(Vector3))
			{
				Vector3 vec = (Vector3)(object)value;
				RDR2DN.NativeMemory.WriteVector3(address, new RDR2DN.NativeMemory.FVector3(vec.X, vec.Y, vec.Z));
			}
			// StreamEmber: 64-bit and unsigned values (hashes above int.MaxValue used to throw OverflowException)
			else if (typeof(T) == typeof(long) || typeof(T) == typeof(ulong))
			{
				*MemoryAddress = typeof(T) == typeof(long) ? unchecked((ulong)(long)(object)value) : (ulong)(object)value;
			}
			else if (typeof(T) == typeof(uint))
			{
				RDR2DN.NativeMemory.WriteInt32(address, unchecked((int)(uint)(object)value));
			}
			else if (typeof(T).IsPrimitive || typeof(T).IsEnum)
			{
				RDR2DN.NativeMemory.WriteInt32(address, Convert.ToInt32(value));
			}
			else
			{
				throw new InvalidCastException("Cannot cast script global to type " + typeof(T).ToString() + "; not a supported cast.");
			}
		}


		public unsafe T As<T>()
		{
			if (typeof(T) == typeof(bool))
			{
				if (RDR2DN.NativeMemory.ReadInt32(address) == 0)
				{
					return (T)(object)false;
				}
				else
				{
					return (T)(object)true;
				}
			}

			if (typeof(T) == typeof(byte))
			{
				return (T)(object)RDR2DN.NativeMemory.ReadByte(address);
			}

			if (typeof(T) == typeof(short))
			{
				return (T)(object)RDR2DN.NativeMemory.ReadInt16(address);
			}

			if (typeof(T) == typeof(float))
			{
				return (T)(object)RDR2DN.NativeMemory.ReadFloat(address);
			}

			if (typeof(T) == typeof(string))
			{
				return (T)(object)RDR2DN.NativeMemory.PtrToStringUTF8(address);
			}

			if (typeof(T) == typeof(Vector3))
			{
				var data = RDR2DN.NativeMemory.ReadVector3(address);
				return (T)(object)new Vector3(data.X, data.Y, data.Z);
			}

			// StreamEmber: unsigned and 64-bit reads (the int fallback below threw InvalidCastException for them)
			if (typeof(T) == typeof(uint))
			{
				return (T)(object)unchecked((uint)RDR2DN.NativeMemory.ReadInt32(address));
			}
			if (typeof(T) == typeof(long))
			{
				return (T)(object)unchecked((long)*MemoryAddress);
			}
			if (typeof(T) == typeof(ulong))
			{
				return (T)(object)*MemoryAddress;
			}

			if (typeof(T).IsPrimitive || typeof(T).IsEnum)
			{
				return (T)(object)RDR2DN.NativeMemory.ReadInt32(address);
			}

			throw new InvalidCastException("Cannot cast script global to type " + typeof(T).ToString() + "; not a supported cast.");
		}
	}
}
