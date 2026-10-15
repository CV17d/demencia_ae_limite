// ============================================================================
// EnemySpawner.cs — Dynamic enemy population driven by AI Director & Degradation
// ============================================================================
// Selects enemy archetypes according to AIDirector's EnemyComposition.
// Injects Cursed Pact EnemyBuffState multipliers.
// Injects hallucination PhantomEnemies when Mental Degradation is high.
// Enforces strict human-scale limits so enemies never clip into ceilings or corridors.
// Spawns enemies strictly on top of generated physical floor tiles.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Enemies;

namespace DemenciaNoLimite.Level
{
    public class EnemySpawner : MonoBehaviour
    {
        [Header("Physical Size & Corridor Constraints")]
        [Tooltip("Ceiling clearance limit (meters) for normal enemies.")]
        [SerializeField] private float _maxAllowedHeight = 1.85f;
        [SerializeField] private float _maxAllowedRadius = 0.42f;

        [Header("Hitbox Auto-Calibration")]
        [SerializeField] private bool _autoCalibrateHitbox = true;

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

        public float MaxAllowedHeight => _maxAllowedHeight;
        public float MaxAllowedRadius => _maxAllowedRadius;

        public void SpawnEnemies(FloorLayout layout, FloorConfig config)
        {
            ClearEnemies();

            if (layout == null || config == null)
            {
                Debug.LogWarning("[EnemySpawner] Cannot spawn enemies: layout or config is null.");
                return;
            }

            var gm = GameManager.Instance;
            var aiDirector = FindAnyObjectByType<AIDirector>();
            var degradationSystem = FindAnyObjectByType<MentalDegradationSystem>();

            EnemyComposition composition = (aiDirector != null && gm != null) 
                ? aiDirector.GetEnemyComposition(config, gm.EnemyBuffs) 
                : new EnemyComposition();

            // Collect robust list of spawn coordinates directly on floor tiles
            List<Vector3> spawnWorldPositions = CollectFloorSpawnPositions(layout, config.BaseEnemyCount);
            int spawnCount = spawnWorldPositions.Count;

            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 worldPos = spawnWorldPositions[i];

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

                SpawnRealEnemy(worldPos, selectedType, selectedData, gm != null ? gm.EnemyBuffs : null);
            }

            Debug.Log($"[EnemySpawner] Spawned {spawnCount} entities on Floor {config.FloorNumber} strictly on floor tiles.");
        }

        private List<Vector3> CollectFloorSpawnPositions(FloorLayout layout, int desiredCount)
        {
            var result = new List<Vector3>();
            float cellSize = LevelGenerator.StandardCellSize;

            // 1. First add validated points from layout.EnemySpawnPoints
            if (layout.EnemySpawnPoints != null)
            {
                foreach (var pt in layout.EnemySpawnPoints)
                {
                    if (pt.x > 0 && pt.x < layout.Width && pt.y > 0 && pt.y < layout.Height)
                    {
                        CellType cell = layout.Grid[pt.x, pt.y];
                        if (cell == CellType.Floor || cell == CellType.Corridor)
                        {
                            result.Add(new Vector3(pt.x * cellSize, 0f, pt.y * cellSize));
                            if (result.Count >= desiredCount) return result;
                        }
                    }
                }
            }

            // 2. If quota not reached, scan all walkable floor tiles away from player
            if (result.Count < desiredCount)
            {
                var candidateTiles = new List<Vector2Int>();
                for (int x = 1; x < layout.Width - 1; x++)
                {
                    for (int y = 1; y < layout.Height - 1; y++)
                    {
                        CellType c = layout.Grid[x, y];
                        if (c == CellType.Floor || c == CellType.Corridor)
                        {
                            Vector2Int pos = new Vector2Int(x, y);
                            if (pos != layout.SpawnPoint && pos != layout.ExitPoint)
                            {
                                float distToSpawn = Vector2Int.Distance(pos, layout.SpawnPoint);
                                if (distToSpawn > 3.5f)
                                {
                                    candidateTiles.Add(pos);
                                }
                            }
                        }
                    }
                }

                // Shuffle candidates
                for (int i = 0; i < candidateTiles.Count; i++)
                {
                    int swapIdx = Random.Range(i, candidateTiles.Count);
                    var temp = candidateTiles[i];
                    candidateTiles[i] = candidateTiles[swapIdx];
                    candidateTiles[swapIdx] = temp;
                }

                foreach (var tile in candidateTiles)
                {
                    Vector3 worldPos = new Vector3(tile.x * cellSize, 0f, tile.y * cellSize);
                    if (!result.Contains(worldPos))
                    {
                        result.Add(worldPos);
                        if (result.Count >= desiredCount) break;
                    }
                }
            }

            // Fallback emergency center if level is exceptionally small
            if (result.Count == 0)
            {
                result.Add(new Vector3(layout.SpawnPoint.x * cellSize + 6f, 0f, layout.SpawnPoint.y * cellSize + 6f));
            }

            return result;
        }

        private void SpawnRealEnemy(Vector3 worldPos, EnemyType type, EnemyData data, EnemyBuffState buffs)
        {
            GetArchetypeDimensions(type, out float targetHeight, out float targetRadius);

            GameObject enemyGo;
            bool isPrimitive = false;

            if (_enemyPrefabDefault != null)
            {
                enemyGo = Instantiate(_enemyPrefabDefault, worldPos, Quaternion.identity, transform);
            }
            else
            {
                isPrimitive = true;
                enemyGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                enemyGo.name = $"Enemy_{data?.EnemyID ?? type.ToString()}";
                // Primitive capsule has pivot at center, so position at half height
                enemyGo.transform.position = worldPos + Vector3.up * (targetHeight * 0.5f);
                enemyGo.transform.parent = transform;
            }

            // Ensure layer is Enemy if defined, otherwise keep default
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1)
            {
                enemyGo.layer = enemyLayer;
            }
            enemyGo.tag = "Enemy";

