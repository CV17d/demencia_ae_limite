// ============================================================================
// PlayerWeaponEquipManager.cs — Gestor de Equipamiento e Instanciamiento de Armas 3D
// ============================================================================
// Coordina la visualización en primera persona de los modelos 3D de todas las armas:
// - Sistema inteligente de auto-normalización de tamaño basado en Mesh/Render Bounds.
// - Perfil de offset individual por arma (Posición, Rotación, Escala y Multiplicador).
// - Calibración persistente y previsualización en vivo en el Inspector de Unity.
// - Búsqueda automática y dinámica de prefabs en Assets/Prefabs/Entornos/Armas_Prefab/.
// - Neutralización total de físicas (Colliders, Rigidbodies, Joints) para evitar
//   colisiones o empujes sobre el CharacterController del jugador.
// - Asignación recursiva de la capa "Weapon" garantizando el cullingMask de la Main Camera.
// - Sincronización continua con PlayerHandController (HandState.EquippedItem) y PlayerInventory.
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
    /// Modo de auto-normalización de tamaño para modelos viewmodel en primera persona.
    /// </summary>
    public enum AutoNormalizationMode
    {
        [Tooltip("Normaliza automáticamente si el arma supera la dimensión máxima o no alcanza la mínima.")]
        NormalizeIfOutOfBounds,

        [Tooltip("Fuerza siempre el escalado para que la dimensión mayor coincida exactamente con targetVisualSize.")]
        AlwaysNormalize,

        [Tooltip("Desactiva la auto-normalización y utiliza exclusivamente los valores de escala manual.")]
        ManualOnly
    }

    /// <summary>
    /// Perfil de mapeo y offsets individuales para cada arma 3D en primera persona.
    /// </summary>
    [Serializable]
    public class Weapon3DMapping
    {
        [Header("Identificación")]
        [Tooltip("Identificador único del arma (ej. 'bingala', 'revolver', 'demencia revolver', 'muak', 'trabeca').")]
        public string weaponId;

        [Tooltip("ScriptableObject de WeaponData opcional asociado al arma.")]
        public WeaponData weaponData;

        [Tooltip("Prefab 3D que contiene el mesh, materiales y efectos visuales del arma.")]
        public GameObject weaponPrefab;

        [Header("Transform y Offsets Locales")]
        [Tooltip("Desplazamiento de posición local respecto al WeaponHolder.")]
        public Vector3 localPositionOffset = Vector3.zero;

        [Tooltip("Rotación local (Euler) respecto al WeaponHolder.")]
        public Vector3 localRotationOffset = Vector3.zero;

        [Header("Auto-Normalización y Escala")]
        [Tooltip("¿Activar auto-normalización basada en Mesh Bounds para este modelo?")]
        public bool useAutoBoundsNormalization = true;

        [Tooltip("Tamaño/longitud visual objetivo en metros frente a la cámara (ej. 0.30m para revólveres/pistolas, 0.48m para rifles). Si es 0, usa el estándar global.")]
        [Range(0f, 1.5f)]
        public float targetVisualSize = 0f;

        [Tooltip("Multiplicador de escala individual para afinar el tamaño de este modelo.")]
        [Range(0.01f, 10f)]
        public float scaleMultiplier = 1f;

        [Tooltip("Escala manual base (usada si la auto-normalización está desactivada o como fallback).")]
        public Vector3 localScale = new Vector3(100f, 100f, 100f);

        [Header("Alineación de Pivote")]
        [Tooltip("Compensa automáticamente el centro geométrico del mesh si el pivote del modelo 3D fue exportado descentrado.")]
        public bool autoCenterMeshPivot = false;

        [Tooltip("Desplazamiento adicional manual para compensar el pivote/empuñadura.")]
        public Vector3 pivotCompensationOffset = Vector3.zero;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Player/Player Weapon Equip Manager")]
    public class PlayerWeaponEquipManager : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CATÁLOGO Y REGISTRO DE ARMAS 3D
        // ════════════════════════════════════════════════════════════════════

        [Header("Catálogo de Armas 3D")]
        [Tooltip("Lista mapeada de armas y sus respectivos prefabs, escalas y perfiles de offset.")]
        [SerializeField] private List<Weapon3DMapping> _weaponCatalog = new List<Weapon3DMapping>();

        // ════════════════════════════════════════════════════════════════════
        // AUTO-NORMALIZACIÓN INTELIGENTE DE TAMAÑO (MESH BOUNDS)
        // ════════════════════════════════════════════════════════════════════

        [Header("Auto-Normalización Inteligente (Mesh Bounds)")]
        [Tooltip("Activa la normalización automática del tamaño de las armas en base a los límites físicos (Mesh Bounds) de su geometría.")]
        [SerializeField] private bool _enableAutoBoundsNormalization = true;

        [Tooltip("Criterio de aplicación de la normalización por límites geométricos.")]
        [SerializeField] private AutoNormalizationMode _normalizationMode = AutoNormalizationMode.NormalizeIfOutOfBounds;

        [Tooltip("Dimensión máxima estándar (en metros) para armas en primera persona (ej. 0.34m para handguns/compactas).")]
        [Range(0.15f, 1.2f)]
        [SerializeField] private float _standardViewmodelSize = 0.34f;

        [Tooltip("Dimensión mínima aceptable sin corrección (en metros). Si el modelo mide menos, se amplía automáticamente.")]
        [SerializeField] private float _minVisualSize = 0.20f;

        [Tooltip("Dimensión máxima aceptable sin corrección (en metros). Si el modelo mide más (ej. revólver a escala 100 mide > 1m), se reduce automáticamente.")]
        [SerializeField] private float _maxVisualSize = 0.55f;

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE ESCALA GLOBAL Y RESERVA
        // ════════════════════════════════════════════════════════════════════

        [Header("Escala Global y Parámetros de Reserva")]
        [Tooltip("Escala local de reserva para modelos FBX que no tengan normalización.")]
        [SerializeField] private Vector3 _defaultWeaponScale = new Vector3(100f, 100f, 100f);

        [Tooltip("Multiplicador global de escala aplicado a todas las armas equipadas.")]
        [Range(0.01f, 5f)]
        [SerializeField] private float _globalScaleMultiplier = 1f;

        [Tooltip("Respeta la escala nativa del prefab si está configurada en disco (ej. 100).")]
        [SerializeField] private bool _usePrefabNativeScale = true;

        [Tooltip("Umbral de escala mínima para detectar colapso unitario accidental.")]
        [SerializeField] private float _tinyScaleThreshold = 1.8f;

        // ════════════════════════════════════════════════════════════════════
        // ANCLAJE DIRECTO A CÁMARA PRINCIPAL
        // ════════════════════════════════════════════════════════════════════

        [Header("Anclaje a Cámara Principal")]
        [Tooltip("Cámara principal del jugador. Si es nula, se busca y resuelve automáticamente.")]
        [SerializeField] private Camera _mainCamera;

        [Tooltip("Transform contenedor donde se anclan los modelos 3D de las armas directamente como hijo de la Main Camera.")]
        [SerializeField] private Transform _weaponHolder;

        [Tooltip("Nombre de la capa para el modelo del arma (ej. 'Weapon' o 'Default').")]
        [SerializeField] private string _weaponLayerName = "Weapon";

        [Tooltip("Asegura automáticamente que la Main Camera incluya esta capa en su cullingMask.")]
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

        public event Action<GameObject, WeaponData> OnWeaponEquipped;
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
        public float StandardViewmodelSize => _standardViewmodelSize;
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

            // En tiempo de ejecución o edición en vivo, actualizar el arma activa si se ajustan sliders
            if (_currentSpawnedWeaponInstance != null)
            {
                Weapon3DMapping mapping = BuscarMapping(_currentEquippedWeaponId, _currentEquippedWeaponData);
                AplicarTransformYOffsets(_currentSpawnedWeaponInstance, mapping, null);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE EQUIPAMIENTO E INSTANCIAMIENTO 3D
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
        /// Método central de equipamiento:
        /// - Resuelve mapping o busca automáticamente el prefab en el proyecto.
        /// - Instancia bajo el WeaponHolder de la Main Camera.
        /// - Aplica el cálculo inteligente de escala (auto-normalización por Mesh Bounds y offsets individuales).
        /// - Neutraliza totalmente colisiones físicas (Colliders, Rigidbodies, Joints).
        /// - Asigna recursivamente la capa "Weapon" y sincroniza la mano 2D.
        /// </summary>
        public void EquipWeaponExplicit(string weaponId, WeaponData data, GameObject explicitPrefab)
        {
            if (_mappingsById.Count == 0 && _weaponCatalog.Count > 0)
            {
                ReconstruirDiccionarios();
            }

            // 1. Limpiar cualquier modelo 3D previo
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

            // 3. Buscar correspondencia en el catálogo configurado
            Weapon3DMapping mapping = BuscarMapping(weaponId, data);

            // 4. Determinar prefab a instanciar (explícito > mapeado > búsqueda dinámica en proyecto)
            GameObject prefabToUse = explicitPrefab ?? mapping?.weaponPrefab;
            if (prefabToUse == null)
            {
                prefabToUse = BuscarPrefabEnProyecto(weaponId);
            }

            if (prefabToUse == null)
            {
                Debug.LogWarning($"[PlayerWeaponEquipManager] No se encontró prefab 3D para el arma '{weaponId}'. Sincronizando mano diegética.");
                SincronizarManoDiegetica(HandState.EquippedItem);
                return;
            }

            _currentEquippedWeaponData = data ?? mapping?.weaponData;
            _currentEquippedWeaponId = !string.IsNullOrEmpty(weaponId) ? weaponId : (mapping?.weaponId ?? prefabToUse.name);

            // 5. Instanciar limpiamente bajo WeaponHolder
            _currentSpawnedWeaponInstance = Instantiate(prefabToUse, holder);
            _currentSpawnedWeaponInstance.name = $"Weapon3D_{_currentEquippedWeaponId}";

            // 6. Aplicar escala normalizada, posición y rotación relativas
            AplicarTransformYOffsets(_currentSpawnedWeaponInstance, mapping, prefabToUse);

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

            Debug.Log($"[PlayerWeaponEquipManager] Arma 3D '{_currentEquippedWeaponId}' equipada bajo '{holder.name}'. " +
                      $"Escala: {_currentSpawnedWeaponInstance.transform.localScale}, PosLocal: {_currentSpawnedWeaponInstance.transform.localPosition}");
        }

        /// <summary>
        /// Aplica la escala calculada, la posición local y la rotación según el perfil individual del arma.
        /// </summary>
        public void AplicarTransformYOffsets(GameObject weaponInstance, Weapon3DMapping mapping, GameObject prefabSource)
        {
            if (weaponInstance == null) return;

            Transform holder = AsegurarWeaponHolder();
            weaponInstance.transform.SetParent(holder, false);

            // 1. Calcular y forzar escala normalizada inteligente
            Vector3 finalScale = CalcularEscalaArma(weaponInstance, prefabSource, mapping);
            weaponInstance.transform.localScale = finalScale;

            // 2. Calcular posición local
            Vector3 posOffset = (mapping != null) ? mapping.localPositionOffset : Vector3.zero;

            // Compensar pivote si está habilitada la opción
            if (mapping != null && mapping.autoCenterMeshPivot)
            {
                if (TryCalculateRenderBounds(weaponInstance, out Bounds b))
                {
                    Vector3 localCenter = holder.InverseTransformPoint(b.center);
                    posOffset -= localCenter;
                }
            }

            if (mapping != null && mapping.pivotCompensationOffset != Vector3.zero)
            {
                posOffset += mapping.pivotCompensationOffset;
            }

            weaponInstance.transform.localPosition = posOffset;

            // 3. Calcular rotación local
            if (mapping != null && mapping.localRotationOffset != Vector3.zero)
            {
                weaponInstance.transform.localRotation = Quaternion.Euler(mapping.localRotationOffset);
            }
            else if (prefabSource != null)
            {
                weaponInstance.transform.localRotation = prefabSource.transform.localRotation;
            }
        }

        /// <summary>
        /// Calcula la escala óptima del arma empleando auto-normalización basada en Mesh/Render Bounds
        /// o respetando los ajustes manuales si están configurados.
        /// </summary>
        public Vector3 CalcularEscalaArma(GameObject weaponInstance, GameObject prefabSource, Weapon3DMapping mapping)
        {
            // 1. Escala base tentativa
            Vector3 baseScale = _defaultWeaponScale;

            if (mapping != null && mapping.localScale != Vector3.zero)
            {
                baseScale = mapping.localScale;
            }
            else if (_usePrefabNativeScale && prefabSource != null && prefabSource.transform.localScale.magnitude > _tinyScaleThreshold)
            {
                baseScale = prefabSource.transform.localScale;
            }

            // Aplicar escala tentativa temporal al objeto para medir sus Bounds reales
            if (weaponInstance != null)
            {
                weaponInstance.transform.localScale = baseScale;
            }

            // 2. Verificar auto-normalización por Mesh Bounds
            bool autoNormalize = _enableAutoBoundsNormalization && (mapping == null || mapping.useAutoBoundsNormalization);

            if (autoNormalize && weaponInstance != null && _normalizationMode != AutoNormalizationMode.ManualOnly)
            {
                if (TryCalculateRenderBounds(weaponInstance, out Bounds renderBounds))
                {
                    float currentMaxDimension = Mathf.Max(renderBounds.size.x, renderBounds.size.y, renderBounds.size.z);

                    if (currentMaxDimension > 0.001f)
                    {
                        float targetSize = (mapping != null && mapping.targetVisualSize > 0.01f)
                            ? mapping.targetVisualSize
                            : _standardViewmodelSize;

                        bool isOutOfBounds = (currentMaxDimension < _minVisualSize || currentMaxDimension > _maxVisualSize);
                        bool shouldScale = (_normalizationMode == AutoNormalizationMode.AlwaysNormalize) ||
                                           (_normalizationMode == AutoNormalizationMode.NormalizeIfOutOfBounds && isOutOfBounds);

                        if (shouldScale)
                        {
                            float factor = targetSize / currentMaxDimension;
                            baseScale *= factor;

                            Debug.Log($"[PlayerWeaponEquipManager] Auto-normalización de Bounds para '{weaponInstance.name}': " +
                                      $"Dimensión Actual = {currentMaxDimension:F2}m -> Objetivo = {targetSize:F2}m (Factor: {factor:F3})");
                        }
                    }
                }
            }

            // 3. Aplicar multiplicadores
            float individualMultiplier = (mapping != null && mapping.scaleMultiplier > 0.001f) ? mapping.scaleMultiplier : 1f;
            Vector3 finalScale = baseScale * (individualMultiplier * _globalScaleMultiplier);

            // Salvaguarda contra escalas colapsadas
            if (finalScale.x <= 0.0001f || finalScale.y <= 0.0001f || finalScale.z <= 0.0001f)
            {
                finalScale = _defaultWeaponScale;
            }

            return finalScale;
        }

        /// <summary>
        /// Sobrecarga compatible hacia atrás para cálculo de escala.
        /// </summary>
        public Vector3 CalcularEscalaArma(GameObject prefabToUse, Weapon3DMapping mapping)
        {
            return CalcularEscalaArma(prefabToUse, prefabToUse, mapping);
        }

        /// <summary>
        /// Calcula los límites de renderizado combinados (Render Bounds) en el espacio de la escena
        /// a partir de todos los MeshRenderer y SkinnedMeshRenderer presentes.
        /// </summary>
        public static bool TryCalculateRenderBounds(GameObject rootGo, out Bounds combinedBounds)
        {
            combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            if (rootGo == null) return false;

            Renderer[] renderers = rootGo.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer || renderers[i] is TrailRenderer) continue;

                if (!hasBounds)
                {
                    combinedBounds = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(renderers[i].bounds);
                }
            }

            return hasBounds;
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

            // 2. Coincidencia directa por ID exacto en el diccionario (incluyendo aliases)
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
        /// Busca de forma robusta y dinámica el prefab de cualquier arma en el proyecto.
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

                // Prioridad 3: Coincidencia de tokens individuales
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

                Transform existing = targetCam.transform.Find("WeaponHolder");
                if (existing != null)
                {
                    _weaponHolder = existing;
                }
                else if (_weaponHolder != null && _weaponHolder.parent != targetCam.transform)
                {
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

            if (_playerManager != null && _playerManager.PlayerCamera != null)
            {
                _mainCamera = _playerManager.PlayerCamera.GetComponent<Camera>();
                if (_mainCamera != null && _mainCamera.isActiveAndEnabled) return _mainCamera;
            }

            if (Camera.main != null && Camera.main.isActiveAndEnabled)
            {
                _mainCamera = Camera.main;
                return _mainCamera;
            }

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

            // 1. Desactivar y remover Joints primero
            Joint[] joints = weaponGo.GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null) Destroy(joints[i]);
            }

            // 2. Desactivar y remover todos los colliders 3D
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

            // 4. Limpiar físicas 2D por salvaguarda
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

                bool exists = false;
                foreach (var mapping in _weaponCatalog)
                {
                    if (mapping == null) continue;
                    if (mapping.weaponPrefab == loadedPrefab || string.Equals(mapping.weaponId, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    float targetSize = 0.34f;
                    string lowerName = fileName.ToLowerInvariant();
                    if (lowerName.Contains("revolver")) targetSize = 0.30f;
                    else if (lowerName.Contains("bingala")) targetSize = 0.32f;
                    else if (lowerName.Contains("muak")) targetSize = 0.36f;
                    else if (lowerName.Contains("trabeca")) targetSize = 0.48f;

                    var newMapping = new Weapon3DMapping
                    {
                        weaponId = fileName.ToLowerInvariant(),
                        weaponPrefab = loadedPrefab,
                        localPositionOffset = Vector3.zero,
                        localRotationOffset = Vector3.zero,
                        useAutoBoundsNormalization = true,
                        targetVisualSize = targetSize,
                        scaleMultiplier = 1f,
                        localScale = _defaultWeaponScale
                    };
                    _weaponCatalog.Add(newMapping);
                    Debug.Log($"[PlayerWeaponEquipManager] Auto-sincronizada arma en catálogo: '{fileName}' (Tamaño Objetivo: {targetSize:F2}m)");
                }
            }
        }

        /// <summary>
        /// Context Menu para guardar la posición, rotación y escala actuales del arma en el catálogo.
        /// </summary>
        [ContextMenu("Guardar Transform del Arma Actual en el Catálogo")]
        public void GuardarTransformActualEnMapping()
        {
            if (_currentSpawnedWeaponInstance == null)
            {
                Debug.LogWarning("[PlayerWeaponEquipManager] No hay ningún arma instanciada actualmente para guardar.");
                return;
            }

            Weapon3DMapping mapping = BuscarMapping(_currentEquippedWeaponId, _currentEquippedWeaponData);
            if (mapping != null)
            {
                mapping.localPositionOffset = _currentSpawnedWeaponInstance.transform.localPosition;
                mapping.localRotationOffset = _currentSpawnedWeaponInstance.transform.localRotation.eulerAngles;
                mapping.localScale = _currentSpawnedWeaponInstance.transform.localScale;
                EditorUtility.SetDirty(this);
                Debug.Log($"[PlayerWeaponEquipManager] Perfil de '{_currentEquippedWeaponId}' guardado con éxito: " +
                          $"Pos={mapping.localPositionOffset}, Rot={mapping.localRotationOffset}, Escala={mapping.localScale}");
            }
        }

        /// <summary>
        /// Context Menu para forzar la recalibración por Bounds del arma equipada.
        /// </summary>
        [ContextMenu("Recalcular Auto-Normalización del Arma Actual")]
        public void RecalcularAutoNormalizacionArmaActual()
        {
            if (_currentSpawnedWeaponInstance == null) return;
            Weapon3DMapping mapping = BuscarMapping(_currentEquippedWeaponId, _currentEquippedWeaponData);
            AplicarTransformYOffsets(_currentSpawnedWeaponInstance, mapping, null);
        }
