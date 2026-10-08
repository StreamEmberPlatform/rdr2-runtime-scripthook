// StreamEmber: Entity additions (physics, fire).

using RDR2.Math;
using RDR2.Native;

namespace RDR2
{
	public abstract partial class Entity
	{
		/// <summary>Whether gravity acts on this entity (SET_ENTITY_HAS_GRAVITY).</summary>
		public bool HasGravity
		{
			set => ENTITY.SET_ENTITY_HAS_GRAVITY(Handle, value);
		}

		/// <summary>Turns physics simulation on or off (SET_ENTITY_DYNAMIC).</summary>
		public void SetDynamic(bool dynamic)
		{
			ENTITY.SET_ENTITY_DYNAMIC(Handle, dynamic);
		}

		/// <summary>Applies a force at the centre of mass (forceType 1 = impulse, 3 = continuous force).</summary>
		public void ApplyForceToCenterOfMass(Vector3 force, int forceType = 1, bool relative = false)
		{
			ENTITY.APPLY_FORCE_TO_ENTITY_CENTER_OF_MASS(Handle, forceType, force.X, force.Y, force.Z, false, relative, true, false);
		}

		/// <summary>Sets this entity on fire.</summary>
		/// <remarks>START_ENTITY_FIRE is declared with untyped parameters; it is called by hash here with the real types.</remarks>
		public void Ignite(float intensity = 1f)
		{
			Function.Call(0xC4DC7418A44D6822, Handle, intensity, 0, 0);
		}

		/// <summary>Puts out a fire on this entity.</summary>
		public void Extinguish()
		{
			Function.Call(0x8390751DC40C1E98, Handle, 0);
		}
	}
}
