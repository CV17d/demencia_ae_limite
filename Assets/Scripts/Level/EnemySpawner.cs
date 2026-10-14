// ============================================================================
// EnemySpawner.cs — Dynamic enemy population driven by AI Director & Degradation
// ============================================================================
// Selects enemy archetypes according to AIDirector's EnemyComposition.
// Injects Cursed Pact EnemyBuffState multipliers.
// Injects hallucination PhantomEnemies when Mental Degradation is high.
// Enforces indoor ceiling height constraints and hitbox calibration for rooms.
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
        [Header("Enemy Data Catalogs")]
        [SerializeField] private EnemyData _gruntData;
        [SerializeField] private EnemyData _rangedData;
        [SerializeField] private EnemyData _heavyData;
        [SerializeField] private EnemyData _eliteData;

        [Header("Prefabs Fallback")]
        [SerializeField] private GameObject _enemyPrefabDefault;
        [SerializeField] private GameObject _phantomEnemyPrefab;

        [Header("Indoor Scale & Ceiling Constraints")]
        [Tooltip("Minimum headroom clearance beneath the room ceiling. Enemy height will be clamped below (Ceiling - Margin).")]
        [SerializeField] private float _ceilingClearance = 1.0f;

        [Tooltip("Hard ceiling limit for enemy height inside rooms (e.g. 1.85m to 2.1m for 3.0m ceilings).")]
        [SerializeField] private float _maxEnemyHeight = 2.0f;

        [Tooltip("Automatically calibrate and align CapsuleCollider dimensions to strictly fit model.")]
        [SerializeField] private bool _autoCalibrateHitbox = true;

        private readonly List<GameObject> _activeEnemies = new List<GameObject>();
        public int RemainingEnemiesCount => _activeEnemies.Count;

        /// <summary>
        /// Returns the maximum allowable enemy height strictly lower than the procedural ceiling.
        /// </summary>
        public float MaxAllowedHeight
        {
            get
            {
                float ceiling = LevelGenerator.StandardCeilingHeight;
                var levelBuilder = FindAnyObjectByType<LevelBuilder>();
                if (levelBuilder != null && levelBuilder.WallHeight > 0f)
                {
                    ceiling = levelBuilder.WallHeight;
                }

                float safeCeilingLimit = Mathf.Max(1.5f, ceiling - _ceilingClearance);
                return Mathf.Min(_maxEnemyHeight, safeCeilingLimit);
            }
        }

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

            float cellSize = LevelGenerator.StandardCellSize;
            var levelBuilder = FindAnyObjectByType<LevelBuilder>();
            if (levelBuilder != null)
            {
                cellSize = levelBuilder.CellSize;
            }

            for (int i = 0; i < spawnCount; i++)
            {
                Vector2Int gridPoint = layout.EnemySpawnPoints[i];
                // Floor tiles have surface at Y = 0.0f
                Vector3 worldPos = new Vector3(gridPoint.x * cellSize, 0f, gridPoint.y * cellSize);

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

                SpawnRealEnemy(worldPos, selectedType, selectedData, gm.EnemyBuffs);
            }

            Debug.Log($"[EnemySpawner] Spawned {spawnCount} entities on Floor {config.FloorNumber} with indoor scale enforcement (MaxHeight: {MaxAllowedHeight:F2}m).");
        }

        private void SpawnRealEnemy(Vector3 worldPos, EnemyType type, EnemyData data, EnemyBuffState buffs)
        {
            GetArchetypeDimensions(type, out float targetHeight, out float targetRadius);

            GameObject enemyGo;
            bool isPrimitive = false;

            if (_enemyPrefabDefault != null)
            {
                // Prefabs with pivot at feet spawn at ground level (Y = 0)
                enemyGo = Instantiate(_enemyPrefabDefault, worldPos, Quaternion.identity, transform);
            }
            else
            {
                isPrimitive = true;
                enemyGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                enemyGo.name = $"Enemy_{data?.EnemyID ?? type.ToString()}";
                // Primitive capsule has pivot at center, so offset up by half height to rest on floor
                enemyGo.transform.position = worldPos + Vector3.up * (targetHeight * 0.5f);
                enemyGo.transform.parent = transform;
            }

            // Enforce size limit & hitbox accuracy
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
            float targetHeight = Mathf.Min(1.8f, MaxAllowedHeight);
            float targetRadius = 0.38f;

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

            EnforceEnemyScaleAndHitbox(phantomGo, targetHeight, targetRadius, isPrimitive);

            var phantomComp = phantomGo.GetComponent<PhantomEnemy>();
            if (phantomComp == null)
            {
                phantomComp = phantomGo.AddComponent<PhantomEnemy>();
            }
        }

        /// <summary>
        /// Retrieves proportional physical dimensions (height & radius) per enemy archetype.
        /// Guaranteed to be strictly lower than building ceiling.
        /// </summary>
        public void GetArchetypeDimensions(EnemyType type, out float height, out float radius)
        {
            float maxAllowed = MaxAllowedHeight;
            float desiredHeight;
            float desiredRadius;

            switch (type)
            {
                case EnemyType.Heavy:
                    desiredHeight = 2.1f;
                    desiredRadius = 0.48f;
                    break;
                case EnemyType.Elite:
                    desiredHeight = 1.95f;
                    desiredRadius = 0.42f;
                    break;
                case EnemyType.Ranged:
                    desiredHeight = 1.75f;
                    desiredRadius = 0.35f;
                    break;
                case EnemyType.Grunt:
                default:
                    desiredHeight = 1.8f;
                    desiredRadius = 0.38f;
                    break;
            }

            height = Mathf.Min(desiredHeight, maxAllowed);
            radius = desiredRadius * (height / desiredHeight);
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
                // 1. Measure prefab's natural visual height
                float currentHeight = MeasureCharacterHeight(enemyGo, out Bounds bounds);
                if (currentHeight > 0.1f && currentHeight > targetHeight)
                {
                    float scaleRatio = targetHeight / currentHeight;
                    enemyGo.transform.localScale *= scaleRatio;
                }

                // 2. Adjust / ensure accurate hitbox
                if (_autoCalibrateHitbox)
                {
                    CalibrateCapsuleCollider(enemyGo, targetHeight, targetRadius);
                }
            }

            // 3. Synchronize NavMeshAgent dimensions to prevent ceiling or doorway blockage
            var agent = enemyGo.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = targetHeight;
                agent.radius = targetRadius;
                agent.baseOffset = 0f;
            }
        }

        private float MeasureCharacterHeight(GameObject go, out Bounds combinedBounds)
        {
            combinedBounds = new Bounds(go.transform.position, Vector3.zero);
            bool hasBounds = false;

            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    if (!hasBounds)
                    {
                        combinedBounds = r.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(r.bounds);
                    }
                }
            }

            if (!hasBounds)
            {
                var colliders = go.GetComponentsInChildren<Collider>();
                foreach (var c in colliders)
                {
                    if (!c.isTrigger)
                    {
                        if (!hasBounds)
                        {
                            combinedBounds = c.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            combinedBounds.Encapsulate(c.bounds);
                        }
                    }
                }
            }

            return hasBounds ? combinedBounds.size.y : 2.0f;
        }

        private void CalibrateCapsuleCollider(GameObject go, float targetHeight, float targetRadius)
        {
            var cap = go.GetComponent<CapsuleCollider>();
            if (cap == null)
            {
                cap = go.AddComponent<CapsuleCollider>();
            }

            // In local coordinates of go:
            float localScaleY = Mathf.Max(0.001f, go.transform.lossyScale.y);
            float localScaleXZ = Mathf.Max(0.001f, Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.z));

            float localHeight = targetHeight / localScaleY;
            float localRadius = targetRadius / localScaleXZ;

            // Detect if pivot is at feet (local bottom Y near 0) or center (local bottom Y negative)
            var rend = go.GetComponentInChildren<Renderer>();
            bool isPivotAtFeet = true;
            if (rend != null)
            {
                float localBottomY = go.transform.InverseTransformPoint(rend.bounds.min).y;
                if (localBottomY < -localHeight * 0.3f)
                {
                    isPivotAtFeet = false;
                }
            }

            cap.height = localHeight;
            cap.radius = localRadius;
            cap.center = isPivotAtFeet ? new Vector3(0f, localHeight * 0.5f, 0f) : Vector3.zero;
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

