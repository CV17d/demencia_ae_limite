// ============================================================================
// LevelGenerator.cs — Motor Procedural de la Ciudad (Sector Cero / WFC Modular)
// ============================================================================
// Ensambla la arquitectura modular de la ciudad mediante acoplamiento por sockets.
// - Conciencia Espacial (Overlap Prevention): Pre-valida el volumen proyectado antes
//   de confirmar la instanciación de piezas arquitectónicas (salas, arenas, pasillos).
// - Lógica de rechazo y fallback: Si una pieza choca, prueba otra; si ninguna cabe,
//   sella el enchufe con un deadEnd (muro ciego) evitando huecos al vacío.
// - Compatible con especificaciones de Grid de 4x4 unidades y piezas de gran tamaño
//   (Arenas de 16x16 o 24x28).
// - Notifica a la IA mediante GameEvents.FireMapGenerationCompleted() para iniciar
//   el horneado asíncrono con ProceduralNavMeshBuilder.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Level
{
    [DisallowMultipleComponent]
    public class LevelGenerator : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // ENUMERACIONES Y ESTRUCTURAS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Estrategia de detección de colisiones espaciales.
        /// </summary>
        public enum SpatialValidationMethod
        {
            [Tooltip("Validación matemática pura por AABB (Bounds.Intersects). Determinista, sin asignación de memoria y sin sobrecarga de PhysX.")]
            BoundsIntersects,

            [Tooltip("Validación física en escena mediante Physics.OverlapBox. Requiere colliders y sincronización con PhysX.")]
            PhysicsOverlapBox
        }

        /// <summary>
        /// Representa un enchufe libre en el mapa pendiente de recibir una conexión.
        /// </summary>
        private struct OpenSocketEntry
        {
            public Transform SocketTransform;
            public ModuleConnector ParentConnector;

            public OpenSocketEntry(Transform socket, ModuleConnector connector)
            {
                SocketTransform = socket;
                ParentConnector = connector;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CONSTANTES ARQUITECTÓNICAS (ESPECIFICACIÓN GRID 4x4)
        // ════════════════════════════════════════════════════════════════════

        public const float StandardCellSize = 4.0f;
        public const float StandardWallHeight = 3.2f;
        public const float StandardCeilingHeight = 3.2f;

        /// <summary>
        /// Contenedor de compatibilidad con jefes y sistemas analíticos de piso.
        /// </summary>
        public FloorLayout LastGeneratedLayout { get; private set; } = new FloorLayout();

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE CONCIENCIA ESPACIAL (OVERLAP PREVENTION)
        // ════════════════════════════════════════════════════════════════════

        [Header("Conciencia Espacial (Overlap Prevention)")]
        [Tooltip("Método utilizado para comprobar si el volumen de un nuevo módulo colisiona con módulos ya existentes.")]
        [SerializeField] private SpatialValidationMethod _validationMethod = SpatialValidationMethod.BoundsIntersects;

        [Tooltip("Margen de tolerancia (skin width) hacia adentro para permitir que muros contiguos en las puertas no generen falsos positivos de colisión.")]
        [SerializeField] private float _boundsTolerance = 0.15f;

        [Tooltip("Cantidad máxima de piezas aleatorias alternativas a intentar en un socket antes de forzar el sellado con deadEnd.")]
        [SerializeField] private int _maxRetriesPerSocket = 10;

        [Tooltip("Máscara de capas físicas para Physics.OverlapBox (en caso de usar dicho método).")]
        [SerializeField] private LayerMask _physicsLayerMask = ~0;

        // ════════════════════════════════════════════════════════════════════
        // PROGRESIÓN DE PISOS (DESCENSO EN ELEVADOR / DESTROY & REBUILD)
        // ════════════════════════════════════════════════════════════════════

        [Header("Progresión de Pisos (Descenso en Elevador)")]
        [Tooltip("Piso actual en el que se encuentra el jugador dentro del descenso.")]
        [SerializeField] private int pisoActual = 1;

        [Tooltip("Límite máximo de pisos antes de completar el descenso total.")]
        [SerializeField] private int pisoMaximo = 30;

        [Tooltip("Incremento opcional de módulos máximos (_maxModules) por cada piso descendido para hacer niveles más extensos.")]
        [SerializeField] private int modulosExtraPorPiso = 2;

        [Header("Semilla Pseudoaleatoria")]
        [Tooltip("Texto utilizado para inicializar el generador determinista. Misma semilla = misma ciudad.")]
        [SerializeField] private string _levelSeed = "SECTOR_ZERO_ALPHA";

        [Tooltip("Si está activo, genera una semilla aleatoria única en cada partida.")]
        [SerializeField] private bool _randomizeSeedOnStart = false;

        [Header("Límites de Ensamblaje")]
        [Tooltip("Cantidad máxima de módulos estructurales a instanciar antes de cerrar fronteras.")]
        [SerializeField] private int _maxModules = 30;

        [Tooltip("Ejecuta la generación automáticamente en el ciclo Start().")]
        [SerializeField] private bool _generateOnStart = true;

        [Header("Pesos de Probabilidad de Módulos")]
        [Range(0f, 1f)]
        [SerializeField] private float _intersectionChance = 0.35f;

        [Range(0f, 1f)]
        [SerializeField] private float _safeRoomChance = 0.15f;

        // ════════════════════════════════════════════════════════════════════
        // CATÁLOGO DE MÓDULOS (PREFABS CON MODULECONNECTOR)
        // ════════════════════════════════════════════════════════════════════

        [Header("Catálogo de Piezas Modulares")]
        [Tooltip("Módulos iniciales de aparición (Spawn Room).")]
        public List<GameObject> startRoom = new List<GameObject>();

        [Tooltip("Pasillos lineales y conexiones directas.")]
        public List<GameObject> corridors = new List<GameObject>();

        [Tooltip("Cruces en T, bifurcaciones y esquinas de 90 grados.")]
        public List<GameObject> intersections = new List<GameObject>();

        [Header("Tapón de Cierre (Blender / Muro Ciego)")]
        [Tooltip("Prefab del muro ciego modelado en Blender para sellar sockets que queden abiertos al finalizar la generación.")]
        [SerializeField] private GameObject muroCiegoPrefab;

        [Header("Meta del Nivel (Elevadores de Salida)")]
        [Tooltip("Lista de prefabs de elevadores de salida para elegir aleatoriamente el que conduce al siguiente piso.")]
        [SerializeField] private List<GameObject> elevadorSalidaPrefabs = new List<GameObject>();

        [Tooltip("Paredes ciegas y cierres para sellar enchufes abiertos al alcanzar maxModules o por falta de espacio.")]
        public List<GameObject> deadEnds = new List<GameObject>();

        [Tooltip("Salas seguras con puntos de respiro o mercaderes.")]
        public List<GameObject> safeRooms = new List<GameObject>();

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private System.Random _rng;
        private Transform _cityRoot;
        private readonly List<OpenSocketEntry> _activeOpenSockets = new List<OpenSocketEntry>();
        private List<Transform> _rejectedSockets = new List<Transform>();
        private readonly List<Bounds> _placedModuleBounds = new List<Bounds>();
        private readonly List<GameObject> _spawnedModulesList = new List<GameObject>();
        private GameObject _instantiatedExitElevator;
        private int _spawnedModulesCount;
        private bool _isGenerating;

        // Estado temporal para congelar y proteger al jugador durante la reconstrucción del nivel
        private CharacterController _frozenCC;
        private Rigidbody _frozenRB;
        private bool _wasKinematic;
        private bool _wasUseGravity;
        private GameObject _cachedPlayerGo;

        public bool IsGenerating => _isGenerating;
        public string ActiveSeed => _levelSeed;
        public SpatialValidationMethod ActiveValidationMethod => _validationMethod;
        public int PlacedBoundsCount => _placedModuleBounds.Count;
        public int PisoActual => pisoActual;
        public int PisoMaximo => pisoMaximo;
        public int MaxModules => _maxModules;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            ObtenerOInicializarCityRoot();
#if UNITY_EDITOR
            if (startRoom == null || startRoom.Count == 0 || elevadorSalidaPrefabs == null || elevadorSalidaPrefabs.Count == 0)
            {
                AutoCargarPrefabsEditor();
            }
#endif
            AsegurarCatalogoElevadoresSalida();
        }

        private void Start()
        {
            if (_generateOnStart)
            {
                // Si hay un GameBootstrapper activo en la escena, él coordinará el inicio de la run.
                if (FindAnyObjectByType<GameBootstrapper>() == null)
                {
                    GenerateCitySector();
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GENERACIÓN PROCEDURAL CON CONCIENCIA ESPACIAL
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Inicia la generación determinista de la ciudad con prevención de solapamientos (Overlap Prevention).
        /// </summary>
        [ContextMenu("Generar Ciudad Procedural")]
        public void GenerateCitySector()
        {
            if (_isGenerating) return;

            // Congelar y resguardar al jugador antes de purgar el nivel
            CongelarJugadorParaTransicion();

            LimpiarNivelActual();
            InicializarSemilla();
            ConstruirPiso();

            // Reposicionar y reactivar al jugador con físicas 100% sincronizadas
            ReposicionarYReactivarJugador();
        }

        /// <summary>
        /// Transición al siguiente piso mediante el patrón Destroy & Rebuild.
        /// Diseñado para ser invocado por eventos de UI (botón de elevador/avance).
        /// </summary>
        [ContextMenu("Generar Siguiente Piso")]
        public void GenerarSiguientePiso()
        {
            if (_isGenerating)
            {
                Debug.LogWarning("[LevelGenerator] Ya hay una generación en progreso. Ignorando solicitud.");
                return;
            }

            if (pisoActual >= pisoMaximo)
            {
                Debug.Log($"[LevelGenerator] Se ha alcanzado el piso máximo ({pisoMaximo}). No es posible descender más.");
                return;
            }

            // 1. Congelar física, controles y resguardar provisionalmente al jugador antes de destruir el piso anterior
            CongelarJugadorParaTransicion();

            // 2. Incrementar pisoActual
            pisoActual++;

            // Sincronizar con el GameManager si está presente
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TransitionToFloor(pisoActual);
            }

            // 3. Limpieza completa del nivel anterior
            LimpiarNivelActual();

            // 4. Forzar cambio de semilla única para el nuevo piso
            _levelSeed = $"SECTOR_{pisoActual:D2}_{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";
            int seedHash = _levelSeed.GetHashCode();
            _rng = new System.Random(seedHash);

            // 5. (Opcional) Aumentar ligeramente MaxModules para que los pisos más profundos sean más grandes
            _maxModules += modulosExtraPorPiso;

            Debug.Log($"[LevelGenerator] Descendiendo al Piso {pisoActual}/{pisoMaximo} (Nueva Semilla: '{_levelSeed}', MaxMódulos: {_maxModules})...");

            // 6. Invocar la construcción del nuevo piso (ejecuta el bucle principal y dispara GameEvents.FireMapGenerationCompleted)
            ConstruirPiso();

            // 7. Reposicionar y reactivar de forma 100% segura al jugador tras finalizar y sincronizar físicas
            ReposicionarYReactivarJugador();
        }

        /// <summary>
        /// Núcleo de construcción procedural: Instancia la sala inicial, ejecuta el bucle de expansión
        /// por sockets con detección de colisiones, sella los conectores abiertos y notifica la finalización.
        /// </summary>
        private void ConstruirPiso()
        {
            _isGenerating = true;

            Debug.Log($"[LevelGenerator] Iniciando generación de Sector Cero (Piso {pisoActual}) con semilla: '{_levelSeed}' (Validación: {_validationMethod}, Grid: {StandardCellSize}x{StandardCellSize})...");

            // 1. INSTANCIACIÓN DE LA SALA INICIAL (START ROOM) EN EL ORIGEN
#if UNITY_EDITOR
            if (startRoom == null || startRoom.Count == 0 || elevadorSalidaPrefabs == null || elevadorSalidaPrefabs.Count == 0)
            {
                AutoCargarPrefabsEditor();
            }
#endif
            AsegurarCatalogoElevadoresSalida();
            _spawnedModulesList.Clear();
            _instantiatedExitElevator = null;
            GameObject startModule = InstanciarStartRoom();
            if (startModule == null)
            {
                Debug.LogError("[LevelGenerator] Error crítico: No se configuraron prefabs en 'startRoom'.");
                _isGenerating = false;
                return;
            }

            _spawnedModulesList.Add(startModule);

            // Registrar el volumen de la sala de inicio
            ModuleConnector startConnector = startModule.GetComponent<ModuleConnector>();
            if (startConnector != null)
            {
                Bounds startBounds = startConnector.CalculateWorldBounds(startModule.transform.position, startModule.transform.rotation);
                _placedModuleBounds.Add(startBounds);
            }
            // Sincronización inmediata con PhysX para asegurar que los colisionadores de la sala inicial estén activos
            Physics.SyncTransforms();

            // Registrar los enchufes de la sala inicial
            RegistrarSocketsDeModulo(startModule);
            _spawnedModulesCount = 1;

            // 2. BUCLE DE EXPANSIÓN POR ENCHUFES CON LOOK-AHEAD Y PREVENCIÓN DE SOLAPAMIENTO
            while (_spawnedModulesCount < _maxModules && _activeOpenSockets.Count > 0)
            {
                // Seleccionar un socket abierto disponible de la frontera
                int socketIndex = _rng.Next(0, _activeOpenSockets.Count);
                OpenSocketEntry originEntry = _activeOpenSockets[socketIndex];

                if (originEntry.SocketTransform == null)
                {
                    _activeOpenSockets.RemoveAt(socketIndex);
                    continue;
                }

                bool modulePlaced = false;

                // Intentar hasta _maxRetriesPerSocket veces con piezas aleatorias de las listas principales
                for (int attempt = 0; attempt < _maxRetriesPerSocket; attempt++)
                {
                    GameObject candidatePrefab = SeleccionarPrefabParaFrontera();
                    if (candidatePrefab == null) continue;

                    // Pre-validar espacio (Look-Ahead): Simula rotación/posición y comprueba Bounds o Physics
                    if (IntentarAcoplarModulo(candidatePrefab, originEntry.SocketTransform, out GameObject newModuleInstance, out Bounds placedBounds))
                    {
                        _spawnedModulesCount++;
                        _placedModuleBounds.Add(placedBounds);
                        _spawnedModulesList.Add(newModuleInstance);
                        RegistrarSocketsDeModulo(newModuleInstance);
                        modulePlaced = true;
                        break; // ¡Pieza aceptada sin solapamiento!
                    }
                }

                // 3. RECHAZO POR FALTA DE ESPACIO (COLISIÓN ESPACIAL):
                // Si ninguna pieza de las listas principales cupo tras agotar los reintentos,
                // guardamos el socket en _rejectedSockets para la consolidación final de la salida.
                if (!modulePlaced)
                {
                    _rejectedSockets.Add(originEntry.SocketTransform);
                }

                // Descartar definitivamente el socket procesado de la lista activa
                _activeOpenSockets.RemoveAt(socketIndex);
            }

            // 4. CIERRE DE FRONTERAS (SELLADO FINAL DE CONECTORES ABIERTOS)
            SellarConectoresAbiertos();

            // 5. MECANISMO DE RESPALDO INFALIBLE (FALLBACK DE EMERGENCIA)
            // Si por cualquier circunstancia no se instanció el elevador de salida, forzar obligatoriamente su colocación
            if (!HayElevadorDeSalidaEnNivel())
            {
                EjecutarFallbackEmergenciaElevadorSalida();
            }

            // Sincronización global obligatoria de colisionadores antes de hornear NavMesh o posicionar al jugador
            Physics.SyncTransforms();

            // 6. VALIDACIÓN EN CONSOLA (ALERTA VISUAL INMEDIATA EN EDITOR)
            if (!HayElevadorDeSalidaEnNivel())
            {
                Debug.LogError($"[LevelGenerator] ERROR CRÍTICO FATAL: El Piso {pisoActual} ha finalizado su generación SIN elevador de salida (SM_Elevador_Mercader / SM_Elevador_Ruleta). ¡Imposible avanzar al siguiente piso!");
            }
            else
            {
                Debug.Log($"[LevelGenerator] Elevador de salida verificado y garantizado en Piso {pisoActual}: '{_instantiatedExitElevator?.name}'. Panel de control: HABILITADO.");
            }

            _isGenerating = false;
            Debug.Log($"[LevelGenerator] Piso {pisoActual} generado con éxito ({_spawnedModulesCount} módulos estructurales, {_placedModuleBounds.Count} volúmenes registrados).");

            // 7. INTEGRACIÓN CRÍTICA CON IA: Notificación al Event Bus para horneado de NavMesh
            GameEvents.FireMapGenerationCompleted();
        }

        /// <summary>
        /// Congela temporalmente la física, el movimiento y desactiva el CharacterController / Rigidbody
        /// del jugador, moviéndolo provisionalmente a un punto neutro seguro para evitar que caiga al vacío
        /// mientras se destruye la geometría del nivel anterior y se construye el nuevo piso.
        /// </summary>
        private void CongelarJugadorParaTransicion()
        {
            _cachedPlayerGo = null;
            _frozenCC = null;
            _frozenRB = null;

            if (PlayerManager.Instance != null)
            {
                _cachedPlayerGo = PlayerManager.Instance.gameObject;
                PlayerManager.Instance.SetPlayerFrozen(true);
            }
            else
            {
                _cachedPlayerGo = GameObject.FindGameObjectWithTag("Player");
            }

            if (_cachedPlayerGo == null) return;

            // 1. Congelar y deshabilitar CharacterController para que no caiga por gravedad
            _frozenCC = _cachedPlayerGo.GetComponent<CharacterController>();
            if (_frozenCC != null)
            {
                _frozenCC.enabled = false;
            }

            // 2. Congelar y neutralizar Rigidbody si existe
            _frozenRB = _cachedPlayerGo.GetComponent<Rigidbody>();
            if (_frozenRB != null)
            {
                _wasKinematic = _frozenRB.isKinematic;
                _wasUseGravity = _frozenRB.useGravity;
#if UNITY_6000_0_OR_NEWER
                _frozenRB.linearVelocity = Vector3.zero;
#else
                _frozenRB.velocity = Vector3.zero;
#endif
                _frozenRB.angularVelocity = Vector3.zero;
                _frozenRB.isKinematic = true;
                _frozenRB.useGravity = false;
            }

            // 3. Desactivar FirstPersonController si está presente
            var fpc = _cachedPlayerGo.GetComponent<FirstPersonController>();
            if (fpc != null)
            {
                fpc.SetMovementEnabled(false);
            }

            // 4. Mover al jugador provisionalmente al origen seguro suspendido
            _cachedPlayerGo.transform.SetPositionAndRotation(new Vector3(0f, 2f, 0f), Quaternion.identity);

            Debug.Log("[LevelGenerator] Jugador congelado y resguardado provisionalmente antes de destruir _cityRoot.");
        }

        /// <summary>
        /// Reposiciona al jugador en el origen de la nueva sala inicial, sincroniza las físicas
        /// al 100% y reactiva su CharacterController/Rigidbody y controles tras finalizar la generación.
        /// </summary>
        private void ReposicionarYReactivarJugador()
        {
            Vector3 spawnPosition = new Vector3(0f, 1.5f, 0f);

            // Intentar buscar punto de spawn explícito en la nueva StartRoom si existe
            Transform startRoomObj = _cityRoot != null ? _cityRoot.Find("StartRoom_SectorZero") : null;
            if (startRoomObj != null)
            {
                Transform customSpawn = startRoomObj.Find("SpawnPoint") ?? startRoomObj.Find("PlayerSpawn");
                if (customSpawn != null)
                {
                    spawnPosition = customSpawn.position;
                }
            }

            // Asegurar que las colisiones y transformaciones del nuevo piso estén 100% actualizadas en PhysX
            Physics.SyncTransforms();

            if (_cachedPlayerGo == null)
            {
                if (PlayerManager.Instance != null)
                {
                    _cachedPlayerGo = PlayerManager.Instance.gameObject;
                }
                else
                {
                    _cachedPlayerGo = GameObject.FindGameObjectWithTag("Player");
                }
            }

            if (_cachedPlayerGo != null)
            {
                // 1. Ubicar físicamente al jugador en la posición de inicio
                _cachedPlayerGo.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);

                // Sincronizar la transformación de la cápsula en PhysX antes de reactivar colisiones
                Physics.SyncTransforms();

                // 2. Reactivar CharacterController
                if (_frozenCC != null)
                {
                    _frozenCC.enabled = true;
                }
                else
                {
                    var cc = _cachedPlayerGo.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = true;
                }

                // 3. Restaurar Rigidbody si estaba presente
                if (_frozenRB != null)
                {
                    _frozenRB.isKinematic = _wasKinematic;
                    _frozenRB.useGravity = _wasUseGravity;
#if UNITY_6000_0_OR_NEWER
                    _frozenRB.linearVelocity = Vector3.zero;
#else
                    _frozenRB.velocity = Vector3.zero;
#endif
                    _frozenRB.angularVelocity = Vector3.zero;
                }

                // 4. Reactivar FirstPersonController
                var fpc = _cachedPlayerGo.GetComponent<FirstPersonController>();
                if (fpc != null)
                {
                    fpc.SetMovementEnabled(true);
                    fpc.Teleport(spawnPosition, Quaternion.identity);
                }

                // 5. Descongelar PlayerManager
                if (PlayerManager.Instance != null)
                {
                    PlayerManager.Instance.SetPlayerFrozen(false);
                    PlayerManager.Instance.Teleport(spawnPosition, Quaternion.identity);
                }
                else if (GameManager.Instance != null)
                {
                    GameManager.Instance.SafelyTeleportPlayer(spawnPosition);
                }

                Debug.Log($"[LevelGenerator] Jugador reactivado y posicionado con éxito en {spawnPosition} con físicas sincronizadas.");
            }

            _cachedPlayerGo = null;
            _frozenCC = null;
            _frozenRB = null;
        }

        /// <summary>
        /// Método de compatibilidad hacia atrás: invoca ReposicionarYReactivarJugador().
        /// </summary>
        private void ReposicionarJugadorEnInicio()
        {
            ReposicionarYReactivarJugador();
        }

        // ════════════════════════════════════════════════════════════════════
        // PRE-VALIDACIÓN ESPACIAL Y ACOPLAMIENTO (LOOK-AHEAD)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Simula matemáticamente la posición y rotación del nuevo módulo en el socket de origen.
        /// Si no solapa con piezas existentes, confirma e instancia el módulo.
        /// </summary>
        private bool IntentarAcoplarModulo(
            GameObject prefab,
            Transform originSocket,
            out GameObject instantiatedModule,
            out Bounds finalWorldBounds)
        {
            instantiatedModule = null;
            finalWorldBounds = default;

            if (prefab == null || originSocket == null) return false;

            ModuleConnector candidateConnector = prefab.GetComponent<ModuleConnector>();

            // Si el prefab carece de sockets definidos, validar posición directa
            if (candidateConnector == null || !candidateConnector.HasAvailableSockets)
            {
                Vector3 directPos = originSocket.position;
                Quaternion directRot = originSocket.rotation;
                Bounds localB = candidateConnector != null 
                    ? candidateConnector.GetLocalBounds() 
                    : new Bounds(new Vector3(0f, 2f, 0f), new Vector3(StandardCellSize, StandardCellSize, StandardCellSize));

                if (!ComprobarEspacioLibre(directPos, directRot, localB, out Bounds simulatedBounds))
                {
                    return false; // Choca -> Rechazado
                }

                Transform root = ObtenerOInicializarCityRoot();
                instantiatedModule = Instantiate(prefab, directPos, directRot, root);
                instantiatedModule.name = $"{prefab.name}_{_spawnedModulesCount}";
                instantiatedModule.transform.SetPositionAndRotation(directPos, directRot);
                instantiatedModule.transform.localScale = Vector3.one;
                Physics.SyncTransforms();

                finalWorldBounds = simulatedBounds;
                return true;
            }

            // Probamos los sockets disponibles en el candidato en orden pseudoaleatorio
            List<Transform> candidateSockets = new List<Transform>(candidateConnector.availableSockets);
            ShuffleList(candidateSockets, _rng);

            for (int i = 0; i < candidateSockets.Count; i++)
            {
                Transform targetSocketOnPrefab = candidateSockets[i];
                if (targetSocketOnPrefab == null) continue;

                // ── SIMULACIÓN MATEMÁTICA SIN INSTANCIAR EN ESCENA ───────────
                if (SimularTransformacionModulo(originSocket, targetSocketOnPrefab, prefab.transform, out Vector3 simPos, out Quaternion simRot))
                {
                    Bounds localBounds = candidateConnector.GetLocalBounds();

                    // Pre-validación espacial (Look-Ahead)
                    if (ComprobarEspacioLibre(simPos, simRot, localBounds, out Bounds simulatedBounds))
                    {
                        // ¡Espacio confirmado! Se procede a instanciar el módulo de manera definitiva
                        Transform root = ObtenerOInicializarCityRoot();
                        instantiatedModule = Instantiate(prefab, simPos, simRot, root);
                        instantiatedModule.name = $"{prefab.name}_{_spawnedModulesCount}";
                        instantiatedModule.transform.SetPositionAndRotation(simPos, simRot);
                        instantiatedModule.transform.localScale = Vector3.one;
                        Physics.SyncTransforms();

                        finalWorldBounds = simulatedBounds;

                        // Decoración temática determinista si el módulo cuenta con ThematicPropSpawner
                        ThematicPropSpawner propSpawner = instantiatedModule.GetComponent<ThematicPropSpawner>();
                        if (propSpawner != null)
                        {
                            propSpawner.SpawnThematicProps(_levelSeed);
                        }

                        ModuleConnector newConnector = instantiatedModule.GetComponent<ModuleConnector>();
                        if (newConnector != null)
                        {
                            // Encontramos el socket equivalente en la instancia recién creada para consumirlo
                            Transform instanceTargetSocket = EncontrarSocketEquivalente(newConnector, targetSocketOnPrefab.name, i);
                            if (instanceTargetSocket != null)
                            {
                                newConnector.RemoveSocket(instanceTargetSocket);
                            }
                        }

                        return true;
                    }
                }
            }

            return false; // Ninguna rotación o socket del candidato cupo en el espacio
        }

        /// <summary>
        /// Calcula la posición y rotación que adoptaría el módulo padre para conectar targetSocket con originSocket.
        /// </summary>
        private bool SimularTransformacionModulo(
            Transform originSocket,
            Transform targetSocketOnPrefab,
            Transform prefabRoot,
            out Vector3 simulatedPosition,
            out Quaternion simulatedRotation)
        {
            // Obtener coordenadas locales del socket respecto a la raíz del prefab (incluso si está anidado)
            Vector3 socketLocalPos;
            Quaternion socketLocalRot;
            ModuleConnector.GetSocketRelativeTransform(prefabRoot, targetSocketOnPrefab, out socketLocalPos, out socketLocalRot);

            // Dos sockets deben quedar enfrentados: El forward del enchufe de destino debe ser -originSocket.forward
            Vector3 desiredForward = -originSocket.forward;
            Vector3 desiredUp = originSocket.up;

            if (Mathf.Abs(Vector3.Dot(desiredForward, desiredUp)) > 0.99f)
            {
                desiredUp = Vector3.Cross(desiredForward, Vector3.right);
                if (desiredUp.sqrMagnitude < 0.01f)
                {
                    desiredUp = Vector3.Cross(desiredForward, Vector3.forward);
                }
                desiredUp.Normalize();
            }

            Quaternion targetRotation = Quaternion.LookRotation(desiredForward, desiredUp);

            // R_parent = Q_target * Inverse(Socket_localRotation)
            simulatedRotation = targetRotation * Quaternion.Inverse(socketLocalRot);

            // Pos_parent = Pos_origin - (R_parent * Socket_localPosition)
            Vector3 socketWorldOffset = simulatedRotation * socketLocalPos;
            simulatedPosition = originSocket.position - socketWorldOffset;

            return true;
        }

        /// <summary>
        /// Comprueba si el volumen proyectado del módulo está libre de colisiones o solapamientos.
        /// </summary>
        private bool ComprobarEspacioLibre(Vector3 worldPos, Quaternion worldRot, Bounds localBounds, out Bounds simulatedWorldBounds)
        {
            simulatedWorldBounds = ModuleConnector.TransformBounds(localBounds, worldPos, worldRot);

            if (_validationMethod == SpatialValidationMethod.PhysicsOverlapBox)
            {
                return !ComprobarSolapamientoPhysics(worldPos, worldRot, localBounds);
            }
            else
            {
                return !ComprobarSolapamientoBounds(simulatedWorldBounds);
            }
        }

        /// <summary>
        /// Validación puramente geométrica mediante Bounds.Intersects aplicando tolerancia (skin width).
        /// </summary>
        private bool ComprobarSolapamientoBounds(Bounds candidateBounds)
        {
            // Contrae ligeramente el bounds candidato para tolerar juntas de puertas en paredes compartidas
            Vector3 candidateContractedSize = candidateBounds.size - new Vector3(_boundsTolerance * 2f, _boundsTolerance * 2f, _boundsTolerance * 2f);
            if (candidateContractedSize.x <= 0f || candidateContractedSize.y <= 0f || candidateContractedSize.z <= 0f)
            {
                candidateContractedSize = candidateBounds.size * 0.9f;
            }
            Bounds contractedCandidate = new Bounds(candidateBounds.center, candidateContractedSize);

            for (int i = 0; i < _placedModuleBounds.Count; i++)
            {
                Bounds placed = _placedModuleBounds[i];
                Vector3 placedContractedSize = placed.size - new Vector3(_boundsTolerance * 2f, _boundsTolerance * 2f, _boundsTolerance * 2f);
                if (placedContractedSize.x <= 0f || placedContractedSize.y <= 0f || placedContractedSize.z <= 0f)
                {
                    placedContractedSize = placed.size * 0.9f;
                }
                Bounds contractedPlaced = new Bounds(placed.center, placedContractedSize);

                if (contractedCandidate.Intersects(contractedPlaced))
                {
                    return true; // Solapamiento geométrico detectado
                }
            }

            return false; // Espacio libre de solapamiento
        }

        /// <summary>
        /// Validación física mediante Physics.OverlapBox contra los colliders ya asentados en la escena.
        /// </summary>
        private bool ComprobarSolapamientoPhysics(Vector3 worldPos, Quaternion worldRot, Bounds localBounds)
        {
            Vector3 worldCenter = worldPos + (worldRot * localBounds.center);
            Vector3 halfExtents = (localBounds.size * 0.5f) - new Vector3(_boundsTolerance, _boundsTolerance, _boundsTolerance);
            if (halfExtents.x <= 0f || halfExtents.y <= 0f || halfExtents.z <= 0f)
            {
                halfExtents = localBounds.extents * 0.9f;
            }

            Collider[] hits = Physics.OverlapBox(worldCenter, halfExtents, worldRot, _physicsLayerMask, QueryTriggerInteraction.Ignore);
            return hits != null && hits.Length > 0;
        }

        // ════════════════════════════════════════════════════════════════════
        // SELLADO DE ENCHUFES (DEAD ENDS / MUROS CIEGOS)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sella obligatoriamente un socket individual que ha sido rechazado tras agotar
        /// sus reintentos por colisión espacial, instanciando muroCiegoPrefab en su posición y rotación exactas.
        /// </summary>
        private void SellarSocketIndividual(Transform openSocket)
        {
            if (openSocket == null) return;

            // Prioridad absoluta a muroCiegoPrefab con fallback seguro a deadEnds
            GameObject prefabToUse = muroCiegoPrefab;
            if (prefabToUse == null)
            {
                if (deadEnds != null && deadEnds.Count > 0)
                {
                    prefabToUse = deadEnds[0];
                }
                else
                {
                    Debug.LogWarning("[LevelGenerator] No se ha asignado 'muroCiegoPrefab' ni elementos en 'deadEnds' para sellar el conector rechazado.");
                    return;
                }
            }

            Transform parentContainer = ObtenerOInicializarCityRoot();

            // Instanciación directa en las coordenadas y orientación exactas del socket rechazado
            GameObject muro = Instantiate(prefabToUse, openSocket.position, openSocket.rotation, parentContainer);
            muro.name = $"MuroCiego_Rechazo_{openSocket.name}";
            muro.transform.SetPositionAndRotation(openSocket.position, openSocket.rotation);
            muro.transform.localScale = Vector3.one;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Rutina de cierre final: Consolida todos los conectores pendientes (frontera activa + rechazados por falta de espacio),
        /// garantiza de forma obligatoria la instanciación de un elevador de salida al final del recorrido (máxima distancia desde el spawn)
        /// con su panel activado dinámicamente, y sella el resto de conectores con muroCiegoPrefab.
        /// </summary>
        private void SellarConectoresAbiertos()
        {
            // 1. Consolidar todos los conectores disponibles en una sola lista unificada
            List<Transform> todosLosConectores = new List<Transform>();

            if (_activeOpenSockets != null)
            {
                for (int i = 0; i < _activeOpenSockets.Count; i++)
                {
                    if (_activeOpenSockets[i].SocketTransform != null)
                    {
                        todosLosConectores.Add(_activeOpenSockets[i].SocketTransform);
                    }
                }
            }

            if (_rejectedSockets != null)
            {
                for (int i = 0; i < _rejectedSockets.Count; i++)
                {
                    if (_rejectedSockets[i] != null)
                    {
                        todosLosConectores.Add(_rejectedSockets[i]);
                    }
                }
            }

            todosLosConectores.RemoveAll(s => s == null);

            if (todosLosConectores.Count == 0)
            {
                Debug.LogWarning("[LevelGenerator] No hay conectores disponibles en frontera ni rechazados para colocar la salida. Activando fallback de emergencia...");
                _activeOpenSockets.Clear();
                _rejectedSockets.Clear();
                EjecutarFallbackEmergenciaElevadorSalida();
                return;
            }

            Transform parentContainer = ObtenerOInicializarCityRoot();

            // 2. Garantía obligatoria de catálogo de elevadores de salida (SM_Elevador_Mercader / SM_Elevador_Ruleta)
            AsegurarCatalogoElevadoresSalida();

            if (elevadorSalidaPrefabs == null || elevadorSalidaPrefabs.Count == 0)
            {
                Debug.LogError("[LevelGenerator] ERROR CRÍTICO: Imposible sellar el nivel sin elevador de salida. Operación abortada para prevenir nivel incompleto.");
                return;
            }

            // Determinar la posición de la sala inicial para ubicar la salida en el conector más alejado del recorrido
            Vector3 startPos = Vector3.zero;
            Transform startRoomObj = _cityRoot != null ? _cityRoot.Find("StartRoom_SectorZero") : null;
            if (startRoomObj != null)
            {
                startPos = startRoomObj.position;
            }

            // Identificar el conector terminal a mayor distancia euclidiana del spawn (final de la ruta)
            int exitIndex = 0;
            float maxDistSqr = -1f;

            for (int i = 0; i < todosLosConectores.Count; i++)
            {
                Transform socket = todosLosConectores[i];
                if (socket == null) continue;

                float distSqr = (socket.position - startPos).sqrMagnitude;
                if (distSqr > maxDistSqr)
                {
                    maxDistSqr = distSqr;
                    exitIndex = i;
                }
            }

            // Agrupar los conectores terminales dentro del umbral más alejado (>= 85% de distancia máxima)
            // para permitir variedad determinista guiada por la semilla
            List<int> conectoresFinalRecorrido = new List<int>();
            float umbralDistanciaSqr = maxDistSqr * 0.85f;
            for (int i = 0; i < todosLosConectores.Count; i++)
            {
                Transform socket = todosLosConectores[i];
                if (socket == null) continue;

                float distSqr = (socket.position - startPos).sqrMagnitude;
                if (distSqr >= umbralDistanciaSqr)
                {
                    conectoresFinalRecorrido.Add(i);
                }
            }

            if (conectoresFinalRecorrido.Count > 0)
            {
                int pick = _rng != null ? _rng.Next(0, conectoresFinalRecorrido.Count) : UnityEngine.Random.Range(0, conectoresFinalRecorrido.Count);
                exitIndex = conectoresFinalRecorrido[pick];
            }

            Transform exitSocket = todosLosConectores[exitIndex];

            // Selección determinista del modelo de elevador de salida (SM_Elevador_Mercader o SM_Elevador_Ruleta)
            int modelIndex = _rng != null ? _rng.Next(0, elevadorSalidaPrefabs.Count) : UnityEngine.Random.Range(0, elevadorSalidaPrefabs.Count);
            GameObject selectedElevatorPrefab = elevadorSalidaPrefabs[modelIndex];

            if (exitSocket != null && selectedElevatorPrefab != null)
            {
                Vector3 spawnPos = exitSocket.position;
                Quaternion spawnRot = exitSocket.rotation;

                // Si el elevador cuenta con un conector de entrada, calcular transformación precisa para acoplar sockets
                ModuleConnector elevatorConnector = selectedElevatorPrefab.GetComponent<ModuleConnector>();
                if (elevatorConnector != null && elevatorConnector.availableSockets != null && elevatorConnector.availableSockets.Count > 0)
                {
                    Transform entranceSocket = elevatorConnector.availableSockets[0];
                    if (entranceSocket != null && SimularTransformacionModulo(exitSocket, entranceSocket, selectedElevatorPrefab.transform, out Vector3 simPos, out Quaternion simRot))
                    {
                        spawnPos = simPos;
                        spawnRot = simRot;
                    }
                }

                GameObject exitElevator = Instantiate(selectedElevatorPrefab, spawnPos, spawnRot, parentContainer);
                exitElevator.name = $"ElevadorSalida_{exitSocket.name}";
                exitElevator.transform.SetPositionAndRotation(spawnPos, spawnRot);
                exitElevator.transform.localScale = Vector3.one;
                Physics.SyncTransforms();

                _instantiatedExitElevator = exitElevator;
                _spawnedModulesList.Add(exitElevator);

                // 3. Activación dinámica del panel de control del elevador de salida
                ElevatorPanelController panelController = exitElevator.GetComponentInChildren<ElevatorPanelController>(true);
                if (panelController != null)
                {
                    panelController.HabilitarPanel(true);
                    Debug.Log($"[LevelGenerator] Elevador de salida '{selectedElevatorPrefab.name}' instanciado exitosamente al final del recorrido ('{exitSocket.name}'). Panel de control activado dinámicamente.");
                }
                else
                {
                    Debug.LogWarning($"[LevelGenerator] El elevador de salida '{exitElevator.name}' no tiene componente ElevatorPanelController en su jerarquía.");
                }

                ThematicPropSpawner propSpawner = exitElevator.GetComponent<ThematicPropSpawner>();
                if (propSpawner != null)
                {
                    propSpawner.SpawnThematicProps(_levelSeed);
                }
            }

            // Consumir el conector seleccionado para la salida para no taparlo con un muro ciego
            todosLosConectores.RemoveAt(exitIndex);

            // 4. Sellado del resto de conectores de la lista unificada con muros ciegos
            GameObject prefabToUse = muroCiegoPrefab;
            if (prefabToUse == null)
            {
                if (deadEnds != null && deadEnds.Count > 0)
                {
                    prefabToUse = deadEnds[0];
                }
                else
                {
                    Debug.LogWarning("[LevelGenerator] No se ha asignado 'muroCiegoPrefab' ni prefabs en 'deadEnds' para sellar los conectores restantes.");
                    _activeOpenSockets.Clear();
                    _rejectedSockets.Clear();
                    return;
                }
            }

            Debug.Log($"[LevelGenerator] Sellando {todosLosConectores.Count} conectores restantes con '{prefabToUse.name}'...");

            for (int i = 0; i < todosLosConectores.Count; i++)
            {
                Transform socket = todosLosConectores[i];
                if (socket == null) continue;

                GameObject muro = Instantiate(prefabToUse, socket.position, socket.rotation, parentContainer);
                muro.name = $"MuroCiego_{socket.name}_{i}";
                muro.transform.SetPositionAndRotation(socket.position, socket.rotation);
                muro.transform.localScale = Vector3.one;
                Physics.SyncTransforms();
            }

            // 5. Limpieza final de todas las estructuras de conectores
            _activeOpenSockets.Clear();
            _rejectedSockets.Clear();
            todosLosConectores.Clear();

            // Sincronización obligatoria final de colisionadores físicos
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Garantiza de forma infalible la disponibilidad de prefabs de elevadores de salida
        /// (SM_Elevador_Mercader_12x16 / SM_Elevador_Ruleta_12x12x12). Previene que cualquier
        /// nivel procedural sea sellado sin una salida funcional.
        /// </summary>
        public void AsegurarCatalogoElevadoresSalida()
        {
            if (elevadorSalidaPrefabs == null)
            {
                elevadorSalidaPrefabs = new List<GameObject>();
            }

            // Filtrar nulos si los hubiera
            for (int i = elevadorSalidaPrefabs.Count - 1; i >= 0; i--)
            {
                if (elevadorSalidaPrefabs[i] == null)
                {
                    elevadorSalidaPrefabs.RemoveAt(i);
                }
            }

            if (elevadorSalidaPrefabs.Count > 0) return;

#if UNITY_EDITOR
            AutoCargarPrefabsEditor();
#endif

            if (elevadorSalidaPrefabs.Count == 0 && deadEnds != null)
            {
                for (int i = 0; i < deadEnds.Count; i++)
                {
                    if (deadEnds[i] != null && (deadEnds[i].name.Contains("Mercader") || deadEnds[i].name.Contains("Ruleta") || deadEnds[i].name.Contains("Elevador")))
                    {
                        elevadorSalidaPrefabs.Add(deadEnds[i]);
                    }
                }
            }

#if UNITY_EDITOR
            if (elevadorSalidaPrefabs.Count == 0)
            {
                string[] prefabsRutas = new[]
                {
                    "Assets/Prefabs/Entornos/SectorCero/SM_Elevador_Mercader_12x16.prefab",
                    "Assets/Prefabs/Entornos/SectorCero/SM_Elevador_Ruleta_12x12x12.prefab"
                };

                foreach (string ruta in prefabsRutas)
                {
                    GameObject p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
                    if (p != null && !elevadorSalidaPrefabs.Contains(p))
                    {
                        elevadorSalidaPrefabs.Add(p);
                    }
                }
            }
#endif

            if (elevadorSalidaPrefabs.Count > 0)
            {
                Debug.Log($"[LevelGenerator] Catálogo de elevadores de salida asegurado con éxito ({elevadorSalidaPrefabs.Count} modelos disponibles).");
            }
            else
            {
                Debug.LogError("[LevelGenerator] ERROR CRÍTICO: No fue posible encontrar prefabs de elevador de salida en el proyecto.");
            }
        }

        /// <summary>
        /// Comprueba si ya existe un elevador de salida válido e interactuable en el piso actual.
        /// </summary>
        public bool HayElevadorDeSalidaEnNivel()
        {
            if (_instantiatedExitElevator != null) return true;
            if (_cityRoot == null) return false;

            for (int i = 0; i < _cityRoot.childCount; i++)
            {
                Transform child = _cityRoot.GetChild(i);
                if (child == null) continue;
                if (child.name.Equals("StartRoom_SectorZero", StringComparison.OrdinalIgnoreCase)) continue;

                if (child.name.IndexOf("ElevadorSalida", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.IndexOf("Mercader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.IndexOf("Ruleta", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _instantiatedExitElevator = child.gameObject;
                    return true;
                }

                ElevatorPanelController panel = child.GetComponentInChildren<ElevatorPanelController>(true);
                if (panel != null && panel.StartEnabled)
                {
                    _instantiatedExitElevator = child.gameObject;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Mecanismo de respaldo infalible (Fallback de Emergencia):
        /// Si al finalizar el algoritmo de generación de habitaciones/pasillos no se ha instanciado
        /// ningún elevador de salida, fuerza obligatoriamente la colocación del elevador de salida
        /// en el último nodo o en la última sala creada, incluso reemplazando una pieza de pasillo estándar.
        /// Garantiza de forma estricta que ningún piso carezca de salida y activa su panel de control.
        /// </summary>
        public void EjecutarFallbackEmergenciaElevadorSalida()
        {
            if (HayElevadorDeSalidaEnNivel()) return;

            Debug.LogWarning("[LevelGenerator] [FALLBACK DE EMERGENCIA] Nivel sin elevador de salida detectado. Ejecutando forzado obligatorio de colocación de salida...");

            AsegurarCatalogoElevadoresSalida();

            if (elevadorSalidaPrefabs == null || elevadorSalidaPrefabs.Count == 0)
            {
                Debug.LogError("[LevelGenerator] [FALLBACK FALLIDO] ERROR CRÍTICO: Catálogo de elevadores de salida completamente vacío. Imposible forzar salida.");
                return;
            }

            int modelIndex = _rng != null ? _rng.Next(0, elevadorSalidaPrefabs.Count) : UnityEngine.Random.Range(0, elevadorSalidaPrefabs.Count);
            GameObject selectedElevatorPrefab = elevadorSalidaPrefabs[modelIndex];
            Transform parentContainer = ObtenerOInicializarCityRoot();

            Vector3 startPos = Vector3.zero;
            Transform startRoomObj = _cityRoot != null ? _cityRoot.Find("StartRoom_SectorZero") : null;
            if (startRoomObj != null) startPos = startRoomObj.position;

            // 1. Identificar módulos candidatos generados (excluyendo StartRoom_SectorZero)
            GameObject targetModule = null;
            float maxDistSqr = -1f;

            // Prioridad 1: Pasillos estándar situados a mayor distancia euclidiana del spawn
            for (int i = _spawnedModulesList.Count - 1; i >= 0; i--)
            {
                GameObject m = _spawnedModulesList[i];
                if (m == null || m.name.Contains("StartRoom")) continue;

                if (EsPasilloEstandar(m))
                {
                    float distSqr = (m.transform.position - startPos).sqrMagnitude;
                    if (distSqr > maxDistSqr)
                    {
                        maxDistSqr = distSqr;
                        targetModule = m;
                    }
                }
            }

            // Prioridad 2: El último nodo o módulo estructural generado (distinto de StartRoom)
            if (targetModule == null)
            {
                for (int i = _spawnedModulesList.Count - 1; i >= 0; i--)
                {
                    GameObject m = _spawnedModulesList[i];
                    if (m != null && !m.name.Contains("StartRoom"))
                    {
                        targetModule = m;
                        break;
                    }
                }
            }

            // Prioridad 3: Buscar en hijos de _cityRoot por si _spawnedModulesList estuviera desincronizada
            if (targetModule == null && _cityRoot != null)
            {
                for (int i = _cityRoot.childCount - 1; i >= 0; i--)
                {
                    Transform child = _cityRoot.GetChild(i);
                    if (child == null || child.name.Contains("StartRoom") || child.name.StartsWith("MuroCiego_")) continue;
                    targetModule = child.gameObject;
                    break;
                }
            }

            // CASO A: Se encontró una sala o pasillo para reemplazar
            if (targetModule != null)
            {
                string targetName = targetModule.name;
                Vector3 spawnPos = targetModule.transform.position;
                Quaternion spawnRot = targetModule.transform.rotation;

                // Intentar alineación fina con socket de entrada del módulo a reemplazar
                ModuleConnector targetConnector = targetModule.GetComponent<ModuleConnector>();
                ModuleConnector elevatorConnector = selectedElevatorPrefab.GetComponent<ModuleConnector>();

                if (targetConnector != null && targetConnector.availableSockets != null && targetConnector.availableSockets.Count > 0 &&
                    elevatorConnector != null && elevatorConnector.availableSockets != null && elevatorConnector.availableSockets.Count > 0)
                {
                    Transform targetSocket = targetConnector.availableSockets[0];
                    Transform elevatorEntrance = elevatorConnector.availableSockets[0];
                    if (targetSocket != null && elevatorEntrance != null)
                    {
                        Vector3 entrancePos = targetSocket.position;
                        Quaternion entranceRot = targetSocket.rotation;
                        spawnRot = entranceRot * Quaternion.Inverse(elevatorEntrance.localRotation);
                        spawnPos = entrancePos - (spawnRot * elevatorEntrance.localPosition);
                    }
                }

                _spawnedModulesList.Remove(targetModule);

                // Destruir la pieza a reemplazar
                if (Application.isPlaying)
                {
                    targetModule.SetActive(false);
                    targetModule.transform.SetParent(null);
                    Destroy(targetModule);
                }
                else
                {
                    DestroyImmediate(targetModule);
                }

                // Limpiar muros ciegos que pudieran haber taponado ese punto
                LimpiarMurosCiegosCercanos(spawnPos, 4.5f);

                // Instanciar el elevador de salida forzado
                GameObject exitElevator = Instantiate(selectedElevatorPrefab, spawnPos, spawnRot, parentContainer);
                exitElevator.name = $"ElevadorSalida_Fallback_{selectedElevatorPrefab.name}";
                exitElevator.transform.SetPositionAndRotation(spawnPos, spawnRot);
                exitElevator.transform.localScale = Vector3.one;
                Physics.SyncTransforms();

                // Habilitar panel de control dinámicamente mediante panelController.HabilitarPanel(true)
                ElevatorPanelController panelController = exitElevator.GetComponentInChildren<ElevatorPanelController>(true);
                if (panelController != null)
                {
                    panelController.HabilitarPanel(true);
                }

                ThematicPropSpawner propSpawner = exitElevator.GetComponent<ThematicPropSpawner>();
                if (propSpawner != null)
                {
                    propSpawner.SpawnThematicProps(_levelSeed);
                }

                _instantiatedExitElevator = exitElevator;
                _spawnedModulesList.Add(exitElevator);
                Debug.Log($"[LevelGenerator] [FALLBACK EXITOSO] Elevador de salida '{exitElevator.name}' colocado forzosamente reemplazando a '{targetName}'. Panel HABILITADO.");
                return;
            }

            // CASO B: El nivel solo tiene la sala de inicio (StartRoom_SectorZero)
            if (startRoomObj != null)
            {
                ModuleConnector startConnector = startRoomObj.GetComponent<ModuleConnector>();
                Vector3 spawnPos = startPos + startRoomObj.forward * 12f;
                Quaternion spawnRot = startRoomObj.rotation;

                if (startConnector != null && startConnector.availableSockets != null && startConnector.availableSockets.Count > 0)
                {
                    Transform startSocket = startConnector.availableSockets[0];
                    if (startSocket != null)
                    {
                        spawnPos = startSocket.position;
                        spawnRot = startSocket.rotation;

                        ModuleConnector elevatorConnector = selectedElevatorPrefab.GetComponent<ModuleConnector>();
                        if (elevatorConnector != null && elevatorConnector.availableSockets != null && elevatorConnector.availableSockets.Count > 0)
                        {
                            Transform elevatorEntrance = elevatorConnector.availableSockets[0];
                            if (elevatorEntrance != null && SimularTransformacionModulo(startSocket, elevatorEntrance, selectedElevatorPrefab.transform, out Vector3 simPos, out Quaternion simRot))
                            {
                                spawnPos = simPos;
                                spawnRot = simRot;
                            }
                        }
                    }
                }

                LimpiarMurosCiegosCercanos(spawnPos, 4.5f);

                GameObject exitElevator = Instantiate(selectedElevatorPrefab, spawnPos, spawnRot, parentContainer);
                exitElevator.name = $"ElevadorSalida_FallbackDirecto_{selectedElevatorPrefab.name}";
                exitElevator.transform.SetPositionAndRotation(spawnPos, spawnRot);
                exitElevator.transform.localScale = Vector3.one;
                Physics.SyncTransforms();

                ElevatorPanelController panelController = exitElevator.GetComponentInChildren<ElevatorPanelController>(true);
                if (panelController != null)
                {
                    panelController.HabilitarPanel(true);
                }

                ThematicPropSpawner propSpawner = exitElevator.GetComponent<ThematicPropSpawner>();
                if (propSpawner != null)
                {
                    propSpawner.SpawnThematicProps(_levelSeed);
                }

                _instantiatedExitElevator = exitElevator;
                _spawnedModulesList.Add(exitElevator);
                Debug.Log($"[LevelGenerator] [FALLBACK EXITOSO] Elevador de salida '{exitElevator.name}' acoplado forzosamente a la sala de inicio. Panel HABILITADO.");
            }
        }

        /// <summary>
        /// Comprueba si un módulo dado corresponde a una pieza de pasillo estándar.
        /// </summary>
        private bool EsPasilloEstandar(GameObject m)
        {
            if (m == null) return false;
            string n = m.name;
            if (n.IndexOf("Pasillo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Corridor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Lineal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (corridors != null)
            {
                for (int i = 0; i < corridors.Count; i++)
                {
                    if (corridors[i] != null && n.StartsWith(corridors[i].name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Elimina cualquier muro ciego o tapón dentro de un radio determinado para desbloquear el acceso al elevador forzado.
        /// </summary>
        private void LimpiarMurosCiegosCercanos(Vector3 center, float radius)
        {
            if (_cityRoot == null) return;
            float radiusSqr = radius * radius;

            for (int i = _cityRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = _cityRoot.GetChild(i);
                if (child == null) continue;

                if (child.name.StartsWith("MuroCiego_", StringComparison.OrdinalIgnoreCase) ||
                    child.name.IndexOf("Muro_Ciego", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.IndexOf("Tapon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if ((child.position - center).sqrMagnitude <= radiusSqr)
                    {
                        if (Application.isPlaying)
                        {
                            Destroy(child.gameObject);
                        }
                        else
                        {
                            DestroyImmediate(child.gameObject);
                        }
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // SELECCIÓN Y REGISTRO DE MÓDULOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selecciona un prefab aleatorio respetando probabilidades de cruces y salas seguras.
        /// </summary>
        private GameObject SeleccionarPrefabParaFrontera()
        {
            double roll = _rng.NextDouble();

            // Probabilidad de sala segura
            if (roll < _safeRoomChance && safeRooms != null && safeRooms.Count > 0)
            {
                return safeRooms[_rng.Next(0, safeRooms.Count)];
            }

            // Probabilidad de intersección
            if (roll < (_safeRoomChance + _intersectionChance) && intersections != null && intersections.Count > 0)
            {
                return intersections[_rng.Next(0, intersections.Count)];
            }

            // Pasillo estándar (Corridor)
            if (corridors != null && corridors.Count > 0)
            {
                return corridors[_rng.Next(0, corridors.Count)];
            }

            return null;
        }

        /// <summary>
        /// Instancia el StartRoom inicial en el origen garantizando escala unitaria y sincronización física.
        /// Asegura que el panel de control del elevador de spawn/entrada permanezca bloqueado para impedir
        /// que el jugador descienda o escape nada más aparecer en la sala inicial.
        /// </summary>
        private GameObject InstanciarStartRoom()
        {
            if (startRoom == null || startRoom.Count == 0) return null;

            Transform root = ObtenerOInicializarCityRoot();
            GameObject prefab = startRoom[_rng.Next(0, startRoom.Count)];
            GameObject instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, root);
            instance.name = "StartRoom_SectorZero";

            // Blindaje de escala y colisionadores físicos
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            Physics.SyncTransforms();

            // Bloqueo estricto del panel de control en el elevador inicial/spawn
            ElevatorPanelController startPanel = instance.GetComponentInChildren<ElevatorPanelController>(true);
            if (startPanel != null)
            {
                startPanel.HabilitarPanel(false);
                Debug.Log("[LevelGenerator] Panel de control del elevador de inicio (Spawn) bloqueado.");
            }

            ThematicPropSpawner propSpawner = instance.GetComponent<ThematicPropSpawner>();
            if (propSpawner != null)
            {
                propSpawner.SpawnThematicProps(_levelSeed);
            }

            return instance;
        }

        /// <summary>
        /// Extrae y agrega los sockets libres de una nueva pieza a la lista de frontera.
        /// </summary>
        private void RegistrarSocketsDeModulo(GameObject moduleInstance)
        {
            ModuleConnector connector = moduleInstance.GetComponent<ModuleConnector>();
            if (connector == null) return;

            for (int i = 0; i < connector.availableSockets.Count; i++)
            {
                Transform socket = connector.availableSockets[i];
                if (socket != null)
                {
                    _activeOpenSockets.Add(new OpenSocketEntry(socket, connector));
                }
            }
        }

        /// <summary>
        /// Encuentra el socket equivalente en una instancia recién clonada coincidiendo por nombre o índice.
        /// </summary>
        private Transform EncontrarSocketEquivalente(ModuleConnector connector, string socketName, int preferredIndex)
        {
            if (connector == null || connector.availableSockets == null || connector.availableSockets.Count == 0)
                return null;

            for (int i = 0; i < connector.availableSockets.Count; i++)
            {
                Transform s = connector.availableSockets[i];
                if (s != null && s.name == socketName)
                {
                    return s;
                }
            }

            if (preferredIndex >= 0 && preferredIndex < connector.availableSockets.Count)
            {
                return connector.availableSockets[preferredIndex];
            }

            return connector.availableSockets[0];
        }

        /// <summary>
        /// Mezcla determinista de una lista mediante el algoritmo de Fisher-Yates.
        /// </summary>
        private void ShuffleList<T>(List<T> list, System.Random rng)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        /// <summary>
        /// Configura el generador pseudoaleatorio determinista.
        /// </summary>
        private void InicializarSemilla()
        {
            if (_randomizeSeedOnStart)
            {
                _levelSeed = System.Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
            }

            int seedHash = _levelSeed.GetHashCode();
            _rng = new System.Random(seedHash);
        }

        /// <summary>
        /// Obtiene o inicializa el contenedor raíz del nivel asegurando transformación identidad
        /// (Vector3.zero, Quaternion.identity, localScale Vector3.one) para evitar distorsiones de rotación en sockets.
        /// </summary>
        private Transform ObtenerOInicializarCityRoot()
        {
            if (_cityRoot == null)
            {
                GameObject rootGo = GameObject.Find("CitySectorZero_Root");
                if (rootGo == null)
                {
                    rootGo = new GameObject("CitySectorZero_Root");
                }
                _cityRoot = rootGo.transform;
            }

            _cityRoot.SetParent(null);
            _cityRoot.position = Vector3.zero;
            _cityRoot.rotation = Quaternion.identity;
            _cityRoot.localScale = Vector3.one;

            return _cityRoot;
        }

        /// <summary>
        /// Destruye todos los GameObjects hijos dentro del contenedor raíz (_cityRoot)
        /// y limpia las listas de rastreo para liberar memoria antes de reconstruir el nivel.
        /// </summary>
        [ContextMenu("Limpiar Nivel Actual")]
        public void LimpiarNivelActual()
        {
            ObtenerOInicializarCityRoot();
#if UNITY_EDITOR
            if (startRoom == null || startRoom.Count == 0)
            {
                AutoCargarPrefabsEditor();
            }
#endif

            // Destrucción limpia de los módulos hijos en tiempo de juego o edición
            for (int i = _cityRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = _cityRoot.GetChild(i);
                if (child == null) continue;

                if (Application.isPlaying)
                {
                    child.gameObject.SetActive(false);
                    child.SetParent(null);
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            // Limpieza de estructuras de datos y listas de rastreo
            _activeOpenSockets.Clear();
            _rejectedSockets.Clear();
            _placedModuleBounds.Clear();
            _spawnedModulesList.Clear();
            _instantiatedExitElevator = null;
            _spawnedModulesCount = 0;

            // Sincronización inmediata con el motor PhysX para purgar colisionadores destruidos
            Physics.SyncTransforms();

            Debug.Log("[LevelGenerator] LimpiarNivelActual: Módulos y volúmenes anteriores destruidos correctamente.");
        }

        /// <summary>
        /// Alias de compatibilidad hacia atrás para LimpiarNivelActual().
        /// </summary>
        private void LimpiarCiudadPrevia()
        {
            LimpiarNivelActual();
        }

        // ════════════════════════════════════════════════════════════════════
        // DEPURACIÓN VISUAL EN EDITOR (GIZMOS)
        // ════════════════════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            // Dibuja los volúmenes de todos los módulos ya colocados
            if (_placedModuleBounds != null && _placedModuleBounds.Count > 0)
            {
                Gizmos.color = new Color(0f, 1f, 0.6f, 0.25f);
                for (int i = 0; i < _placedModuleBounds.Count; i++)
                {
                    Bounds b = _placedModuleBounds[i];
                    Gizmos.DrawWireCube(b.center, b.size);
                }
            }

            // Dibuja los enchufes abiertos que queden en la frontera
            if (_activeOpenSockets != null && _activeOpenSockets.Count > 0)
            {
                Gizmos.color = Color.yellow;
                for (int i = 0; i < _activeOpenSockets.Count; i++)
                {
                    Transform socket = _activeOpenSockets[i].SocketTransform;
                    if (socket != null)
                    {
                        Gizmos.DrawWireSphere(socket.position, 0.3f);
                        Gizmos.DrawRay(socket.position, socket.forward * 2f);
                    }
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Carga automáticamente el catálogo completo de prefabs modulares desde la carpeta del proyecto
        /// si las listas del inspector están vacías (blindaje contra escenas nuevas o componentes añadidos dinámicamente).
        /// </summary>
        [ContextMenu("Auto-Cargar Prefabs (Sector Cero)")]
        public void AutoCargarPrefabsEditor()
        {
            if (startRoom != null && startRoom.Count > 0 && elevadorSalidaPrefabs != null && elevadorSalidaPrefabs.Count > 0) return;

            string folder = "Assets/Prefabs/Entornos/SectorCero";
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) return;

            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            if (guids == null || guids.Length == 0) return;

            Debug.Log($"[LevelGenerator] Auto-cargando catálogo de {guids.Length} prefabs modulares desde '{folder}'...");

            startRoom = new List<GameObject>();
            corridors = new List<GameObject>();
            intersections = new List<GameObject>();
            elevadorSalidaPrefabs = new List<GameObject>();
            deadEnds = new List<GameObject>();
            safeRooms = new List<GameObject>();

            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                string name = prefab.name;

                if (name.Contains("SM_Elevador_Carga_8x8"))
                {
                    startRoom.Add(prefab);
                }
                else if (name.Contains("SM_Elevador_Mercader") || name.Contains("SM_Elevador_Ruleta"))
                {
                    elevadorSalidaPrefabs.Add(prefab);
                    if (!deadEnds.Contains(prefab)) deadEnds.Add(prefab);
                }
                else if (name.Contains("SM_Muro_Ciego") || name.Contains("Tapon_Definitivo"))
                {
                    muroCiegoPrefab = prefab;
                }
                else if (name.Contains("Armeria_Oculta") || name.Contains("Refugio_Vertical") || name.Contains("Sala_Monitoreo"))
                {
                    safeRooms.Add(prefab);
                }
                else if (name.Contains("Habitacion_Rehen") || name.Contains("Laboratorio_Clandestino") || name.Contains("Oficina_Seguridad"))
                {
                    deadEnds.Add(prefab);
                }
                else if (name.Contains("Cruce") || name.Contains("Interseccion") || name.Contains("Arena_Hangar") || name.Contains("Arena_Ensamblaje") || name.Contains("Arena_Almacen") || name.Contains("Recibidor_Amplio") || name.Contains("Servidores"))
                {
                    intersections.Add(prefab);
                }
                else
                {
                    corridors.Add(prefab);
                }
            }

            if (muroCiegoPrefab == null)
            {
                var tapon = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Entornos/SectorCero/Tapon_Definitivo.prefab");
                if (tapon != null) muroCiegoPrefab = tapon;
            }

            Debug.Log($"[LevelGenerator] Catálogo modular cargado con éxito: StartRoom={startRoom.Count}, Pasillos={corridors.Count}, Cruces={intersections.Count}, Salidas={elevadorSalidaPrefabs.Count}, DeadEnds={deadEnds.Count}, SafeRooms={safeRooms.Count}");
        }
#endif
    }
}
