// ============================================================================
// MentalDegradationSystem.cs — Sanity erosion as the player ascends
// ============================================================================
// The Mental Degradation variable (0..1) increases as the player climbs
// the tower. It triggers discrete tiers of visual/audio hallucinations:
//
//   Stable    (0.00–0.20) — Normal gameplay
//   Uneasy   (0.20–0.40) — Subtle audio distortions, minor vignette
//   Disturbed (0.40–0.60) — Visual artifacts, whispering, shadow flickers
//   Fractured (0.60–0.80) — Heavy hallucinations, phantom enemies, FOV shifts
//   Shattered (0.80–1.00) — Reality breaks, extreme post-processing, false UI
//
// This script manages the VARIABLE and TIER TRANSITIONS only.
// Actual post-processing, audio, and FOV effects are hooked up separately
// by subscribing to GameEvents.OnDegradationChanged / OnDegradationTierReached.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Core
{
    public class MentalDegradationSystem : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ════════════════════════════════════════════════════════════════════

        [Header("Degradation Curve")]
        [Tooltip("Base degradation added per floor cleared.")]
        [SerializeField] private float _baseDegradationPerFloor = 0.025f;

        [Tooltip("Extra degradation per boss defeated.")]
        [SerializeField] private float _bossDefeatDegradation = 0.05f;

        [Tooltip("Degradation per enemy kill (minor, but cumulative).")]
        [SerializeField] private float _killDegradation = 0.002f;

        [Tooltip("Degradation per unit of damage taken (rewards caution slightly).")]
        [SerializeField] private float _damageTakenDegradation = 0.0003f;

        [Header("Tier Thresholds")]
        [SerializeField] private float _uneasyThreshold    = 0.20f;
        [SerializeField] private float _disturbedThreshold = 0.40f;
        [SerializeField] private float _fracturedThreshold = 0.60f;
        [SerializeField] private float _shatteredThreshold = 0.80f;

        [Header("Time-based Ambient Degradation")]
        [Tooltip("Degradation per second of real-time gameplay. The tower erodes your mind just by being in it.")]
        [SerializeField] private float _ambientDegradationPerSecond = 0.0001f;

        // ── State ──────────────────────────────────────────────────────────
        private DegradationTier _currentTier = DegradationTier.Stable;
        public  DegradationTier CurrentTier => _currentTier;

        // ════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ════════════════════════════════════════════════════════════════════

        private void OnEnable()
        {
            GameEvents.OnEnemyKilled    += OnEnemyKilled;
            GameEvents.OnBossDefeated   += OnBossDefeated;
            GameEvents.OnPlayerDamaged  += OnPlayerDamaged;
            GameEvents.OnRunStarted     += OnRunStarted;
        }

        private void OnDisable()
        {
            GameEvents.OnEnemyKilled    -= OnEnemyKilled;
            GameEvents.OnBossDefeated   -= OnBossDefeated;
            GameEvents.OnPlayerDamaged  -= OnPlayerDamaged;
            GameEvents.OnRunStarted     -= OnRunStarted;
        }

        private void OnRunStarted()
        {
            _currentTier = DegradationTier.Stable;
        }

        // ════════════════════════════════════════════════════════════════════
        // UPDATE — Ambient degradation (the tower itself is hostile)
        // ════════════════════════════════════════════════════════════════════

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsRunActive) return;

            // The tower slowly degrades your sanity just by existing
            float ambient = _ambientDegradationPerSecond * Time.deltaTime;

            // Scale ambient degradation with floor progression
            float floorScale = 1f + (gm.Player.FloorsCleared / (float)GameManager.TOTAL_FLOORS);
            ambient *= floorScale;

            AddDegradation(gm.Player, ambient, silent: true);
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called by GameManager during floor transitions.
        /// Applies the per-floor degradation increase.
        /// </summary>
        public void OnFloorTransition(PlayerStats player, FloorConfig completedFloor)
        {
            float increase = _baseDegradationPerFloor;

            // Scale with tier — later tiers degrade faster
            float tierScale = 1f + (int)completedFloor.Tier * 0.3f;
            increase *= tierScale;

            // Low luck accelerates degradation
            float luckPenalty = 1f + (0.5f - player.Luck) * 0.4f; // Luck 0 → 1.2x, Luck 1 → 0.8x
            increase *= luckPenalty;

            AddDegradation(player, increase);

            Debug.Log($"[Degradation] Floor transition: +{increase:F4} " +
                      $"(Tier scale: {tierScale:F2}, Luck penalty: {luckPenalty:F2}) " +
                      $"→ Total: {player.MentalDegradation:F3}");
        }

        // ════════════════════════════════════════════════════════════════════
        // CORE LOGIC
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Adds degradation to the player and checks for tier transitions.
        /// </summary>
        /// <param name="silent">If true, don't log. Used for ambient updates.</param>
        private void AddDegradation(PlayerStats player, float amount, bool silent = false)
        {
            float prev = player.MentalDegradation;
            player.MentalDegradation = Mathf.Clamp01(prev + amount);

            // Only fire events on meaningful changes (avoid spam from ambient)
            if (Mathf.Abs(player.MentalDegradation - prev) > 0.001f || !silent)
            {
                GameEvents.FireDegradationChanged(player.MentalDegradation);
            }

            // Check for tier transitions
            DegradationTier newTier = GetTier(player.MentalDegradation);
            if (newTier != _currentTier)
            {
                DegradationTier oldTier = _currentTier;
                _currentTier = newTier;
                GameEvents.FireDegradationTierReached(newTier);

                Debug.Log($"[Degradation] ████ TIER CHANGE: {oldTier} → {newTier} " +
                          $"(Value: {player.MentalDegradation:F3}) ████");
            }
        }

        /// <summary>
        /// Maps a degradation value (0..1) to its corresponding tier.
        /// </summary>
        private DegradationTier GetTier(float value)
        {
            if (value >= _shatteredThreshold) return DegradationTier.Shattered;
            if (value >= _fracturedThreshold) return DegradationTier.Fractured;
            if (value >= _disturbedThreshold) return DegradationTier.Disturbed;
            if (value >= _uneasyThreshold)    return DegradationTier.Uneasy;
            return DegradationTier.Stable;
        }

        // ════════════════════════════════════════════════════════════════════
        // DEGRADATION QUERIES (for external systems)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the intensity (0..1) for visual effects at the current tier.
        /// Used by post-processing hooks to interpolate effect strength.
        /// </summary>
        public float GetVisualIntensity()
        {
            var player = GameManager.Instance?.Player;
            if (player == null) return 0f;

            float val = player.MentalDegradation;

            // No visual effects below Uneasy
            if (val < _uneasyThreshold) return 0f;

            // Map to 0..1 within the active range
            return Mathf.InverseLerp(_uneasyThreshold, 1f, val);
        }

        /// <summary>
        /// Returns the intensity (0..1) for audio distortion effects.
        /// Audio starts earlier than visual (at Uneasy threshold).
        /// </summary>
        public float GetAudioIntensity()
        {
            var player = GameManager.Instance?.Player;
            if (player == null) return 0f;

            float val = player.MentalDegradation;

            // Audio distortion starts at half the Uneasy threshold
            float audioStart = _uneasyThreshold * 0.5f;
            if (val < audioStart) return 0f;

            return Mathf.InverseLerp(audioStart, 1f, val);
        }

        /// <summary>
        /// Returns the FOV offset for the current degradation level.
        /// Used by the camera controller to create disorienting FOV shifts.
        /// </summary>
        public float GetFOVOffset()
        {
            var player = GameManager.Instance?.Player;
            if (player == null) return 0f;

            float val = player.MentalDegradation;

            // FOV warps start at Disturbed (0.4) and can reach ±15 degrees
            if (val < _disturbedThreshold) return 0f;

            float intensity = Mathf.InverseLerp(_disturbedThreshold, 1f, val);

            // Sinusoidal oscillation that increases in amplitude with degradation
            float oscillation = Mathf.Sin(Time.time * (1f + intensity * 3f));
            return oscillation * intensity * 15f;
        }

        /// <summary>
        /// Returns true if hallucination enemies should be spawned.
        /// At Fractured+ tiers, the game spawns "phantom" enemies that
        /// deal no damage but look/sound real.
        /// </summary>
        public bool ShouldSpawnPhantomEnemies()
        {
            return _currentTier >= DegradationTier.Fractured;
        }

        /// <summary>
        /// Returns the probability (0..1) that a spawned enemy is a phantom.
        /// Only relevant at Fractured+ tiers.
        /// </summary>
        public float PhantomEnemyChance()
        {
            var player = GameManager.Instance?.Player;
            if (player == null) return 0f;

            if (_currentTier < DegradationTier.Fractured) return 0f;

            // 10% at Fractured entry → 40% at Shattered max
            return Mathf.Lerp(0.1f, 0.4f,
                Mathf.InverseLerp(_fracturedThreshold, 1f, player.MentalDegradation)
            );
        }

        // ════════════════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ════════════════════════════════════════════════════════════════════

        private void OnEnemyKilled(Vector3 deathPosition)
        {
            var player = GameManager.Instance?.Player;
            if (player == null) return;

            // Killing enemies slightly degrades sanity (violence takes its toll)
            AddDegradation(player, _killDegradation, silent: true);
        }

        private void OnBossDefeated(string bossID)
        {
            var player = GameManager.Instance?.Player;
            if (player == null) return;

            // Boss defeats cause significant degradation spikes
            AddDegradation(player, _bossDefeatDegradation);

            Debug.Log($"[Degradation] Boss '{bossID}' defeated: " +
                      $"+{_bossDefeatDegradation:F3} degradation");
        }

        private void OnPlayerDamaged(float damage)
        {
            var player = GameManager.Instance?.Player;
            if (player == null) return;

            // Taking damage degrades sanity (pain and fear)
            AddDegradation(player, damage * _damageTakenDegradation, silent: true);
        }
    }
}
