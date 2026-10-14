// ============================================================================
// IDamageable.cs & IKnockbackable.cs — Core combat interfaces
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Combat
{
    /// <summary>
    /// Interface for any entity that can take damage.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        float CurrentHealth { get; }
        float MaxHealth { get; }

        /// <summary>
        /// Applies damage and returns the actual damage absorbed/inflicted.
        /// </summary>
        float TakeDamage(DamageInfo info);
    }

    /// <summary>
    /// Interface for entities that react physically to impact impulses.
    /// </summary>
    public interface IKnockbackable
    {
        void ApplyKnockback(Vector3 force);
    }
}
