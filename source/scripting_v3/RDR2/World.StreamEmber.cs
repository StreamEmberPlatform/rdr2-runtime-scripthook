// StreamEmber: World additions (weather, wind, snow, lightning, clock, timecycle, spawning by Model).

using RDR2.Math;
using RDR2.Native;

namespace RDR2
{
	public static partial class World
	{
		#region Weather & sky

		/// <summary>Sets the weather now and keeps it (clears overrides and persistence).</summary>
		public static void SetWeather(WeatherType weather)
		{
			SetWeather((uint)weather);
		}

		/// <summary>Sets the weather by hash, e.g. Game.Joaat("WHITEOUT").</summary>
		public static void SetWeather(uint weatherHash)
		{
			MISC.CLEAR_OVERRIDE_WEATHER();
			MISC.SET_WEATHER_TYPE(weatherHash, true, true, false, 0f, false);
			MISC.CLEAR_WEATHER_TYPE_PERSIST();
		}

		/// <summary>Wind speed (m/s). Setting it overrides the weather's wind.</summary>
		public static float WindSpeed
		{
			set => MISC.SET_WIND_SPEED(value);
		}

		/// <summary>Current wind direction (unit vector).</summary>
		public static Vector3 WindDirection => MISC.GET_WIND_DIRECTION();

		/// <summary>Falling snow (_SET_SNOW_LEVEL). ChaosModRDR: -1 turns snowfall on, 1 turns it off.</summary>
		public static float SnowLevel
		{
			set => MISC._SET_SNOW_LEVEL(value);
		}

		/// <summary>Strikes a lightning bolt at <paramref name="position"/> (visual and sound; damage is up to the caller).</summary>
		public static void ForceLightningFlash(Vector3 position)
		{
			MISC._FORCE_LIGHTNING_FLASH_AT_COORDS(position.X, position.Y, position.Z, -1f);
		}

		/// <summary>Sets the in-game clock.</summary>
		public static void SetClockTime(int hours, int minutes = 0, int seconds = 0)
		{
			CLOCK.SET_CLOCK_TIME(hours, minutes, seconds);
		}

		/// <summary>Applies a timecycle modifier (screen grading, e.g. "PauseMenuDark", "rainBowMod").</summary>
		public static void SetTimecycleModifier(string name, float strength = 1f)
		{
			GRAPHICS.SET_TIMECYCLE_MODIFIER(name);
			GRAPHICS.SET_TIMECYCLE_MODIFIER_STRENGTH(strength);
		}

		/// <summary>Removes the timecycle modifier.</summary>
		public static void ClearTimecycleModifier()
		{
			GRAPHICS.CLEAR_TIMECYCLE_MODIFIER();
			GRAPHICS.SET_TIMECYCLE_MODIFIER_STRENGTH(1f);
		}

		#endregion

		#region Spawning by Model

		/// <summary>Spawns a ped of any model (story characters, animals, "CS_…" models).</summary>
		/// <param name="randomOutfit">Pick a random outfit variation (otherwise outfit preset 0).</param>
		public static Ped CreatePed(Model model, Vector3 position, float heading = 0f, bool randomOutfit = true)
		{
			if (!model.IsPed || !model.Request(4000))
			{
				return null;
			}
			int ped = PED.CREATE_PED((uint)model.Hash, position.X, position.Y, position.Z, heading, true, true, false, false);
			STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED((uint)model.Hash);
			if (ped == 0)
			{
				return null;
			}
			if (randomOutfit)
			{
				PED._SET_RANDOM_OUTFIT_VARIATION(ped, true);
			}
			else
			{
				PED._EQUIP_META_PED_OUTFIT_PRESET(ped, 0, false);
			}
			PED._UPDATE_PED_VARIATION(ped, false, true, true, true, false);
			return new Ped(ped);
		}

		/// <summary>Spawns a vehicle of any model (e.g. "COACH3", "hotAirBalloon01", "mineCart01x").</summary>
		public static Vehicle CreateVehicle(Model model, Vector3 position, float heading = 0f)
		{
			if (!model.IsVehicle || !model.Request(4000))
			{
				return null;
			}
			int vehicle = VEHICLE.CREATE_VEHICLE((uint)model.Hash, position.X, position.Y, position.Z, heading, true, true, false, false);
			STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED((uint)model.Hash);
			return vehicle == 0 ? null : new Vehicle(vehicle);
		}

		#endregion
	}
}
