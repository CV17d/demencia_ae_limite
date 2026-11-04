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
        // CAPA PROCEDIMENTAL DE BOBBING (PASOS) E INERCIA/SWAY (RATÓN)
        // ════════════════════════════════════════════════════════════════════

        [Header("Vaivén Procedimental por Pasos (Weapon Bobbing)")]
        [Tooltip("Activa el vaivén cíclico del WeaponHolder sincronizado con los pasos y velocidad del jugador.")]
        [SerializeField] private bool _enableWeaponBobbing = true;

        [Tooltip("Frecuencia base del balanceo al caminar.")]
        [SerializeField] private float _walkBobFrequency = 8.5f;

        [Tooltip("Multiplicador de frecuencia cuando el jugador corre (sprint).")]
        [SerializeField] private float _sprintBobMultiplier = 1.35f;

        [Tooltip("Amplitud del desplazamiento horizontal cíclico (eje local X).")]
        [SerializeField] private float _bobAmountX = 0.015f;

        [Tooltip("Amplitud del desplazamiento vertical cíclico (eje local Y).")]
        [SerializeField] private float _bobAmountY = 0.018f;

        [Tooltip("Rotación angular procedural de ladeo (Roll en eje Z) al dar pasos.")]
        [SerializeField] private float _bobTiltAngle = 1.2f;

        [Tooltip("Velocidad de suavizado del bobbing (retorno fluido libre de tirones).")]
        [SerializeField] private float _bobSmoothing = 10f;

        [Header("Respiración en Reposo (Idle Breathing)")]
        [Tooltip("Aplica una sutil oscilación senoidal cuando el jugador está detenido.")]
        [SerializeField] private bool _enableIdleBreathing = true;

        [Tooltip("Velocidad de la oscilación de respiración.")]
        [SerializeField] private float _idleBreathingSpeed = 1.8f;

        [Tooltip("Amplitud de la oscilación de respiración.")]
        [SerializeField] private float _idleBreathingAmount = 0.003f;

        [Header("Inercia y Sway por Entrada del Ratón (Mouse Sway)")]
        [Tooltip("Activa la inercia procedural del arma 3D en respuesta al movimiento del ratón.")]
        [SerializeField] private bool _enableWeaponSway = true;

        [Tooltip("Sensibilidad de desplazamiento posicional por inercia.")]
        [SerializeField] private float _swayAmount = 1.2f;

        [Tooltip("Límite máximo de desplazamiento posicional por inercia.")]
        [SerializeField] private float _maxSwayAmount = 0.04f;

        [Tooltip("Sensibilidad de rotación angular (Pitch / Yaw) por inercia del ratón.")]
        [SerializeField] private float _swayRotAmount = 2.5f;

        [Tooltip("Inclinación de ladeo (Roll) al girar la vista rápidamente.")]
        [SerializeField] private float _swayRollAmount = 3.5f;

        [Tooltip("Límite máximo de rotación angular por inercia en grados.")]
        [SerializeField] private float _maxSwayRotAngle = 6.0f;

        [Tooltip("Velocidad de suavizado del retorno del arma a su posición central.")]
        [SerializeField] private float _swaySmoothSpeed = 9f;

        // ════════════════════════════════════════════════════════════════════
        // COMPONENTES CACHEADOS
        // ════════════════════════════════════════════════════════════════════

        [Header("Referencias de Jugador (Auto-Resueltas)")]
        [SerializeField] private PlayerManager _playerManager;
        [SerializeField] private PlayerInventory _playerInventory;
        [SerializeField] private PlayerHandController _playerHandController;
        [SerializeField] private FirstPersonController _fpsController;
        [SerializeField] private CharacterController _characterController;
        [SerializeField] private WeaponCombatController _combatController;

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
        private bool _holderBaseTransformInitialized = false;
        private bool _hasExternalMotionComponent = false;
        private int _cachedWeaponLayer = -1;

        // Variables de Motion Procedural
        private float _bobTimer = 0f;
        private Vector3 _currentBobPosition = Vector3.zero;
        private Quaternion _currentBobRotation = Quaternion.identity;
        private Vector3 _currentSwayPosition = Vector3.zero;
        private Quaternion _currentSwayRotation = Quaternion.identity;

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
        public WeaponCombatController CombatController => _combatController;
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
            ActualizarMotionProcedural();
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

            // 7. Limpiar primero y de forma segura los scripts interactivos del viewmodel (elimina dependencias)
            EliminarComponentesInteractivos(_currentSpawnedWeaponInstance);

            // 8. Neutralizar colisiones físicas (Colliders, Rigidbodies, Joints) de forma total
            NeutralizarFisicasArma(_currentSpawnedWeaponInstance);

            // 9. Asignar capa recursivamente a todos los hijos y garantizar cullingMask en la cámara
            AsegurarCapaWeapon();
            if (_cachedWeaponLayer >= 0)
            {
                AsignarCapaRecursiva(_currentSpawnedWeaponInstance, _cachedWeaponLayer);
            }

            // 10. Sincronizar mano 2D diegética al estado empuñado (EquippedItem)
            SincronizarManoDiegetica(HandState.EquippedItem);

            // 11. Notificar a observadores y gestor de combate
            OnWeaponEquipped?.Invoke(_currentSpawnedWeaponInstance, _currentEquippedWeaponData);
            if (_combatController != null)
            {
                _combatController.HandleWeaponEquipped(_currentSpawnedWeaponInstance, _currentEquippedWeaponData);
            }

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
        /// a partir exclusivamente de todos los MeshRenderer y SkinnedMeshRenderer presentes.
        /// </summary>
        public static bool TryCalculateRenderBounds(GameObject rootGo, out Bounds combinedBounds)
        {
            combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            if (rootGo == null) return false;

            bool hasBounds = false;

            // 1. Evaluar MeshRenderer estándar de la geometría 3D
            MeshRenderer[] meshRenderers = rootGo.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < meshRenderers.Length; i++)
            {
                if (meshRenderers[i] == null) continue;

                if (!hasBounds)
                {
                    combinedBounds = meshRenderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(meshRenderers[i].bounds);
                }
            }

            // 2. Evaluar SkinnedMeshRenderer (para mallas con rigging o animadas)
            SkinnedMeshRenderer[] skinnedRenderers = rootGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                if (skinnedRenderers[i] == null) continue;

                if (!hasBounds)
                {
                    combinedBounds = skinnedRenderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(skinnedRenderers[i].bounds);
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
            if (_combatController != null)
            {
                _combatController.HandleWeaponUnequipped();
            }
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

            if (_weaponHolder != null && !_holderBaseTransformInitialized)
            {
                _holderInitialLocalPosition = _defaultHolderPosition;
                _holderInitialLocalRotation = Quaternion.Euler(_defaultHolderRotation);
                _holderBaseTransformInitialized = true;
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

            // 1. Desactivar y remover primero los scripts de interacción para eliminar dependencias RequireComponent
            EliminarComponentesInteractivos(weaponGo);

            // 2. Desactivar inmediatamente todos los colisionadores 3D para evitar interferencias físicas inmediatas
            Collider[] colliders = weaponGo.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                }
            }

            // 3. Desactivar y remover Rigidbodies 3D antes de eliminar colisionadores
            Rigidbody[] rbs = weaponGo.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                if (rbs[i] != null)
                {
                    rbs[i].isKinematic = true;
                    rbs[i].detectCollisions = false;
                    rbs[i].linearVelocity = Vector3.zero;
                    rbs[i].angularVelocity = Vector3.zero;
                    if (Application.isPlaying) Destroy(rbs[i]);
                    else DestroyImmediate(rbs[i]);
                }
            }

            // 4. Desactivar y remover Joints
            Joint[] joints = weaponGo.GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null)
                {
                    if (Application.isPlaying) Destroy(joints[i]);
                    else DestroyImmediate(joints[i]);
                }
            }

            // 5. Destruir de forma segura todos los colisionadores 3D
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    if (Application.isPlaying) Destroy(colliders[i]);
                    else DestroyImmediate(colliders[i]);
                }
            }

            // 6. Limpiar físicas 2D por salvaguarda
            Collider2D[] col2Ds = weaponGo.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < col2Ds.Length; i++)
            {
                if (col2Ds[i] != null)
                {
                    col2Ds[i].enabled = false;
                    if (Application.isPlaying) Destroy(col2Ds[i]);
                    else DestroyImmediate(col2Ds[i]);
                }
            }

            Rigidbody2D[] rb2Ds = weaponGo.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < rb2Ds.Length; i++)
            {
                if (rb2Ds[i] != null)
                {
                    rb2Ds[i].simulated = false;
                    if (Application.isPlaying) Destroy(rb2Ds[i]);
                    else DestroyImmediate(rb2Ds[i]);
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
                    pickups[i].enabled = false;
                    if (Application.isPlaying) Destroy(pickups[i]);
                    else DestroyImmediate(pickups[i]);
                }
            }

            var pickupBases = weaponGo.GetComponentsInChildren<DemenciaNoLimite.Pickups.PickupBase>(true);
            for (int i = 0; i < pickupBases.Length; i++)
            {
                if (pickupBases[i] != null)
                {
                    pickupBases[i].enabled = false;
                    if (Application.isPlaying) Destroy(pickupBases[i]);
                    else DestroyImmediate(pickupBases[i]);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ANIMACIÓN PROCEDIMENTAL DE VAIVÉN (BOBBING) E INERCIA (SWAY)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Actualiza la capa procedural acumulativa de vaivén de pasos (Bobbing),
        /// respiración en reposo e inercia de ratón (Sway) sin alterar los offsets
        /// locales individuales del arma equipada.
        /// </summary>
        private void ActualizarMotionProcedural()
        {
            if (_weaponHolder == null) return;

            // Si existe un script externo WeaponBobAndSway gestionando el holder, cederle el control
            if (_hasExternalMotionComponent) return;

            // 1. Calcular Vaivén Cíclico por Pasos (o Respiración en Reposo)
            CalcularBobbing(out Vector3 targetBobPos, out Quaternion targetBobRot);

            // 2. Calcular Inercia y Sway por Entrada del Ratón
            CalcularMouseSway(out Vector3 targetSwayPos, out Quaternion targetSwayRot);

            // 3. Suavizado de traslaciones mediante Vector3.Lerp libre de tirones
            _currentBobPosition = Vector3.Lerp(_currentBobPosition, targetBobPos, Time.deltaTime * _bobSmoothing);
            _currentSwayPosition = Vector3.Lerp(_currentSwayPosition, targetSwayPos, Time.deltaTime * _swaySmoothSpeed);

            // 4. Suavizado de rotaciones angulares mediante Quaternion.Slerp orgánico
            _currentBobRotation = Quaternion.Slerp(_currentBobRotation, targetBobRot, Time.deltaTime * _bobSmoothing);
            _currentSwayRotation = Quaternion.Slerp(_currentSwayRotation, targetSwayRot, Time.deltaTime * _swaySmoothSpeed);

            // 5. Aplicar como capa no destructiva sobre la posición y rotación base del WeaponHolder
            _weaponHolder.localPosition = _holderInitialLocalPosition + _currentBobPosition + _currentSwayPosition;
            _weaponHolder.localRotation = _holderInitialLocalRotation * _currentBobRotation * _currentSwayRotation;
        }

        /// <summary>
        /// Calcula el desplazamiento y rotación procedural basado en la velocidad horizontal
        /// del CharacterController (o FirstPersonController) y el ritmo de los pasos.
        /// </summary>
        private void CalcularBobbing(out Vector3 bobPos, out Quaternion bobRot)
        {
            bobPos = Vector3.zero;
            bobRot = Quaternion.identity;

            if (!_enableWeaponBobbing) return;

            // Obtener estado cinemático del jugador
            Vector3 velocity = Vector3.zero;
            bool isGrounded = true;
            bool isSprinting = false;

            if (_fpsController != null)
            {
                velocity = _fpsController.Velocity;
                isGrounded = _fpsController.IsGrounded;
                isSprinting = _fpsController.IsSprinting;
            }
            else if (_characterController != null)
            {
                velocity = _characterController.velocity;
                isGrounded = _characterController.isGrounded;
                isSprinting = Input.GetKey(KeyCode.LeftShift);
            }
            else
            {
                // Fallback por entradas de teclado en caso de no tener controlador físico enlazado
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                if (Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f)
                {
                    isSprinting = Input.GetKey(KeyCode.LeftShift);
                    velocity = new Vector3(h, 0f, v).normalized * (isSprinting ? 7f : 4.5f);
                }
            }

            Vector3 horizVelocity = new Vector3(velocity.x, 0f, velocity.z);
            float speed = horizVelocity.magnitude;

            // Si el jugador está sobre el suelo y moviéndose a velocidad perceptible (> 0.15 m/s)
            if (isGrounded && speed > 0.15f)
            {
                // Modulación de frecuencia en función del sprint (sincronizada a 8.5Hz - 11.5Hz con PlayerHandController)
                float freq = _walkBobFrequency * (isSprinting ? _sprintBobMultiplier : 1f);
                _bobTimer += Time.deltaTime * freq;

                // Modulación de amplitud por velocidad normalizada
                float speedFactor = Mathf.Clamp(speed / 4.5f, 0.35f, 1.5f);
                float sprintMult = isSprinting ? 1.25f : 1f;

                // Eje Local X: Oscilación lateral en medio ciclo (Mathf.Cos), alternando pie izquierdo y derecho
                float targetX = Mathf.Cos(_bobTimer * 0.5f) * _bobAmountX * speedFactor * sprintMult;

                // Eje Local Y: Oscilación vertical de doble frecuencia (Mathf.Sin), reflejando dos rebotes por zancada
                float sinY = Mathf.Sin(_bobTimer);
                float targetY = (sinY * 0.7f - Mathf.Abs(sinY) * 0.3f) * _bobAmountY * speedFactor * sprintMult;

                bobPos = new Vector3(targetX, targetY, 0f);

                // Ladeo angular sutil (Roll) en Z con cada paso
                float targetTiltZ = -Mathf.Cos(_bobTimer * 0.5f) * _bobTiltAngle * speedFactor;
                bobRot = Quaternion.Euler(0f, 0f, targetTiltZ);
            }
            else
            {
                // En reposo: respiración sutil senoidal (Idle Breathing)
                if (_enableIdleBreathing)
                {
                    float idleY = Mathf.Sin(Time.time * _idleBreathingSpeed) * _idleBreathingAmount;
                    float idleX = Mathf.Cos(Time.time * (_idleBreathingSpeed * 0.5f)) * (_idleBreathingAmount * 0.5f);
                    bobPos = new Vector3(idleX, idleY, 0f);
                }
            }
        }

        /// <summary>
        /// Calcula el retraso (lag) de posición y rotación por inercia en respuesta a la rotación de la cámara.
        /// </summary>
        private void CalcularMouseSway(out Vector3 swayPos, out Quaternion swayRot)
        {
            swayPos = Vector3.zero;
            swayRot = Quaternion.identity;

            if (!_enableWeaponSway) return;

            // Leer movimiento relativo de la vista del ratón
            float mouseX = Input.GetAxisRaw("Mouse X");
            float mouseY = Input.GetAxisRaw("Mouse Y");

            // Desplazamiento posicional inverso (lag físico de inercia)
            float moveMult = _swayAmount * -0.01f;
            float posX = Mathf.Clamp(mouseX * moveMult, -_maxSwayAmount, _maxSwayAmount);
            float posY = Mathf.Clamp(mouseY * moveMult, -_maxSwayAmount, _maxSwayAmount);
            swayPos = new Vector3(posX, posY, 0f);

            // Rotación angular de arrastre:
            // - Pitch (X): al mover la mirada verticalmente, la boca del cañón acompaña con retraso
            // - Yaw (Y): al girar horizontalmente, la culata acompaña con retraso
            // - Roll (Z): al girar rápido horizontalmente, el arma se ladea orgánicamente
            float rotPitch = Mathf.Clamp(mouseY * _swayRotAmount, -_maxSwayRotAngle, _maxSwayRotAngle);
            float rotYaw = Mathf.Clamp(-mouseX * _swayRotAmount, -_maxSwayRotAngle, _maxSwayRotAngle);
            float rotRoll = Mathf.Clamp(mouseX * _swayRollAmount, -_maxSwayRotAngle * 1.25f, _maxSwayRotAngle * 1.25f);

            swayRot = Quaternion.Euler(rotPitch, rotYaw, rotRoll);
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

            if (_fpsController == null && _playerManager != null)
            {
                _fpsController = _playerManager.Controller;
            }
            if (_fpsController == null)
            {
                _fpsController = GetComponentInParent<FirstPersonController>() ?? FindAnyObjectByType<FirstPersonController>();
            }

            if (_characterController == null && _fpsController != null)
            {
                _characterController = _fpsController.CharacterController;
            }
            if (_characterController == null && _playerManager != null)
            {
                _characterController = _playerManager.CharacterController;
            }
            if (_characterController == null)
            {
                _characterController = GetComponentInParent<CharacterController>() ?? FindAnyObjectByType<CharacterController>();
            }

            if (_combatController == null)
            {
                _combatController = GetComponent<WeaponCombatController>() ?? GetComponentInParent<WeaponCombatController>() ?? FindAnyObjectByType<WeaponCombatController>();
                if (_combatController == null)
                {
                    _combatController = gameObject.AddComponent<WeaponCombatController>();
                }
            }

            if (_weaponHolder != null)
            {
                _hasExternalMotionComponent = _weaponHolder.GetComponent<WeaponBobAndSway>() != null || GetComponent<WeaponBobAndSway>() != null;
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

        /// <summary>
        /// Activa o desactiva el vaivén por pasos (Bobbing) del WeaponHolder en tiempo de ejecución.
        /// </summary>
        public void SetWeaponBobbingEnabled(bool enabled) => _enableWeaponBobbing = enabled;

        /// <summary>
        /// Activa o desactiva la inercia del ratón (Sway) del WeaponHolder en tiempo de ejecución.
        /// </summary>
        public void SetWeaponSwayEnabled(bool enabled) => _enableWeaponSway = enabled;

        /// <summary>
        /// Calibra dinámicamente la amplitud del vaivén en X (horizontal) y Y (vertical).
        /// </summary>
        public void SetBobAmount(float amountX, float amountY)
        {
            _bobAmountX = Mathf.Max(0f, amountX);
            _bobAmountY = Mathf.Max(0f, amountY);
        }

        /// <summary>
        /// Restablece inmediatamente los acumuladores procedurales de movimiento a cero.
        /// </summary>
        public void ResetProceduralMotion()
        {
            _currentBobPosition = Vector3.zero;
            _currentBobRotation = Quaternion.identity;
            _currentSwayPosition = Vector3.zero;
            _currentSwayRotation = Quaternion.identity;
            _bobTimer = 0f;

            if (_weaponHolder != null)
            {
                _weaponHolder.localPosition = _holderInitialLocalPosition;
                _weaponHolder.localRotation = _holderInitialLocalRotation;
            }
        }
    }
}
