// ============================================================================
// DamageInfo.cs — Unified damage data payload for DEMENCIA NO LIMITE
// ============================================================================
// Carries all contextual information about a damage event through the pipeline.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Combat
{
    public enum DamageSourceType
    {
        Melee,
        Bullet,
        Explosion,
        HazardEnvironment,
        BossSpecial,
        MentalDecay
    }

    [System.Serializable]
    public struct DamageInfo
    {
        public float Amount;
        public GameObject Attacker;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public Vector3 Direction;
        public DamageSourceType SourceType;
        public bool IsCritical;
        public bool IsHeadshot;
        public bool IgnoreDefense;
        public float KnockbackForce;

        public DamageInfo(
            float amount,
            GameObject attacker = null,
            Vector3 hitPoint = default,
            Vector3 hitNormal = default,
            Vector3 direction = default,
            DamageSourceType sourceType = DamageSourceType.Bullet,
            bool isCritical = false,
            bool isHeadshot = false,
            bool ignoreDefense = false,
            float knockbackForce = 0f)
        {
            Amount = amount;
            Attacker = attacker;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            Direction = direction == default ? Vector3.forward : direction.normalized;
            SourceType = sourceType;
            IsCritical = isCritical;
            IsHeadshot = isHeadshot;
            IgnoreDefense = ignoreDefense;
            KnockbackForce = knockbackForce;
        }
    }
}
