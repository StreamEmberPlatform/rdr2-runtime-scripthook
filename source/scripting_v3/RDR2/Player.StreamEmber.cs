// StreamEmber: Player additions (abilities, crimes, story-safe model change).

using System;
using RDR2.Native;

namespace RDR2
{
	public sealed partial class Player
	{
		#region Abilities

		/// <summary>Enables or disables Dead Eye.</summary>
		public bool DeadEyeEnabled
		{
			set => PLAYER._ENABLE_CUSTOM_DEADEYE_ABILITY(Handle, value);
		}

		/// <summary>Dead Eye ability level (1..5 in story).</summary>
		public int DeadEyeLevel
		{
			get => PLAYER._GET_DEADEYE_ABILITY_LEVEL(Handle);
			set => PLAYER._SET_DEADEYE_ABILITY_LEVEL(Handle, value);
		}

		/// <summary>Enables or disables Eagle Eye.</summary>
		public bool EagleEyeEnabled
		{
			set => PLAYER._ENABLE_EAGLEEYE(Handle, value);
		}

		/// <summary>Enables or disables the special abilities (Dead Eye core use).</summary>
		public void SetSpecialAbilitiesEnabled(bool enabled)
		{
			PLAYER._SPECIAL_ABILITY_SET_DISABLED(Handle, !enabled);
			PLAYER._SECONDARY_SPECIAL_ABILITY_SET_DISABLED(Handle, !enabled);
		}

		/// <summary>Refills stamina (0..1 of the maximum).</summary>
		public void RestoreStamina(float amount = 1f)
		{
			PLAYER.RESTORE_PLAYER_STAMINA(Handle, amount);
		}

		/// <summary>The player's ped group id (for <see cref="Ped.SetAsGroupMember"/>).</summary>
		public int GroupId => PLAYER.GET_PLAYER_GROUP(Handle);

		#endregion

		#region Law

		/// <summary>Reports a crime by this player, e.g. Game.Joaat("CRIME_ASSAULT_LAW").</summary>
		public void ReportCrime(uint crimeHash, int bounty = 0)
		{
			LAW._REPORT_CRIME(Handle, crimeHash, bounty, 0, true);
		}

		/// <summary>Clears the wanted state.</summary>
		public void ClearWanted()
		{
			PLAYER.CLEAR_PLAYER_WANTED_LEVEL(Handle);
		}

		#endregion

		#region Story-safe model change

		// Script globals that the story scripts read to know the player ped and its model. Found in ChaosModRDR
		// (game builds 1311/1436): without them the game switches the model back or keeps a stale ped handle.
		// They are written only after a check that they really hold what we expect, so a different game build
		// cannot get a random global overwritten.
		const int GlobalPlayerPed = 35;           // Global_35
		const int GlobalModelA = 40 + 39;         // Global_40.f_39
		const int GlobalModelB = 1935630 + 2;     // Global_1935630.f_2

		static uint s_savedModelA, s_savedModelB;
		static bool s_hasSavedModel;
		static bool s_hasSavedModelB;
		static uint s_lastWrittenModel;
		static uint s_storyModel;

		static readonly uint ArthurModel = Game.Joaat("Player_Zero");
		static readonly uint JohnModel = Game.Joaat("Player_Three");

		/// <summary>Whether the player ped currently uses Arthur's or John's model.</summary>
		public bool IsStoryCharacterModel
		{
			get
			{
				uint model = ENTITY.GET_ENTITY_MODEL(PLAYER.PLAYER_PED_ID());
				return model == ArthurModel || model == JohnModel;
			}
		}

		/// <summary>Whether the last <see cref="ChangeModelPersistent"/> could update the story globals.</summary>
		public static bool LastModelChangeSyncedGlobals { get; private set; }

