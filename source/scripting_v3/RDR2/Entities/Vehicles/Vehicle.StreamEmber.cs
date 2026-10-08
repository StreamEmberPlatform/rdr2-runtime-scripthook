// StreamEmber: Vehicle additions (wheels, seats, trains).

using RDR2.Native;

namespace RDR2
{
	public sealed partial class Vehicle
	{
		/// <summary>Breaks off wheel <paramref name="wheelIndex"/> (0..3 for wagons).</summary>
		public void BreakOffWheel(int wheelIndex)
		{
			VEHICLE._BREAK_OFF_VEHICLE_WHEEL(Handle, wheelIndex);
		}

		/// <summary>Number of seats of this vehicle's model, driver included.</summary>
		public int SeatCount => VEHICLE.GET_VEHICLE_MODEL_NUMBER_OF_SEATS((uint)Model.Hash);

		/// <summary>Whether this vehicle is a train (locomotive or carriage).</summary>
		public bool IsTrain => VEHICLE.IS_THIS_MODEL_A_TRAIN((uint)Model.Hash);

		/// <summary>Sets the speed and cruise speed of a train (m/s).</summary>
		public void SetTrainSpeed(float speed)
		{
			VEHICLE.SET_TRAIN_SPEED(Handle, speed);
			VEHICLE.SET_TRAIN_CRUISE_SPEED(Handle, speed);
		}

		/// <summary>Sets the forward speed now (m/s).</summary>
		public void SetForwardSpeed(float speed)
		{
			VEHICLE.SET_VEHICLE_FORWARD_SPEED(Handle, speed);
		}
	}
}
