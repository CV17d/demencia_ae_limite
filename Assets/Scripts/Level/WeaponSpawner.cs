// ============================================================================
// WeaponSpawner.cs — Sistema de Generación Aleatoria de Armas en el Mapa (Unity 6)
// ============================================================================
// Gestiona la aparición procedural y aleatoria de armas en el entorno 3D:
// 1. Catálogo Activo y Ponderado:
//    - bingala (Básica / Frecuente - Peso: 35)
//    - trabeca (Escopeta pesada / Media - Peso: 25)
//    - canoduplo (Doble cañón / Potente - Peso: 20)
//    - demencia revolver (Especial / Rara - Peso: 15)
//    - EXCLUSIÓN ESTRICTA: El arma "muak" está totalmente descartada y omitida.
//
// 2. Suscripción Robusta y Persistencia entre Escenarios:
//    - Suscripción limpia a GameEvents (OnFloorReady, OnMapGenerationCompleted, OnFloorLoading)
//      y a SceneManager.sceneLoaded para garantizar ejecución en cualquier nivel o piso nuevo.
//    - Corrutina con retardo de estabilización geométrica (WaitForEndOfFrame + Physics.SyncTransforms)
//      que espera a que LevelGenerator termine de posicionar todos los módulos.
//
// 3. Validación y Reescaneo Profundo de Módulos Activos:
//    - Si LevelGenerator.SpawnedModules está vacío, ejecuta un reescaneo de jerarquía
//      buscando contenedores de nivel (CitySectorZero_Root), módulos 'SM_', o etiquetas 'Ground'.
//
// 4. Logs de Diagnóstico Temporales en Consola:
//    - Notificaciones visuales detalladas de eventos de piso, módulos encontrados,
//      elecciones ponderadas y balance final de armas instanciadas.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Pickups;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DemenciaNoLimite.Level
{
    /// <summary>
    /// Configuración individual de arma en el catálogo ponderado de generación.
    /// </summary>
    [Serializable]
    public class WeaponSpawnConfig
    {
        [Tooltip("Identificador único del arma (ej. 'bingala', 'demencia revolver', 'trabeca', 'canoduplo').")]
        public string weaponId = "bingala";

        [Tooltip("Nombre legible para el HUD diegético y avisos de interacción.")]
        public string displayName = "Bingala";

        [Tooltip("Prefab del arma 3D con WorldItemPickup (de Assets/Prefabs/Entornos/Armas_Prefab/).")]
        public GameObject weaponPrefab;

        [Tooltip("Peso relativo en la ruleta aleatoria ponderada (mayor valor = mayor frecuencia).")]
        [Range(1, 100)]
        public int spawnWeight = 25;

        [Tooltip("Habilita o deshabilita este arma para el spawn procedural.")]
        public bool isEnabled = true;

        public WeaponSpawnConfig() { }

        public WeaponSpawnConfig(string id, string name, GameObject prefab, int weight)
        {
            weaponId = id;
            displayName = name;
            weaponPrefab = prefab;
            spawnWeight = weight;
            isEnabled = true;
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Level/Weapon Spawner")]
    public class WeaponSpawner : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // SINGLETON Y PERSISTENCIA
        // ════════════════════════════════════════════════════════════════════

        public static WeaponSpawner Instance { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        // CATÁLOGO ACTIVO DE ARMAS (CON EXCLUSIÓN ESTRICTA DE "MUAK")
        // ════════════════════════════════════════════════════════════════════

        [Header("Catálogo Activo Ponderado (Sin 'Muak')")]
        [Tooltip("Lista de armas disponibles para aparición procedural con sus respectivos pesos de probabilidad.")]
        [SerializeField] private List<WeaponSpawnConfig> _weaponCatalog = new List<WeaponSpawnConfig>();

        // ════════════════════════════════════════════════════════════════════
        // PUNTOS DE APARICIÓN Y ESCANEO DE SUPERFICIES
        // ════════════════════════════════════════════════════════════════════

        [Header("Puntos de Interés / Spawn Points de Armas")]
        [Tooltip("Puntos manuales o diseñados en el mapa (mesas, estantes, armerías). Si está vacío o es insuficiente, se escanean superficies automáticamente.")]
        [SerializeField] private Transform[] _weaponSpawnPoints;

        [Tooltip("Si está activo, busca automáticamente en la jerarquía objetos con nombres como 'WeaponSpawn', 'ItemSpawn', 'Mesa', 'Desk'.")]
        [SerializeField] private bool _autoDiscoverSceneSpawnPoints = true;

        [Header("Escaneo Procedural de Superficies Planas")]
        [Tooltip("Si faltan puntos explícitos, escanea superficies planas transitables (suelos, mesas, mostradores) dentro de los módulos del mapa.")]
        [SerializeField] private bool _enableSurfaceScanning = true;

        [Tooltip("Capas consideradas superficie válida (Suelo, Decoración, Muebles).")]
        [SerializeField] private LayerMask _surfaceLayerMask = ~0;

        [Tooltip("Capas de obstáculos que impiden el spawn (Paredes, Techos, Columnas).")]
        [SerializeField] private LayerMask _obstacleMask = 0;

        [Tooltip("Inclinación mínima requerida de la normal de superficie (normal.y > 0.85 = plano/horizontal).")]
        [Range(0.7f, 1.0f)]
        [SerializeField] private float _minSurfaceFlatness = 0.85f;

        [Tooltip("Altura de seguridad superior desde donde se dispara el Raycast vertical hacia abajo.")]
        [Range(1f, 10f)]
        [SerializeField] private float _raycastOriginHeight = 2.5f;

        [Tooltip("Distancia máxima del raycast vertical hacia abajo.")]
        [Range(2f, 20f)]
        [SerializeField] private float _raycastMaxDistance = 6.0f;

        [Tooltip("Desplazamiento vertical sobre la superficie para evitar solapamientos con la mesa o suelo.")]
        [SerializeField] private float _surfaceRestingOffset = 0.08f;

        // ════════════════════════════════════════════════════════════════════
        // PARÁMETROS DE CANTIDAD Y CONTROL POR PISO
        // ════════════════════════════════════════════════════════════════════

        [Header("Cantidad de Armas por Piso")]
        [Tooltip("Mínimo número de armas a instanciar en cada piso.")]
        [Range(1, 10)]
        [SerializeField] private int _minWeaponsPerFloor = 2;

        [Tooltip("Máximo número de armas a instanciar en cada piso.")]
        [Range(1, 15)]
        [SerializeField] private int _maxWeaponsPerFloor = 4;

        [Tooltip("Probabilidad de que un punto de spawn disponible reciba efectivamente un arma (ej. 0.75 = 75%).")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float _spawnChancePerPoint = 0.80f;

        [Tooltip("Distancia mínima al jugador para no instanciar armas directamente sobre él.")]
        [SerializeField] private float _minDistanceFromPlayer = 3.5f;

        [Tooltip("Distancia mínima entre armas distintas para evitar aglomeraciones en una misma habitación.")]
        [SerializeField] private float _minDistanceBetweenWeapons = 5.0f;

        [Header("Ciclo de Vida y Limpieza")]
        [Tooltip("Si está activo, ejecuta la generación de armas automáticamente cuando el piso o mapa termina de generarse.")]
        [SerializeField] private bool _spawnOnFloorGeneration = true;

        [Tooltip("Limpia y destruye las armas no recogidas del piso anterior al cambiar de piso.")]
        [SerializeField] private bool _clearPreviousWeaponsOnFloorChange = true;

        [Tooltip("Contenedor padre opcional para organizar las armas generadas en la jerarquía.")]
        [SerializeField] private Transform _weaponsContainer;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO DE EJECUCIÓN
        // ════════════════════════════════════════════════════════════════════

        private readonly List<GameObject> _spawnedWeapons = new List<GameObject>();
        private readonly List<Vector3> _activeSpawnLocations = new List<Vector3>();
        private int _currentFloor = 1;
        private Coroutine _delayedSpawnCoroutine;

        public IReadOnlyList<GameObject> SpawnedWeapons => _spawnedWeapons;
        public int SpawnedWeaponsCount => _spawnedWeapons.Count;

        // ════════════════════════════════════════════════════════════════════
        // CONSTANTES Y RUTAS
        // ════════════════════════════════════════════════════════════════════

        public const string WEAPONS_FOLDER_PATH = "Assets/Prefabs/Entornos/Armas_Prefab";
        private const string MUAK_KEYWORD = "muak";

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            // Garantizar persistencia entre escenas si es objeto raíz
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }

            ValidarYLimpiarCatalogo();
            AsegurarContenedorArmas();
        }

        private void OnEnable()
        {
            // Suscripción robusta a los eventos de arquitectura y progresión
            GameEvents.OnFloorLoading += HandleFloorLoadingEvent;
            GameEvents.OnFloorReady += HandleFloorReadyEvent;
            GameEvents.OnMapGenerationCompleted += HandleMapGenerationCompletedEvent;
            SceneManager.sceneLoaded += HandleSceneLoadedEvent;
        }

        private void OnDisable()
        {
            // Desuscripción limpia para evitar referencias rotas o fugas de memoria
            GameEvents.OnFloorLoading -= HandleFloorLoadingEvent;
            GameEvents.OnFloorReady -= HandleFloorReadyEvent;
            GameEvents.OnMapGenerationCompleted -= HandleMapGenerationCompletedEvent;
            SceneManager.sceneLoaded -= HandleSceneLoadedEvent;

            if (_delayedSpawnCoroutine != null)
            {
                StopCoroutine(_delayedSpawnCoroutine);
                _delayedSpawnCoroutine = null;
            }
        }

        private void Start()
        {
            // Si la partida ya está activa y aún no hay armas generadas en el mapa actual
            if (_spawnedWeapons.Count == 0 && _spawnOnFloorGeneration)
            {
                SolicitarSpawnArmas("Start() inicial");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnValidate()
        {
            ValidarYLimpiarCatalogo();
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE EVENTOS DE PISO, MAPA Y ESCENA
        // ════════════════════════════════════════════════════════════════════

        private void HandleSceneLoadedEvent(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"<color=#38BDF8><b>[WeaponSpawner]</b> 🎬 Nueva escena cargada: '<b>{scene.name}</b>' (Modo: {mode}). Preparando escaneo de armas...</color>");

            // Re-vincular contenedor de armas de la escena activa
            AsegurarContenedorArmas();

            if (_spawnOnFloorGeneration)
            {
                SolicitarSpawnArmas($"SceneLoaded ('{scene.name}')");
            }
        }

        private void HandleFloorLoadingEvent(FloorConfig config)
        {
            if (config != null)
            {
                _currentFloor = config.FloorNumber;
            }

            Debug.Log($"<color=#94A3B8><b>[WeaponSpawner]</b> 🚪 Transición de piso en proceso: Preparando Piso {_currentFloor}...</color>");

            if (_clearPreviousWeaponsOnFloorChange)
            {
                LimpiarArmasInstanciadas();
            }
        }

        private void HandleFloorReadyEvent(FloorConfig config)
        {
            if (config != null)
            {
                _currentFloor = config.FloorNumber;
            }

            Debug.Log($"<color=#38BDF8><b>[WeaponSpawner]</b> 📡 Evento GameEvents.OnFloorReady recibido para Piso {_currentFloor}.</color>");

            if (_spawnOnFloorGeneration)
            {
                SolicitarSpawnArmas($"OnFloorReady (Piso {_currentFloor})");
            }
        }

        private void HandleMapGenerationCompletedEvent()
        {
            Debug.Log($"<color=#38BDF8><b>[WeaponSpawner]</b> 📡 Evento GameEvents.OnMapGenerationCompleted recibido.</color>");

            if (_spawnOnFloorGeneration)
            {
                SolicitarSpawnArmas("OnMapGenerationCompleted");
            }
        }

        /// <summary>
        /// Solicita la ejecución del spawn con un retardo seguro que permite que la geometría
        /// y los colisionadores de la nueva escena se asienten completamente.
        /// </summary>
        public void SolicitarSpawnArmas(string motivo)
        {
            if (!isActiveAndEnabled) return;

            if (_delayedSpawnCoroutine != null)
            {
                StopCoroutine(_delayedSpawnCoroutine);
                _delayedSpawnCoroutine = null;
            }

            _delayedSpawnCoroutine = StartCoroutine(RutinaSpawnConRetardo(motivo));
        }

        private IEnumerator RutinaSpawnConRetardo(string motivo)
        {
            Debug.Log($"<color=#38BDF8><b>[WeaponSpawner]</b> ⏳ Iniciando secuencia de spawn retardada por: <b>{motivo}</b> (Piso: {_currentFloor}). Esperando estabilización geométrica...</color>");

            // 1. Esperar al final del frame para que todos los GameObjects de la nueva escena/módulos se hayan instanciado
            yield return new WaitForEndOfFrame();

            // 2. Esperar 1 frame adicional para asegurar que Start(), Awake() y transforms se hayan propagado
            yield return null;

            // 3. Forzar sincronización global del motor de físicas para que los raycasts detecten todos los colliders recién creados
            Physics.SyncTransforms();

            // 4. Si LevelGenerator sigue ocupado generando el piso, esperar activamente a que termine
            var levelGen = FindAnyObjectByType<LevelGenerator>();
            if (levelGen != null && levelGen.IsGenerating)
            {
                Debug.Log("[WeaponSpawner] ⏳ LevelGenerator en proceso de generación. Esperando finalización de construcción...");
                float timeout = 5.0f;
                while (levelGen != null && levelGen.IsGenerating && timeout > 0f)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
                Physics.SyncTransforms();
            }

            // 5. Pequeño margen de seguridad para físicas
            yield return new WaitForSeconds(0.25f);
            Physics.SyncTransforms();

            // 6. Ejecutar la generación en el nuevo escenario
            SpawnWeaponsForCurrentFloor();

            _delayedSpawnCoroutine = null;
        }

        // ════════════════════════════════════════════════════════════════════
        // COMPATIBILIDAD CON GENERACIÓN PROCEDURAL DE PISOS (API FLOORLAYOUT)
        // ════════════════════════════════════════════════════════════════════

        public void SpawnWeapons(FloorLayout layout, FloorConfig config)
        {
            SpawnWeaponsAtElevation(layout, config, 0f, transform);
        }

        public void SpawnWeaponsAtElevation(FloorLayout layout, FloorConfig config, float elevation, Transform parentContainer)
        {
            if (layout == null || config == null) return;
            _currentFloor = config.FloorNumber;

            Transform targetParent = parentContainer != null ? parentContainer : (_weaponsContainer != null ? _weaponsContainer : transform);

            if (layout.ItemSpawnPoints != null && layout.ItemSpawnPoints.Count > 0)
            {
                float cellSize = 3.0f;
                int count = Mathf.Min(layout.ItemSpawnPoints.Count, UnityEngine.Random.Range(_minWeaponsPerFloor, _maxWeaponsPerFloor + 1));
                for (int i = 0; i < count; i++)
                {
                    Vector2Int pt = layout.ItemSpawnPoints[i];
                    Vector3 rawPos = new Vector3(pt.x * cellSize, elevation, pt.y * cellSize);
                    if (AjustarASuperficiePlana(rawPos, out Vector3 surfacePos))
                    {
                        WeaponSpawnConfig selectedWeapon = SeleccionarArmaAleatoriaPonderada();
                        if (selectedWeapon != null)
                        {
                            InstanciarArma(selectedWeapon, surfacePos, targetParent);
                        }
                    }
                }
            }
            else
            {
                SpawnWeaponsForCurrentFloor();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // NÚCLEO DE GENERACIÓN ALEATORIA DE ARMAS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recorre los puntos de spawn disponibles o escanea superficies planas en las habitaciones
        /// e instancia proceduralmente armas del catálogo activo aplicando selección ponderada.
        /// </summary>
        [ContextMenu("Generar Armas en Mapa")]
        public void SpawnWeaponsForCurrentFloor()
        {
            ValidarYLimpiarCatalogo();

            if (_weaponCatalog.Count == 0)
            {
#if UNITY_EDITOR
                AutoPoblarCatalogoDesdeProyecto();
#endif
                if (_weaponCatalog.Count == 0)
                {
                    Debug.LogWarning("[WeaponSpawner] No hay armas configuradas en el catálogo activo ni prefabs disponibles.");
                    return;
                }
            }

            AsegurarContenedorArmas();
            _activeSpawnLocations.Clear();

            // 1. Recolectar puntos de interés (Transforms explícitos o detectados en escena)
            List<Vector3> candidatePoints = RecolectarPuntosDeSpawn();

            // 2. Determinar la cantidad objetivo de armas para este piso
            int targetWeaponCount = UnityEngine.Random.Range(_minWeaponsPerFloor, _maxWeaponsPerFloor + 1);

            // 3. Obtener la posición del jugador para validar distancias seguras
            Vector3 playerPos = ObtenerPosicionJugador();

            Debug.Log($"<color=#FBBF24><b>[WeaponSpawner]</b> 🔍 Puntos de interés explícitos encontrados: <b>{candidatePoints.Count}</b> | Jugador en: {playerPos:F1} | Objetivo: <b>{targetWeaponCount}</b> armas.</color>");

            int armasInstanciadas = 0;

            // Barajar aleatoriamente los puntos candidatos
            BarajarLista(candidatePoints);

            // Intentar instanciar en los puntos candidatos
            foreach (Vector3 rawPos in candidatePoints)
            {
                if (armasInstanciadas >= targetWeaponCount) break;

                // Evaluación de probabilidad por punto
                if (UnityEngine.Random.value > _spawnChancePerPoint) continue;

                // Validar distancia al jugador
                if (Vector3.Distance(rawPos, playerPos) < _minDistanceFromPlayer) continue;

                // Validar distancia a otras armas ya colocadas
                if (EstaDemasiadoCercaDeOtrasArmas(rawPos)) continue;

                // Ajustar al suelo o superficie plana mediante Raycast vertical
                if (AjustarASuperficiePlana(rawPos, out Vector3 surfacePos))
                {
                    WeaponSpawnConfig selectedWeapon = SeleccionarArmaAleatoriaPonderada();
                    if (selectedWeapon != null && selectedWeapon.weaponPrefab != null)
                    {
                        GameObject weapon = InstanciarArma(selectedWeapon, surfacePos, targetParent: _weaponsContainer);
                        if (weapon != null)
                        {
                            _activeSpawnLocations.Add(surfacePos);
                            armasInstanciadas++;
                        }
                    }
                }
            }

            // 4. Si aún no alcanzamos la cantidad mínima y el escaneo de superficies está activo, escanear módulos
            if (armasInstanciadas < _minWeaponsPerFloor && _enableSurfaceScanning)
            {
                armasInstanciadas += EscanearSuperficiesEnModulos(targetWeaponCount - armasInstanciadas, playerPos);
            }

            Debug.Log($"<color=#55FF55><b>[WeaponSpawner]</b> ✅ ¡Generación de armas completada con éxito!</color> " +
                      $"Armas activas generadas en Piso {_currentFloor}: <b>{armasInstanciadas}</b> (Objetivo establecido: {targetWeaponCount}).");
        }

        // ════════════════════════════════════════════════════════════════════
        // SELECCIÓN ALEATORIA PONDERADA (WEIGHTED RANDOM SELECTION)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selecciona un arma del catálogo activo en base a su peso relativo.
        /// Excluye y descarta de manera estricta cualquier arma 'muak'.
        /// </summary>
        public WeaponSpawnConfig SeleccionarArmaAleatoriaPonderada()
        {
            var validos = new List<WeaponSpawnConfig>();
            int sumaPesos = 0;

            foreach (var item in _weaponCatalog)
            {
                if (item == null || !item.isEnabled || item.weaponPrefab == null) continue;
                if (EsArmaProhibida(item.weaponId, item.displayName, item.weaponPrefab)) continue;

                validos.Add(item);
                sumaPesos += Mathf.Max(1, item.spawnWeight);
            }

            if (validos.Count == 0) return null;

            int tirada = UnityEngine.Random.Range(0, sumaPesos);
            int acumulado = 0;

            foreach (var item in validos)
            {
                acumulado += Mathf.Max(1, item.spawnWeight);
                if (tirada < acumulado)
                {
                    return item;
                }
            }

            return validos[validos.Count - 1];
        }

        // ════════════════════════════════════════════════════════════════════
        // INSTANCIACIÓN E INTERACTIVIDAD (WORLD ITEM PICKUP)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Instancia el prefab del arma, garantiza la presencia de WorldItemPickup,
        /// configura los colliders de interacción por tecla [E] y lo ancla al contenedor.
        /// </summary>
        public GameObject InstanciarArma(WeaponSpawnConfig config, Vector3 posicion, Transform targetParent = null)
        {
            if (config == null || config.weaponPrefab == null) return null;

            // Filtro de seguridad estricto contra 'muak'
            if (EsArmaProhibida(config.weaponId, config.displayName, config.weaponPrefab))
            {
                Debug.LogWarning($"[WeaponSpawner] Generación cancelada: El arma '{config.weaponId}' está obsoleta y excluida.");
                return null;
            }

            Transform parent = targetParent != null ? targetParent : (_weaponsContainer != null ? _weaponsContainer : transform);

            // Orientación natural aleatoria sobre el plano horizontal para variedad estética
            Quaternion rotacionAleatoria = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            GameObject weaponInstance = Instantiate(config.weaponPrefab, posicion, rotacionAleatoria, parent);
            weaponInstance.name = $"Pickup_{config.displayName}_{_spawnedWeapons.Count + 1}";

            // 1. Garantizar componente WorldItemPickup para interacción diegética y recogida
            WorldItemPickup pickup = weaponInstance.GetComponent<WorldItemPickup>();
            if (pickup == null)
            {
                pickup = weaponInstance.AddComponent<WorldItemPickup>();
            }

            // 2. Configurar la identidad, datos y prefab del arma para el inventario
            pickup.ConfigurarComoArma(config.weaponId, config.displayName, config.weaponPrefab);

            // 3. Garantizar Colisionador Trigger de proximidad para interacción
            Collider[] colliders = weaponInstance.GetComponentsInChildren<Collider>();
            bool tieneTrigger = false;
            foreach (var col in colliders)
            {
                if (col != null && col.isTrigger)
                {
                    tieneTrigger = true;
                    break;
                }
            }

            if (!tieneTrigger)
            {
                SphereCollider trigger = weaponInstance.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 1.6f;
                trigger.center = Vector3.zero;
            }

            // 4. Garantizar Rigidbody cinemático si no lo posee para estabilidad física
            Rigidbody rb = weaponInstance.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = weaponInstance.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            _spawnedWeapons.Add(weaponInstance);

            Debug.Log($"<color=#A7F3D0><b>[WeaponSpawner]</b> 🎲 Instanciada arma: '<b>{config.displayName}</b>' (ID: '{config.weaponId}', Peso: {config.spawnWeight}) en posición {posicion:F2}.</color>");

            return weaponInstance;
        }

        // ════════════════════════════════════════════════════════════════════
        // RECOLECCIÓN Y ESCANEO DE PUNTOS DE INTERÉS
        // ════════════════════════════════════════════════════════════════════

        private List<Vector3> RecolectarPuntosDeSpawn()
        {
            var puntos = new List<Vector3>();

            // 1. Puntos serializados explícitamente en el Inspector (descartando nulos por cambios de escena)
            if (_weaponSpawnPoints != null)
            {
                foreach (var t in _weaponSpawnPoints)
                {
                    if (t != null && t.gameObject != null)
                    {
                        puntos.Add(t.position);
                    }
                }
            }

            // 2. Detección automática en la escena por nombres clave
            if (_autoDiscoverSceneSpawnPoints)
            {
                var todosLosObjetos = FindObjectsByType<Transform>(FindObjectsSortMode.None);
                foreach (var t in todosLosObjetos)
                {
                    if (t == null) continue;
                    string lowerName = t.name.ToLowerInvariant();

                    bool esPuntoDeInteres = lowerName.Contains("weaponspawn") ||
                                            lowerName.Contains("weapon_spawn") ||
                                            lowerName.Contains("itemspawn") ||
                                            lowerName.Contains("item_spawn") ||
                                            lowerName.Contains("spawn_arma") ||
                                            lowerName.Contains("mesaarmeria") ||
                                            lowerName.Contains("mostrador") ||
                                            (lowerName.Contains("mesa") && !lowerName.Contains("silla")) ||
                                            lowerName.Contains("desk");

                    if (esPuntoDeInteres && !puntos.Contains(t.position))
                    {
                        puntos.Add(t.position);
                    }
                }
            }

            return puntos;
        }

        /// <summary>
        /// Escanea proceduralmente habitaciones o módulos arquitectónicos del nivel para ubicar armas
        /// sobre superficies planas transitables o mesas mediante Raycasts verticales precisos.
        /// </summary>
        private int EscanearSuperficiesEnModulos(int cantidadFaltante, Vector3 playerPos)
        {
            int instanciadas = 0;

            // Obtener lista validada de módulos o superficies activas con diagnóstico
            List<GameObject> modulos = ObtenerModulosActivos(out string origenModulos);

            Debug.Log($"<color=#FBBF24><b>[WeaponSpawner]</b> 🏢 Módulos activos para escaneo de superficies: <b>{modulos.Count}</b> (Origen: {origenModulos}).</color>");

            if (modulos.Count == 0)
            {
                Debug.LogWarning("[WeaponSpawner] ⚠️ No se detectaron módulos ni superficies en la jerarquía. Ejecutando muestreo radial de emergencia...");
                return MuestrearSuperficiesEnRadio(playerPos != Vector3.zero ? playerPos : Vector3.zero, 25f, cantidadFaltante, playerPos);
            }

            BarajarLista(modulos);

            foreach (var modulo in modulos)
            {
                if (instanciadas >= cantidadFaltante) break;
                if (modulo == null) continue;

                // Paso A: Buscar dentro del módulo puntos prop o mesas primero
                Transform[] hijos = modulo.GetComponentsInChildren<Transform>();
                bool colocadoEnHijo = false;

                foreach (var hijo in hijos)
                {
                    if (hijo == null || hijo == modulo.transform) continue;
                    string hName = hijo.name.ToLowerInvariant();

                    if (hName.Contains("desk") || hName.Contains("mesa") || hName.Contains("estante") || hName.Contains("prop_floor"))
                    {
                        if (AjustarASuperficiePlana(hijo.position, out Vector3 surfacePos))
                        {
                            if (Vector3.Distance(surfacePos, playerPos) >= _minDistanceFromPlayer &&
                                !EstaDemasiadoCercaDeOtrasArmas(surfacePos))
                            {
                                WeaponSpawnConfig config = SeleccionarArmaAleatoriaPonderada();
                                if (config != null)
                                {
                                    InstanciarArma(config, surfacePos, modulo.transform);
                                    _activeSpawnLocations.Add(surfacePos);
                                    instanciadas++;
                                    colocadoEnHijo = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (colocadoEnHijo) continue;

                // Paso B: Si no hay mesa en el módulo, escanear el centro y zonas transitables del piso del módulo
                Vector3 moduloPos = modulo.transform.position;
                for (int intento = 0; intento < 6; intento++)
                {
                    Vector3 testPos = moduloPos + new Vector3(
                        UnityEngine.Random.Range(-3.5f, 3.5f),
                        0f,
                        UnityEngine.Random.Range(-3.5f, 3.5f)
                    );

                    if (Vector3.Distance(testPos, playerPos) < _minDistanceFromPlayer) continue;
                    if (EstaDemasiadoCercaDeOtrasArmas(testPos)) continue;

                    if (AjustarASuperficiePlana(testPos, out Vector3 surfacePos))
                    {
                        WeaponSpawnConfig config = SeleccionarArmaAleatoriaPonderada();
                        if (config != null)
                        {
                            InstanciarArma(config, surfacePos, modulo.transform);
                            _activeSpawnLocations.Add(surfacePos);
                            instanciadas++;
                            break;
                        }
                    }
                }
            }

            // Si tras recorrer los módulos aún faltan armas para alcanzar el mínimo, usar muestreo radial alrededor del jugador
            if (instanciadas < cantidadFaltante)
            {
                instanciadas += MuestrearSuperficiesEnRadio(playerPos, 22f, cantidadFaltante - instanciadas, playerPos);
            }

            return instanciadas;
        }

        /// <summary>
        /// Obtiene y valida los módulos arquitectónicos activos en la escena.
        /// Si LevelGenerator.SpawnedModules está vacío, realiza un reescaneo exhaustivo de la jerarquía.
        /// </summary>
        private List<GameObject> ObtenerModulosActivos(out string origenDiagnostico)
        {
            var modulosValidos = new List<GameObject>();
            var levelGen = FindAnyObjectByType<LevelGenerator>();

            // 1. Prioridad: LevelGenerator.SpawnedModules
            if (levelGen != null && levelGen.SpawnedModules != null && levelGen.SpawnedModules.Count > 0)
            {
                foreach (var m in levelGen.SpawnedModules)
                {
                    if (m != null && m.activeInHierarchy)
                    {
                        modulosValidos.Add(m);
                    }
                }

                if (modulosValidos.Count > 0)
                {
                    origenDiagnostico = $"LevelGenerator.SpawnedModules ({modulosValidos.Count} módulos vivos)";
                    return modulosValidos;
                }
            }

            Debug.LogWarning("[WeaponSpawner] ⚠️ LevelGenerator.SpawnedModules está vacío o no inicializado. Iniciando reescaneo profundo de jerarquía en el escenario actual...");

            // 2. Reescaneo de contenedor raíz de ciudad / nivel
            string[] posiblesRaices = new[] { "CitySectorZero_Root", "City_Root", "=== LEVEL ===", "LevelRoot", "Dungeon_Root" };
            foreach (var nombreRaiz in posiblesRaices)
            {
                var raizGo = GameObject.Find(nombreRaiz);
                if (raizGo != null)
                {
                    for (int i = 0; i < raizGo.transform.childCount; i++)
                    {
                        var hijo = raizGo.transform.GetChild(i).gameObject;
                        if (hijo != null && hijo.activeInHierarchy)
                        {
                            modulosValidos.Add(hijo);
                        }
                    }

                    if (modulosValidos.Count > 0)
                    {
                        origenDiagnostico = $"Contenedor Raíz '{nombreRaiz}' ({modulosValidos.Count} módulos hijos)";
                        return modulosValidos;
                    }
                }
            }

            // 3. Reescaneo por prefabs modulares 'SM_' en la escena
            var todosLosTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in todosLosTransforms)
            {
                if (t == null) continue;
                string n = t.name;
                if (n.StartsWith("SM_") && t.parent != null && (t.parent.name.Contains("Root") || t.parent.name.Contains("City") || t.parent.parent == null))
                {
                    if (!modulosValidos.Contains(t.gameObject))
                    {
                        modulosValidos.Add(t.gameObject);
                    }
                }
            }

            if (modulosValidos.Count > 0)
            {
                origenDiagnostico = $"Búsqueda Modular de Escena 'SM_' ({modulosValidos.Count} módulos)";
                return modulosValidos;
            }

            // 4. Búsqueda por Tag "Ground" (con verificación segura para evitar excepciones en Unity)
            var objetosGround = BuscarPorTagSeguro("Ground");
            if (objetosGround.Length > 0)
            {
                foreach (var g in objetosGround)
                {
                    if (g != null && g.activeInHierarchy && !modulosValidos.Contains(g))
                    {
                        modulosValidos.Add(g);
                    }
                }

                if (modulosValidos.Count > 0)
                {
                    origenDiagnostico = $"Objetos con Tag 'Ground' ({modulosValidos.Count} superficies)";
                    return modulosValidos;
                }
            }

            // 5. Búsqueda de objetos con componentes de Suelo / Floor en el nombre
            foreach (var t in todosLosTransforms)
            {
                if (t == null) continue;
                string lower = t.name.ToLowerInvariant();
                if (lower.Contains("floor") || lower.Contains("suelo") || lower.Contains("piso_") || lower.Contains("walkable"))
                {
                    var col = t.GetComponent<Collider>();
                    if (col != null && !col.isTrigger && !modulosValidos.Contains(t.gameObject))
                    {
                        modulosValidos.Add(t.gameObject);
                    }
                }
            }

            if (modulosValidos.Count > 0)
            {
                origenDiagnostico = $"Superficies de Suelo encontradas por nombre ({modulosValidos.Count} objetos)";
                return modulosValidos;
            }

            origenDiagnostico = "Ningún módulo detectado (Fallback radial activado)";
            return modulosValidos;
        }

        private static GameObject[] BuscarPorTagSeguro(string tag)
        {
            try
            {
                return GameObject.FindGameObjectsWithTag(tag);
            }
            catch (Exception)
            {
                return Array.Empty<GameObject>();
            }
        }

        private int MuestrearSuperficiesEnRadio(Vector3 centro, float radio, int cantidad, Vector3 playerPos)
        {
            int generadas = 0;
            float baseElevation = (playerPos != Vector3.zero) ? playerPos.y : centro.y;

            for (int i = 0; i < 30; i++)
            {
                if (generadas >= cantidad) break;

                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * radio;
                if (randomCircle.magnitude < _minDistanceFromPlayer)
                {
                    randomCircle = randomCircle.normalized * (_minDistanceFromPlayer + 1.5f);
                }

                Vector3 samplePos = new Vector3(centro.x + randomCircle.x, baseElevation, centro.z + randomCircle.y);

                if (Vector3.Distance(samplePos, playerPos) < _minDistanceFromPlayer) continue;
                if (EstaDemasiadoCercaDeOtrasArmas(samplePos)) continue;

                if (AjustarASuperficiePlana(samplePos, out Vector3 surfacePos))
                {
                    WeaponSpawnConfig config = SeleccionarArmaAleatoriaPonderada();
                    if (config != null)
                    {
                        GameObject weapon = InstanciarArma(config, surfacePos, _weaponsContainer);
                        if (weapon != null)
                        {
                            _activeSpawnLocations.Add(surfacePos);
                            generadas++;
                        }
                    }
                }
            }
            return generadas;
        }

        // ════════════════════════════════════════════════════════════════════
        // DETECCIÓN Y ANCLAJE PRECISO A SUPERFICIES PLANAS (RAYCAST)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Proyecta un raycast vertical hacia abajo desde una altura segura para detectar
        /// con precisión la superficie plana del suelo, mesa o estante.
        /// </summary>
        public bool AjustarASuperficiePlana(Vector3 puntoOrigen, out Vector3 superficieFinal)
        {
            superficieFinal = puntoOrigen;

            Vector3 rayStart = puntoOrigen + Vector3.up * _raycastOriginHeight;
            float maxDist = _raycastOriginHeight + _raycastMaxDistance;

            // Excluir capas de Jugador, Enemigos y Armas del raycast de superficie
            LayerMask maskEfectiva = _surfaceLayerMask;
            int playerLayer = LayerMask.NameToLayer("Player");
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int weaponLayer = LayerMask.NameToLayer("Weapon");

            if (playerLayer != -1) maskEfectiva &= ~(1 << playerLayer);
            if (enemyLayer != -1) maskEfectiva &= ~(1 << enemyLayer);
            if (weaponLayer != -1) maskEfectiva &= ~(1 << weaponLayer);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxDist, maskEfectiva, QueryTriggerInteraction.Ignore))
            {
                // 1. Validar que la superficie sea horizontal/plana (evitar paredes verticales)
                if (hit.normal.y < _minSurfaceFlatness)
                {
                    return false;
                }

                // 2. Validar que exista espacio libre vertical (despeje de techo/obstáculo)
                if (_obstacleMask != 0)
                {
                    if (Physics.CheckSphere(hit.point + Vector3.up * 0.4f, 0.25f, _obstacleMask, QueryTriggerInteraction.Ignore))
                    {
                        return false;
                    }
                }

                // 3. Aplicar reposo seguro ligeramente elevado sobre la superficie
                superficieFinal = hit.point + Vector3.up * _surfaceRestingOffset;
                return true;
            }

            return false;
        }

        private bool EstaDemasiadoCercaDeOtrasArmas(Vector3 candidatePos)
        {
            foreach (var loc in _activeSpawnLocations)
            {
                if (Vector3.Distance(candidatePos, loc) < _minDistanceBetweenWeapons)
                {
                    return true;
                }
            }
            return false;
        }

        private Vector3 ObtenerPosicionJugador()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) return player.transform.position;
            return Vector3.zero;
        }

        // ════════════════════════════════════════════════════════════════════
        // FILTRO Y EXCLUSIÓN ESTRICTA DE "MUAK"
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Comprueba si los identificadores o nombres coinciden con el arma excluida 'muak'.
        /// </summary>
        public static bool EsArmaProhibida(string id, string displayName, GameObject prefab)
        {
            if (!string.IsNullOrEmpty(id) && id.IndexOf(MUAK_KEYWORD, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(displayName) && displayName.IndexOf(MUAK_KEYWORD, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (prefab != null && prefab.name.IndexOf(MUAK_KEYWORD, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private void ValidarYLimpiarCatalogo()
        {
            if (_weaponCatalog == null)
            {
                _weaponCatalog = new List<WeaponSpawnConfig>();
                return;
            }

            // Eliminar cualquier entrada nula o que contenga la palabra 'muak'
            for (int i = _weaponCatalog.Count - 1; i >= 0; i--)
            {
                var entry = _weaponCatalog[i];
                if (entry == null)
                {
                    _weaponCatalog.RemoveAt(i);
                    continue;
                }

                if (EsArmaProhibida(entry.weaponId, entry.displayName, entry.weaponPrefab))
                {
                    Debug.LogWarning($"[WeaponSpawner] Omitida y descartada arma obsoleta '{entry.weaponId}' del catálogo activo.");
                    _weaponCatalog.RemoveAt(i);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // POBLACIÓN AUTOMÁTICA EN EDITOR
        // ════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
        /// <summary>
        /// Escanea automáticamente la carpeta Assets/Prefabs/Entornos/Armas_Prefab/ e integra
        /// las 4 armas activas (bingala, revolver, trabeca, canoduplo), excluyendo totalmente 'muak'.
        /// </summary>
        [ContextMenu("Auto-Poblar Catálogo desde Prefabs")]
        public void AutoPoblarCatalogoDesdeProyecto()
        {
            if (_weaponCatalog == null) _weaponCatalog = new List<WeaponSpawnConfig>();

            string[] defaultWeapons = new[]
            {
                "bingala",
                "demencia revolver",
                "Trabeca",
                "Canoduplo"
            };

            var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "bingala", 35 },
                { "demencia revolver", 15 },
                { "Trabeca", 25 },
                { "Canoduplo", 20 }
            };

            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "bingala", "Bingala" },
                { "demencia revolver", "Demencia Revólver" },
                { "Trabeca", "Trabeca" },
                { "Canoduplo", "Canoduplo" }
            };

            foreach (var wName in defaultWeapons)
            {
                if (wName.IndexOf(MUAK_KEYWORD, StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string path = $"{WEAPONS_FOLDER_PATH}/{wName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                // Verificar si ya existe en el catálogo
                bool existe = false;
                foreach (var c in _weaponCatalog)
                {
                    if (c != null && (c.weaponPrefab == prefab || string.Equals(c.weaponId, wName, StringComparison.OrdinalIgnoreCase)))
                    {
                        existe = true;
                        break;
                    }
                }

                if (!existe)
                {
                    int w = weights.ContainsKey(wName) ? weights[wName] : 20;
                    string dName = names.ContainsKey(wName) ? names[wName] : wName;
                    _weaponCatalog.Add(new WeaponSpawnConfig(wName.ToLowerInvariant(), dName, prefab, w));
                    Debug.Log($"[WeaponSpawner] Agregada arma al catálogo activo: '{dName}' (Peso: {w})");
                }
            }

            EditorUtility.SetDirty(this);
        }
#endif

        // ════════════════════════════════════════════════════════════════════
        // LIMPIEZA Y UTILIDADES
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Destruye las instancias de armas no recogidas en el mundo.
        /// </summary>
        [ContextMenu("Limpiar Armas Instanciadas")]
        public void LimpiarArmasInstanciadas()
        {
            int destruidas = 0;
            for (int i = _spawnedWeapons.Count - 1; i >= 0; i--)
            {
                var w = _spawnedWeapons[i];
                if (w != null)
                {
                    destruidas++;
                    if (Application.isPlaying)
                    {
                        Destroy(w);
                    }
                    else
                    {
                        DestroyImmediate(w);
                    }
                }
            }
            _spawnedWeapons.Clear();
            _activeSpawnLocations.Clear();

            if (destruidas > 0)
            {
                Debug.Log($"<color=#94A3B8>[WeaponSpawner] 🧹 Limpieza de armas: {destruidas} arma(s) residuales eliminadas del mundo.</color>");
            }
        }

        private void AsegurarContenedorArmas()
        {
            if (_weaponsContainer == null || _weaponsContainer.gameObject == null)
            {
                GameObject containerGo = GameObject.Find("=== WEAPONS_CONTAINER ===");
                if (containerGo == null)
                {
                    containerGo = new GameObject("=== WEAPONS_CONTAINER ===");
                }
                _weaponsContainer = containerGo.transform;
            }
        }

        private static void BarajarLista<T>(IList<T> lista)
        {
            if (lista == null || lista.Count <= 1) return;
            for (int i = lista.Count - 1; i > 0; i--)
            {
                int r = UnityEngine.Random.Range(0, i + 1);
                T temp = lista[i];
                lista[i] = lista[r];
                lista[r] = temp;
            }
        }
    }
}
