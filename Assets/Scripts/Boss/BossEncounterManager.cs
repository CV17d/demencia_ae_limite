// ============================================================================
// BossEncounterManager.cs — Orchestrator for Boss Arena encounters
// ============================================================================
// Spawns the boss, applies AdaptiveBossController stats, locks arena exits,
// and unlocks the elevator gate when the boss is defeated.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Boss
{
    public class BossEncounterManager : MonoBehaviour
    {
        [Header("Boss Prefabs")]
        [SerializeField] private GameObject _bossPrefabDefault;

        [Header("Arena References")]
        [SerializeField] private GameObject _exitElevatorGate;
        [SerializeField] private AdaptiveBossController _bossMathController;

        private BossController _activeBoss;

        private void OnEnable()
        {
            GameEvents.OnFloorReady += OnFloorReady;
            GameEvents.OnBossDefeated += OnBossDefeated;
        }

        private void OnDisable()
        {
            GameEvents.OnFloorReady -= OnFloorReady;
            GameEvents.OnBossDefeated -= OnBossDefeated;
        }

        private void Awake()
        {
            if (_bossMathController == null)
            {
                _bossMathController = FindAnyObjectByType<AdaptiveBossController>();
            }
        }

        private void OnFloorReady(FloorConfig config)
        {
            if (config.Type != FloorType.BossRoom) return;

            Debug.Log($"[BossEncounterManager] Preparing arena for boss encounter: {config.BossID}");

            // Lock exit until boss is defeated
            if (_exitElevatorGate != null)
            {
                _exitElevatorGate.SetActive(false);
            }

            SpawnAndInitializeBoss(config);
        }

        private void SpawnAndInitializeBoss(FloorConfig config)
        {
            var gm = GameManager.Instance;
            if (gm == null || _bossMathController == null) return;

            // Generate adaptive stats
            BossStats stats = _bossMathController.GenerateBossStats(config, gm.Player, gm.EnemyBuffs);

            // Determine spawn location from level layout or center fallback
            var levelGen = FindAnyObjectByType<LevelGenerator>();
            float cellSize = LevelGenerator.StandardCellSize;
            Vector3 spawnPos = Vector3.forward * (10f * cellSize);
            if (levelGen != null && levelGen.LastGeneratedLayout != null)
            {
                var exit = levelGen.LastGeneratedLayout.ExitPoint;
                spawnPos = new Vector3(exit.x * cellSize, 0f, exit.y * cellSize);
            }

            GameObject bossGo = null;
            if (_bossPrefabDefault != null)
            {
                bossGo = Instantiate(_bossPrefabDefault, spawnPos, Quaternion.identity);
            }
            else
            {
                // Fallback primitive: scaled wide (2.6m) and height clamped under ceiling (2.6m)
                bossGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bossGo.name = $"Boss_{stats.BossID}";
                bossGo.transform.position = spawnPos + Vector3.up * 1.3f;
                bossGo.transform.localScale = new Vector3(2.6f, 1.3f, 2.6f);
            }

            _activeBoss = bossGo.GetComponent<BossController>();
            if (_activeBoss == null)
            {
                _activeBoss = bossGo.AddComponent<BossController>();
            }

            _activeBoss.Initialize(stats, _bossMathController);
        }

        private void OnBossDefeated(string bossID)
        {
            Debug.Log($"[BossEncounterManager] Arena cleared! Unlocking elevator gate for {bossID}");

            if (_exitElevatorGate != null)
            {
                _exitElevatorGate.SetActive(true);
            }

            // Report cleared to GameManager
            GameManager.Instance?.OnCurrentFloorCleared();
        }
    }
}
