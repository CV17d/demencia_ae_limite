// ============================================================================
// CursedPactData.cs — ScriptableObject defining a single Cursed Pact
// ============================================================================
// Each Cursed Pact is a risk/reward contract: the player receives a buff,
// but all enemies in the run receive a permanent buff. Pacts are selected
// by the ElevatorEventSystem via RNG weighted by the AI Director.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Core.Data
{
    [CreateAssetMenu(
        fileName = "New Cursed Pact",
        menuName = "Demencia/Elevator Events/Cursed Pact",
        order    = 1
    )]
    public class CursedPactData : ScriptableObject
    {
        // ── Identity ───────────────────────────────────────────────────────
        [Header("Identity")]
        public string PactID;

        [TextArea(2, 4)]
        public string DisplayName;

        [TextArea(3, 6)]
        public string FlavorText; // Lore/horror description shown to the player

        // ── Player Buff ────────────────────────────────────────────────────
        [Header("Player Buff")]
        public float BonusMaxHP       = 0f;
        public float BonusDamage      = 0f;
        public float BonusAttackSpeed = 0f;
        public float BonusMoveSpeed   = 0f;
        public float BonusJumpHeight  = 0f;
        public float BonusCritChance  = 0f;
        public float BonusDamageReduction = 0f;
        public float HealAmount       = 0f; // Immediate heal on accept

        // ── Enemy Debuff (permanent for the run) ───────────────────────────
        [Header("Enemy Buff (Multipliers — 1.0 = no change)")]
        public float EnemyHPMultiplier        = 1f;
        public float EnemyDamageMultiplier    = 1f;
        public float EnemySpeedMultiplier     = 1f;
        public float EnemySpawnRateMultiplier = 1f;

        [Header("Enemy Ability Grants")]
        public bool GrantThrowDebris      = false;
        public bool GrantDodgeProjectiles = false;
        public bool GrantRangedAttack     = false;
        public bool GrantReviveOnce       = false;
        public bool GrantExplodesOnDeath  = false;

        // ── Mental Degradation Impact ──────────────────────────────────────
        [Header("Mental Degradation")]
        [Tooltip("How much accepting this pact increases the MentalDegradation variable.")]
        [Range(0f, 0.15f)]
        public float DegradationIncrease = 0.02f;

        // ── AI Director Weighting ──────────────────────────────────────────
        [Header("Selection Weight")]
        [Tooltip("Base weight for RNG selection. AI Director modifies this.")]
        public float BaseSelectionWeight = 1f;

        [Tooltip("Minimum floor number before this pact can appear.")]
        public int   MinFloor = 1;

        [Tooltip("Maximum floor number this pact can appear on. 0 = no limit.")]
        public int   MaxFloor = 0;

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if this pact is available on the given floor.
        /// </summary>
        public bool IsAvailableOnFloor(int floorNumber)
        {
            if (floorNumber < MinFloor) return false;
            if (MaxFloor > 0 && floorNumber > MaxFloor) return false;
            return true;
        }

        /// <summary>
        /// Applies the player-side buff portion of the pact to the given stats.
        /// </summary>
        public void ApplyPlayerBuff(PlayerStats stats)
        {
            stats.MaxHP           += BonusMaxHP;
            stats.BaseDamage      += BonusDamage;
            stats.AttackSpeed     += BonusAttackSpeed;
            stats.MoveSpeed       += BonusMoveSpeed;
            stats.JumpHeight      += BonusJumpHeight;
            stats.CritChance      += BonusCritChance;
            stats.DamageReduction += BonusDamageReduction;

            if (HealAmount > 0f)
            {
                stats.Heal(HealAmount);
            }

            // Mental degradation always increases with pacts
            stats.MentalDegradation = Mathf.Clamp01(
                stats.MentalDegradation + DegradationIncrease
            );

            stats.PactsTaken++;
        }
    }
}
