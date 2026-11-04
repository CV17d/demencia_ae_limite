// ============================================================================
// PlayerWeaponEquipManager.cs — Gestor de Equipamiento e Instanciamiento de Armas 3D
// ============================================================================
// Coordina la visualización en primera persona de los modelos 3D de todas las armas:
// - Instancia y emparenta prefabs 3D directamente bajo el WeaponHolder de la Main Camera.
// - Generalizado para todo el catálogo (Bingala, Demencia Revólver, Muak, Trabeca, etc.).
// - Fuerza explícitamente la escala adecuada (respetando la escala nativa de 100 en modelos
//   exportados en centímetros y evitando el colapso a Vector3.one).
// - Búsqueda automática de prefabs en Assets/Prefabs/Entornos/Armas_Prefab/ o Resources.
// - Neutralización total de físicas (Colliders, Rigidbodies, Joints) para no interferir
//   con el CharacterController del jugador.
// - Asigna recursivamente la capa "Weapon" garantizando el cullingMask de la Main Camera.
// - Sincroniza el estado de la mano 2D diegética (PlayerHandController -> HandState.EquippedItem).
// - Escucha automáticamente cambios de ranura de PlayerInventory y recogidas de WorldItemPickup.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DemenciaNoLimite.Core.Data;

#if UNITY_EDITOR
using UnityEditor;
#endif

#pragma warning disable CS0649

namespace DemenciaNoLimite.Player
{
    /// <summary>
    /// Mapeo serializable que asocia un identificador o WeaponData con su prefab 3D en el mundo.
    /// </summary>
    [Serializable]
    public class Weapon3DMapping
    {
        [Tooltip("Identificador único del arma (ej. 'bingala', 'revolver', 'demencia revolver', 'muak', 'trabeca').")]
        public string weaponId;

        [Tooltip("ScriptableObject de WeaponData opcional asociado al arma.")]
        public WeaponData weaponData;

        [Tooltip("Prefab 3D que contiene el mesh, materiales y efectos visuales del arma en primera persona.")]
        public GameObject weaponPrefab;

        [Tooltip("Desplazamiento local respecto al WeaponHolder.")]
        public Vector3 localPositionOffset = Vector3.zero;

        [Tooltip("Rotación local (Euler) respecto al WeaponHolder.")]
        public Vector3 localRotationOffset = Vector3.zero;

        [Tooltip("Escala local del modelo 3D. Por defecto (100, 100, 100) para modelos FBX/centimétricos.")]
        public Vector3 localScale = new Vector3(100f, 100f, 100f);

        [Tooltip("Multiplicador individual de escala para este modelo.")]
        [Range(0.01f, 10f)]
        public float scaleMultiplier = 1f;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Player/Player Weapon Equip Manager")]
    public class PlayerWeaponEquipManager : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CATÁLOGO Y REGISTRO DE ARMAS 3D
        // ════════════════════════════════════════════════════════════════════

        [Header("Catálogo de Armas 3D")]
        [Tooltip("Lista mapeada de armas y sus respectivos prefabs 3D.")]
        [SerializeField] private List<Weapon3DMapping> _weaponCatalog = new List<Weapon3DMapping>();

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE ESCALA Y VISIBILIDAD DE ARMAS 3D
        // ════════════════════════════════════════════════════════════════════

        [Header("Configuración de Escala de Armas 3D")]
        [Tooltip("Escala local forzada por defecto para las armas equipadas frente a la cámara (ej. (100, 100, 100) para modelos FBX).")]
        [SerializeField] private Vector3 _defaultWeaponScale = new Vector3(100f, 100f, 100f);

        [Tooltip("Multiplicador global de escala aplicado a las armas al ser equipadas frente a la cámara.")]
        [SerializeField] private float _globalScaleMultiplier = 1f;

        [Tooltip("Si está activo, detecta si el prefab original tiene una escala nativa (ej. 100 en los prefabs) y la respeta en lugar de forzar (1,1,1).")]
        [SerializeField] private bool _usePrefabNativeScale = true;

        [Tooltip("Umbral de escala mínima. Si la escala detectada o del mapping es menor o igual a este valor (ej. 1.8), se forzará automáticamente a _defaultWeaponScale (100,100,100) para evitar que quede microscópica.")]
        [SerializeField] private float _tinyScaleThreshold = 1.8f;

        // ════════════════════════════════════════════════════════════════════
        // ANCLAJE DIRECTO A CÁMARA PRINCIPAL (CERO DEPENDENCIAS SECUNDARIAS)
        // ════════════════════════════════════════════════════════════════════

        [Header("Anclaje a Cámara Principal")]
        [Tooltip("Cámara principal del jugador (Main Camera / PlayerCamera). Si es nula, se busca y resuelve automáticamente.")]
        [SerializeField] private Camera _mainCamera;

        [Tooltip("Transform contenedor donde se anclan los modelos 3D de las armas directamente como hijo de la Main Camera.")]
        [SerializeField] private Transform _weaponHolder;

