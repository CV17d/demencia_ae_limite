// ============================================================================
// EnemySpawner.cs — Dynamic enemy population driven by AI Director & Degradation
// ============================================================================
// Selects enemy archetypes according to AIDirector's EnemyComposition.
// Injects Cursed Pact EnemyBuffState multipliers.
// Injects hallucination PhantomEnemies when Mental Degradation is high.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Enemies;

namespace DemenciaNoLimite.Level
{
    public class EnemySpawner : MonoBehaviour
    {
        [Header("Enemy Data Catalogs")]
        [SerializeField] private EnemyData _gruntData;
        [SerializeField] private EnemyData _rangedData;
        [SerializeField] private EnemyData _heavyData;
        [SerializeField] private EnemyData _eliteData;

        [Header("Prefabs Fallback")]
        [SerializeField] private GameObject _enemyPrefabDefault;
        [SerializeField] private GameObject _phantomEnemyPrefab;

        private readonly List<GameObject> _activeEnemies = new List<GameObject>();
        public int RemainingEnemiesCount => _activeEnemies.Count;

        public void SpawnEnemies(FloorLayout layout, FloorConfig config)
        {
            ClearEnemies();

            var gm = GameManager.Instance;
            var aiDirector = FindAnyObjectByType<AIDirector>();
            var degradationSystem = FindAnyObjectByType<MentalDegradationSystem>();

            EnemyComposition composition = (aiDirector != null) 
                ? aiDirector.GetEnemyComposition(config, gm.EnemyBuffs) 
                : new EnemyComposition();

            int spawnCount = Mathf.Min(config.BaseEnemyCount, layout.EnemySpawnPoints.Count);

            for (int i = 0; i < spawnCount; i++)
            {
                Vector2Int gridPoint = layout.EnemySpawnPoints[i];
                Vector3 worldPos = new Vector3(gridPoint.x * 3.0f, 0.5f, gridPoint.y * 3.0f);

                // Hallucination phantom check at high degradation
                if (degradationSystem != null && degradationSystem.ShouldSpawnPhantomEnemies())
                {
                    if (Random.value < degradationSystem.PhantomEnemyChance())
                    {
                        SpawnPhantomEnemy(worldPos);
                        continue;
                    }
                }

                // Standard enemy selection via Director composition
                EnemyType selectedType = composition.SelectType(Random.value);
                EnemyData selectedData = GetEnemyDataForType(selectedType);

                SpawnRealEnemy(worldPos, selectedData, gm.EnemyBuffs);
            }

            Debug.Log($"[EnemySpawner] Spawned {spawnCount} entities on Floor {config.FloorNumber}.");
        }

        private void SpawnRealEnemy(Vector3 worldPos, EnemyData data, EnemyBuffState buffs)
        {
            GameObject enemyGo;
            if (_enemyPrefabDefault != null)
            {
                enemyGo = Instantiate(_enemyPrefabDefault, worldPos, Quaternion.identity, transform);
            }
            else
            {
                enemyGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                enemyGo.name = $"Enemy_{data?.EnemyID ?? "Grunt"}";
                enemyGo.transform.position = worldPos;
                enemyGo.transform.parent = transform;
            }

            var enemyComp = enemyGo.GetComponent<EnemyBase>();
            if (enemyComp == null)
            {
                enemyComp = enemyGo.AddComponent<EnemyBase>();
            }

            enemyComp.Initialize(data != null ? data : ScriptableObject.CreateInstance<EnemyData>(), buffs);
            _activeEnemies.Add(enemyGo);
        }

        private void SpawnPhantomEnemy(Vector3 worldPos)
        {
            GameObject phantomGo;
            if (_phantomEnemyPrefab != null)
            {
                phantomGo = Instantiate(_phantomEnemyPrefab, worldPos, Quaternion.identity, transform);
            }
            else
            {
                phantomGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                phantomGo.name = "Phantom_Hallucination";
                phantomGo.transform.position = worldPos;
                phantomGo.transform.parent = transform;
            }

            var phantomComp = phantomGo.GetComponent<PhantomEnemy>();
            if (phantomComp == null)
            {
                phantomComp = phantomGo.AddComponent<PhantomEnemy>();
            }
        }

        private EnemyData GetEnemyDataForType(EnemyType type)
        {
            switch (type)
            {
                case EnemyType.Grunt: return _gruntData;
                case EnemyType.Ranged: return _rangedData != null ? _rangedData : _gruntData;
                case EnemyType.Heavy: return _heavyData != null ? _heavyData : _gruntData;
                case EnemyType.Elite: return _eliteData != null ? _eliteData : _gruntData;
                default: return _gruntData;
            }
        }

        public void ClearEnemies()
        {
            foreach (var e in _activeEnemies)
            {
                if (e != null) Destroy(e);
            }
            _activeEnemies.Clear();
        }
    }
}
