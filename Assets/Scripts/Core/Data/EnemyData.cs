// ============================================================================
// EnemyData.cs — ScriptableObject defining enemy archetypes
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Core.Data
{
    [CreateAssetMenu(
        fileName = "New Enemy Type",
        menuName = "Demencia/Enemies/Enemy Data",
        order    = 1
    )]
    public class EnemyData : ScriptableObject
    {
        [Header("Identity")]
        public string EnemyID;
        public string DisplayName;
        [TextArea(2, 4)]
        public string Description;
        public EnemyType ArchetypeType; // Grunt, Ranged, Heavy, Elite, Special

        [Header("Base Stats")]
        public float BaseHP         = 50f;
        public float BaseDamage     = 8f;
        public float MoveSpeed      = 3f;
        public float AttackRange    = 2f;
        public float AttackCooldown = 1.5f;
        public float DetectionRange = 15f;

        [Header("Behavior")]
        public bool  IsAggressive     = true;   // Attacks on sight
        public bool  PatrolsWhenIdle  = true;
        public bool  FleesAtLowHP     = false;
        public float FleeHPThreshold  = 0.2f;   // Flee below this HP ratio
        public bool  CanCallForHelp   = false;   // Alerts nearby enemies
        public float CallRange        = 10f;

        [Header("Combat Abilities")]
        public bool  HasRangedAttack       = false;
        public float ProjectileSpeed       = 15f;
        public bool  HasAreaAttack         = false;
        public float AreaAttackRadius      = 3f;
        public bool  HasChargeAttack       = false;
        public float ChargeSpeed           = 12f;
        public float ChargeDamageMultiplier = 2f;

        [Header("Resistances")]
        [Range(0f, 1f)]
        public float DamageResistance    = 0f;   // % damage reduction
        public bool  ImmuneToKnockback   = false;
        public bool  ImmuneToStun        = false;

        [Header("Drops")]
        public int   MinCurrencyDrop  = 5;
        public int   MaxCurrencyDrop  = 15;
        [Range(0f, 1f)]
        public float ItemDropChance   = 0.05f;

        [Header("Visual/Audio References")]
        public string PrefabName;
        public string DeathSoundID;
        public string AttackSoundID;
        public string IdleSoundID;

        [Header("Tier Availability")]
        public FloorTier MinTier = FloorTier.Act1;

        // ── Runtime Stat Computation ───────────────────────────────────────

        /// <summary>
        /// Returns adjusted HP accounting for EnemyBuffState multipliers.
        /// </summary>
        public float GetAdjustedHP(EnemyBuffState buffs)
        {
            return BaseHP * buffs.HPMultiplier;
        }

        /// <summary>
        /// Returns adjusted damage accounting for EnemyBuffState multipliers.
        /// </summary>
        public float GetAdjustedDamage(EnemyBuffState buffs)
        {
            return BaseDamage * buffs.DamageMultiplier;
        }

        /// <summary>
        /// Returns adjusted move speed accounting for EnemyBuffState multipliers.
        /// </summary>
        public float GetAdjustedSpeed(EnemyBuffState buffs)
        {
            return MoveSpeed * buffs.SpeedMultiplier;
        }

        /// <summary>
        /// Returns the currency drop amount for this enemy (randomized).
        /// </summary>
        public int RollCurrencyDrop()
        {
            return Random.Range(MinCurrencyDrop, MaxCurrencyDrop + 1);
        }
    }
}
