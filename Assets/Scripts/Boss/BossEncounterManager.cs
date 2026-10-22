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
using DemenciaNoLimite.Level;

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
            GameEvents.OnFloorLoading += OnFloorLoading;
            GameEvents.OnFloorReady += OnFloorReady;
            GameEvents.OnBossDefeated += OnBossDefeated;
        }

        private void OnDisable()
        {
            GameEvents.OnFloorLoading -= OnFloorLoading;
            GameEvents.OnFloorReady -= OnFloorReady;
            GameEvents.OnBossDefeated -= OnBossDefeated;
        }

        private void OnFloorLoading(FloorConfig config)
        {
            CleanupBoss();
        }

        public void CleanupBoss()
        {
            if (_activeBoss != null)
            {
                Destroy(_activeBoss.gameObject);
                _activeBoss = null;
            }
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

            CleanupBoss();

            // Generate adaptive stats
            BossStats stats = _bossMathController.GenerateBossStats(config, gm.Player, gm.EnemyBuffs);

            // Determine spawn location at the physical center and elevation of the boss arena
            var levelGen = FindAnyObjectByType<LevelGenerator>();
            float cellSize = LevelGenerator.StandardCellSize;
            int arenaW = (levelGen != null && levelGen.LastGeneratedLayout != null) ? levelGen.LastGeneratedLayout.Width : 24;
            int arenaH = (levelGen != null && levelGen.LastGeneratedLayout != null) ? levelGen.LastGeneratedLayout.Height : 24;
            float elevation = FloorConfig.GetFloorElevation(config.FloorNumber);
            Vector3 spawnPos = new Vector3((arenaW / 2) * cellSize, elevation, (arenaH / 2) * cellSize);

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

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1) bossGo.layer = enemyLayer;
            bossGo.tag = "Enemy";

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