        [Tooltip("Nombre de la capa para el modelo del arma (ej. 'Weapon' o 'Default').")]
        [SerializeField] private string _weaponLayerName = "Weapon";

        [Tooltip("Asegura automáticamente que la Main Camera incluya esta capa en su cullingMask para que sea visible en pantalla.")]
        [SerializeField] private bool _ensureCameraCullingMask = true;

        [Tooltip("Posición local por defecto del WeaponHolder frente a la vista de la cámara.")]
        [SerializeField] private Vector3 _defaultHolderPosition = new Vector3(0.22f, -0.18f, 0.42f);

        [Tooltip("Rotación local por defecto del WeaponHolder.")]
        [SerializeField] private Vector3 _defaultHolderRotation = Vector3.zero;

        // ════════════════════════════════════════════════════════════════════
        // INTEGRACIÓN DIEGÉTICA Y ANIMACIÓN PROCEDIMENTAL (SWAY)
        // ════════════════════════════════════════════════════════════════════

        [Header("Balanceo e Inercia Procedimental (Weapon Sway)")]
        [Tooltip("Activa la inercia procedural del arma 3D en respuesta al movimiento del ratón.")]
        [SerializeField] private bool _enableWeaponSway = true;

        [Tooltip("Sensibilidad de la inercia del arma.")]
        [SerializeField] private float _swayAmount = 1.2f;

        [Tooltip("Límite máximo de desplazamiento por inercia.")]
        [SerializeField] private float _maxSwayAmount = 0.05f;

        [Tooltip("Velocidad de suavizado del retorno del arma a su posición central.")]
        [SerializeField] private float _swaySmoothSpeed = 8f;

        // ════════════════════════════════════════════════════════════════════
        // COMPONENTES CACHEADOS
        // ════════════════════════════════════════════════════════════════════

        [Header("Referencias de Jugador (Auto-Resueltas)")]
        [SerializeField] private PlayerManager _playerManager;
        [SerializeField] private PlayerInventory _playerInventory;
        [SerializeField] private PlayerHandController _playerHandController;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private GameObject _currentSpawnedWeaponInstance;
        private WeaponData _currentEquippedWeaponData;
        private string _currentEquippedWeaponId = "";

        private readonly Dictionary<string, Weapon3DMapping> _mappingsById = new Dictionary<string, Weapon3DMapping>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<WeaponData, Weapon3DMapping> _mappingsByData = new Dictionary<WeaponData, Weapon3DMapping>();

        private Vector3 _holderInitialLocalPosition;
        private Quaternion _holderInitialLocalRotation;
        private int _cachedWeaponLayer = -1;

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Se dispara al equipar e instanciar un nuevo modelo 3D de arma.
        /// </summary>
        public event Action<GameObject, WeaponData> OnWeaponEquipped;

        /// <summary>
        /// Se dispara al desequipar o limpiar el modelo 3D actual.
        /// </summary>
        public event Action OnWeaponUnequipped;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public GameObject CurrentSpawnedWeapon => _currentSpawnedWeaponInstance;
        public WeaponData CurrentEquippedWeaponData => _currentEquippedWeaponData;
        public string CurrentEquippedWeaponId => _currentEquippedWeaponId;
        public Transform WeaponHolder => _weaponHolder;
        public bool HasWeaponEquipped => _currentSpawnedWeaponInstance != null;
        public Vector3 DefaultWeaponScale => _defaultWeaponScale;
        public float GlobalScaleMultiplier => _globalScaleMultiplier;
        public IReadOnlyList<Weapon3DMapping> WeaponCatalog => _weaponCatalog;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            ResolverComponentesLocales();
            ReconstruirDiccionarios();
            AsegurarWeaponHolder();
            AsegurarCapaWeapon();
        }

        private void OnEnable()
        {
            SuscribirEventosInventario();
        }

        private void Start()
        {
            ResolverComponentesLocales();
            AsegurarWeaponHolder();

            // Si el inventario ya tiene un arma activa al iniciar, equipar su modelo 3D
            if (_playerInventory != null && _playerInventory.ActiveWeaponInstance != null)
            {
                EquipWeapon(_playerInventory.ActiveWeaponInstance.Data);
            }
        }

        private void Update()
        {
            if (_enableWeaponSway && _weaponHolder != null)
            {
                ActualizarWeaponSway();
            }
        }

        private void OnDisable()
        {
            DesuscribirEventosInventario();
        }

        private void OnValidate()
        {
            ReconstruirDiccionarios();
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE EQUIPAMIENTO E INSTANCIAMIENTO 3D GENERALIZADO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Equipa un arma mediante su ScriptableObject de WeaponData.
        /// </summary>
        public void EquipWeapon(WeaponData weaponData)
        {
            if (weaponData == null)
            {
                UnequipCurrentWeapon();
                return;
            }

            EquipWeaponExplicit(weaponData.WeaponID, weaponData, null);
        }

        /// <summary>
        /// Equipa un arma buscando su prefab por su identificador de texto.
        /// </summary>
        public void EquipWeapon(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId))
            {
                UnequipCurrentWeapon();
                return;
            }