		/// <summary>
		/// Changes the player model like <see cref="ChangeModel"/> and also updates the story script globals
		/// (player ped and model), so the game does not switch back on its own. Undo with <see cref="RestoreStoryModel"/>.
		/// </summary>
		/// <returns><c>false</c> when the model could not be loaded.</returns>
		public bool ChangeModelPersistent(Model model)
		{
			if (!model.IsInCdImage || !model.IsPed || !model.Request(2000))
			{
				return false;
			}

			int oldPed = PLAYER.PLAYER_PED_ID();
			// Remember Arthur or John from the ped itself (works even when the globals cannot be verified)
			uint currentModel = ENTITY.GET_ENTITY_MODEL(oldPed);
			if (IsStoryHash(currentModel))
			{
				s_storyModel = currentModel;
			}
			bool pedOk = TryRead(GlobalPlayerPed, out ulong pedValue) && unchecked((int)(uint)pedValue) == oldPed;
			bool aOk = TryRead(GlobalModelA, out ulong a) && IsExpectedModel((uint)a);
			bool bOk = TryRead(GlobalModelB, out ulong b) && IsExpectedModel((uint)b);

			if (aOk && !s_hasSavedModel && IsStoryHash((uint)a))
			{
				s_savedModelA = (uint)a;
				s_hasSavedModelB = bOk && IsStoryHash((uint)b);
				s_savedModelB = s_hasSavedModelB ? (uint)b : 0;
				s_hasSavedModel = true;
			}

			uint hash = (uint)model.Hash;
			PLAYER.SET_PLAYER_MODEL(Handle, hash, true);
			STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(hash);

			int newPed = PLAYER.PLAYER_PED_ID();
			if (pedOk)
			{
				TryWrite(GlobalPlayerPed, unchecked((uint)newPed));
			}
			// Each global is written only when it held what we expected (story model or our last write)
			if (aOk) TryWrite(GlobalModelA, hash);
			if (bOk) TryWrite(GlobalModelB, hash);
			if (aOk || bOk) s_lastWrittenModel = hash;
			LastModelChangeSyncedGlobals = pedOk && aOk;

			ENTITY.SET_ENTITY_COLLISION(newPed, true, true);
			ENTITY.SET_ENTITY_DYNAMIC(newPed, true);
			// A MetaPed without an outfit renders invisible / without body parts
			PED._SET_RANDOM_OUTFIT_VARIATION(newPed, true);
			PED._UPDATE_PED_VARIATION(newPed, false, true, true, true, false);
			return true;
		}

		/// <summary>
		/// Restores the story model saved by the first <see cref="ChangeModelPersistent"/> (Arthur or John)
		/// and the story globals. Does nothing when the player already has a story model.
		/// </summary>
		public bool RestoreStoryModel()
		{
			if (IsStoryCharacterModel)
			{
				s_hasSavedModel = false;
				return true;
			}
			uint target = s_storyModel != 0 ? s_storyModel : s_hasSavedModel ? s_savedModelA : ArthurModel;
			var model = new Model(target);
			// Already loaded = no yield, so this also works from an Aborted handler
			if (!model.IsLoaded && !model.Request(3000))
			{
				return false;
			}
			bool aOk = TryRead(GlobalModelA, out ulong a) && IsExpectedModel((uint)a);
			bool bOk = TryRead(GlobalModelB, out ulong b) && IsExpectedModel((uint)b);
			int oldPed = PLAYER.PLAYER_PED_ID();
			bool pedOk = TryRead(GlobalPlayerPed, out ulong pedValue) && unchecked((int)(uint)pedValue) == oldPed;

			PLAYER.SET_PLAYER_MODEL(Handle, target, true);
			STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(target);

			int newPed = PLAYER.PLAYER_PED_ID();
			if (pedOk)
			{
				TryWrite(GlobalPlayerPed, unchecked((uint)newPed));
			}
			if (s_hasSavedModel)
			{
				if (aOk) TryWrite(GlobalModelA, s_savedModelA);
				if (bOk) TryWrite(GlobalModelB, s_hasSavedModelB ? s_savedModelB : s_savedModelA);
			}
			s_hasSavedModel = false;
			s_hasSavedModelB = false;
			s_lastWrittenModel = 0;
			s_storyModel = 0;

			ENTITY.SET_ENTITY_COLLISION(newPed, true, true);
			ENTITY.SET_ENTITY_DYNAMIC(newPed, true);
			// Default story outfit; callers re-apply saved clothes (Ped.ApplyShopItemComponents)
			PED._EQUIP_META_PED_OUTFIT_PRESET(newPed, 0, false);
			PED._UPDATE_PED_VARIATION(newPed, false, true, true, true, false);
			return true;
		}

		static bool IsStoryHash(uint hash) => hash == ArthurModel || hash == JohnModel;

		static bool IsExpectedModel(uint hash) => IsStoryHash(hash) || (s_lastWrittenModel != 0 && hash == s_lastWrittenModel);

		static unsafe bool TryRead(int global, out ulong value)
		{
			value = 0;
			IntPtr address = RDR2DN.NativeMemory.GetGlobalPtr(global);
			if (address == IntPtr.Zero)
			{
				return false;
			}
			value = *(ulong*)address.ToPointer();
			return true;
		}

		static unsafe void TryWrite(int global, ulong value)
		{
			IntPtr address = RDR2DN.NativeMemory.GetGlobalPtr(global);
			if (address != IntPtr.Zero)
			{
				*(ulong*)address.ToPointer() = value;
			}
		}

		#endregion
	}
}
