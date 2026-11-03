// ============================================================================
// PlayerWeaponEquipManager.cs — Gestor de Equipamiento e Instanciamiento de Armas 3D
// ============================================================================
// Coordina la visualización en primera persona de los modelos 3D de las armas:
// - Instancia y destruye prefabs 3D en el punto de anclaje (WeaponHolder).
// - Asigna recursivamente la capa "Weapon" para filtrado de cámaras secundarias.
// - Sincroniza el estado de la mano 2D diegética (PlayerHandController -> HandState.EquippedItem).
// - Escucha automáticamente los cambios de ranura de PlayerInventory.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core.Data;

#pragma warning disable CS0649

namespace DemenciaNoLimite.Player
{
    /// <summary>
    /// Mapeo serializable que asocia un identificador o WeaponData con su prefab 3D en el mundo.
    /// </summary>
    [Serializable]
    public class Weapon3DMapping
    {
        [Tooltip("Identificador único del arma (ej. 'Pistola9mm', 'EscopetaTrinchera').")]
        public string weaponId;

        [Tooltip("ScriptableObject de WeaponData opcional asociado al arma.")]
        public WeaponData weaponData;

        [Tooltip("Prefab 3D que contiene el mesh, materiales y efectos visuales del arma en primera persona.")]
        public GameObject weaponPrefab;

        [Tooltip("Desplazamiento local respecto al WeaponHolder.")]
        public Vector3 localPositionOffset = Vector3.zero;

        [Tooltip("Rotación local (Euler) respecto al WeaponHolder.")]
        public Vector3 localRotationOffset = Vector3.zero;

        [Tooltip("Escala local del modelo 3D.")]
        public Vector3 localScale = Vector3.one;
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
        // ANCLAJE DIRECTO A CÁMARA PRINCIPAL (CERO DEPENDENCIAS SECUNDARIAS)
        // ════════════════════════════════════════════════════════════════════

        [Header("Anclaje a Cámara Principal")]
        [Tooltip("Cámara principal del jugador (Main Camera / PlayerCamera). Si es nula, se busca y resuelve automáticamente.")]
        [SerializeField] private Camera _mainCamera;

        [Tooltip("Transform contenedor donde se anclan los modelos 3D de las armas directamente como hijo de la Main Camera.")]
        [SerializeField] private Transform _weaponHolder;

        [Tooltip("Nombre de la capa para el modelo del arma (ej. 'Default' o 'Weapon').")]
        [SerializeField] private string _weaponLayerName = "Default";

        [Tooltip("Asegura automáticamente que la Main Camera incluya esta capa en su cullingMask para que sea visible en pantalla.")]
        [SerializeField] private bool _ensureCameraCullingMask = true;

        [Tooltip("Posición local por defecto del WeaponHolder frente a la vista de la cámara.")]
        [SerializeField] private Vector3 _defaultHolderPosition = new Vector3(0.24f, -0.20f, 0.45f);

        [Tooltip("Rotación local por defecto del WeaponHolder.")]
        [SerializeField] private Vector3 _defaultHolderRotation = Vector3.zero;

        // ════════════════════════════════════════════════════════════════════
        // INTEGRACIÓN DIEGÉTICA Y ANIMACIÓN PROCEDIMENTAL (SWAY / BOB)
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
        // GESTIÓN DE EQUIPAMIENTO E INSTANCIAMIENTO 3D
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Equipa un arma mediante su ScriptableObject de WeaponData.
        /// Instancia su modelo 3D, asigna la capa Weapon y sincroniza la mano 2D.
        /// </summary>
        /// <param name="weaponData">Datos del arma a instanciar.</param>
        public void EquipWeapon(WeaponData weaponData)
        {
            if (weaponData == null)
            {
                UnequipCurrentWeapon();
                return;
            }

            // Buscar mapeo por WeaponData o por WeaponID
            Weapon3DMapping mapping = null;
            if (!_mappingsByData.TryGetValue(weaponData, out mapping))
            {
                if (!string.IsNullOrEmpty(weaponData.WeaponID))
                {
                    _mappingsById.TryGetValue(weaponData.WeaponID, out mapping);
                }
            }

            InstanciarModeloArma(mapping, weaponData, weaponData.WeaponID);
        }

        /// <summary>
        /// Equipa un arma buscando su prefab por su identificador de texto.
        /// </summary>
        /// <param name="weaponId">Identificador configurado en el catálogo.</param>
        public void EquipWeapon(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId))
            {
                UnequipCurrentWeapon();
                return;
            }

            _mappingsById.TryGetValue(weaponId, out Weapon3DMapping mapping);
            InstanciarModeloArma(mapping, mapping?.weaponData, weaponId);
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

            // Notificar a la mano diegética para mantener el estado de descanso
            SincronizarManoDiegetica(HandState.EquippedItem);