            EquipWeaponExplicit(weaponId, null, null);
        }

        /// <summary>
        /// Equipa directamente un prefab asignado, asociándolo opcionalmente a un WeaponData o ID.
        /// </summary>
        public void EquipWeaponPrefab(GameObject prefab, WeaponData data = null, string weaponId = null)
        {
            if (prefab == null)
            {
                UnequipCurrentWeapon();
                return;
            }

            string resolvedId = !string.IsNullOrEmpty(weaponId) ? weaponId : (data != null && !string.IsNullOrEmpty(data.WeaponID) ? data.WeaponID : prefab.name);
            EquipWeaponExplicit(resolvedId, data, prefab);
        }

        /// <summary>
        /// Método nuclear y generalizado de equipamiento para todas las armas del juego:
        /// - Resuelve mapping o busca automáticamente el prefab en el proyecto.
        /// - Instancia limpio bajo WeaponHolder de la Main Camera.
        /// - Fuerza explícitamente su escala adecuada (evitando el colapso microscópico).
        /// - Neutraliza totalmente colisiones físicas (Colliders, Rigidbodies, Joints).
        /// - Asigna recursivamente la capa "Weapon" y sincroniza la mano 2D.
        /// </summary>
        public void EquipWeaponExplicit(string weaponId, WeaponData data, GameObject explicitPrefab)
        {
            if (_mappingsById.Count == 0 && _weaponCatalog.Count > 0)
            {
                ReconstruirDiccionarios();
            }

            // 1. Limpiar cualquier modelo 3D previo instanciado
            if (_currentSpawnedWeaponInstance != null)
            {
                Destroy(_currentSpawnedWeaponInstance);
                _currentSpawnedWeaponInstance = null;
            }

            // 2. Asegurar que el WeaponHolder esté anclado bajo la Main Camera activa
            Transform holder = AsegurarWeaponHolder();
            if (holder == null)
            {
                Debug.LogError("[PlayerWeaponEquipManager] No se pudo encontrar ni crear WeaponHolder bajo la cámara principal.");
                return;
            }

            // 3. Buscar correspondencia en el catálogo configurado (exacta, alias o flexible)
            Weapon3DMapping mapping = BuscarMapping(weaponId, data);

            // 4. Determinar prefab a instanciar (explícito > mapeado > búsqueda dinámica en proyecto)
            GameObject prefabToUse = explicitPrefab ?? mapping?.weaponPrefab;
            if (prefabToUse == null)
            {
                prefabToUse = BuscarPrefabEnProyecto(weaponId);
            }

            if (prefabToUse == null)
            {
                Debug.LogWarning($"[PlayerWeaponEquipManager] No se encontró prefab 3D para el arma identificada como '{weaponId}'. Sincronizando mano diegética.");
                SincronizarManoDiegetica(HandState.EquippedItem);
                return;
            }

            _currentEquippedWeaponData = data ?? mapping?.weaponData;
            _currentEquippedWeaponId = !string.IsNullOrEmpty(weaponId) ? weaponId : (mapping?.weaponId ?? prefabToUse.name);

            // 5. Instanciar limpiamente bajo WeaponHolder
            _currentSpawnedWeaponInstance = Instantiate(prefabToUse, holder);
            _currentSpawnedWeaponInstance.name = $"Weapon3D_{_currentEquippedWeaponId}";

            // 6. Restablecer transform: posición local, rotación y FUERZA EXPLÍCITA DE ESCALA
            Vector3 posOffset = (mapping != null && mapping.localPositionOffset != Vector3.zero) ? mapping.localPositionOffset : Vector3.zero;
            Vector3 rotOffset = (mapping != null && mapping.localRotationOffset != Vector3.zero) ? mapping.localRotationOffset : Vector3.zero;
            Vector3 finalScale = CalcularEscalaArma(prefabToUse, mapping);

            _currentSpawnedWeaponInstance.transform.SetParent(holder, false);
            _currentSpawnedWeaponInstance.transform.localPosition = posOffset;
            _currentSpawnedWeaponInstance.transform.localScale = finalScale;

            if (rotOffset != Vector3.zero)
            {
                _currentSpawnedWeaponInstance.transform.localRotation = Quaternion.Euler(rotOffset);
            }
            else
            {
                // Conservar la rotación nativa del prefab si no hay rotación de offset explícita
                _currentSpawnedWeaponInstance.transform.localRotation = prefabToUse.transform.localRotation;
            }

            // 7. Neutralizar colisiones físicas (Colliders, Rigidbodies, Joints) de forma total
            NeutralizarFisicasArma(_currentSpawnedWeaponInstance);

            // 8. Limpiar posibles scripts interactivos de suelo residuales en el viewmodel
            EliminarComponentesInteractivos(_currentSpawnedWeaponInstance);

            // 9. Asignar capa recursivamente a todos los hijos y garantizar cullingMask en la cámara
            AsegurarCapaWeapon();
            if (_cachedWeaponLayer >= 0)
            {
                AsignarCapaRecursiva(_currentSpawnedWeaponInstance, _cachedWeaponLayer);
            }

            // 10. Sincronizar mano 2D diegética al estado empuñado (EquippedItem)
            SincronizarManoDiegetica(HandState.EquippedItem);

            // 11. Notificar a observadores
            OnWeaponEquipped?.Invoke(_currentSpawnedWeaponInstance, _currentEquippedWeaponData);

            Debug.Log($"[PlayerWeaponEquipManager] Arma 3D '{_currentEquippedWeaponId}' equipada con éxito bajo '{holder.name}'. Escala forzada: {finalScale}, PosLocal: {posOffset}");
        }

        /// <summary>
        /// Calcula y fuerza la escala local adecuada del modelo 3D frente a la cámara para cualquier arma del juego,
        /// respetando la escala nativa de 100 de modelos exportados en centímetros y evitando el colapso a Vector3.one.
        /// </summary>
        public Vector3 CalcularEscalaArma(GameObject prefabToUse, Weapon3DMapping mapping)
        {
            Vector3 resolvedScale = _defaultWeaponScale; // (100, 100, 100) por defecto

            // 1. Si el prefab cuenta con una escala nativa configurada en el proyecto (ej. (100, 100, 100) en el prefab)
            if (_usePrefabNativeScale && prefabToUse != null)
            {
                Vector3 pScale = prefabToUse.transform.localScale;
                if (pScale.magnitude > _tinyScaleThreshold)
                {
                    resolvedScale = pScale;
                }
            }

            // 2. Si el mapping cuenta con una escala personalizada explícita (mayor al umbral microscópico)
            if (mapping != null && mapping.localScale != Vector3.zero)
            {
                if (mapping.localScale.magnitude > _tinyScaleThreshold)
                {
                    resolvedScale = mapping.localScale;
                }
            }

            // 3. Salvaguarda: si tras las evaluaciones la escala sigue siendo microscópica (<= threshold)
            if (resolvedScale.magnitude <= _tinyScaleThreshold)
            {
                resolvedScale = _defaultWeaponScale;
            }

            // 4. Aplicar multiplicadores
            if (mapping != null && mapping.scaleMultiplier > 0.001f && mapping.scaleMultiplier != 1f)
            {
                resolvedScale *= mapping.scaleMultiplier;
            }

            if (_globalScaleMultiplier > 0.001f && _globalScaleMultiplier != 1f)
            {
                resolvedScale *= _globalScaleMultiplier;
            }

            return resolvedScale;
        }

        /// <summary>
        /// Desequipa el arma activa, destruyendo su modelo 3D y notificando a la mano diegética.
        /// </summary>
        public void UnequipCurrentWeapon()
        {
            if (_currentSpawnedWeaponInstance != null)
            {
                Destroy(_currentSpawnedWeaponInstance);
                _currentSpawnedWeaponInstance = null;
            }

            _currentEquippedWeaponData = null;
            _currentEquippedWeaponId = "";

            SincronizarManoDiegetica(HandState.EquippedItem);
            OnWeaponUnequipped?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════════
        // RESOLUCIÓN ROBUSTA DE MAPPINGS, PREFABS Y ALIASES
        // ════════════════════════════════════════════════════════════════════

        private Weapon3DMapping BuscarMapping(string weaponId, WeaponData data)
        {
            Weapon3DMapping mapping = null;

            // 1. Coincidencia directa por objeto WeaponData
            if (data != null && _mappingsByData.TryGetValue(data, out mapping))
            {
                return mapping;
            }

            // 2. Coincidencia directa por ID exacto en el diccionario (incluyendo aliases registrados)
            if (!string.IsNullOrEmpty(weaponId) && _mappingsById.TryGetValue(weaponId, out mapping))
            {
                return mapping;
            }

            // 3. Búsqueda normalizada y flexible en el catálogo
            if (!string.IsNullOrEmpty(weaponId))
            {
                string normQuery = NormalizarTexto(weaponId);

                foreach (var item in _weaponCatalog)
                {
                    if (item == null) continue;

                    string normId = NormalizarTexto(item.weaponId);
                    if (!string.IsNullOrEmpty(normId) && (normId == normQuery || normId.Contains(normQuery) || normQuery.Contains(normId)))
                    {
                        return item;
                    }

                    if (item.weaponPrefab != null)
                    {
                        string normPrefabName = NormalizarTexto(item.weaponPrefab.name);
                        if (!string.IsNullOrEmpty(normPrefabName) && (normPrefabName == normQuery || normPrefabName.Contains(normQuery) || normQuery.Contains(normPrefabName)))
                        {
                            return item;
                        }
                    }

                    if (item.weaponData != null)
                    {
                        string normDataId = NormalizarTexto(item.weaponData.WeaponID);
                        string normDataName = NormalizarTexto(item.weaponData.DisplayName);
                        if ((!string.IsNullOrEmpty(normDataId) && (normDataId == normQuery || normDataId.Contains(normQuery) || normQuery.Contains(normDataId))) ||
                            (!string.IsNullOrEmpty(normDataName) && (normDataName == normQuery || normDataName.Contains(normQuery) || normQuery.Contains(normDataId))))
                        {
                            return item;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Busca de forma robusta y dinámica el prefab de cualquier arma en el proyecto (Editor y Runtime),
        /// empleando coincidencia exacta, normalizada, tokens y búsqueda de respaldo.
        /// </summary>
        public GameObject BuscarPrefabEnProyecto(string weaponId)
        {
            if (string.IsNullOrWhiteSpace(weaponId)) return null;

            string normalizedQuery = NormalizarTexto(weaponId);

#if UNITY_EDITOR
            // 1. Exploración exhaustiva en la carpeta de prefabs de armas
            string folderPath = "Assets/Prefabs/Entornos/Armas_Prefab";
            if (Directory.Exists(folderPath))
            {
                string[] files = Directory.GetFiles(folderPath, "*.prefab", SearchOption.AllDirectories);

                // Prioridad 1: Coincidencia exacta por nombre de archivo
                foreach (string file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (string.Equals(fileName, weaponId, StringComparison.OrdinalIgnoreCase))
                    {
                        var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
                        if (loaded != null) return loaded;
                    }
                }

                // Prioridad 2: Coincidencia normalizada y por contención de texto
                foreach (string file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string normFile = NormalizarTexto(fileName);
                    if (!string.IsNullOrEmpty(normFile) && (normFile == normalizedQuery || normFile.Contains(normalizedQuery) || normalizedQuery.Contains(normFile)))
                    {
                        var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
                        if (loaded != null) return loaded;
                    }
                }

                // Prioridad 3: Coincidencia de tokens individuales (ej. 'revolver' dentro de 'demencia revolver')
                string[] queryTokens = weaponId.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string normFile = NormalizarTexto(fileName);
                    foreach (string token in queryTokens)
                    {
                        string normToken = NormalizarTexto(token);
                        if (normToken.Length >= 3 && normFile.Contains(normToken))
                        {
                            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
                            if (loaded != null) return loaded;
                        }
                    }
                }
            }

            // 2. Búsqueda en todo el proyecto mediante AssetDatabase
            string[] guids = AssetDatabase.FindAssets($"{weaponId} t:Prefab", new[] { folderPath, "Assets/Prefabs" });
            if (guids != null && guids.Length > 0)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (loaded != null) return loaded;
            }

            // 3. Fallback a modelos FBX si no se ha generado prefab aún
            string fbxFolder = "Assets/Art/3D/SectorCero/Modelos_Armas";
            if (Directory.Exists(fbxFolder))
            {
                string[] fbxFiles = Directory.GetFiles(fbxFolder, "*.fbx", SearchOption.AllDirectories);
                foreach (string file in fbxFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string normFile = NormalizarTexto(fileName);
                    if (!string.IsNullOrEmpty(normFile) && (normFile == normalizedQuery || normFile.Contains(normalizedQuery) || normalizedQuery.Contains(normFile)))
                    {
                        var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
                        if (loaded != null) return loaded;
                    }
                }
            }
#endif

            // Fallback en builds finales mediante Resources
            GameObject resPrefab = Resources.Load<GameObject>($"Weapons/{weaponId}") ??
                                   Resources.Load<GameObject>($"Armas/{weaponId}") ??
                                   Resources.Load<GameObject>($"Prefabs/{weaponId}") ??
                                   Resources.Load<GameObject>(weaponId);
            return resPrefab;
        }

        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in texto.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // ════════════════════════════════════════════════════════════════════
        // ANCLAJE Y CONFIGURACIÓN DEL WEAPON HOLDER
        // ════════════════════════════════════════════════════════════════════

        public Transform AsegurarWeaponHolder()
        {
            Camera targetCam = ObtenerCamaraPrincipal();

            if (targetCam != null)
            {
                _mainCamera = targetCam;

                // Buscar WeaponHolder como hijo directo de la cámara
                Transform existing = targetCam.transform.Find("WeaponHolder");
                if (existing != null)
                {
                    _weaponHolder = existing;
                }
                else if (_weaponHolder != null && _weaponHolder.parent != targetCam.transform)
                {
                    // Si existía desvinculado o en otro transform, re-emparentarlo a la cámara activa
                    _weaponHolder.SetParent(targetCam.transform, false);
                    _weaponHolder.localPosition = _defaultHolderPosition;
                    _weaponHolder.localRotation = Quaternion.Euler(_defaultHolderRotation);
                    _weaponHolder.localScale = Vector3.one;
                }
                else if (_weaponHolder == null)
                {
                    GameObject holderGo = new GameObject("WeaponHolder");
                    holderGo.transform.SetParent(targetCam.transform, false);
                    holderGo.transform.localPosition = _defaultHolderPosition;
                    holderGo.transform.localRotation = Quaternion.Euler(_defaultHolderRotation);
                    holderGo.transform.localScale = Vector3.one;
                    _weaponHolder = holderGo.transform;
                }
            }
            else
            {
                // Fallback temporal bajo este GameObject si la cámara aún no está disponible
                if (_weaponHolder == null)
                {
                    Transform existing = transform.Find("WeaponHolder");
                    if (existing != null)
                    {
                        _weaponHolder = existing;
                    }
                    else
                    {
                        GameObject holderGo = new GameObject("WeaponHolder");
                        holderGo.transform.SetParent(transform, false);
                        holderGo.transform.localPosition = _defaultHolderPosition;
                        holderGo.transform.localRotation = Quaternion.Euler(_defaultHolderRotation);
                        _weaponHolder = holderGo.transform;
                    }
                }
            }

            if (_weaponHolder != null)
            {
                _holderInitialLocalPosition = _weaponHolder.localPosition;
                _holderInitialLocalRotation = _weaponHolder.localRotation;
            }

            return _weaponHolder;
        }

        private Camera ObtenerCamaraPrincipal()
        {
            if (_mainCamera != null && _mainCamera.isActiveAndEnabled) return _mainCamera;

            // 1. A través de PlayerManager
            if (_playerManager != null && _playerManager.PlayerCamera != null)
            {
                _mainCamera = _playerManager.PlayerCamera.GetComponent<Camera>();
                if (_mainCamera != null && _mainCamera.isActiveAndEnabled) return _mainCamera;
            }

            // 2. Camera.main nativa
            if (Camera.main != null && Camera.main.isActiveAndEnabled)
            {
                _mainCamera = Camera.main;
                return _mainCamera;
            }

            // 3. Buscar cámaras en la jerarquía del jugador o escena
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            foreach (var cam in cameras)
            {
                if (cam.CompareTag("MainCamera"))
                {
                    _mainCamera = cam;
                    return _mainCamera;
                }
            }

            if (cameras.Length > 0)
            {
                _mainCamera = cameras[0];
                return _mainCamera;
            }

            return null;
        }

        // ════════════════════════════════════════════════════════════════════
        // ASIGNACIÓN RECURSIVA DE CAPA Y FILTRADO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Asigna recursivamente la capa especificada a un GameObject y todos sus descendientes.
        /// </summary>
        public void AsignarCapaRecursiva(GameObject obj, int targetLayer)
        {
            if (obj == null || targetLayer < 0 || targetLayer > 31) return;

            obj.layer = targetLayer;

            int childCount = obj.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = obj.transform.GetChild(i);
                if (child != null)
                {
                    AsignarCapaRecursiva(child.gameObject, targetLayer);
                }
            }
        }

        private void AsegurarCapaWeapon()
        {
            Camera cam = ObtenerCamaraPrincipal();

            int layer = LayerMask.NameToLayer(_weaponLayerName);
            if (layer != -1)
            {
                _cachedWeaponLayer = layer;
            }
            else
            {
                _cachedWeaponLayer = (cam != null) ? cam.gameObject.layer : 0;
            }

            // Garantizar que la Main Camera renderice esta capa de forma inmediata
            if (_ensureCameraCullingMask && cam != null && _cachedWeaponLayer >= 0)
            {
                cam.cullingMask |= (1 << _cachedWeaponLayer);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // NEUTRALIZACIÓN COMPLETA DE FÍSICAS (COLLIDERS, RIGIDBODIES, JOINTS)
        // ════════════════════════════════════════════════════════════════════

        private void NeutralizarFisicasArma(GameObject weaponGo)
        {
            if (weaponGo == null) return;

            // 1. Desactivar y remover Joints primero para evitar advertencias nativas de física
            Joint[] joints = weaponGo.GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null) Destroy(joints[i]);
            }

            // 2. Desactivar y remover todos los colliders 3D para evitar interferencias con el CharacterController
            Collider[] colliders = weaponGo.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                    Destroy(colliders[i]);
                }
            }

            // 3. Desactivar y remover Rigidbodies 3D
            Rigidbody[] rbs = weaponGo.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                if (rbs[i] != null)
                {
                    rbs[i].isKinematic = true;
                    rbs[i].detectCollisions = false;
                    Destroy(rbs[i]);
                }
            }

            // 4. Salvaguarda adicional: limpiar físicas 2D si alguna estuviera presente
            Collider2D[] col2Ds = weaponGo.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < col2Ds.Length; i++)
            {
                if (col2Ds[i] != null)
                {
                    col2Ds[i].enabled = false;
                    Destroy(col2Ds[i]);
                }
            }

            Rigidbody2D[] rb2Ds = weaponGo.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < rb2Ds.Length; i++)
            {
                if (rb2Ds[i] != null)
                {
                    rb2Ds[i].simulated = false;
                    Destroy(rb2Ds[i]);
                }
            }
        }

        private void EliminarComponentesInteractivos(GameObject weaponGo)
        {
            if (weaponGo == null) return;

            // Eliminar cualquier componente de recogida en el suelo que viniera adherido en el prefab
            var pickups = weaponGo.GetComponentsInChildren<DemenciaNoLimite.Pickups.WorldItemPickup>(true);
            for (int i = 0; i < pickups.Length; i++)
            {
                if (pickups[i] != null)
                {
                    Destroy(pickups[i]);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ANIMACIÓN PROCEDIMENTAL DE INERCIA (WEAPON SWAY)
        // ════════════════════════════════════════════════════════════════════

        private void ActualizarWeaponSway()
        {
            if (_weaponHolder == null) return;

            float mouseX = Input.GetAxisRaw("Mouse X") * _swayAmount * -0.01f;
            float mouseY = Input.GetAxisRaw("Mouse Y") * _swayAmount * -0.01f;

            mouseX = Mathf.Clamp(mouseX, -_maxSwayAmount, _maxSwayAmount);
            mouseY = Mathf.Clamp(mouseY, -_maxSwayAmount, _maxSwayAmount);

            Vector3 targetPosition = _holderInitialLocalPosition + new Vector3(mouseX, mouseY, 0f);

            _weaponHolder.localPosition = Vector3.Lerp(
                _weaponHolder.localPosition,
                targetPosition,
                Time.deltaTime * _swaySmoothSpeed
            );
        }

        // ════════════════════════════════════════════════════════════════════
        // INTEGRACIÓN CON INVENTARIO Y MANO 2D DIEGÉTICA
        // ════════════════════════════════════════════════════════════════════

        private void SuscribirEventosInventario()
        {
            if (_playerInventory == null)
            {
                _playerInventory = GetComponentInParent<PlayerInventory>() ?? FindAnyObjectByType<PlayerInventory>();
            }

            if (_playerInventory != null)
            {
                _playerInventory.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
                _playerInventory.OnActiveWeaponChanged += HandleActiveWeaponChanged;
            }
        }

        private void DesuscribirEventosInventario()
        {
            if (_playerInventory != null)
            {
                _playerInventory.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
            }
        }

        private void HandleActiveWeaponChanged(WeaponSlotInstance activeSlot)
        {
            if (activeSlot != null && activeSlot.Data != null)
            {
                EquipWeaponExplicit(activeSlot.Data.WeaponID, activeSlot.Data, null);
            }
            else
            {
                UnequipCurrentWeapon();
            }
        }

        private void SincronizarManoDiegetica(HandState estadoDeseado)
        {
            if (_playerHandController == null)
            {
                _playerHandController = _playerManager?.HandController ?? GetComponentInChildren<PlayerHandController>() ?? FindAnyObjectByType<PlayerHandController>();
            }

            if (_playerHandController != null)
            {
                _playerHandController.SetHandState(estadoDeseado);
            }
        }

        private void ResolverComponentesLocales()
        {
            if (_playerManager == null)
            {
                _playerManager = PlayerManager.Instance ?? GetComponentInParent<PlayerManager>() ?? FindAnyObjectByType<PlayerManager>();
            }

            if (_playerInventory == null && _playerManager != null)
            {
                _playerInventory = _playerManager.Inventory;
            }

            if (_playerInventory == null)
            {
                _playerInventory = GetComponentInParent<PlayerInventory>() ?? FindAnyObjectByType<PlayerInventory>();
            }

            if (_playerHandController == null && _playerManager != null)
            {
                _playerHandController = _playerManager.HandController;
            }

            if (_playerHandController == null)
            {
                _playerHandController = GetComponentInChildren<PlayerHandController>() ?? FindAnyObjectByType<PlayerHandController>();
            }
        }

        private void ReconstruirDiccionarios()
        {
            _mappingsById.Clear();
            _mappingsByData.Clear();

#if UNITY_EDITOR
            AutoSincronizarCatalogoConPrefabs();
#endif

            if (_weaponCatalog == null) return;

            foreach (var item in _weaponCatalog)
            {
                if (item == null) continue;

                // Registrar ID principal
                if (!string.IsNullOrEmpty(item.weaponId))
                {
                    RegistrarEnDiccionarioId(item.weaponId, item);
                    GenerarYRegistrarAliases(item.weaponId, item);
                }

                // Registrar por nombre del prefab
                if (item.weaponPrefab != null)
                {
                    RegistrarEnDiccionarioId(item.weaponPrefab.name, item);
                    GenerarYRegistrarAliases(item.weaponPrefab.name, item);
                }

                // Registrar por WeaponData
                if (item.weaponData != null && !_mappingsByData.ContainsKey(item.weaponData))
                {
                    _mappingsByData.Add(item.weaponData, item);
                    if (!string.IsNullOrEmpty(item.weaponData.WeaponID))
                    {
                        RegistrarEnDiccionarioId(item.weaponData.WeaponID, item);
                        GenerarYRegistrarAliases(item.weaponData.WeaponID, item);
                    }
                }
            }
        }

        private void RegistrarEnDiccionarioId(string id, Weapon3DMapping mapping)
        {
            if (string.IsNullOrWhiteSpace(id) || mapping == null) return;
            string key = id.Trim();
            if (!_mappingsById.ContainsKey(key))
            {
                _mappingsById.Add(key, mapping);
            }
        }

        private void GenerarYRegistrarAliases(string baseId, Weapon3DMapping mapping)
        {
            if (string.IsNullOrWhiteSpace(baseId) || mapping == null) return;

            string lower = baseId.ToLowerInvariant().Trim();

            // Aliases para revólver
            if (lower.Contains("revolver") || lower.Contains("demencia"))
            {
                RegistrarEnDiccionarioId("revolver", mapping);
                RegistrarEnDiccionarioId("demencia revolver", mapping);
                RegistrarEnDiccionarioId("demencia_revolver", mapping);
                RegistrarEnDiccionarioId("demencia", mapping);
            }

            // Aliases para trabeca
            if (lower.Contains("trabeca"))
            {
                RegistrarEnDiccionarioId("trabeca", mapping);
                RegistrarEnDiccionarioId("Trabeca", mapping);
            }

            // Aliases para bingala
            if (lower.Contains("bingala"))
            {
                RegistrarEnDiccionarioId("bingala", mapping);
                RegistrarEnDiccionarioId("Bingala", mapping);
            }

            // Aliases para muak
            if (lower.Contains("muak"))
            {
                RegistrarEnDiccionarioId("muak", mapping);
                RegistrarEnDiccionarioId("Muak", mapping);
            }

            // Versión sin espacios
            string noSpaces = baseId.Replace(" ", "");
            if (!string.Equals(noSpaces, baseId, StringComparison.OrdinalIgnoreCase))
            {
                RegistrarEnDiccionarioId(noSpaces, mapping);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Escanea automáticamente la carpeta de prefabs de armas e integra cualquier arma faltante al catálogo.
        /// </summary>
        private void AutoSincronizarCatalogoConPrefabs()
        {
            string folderPath = "Assets/Prefabs/Entornos/Armas_Prefab";
            if (!Directory.Exists(folderPath)) return;

            string[] prefabPaths = Directory.GetFiles(folderPath, "*.prefab", SearchOption.TopDirectoryOnly);
            foreach (string path in prefabPaths)
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                GameObject loadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                if (loadedPrefab == null) continue;

                // Verificar si ya existe en el catálogo
                bool exists = false;
                foreach (var mapping in _weaponCatalog)
                {
                    if (mapping == null) continue;
                    if (mapping.weaponPrefab == loadedPrefab || string.Equals(mapping.weaponId, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        // Si el mapping tiene escala unitaria (1,1,1) o cero, asegurar escala 100
                        if (mapping.localScale == Vector3.one || mapping.localScale == Vector3.zero)
                        {
                            mapping.localScale = _defaultWeaponScale;
                        }
                        break;
                    }
                }

                if (!exists)
                {
                    var newMapping = new Weapon3DMapping
                    {
                        weaponId = fileName.ToLowerInvariant(),
                        weaponPrefab = loadedPrefab,
                        localPositionOffset = Vector3.zero,
                        localRotationOffset = Vector3.zero,
                        localScale = _defaultWeaponScale,
                        scaleMultiplier = 1f
                    };
                    _weaponCatalog.Add(newMapping);
                    Debug.Log($"[PlayerWeaponEquipManager] Auto-sincronizada arma en catálogo: '{fileName}' (Escala: {_defaultWeaponScale})");
                }
            }
        }
#endif

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS PÚBLICOS DE CONFIGURACIÓN DINÁMICA
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Permite registrar programáticamente un nuevo prefab 3D en tiempo de ejecución.
        /// </summary>
        public void RegistrarMapeoArma(string weaponId, GameObject prefab, Vector3 posOffset, Vector3 rotOffset, Vector3 scale, float scaleMultiplier = 1f)
        {
            if (string.IsNullOrEmpty(weaponId) || prefab == null) return;

            var mapping = new Weapon3DMapping
            {
                weaponId = weaponId,
                weaponPrefab = prefab,
                localPositionOffset = posOffset,
                localRotationOffset = rotOffset,
                localScale = (scale.magnitude > _tinyScaleThreshold) ? scale : _defaultWeaponScale,
                scaleMultiplier = scaleMultiplier
            };

            RegistrarEnDiccionarioId(weaponId, mapping);
            GenerarYRegistrarAliases(weaponId, mapping);
        }

        /// <summary>
        /// Ajusta dinámicamente la escala del arma actualmente equipada en pantalla.
        /// </summary>
        public void SetEquippedWeaponScale(Vector3 newScale)
        {
            if (_currentSpawnedWeaponInstance != null)
            {
                _currentSpawnedWeaponInstance.transform.localScale = newScale;
            }
        }

        /// <summary>
        /// Aplica un multiplicador dinámico de escala global y actualiza el arma activa.
        /// </summary>
        public void SetGlobalScaleMultiplier(float multiplier)
        {
            _globalScaleMultiplier = Mathf.Max(0.01f, multiplier);
            if (_currentSpawnedWeaponInstance != null)
            {
                Weapon3DMapping mapping = BuscarMapping(_currentEquippedWeaponId, _currentEquippedWeaponData);
                _currentSpawnedWeaponInstance.transform.localScale = CalcularEscalaArma(_currentSpawnedWeaponInstance, mapping);
            }
        }
    }
}
