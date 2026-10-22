// ============================================================================
// GameManager.cs — Central Run Lifecycle & 30-Floor Tower Teleportation Orchestrator
// ============================================================================
// Controls: PlayerStats, EnemyBuffState, run state machine, and floor progression.
// Coordinates: LevelGenerator, AIDirector, ElevatorEventSystem.
//
// New Pre-Generation Architecture:
//   - All 30 floors are pre-generated vertically at game start (Floor N at Y = (N-1)*100).
//   - Elevator progression is pure teleportation (no runtime generation or teardown).
//   - Eliminates player void-falling bugs and loading stutter.
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
        Initializing,    // Pre-generating tower
        InFloor,         // Active inside a procedural skyscraper floor
        InElevator,      // Elevator teleportation sequence
        BossEncounter,   // Active boss fight (Floors 10, 20, 30)
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
        public int CurrentFloorNumber { get; private set; } = 1;
        public PlayerStats Player { get; private set; }
        public EnemyBuffState EnemyBuffs { get; private set; }
        public int Currency { get; set; }
        public bool IsRunActive => _currentState == RunState.InFloor || _currentState == RunState.BossEncounter || _currentState == RunState.InElevator;

        // ════════════════════════════════════════════════════════════════════
        // CONSTANTS (30-FLOOR ARCHITECTURE)
        // ════════════════════════════════════════════════════════════════════

        public const int TOTAL_FLOORS = FloorConfig.MAX_TOWER_FLOORS; // 30 floors
        public const int STARTING_CURRENCY = 0;
        public static readonly int[] BossFloors = { 10, 20, 30 };

        // ════════════════════════════════════════════════════════════════════
        // SYSTEM REFERENCES
        // ════════════════════════════════════════════════════════════════════

        [Header("System References")]
        [SerializeField] private AIDirector _aiDirector;
        [SerializeField] private LevelGenerator _levelGenerator;
        [SerializeField] private ElevatorEventSystem _elevatorSystem;
        [SerializeField] private MentalDegradationSystem _degradationSystem;

        private void InitializeSystems()
        {
            if (Player == null) Player = new PlayerStats();
            if (EnemyBuffs == null) EnemyBuffs = new EnemyBuffState();
            Currency = STARTING_CURRENCY;

            if (_aiDirector == null) _aiDirector = FindAnyObjectByType<AIDirector>();
            if (_levelGenerator == null) _levelGenerator = FindAnyObjectByType<LevelGenerator>();
            if (_elevatorSystem == null) _elevatorSystem = FindAnyObjectByType<ElevatorEventSystem>();
            if (_degradationSystem == null) _degradationSystem = FindAnyObjectByType<MentalDegradationSystem>();
        }

        public void SetReferences(
            AIDirector aiDirector,
            LevelGenerator levelGenerator,
            ElevatorEventSystem elevatorSystem,
            MentalDegradationSystem degradationSystem)
        {
            _aiDirector = aiDirector;
            _levelGenerator = levelGenerator;
            _elevatorSystem = elevatorSystem;
            _degradationSystem = degradationSystem;
        }

        // ════════════════════════════════════════════════════════════════════
        // RUN LIFECYCLE (PRE-GENERATED TOWER TELEPORTATION)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Begins a fresh 30-floor run. Ensures all 30 floors are pre-generated
        /// in vertical space and teleports the player to Floor 1.
        /// </summary>
        public void StartNewRun()
        {
            Debug.Log("[GameManager] Initializing procedural city sector run...");

            Player.ResetForNewRun();
            EnemyBuffs.Reset();
            Currency = STARTING_CURRENCY;
            CurrentFloorNumber = 1;

            GameEvents.FireRunStarted();

            // Inicia la generación procedural del sector urbano mediante el nuevo generador modular
            if (_levelGenerator == null) _levelGenerator = FindAnyObjectByType<LevelGenerator>();
            if (_levelGenerator != null)
            {
                _levelGenerator.GenerateCitySector();
            }

            TeleportToFloor(1);
        }

        /// <summary>
        /// Advances to the next floor via instant teleportation.
        /// Called directly by ElevatorDoorTrigger when the player steps into the cabin.
        /// </summary>
        public void AdvanceToNextFloor()
        {
            int nextFloor = CurrentFloorNumber + 1;
            TeleportToFloor(nextFloor);
        }

        public void TransitionToFloor(int floorNumber)
        {
            TeleportToFloor(floorNumber);
        }

        /// <summary>
        /// Instant, safe teleportation to the target floor in the 30-floor tower stack.
        /// Moves the player Transform to the pre-generated floor's start coordinates
        /// (Y = (targetFloor - 1) * 100 + floorSpawnY).
        /// </summary>
        public void TeleportToFloor(int targetFloorNumber)
        {
            if (targetFloorNumber < 1) targetFloorNumber = 1;

            if (targetFloorNumber > TOTAL_FLOORS)
            {
                EndRun(RunEndReason.Victory);
                return;
            }

            Debug.Log($"[GameManager] Teleporting player to Floor {targetFloorNumber} of {TOTAL_FLOORS} (Y={(targetFloorNumber - 1) * FloorConfig.VerticalFloorSpacing})...");

            CurrentFloorNumber = targetFloorNumber;
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

            // Posición de spawn en el origen del sector generado
            Vector3 worldSpawn = Vector3.up * FloorConfig.GetFloorElevation(targetFloorNumber);
            SafelyTeleportPlayer(worldSpawn);

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
            Debug.Log($"[GameManager] Player arrived at Floor {targetFloorNumber} ({config.Type}) at position {worldSpawn}.");
        }

        public void SafelyTeleportPlayer(Vector3 worldPos)
        {
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
            int clearedFloor = CurrentFloorNumber;
            Debug.Log($"[GameManager] Floor {clearedFloor} cleared! Elevator unlocked.");

            // Add clear reward currency
            int reward = 10 + (clearedFloor * 2);
            Currency += reward;

            GameEvents.FireFloorCleared(clearedFloor);
        }

        // ════════════════════════════════════════════════════════════════════
        // ELEVATOR RESUMPTION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called when the player finishes an elevator event (pact/merchant) and proceeds to next floor.
        /// </summary>
        public void ResumeFromElevator(int targetFloorNumber)
        {
            TeleportToFloor(targetFloorNumber);
        }

        // ════════════════════════════════════════════════════════════════════
        // RUN TERMINATION
        // ════════════════════════════════════════════════════════════════════

        public void EndRun(RunEndReason reason)
        {
            SetState(RunState.RunOver);

            Debug.Log($"[GameManager] Run Over! Result: {reason}. Floors cleared: {Player.FloorsCleared}");

            GameEvents.FireRunEnded(reason);
        }

        private void SetState(RunState newState)
        {
            if (_currentState == newState) return;
            _currentState = newState;
            Debug.Log($"[GameManager] Game state changed to: {newState}");
        }
    }
}
