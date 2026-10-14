// ============================================================================
// AdaptiveBossController.cs — Boss stat scaling inversely to player build
// ============================================================================
// The core design principle: NO BUILD IS SAFE. The boss adapts to punish
// whatever the player is strongest at while exploiting their weaknesses.
//
// Glass Cannon (High DMG, Low HP):
//   → Boss gets massive HP pool + high burst damage
//   → Forces the player to survive a long fight
//
// Tank (High HP, Low DMG):
//   → Boss gets low HP but devastating defense-ignoring attacks
//   → Forces the player to actually output damage before dying
//
// Balanced:
//   → Boss gets a moderate stat spread with unpredictable attack patterns
//   → The "fairest" fight but the most mechanically demanding
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Core
{
    /// <summary>
    /// Computed stat block for a boss encounter. Generated fresh for each
    /// boss fight based on the player's current stats at the moment of entry.
    /// </summary>
    [System.Serializable]
    public class BossStats
    {
        public string BossID;
        public float  MaxHP;
        public float  BaseDamage;
        public float  AttackSpeed;       // Attacks per second
        public float  MoveSpeed;
        public float  DefenseIgnoreRatio; // 0..1, how much of player defense is bypassed
        public float  EnrageThreshold;   // HP % at which boss enrages (0..1)
        public float  EnrageDmgMultiplier;
        public float  EnrageSpdMultiplier;

        // ── Ability Flags ──────────────────────────────────────────────────
        public bool   HasAreaDenial;      // Boss creates hazard zones
        public bool   HasGrabAttack;      // Instant-kill attempt if player is too close
        public bool   HasPhaseTransition; // Boss changes behavior at EnrageThreshold
        public int    PhaseCount;         // 1, 2, or 3 phases

        public override string ToString()
        {
            return $"[Boss:{BossID}] HP={MaxHP:F0} DMG={BaseDamage:F1} " +
                   $"AtkSpd={AttackSpeed:F2} DefIgnore={DefenseIgnoreRatio:P0} " +
                   $"Phases={PhaseCount}";
        }
    }

    public class AdaptiveBossController : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURATION — Base boss templates per act
        // ════════════════════════════════════════════════════════════════════

        [Header("Base Boss Stats (per Act)")]
        [Tooltip("Base HP for bosses in Act 1 / 2 / 3")]
        [SerializeField] private float[] _baseHP        = { 300f, 600f, 1200f };
        [SerializeField] private float[] _baseDamage    = { 15f, 25f, 40f };
        [SerializeField] private float[] _baseAtkSpeed  = { 0.8f, 1.0f, 1.2f };
        [SerializeField] private float[] _baseMoveSpeed = { 3f, 4f, 5f };

        [Header("Scaling Coefficients")]
        [Tooltip("How aggressively stats scale inversely to the player build.")]
        [Range(0.5f, 3f)]
        [SerializeField] private float _scalingIntensity = 1.5f;

        [Tooltip("Maximum multiplier applied to any single boss stat.")]
        [SerializeField] private float _maxStatMultiplier = 3.0f;

        [Tooltip("Minimum multiplier — bosses never drop below this fraction.")]
        [SerializeField] private float _minStatMultiplier = 0.4f;

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The most recently generated boss stat block. Set after GenerateBossStats.
        /// </summary>
        public BossStats CurrentBossStats { get; private set; }

        /// <summary>
        /// Generates an adaptive boss stat block based on the player's
        /// current build. Called by GameManager when entering a boss floor.
        /// </summary>
        public BossStats GenerateBossStats(FloorConfig floor, PlayerStats player, EnemyBuffState enemyBuffs)
        {
            int actIndex = (int)floor.Tier; // 0, 1, 2

            // ── Step 1: Start with base template ───────────────────────────
            var stats = new BossStats
            {
                BossID     = floor.BossID,
                MaxHP      = _baseHP[actIndex],
                BaseDamage = _baseDamage[actIndex],
                AttackSpeed = _baseAtkSpeed[actIndex],
                MoveSpeed  = _baseMoveSpeed[actIndex],
            };

            // ── Step 2: Read the player's build archetype ──────────────────
            float gcr = player.GlassCannonRatio; // 0=tank, 1=glass cannon

            Debug.Log($"[BossController] Player GlassCannonRatio: {gcr:F3} " +
                      $"(0=Tank, 1=GlassCannon)");

            // ── Step 3: INVERSE SCALING ────────────────────────────────────
            // The core math: the boss's stats scale AGAINST the player's
            // strengths and TOWARD their weaknesses.

            // --- HP Scaling ---
            // Glass cannon → boss gets MORE HP (long fight)
            // Tank → boss gets LESS HP (but hits harder)
            float hpMultiplier = CalculateInverseScale(
                gcr,                        // Player's glass cannon ratio
                lowPlayerValue: 0.5f,       // Tank gets boss at 50% HP
                highPlayerValue: 2.0f,      // Glass cannon gets boss at 200% HP
                _scalingIntensity
            );
            stats.MaxHP *= ClampMultiplier(hpMultiplier);

            // --- Damage Scaling ---
            // Tank → boss does MORE damage (punish stacking HP)
            // Glass cannon → boss does moderate damage (still lethal at low HP)
            float dmgMultiplier = CalculateInverseScale(
                1f - gcr,                   // INVERSE of glass cannon → tank
                lowPlayerValue: 0.7f,       // Glass cannon: boss at 70% damage
                highPlayerValue: 1.8f,      // Tank: boss at 180% damage
                _scalingIntensity
            );
            stats.BaseDamage *= ClampMultiplier(dmgMultiplier);

            // --- Defense Ignore Ratio ---
            // Tank (high defense) → boss ignores MORE defense
            // Glass cannon (low defense) → boss barely ignores defense
            stats.DefenseIgnoreRatio = Mathf.Lerp(
                0.1f,  // Glass cannon: boss ignores 10% of defense
                0.85f, // Tank: boss ignores 85% of defense (DEVASTATING)
                Mathf.Pow(1f - gcr, _scalingIntensity * 0.7f)
            );

            // --- Attack Speed ---
            // Balanced players get faster boss attack speed (less predictable)
            // Extreme builds get slightly slower (but deadlier) attacks
            float extremity = Mathf.Abs(gcr - 0.5f) * 2f; // 0=balanced, 1=extreme
            float atkSpdMult = Mathf.Lerp(1.3f, 0.8f, extremity);
            stats.AttackSpeed *= atkSpdMult;

            // --- Movement Speed ---
            // Scale with player's movement speed (boss matches the player)
            float playerSpeedRatio = player.MoveSpeed / 5f; // 5 is default
            stats.MoveSpeed *= Mathf.Lerp(0.8f, 1.4f, playerSpeedRatio);

            // ── Step 4: Enemy Buff Integration ─────────────────────────────
            // Pact-granted enemy buffs also apply to bosses, but at 50% effect
            // to avoid stacking too aggressively
            float pactHPMult = Mathf.Lerp(1f, enemyBuffs.HPMultiplier, 0.5f);
            float pactDmgMult = Mathf.Lerp(1f, enemyBuffs.DamageMultiplier, 0.5f);
            stats.MaxHP      *= pactHPMult;
            stats.BaseDamage *= pactDmgMult;

            // ── Step 5: Ability Assignment ─────────────────────────────────
            AssignAbilities(stats, floor, player);

            // ── Step 6: Enrage Mechanics ───────────────────────────────────
            CalculateEnrage(stats, player);

            // ── Step 7: AI Director integration ────────────────────────────
            // Apply a final global multiplier from the Director's weight
            float directorWeight = GameManager.Instance != null
                ? FindAnyObjectByType<AIDirector>()?.CurrentDifficultyWeight ?? 1f
                : 1f;

            // Director has a softer influence on bosses (±15%)
            float directorInfluence = Mathf.Lerp(0.85f, 1.15f, (directorWeight - _minStatMultiplier) / (_maxStatMultiplier - _minStatMultiplier));
            stats.MaxHP      *= directorInfluence;
            stats.BaseDamage *= directorInfluence;

            CurrentBossStats = stats;

            Debug.Log($"[BossController] Generated: {stats}");
            Debug.Log($"[BossController] Multipliers applied — " +
                      $"HP:{hpMultiplier:F2}*Pact:{pactHPMult:F2}*Dir:{directorInfluence:F2}, " +
                      $"DMG:{dmgMultiplier:F2}*Pact:{pactDmgMult:F2}*Dir:{directorInfluence:F2}, " +
                      $"DefIgnore:{stats.DefenseIgnoreRatio:P0}");

            return stats;
        }

        // ════════════════════════════════════════════════════════════════════
        // INVERSE SCALING MATH
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Core scaling function. Maps a player stat ratio (0..1) to a boss
        /// stat multiplier using a power curve for non-linear feel.
        ///
        /// When playerRatio is HIGH:  result approaches highPlayerValue
        /// When playerRatio is LOW:   result approaches lowPlayerValue
        /// </summary>
        /// <param name="playerRatio">0..1 value representing the player's specialization.</param>
        /// <param name="lowPlayerValue">Multiplier when playerRatio = 0.</param>
        /// <param name="highPlayerValue">Multiplier when playerRatio = 1.</param>
        /// <param name="intensity">Power curve exponent. >1 = steeper scaling.</param>
        private float CalculateInverseScale(
            float playerRatio,
            float lowPlayerValue,
            float highPlayerValue,
            float intensity)
        {
            // Apply power curve for non-linear scaling
            // This creates a "sweet spot" in the middle where changes are gradual,
            // with steep jumps at the extremes.
            float curved = Mathf.Pow(Mathf.Clamp01(playerRatio), intensity);
            return Mathf.Lerp(lowPlayerValue, highPlayerValue, curved);
        }

        /// <summary>
        /// Clamps a multiplier to configured bounds.
        /// </summary>
        private float ClampMultiplier(float multiplier)
        {
            return Mathf.Clamp(multiplier, _minStatMultiplier, _maxStatMultiplier);
        }

        // ════════════════════════════════════════════════════════════════════
        // ABILITY ASSIGNMENT
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Assigns boss abilities based on act, player stats, and build.
        /// Later acts get more abilities; specific builds trigger specific counters.
        /// </summary>
        private void AssignAbilities(BossStats stats, FloorConfig floor, PlayerStats player)
        {
            int act = (int)floor.Tier;

            // Phase count scales with act
            stats.PhaseCount = act + 1; // Act1=1 phase, Act2=2, Act3=3
            stats.HasPhaseTransition = stats.PhaseCount > 1;

            // Area denial: always present from Act 2+
            stats.HasAreaDenial = act >= 1;

            // Grab attack: present against high-move-speed players (punish aggression)
            stats.HasGrabAttack = player.MoveSpeed > 6f || act >= 2;
        }

        // ════════════════════════════════════════════════════════════════════
        // ENRAGE MECHANICS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calculates the enrage threshold and multipliers.
        /// Enrage triggers when the boss drops below a HP percentage,
        /// dramatically increasing damage and speed.
        ///
        /// Against glass cannons: early enrage (boss gets desperate faster)
        /// Against tanks: late enrage (drawn-out punishment)
        /// </summary>
        private void CalculateEnrage(BossStats stats, PlayerStats player)
        {
            float gcr = player.GlassCannonRatio;

            // Glass cannon → enrage at 40% HP (boss panics, gets aggressive)
            // Tank → enrage at 15% HP (boss conserves, then explodes)
            stats.EnrageThreshold = Mathf.Lerp(0.15f, 0.40f, gcr);

            // Enrage damage multiplier: always punishing, but more so for tanks
            stats.EnrageDmgMultiplier = Mathf.Lerp(1.8f, 1.4f, gcr);

            // Enrage speed multiplier: glass cannons face a faster boss in enrage
            stats.EnrageSpdMultiplier = Mathf.Lerp(1.2f, 1.6f, gcr);
        }

        // ════════════════════════════════════════════════════════════════════
        // RUNTIME BOSS BEHAVIOR (called during the fight)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks if the boss should enter enrage state based on current HP.
        /// Called each frame by the boss's AI behavior tree.
        /// </summary>
        public bool ShouldEnrage(float currentBossHP)
        {
            if (CurrentBossStats == null) return false;
            float hpRatio = currentBossHP / CurrentBossStats.MaxHP;
            return hpRatio <= CurrentBossStats.EnrageThreshold;
        }

        /// <summary>
        /// Returns the damage a boss attack should deal, factoring in
        /// defense ignore against the player's DamageReduction.
        /// </summary>
        public float CalculateBossDamage(float baseBossDmg, float playerDefense, bool isEnraged)
        {
            if (CurrentBossStats == null) return baseBossDmg;

            float effectiveDefense = playerDefense * (1f - CurrentBossStats.DefenseIgnoreRatio);
            float damage = Mathf.Max(1f, baseBossDmg - effectiveDefense);

            if (isEnraged)
            {
                damage *= CurrentBossStats.EnrageDmgMultiplier;
            }

            return damage;
        }

        /// <summary>
        /// Returns the boss's current movement speed, accounting for enrage.
        /// </summary>
        public float GetBossMoveSpeed(bool isEnraged)
        {
            if (CurrentBossStats == null) return 3f;

            float speed = CurrentBossStats.MoveSpeed;
            if (isEnraged) speed *= CurrentBossStats.EnrageSpdMultiplier;
            return speed;
        }
    }
}
