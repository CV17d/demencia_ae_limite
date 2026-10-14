// ============================================================================
// EnemyBuffState.cs — Tracks permanent enemy buffs from Cursed Pacts
// ============================================================================
// Each Cursed Pact the player accepts grants enemies a permanent buff for the
// remainder of the run. This class accumulates those buffs and exposes
// multipliers that the AI Director and enemy spawners read.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemenciaNoLimite.Core.Data
{
    /// <summary>
    /// Accumulates all enemy buffs acquired through Cursed Pacts.
    /// Owned by GameManager; consumed by AI Director and enemy spawning logic.
    /// </summary>
    [Serializable]
    public class EnemyBuffState
    {
        // ── Multipliers (start at 1.0 = no change) ────────────────────────
        public float HPMultiplier          = 1f;
        public float DamageMultiplier      = 1f;
        public float SpeedMultiplier       = 1f;
        public float SpawnRateMultiplier   = 1f;

        // ── Ability Flags (unlocked by specific pacts) ─────────────────────
        public bool CanThrowDebris         = false;
        public bool CanDodgeProjectiles    = false;
        public bool HasRangedAttack        = false;
        public bool CanReviveOnce          = false;
        public bool ExplodesOnDeath        = false;

        // ── Active Pact IDs (for UI display / save) ────────────────────────
        public List<string> ActivePactIDs = new List<string>();

        /// <summary>
        /// Applies an enemy buff from a Cursed Pact definition.
        /// Called by ElevatorEventSystem when the player accepts a pact.
        /// </summary>
        public void ApplyPact(CursedPactData pact)
        {
            HPMultiplier        *= pact.EnemyHPMultiplier;
            DamageMultiplier    *= pact.EnemyDamageMultiplier;
            SpeedMultiplier     *= pact.EnemySpeedMultiplier;
            SpawnRateMultiplier *= pact.EnemySpawnRateMultiplier;

            // Apply ability flags
            CanThrowDebris      |= pact.GrantThrowDebris;
            CanDodgeProjectiles |= pact.GrantDodgeProjectiles;
            HasRangedAttack     |= pact.GrantRangedAttack;
            CanReviveOnce       |= pact.GrantReviveOnce;
            ExplodesOnDeath     |= pact.GrantExplodesOnDeath;

            ActivePactIDs.Add(pact.PactID);
        }

        /// <summary>
        /// Resets all enemy buffs for a new run.
        /// </summary>
        public void Reset()
        {
            HPMultiplier        = 1f;
            DamageMultiplier    = 1f;
            SpeedMultiplier     = 1f;
            SpawnRateMultiplier = 1f;

            CanThrowDebris      = false;
            CanDodgeProjectiles = false;
            HasRangedAttack     = false;
            CanReviveOnce       = false;
            ExplodesOnDeath     = false;

            ActivePactIDs.Clear();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Forward-declaration of CursedPactData shape consumed by EnemyBuffState.
    // The actual ScriptableObject lives in ElevatorEvents/CursedPactData.cs
    // but we need the interface here to avoid circular dependencies.
    // ════════════════════════════════════════════════════════════════════════
    // (See CursedPactData.cs for the full ScriptableObject implementation)
}
