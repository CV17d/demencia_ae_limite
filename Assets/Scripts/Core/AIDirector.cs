// ============================================================================
// AIDirector.cs — Dynamic Difficulty & Luck-based Procedural Adjustments
// ============================================================================
// The AI Director is the invisible hand shaping each run. It reads the
// player's stats, performance history, and Luck to dynamically modify:
//   - Floor configs before generation (enemy count, hazards, complexity)
//   - Enemy spawn composition (which enemy types appear)
//   - Environmental intensity (lighting, hazard density)
//
// Design Philosophy:
//   The Director does NOT punish skilled players or reward bad ones.
//   Instead, it maintains TENSION. A player who's cruising gets a subtle
//   ramp; a player who's struggling gets breathing room — but never
//   safety. This is horror. There is no safety.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Core
{
    public class AIDirector : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ════════════════════════════════════════════════════════════════════

        [Header("Difficulty Tuning")]
        [Tooltip("How aggressively the Director scales difficulty. 1.0 = balanced.")]
        [Range(0.5f, 2f)]
        [SerializeField] private float _aggressiveness = 1.0f;

        [Tooltip("Minimum difficulty weight (floor for struggling players).")]
        [SerializeField] private float _minWeight = 0.4f;

        [Tooltip("Maximum difficulty weight (cap for dominant players).")]
        [SerializeField] private float _maxWeight = 2.5f;

        [Header("Performance Tracking Window")]
        [Tooltip("Number of recent floors to consider for performance analysis.")]
        [SerializeField] private int _analysisWindow = 3;

        // ── Internal State ─────────────────────────────────────────────────

        /// <summary>
        /// Current computed difficulty weight. 1.0 = baseline.
        /// Published via GameEvents when changed.
        /// </summary>
        public float CurrentDifficultyWeight { get; private set; } = 1.0f;

        // Rolling performance metrics (updated each floor)
        private float _recentDamageTakenPerFloor;
        private float _recentTimePerFloor;
        private float _recentKillEfficiency; // kills / time

        // ════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ════════════════════════════════════════════════════════════════════

        private void OnEnable()
        {
            GameEvents.OnFloorCleared  += OnFloorCleared;
            GameEvents.OnRunStarted    += OnRunStarted;
            GameEvents.OnPactAccepted  += OnPactAccepted;
        }

        private void OnDisable()
        {
            GameEvents.OnFloorCleared  -= OnFloorCleared;
            GameEvents.OnRunStarted    -= OnRunStarted;
            GameEvents.OnPactAccepted  -= OnPactAccepted;
        }

        private void OnRunStarted()
        {
            CurrentDifficultyWeight = 1.0f;
            _recentDamageTakenPerFloor = 0f;
            _recentTimePerFloor = 0f;
            _recentKillEfficiency = 0f;
        }

        // ════════════════════════════════════════════════════════════════════
        // CORE: DIFFICULTY WEIGHT CALCULATION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recalculates the difficulty weight based on current player state.
        /// Called after each floor is cleared.
        ///
        /// The weight is a composite score from several factors:
        ///   - Player Power Level (higher power → harder)
        ///   - Health Ratio (more HP remaining → harder)
        ///   - Kill Efficiency (faster kills → harder)
        ///   - Luck (higher luck → slightly easier normal floors, but richer rewards)
        ///   - Pacts Taken (more pacts → enemies already buffed, so slightly lower weight)
        ///   - Floor Progression (natural escalation)
        /// </summary>
        private float CalculateDifficultyWeight(PlayerStats player, int currentFloor)
        {
            // ── Factor 1: Player Power Level ───────────────────────────────
            // PowerLevel is a composite of HP, DMG, Speed, Defense, Luck.
            // A PowerLevel of 1.0 is the baseline (default stats, no upgrades).
            float powerFactor = player.PowerLevel;

            // ── Factor 2: Health Efficiency ────────────────────────────────
            // If the player has been taking very little damage, ramp up.
            // If they're barely surviving, ease off.
            float healthRatio = player.CurrentHP / player.MaxHP;
            // Transform: 1.0 HP → 1.3 weight, 0.2 HP → 0.7 weight
            float healthFactor = 0.5f + (healthRatio * 0.8f);

            // ── Factor 3: Kill Efficiency ──────────────────────────────────
            // Measured as enemies killed per second of gameplay.
            // Fast killers get ramped; slow ones get relief.
            float killEff = player.RunTimer > 0f
                ? (player.EnemiesKilled / player.RunTimer) * 60f // kills per minute
                : 0f;
            // Normalize: 5 kills/min = baseline (1.0)
            float killFactor = Mathf.Clamp(killEff / 5f, 0.5f, 1.8f);

            // ── Factor 4: Luck Modifier ────────────────────────────────────
            // High luck slightly reduces raw difficulty (but increases reward quality).
            // This creates a subtle rubber-banding effect.
            float luckModifier = 1.0f - (player.Luck * 0.15f); // Luck 1.0 → 0.85

            // ── Factor 5: Pact Compensation ────────────────────────────────
            // Each pact has already buffed enemies via EnemyBuffState.
            // Reduce the Director's weight slightly to avoid double-punishment.
            float pactCompensation = 1.0f - (player.PactsTaken * 0.03f);
            pactCompensation = Mathf.Max(0.7f, pactCompensation);

            // ── Factor 6: Floor Progression ────────────────────────────────
            // Natural difficulty curve independent of player performance.
            float floorProgression = 1.0f + (currentFloor / (float)GameManager.TOTAL_FLOORS) * 0.5f;

            // ── Composite ──────────────────────────────────────────────────
            float raw = powerFactor
                      * healthFactor
                      * killFactor
                      * luckModifier
                      * pactCompensation
                      * floorProgression
                      * _aggressiveness;

            float clamped = Mathf.Clamp(raw, _minWeight, _maxWeight);

            Debug.Log($"[AIDirector] Weight Calc — " +
                      $"Power:{powerFactor:F2} HP:{healthFactor:F2} " +
                      $"Kill:{killFactor:F2} Luck:{luckModifier:F2} " +
                      $"Pact:{pactCompensation:F2} Floor:{floorProgression:F2} " +
                      $"→ Raw:{raw:F2} → Clamped:{clamped:F2}");

            return clamped;
        }

        // ════════════════════════════════════════════════════════════════════
        // FLOOR CONFIG MODIFICATION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called by GameManager BEFORE floor generation. Modifies the
        /// FloorConfig in-place based on the current difficulty weight.
        /// </summary>
        public void ModifyFloorConfig(FloorConfig config, PlayerStats player, EnemyBuffState enemyBuffs)
        {
            // Recalculate weight for this floor
            CurrentDifficultyWeight = CalculateDifficultyWeight(player, config.FloorNumber);
            GameEvents.FireDifficultyWeightChanged(CurrentDifficultyWeight);

            if (config.Type == FloorType.BossRoom)
            {
                // Boss rooms are handled by AdaptiveBossController, not here.
                // We only adjust hazard count.
                config.BaseHazardCount = Mathf.RoundToInt(
                    config.BaseHazardCount * CurrentDifficultyWeight
                );
                return;
            }

            // ── Enemy Count Adjustment ─────────────────────────────────────
            // Base count * Director weight * enemy spawn rate multiplier from pacts
            float adjustedEnemies = config.BaseEnemyCount
                                  * CurrentDifficultyWeight
                                  * enemyBuffs.SpawnRateMultiplier;

            config.BaseEnemyCount = Mathf.Max(2, Mathf.RoundToInt(adjustedEnemies));

            // ── Hazard Count Adjustment ────────────────────────────────────
            config.BaseHazardCount = Mathf.Max(0, Mathf.RoundToInt(
                config.BaseHazardCount * CurrentDifficultyWeight * 0.8f
            ));

            // ── Maze Complexity Adjustment ─────────────────────────────────
            // Higher difficulty → more complex mazes (more dead ends, longer paths)
            config.MazeComplexity = Mathf.Clamp01(
                config.MazeComplexity + (CurrentDifficultyWeight - 1f) * 0.15f
            );

            // ── Luck-based Adjustments ─────────────────────────────────────
            // High luck reduces hazards, low luck increases them
            float luckHazardMod = 1.0f - ((player.Luck - 0.5f) * 0.3f);
            config.BaseHazardCount = Mathf.RoundToInt(config.BaseHazardCount * luckHazardMod);

            Debug.Log($"[AIDirector] Floor {config.FloorNumber} modified: " +
                      $"Enemies={config.BaseEnemyCount}, Hazards={config.BaseHazardCount}, " +
                      $"Complexity={config.MazeComplexity:F2}");
        }

        // ════════════════════════════════════════════════════════════════════
        // ENEMY COMPOSITION SELECTION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Determines the mix of enemy types for a floor based on tier,
        /// difficulty weight, and enemy buff state. Returns weighted
        /// probabilities for each enemy archetype.
        /// </summary>
        public EnemyComposition GetEnemyComposition(FloorConfig config, EnemyBuffState buffs)
        {
            var comp = new EnemyComposition();
            float w = CurrentDifficultyWeight;

            // Base composition by tier
            switch (config.Tier)
            {
                case FloorTier.Act1:
                    comp.GruntWeight   = 0.60f;
                    comp.RangedWeight  = 0.20f;
                    comp.HeavyWeight   = 0.10f;
                    comp.EliteWeight   = 0.05f;
                    comp.SpecialWeight = 0.05f;
                    break;

                case FloorTier.Act2:
                    comp.GruntWeight   = 0.35f;
                    comp.RangedWeight  = 0.25f;
                    comp.HeavyWeight   = 0.20f;
                    comp.EliteWeight   = 0.12f;
                    comp.SpecialWeight = 0.08f;
                    break;

                case FloorTier.Act3:
                    comp.GruntWeight   = 0.15f;
                    comp.RangedWeight  = 0.20f;
                    comp.HeavyWeight   = 0.30f;
                    comp.EliteWeight   = 0.20f;
                    comp.SpecialWeight = 0.15f;
                    break;
            }

            // Difficulty weight shifts composition toward harder enemies
            if (w > 1.3f)
            {
                float shift = (w - 1.3f) * 0.15f;
                comp.GruntWeight  -= shift;
                comp.EliteWeight  += shift * 0.6f;
                comp.SpecialWeight += shift * 0.4f;
            }
            else if (w < 0.7f)
            {
                float shift = (0.7f - w) * 0.15f;
                comp.GruntWeight  += shift;
                comp.EliteWeight  -= shift * 0.5f;
                comp.HeavyWeight  -= shift * 0.5f;
            }

            comp.Normalize();
            return comp;
        }

        // ════════════════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ════════════════════════════════════════════════════════════════════

        private void OnFloorCleared(int floorNumber)
        {
            // Update rolling performance metrics
            var player = GameManager.Instance?.Player;
            if (player == null) return;

            // Exponential moving average for damage taken per floor
            float recentDamage = player.DamageTaken;
            _recentDamageTakenPerFloor = Mathf.Lerp(
                _recentDamageTakenPerFloor, recentDamage, 1f / _analysisWindow
            );
        }

        private void OnPactAccepted(CursedPactData pact)
        {
            // Pacts affect the difficulty equation — recalculate
            // (the actual recalc happens at the next floor transition)
            Debug.Log($"[AIDirector] Pact '{pact.PactID}' accepted. " +
                      $"Enemy buffs will compound with Director scaling.");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // ENEMY COMPOSITION DATA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Weighted probability distribution for enemy types on a floor.
    /// Used by the spawner to select which enemy prefabs to instantiate.
    /// </summary>
    [System.Serializable]
    public class EnemyComposition
    {
        public float GruntWeight;    // Basic melee enemies
        public float RangedWeight;   // Ranged attackers
        public float HeavyWeight;    // Tanky / slow enemies
        public float EliteWeight;    // Mini-boss level enemies
        public float SpecialWeight;  // Unique / environmental enemies

        /// <summary>
        /// Normalizes all weights so they sum to 1.0.
        /// </summary>
        public void Normalize()
        {
            float total = GruntWeight + RangedWeight + HeavyWeight
                        + EliteWeight + SpecialWeight;

            if (total <= 0f)
            {
                // Fallback: all grunts
                GruntWeight = 1f;
                return;
            }

            GruntWeight   /= total;
            RangedWeight  /= total;
            HeavyWeight   /= total;
            EliteWeight   /= total;
            SpecialWeight /= total;
        }

        /// <summary>
        /// Selects an enemy type based on a random roll (0..1).
        /// </summary>
        public EnemyType SelectType(float roll)
        {
            roll = Mathf.Clamp01(roll);
            float cumulative = 0f;

            cumulative += GruntWeight;
            if (roll <= cumulative) return EnemyType.Grunt;

            cumulative += RangedWeight;
            if (roll <= cumulative) return EnemyType.Ranged;

            cumulative += HeavyWeight;
            if (roll <= cumulative) return EnemyType.Heavy;

            cumulative += EliteWeight;
            if (roll <= cumulative) return EnemyType.Elite;

            return EnemyType.Special;
        }
    }

    public enum EnemyType
    {
        Grunt,
        Ranged,
        Heavy,
        Elite,
        Special
    }
}
