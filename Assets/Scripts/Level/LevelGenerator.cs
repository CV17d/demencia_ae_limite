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

        [Header("Meta del Nivel (Elevador de Salida)")]
        [Tooltip("Prefab del elevador de salida que conduce al siguiente piso.")]
        [SerializeField] private GameObject elevadorSalidaPrefab;

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
        private readonly List<Bounds> _placedModuleBounds = new List<Bounds>();
        private int _spawnedModulesCount;
        private bool _isGenerating;

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

            LimpiarNivelActual();
            InicializarSemilla();
            ConstruirPiso();
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

            // 1. Incrementar pisoActual
            pisoActual++;

            // Sincronizar con el GameManager si está presente
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TransitionToFloor(pisoActual);
            }

            // 2. Limpieza completa del nivel anterior
            LimpiarNivelActual();

            // 3. Forzar cambio de semilla única para el nuevo piso
            _levelSeed = $"SECTOR_{pisoActual:D2}_{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";
            int seedHash = _levelSeed.GetHashCode();
            _rng = new System.Random(seedHash);

            // 4. (Opcional) Aumentar ligeramente MaxModules para que los pisos más profundos sean más grandes
            _maxModules += modulosExtraPorPiso;

            Debug.Log($"[LevelGenerator] Descendiendo al Piso {pisoActual}/{pisoMaximo} (Nueva Semilla: '{_levelSeed}', MaxMódulos: {_maxModules})...");

            // 5. Invocar la construcción del nuevo piso (ejecuta el bucle principal y dispara GameEvents.FireMapGenerationCompleted)
            ConstruirPiso();

            // 6. Reposicionar al jugador de forma segura en la nueva sala inicial
            ReposicionarJugadorEnInicio();
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
            if (_validationMethod == SpatialValidationMethod.PhysicsOverlapBox)
            {
                Physics.SyncTransforms();
            }

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

                // 3. RECHAZO Y SELLADO OBLIGATORIO DE CONECTOR HUÉRFANO:
                // Si agotó todos los reintentos (MaxRetriesPerSocket) por colisión espacial y ninguna pieza cupo,
                // forzar obligatoriamente la instanciación de muroCiegoPrefab en las coordenadas de ese socket
                // antes de descartarlo de la lista para evitar pasillos abiertos intermedios.
                if (!modulePlaced)
                {
                    SellarSocketIndividual(originEntry.SocketTransform);
                }

                // Descartar definitivamente el socket procesado de la lista activa
                _activeOpenSockets.RemoveAt(socketIndex);
            }

            // 4. CIERRE DE FRONTERAS (SELLADO FINAL DE CONECTORES ABIERTOS)
            SellarConectoresAbiertos();

            _isGenerating = false;
            Debug.Log($"[LevelGenerator] Piso {pisoActual} generado con éxito ({_spawnedModulesCount} módulos estructurales, {_placedModuleBounds.Count} volúmenes registrados).");

            // 5. INTEGRACIÓN CRÍTICA CON IA: Notificación al Event Bus para horneado de NavMesh
            GameEvents.FireMapGenerationCompleted();
        }

        /// <summary>
        /// Reposiciona al jugador en el origen de la nueva sala inicial para evitar caídas al vacío.
        /// </summary>
        private void ReposicionarJugadorEnInicio()
        {
            Vector3 spawnPosition = new Vector3(0f, 1.5f, 0f);

            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.Teleport(spawnPosition, Quaternion.identity);
                return;
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.SafelyTeleportPlayer(spawnPosition);
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = spawnPosition;
                player.transform.rotation = Quaternion.identity;
                if (cc != null) cc.enabled = true;
            }
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

                instantiatedModule = Instantiate(prefab, directPos, directRot, _cityRoot);
                finalWorldBounds = simulatedBounds;

                if (_validationMethod == SpatialValidationMethod.PhysicsOverlapBox)
                {
                    Physics.SyncTransforms();
                }
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
                        instantiatedModule = Instantiate(prefab, simPos, simRot, _cityRoot);
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

                        if (_validationMethod == SpatialValidationMethod.PhysicsOverlapBox)
                        {
                            Physics.SyncTransforms();
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

            Transform parentContainer = _cityRoot != null ? _cityRoot : transform;

            // Instanciación directa en las coordenadas y orientación exactas del socket rechazado
            GameObject muro = Instantiate(prefabToUse, openSocket.position, openSocket.rotation, parentContainer);
            muro.name = $"MuroCiego_Rechazo_{openSocket.name}";

            if (_validationMethod == SpatialValidationMethod.PhysicsOverlapBox)
            {
                Physics.SyncTransforms();
            }
        }

        /// <summary>
        /// Rutina de cierre final: Selecciona aleatoriamente un socket abierto para instanciar
        /// el elevadorSalidaPrefab (meta del piso) y sella todos los conectores restantes con muroCiegoPrefab.
        /// </summary>
        private void SellarConectoresAbiertos()
        {
            if (_activeOpenSockets == null || _activeOpenSockets.Count == 0)
                return;

            Transform parentContainer = _cityRoot != null ? _cityRoot : transform;

            // 1. Instanciación de la meta del nivel (Elevador de Salida) en un socket aleatorio
            if (elevadorSalidaPrefab != null && _activeOpenSockets.Count > 0)
            {
                int exitIndex = _rng != null ? _rng.Next(0, _activeOpenSockets.Count) : UnityEngine.Random.Range(0, _activeOpenSockets.Count);
                OpenSocketEntry exitEntry = _activeOpenSockets[exitIndex];

                if (exitEntry.SocketTransform != null)
                {
                    GameObject exitElevator = Instantiate(elevadorSalidaPrefab, exitEntry.SocketTransform.position, exitEntry.SocketTransform.rotation, parentContainer);
                    exitElevator.name = $"ElevadorSalida_{exitEntry.SocketTransform.name}";
                    Debug.Log($"[LevelGenerator] Elevador de salida instanciado con éxito en conector '{exitEntry.SocketTransform.name}'.");
                }

                // Consumir el socket asignado a la salida para no taparlo con un muro ciego
                _activeOpenSockets.RemoveAt(exitIndex);
            }
            else if (elevadorSalidaPrefab == null)
            {
                Debug.LogWarning("[LevelGenerator] No se ha asignado 'elevadorSalidaPrefab' en el Inspector. El nivel se sellará sin elevador de salida.");
            }

            // 2. Sellado del resto de conectores abiertos con muros ciegos
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
                    return;
                }
            }

            Debug.Log($"[LevelGenerator] Sellando {_activeOpenSockets.Count} conectores restantes con '{prefabToUse.name}'...");

            for (int i = 0; i < _activeOpenSockets.Count; i++)
            {
                OpenSocketEntry entry = _activeOpenSockets[i];
                if (entry.SocketTransform == null) continue;

                // Instancia el tapón en la posición y rotación exactas del socket abierto
                GameObject muro = Instantiate(prefabToUse, entry.SocketTransform.position, entry.SocketTransform.rotation, parentContainer);
                muro.name = $"MuroCiego_{entry.SocketTransform.name}_{i}";
            }

            // 3. Limpieza final de la estructura de sockets disponibles
            _activeOpenSockets.Clear();

            if (_validationMethod == SpatialValidationMethod.PhysicsOverlapBox)
            {
                Physics.SyncTransforms();
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
        /// Instancia el StartRoom inicial en el origen.
        /// </summary>
        private GameObject InstanciarStartRoom()
        {
            if (startRoom == null || startRoom.Count == 0) return null;

            GameObject prefab = startRoom[_rng.Next(0, startRoom.Count)];
            GameObject instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, _cityRoot);
            instance.name = "StartRoom_SectorZero";

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
        /// Destruye todos los GameObjects hijos dentro del contenedor raíz (_cityRoot)
        /// y limpia las listas de rastreo para liberar memoria antes de reconstruir el nivel.
        /// </summary>
        [ContextMenu("Limpiar Nivel Actual")]
        public void LimpiarNivelActual()
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
            _placedModuleBounds.Clear();
            _spawnedModulesCount = 0;

            if (_validationMethod == SpatialValidationMethod.PhysicsOverlapBox)
            {
                Physics.SyncTransforms();
            }

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
