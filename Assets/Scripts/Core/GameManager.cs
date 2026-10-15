// ============================================================================
// GameManager.cs — Central Run Lifecycle & Floor Transition Orchestrator
// ============================================================================
// Controls: PlayerStats, EnemyBuffState, run state machine, and floor progression.
// Coordinates: LevelGenerator, LevelBuilder, AIDirector, ElevatorEventSystem.
//
// Implements the strict, robust 4-step floor transition sequence:
//   a) Increment the current floor counter.
//   b) Memory Management: Destroy the entire container GameObject of the previous
//      floor's geometry to free up RAM.
//   c) Trigger LevelGenerator & LevelBuilder to instantiate the new floor.
//   d) Safely teleport the player's Transform to the Start Room of the new floor.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Level;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Core
{
    public enum RunState
    {
        MainMenu,
        Initializing,    // Setting up a new run
        InFloor,         // Active inside a procedural skyscraper floor
        InElevator,      // Elevator transition sequence
        BossEncounter,   // Active boss fight
        RunOver          // Victory or Player Death
    }

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
        // PUBLIC RUN STATE
        // ════════════════════════════════════════════════════════════════════

        [Header("Run State")]
        [SerializeField] private RunState _currentState = RunState.MainMenu;
        public RunState CurrentState => _currentState;

        public FloorConfig CurrentFloor { get; private set; }
        public PlayerStats Player { get; private set; }
        public EnemyBuffState EnemyBuffs { get; private set; }
        public int Currency { get; set; }

        // ════════════════════════════════════════════════════════════════════
        // CONSTANTS
        // ════════════════════════════════════════════════════════════════════

        public const int TOTAL_FLOORS = 27;
        public const int STARTING_CURRENCY = 0;
        public static readonly int[] BossFloors = { 9, 18, 27 };

        // ════════════════════════════════════════════════════════════════════
        // SYSTEM REFERENCES
        // ════════════════════════════════════════════════════════════════════

        [Header("System References")]
        [SerializeField] private AIDirector _aiDirector;
        [SerializeField] private LevelGenerator _levelGenerator;
        [SerializeField] private LevelBuilder _levelBuilder;
        [SerializeField] private ElevatorEventSystem _elevatorSystem;
        [SerializeField] private MentalDegradationSystem _degradationSystem;

        // ════════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ════════════════════════════════════════════════════════════════════

        private void InitializeSystems()
        {
            if (Player == null) Player = new PlayerStats();
            if (EnemyBuffs == null) EnemyBuffs = new EnemyBuffState();
            Currency = STARTING_CURRENCY;

            // Auto-find references if unassigned in Inspector
            if (_aiDirector == null) _aiDirector = FindAnyObjectByType<AIDirector>();
            if (_levelGenerator == null) _levelGenerator = FindAnyObjectByType<LevelGenerator>();
            if (_levelBuilder == null) _levelBuilder = FindAnyObjectByType<LevelBuilder>();
            if (_elevatorSystem == null) _elevatorSystem = FindAnyObjectByType<ElevatorEventSystem>();
            if (_degradationSystem == null) _degradationSystem = FindAnyObjectByType<MentalDegradationSystem>();
        }

        public void SetReferences(
            AIDirector aiDirector,
            LevelGenerator levelGenerator,
            LevelBuilder levelBuilder,
            ElevatorEventSystem elevatorSystem,
            MentalDegradationSystem degradationSystem)
        {
            _aiDirector = aiDirector;
            _levelGenerator = levelGenerator;
            _levelBuilder = levelBuilder;
            _elevatorSystem = elevatorSystem;
            _degradationSystem = degradationSystem;
        }

        // ════════════════════════════════════════════════════════════════════
        // RUN LIFECYCLE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Begins a fresh run from Floor 1.
        /// </summary>
        public void StartNewRun()
        {
            Debug.Log("[GameManager] Starting new 27-floor run...");

            Player.ResetForNewRun();
            EnemyBuffs.Reset();
            Currency = STARTING_CURRENCY;

            GameEvents.FireRunStarted();
            ExecuteFloorTransition(1);
        }

        /// <summary>
        /// Advances sequentially to the next floor.
        /// Called directly by ElevatorDoorTrigger when the player steps into the cabin.
        /// </summary>
        public void AdvanceToNextFloor()
        {
            int nextFloor = (CurrentFloor != null) ? CurrentFloor.FloorNumber + 1 : 1;
            ExecuteFloorTransition(nextFloor);
        }

        public void TransitionToFloor(int floorNumber)
        {
            ExecuteFloorTransition(floorNumber);
        }

        /// <summary>
        /// Core floor transition sequence:
        ///   a) Increment current floor counter.
        ///   b) Memory Management: Destroy previous floor geometry container to free RAM.
        ///   c) Trigger LevelGenerator & LevelBuilder to instantiate the new floor.
        ///   d) Safely teleport the player Transform to the Start Room.
        /// </summary>
        public void ExecuteFloorTransition(int targetFloorNumber)
        {
            if (targetFloorNumber < 1) targetFloorNumber = 1;

            if (targetFloorNumber > TOTAL_FLOORS)
            {
                EndRun(RunEndReason.Victory);
                return;
            }

            Debug.Log($"[GameManager] === EXECUTING FLOOR TRANSITION TO FLOOR {targetFloorNumber} ===");

            // ── STEP A: Increment floor counter & build config ──────────────
            Player.FloorsCleared = targetFloorNumber - 1;
            FloorConfig config = FloorConfig.Build(targetFloorNumber);

            if (_aiDirector != null)
            {
                _aiDirector.ModifyFloorConfig(config, Player, EnemyBuffs);
            }

            CurrentFloor = config;

            if (_degradationSystem != null)
            {
                _degradationSystem.OnFloorTransition(Player, config);
            }

            GameEvents.FireFloorCleared(targetFloorNumber - 1);
            GameEvents.FireFloorLoading(config);

            // ── STEP B: Memory Management — Destroy previous floor container
            if (_levelBuilder == null) _levelBuilder = FindAnyObjectByType<LevelBuilder>();
            if (_levelBuilder != null)
            {
                _levelBuilder.TeardownFloorGeometry();
            }
            else
            {
                Debug.LogWarning("[GameManager] LevelBuilder reference missing during teardown!");
            }

            // ── STEP C: Trigger LevelGenerator to generate and build layout ──
            FloorLayout layout = null;
            if (_levelGenerator == null) _levelGenerator = FindAnyObjectByType<LevelGenerator>();

            if (_levelGenerator != null)
            {
                layout = _levelGenerator.GenerateFloor(config);
            }
            else
            {
                Debug.LogError("[GameManager] Critical: LevelGenerator reference missing!");
            }

            if (_levelBuilder != null && layout != null)
            {
                _levelBuilder.BuildFloorFromLayout(layout, config);
            }
            else if (_levelBuilder == null)
            {
                Debug.LogError("[GameManager] Critical: LevelBuilder reference missing!");
            }

            // ── STEP D: Safely teleport player Transform to Start Room ──────
            if (layout != null)
            {
                if (_levelBuilder != null)
                {
                    _levelBuilder.PositionPlayerAtSpawn(layout.SpawnPoint);
                }
                else
                {
                    SafelyTeleportPlayer(layout.SpawnPoint);
                }
            }

            // Update State
            if (config.Type == FloorType.BossRoom)
            {
                SetState(RunState.BossEncounter);
            }
            else
            {
                SetState(RunState.InFloor);
            }

            GameEvents.FireFloorReady(config);

            Debug.Log($"[GameManager] Transition successful. Now on Floor {targetFloorNumber} ({config.Type}).");
        }

        private void SafelyTeleportPlayer(Vector2Int spawnGrid)
        {
            Vector3 worldPos = new Vector3(
                spawnGrid.x * LevelGenerator.StandardCellSize,
                1.0f,
                spawnGrid.y * LevelGenerator.StandardCellSize
            );

            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null)
            {
                pc.Teleport(worldPos, Quaternion.identity);
                return;
            }

            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null)
            {
                var cc = playerGo.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                playerGo.transform.position = worldPos;
                playerGo.transform.rotation = Quaternion.identity;
                if (cc != null) cc.enabled = true;
            }
        }

        public void OnCurrentFloorCleared()
        {
            int clearedFloor = (CurrentFloor != null) ? CurrentFloor.FloorNumber : 1;
            if (clearedFloor >= TOTAL_FLOORS)
            {
                EndRun(RunEndReason.Victory);
                return;
            }

            AdvanceToNextFloor();
        }

        public void ResumeFromElevator(int nextFloorNumber)
        {
            ExecuteFloorTransition(nextFloorNumber);
        }

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

        public bool IsRunActive =>
            _currentState == RunState.InFloor ||
            _currentState == RunState.InElevator ||
            _currentState == RunState.BossEncounter;

        private void Update()
        {
            if (!IsRunActive) return;
            if (Player != null)
            {
                Player.RunTimer += Time.deltaTime;
            }
        }

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
