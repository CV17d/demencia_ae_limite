// ============================================================================
// PlayerStats.cs — Central player data container for DEMENCIA NO LIMITE
// ============================================================================
// Holds all mutable player stats for the current run. This is NOT a MonoBehaviour;
// it is a plain C# class owned by GameManager. Systems read/write through here.
// The AI Director, Boss Controller, and Elevator Events all depend on this data.
// ============================================================================

using System;
using UnityEngine;

namespace DemenciaNoLimite.Core.Data
{
    /// <summary>
    /// Plain data object that holds all run-mutable player statistics.
    /// Instantiated once per run by the GameManager.
    /// </summary>
    [Serializable]
    public class PlayerStats
    {
        // ── Base Stats (set at run start, modified by upgrades/pacts) ──────
        [Header("Health")]
        public float MaxHP          = 100f;
        public float CurrentHP      = 100f;

        [Header("Combat")]
        public float BaseDamage     = 10f;
        public float AttackSpeed    = 1f;   // Attacks per second
        public float CritChance     = 0.05f; // 0..1

        [Header("Movement")]
        public float MoveSpeed      = 5f;
        public float JumpHeight     = 1.2f;

        [Header("Defense")]
        public float DamageReduction = 0f;   // Flat reduction applied before HP loss

        // ── Hidden / AI-Director Variables ─────────────────────────────────
        [Header("Hidden Metrics")]
        [Range(0f, 1f)]
        public float Luck           = 0.5f;  // 0 = cursed, 1 = blessed
        [Range(0f, 1f)]
        public float MentalDegradation = 0f; // 0 = sane, 1 = fully degraded

        // ── Run Tracking ───────────────────────────────────────────────────
        [Header("Run Tracking")]
        public int   FloorsCleared    = 0;
        public int   EnemiesKilled    = 0;
        public int   BossesDefeated   = 0;
        public int   DamageTaken      = 0;   // Cumulative for the run
        public int   DamageDealt      = 0;   // Cumulative for the run
        public int   PactsTaken       = 0;
        public float RunTimer         = 0f;  // Seconds elapsed

        // ── Derived Ratios (used by AI Director & Boss Controller) ─────────

        /// <summary>
        /// Returns a 0..1 value representing how "glass-cannon" the player is.
        /// 1 = extreme glass cannon (high damage, low HP).
        /// 0 = extreme tank (high HP, low damage).
        /// </summary>
        public float GlassCannonRatio
        {
            get
            {
                // Normalize damage and HP into comparable scales.
                // Reference values: 10 base DMG, 100 base HP.
                float normalizedDmg = BaseDamage / 10f;
                float normalizedHP  = MaxHP / 100f;

                // Ratio: when DMG >> HP the value approaches 1.
                float ratio = normalizedDmg / (normalizedDmg + normalizedHP);
                return Mathf.Clamp01(ratio);
            }
        }

        /// <summary>
        /// Composite "power level" used by the AI Director to gauge overall
        /// player strength relative to the current floor.
        /// </summary>
        public float PowerLevel
        {
            get
            {
                // Weighted sum — tweak coefficients to taste during playtesting.
                float hpWeight    = MaxHP / 100f;
                float dmgWeight   = BaseDamage / 10f;
                float spdWeight   = MoveSpeed / 5f;
                float defWeight   = 1f + (DamageReduction / 20f);

                return (hpWeight * 0.25f)
                     + (dmgWeight * 0.35f)
                     + (spdWeight * 0.15f)
                     + (defWeight * 0.10f)
                     + (Luck * 0.15f);
            }
        }

        // ── Mutation ───────────────────────────────────────────────────────

        /// <summary>
        /// Applies healing clamped to MaxHP.
        /// </summary>
        public void Heal(float amount)
        {
            CurrentHP = Mathf.Min(CurrentHP + amount, MaxHP);
        }

        /// <summary>
        /// Applies damage after DamageReduction. Returns actual damage dealt.
        /// </summary>
        public float TakeDamage(float rawDamage, bool ignoreDefense = false)
        {
            float actual = ignoreDefense
                ? rawDamage
                : Mathf.Max(0f, rawDamage - DamageReduction);

            CurrentHP    = Mathf.Max(0f, CurrentHP - actual);
            DamageTaken += Mathf.RoundToInt(actual);
            return actual;
        }

        /// <summary>
        /// Creates a deep copy for preview / simulation purposes
        /// (e.g., showing the player what a pact would do before accepting).
        /// </summary>
        public PlayerStats DeepCopy()
        {
            return (PlayerStats)MemberwiseClone();
        }

        /// <summary>
        /// Resets stats to default for a new run.
        /// </summary>
        public void ResetForNewRun()
        {
            MaxHP             = 100f;
            CurrentHP         = 100f;
            BaseDamage        = 10f;
            AttackSpeed       = 1f;
            CritChance        = 0.05f;
            MoveSpeed         = 5f;
            JumpHeight        = 1.2f;
            DamageReduction   = 0f;
            Luck              = 0.5f;
            MentalDegradation = 0f;
            FloorsCleared     = 0;
            EnemiesKilled     = 0;
            BossesDefeated    = 0;
            DamageTaken       = 0;
            DamageDealt       = 0;
            PactsTaken        = 0;
            RunTimer          = 0f;
        }
    }
}
