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
        }

        private void Start()
        {
            if (_generateOnStart)
            {
                GenerateCitySector();
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
            GameObject startModule = InstanciarStartRoom();
            if (startModule == null)
            {
                Debug.LogError("[LevelGenerator] Error crítico: No se configuraron prefabs en 'startRoom'.");
                _isGenerating = false;
                return;
            }

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

            // Sincronización global obligatoria de colisionadores antes de hornear NavMesh o posicionar al jugador
            Physics.SyncTransforms();

            _isGenerating = false;
            Debug.Log($"[LevelGenerator] Piso {pisoActual} generado con éxito ({_spawnedModulesCount} módulos estructurales, {_placedModuleBounds.Count} volúmenes registrados).");

            // 5. INTEGRACIÓN CRÍTICA CON IA: Notificación al Event Bus para horneado de NavMesh
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
        /// garantiza la colocación aleatoria de un elevador de la lista elevadorSalidaPrefabs y sella el resto con muroCiegoPrefab.
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

            if (todosLosConectores.Count == 0)
            {
                Debug.LogWarning("[LevelGenerator] No hay conectores disponibles ni rechazados para procesar el cierre del nivel.");
                _activeOpenSockets.Clear();
                _rejectedSockets.Clear();
                return;
            }

            Transform parentContainer = ObtenerOInicializarCityRoot();

            // 2. Selección aleatoria de un conector para instanciar el elevador de salida (meta del nivel)
            if (elevadorSalidaPrefabs != null && elevadorSalidaPrefabs.Count > 0)
            {
                int exitIndex = _rng != null ? _rng.Next(0, todosLosConectores.Count) : UnityEngine.Random.Range(0, todosLosConectores.Count);
                Transform exitSocket = todosLosConectores[exitIndex];

                int modelIndex = _rng != null ? _rng.Next(0, elevadorSalidaPrefabs.Count) : UnityEngine.Random.Range(0, elevadorSalidaPrefabs.Count);
                GameObject selectedElevatorPrefab = elevadorSalidaPrefabs[modelIndex];

                if (exitSocket != null && selectedElevatorPrefab != null)
                {
                    GameObject exitElevator = Instantiate(selectedElevatorPrefab, exitSocket.position, exitSocket.rotation, parentContainer);
                    exitElevator.name = $"ElevadorSalida_{exitSocket.name}";
                    exitElevator.transform.SetPositionAndRotation(exitSocket.position, exitSocket.rotation);
                    exitElevator.transform.localScale = Vector3.one;
                    Physics.SyncTransforms();
                    Debug.Log($"[LevelGenerator] Elevador de salida '{selectedElevatorPrefab.name}' instanciado con éxito en conector consolidado '{exitSocket.name}'.");
                }

                // Consumir el conector seleccionado para la salida para no taparlo con un muro ciego
                todosLosConectores.RemoveAt(exitIndex);
            }
            else
            {
                Debug.LogWarning("[LevelGenerator] No se han asignado prefabs en 'elevadorSalidaPrefabs' en el Inspector. El nivel se sellará sin salida.");
            }

            // 3. Sellado del resto de conectores de la lista unificada con muros ciegos
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

            // 4. Limpieza final de todas las estructuras de conectores
            _activeOpenSockets.Clear();
            _rejectedSockets.Clear();
            todosLosConectores.Clear();

            // Sincronización obligatoria final de colisionadores físicos
            Physics.SyncTransforms();
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
    }
}