#endif

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS PÚBLICOS DE CONFIGURACIÓN DINÁMICA
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Permite registrar programáticamente un nuevo prefab 3D en tiempo de ejecución.
        /// </summary>
        public void RegistrarMapeoArma(string weaponId, GameObject prefab, Vector3 posOffset, Vector3 rotOffset, Vector3 scale, float scaleMultiplier = 1f, float targetSize = 0f)
        {
            if (string.IsNullOrEmpty(weaponId) || prefab == null) return;

            var mapping = new Weapon3DMapping
            {
                weaponId = weaponId,
                weaponPrefab = prefab,
                localPositionOffset = posOffset,
                localRotationOffset = rotOffset,
                localScale = (scale.magnitude > _tinyScaleThreshold) ? scale : _defaultWeaponScale,
                scaleMultiplier = scaleMultiplier,
                targetVisualSize = targetSize,
                useAutoBoundsNormalization = true
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
                AplicarTransformYOffsets(_currentSpawnedWeaponInstance, mapping, null);
            }
        }

        /// <summary>
        /// Modifica el tamaño estándar de viewmodel y recalibra el arma activa.
        /// </summary>
        public void SetStandardViewmodelSize(float newSize)
        {
            _standardViewmodelSize = Mathf.Clamp(newSize, 0.1f, 1.5f);
            if (_currentSpawnedWeaponInstance != null)
            {
                Weapon3DMapping mapping = BuscarMapping(_currentEquippedWeaponId, _currentEquippedWeaponData);
                AplicarTransformYOffsets(_currentSpawnedWeaponInstance, mapping, null);
            }
        }
    }
}
