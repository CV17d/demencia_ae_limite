// ============================================================================
// GameEvents.cs — Centralized event bus for decoupled system communication
// ============================================================================
// Uses C# events (not UnityEvents) for zero-allocation, type-safe signaling.
// All core systems publish/subscribe through this static class.
// ============================================================================

using System;
using DemenciaNoLimite.Core.Data;

namespace DemenciaNoLimite.Core.Events
{
    /// <summary>
    /// Static event bus. All game systems communicate through these events
    /// to maintain loose coupling between modules.
    /// </summary>
    public static class GameEvents
    {
        // ── Run Lifecycle ──────────────────────────────────────────────────
        public static event Action                  OnRunStarted;
        public static event Action<RunEndReason>    OnRunEnded;

        // ── Floor Progression ──────────────────────────────────────────────
        public static event Action<FloorConfig>     OnFloorLoading;
        public static event Action<FloorConfig>     OnFloorReady;
        public static event Action<int>             OnFloorCleared;
        public static event Action                  OnMapGenerationCompleted;
        public static event Action                  OnNavMeshReady;

        // ── Elevator Events ────────────────────────────────────────────────
        public static event Action                  OnElevatorEntered;
        public static event Action<ElevatorResult>  OnElevatorEventResolved;

        // ── Combat / Enemies ───────────────────────────────────────────────
        public static event Action<string>          OnEnemyKilled;       // enemy ID
        public static event Action<string>          OnBossDefeated;      // boss ID
        public static event Action<float>           OnPlayerDamaged;     // damage amount
        public static event Action<float>           OnPlayerHealed;      // heal amount
        public static event Action<Vector3, float>  OnNoiseMade;         // noise position, radius

        // ── Pacts & Merchant ───────────────────────────────────────────────
        public static event Action<CursedPactData>  OnPactAccepted;
        public static event Action<MerchantItemData>OnItemPurchased;

        // ── Mental Degradation ─────────────────────────────────────────────
        public static event Action<float>           OnDegradationChanged; // new value 0..1
        public static event Action<DegradationTier> OnDegradationTierReached;

        // ── AI Director ────────────────────────────────────────────────────
        public static event Action<float>           OnDifficultyWeightChanged; // new weight

        // ── Fire Methods (only called by the owning system) ────────────────

        public static void FireRunStarted()                         => OnRunStarted?.Invoke();
        public static void FireRunEnded(RunEndReason reason)        => OnRunEnded?.Invoke(reason);

        public static void FireFloorLoading(FloorConfig config)     => OnFloorLoading?.Invoke(config);
        public static void FireFloorReady(FloorConfig config)       => OnFloorReady?.Invoke(config);
        public static void FireFloorCleared(int floorNumber)        => OnFloorCleared?.Invoke(floorNumber);
        public static void FireMapGenerationCompleted()             => OnMapGenerationCompleted?.Invoke();
        public static void FireNavMeshReady()                       => OnNavMeshReady?.Invoke();

        public static void FireElevatorEntered()                    => OnElevatorEntered?.Invoke();
        public static void FireElevatorResolved(ElevatorResult r)   => OnElevatorEventResolved?.Invoke(r);

        public static void FireEnemyKilled(string id)               => OnEnemyKilled?.Invoke(id);
        public static void FireBossDefeated(string id)              => OnBossDefeated?.Invoke(id);
        public static void FirePlayerDamaged(float dmg)             => OnPlayerDamaged?.Invoke(dmg);
        public static void FirePlayerHealed(float heal)             => OnPlayerHealed?.Invoke(heal);
        public static void FireNoiseMade(Vector3 pos, float radius) => OnNoiseMade?.Invoke(pos, radius);

        public static void FirePactAccepted(CursedPactData pact)    => OnPactAccepted?.Invoke(pact);
        public static void FireItemPurchased(MerchantItemData item) => OnItemPurchased?.Invoke(item);

        public static void FireDegradationChanged(float val)        => OnDegradationChanged?.Invoke(val);
        public static void FireDegradationTierReached(DegradationTier t) => OnDegradationTierReached?.Invoke(t);

        public static void FireDifficultyWeightChanged(float w)     => OnDifficultyWeightChanged?.Invoke(w);

        /// <summary>
        /// Clears all subscribers. Call on application quit or between runs
        /// to prevent stale references from destroyed MonoBehaviours.
        /// </summary>
        public static void ClearAll()
        {
            OnRunStarted            = null;
            OnRunEnded              = null;
            OnFloorLoading          = null;
            OnFloorReady            = null;
            OnFloorCleared          = null;
            OnMapGenerationCompleted = null;
            OnNavMeshReady          = null;
            OnElevatorEntered       = null;
            OnElevatorEventResolved = null;
            OnEnemyKilled           = null;
            OnBossDefeated          = null;
            OnPlayerDamaged         = null;
            OnPlayerHealed          = null;
            OnNoiseMade             = null;
            OnPactAccepted          = null;
            OnItemPurchased         = null;
            OnDegradationChanged    = null;
            OnDegradationTierReached= null;
            OnDifficultyWeightChanged = null;
        }
    }

    // ── Supporting Enums ───────────────────────────────────────────────────

    public enum RunEndReason
    {
        PlayerDeath,
        Victory,         // Defeated floor 27 boss
        Quit
    }

    public enum ElevatorResult
    {
        Merchant,
        CursedPact
    }

    /// <summary>
    /// Discrete tiers of mental degradation for triggering effects.
    /// </summary>
    public enum DegradationTier
    {
        Stable,      // 0.00 – 0.20 — Normal gameplay
        Uneasy,      // 0.20 – 0.40 — Subtle audio distortions
        Disturbed,   // 0.40 – 0.60 — Visual artifacts, whispers
        Fractured,   // 0.60 – 0.80 — Heavy hallucinations, false enemies
        Shattered    // 0.80 – 1.00 — Reality breaks, extreme distortion
    }
}