            // Enforce strict scaling and calibrate collider
            EnforceEnemyScaleAndHitbox(enemyGo, targetHeight, targetRadius, isPrimitive);

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
            GetArchetypeDimensions(EnemyType.Grunt, out float targetHeight, out float targetRadius);

            GameObject phantomGo;
            bool isPrimitive = false;

            if (_phantomEnemyPrefab != null)
            {
                phantomGo = Instantiate(_phantomEnemyPrefab, worldPos, Quaternion.identity, transform);
            }
            else
            {
                isPrimitive = true;
                phantomGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                phantomGo.name = "Phantom_Hallucination";
                phantomGo.transform.position = worldPos + Vector3.up * (targetHeight * 0.5f);
                phantomGo.transform.parent = transform;
            }

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1)
            {
                phantomGo.layer = enemyLayer;
            }
            phantomGo.tag = "Enemy";

            EnforceEnemyScaleAndHitbox(phantomGo, targetHeight, targetRadius, isPrimitive);

            var phantomComp = phantomGo.GetComponent<PhantomEnemy>();
            if (phantomComp == null)
            {
                phantomComp = phantomGo.AddComponent<PhantomEnemy>();
            }
            _activeEnemies.Add(phantomGo);
        }

        /// <summary>
        /// Retrieves proportional physical dimensions (height & radius) per enemy archetype.
        /// Strictly clamped to be equal or smaller than the player's scale (<= 1.85m height).
        /// </summary>
        public void GetArchetypeDimensions(EnemyType type, out float height, out float radius)
        {
            float maxAllowed = _maxAllowedHeight;
            float desiredHeight;
            float desiredRadius;

            switch (type)
            {
                case EnemyType.Heavy:
                    desiredHeight = 1.85f;
                    desiredRadius = 0.42f;
                    break;
                case EnemyType.Elite:
                    desiredHeight = 1.80f;
                    desiredRadius = 0.38f;
                    break;
                case EnemyType.Ranged:
                    desiredHeight = 1.65f;
                    desiredRadius = 0.32f;
                    break;
                case EnemyType.Grunt:
                default:
                    desiredHeight = 1.75f;
                    desiredRadius = 0.36f;
                    break;
            }

            height = Mathf.Min(desiredHeight, maxAllowed);
            radius = Mathf.Min(desiredRadius, _maxAllowedRadius);
        }

        /// <summary>
        /// Clamps the enemy transform scale to fit building constraints and calibrates the CapsuleCollider.
        /// </summary>
        public void EnforceEnemyScaleAndHitbox(GameObject enemyGo, float targetHeight, float targetRadius, bool isPrimitive)
        {
            if (enemyGo == null) return;

            if (isPrimitive)
            {
                // Primitive capsule has unscaled height 2.0 and diameter 1.0 (radius 0.5)
                float scaleFactorY = targetHeight / 2.0f;
                float scaleFactorXZ = (targetRadius * 2.0f) / 1.0f;
                enemyGo.transform.localScale = new Vector3(scaleFactorXZ, scaleFactorY, scaleFactorXZ);

                var cap = enemyGo.GetComponent<CapsuleCollider>();
                if (cap != null)
                {
                    cap.height = 2.0f;
                    cap.radius = 0.5f;
                    cap.center = Vector3.zero;
                    cap.direction = 1; // Y-Axis
                }
            }
            else
            {
                float currentHeight = MeasureCharacterHeight(enemyGo);
                if (currentHeight > 0.1f && currentHeight > targetHeight)
                {
                    float scaleRatio = targetHeight / currentHeight;
                    enemyGo.transform.localScale *= scaleRatio;
                }

                if (_autoCalibrateHitbox)
                {
                    CalibrateCapsuleCollider(enemyGo, targetHeight, targetRadius);
                }
            }

            // Synchronize NavMeshAgent dimensions
            var agent = enemyGo.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = targetHeight;
                agent.radius = targetRadius;
                agent.baseOffset = 0f;
            }
        }

        private float MeasureCharacterHeight(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            bool hasBounds = false;
            Bounds combined = new Bounds(go.transform.position, Vector3.zero);

            foreach (var r in renderers)
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    if (!hasBounds)
                    {
                        combined = r.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(r.bounds);
                    }
                }
            }

            return hasBounds ? combined.size.y : 1.8f;
        }

        private void CalibrateCapsuleCollider(GameObject go, float targetHeight, float targetRadius)
        {
            var cap = go.GetComponent<CapsuleCollider>();
            if (cap == null)
            {
                cap = go.AddComponent<CapsuleCollider>();
            }

            float localScaleY = Mathf.Max(0.001f, go.transform.lossyScale.y);
            float localScaleXZ = Mathf.Max(0.001f, Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.z));

            float localHeight = targetHeight / localScaleY;
            float localRadius = targetRadius / localScaleXZ;

            cap.height = localHeight;
            cap.radius = localRadius;
            cap.center = new Vector3(0f, localHeight * 0.5f, 0f);
            cap.direction = 1; // Y-Axis
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