            OnWeaponUnequipped?.Invoke();
        }

        private void InstanciarModeloArma(Weapon3DMapping mapping, WeaponData data, string weaponId)
        {
            // Limpiar arma previa si existía
            if (_currentSpawnedWeaponInstance != null)
            {
                Destroy(_currentSpawnedWeaponInstance);
                _currentSpawnedWeaponInstance = null;
            }

            AsegurarWeaponHolder();

            _currentEquippedWeaponData = data;
            _currentEquippedWeaponId = weaponId;

            GameObject prefabToUse = mapping?.weaponPrefab;
            Vector3 posOffset = (mapping != null) ? mapping.localPositionOffset : Vector3.zero;
            Vector3 rotOffset = (mapping != null) ? mapping.localRotationOffset : Vector3.zero;
            Vector3 scale = (mapping != null && mapping.localScale != Vector3.zero) ? mapping.localScale : Vector3.one;

            // Fallback automático por AssetDatabase (Editor) o Resources si el arma no fue mapeada en el Inspector
            if (prefabToUse == null && !string.IsNullOrEmpty(weaponId))
            {
#if UNITY_EDITOR
                string[] searchFolders = new[] { "Assets/Prefabs/Entornos/Armas_Prefab" };
                string[] guids = UnityEditor.AssetDatabase.FindAssets($"{weaponId} t:Prefab", searchFolders);
                if (guids != null && guids.Length > 0)
                {
                    string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    prefabToUse = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                }
#endif
                if (prefabToUse == null)
                {
                    prefabToUse = Resources.Load<GameObject>($"Weapons/{weaponId}") ??
                                  Resources.Load<GameObject>($"Armas/{weaponId}") ??
                                  Resources.Load<GameObject>(weaponId);
                }
            }

            // Si se cuenta con un prefab 3D válido
            if (prefabToUse != null && _weaponHolder != null)
            {
                _currentSpawnedWeaponInstance = Instantiate(prefabToUse, _weaponHolder);
                _currentSpawnedWeaponInstance.name = $"Weapon3D_{weaponId}";

                // Configurar transform relativo
                _currentSpawnedWeaponInstance.transform.localPosition = posOffset;
                _currentSpawnedWeaponInstance.transform.localRotation = Quaternion.Euler(rotOffset);
                _currentSpawnedWeaponInstance.transform.localScale = scale;

                // Asignar capa de forma recursiva a todos los hijos y garantizar cullingMask en la Main Camera
                AsegurarCapaWeapon();
                if (_cachedWeaponLayer != -1)
                {
                    AsignarCapaRecursiva(_currentSpawnedWeaponInstance, _cachedWeaponLayer);
                }

                // Desactivar colisiones físicas en el modelo del arma para no interferir con el CharacterController
                NeutralizarFisicasArma(_currentSpawnedWeaponInstance);
            }

            // Sincronizar mano diegética 2D al estado empuñado (EquippedItem)
            SincronizarManoDiegetica(HandState.EquippedItem);

            OnWeaponEquipped?.Invoke(_currentSpawnedWeaponInstance, data);
        }

        // ════════════════════════════════════════════════════════════════════
        // ASIGNACIÓN RECURSIVA DE CAPA (WEAPON LAYER)
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
                // Fallback automático: usar la capa de la Main Camera o Default (0)
                _cachedWeaponLayer = (cam != null) ? cam.gameObject.layer : 0;
            }

            // Garantizar que la Main Camera renderice esta capa de forma inmediata
            if (_ensureCameraCullingMask && cam != null && _cachedWeaponLayer >= 0)
            {
                cam.cullingMask |= (1 << _cachedWeaponLayer);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ANCLAJE Y CONFIGURACIÓN DEL WEAPON HOLDER
        // ════════════════════════════════════════════════════════════════════

        private void AsegurarWeaponHolder()
        {
            if (_weaponHolder != null) return;

            // 1. Obtener directamente la cámara principal del jugador
            Camera targetCam = ObtenerCamaraPrincipal();

            if (targetCam != null)
            {
                _mainCamera = targetCam;

                Transform existing = targetCam.transform.Find("WeaponHolder");
                if (existing != null)
                {
                    _weaponHolder = existing;
                }
                else
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
                // Si no hay cámara disponible de inmediato, crear un contenedor bajo este GameObject
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

            if (_weaponHolder != null)
            {
                _holderInitialLocalPosition = _weaponHolder.localPosition;
                _holderInitialLocalRotation = _weaponHolder.localRotation;
            }
        }

        private Camera ObtenerCamaraPrincipal()
        {
            if (_mainCamera != null) return _mainCamera;

            // 1. A través de PlayerManager
            if (_playerManager != null && _playerManager.PlayerCamera != null)
            {
                _mainCamera = _playerManager.PlayerCamera.GetComponent<Camera>();
                if (_mainCamera != null) return _mainCamera;
            }

            // 2. Camera.main directa
            _mainCamera = Camera.main;
            if (_mainCamera != null) return _mainCamera;

            // 3. Buscar componente Camera en el jugador o escena
            _mainCamera = GetComponentInChildren<Camera>() ?? FindAnyObjectByType<Camera>();
            return _mainCamera;
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
                EquipWeapon(activeSlot.Data);
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

        private void NeutralizarFisicasArma(GameObject weaponGo)
        {
            if (weaponGo == null) return;

            Collider[] colliders = weaponGo.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Rigidbody[] rbs = weaponGo.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                rbs[i].isKinematic = true;
                rbs[i].detectCollisions = false;
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

            if (_weaponCatalog == null) return;

            foreach (var item in _weaponCatalog)
            {
                if (item == null) continue;

                if (!string.IsNullOrEmpty(item.weaponId) && !_mappingsById.ContainsKey(item.weaponId))
                {
                    _mappingsById.Add(item.weaponId, item);
                }

                if (item.weaponData != null && !_mappingsByData.ContainsKey(item.weaponData))
                {
                    _mappingsByData.Add(item.weaponData, item);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS PÚBLICOS DE CONFIGURACIÓN DINÁMICA
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Permite registrar programáticamente un nuevo prefab 3D en tiempo de ejecución.
        /// </summary>
        public void RegistrarMapeoArma(string weaponId, GameObject prefab, Vector3 posOffset, Vector3 rotOffset, Vector3 scale)
        {
            if (string.IsNullOrEmpty(weaponId) || prefab == null) return;

            var mapping = new Weapon3DMapping
            {
                weaponId = weaponId,
                weaponPrefab = prefab,
                localPositionOffset = posOffset,
                localRotationOffset = rotOffset,
                localScale = scale
            };

            _mappingsById[weaponId] = mapping;
        }
    }
}
