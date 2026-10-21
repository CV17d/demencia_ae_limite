// ============================================================================
// LevelGenerator.cs — Motor Procedural de la Ciudad (Sector Cero / WFC Modular)
// ============================================================================
// Ensambla la arquitectura modular de la ciudad mediante acoplamiento por sockets.
// - Generación pseudoaleatoria determinista basada en System.Random y string levelSeed.
// - Ensamblaje cerrado por enchufes (startRoom -> pasillos/intersecciones -> deadEnds).
// - Notifica a la IA mediante GameEvents.FireMapGenerationCompleted() para iniciar
//   el horneado asíncrono con ProceduralNavMeshBuilder.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Level
{
    [DisallowMultipleComponent]
    public class LevelGenerator : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONSTANTES ARQUITECTÓNICAS (COMPATIBILIDAD CON SISTEMAS DE NIVEL)
        // ════════════════════════════════════════════════════════════════════

        public const float StandardCellSize = 3.0f;
        public const float StandardWallHeight = 3.2f;
        public const float StandardCeilingHeight = 3.2f;

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE SEMILLA Y LÍMITES
        // ════════════════════════════════════════════════════════════════════

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

        [Tooltip("Paredes ciegas y cierres para sellar enchufes abiertos al alcanzar maxModules.")]
        public List<GameObject> deadEnds = new List<GameObject>();

        [Tooltip("Salas seguras con puntos de respiro o mercaderes.")]
        public List<GameObject> safeRooms = new List<GameObject>();

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private System.Random _rng;
        private Transform _cityRoot;
        private readonly List<OpenSocketEntry> _activeOpenSockets = new List<OpenSocketEntry>();
        private int _spawnedModulesCount;
        private bool _isGenerating;

        public bool IsGenerating => _isGenerating;
        public string ActiveSeed => _levelSeed;

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
        // GENERACIÓN PROCEDURAL (WFC POR SOCKETS)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Inicia la generación determinista de la ciudad a partir de la semilla.
        /// </summary>
        [ContextMenu("Generar Ciudad Procedural")]
        public void GenerateCitySector()
        {
            if (_isGenerating) return;

            _isGenerating = true;
            LimpiarCiudadPrevia();
            InicializarSemilla();

            Debug.Log($"[LevelGenerator] Iniciando generación de Sector Cero con semilla: '{_levelSeed}'...");

            // 1. INSTANCIACIÓN DE LA SALA INICIAL (START ROOM) EN EL ORIGEN
            GameObject startModule = InstanciarStartRoom();
            if (startModule == null)
            {
                Debug.LogError("[LevelGenerator] Error crítico: No se configuraron prefabs en 'startRoom'.");
                _isGenerating = false;
                return;
            }

            // Registrar los enchufes de la sala inicial
            RegistrarSocketsDeModulo(startModule);
            _spawnedModulesCount = 1;

            // 2. BUCLE DE EXPANSIÓN POR ENCHUFES
            while (_spawnedModulesCount < _maxModules && _activeOpenSockets.Count > 0)
            {
                // Seleccionar un socket abierto disponible de la frontera
                int socketIndex = _rng.Next(0, _activeOpenSockets.Count);
                OpenSocketEntry originEntry = _activeOpenSockets[socketIndex];
                _activeOpenSockets.RemoveAt(socketIndex);

                if (originEntry.SocketTransform == null) continue;

                // Elegir el tipo de módulo a ensamblar
                GameObject selectedPrefab = SeleccionarPrefabParaFrontera();
                if (selectedPrefab == null) continue;

                // Instanciar y acoplar el nuevo módulo alineando sus enchufes matemáticamente
                GameObject newModule = AcoplarModuloASocket(selectedPrefab, originEntry.SocketTransform);
                if (newModule != null)
                {
                    _spawnedModulesCount++;
                    RegistrarSocketsDeModulo(newModule);
                }
            }

            // 3. CIERRE DE FRONTERAS (SELLADO CON DEAD ENDS)
            SellarEnchufesRestantes();

            _isGenerating = false;
            Debug.Log($"[LevelGenerator] Ciudad generada con éxito ({_spawnedModulesCount} módulos).");

            // 4. INTEGRACIÓN CRÍTICA CON IA: Notificación al Event Bus
            // Dispara el horneado asíncrono en ProceduralNavMeshBuilder sin congelar el hilo principal
            GameEvents.FireMapGenerationCompleted();
        }

        // ════════════════════════════════════════════════════════════════════
        // MATEMÁTICAS DE ALINEACIÓN DE SOCKETS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Acopla un nuevo módulo a un socket de origen alineando posición y rotación con precisión matemática.
        /// </summary>
        /// <param name="prefab">Prefab del módulo que se va a instanciar.</param>
        /// <param name="originSocket">Transform del enchufe existente al cual acoplarse.</param>
        /// <returns>Instancia creada del módulo acoplado.</returns>
        private GameObject AcoplarModuloASocket(GameObject prefab, Transform originSocket)
        {
            // Instanciamos el nuevo GameObject temporalmente en el origen
            GameObject newModuleInstance = Instantiate(prefab, _cityRoot);
            ModuleConnector newConnector = newModuleInstance.GetComponent<ModuleConnector>();

            if (newConnector == null || !newConnector.HasAvailableSockets)
            {
                // Si el prefab carece de sockets definidos, colocarlo directamente
                newModuleInstance.transform.position = originSocket.position;
                newModuleInstance.transform.rotation = originSocket.rotation;
                return newModuleInstance;
            }

            // Seleccionamos un socket de entrada en el nuevo módulo
            Transform targetSocket = newConnector.ConsumeRandomSocket(_rng);
            if (targetSocket == null)
            {
                return newModuleInstance;
            }

            // ── CÁLCULO MATEMÁTICO DE ROTACIÓN ─────────────────────────────────
            // Dos sockets deben quedar enfrentados: El forward del socket de destino
            // debe apuntar exactamente en sentido inverso (-originSocket.forward).
            Vector3 desiredForward = -originSocket.forward;
            Quaternion targetRotation = Quaternion.LookRotation(desiredForward, originSocket.up);

            // Calculamos la rotación final del padre compensando la rotación local del socket hijo:
            // R_parent = Q_target * Inverse(Socket_localRotation)
            Quaternion parentRotation = targetRotation * Quaternion.Inverse(targetSocket.localRotation);
            newModuleInstance.transform.rotation = parentRotation;

            // ── CÁLCULO MATEMÁTICO DE TRASLACIÓN ──────────────────────────────
            // Con el módulo rotado, el offset del socket respecto al pivote es:
            // WorldOffset = R_parent * Socket_localPosition
            // Por lo tanto, para que Socket coincida en originSocket.position:
            // Pos_parent = Pos_origin - WorldOffset
            Vector3 socketWorldOffset = parentRotation * targetSocket.localPosition;
            newModuleInstance.transform.position = originSocket.position - socketWorldOffset;

            // El socket target ya quedó acoplado y no debe recibir conexiones adicionales
            newConnector.RemoveSocket(targetSocket);

            return newModuleInstance;
        }

        // ════════════════════════════════════════════════════════════════════
        // CIERRE DE FRONTERAS (DEAD ENDS)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recorre todos los enchufes que quedaron expuestos al aire libre y los sella con muros ciegos.
        /// </summary>
        private void SellarEnchufesRestantes()
        {
            if (deadEnds == null || deadEnds.Count == 0)
            {
                Debug.LogWarning("[LevelGenerator] No hay prefabs asignados en 'deadEnds' para sellar fronteras.");
                _activeOpenSockets.Clear();
                return;
            }

            Debug.Log($"[LevelGenerator] Sellando {_activeOpenSockets.Count} enchufes con muros ciegos (deadEnds)...");

            for (int i = 0; i < _activeOpenSockets.Count; i++)
            {
                OpenSocketEntry entry = _activeOpenSockets[i];
                if (entry.SocketTransform == null) continue;

                GameObject deadEndPrefab = deadEnds[_rng.Next(0, deadEnds.Count)];
                if (deadEndPrefab != null)
                {
                    AcoplarModuloASocket(deadEndPrefab, entry.SocketTransform);
                }
            }

            _activeOpenSockets.Clear();
        }

        // ════════════════════════════════════════════════════════════════════
        // SELECCIÓN Y REGISTRO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selecciona un prefab aleatorio respetando probabilidades de cruces y salas de descanso.
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
        /// Elimina módulos generados previamente para permitir regeneración limpia en tiempo de ejecución.
        /// </summary>
        private void LimpiarCiudadPrevia()
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

            for (int i = _cityRoot.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(_cityRoot.GetChild(i).gameObject);
            }

            _activeOpenSockets.Clear();
            _spawnedModulesCount = 0;
        }
    }
}
