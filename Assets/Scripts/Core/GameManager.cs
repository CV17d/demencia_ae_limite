// ============================================================================
// GameManager.cs — Singleton orchestrator for the entire run lifecycle
// ============================================================================
// Owns: PlayerStats, EnemyBuffState, run state machine, floor progression.
// Coordinates: LevelGenerator, AIDirector, ElevatorEventSystem.
//
// This is deliberately a "thin" manager — it delegates all heavy logic to
// the specialized systems and acts as the single source of truth for
// run state and shared data.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Core
{
    /// <summary>
    /// Run states — strict linear progression with elevator interludes.
    /// </summary>
    public enum RunState
    {
        MainMenu,
        Initializing,    // Setting up a new run
        InFloor,         // Player is inside a procedural maze or boss room
        InElevator,      // Between floors — merchant or pact event
        BossEncounter,   // Boss fight in progress
        RunOver          // Player died or reached victory
    }

    /// <summary>
    /// Singleton GameManager. Persists across scenes via DontDestroyOnLoad.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // SINGLETON
        // ════════════════════════════════════════════════════════════════════

        public static GameManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeSystems();
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC STATE (read by all systems)
        // ════════════════════════════════════════════════════════════════════

        [Header("Run State")]
        [SerializeField] private RunState _currentState = RunState.MainMenu;
        public RunState CurrentState => _currentState;

        /// <summary>Currently active floor configuration.</summary>
        public FloorConfig CurrentFloor { get; private set; }

        /// <summary>Player stats for the active run.</summary>
        public PlayerStats Player { get; private set; }

        /// <summary>Accumulated enemy buffs from Cursed Pacts.</summary>
        public EnemyBuffState EnemyBuffs { get; private set; }

        /// <summary>Player currency for merchant purchases.</summary>
        public int Currency { get; set; }

        // ════════════════════════════════════════════════════════════════════
        // CONSTANTS
        // ════════════════════════════════════════════════════════════════════

        public const int TOTAL_FLOORS       = 27;
        public const int STARTING_CURRENCY  = 0;

        // Boss floor numbers (hard-coded, not magic numbers)
        public static readonly int[] BossFloors = { 9, 18, 27 };

        // ════════════════════════════════════════════════════════════════════
        // SYSTEM REFERENCES (assigned in Inspector or found at runtime)
        // ════════════════════════════════════════════════════════════════════

        [Header("System References")]
        [SerializeField] private AIDirector          _aiDirector;
        [SerializeField] private LevelGenerator      _levelGenerator;
        [SerializeField] private ElevatorEventSystem  _elevatorSystem;
        [SerializeField] private MentalDegradationSystem _degradationSystem;

        // ════════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ════════════════════════════════════════════════════════════════════

        private void InitializeSystems()
        {
            Player     = new PlayerStats();
            EnemyBuffs = new EnemyBuffState();

            // Auto-find systems if not assigned in Inspector
            if (_aiDirector == null)
                _aiDirector = FindAnyObjectByType<AIDirector>();
            if (_levelGenerator == null)
                _levelGenerator = FindAnyObjectByType<LevelGenerator>();
            if (_elevatorSystem == null)
                _elevatorSystem = FindAnyObjectByType<ElevatorEventSystem>();
            if (_degradationSystem == null)
                _degradationSystem = FindAnyObjectByType<MentalDegradationSystem>();
        }

        // ════════════════════════════════════════════════════════════════════
        // RUN LIFECYCLE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called from the main menu to begin a new run.
        /// Resets all state, builds floor 1, and transitions.
        /// </summary>
        public void StartNewRun()
        {
            Debug.Log("[GameManager] Starting new run...");

            // Reset all run data
            Player.ResetForNewRun();
            EnemyBuffs.Reset();
            Currency = STARTING_CURRENCY;

            // Notify all systems
            GameEvents.FireRunStarted();

            // Load floor 1
            TransitionToFloor(1);
        }

        /// <summary>
        /// Core floor transition — called after clearing a floor or from elevator.
        /// </summary>
        public void TransitionToFloor(int floorNumber)
        {
            if (floorNumber < 1 || floorNumber > TOTAL_FLOORS)
            {
                Debug.LogError($"[GameManager] Invalid floor number: {floorNumber}");
                return;
            }

            // Build the floor configuration
            FloorConfig config = FloorConfig.Build(floorNumber);

            // Let the AI Director modify the config before generation
            if (_aiDirector != null)
            {
                _aiDirector.ModifyFloorConfig(config, Player, EnemyBuffs);
            }

            CurrentFloor = config;
            GameEvents.FireFloorLoading(config);

            // Determine state based on floor type
            if (config.Type == FloorType.BossRoom)
            {
                SetState(RunState.BossEncounter);
            }
            else
            {
                SetState(RunState.InFloor);
            }

            // Delegate to LevelGenerator to build the floor
            if (_levelGenerator != null)
            {
                _levelGenerator.GenerateFloor(config);
            }

            GameEvents.FireFloorReady(config);

            Debug.Log($"[GameManager] Floor {floorNumber} ({config.Type}) ready. " +
                      $"Difficulty: {config.BaseDifficulty:F2}, Enemies: {config.BaseEnemyCount}");
        }

        /// <summary>
        /// Called when the player clears the current floor (all enemies dead,
        /// or boss defeated). Triggers elevator or victory.
        /// </summary>
        public void OnCurrentFloorCleared()
        {
            int clearedFloor = CurrentFloor.FloorNumber;
            Player.FloorsCleared++;

            GameEvents.FireFloorCleared(clearedFloor);

            // Check for victory (floor 27 boss defeated)
            if (clearedFloor >= TOTAL_FLOORS)
            {
                EndRun(RunEndReason.Victory);
                return;
            }

            // Enter the elevator between floors
            EnterElevator(clearedFloor + 1);
        }

        /// <summary>
        /// Transitions to the elevator sequence between floors.
        /// </summary>
        private void EnterElevator(int nextFloorNumber)
        {
            SetState(RunState.InElevator);
            GameEvents.FireElevatorEntered();

            // Update mental degradation on floor transition
            if (_degradationSystem != null)
            {
                _degradationSystem.OnFloorTransition(Player, CurrentFloor);
            }

            // The ElevatorEventSystem will call ResumeFromElevator() when done
            if (_elevatorSystem != null)
            {
                _elevatorSystem.TriggerEvent(Player, EnemyBuffs, nextFloorNumber);
            }
        }

        /// <summary>
        /// Called by ElevatorEventSystem when the player has finished
        /// the elevator event (merchant purchase or pact decision).
        /// </summary>
        public void ResumeFromElevator(int nextFloorNumber)
        {
            TransitionToFloor(nextFloorNumber);
        }

        /// <summary>
        /// Called when the player dies or wins.
        /// </summary>
        public void EndRun(RunEndReason reason)
        {
            SetState(RunState.RunOver);
            GameEvents.FireRunEnded(reason);

            Debug.Log($"[GameManager] Run ended: {reason}. " +
                      $"Floors: {Player.FloorsCleared}, Kills: {Player.EnemiesKilled}, " +
                      $"Time: {Player.RunTimer:F1}s");
        }

        // ════════════════════════════════════════════════════════════════════
        // STATE MANAGEMENT
        // ════════════════════════════════════════════════════════════════════

        private void SetState(RunState newState)
        {
            RunState prev = _currentState;
            _currentState = newState;
            Debug.Log($"[GameManager] State: {prev} → {newState}");
        }

        /// <summary>
        /// Returns true if the game is in an active gameplay state.
        /// </summary>
        public bool IsRunActive =>
            _currentState == RunState.InFloor ||
            _currentState == RunState.InElevator ||
            _currentState == RunState.BossEncounter;

        // ════════════════════════════════════════════════════════════════════
        // UPDATE LOOP
        // ════════════════════════════════════════════════════════════════════

        private void Update()
        {
            if (!IsRunActive) return;

            Player.RunTimer += Time.deltaTime;
        }

        // ════════════════════════════════════════════════════════════════════
        // CLEANUP
        // ════════════════════════════════════════════════════════════════════

        private void OnDestroy()
        {
            if (Instance == this)
            {
                GameEvents.ClearAll();
                Instance = null;
            }
        }
    }
}
