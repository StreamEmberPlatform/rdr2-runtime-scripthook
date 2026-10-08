// StreamEmber: Ped additions (mounts, outfits, movement, nearby entities, combat, shop item components).
// Kept out of Ped.cs so upstream merges stay simple.

using System;
using System.Collections.Generic;
using RDR2.Native;

namespace RDR2
{
	public sealed partial class Ped
	{
		#region Nearby entities

		/// <summary>
		/// Gets up to <paramref name="maxCount"/> peds the game keeps as "nearby" for this <see cref="Ped"/>
		/// (GET_PED_NEARBY_PEDS with a size-prefixed buffer). Cheap enough for every frame; no itemset is created.
		/// </summary>
		/// <param name="maxCount">1..256.</param>
		/// <param name="ignoredPedType">Ped type to skip, -1 for none.</param>
		public unsafe Ped[] GetNearbyPeds(int maxCount = 100, int ignoredPedType = -1)
		{
			maxCount = System.Math.Max(1, System.Math.Min(256, maxCount));
			using (var buffer = new ScriptStruct(maxCount + 1))
			{
				buffer.SetInt(0, maxCount);
				int found = PED.GET_PED_NEARBY_PEDS(Handle, buffer.Pointer, ignoredPedType, 0);
				return ReadHandles<Ped>(buffer, found, maxCount, h => new Ped(h));
			}
		}

		/// <summary>
		/// Gets up to <paramref name="maxCount"/> vehicles the game keeps as "nearby" for this <see cref="Ped"/>
		/// (GET_PED_NEARBY_VEHICLES with a size-prefixed buffer).
		/// </summary>
		public unsafe Vehicle[] GetNearbyVehicles(int maxCount = 100)
		{
			maxCount = System.Math.Max(1, System.Math.Min(256, maxCount));
			using (var buffer = new ScriptStruct(maxCount + 1))
			{
				buffer.SetInt(0, maxCount);
				int found = PED.GET_PED_NEARBY_VEHICLES(Handle, buffer.Pointer);
				return ReadHandles<Vehicle>(buffer, found, maxCount, h => new Vehicle(h));
			}
		}

		T[] ReadHandles<T>(ScriptStruct buffer, int found, int maxCount, Func<int, T> create)
		{
			int count = System.Math.Min(System.Math.Max(found, 0), maxCount);
			var result = new List<T>(count);
			for (int i = 0; i < count; i++)
			{
				int handle = buffer.GetInt(i + 1);
				if (handle != 0 && handle != Handle && ENTITY.DOES_ENTITY_EXIST(handle))
				{
					result.Add(create(handle));
				}
			}
			return result.ToArray();
		}

		#endregion

		#region Mounts

		/// <summary>Puts this <see cref="Ped"/> on <paramref name="mount"/> instantly. Seat -1 is the rider, 0 the passenger.</summary>
		public void SetOnMount(Ped mount, int seat = -1)
		{
			if (mount == null)
			{
				return;
			}
			PED.SET_PED_ONTO_MOUNT(Handle, mount.Handle, seat, true);
		}

		/// <summary>Removes this <see cref="Ped"/> from its mount instantly.</summary>
		public void DismountInstantly()
		{
			PED._REMOVE_PED_FROM_MOUNT(Handle, false, false);
		}

		/// <summary>Removes this <see cref="Ped"/> from its mount, leaving the mount alone (used before changing the player model).</summary>
		public void DismountKeepMount()
		{
			PED._REMOVE_PED_FROM_MOUNT(Handle, true, false);
		}

		/// <summary>Whether <paramref name="seat"/> of this mount is free (-1 rider, 0 passenger).</summary>
		public bool IsMountSeatFree(int seat) => PED._IS_MOUNT_SEAT_FREE(Handle, seat);

		/// <summary>The rider of this mount, or <c>null</c>.</summary>
		public Ped Rider
		{
			get
			{
				int rider = PED._GET_RIDER_OF_MOUNT(Handle, false);
				return rider == 0 ? null : new Ped(rider);
			}
		}

		/// <summary>Makes this horse panic; <paramref name="kickOffRider"/> throws the rider off.</summary>
		public void AgitateHorse(bool kickOffRider = true)
		{
			PED._HORSE_AGITATE(Handle, kickOffRider);
		}

		#endregion

		#region Outfits & look

		/// <summary>Number of MetaPed outfit presets of this ped's model.</summary>
		public int OutfitPresetCount => PED.GET_NUM_META_PED_OUTFITS(Handle);

		/// <summary>Equips a MetaPed outfit preset (0..<see cref="OutfitPresetCount"/>-1).</summary>
		public void SetOutfitPreset(int presetId, bool keepAccessories = false)
		{
			PED._EQUIP_META_PED_OUTFIT_PRESET(Handle, presetId, keepAccessories);
		}

		/// <summary>Sets one facial expression channel (index from the expression table, value usually 0..3).</summary>
		public void SetFacialExpression(int index, float value)
		{
			PED._SET_CHAR_EXPRESSION(Handle, index, value);
		}

		/// <summary>LOD multiplier of this ped (lower = coarser model at distance).</summary>
		public float LodMultiplier
		{
			set => PED.SET_PED_LOD_MULTIPLIER(Handle, value);
		}

		/// <summary>Spawns a blood pool under this ped.</summary>
		public void AddBloodPool(float size = 1f)
		{
			GRAPHICS._ADD_BLOOD_POOLS_FOR_PED_WITH_PARAMS(Handle, 1f, size, 1f);
		}

