// ============================================================================
// EnemySpawner.cs — Generador de Enemigos por Oleadas con Raycast al Suelo (Unity 6)
// ============================================================================
// Gestiona la aparición (spawn) progresiva de enemigos (prioritariamente EnemigoFlotante)
// asegurando estrictamente que nazcan apoyados sobre la superficie transitable del suelo
// (mediante Physics.Raycast vertical) para que su levitación natural (FloatingEnemyAI)
// inicie fluidamente desde el piso hacia arriba.
//
// Incluye:
// 1. Raycast vertical defensivo para anclar el spawn exactamente a la superficie real.
// 2. Sistema de oleadas progresivas con escalado de dificultad por piso y oleada.
// 3. Generación procedural en radio seguro alrededor del jugador (cuando no hay spawnPoints).
// 4. Control de conteo de enemigos en tiempo real y compatibilidad con arquitecturas previas.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Enemies;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DemenciaNoLimite.Level
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Level/Enemy Spawner")]
    public class EnemySpawner : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // PREFABS Y CATÁLOGOS
        // ════════════════════════════════════════════════════════════════════

        [Header("Prefabs de Enemigos")]
        [Tooltip("Prefab del EnemigoFlotante (Assets/Prefabs/EnemigoFlotante.prefab).")]
        [SerializeField] private GameObject _floatingEnemyPrefab;

        [Tooltip("Prefab por defecto de respaldo para enemigos convencionales.")]
        [SerializeField] private GameObject _enemyPrefabDefault;

        [Tooltip("Prefab para alucinaciones espectrales (PhantomEnemy).")]
        [SerializeField] private GameObject _phantomEnemyPrefab;

        [Header("Enemy Data Catalogs")]
        [SerializeField] private EnemyData _gruntData;
        [SerializeField] private EnemyData _rangedData;
        [SerializeField] private EnemyData _heavyData;
        [SerializeField] private EnemyData _eliteData;

        // ════════════════════════════════════════════════════════════════════
        // PUNTOS DE APARICIÓN Y RAYCAST AL SUELO
        // ════════════════════════════════════════════════════════════════════

        [Header("Puntos de Aparición (Spawn Points)")]
        [Tooltip("Puntos manuales de generación en la escena. Si está vacío, se generan proceduralmente alrededor del jugador.")]
        [SerializeField] private Transform[] _spawnPoints;

        [Header("Detección y Anclaje al Suelo (Ground Raycast)")]
        [Tooltip("Máscara de capas que representan el suelo caminable/transitable.")]
        [SerializeField] private LayerMask _groundLayer = ~0;

        [Tooltip("Capa de obstáculos/paredes para verificar que el punto de spawn esté despejado.")]
        [SerializeField] private LayerMask _obstacleLayer = 0;

        [Tooltip("Altura de seguridad superior desde donde se dispara el Raycast vertical hacia abajo.")]
        [Range(2f, 25f)]
        [SerializeField] private float _raycastOriginHeight = 5.0f;

        [Tooltip("Distancia máxima del raycast vertical hacia abajo.")]
        [Range(5f, 50f)]
        [SerializeField] private float _raycastMaxDistance = 20.0f;

        [Tooltip("Pendiente mínima aceptable del suelo (normal.y > 0.65 equivale a suelo transitable, evitando paredes).")]
        [Range(0.4f, 0.95f)]
        [SerializeField] private float _minWalkableSlope = 0.65f;

        [Tooltip("Desplazamiento vertical mínimo sobre la superficie de impacto para evitar solapamientos con el suelo.")]
        [SerializeField] private float _groundSpawnOffset = 0.02f;

        [Header("Generación Procedural Alrededor del Jugador")]
        [Tooltip("Distancia mínima al jugador para no instanciar enemigos en su campo visual directo o encima de él.")]
        [Range(3f, 20f)]
        [SerializeField] private float _minSafeDistance = 8.0f;

        [Tooltip("Radio máximo de búsqueda procedural alrededor del jugador.")]
        [Range(10f, 40f)]
        [SerializeField] private float _maxSpawnRadius = 22.0f;

        [Tooltip("Intentos máximos de muestreo aleatorio antes de usar la posición de respaldo.")]
        [SerializeField] private int _maxProceduralAttempts = 16;

        // ════════════════════════════════════════════════════════════════════
        // SISTEMA DE OLEADAS PROGRESIVAS (WAVE SYSTEM)
        // ════════════════════════════════════════════════════════════════════

        [Header("Sistema de Oleadas Progresivas")]
        [Tooltip("¿Activar el sistema continuo de oleadas automáticamente?")]
        [SerializeField] private bool _enableWaveSystem = true;

        [Tooltip("¿Iniciar las oleadas en Start()?")]
        [SerializeField] private bool _autoStartWavesOnStart = true;

        [Tooltip("Número de la oleada actual.")]
        [SerializeField] private int _currentWaveNumber = 1;

        [Tooltip("Cantidad base de enemigos en la primera oleada.")]
        [SerializeField] private int _baseEnemiesPerWave = 3;

        [Tooltip("Incremento lineal de enemigos adicionales por cada nueva oleada.")]
        [SerializeField] private int _enemiesIncrementPerWave = 2;

        [Tooltip("Tiempo de espera en segundos entre la eliminación de una oleada y el inicio de la siguiente.")]
        [SerializeField] private float _timeBetweenWaves = 5.0f;

        [Tooltip("Tiempo de retraso entre la aparición de cada enemigo individual dentro de una misma oleada.")]
        [SerializeField] private float _delayBetweenSpawns = 0.45f;

        [Tooltip("Límite máximo de enemigos vivos simultáneos en escena para no saturar rendimiento.")]
        [SerializeField] private int _maxConcurrentEnemies = 20;

        [Header("Dificultad por Pisos")]
        [Tooltip("Piso actual de la torre (afecta multiplicador de dificultad).")]
        [SerializeField] private int _currentFloor = 1;

        [Tooltip("Porcentaje de incremento de enemigos por piso (ej. 0.15 = +15% por piso).")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float _difficultyScalePerFloor = 0.15f;

        // ════════════════════════════════════════════════════════════════════
        // RESTRICCIONES DE ESCALA Y CORREDORES (COMPATIBILIDAD)
        // ════════════════════════════════════════════════════════════════════

        [Header("Restricciones de Espacio y Hitbox")]
        [SerializeField] private float _maxAllowedHeight = 1.85f;
        [SerializeField] private float _maxAllowedRadius = 0.42f;
        [SerializeField] private bool _autoCalibrateHitbox = true;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y EVENTOS
        // ════════════════════════════════════════════════════════════════════

        private readonly List<GameObject> _activeEnemies = new List<GameObject>();
        private Coroutine _waveCycleCoroutine;
        private Transform _playerTransform;
        private Transform _enemiesContainer;
        private bool _isWaveSystemRunning = false;
        private int _totalEnemiesTargetForCurrentWave = 0;
        private int _enemiesSpawnedInCurrentWave = 0;

        public int RemainingEnemiesCount => GetLivingEnemiesCount();
        public int CurrentWaveNumber => _currentWaveNumber;
        public bool IsWaveSystemRunning => _isWaveSystemRunning;
        public float MaxAllowedHeight => _maxAllowedHeight;
        public float MaxAllowedRadius => _maxAllowedRadius;

        public event Action<int, int> OnWaveStarted;      // (waveNumber, totalEnemies)
        public event Action<int> OnWaveCompleted;          // (waveNumber)
        public event Action<int, int> OnEnemiesRemaining;  // (aliveCount, waveTarget)

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            EnsureDefaultPrefabReferences();
            SetupEnemiesContainer();

            // Configurar capas por defecto si no han sido modificadas
            if (_groundLayer.value == ~0 || _groundLayer.value == 0)
            {
                int playerLayer = LayerMask.NameToLayer("Player");
                int enemyLayer = LayerMask.NameToLayer("Enemy");
                LayerMask excludeMask = 0;
                if (playerLayer != -1) excludeMask |= (1 << playerLayer);
                if (enemyLayer != -1) excludeMask |= (1 << enemyLayer);
                _groundLayer = ~excludeMask;
            }
        }

        private void OnEnable()
        {
            GameEvents.OnEnemyKilled += HandleEnemyKilledEvent;
            GameEvents.OnFloorReady += HandleFloorReadyEvent;
        }

        private void OnDisable()
        {
            GameEvents.OnEnemyKilled -= HandleEnemyKilledEvent;
            GameEvents.OnFloorReady -= HandleFloorReadyEvent;
            StopWaveSystem();
        }

        private void Start()
        {
            ResolvePlayerReference();

            if (_enableWaveSystem && _autoStartWavesOnStart)
            {
                StartWaveSystem();
            }
        }

        private void Update()
        {
            // Limpieza periódica de referencias destruidas
            PruneDeadEnemies();
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE OLEADAS PROGRESIVAS (WAVE SYSTEM)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Inicia el bucle continuo de oleadas.
        /// </summary>
        public void StartWaveSystem()
        {
            if (_isWaveSystemRunning) return;
            _isWaveSystemRunning = true;
            _waveCycleCoroutine = StartCoroutine(WaveSystemRoutine());
        }

        /// <summary>
        /// Detiene el ciclo de oleadas en curso.
        /// </summary>
        public void StopWaveSystem()
        {
            _isWaveSystemRunning = false;
            if (_waveCycleCoroutine != null)
            {
                StopCoroutine(_waveCycleCoroutine);
                _waveCycleCoroutine = null;
            }
        }

        /// <summary>
        /// Rutina principal que gestiona la secuencia de oleadas y su progresión de dificultad.
        /// </summary>
        private IEnumerator WaveSystemRoutine()
        {
            // Pequeño retardo inicial para permitir que la escena y el jugador se asienten
            yield return new WaitForSeconds(1.5f);

            while (_isWaveSystemRunning)
            {
                ResolvePlayerReference();

                // 1. Calcular cantidad de enemigos para esta oleada
                _totalEnemiesTargetForCurrentWave = CalculateEnemiesForWave(_currentWaveNumber, _currentFloor);
                _enemiesSpawnedInCurrentWave = 0;

                Debug.Log($"<color=#FF9900>[EnemySpawner] ⚔ Iniciando Oleada {_currentWaveNumber} " +
                          $"(Piso {_currentFloor}) — Meta: {_totalEnemiesTargetForCurrentWave} enemigos.</color>");

                OnWaveStarted?.Invoke(_currentWaveNumber, _totalEnemiesTargetForCurrentWave);

                // 2. Spawn progresivo de la oleada con retraso entre cada enemigo
                while (_enemiesSpawnedInCurrentWave < _totalEnemiesTargetForCurrentWave && _isWaveSystemRunning)
                {
                    // Respetar el límite de concurrencia para evitar caídas de frames
                    if (GetLivingEnemiesCount() < _maxConcurrentEnemies)
                    {
                        GameObject spawnedEnemy = SpawnSingleFloatingEnemyOnGround();
                        if (spawnedEnemy != null)
                        {
                            _enemiesSpawnedInCurrentWave++;
                            OnEnemiesRemaining?.Invoke(GetLivingEnemiesCount(), _totalEnemiesTargetForCurrentWave);
                        }
                    }

                    yield return new WaitForSeconds(_delayBetweenSpawns);
                }

                // 3. Esperar a que todos los enemigos de la oleada sean derrotados
                while (GetLivingEnemiesCount() > 0 && _isWaveSystemRunning)
                {
                    yield return new WaitForSeconds(0.4f);
                }

                if (!_isWaveSystemRunning) yield break;

                // 4. Oleada completada con éxito
                Debug.Log($"<color=#55FF55>[EnemySpawner] ✓ Oleada {_currentWaveNumber} Aniquilada. " +
                          $"Próxima oleada en {_timeBetweenWaves}s...</color>");

                OnWaveCompleted?.Invoke(_currentWaveNumber);

                // Tiempo de espera entre oleadas
                yield return new WaitForSeconds(_timeBetweenWaves);

                // 5. Progresar a la siguiente oleada
                _currentWaveNumber++;
            }
        }

        /// <summary>
        /// Calcula la cantidad de enemigos escalada por oleada y piso de la torre.
        /// </summary>
        private int CalculateEnemiesForWave(int wave, int floor)
        {
            int baseWaveCount = _baseEnemiesPerWave + ((wave - 1) * _enemiesIncrementPerWave);
            float floorMultiplier = 1.0f + ((floor - 1) * _difficultyScalePerFloor);
            return Mathf.Max(1, Mathf.RoundToInt(baseWaveCount * floorMultiplier));
        }

        // ════════════════════════════════════════════════════════════════════
        // APARICIÓN GARANTIZADA A NIVEL DEL SUELO (RAYCAST)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Instancia un EnemigoFlotante en el suelo detectado mediante Raycast vertical.
        /// </summary>
        public GameObject SpawnSingleFloatingEnemyOnGround()
        {
            Vector3 groundSpawnPosition;

            // 1. Intentar obtener posición válida desde los puntos manuales o proceduralmente
            if (!TryGetValidGroundSpawnPosition(out groundSpawnPosition))
            {
                Debug.LogWarning("[EnemySpawner] No se pudo encontrar una superficie de suelo válida para el spawn.");
                return null;
            }

            // 2. Resolver el prefab objetivo (preferencia por EnemigoFlotante)
            GameObject prefabToSpawn = _floatingEnemyPrefab != null ? _floatingEnemyPrefab : _enemyPrefabDefault;
            if (prefabToSpawn == null)
            {
                Debug.LogError("[EnemySpawner] No hay prefab de enemigo asignado ni disponible.");
                return null;
            }

            // 3. Instanciar al enemigo exactamente en la superficie transitable
            Transform parent = _enemiesContainer != null ? _enemiesContainer : transform;
            GameObject enemyInstance = Instantiate(prefabToSpawn, groundSpawnPosition, Quaternion.identity, parent);
            enemyInstance.name = $"EnemigoFlotante_W{_currentWaveNumber}_{_enemiesSpawnedInCurrentWave + 1}";

            // Asegurar capa y tag
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1) enemyInstance.layer = enemyLayer;
            enemyInstance.tag = "Enemy";

            // 4. Registrar en lista de enemigos activos
            _activeEnemies.Add(enemyInstance);

            return enemyInstance;
        }

        /// <summary>
        /// Obtiene una posición de suelo transitable, priorizando Transform[] spawnPoints y recurriendo a generación procedural.
        /// </summary>
        private bool TryGetValidGroundSpawnPosition(out Vector3 groundPosition)
        {
            groundPosition = Vector3.zero;

            // 1. Si hay puntos manuales asignados, seleccionar uno y proyectar Raycast al piso
            if (_spawnPoints != null && _spawnPoints.Length > 0)
            {
                int startIndex = UnityEngine.Random.Range(0, _spawnPoints.Length);
                for (int i = 0; i < _spawnPoints.Length; i++)
                {
                    int index = (startIndex + i) % _spawnPoints.Length;
                    Transform pt = _spawnPoints[index];
                    if (pt != null && TryRaycastToGround(pt.position, out groundPosition))
                    {
                        return true;
                    }
                }
            }

            // 2. Si no hay puntos manuales o fallaron, generar proceduralmente alrededor del jugador
            return TryGetProceduralGroundSpawnAroundPlayer(out groundPosition);
        }

        /// <summary>
        /// Proyecta un Raycast vertical seguro para obtener la coordenada Y real del suelo transitable,
        /// analizando la geometría de la habitación para garantizar que el enemigo nazca estrictamente a ras de suelo
        /// y nunca en el techo o en azoteas.
        /// </summary>
        public bool TryRaycastToGround(Vector3 sourcePoint, out Vector3 groundPosition)
        {
            groundPosition = sourcePoint;

            // Excluir capas de Player y Enemy para no detectar sus colisionadores como suelo
            int playerLayer = LayerMask.NameToLayer("Player");
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int excludeMask = 0;
            if (playerLayer != -1) excludeMask |= (1 << playerLayer);
            if (enemyLayer != -1) excludeMask |= (1 << enemyLayer);
            LayerMask effectiveGroundMask = _groundLayer.value & ~excludeMask;

            Transform player = GetPlayerTransform();
            float refFloorY = player != null ? player.position.y : sourcePoint.y;

            // ────────────────────────────────────────────────────────────────
            // PASO 1: Raycast Primario desde el Interior de la Habitación
            // ────────────────────────────────────────────────────────────────
            // Disparar desde una altura controlada (refFloorY + 1.2m), garantizando
            // que el rayo nace DEBAJO del techo y dentro del volumen jugable.
            Vector3 insideRoomOrigin = new Vector3(sourcePoint.x, refFloorY + 1.2f, sourcePoint.z);

            if (Physics.Raycast(insideRoomOrigin, Vector3.down, out RaycastHit directHit, 6.0f, effectiveGroundMask, QueryTriggerInteraction.Ignore))
            {
                if (IsValidWalkableFloor(directHit, effectiveGroundMask))
                {
                    groundPosition = directHit.point + Vector3.up * _groundSpawnOffset;
                    return true;
                }
            }

            // ────────────────────────────────────────────────────────────────
            // PASO 2: Raycast Multi-Impacto desde Altura Superior con Filtro de Altura
            // ────────────────────────────────────────────────────────────────
            // Si el punto de spawn inicial se encuentra elevado o no encontró suelo inmediatamente debajo,
            // proyectar hacia abajo capturando todas las superficies para seleccionar la transitable interior real.
            float highOriginY = Mathf.Max(sourcePoint.y, refFloorY) + _raycastOriginHeight;
            Vector3 highOrigin = new Vector3(sourcePoint.x, highOriginY, sourcePoint.z);

            RaycastHit[] allHits = Physics.RaycastAll(highOrigin, Vector3.down, _raycastMaxDistance, effectiveGroundMask, QueryTriggerInteraction.Ignore);

            if (allHits != null && allHits.Length > 0)
            {
                RaycastHit bestCandidate = default;
                float bestDistanceToRef = float.MaxValue;
                bool foundValidFloor = false;

                foreach (var hit in allHits)
                {
                    if (IsValidWalkableFloor(hit, effectiveGroundMask))
                    {
                        // Priorizar la superficie transitable cuya elevación esté más próxima al nivel de juego actual
                        float diffY = Mathf.Abs(hit.point.y - refFloorY);
                        if (diffY < bestDistanceToRef)
                        {
                            bestDistanceToRef = diffY;
                            bestCandidate = hit;
                            foundValidFloor = true;
                        }
                    }
                }

                if (foundValidFloor)
                {
                    groundPosition = bestCandidate.point + Vector3.up * _groundSpawnOffset;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Comprueba que la superficie impactada sea un suelo transitable (pendiente adecuada)
        /// y que tenga suficiente altura libre (clearance vertical) para no estar bajo un techo bajo.
        /// </summary>
        private bool IsValidWalkableFloor(RaycastHit hit, LayerMask groundMask)
        {
            // 1. Pendiente transitable (la normal apunta hacia arriba, descartando paredes verticales)
            if (hit.normal.y < _minWalkableSlope) return false;

            // 2. Comprobar espacio libre vertical (Clearance):
            // Proyectar un rayo hacia arriba desde el punto de impacto.
            // Si choca con un obstáculo o techo a menos de 1.6m, NO es un punto transitable válido.
            Vector3 clearanceCheckStart = hit.point + Vector3.up * 0.1f;
            if (Physics.Raycast(clearanceCheckStart, Vector3.up, out RaycastHit ceilHit, 1.65f, groundMask | _obstacleLayer, QueryTriggerInteraction.Ignore))
            {
                return false; // Techo demasiado bajo o atascado en geometría
            }

            // 3. Comprobar que no esté dentro de un colisionador de obstáculos
            if (_obstacleLayer.value != 0)
            {
                Vector3 sphereCenter = hit.point + Vector3.up * 0.8f;
                if (Physics.CheckSphere(sphereCenter, 0.38f, _obstacleLayer, QueryTriggerInteraction.Ignore))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Genera un punto procedural alrededor del jugador a una distancia segura fuera de su visión directa.
        /// </summary>
        private bool TryGetProceduralGroundSpawnAroundPlayer(out Vector3 groundPosition)
        {
            groundPosition = Vector3.zero;
            Transform player = GetPlayerTransform();
            if (player == null) return false;

            Vector3 playerPos = player.position;

            for (int attempt = 0; attempt < _maxProceduralAttempts; attempt++)
            {
                // Ángulo aleatorio y distancia comprendida en el radio seguro
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float distance = UnityEngine.Random.Range(_minSafeDistance, _maxSpawnRadius);

                Vector3 candidatePoint = playerPos + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                if (TryRaycastToGround(candidatePoint, out Vector3 detectedGround))
                {
                    // Verificar que no esté demasiado cerca en distancia tridimensional
                    if (Vector3.Distance(detectedGround, playerPos) >= _minSafeDistance)
                    {
                        groundPosition = detectedGround;
                        return true;
                    }
                }
            }

            // Respaldo de emergencia: intentar detrás del jugador a distancia segura
            Vector3 fallbackPoint = playerPos - player.forward * _minSafeDistance;
            if (TryRaycastToGround(fallbackPoint, out Vector3 fallbackGround))
            {
                groundPosition = fallbackGround;
                return true;
            }

            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // CONTROL DE ENEMIGOS VIVOS EN TIEMPO REAL
        // ════════════════════════════════════════════════════════════════════

        private int GetLivingEnemiesCount()
        {
            PruneDeadEnemies();
            return _activeEnemies.Count;
        }

        private void PruneDeadEnemies()
        {
            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                if (_activeEnemies[i] == null)
                {
                    _activeEnemies.RemoveAt(i);
                }
            }
        }

        private void HandleEnemyKilledEvent(Vector3 deathPos)
        {
            PruneDeadEnemies();
            OnEnemiesRemaining?.Invoke(_activeEnemies.Count, _totalEnemiesTargetForCurrentWave);
        }

        private void HandleFloorReadyEvent(FloorConfig config)
        {
            if (config != null)
            {
                _currentFloor = config.FloorNumber;
                Debug.Log($"[EnemySpawner] Configurado para Piso {_currentFloor}.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // COMPATIBILIDAD CON GENERACIÓN PROCEDURAL DE PISOS (API EXISTENTE)
        // ════════════════════════════════════════════════════════════════════

        public void SpawnEnemies(FloorLayout layout, FloorConfig config)
        {
            SpawnEnemiesAtElevation(layout, config, 0f, transform);
        }

        public void SpawnEnemiesAtElevation(FloorLayout layout, FloorConfig config, float elevation, Transform parentContainer)
        {
            if (layout == null || config == null)
            {
                Debug.LogWarning("[EnemySpawner] Cannot spawn enemies: layout or config is null.");
                return;
            }

            _currentFloor = config.FloorNumber;
            var gm = GameManager.Instance;
            var aiDirector = FindAnyObjectByType<AIDirector>();
            var degradationSystem = FindAnyObjectByType<MentalDegradationSystem>();

            EnemyComposition composition = (aiDirector != null && gm != null)
                ? aiDirector.GetEnemyComposition(config, gm.EnemyBuffs)
                : new EnemyComposition();

            List<Vector3> spawnWorldPositions = CollectFloorSpawnPositions(layout, config.BaseEnemyCount);
            int spawnCount = spawnWorldPositions.Count;
            Transform targetParent = parentContainer != null ? parentContainer : (_enemiesContainer != null ? _enemiesContainer : transform);

            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 rawPos = spawnWorldPositions[i] + Vector3.up * elevation;

                // Anclar al suelo mediante Raycast vertical
                Vector3 finalSpawnPos = rawPos;
                if (TryRaycastToGround(rawPos, out Vector3 groundPos))
                {
                    finalSpawnPos = groundPos;
                }

                if (degradationSystem != null && degradationSystem.ShouldSpawnPhantomEnemies())
                {
                    if (UnityEngine.Random.value < degradationSystem.PhantomEnemyChance())
                    {
                        SpawnPhantomEnemy(finalSpawnPos, targetParent);
                        continue;
                    }
                }

                // Si disponemos del EnemigoFlotante, generarlo prioritariamente
                if (_floatingEnemyPrefab != null)
                {
                    GameObject enemy = Instantiate(_floatingEnemyPrefab, finalSpawnPos, Quaternion.identity, targetParent);
                    enemy.name = $"EnemigoFlotante_F{_currentFloor}_{i + 1}";
                    _activeEnemies.Add(enemy);
                }
                else
                {
                    EnemyType selectedType = composition.SelectType(UnityEngine.Random.value);
                    EnemyData selectedData = GetEnemyDataForType(selectedType);
                    SpawnRealEnemy(finalSpawnPos, selectedType, selectedData, gm != null ? gm.EnemyBuffs : null, targetParent);
                }
            }

            Debug.Log($"[EnemySpawner] Spawned {spawnCount} entities on Floor {config.FloorNumber} with Ground-Raycast at elevation Y={elevation:F0}.");
        }

        private List<Vector3> CollectFloorSpawnPositions(FloorLayout layout, int desiredCount)
        {
            var result = new List<Vector3>();
            float cellSize = LevelGenerator.StandardCellSize;

            if (layout.EnemySpawnPoints != null)
            {
                foreach (var pt in layout.EnemySpawnPoints)
                {
                    if (pt.x > 0 && pt.x < layout.Width && pt.y > 0 && pt.y < layout.Height)
                    {
                        CellType cell = layout.Grid[pt.x, pt.y];
                        if (cell == CellType.Floor || cell == CellType.Corridor || cell == CellType.SpawnRoom)
                        {
                            result.Add(new Vector3(pt.x * cellSize, 0f, pt.y * cellSize));
                            if (result.Count >= desiredCount) return result;
                        }
                    }
                }
            }

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

                for (int i = 0; i < candidateTiles.Count; i++)
                {
                    int swapIdx = UnityEngine.Random.Range(i, candidateTiles.Count);
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

            if (result.Count == 0)
            {
                result.Add(new Vector3(layout.SpawnPoint.x * cellSize + 6f, 0f, layout.SpawnPoint.y * cellSize + 6f));
            }

            return result;
        }

        private void SpawnRealEnemy(Vector3 worldPos, EnemyType type, EnemyData data, EnemyBuffState buffs, Transform parent)
        {
            GetArchetypeDimensions(type, out float targetHeight, out float targetRadius);

            GameObject enemyGo;
            bool isPrimitive = false;

            if (_enemyPrefabDefault != null)
            {
                enemyGo = Instantiate(_enemyPrefabDefault, worldPos, Quaternion.identity, parent);
            }
            else
            {
                isPrimitive = true;
                enemyGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                enemyGo.name = $"Enemy_{data?.EnemyID ?? type.ToString()}";
                enemyGo.transform.position = worldPos + Vector3.up * (targetHeight * 0.5f);
                enemyGo.transform.SetParent(parent, true);
            }

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1) enemyGo.layer = enemyLayer;
            enemyGo.tag = "Enemy";

            EnforceEnemyScaleAndHitbox(enemyGo, targetHeight, targetRadius, isPrimitive);

            var enemyComp = enemyGo.GetComponent<EnemyBase>();
            if (enemyComp == null) enemyComp = enemyGo.AddComponent<EnemyBase>();

            enemyComp.Initialize(data != null ? data : ScriptableObject.CreateInstance<EnemyData>(), buffs);
            _activeEnemies.Add(enemyGo);
        }

        private void SpawnPhantomEnemy(Vector3 worldPos, Transform parent)
        {
            GetArchetypeDimensions(EnemyType.Grunt, out float targetHeight, out float targetRadius);

            GameObject phantomGo;
            bool isPrimitive = false;

            if (_phantomEnemyPrefab != null)
            {
                phantomGo = Instantiate(_phantomEnemyPrefab, worldPos, Quaternion.identity, parent);
            }
            else
            {
                isPrimitive = true;
                phantomGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                phantomGo.name = "Phantom_Hallucination";
                phantomGo.transform.position = worldPos + Vector3.up * (targetHeight * 0.5f);
                phantomGo.transform.SetParent(parent, true);
            }

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1) phantomGo.layer = enemyLayer;
            phantomGo.tag = "Enemy";

            EnforceEnemyScaleAndHitbox(phantomGo, targetHeight, targetRadius, isPrimitive);

            var phantomComp = phantomGo.GetComponent<PhantomEnemy>();
            if (phantomComp == null) phantomComp = phantomGo.AddComponent<PhantomEnemy>();
            _activeEnemies.Add(phantomGo);
        }

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

        public void EnforceEnemyScaleAndHitbox(GameObject enemyGo, float targetHeight, float targetRadius, bool isPrimitive)
        {
            if (enemyGo == null) return;

            if (isPrimitive)
            {
                float scaleFactorY = targetHeight / 2.0f;
                float scaleFactorXZ = (targetRadius * 2.0f) / 1.0f;
                enemyGo.transform.localScale = new Vector3(scaleFactorXZ, scaleFactorY, scaleFactorXZ);

                var cap = enemyGo.GetComponent<CapsuleCollider>();
                if (cap != null)
                {
                    cap.height = 2.0f;
                    cap.radius = 0.5f;
                    cap.center = Vector3.zero;
                    cap.direction = 1;
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
            if (cap == null) cap = go.AddComponent<CapsuleCollider>();

            float localScaleY = Mathf.Max(0.001f, go.transform.lossyScale.y);
            float localScaleXZ = Mathf.Max(0.001f, Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.z));

            float localHeight = targetHeight / localScaleY;
            float localRadius = targetRadius / localScaleXZ;

            cap.height = localHeight;
            cap.radius = localRadius;
            cap.center = new Vector3(0f, localHeight * 0.5f, 0f);
            cap.direction = 1;
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

        // ════════════════════════════════════════════════════════════════════
        // HELPERS INTERNOS Y GIZMOS
        // ════════════════════════════════════════════════════════════════════

        private Transform GetPlayerTransform()
        {
            if (_playerTransform == null)
            {
                ResolvePlayerReference();
            }
            return _playerTransform;
        }

        private void ResolvePlayerReference()
        {
            if (Player.PlayerManager.Instance != null)
            {
                _playerTransform = Player.PlayerManager.Instance.transform;
            }
            else
            {
                GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo != null) _playerTransform = playerGo.transform;
            }
        }

        private void SetupEnemiesContainer()
        {
            GameObject container = GameObject.Find("[SPAWNED_ENEMIES_CONTAINER]");
            if (container == null)
            {
                container = new GameObject("[SPAWNED_ENEMIES_CONTAINER]");
            }
            _enemiesContainer = container.transform;
        }

        private void EnsureDefaultPrefabReferences()
        {
#if UNITY_EDITOR
            if (_floatingEnemyPrefab == null)
            {
                _floatingEnemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemigoFlotante.prefab");
            }
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureDefaultPrefabReferences();
        }

        [ContextMenu("Iniciar Siguiente Oleada")]
        public void ForceNextWave()
        {
            if (!_isWaveSystemRunning)
            {
                StartWaveSystem();
            }
            else
            {
                ClearEnemies();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Transform player = GetPlayerTransform();
            Vector3 center = player != null ? player.position : transform.position;

            // Radio mínimo seguro (Verde)
            Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(center, _minSafeDistance);

            // Radio máximo de spawn procedural (Amarillo)
            Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.25f);
            Gizmos.DrawWireSphere(center, _maxSpawnRadius);

            // Puntos de spawn manuales
            if (_spawnPoints != null)
            {
                Gizmos.color = Color.magenta;
                foreach (var pt in _spawnPoints)
                {
                    if (pt != null)
                    {
                        Gizmos.DrawWireCube(pt.position, Vector3.one * 0.8f);
                        Gizmos.DrawLine(pt.position, pt.position + Vector3.up * 1.5f);
                    }
                }
            }
        }
#endif
    }
}