		/// <summary>
		/// Shop item components currently on this ped, one per component category
		/// (_GET_SHOP_ITEM_COMPONENT_AT_INDEX). Use with <see cref="ApplyShopItemComponents"/> to restore clothes.
		/// </summary>
		public unsafe uint[] GetShopItemComponents()
		{
			int categories = PED._GET_NUM_COMPONENT_CATEGORIES_IN_PED(Handle);
			var result = new List<uint>(System.Math.Max(categories, 0));
			using (var first = new ScriptStruct(4))
			using (var second = new ScriptStruct(4))
			{
				for (int i = 0; i < categories; i++)
				{
					first.Clear();
					second.Clear();
					uint component = PED._GET_SHOP_ITEM_COMPONENT_AT_INDEX(Handle, i, true, first.Pointer, second.Pointer);
					if (component != 0)
					{
						result.Add(component);
					}
				}
			}
			return result.ToArray();
		}

		/// <summary>Applies shop item components (e.g. from <see cref="GetShopItemComponents"/>) and updates the variation.</summary>
		public void ApplyShopItemComponents(IEnumerable<uint> components)
		{
			if (components == null)
			{
				return;
			}
			foreach (uint component in components)
			{
				PED._APPLY_SHOP_ITEM_TO_PED(Handle, component, true, false, true);
			}
			UpdateVariation();
		}

		#endregion

		#region Movement & state

		/// <summary>Makes this ped drunk (movement, audio) with <paramref name="level"/> 0..1, or sober.</summary>
		public void SetDrunk(bool drunk, float level = 1f)
		{
			PED._SET_PED_DRUNKNESS(Handle, drunk, drunk ? level : 0f);
			AUDIO.SET_PED_IS_DRUNK(Handle, drunk);
		}

		/// <summary>
		/// Sets the walk style: a locomotion archetype for the model (e.g. "arthur_healthy") and/or a
		/// motion type (e.g. "very_drunk", "injured_left_leg", "cower_known"). Pass null to leave one unchanged.
		/// </summary>
		public void SetWalkStyle(string locomotionArchetype, string motionType)
		{
			if (!string.IsNullOrEmpty(locomotionArchetype))
			{
				PED._SET_PED_DESIRED_LOCO_FOR_MODEL(Handle, locomotionArchetype);
			}
			if (!string.IsNullOrEmpty(motionType))
			{
				PED._SET_PED_DESIRED_LOCO_MOTION_TYPE(Handle, motionType);
			}
		}

		/// <summary>Clears what <see cref="SetWalkStyle"/> set.</summary>
		public void ResetWalkStyle()
		{
			PED._CLEAR_PED_DESIRED_LOCO_FOR_MODEL(Handle);
			PED._CLEAR_PED_DESIRED_LOCO_MOTION_TYPE(Handle);
		}

		/// <summary>Whether gravity acts on this ped (SET_PED_GRAVITY).</summary>
		public bool HasPedGravity
		{
			set => PED.SET_PED_GRAVITY(Handle, value);
		}

		/// <summary>Ragdoll blocking flags (SET_RAGDOLL_BLOCKING_FLAGS).</summary>
		public void SetRagdollBlockingFlags(int flags)
		{
			PED.SET_RAGDOLL_BLOCKING_FLAGS(Handle, flags);
		}

		#endregion

		#region Weapons

		/// <summary>Removes all ammo of every weapon.</summary>
		public void RemoveAllAmmo()
		{
			WEAPON._REMOVE_ALL_PED_AMMO(Handle);
		}

		/// <summary>Hash of the weapon in hand (0 when none).</summary>
		public unsafe uint CurrentWeaponHash
		{
			get
			{
				uint weapon = 0;
				WEAPON.GET_CURRENT_PED_WEAPON(Handle, &weapon, true, 0, false);
				return weapon;
			}
		}

		/// <summary>Drops the weapon <paramref name="weaponHash"/> from the inventory to the ground.</summary>
		public void DropInventoryWeapon(uint weaponHash, int ammoCount = 0)
		{
			WEAPON.SET_PED_DROPS_INVENTORY_WEAPON(Handle, weaponHash, 0f, 0.5f, 0f, ammoCount);
		}

		/// <summary>Sets the ammo of <paramref name="weaponHash"/>.</summary>
		public void SetAmmo(uint weaponHash, int ammo)
		{
			WEAPON.SET_PED_AMMO(Handle, weaponHash, ammo);
		}

		#endregion

		#region Combat & AI

		/// <summary>Sets a combat attribute (eCombatAttribute index, e.g. 5 CanFightArmedPedsWhenNotArmed, 46 AlwaysFight).</summary>
		public void SetCombatAttribute(int attribute, bool enabled)
		{
			PED.SET_PED_COMBAT_ATTRIBUTES(Handle, attribute, enabled);
		}

		/// <summary>Sets flee attribute flags.</summary>
		public void SetFleeAttributes(int flags, bool enabled)
		{
			PED.SET_PED_FLEE_ATTRIBUTES(Handle, flags, enabled);
		}

		/// <summary>Hearing range in metres.</summary>
		public float HearingRange
		{
			set => PED.SET_PED_HEARING_RANGE(Handle, value);
		}

		/// <summary>Makes this ped run away from <paramref name="target"/> (TASK_SMART_FLEE_PED). -1 ms = until far enough.</summary>
		public void FleeFrom(Ped target, float distance = 100f, int timeMs = -1)
		{
			if (target == null)
			{
				return;
			}
			TASK.TASK_SMART_FLEE_PED(Handle, target.Handle, distance, timeMs, 0, 3f, 0);
		}

		/// <summary>Adds this ped to a ped group (e.g. <see cref="Player.GroupId"/>).</summary>
		public void SetAsGroupMember(int groupId)
		{
			PED.SET_PED_AS_GROUP_MEMBER(Handle, groupId);
		}

		#endregion
	}
}
