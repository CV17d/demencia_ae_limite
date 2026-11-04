// ============================================================================
// WeaponCombatController.cs — Sistema Modular de Combate, Balística y HUD (Unity 6)
// ============================================================================
// Implementa la mecánica completa de combate en primera persona:
// 1. Disparo por Raycast: Originado en el centro exacto de la Main Camera
//    (ViewportToWorldPoint(0.5, 0.5, 0)), respetando cadencia (fireRate), alcance (100m)
//    y aplicando daño vía IDamageable / IKnockbackable con registro en consola.
// 2. Sistema de Munición y Recarga: Gestión persistente de cargador (currentAmmo / maxAmmo)
//    por arma, bloqueo de fuego durante recarga y activación por [R] o cargador vacío.
// 3. Destello de Luz (Muzzle Flash): Creación o localización dinámica de FirePoint en la
//    boca del cañón del modelo 3D equipado con luz efímera (0.05s, LightType.Point, color
//    cálido/naranja e intensidad aleatoria).
// 4. Enlace con el HUD: Sincronización en tiempo real del indicador "BALAS: currentAmmo / maxAmmo"
//    a través de HUDController, PlayerInventory y overlay de reserva.
// 5. Compatibilidad Estructural: Totalmente compatible con bingala, demencia revolver,
//    muak, trabeca y WeaponBobAndSway sin interferir con el vaivén procedimental.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Combat;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.UI;

#pragma warning disable CS0649

namespace DemenciaNoLimite.Player
{
    /// <summary>
    /// Configuración de estadísticas de combate y balística por arma.
    /// </summary>
    [Serializable]
    public class WeaponCombatConfig
    {
        [Tooltip("Identificador único del arma (ej. 'bingala', 'revolver', 'demencia revolver', 'muak', 'trabeca', 'canoduplo').")]
        public string weaponId = "default";

        [Tooltip("¿El arma es automática (fuego continuo mientras se mantenga presionado el gatillo) o semiautomática?")]
        public bool isAutomatic = false;

        [Tooltip("Cadencia de fuego en disparos por segundo (disparos/seg).")]
        [Range(0.1f, 30f)]
        public float fireRate = 2.5f;

        [Tooltip("Capacidad máxima del cargador (balas antes de requerir recarga).")]
        [Range(1, 150)]
        public int maxAmmo = 12;

        [Tooltip("Tiempo de recarga en segundos.")]
        [Range(0.2f, 6.0f)]
        public float reloadTime = 1.6f;

        [Tooltip("Daño base infligido al impactar un objetivo con IDamageable.")]
        [Range(1f, 250f)]
        public float damage = 30f;

        [Tooltip("Fuerza de retroceso o impulso aplicado a entidades IKnockbackable.")]
        [Range(0f, 50f)]
        public float knockbackForce = 6f;

        [Tooltip("Alcance máximo del proyectil/raycast en metros.")]
        [Range(10f, 300f)]
        public float weaponRange = 100f;

        [Tooltip("Desplazamiento local adicional opcional para calibrar la posición del FirePoint en el cañón.")]
        public Vector3 firePointOffset = Vector3.zero;

        public WeaponCombatConfig() { }

        public WeaponCombatConfig(string id, bool auto, float rate, int ammo, float reload, float dmg, float knock, float range)
        {
            weaponId = id;
            isAutomatic = auto;
            fireRate = rate;
            maxAmmo = ammo;
            reloadTime = reload;
            damage = dmg;
            knockbackForce = knock;
            weaponRange = range;
            firePointOffset = Vector3.zero;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Demencia No Limite/Player/Weapon Combat Controller")]
    public class WeaponCombatController : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE BALÍSTICA Y RAYCAST
        // ════════════════════════════════════════════════════════════════════

        [Header("Balística y Disparo por Raycast")]
        [Tooltip("Cámara principal del jugador. Si es nula, se resuelve automáticamente.")]
        [SerializeField] private Camera _playerCamera;

        [Tooltip("Alcance por defecto del raycast si no está definido por el arma (100m).")]
        [SerializeField] private float _defaultWeaponRange = 100f;

        [Tooltip("Máscara de capas impactables por el raycast. Por defecto impacta todo excepto la capa Player.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        [Tooltip("Duración en segundos de las líneas visuales de depuración en la escena (Debug.DrawRay).")]
        [SerializeField] private float _debugRayDuration = 1.2f;

        [Tooltip("Activa la depuración visual de rayos en la vista de escena.")]
        [SerializeField] private bool _drawDebugRays = true;

        // ════════════════════════════════════════════════════════════════════
        // SISTEMA DE MUNICIÓN Y RECARGA
        // ════════════════════════════════════════════════════════════════════

        [Header("Sistema de Munición y Recarga")]
        [Tooltip("Tecla para activar la recarga manual del arma.")]
        [SerializeField] private KeyCode _reloadKey = KeyCode.R;

        [Tooltip("Activa automáticamente la recarga cuando el cargador llega a 0 y se intenta disparar.")]
        [SerializeField] private bool _autoReloadOnEmpty = true;

        [Tooltip("Munición actual del arma equipada.")]
        [SerializeField] private int _currentAmmo = 12;

        [Tooltip("Capacidad máxima de munición del arma equipada.")]
        [SerializeField] private int _maxAmmo = 12;

        [Tooltip("Estado actual de recarga en curso (bloquea el disparo).")]
        [SerializeField] private bool _isReloading = false;

        [Header("Perfiles de Combate por Arma")]
        [Tooltip("Configuraciones predeterminadas de combate para las distintas armas del catálogo.")]
        [SerializeField] private List<WeaponCombatConfig> _weaponConfigs = new List<WeaponCombatConfig>();

        // ════════════════════════════════════════════════════════════════════
        // DESTELLO DE LUZ EN BOCA DEL CAÑÓN (MUZZLE FLASH)
        // ════════════════════════════════════════════════════════════════════

        [Header("Destello de Luz (Muzzle Flash)")]
        [Tooltip("¿Activar destello de luz efímero en la boca del cañón al disparar?")]
        [SerializeField] private bool _enableMuzzleFlash = true;

        [Tooltip("Duración efímera del destello en segundos (~0.05s).")]
        [Range(0.02f, 0.15f)]
        [SerializeField] private float _muzzleFlashDuration = 0.05f;

        [Tooltip("Color cálido/anaranjado del destello del disparo.")]
        [SerializeField] private Color _muzzleFlashColor = new Color(1.0f, 0.65f, 0.22f);

        [Tooltip("Intensidad lumínica mínima aleatoria.")]
        [Range(0.5f, 10f)]
        [SerializeField] private float _flashMinIntensity = 2.8f;

        [Tooltip("Intensidad lumínica máxima aleatoria.")]
        [Range(1f, 15f)]
        [SerializeField] private float _flashMaxIntensity = 5.2f;

        [Tooltip("Radio de alcance del punto de luz en metros.")]
        [Range(1f, 10f)]
        [SerializeField] private float _muzzleFlashRange = 5.0f;

        // ════════════════════════════════════════════════════════════════════
        // ENLACE CON EL HUD
        // ════════════════════════════════════════════════════════════════════

        [Header("Enlace con Interfaz HUD")]
        [Tooltip("Controlador del HUD en el juego. Si es nulo, se busca automáticamente.")]
        [SerializeField] private HUDController _hudController;

        [Tooltip("¿Actualizar automáticamente el indicador HUD global en tiempo real?")]
        [SerializeField] private bool _updateGlobalHUD = true;

        [Tooltip("Dibuja un indicador OnGUI de reserva si no existe HUDController en la escena.")]
        [SerializeField] private bool _enableOnGUIFallback = true;

        // ════════════════════════════════════════════════════════════════════
        // AUDIO Y FEEDBACK
        // ════════════════════════════════════════════════════════════════════

        [Header("Retroalimentación Sonora (Opcional)")]
        [Tooltip("Componente AudioSource para reproducir efectos de disparo.")]
        [SerializeField] private AudioSource _audioSource;

        [Tooltip("Clip de audio reproducido al disparar.")]
        [SerializeField] private AudioClip _fireSound;

        [Tooltip("Clip de audio reproducido al recargar.")]
        [SerializeField] private AudioClip _reloadSound;

        [Tooltip("Clip de audio cuando se intenta disparar con cargador vacío.")]
        [SerializeField] private AudioClip _emptyClickSound;

        // ════════════════════════════════════════════════════════════════════
        // REFERENCIAS DEL JUGADOR
        // ════════════════════════════════════════════════════════════════════

        [Header("Componentes Vinculados (Auto-Resueltos)")]
        [SerializeField] private PlayerWeaponEquipManager _weaponEquipManager;
        [SerializeField] private PlayerInventory _playerInventory;
        [SerializeField] private PlayerController _playerController;
        [SerializeField] private PlayerCamera _playerCameraRig;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private string _currentWeaponId = "";
        private WeaponData _currentWeaponData;
        private GameObject _currentEquippedModel;
        private Transform _firePoint;
        private Light _muzzleFlashLight;
        private Coroutine _muzzleFlashCoroutine;
        private Coroutine _reloadCoroutine;

        private float _nextFireTime = 0f;
        private WeaponCombatConfig _activeConfig;
        private readonly Dictionary<string, WeaponCombatConfig> _configsById = new Dictionary<string, WeaponCombatConfig>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _ammoMemoryByWeapon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Collider[] _playerColliders;

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        public event Action<int, int> OnAmmoUpdated; // (currentAmmo, maxAmmo)
        public event Action OnWeaponFired;
        public event Action OnReloadStarted;
        public event Action OnReloadFinished;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public bool HasWeaponEquipped => _currentEquippedModel != null || (!string.IsNullOrEmpty(_currentWeaponId) && _weaponEquipManager != null && _weaponEquipManager.HasWeaponEquipped);
        public string CurrentWeaponId => _currentWeaponId;
        public int CurrentAmmo => _currentAmmo;
        public int MaxAmmo => _maxAmmo;
        public bool IsReloading => _isReloading;
        public Transform FirePoint => _firePoint;
        public Light MuzzleFlashLight => _muzzleFlashLight;
        public string AmmoHUDText => _isReloading ? "BALAS: RECARGANDO..." : $"BALAS: {_currentAmmo} / {_maxAmmo}";

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            InicializarPerfilesPredeterminados();
            ResolverComponentesLocales();
            ResolverMascaraCapas();
        }

        private void OnEnable()
        {
            SuscribirEventos();
        }

        private void OnDisable()
        {
            DesuscribirEventos();
            if (_muzzleFlashCoroutine != null)
            {
                StopCoroutine(_muzzleFlashCoroutine);
                _muzzleFlashCoroutine = null;
            }
            if (_reloadCoroutine != null)
            {
                StopCoroutine(_reloadCoroutine);
                _reloadCoroutine = null;
                _isReloading = false;
            }
            if (_muzzleFlashLight != null)
            {
                _muzzleFlashLight.enabled = false;
            }
        }

        private void Start()
        {
            ResolverComponentesLocales();
            SincronizarArmaInicial();
            ActualizarHUD();
        }

        private void Update()
        {
            // Ignorar entrada si el juego no está en ejecución activa
            if (GameManager.Instance != null && !GameManager.Instance.IsRunActive)
                return;

            ProcesarEntradaDisparo();
            ProcesarEntradaRecarga();
        }

        // ════════════════════════════════════════════════════════════════════
        // INICIALIZACIÓN Y RESOLUCIÓN DE COMPONENTES
        // ════════════════════════════════════════════════════════════════════

        private void ResolverComponentesLocales()
        {
            if (_playerCamera == null)
            {
                _playerCamera = Camera.main ?? GetComponentInChildren<Camera>() ?? FindAnyObjectByType<Camera>();
            }

            if (_weaponEquipManager == null)
            {
                _weaponEquipManager = GetComponent<PlayerWeaponEquipManager>() ?? GetComponentInChildren<PlayerWeaponEquipManager>() ?? FindAnyObjectByType<PlayerWeaponEquipManager>();
            }

            if (_playerInventory == null)
            {
                _playerInventory = GetComponent<PlayerInventory>() ?? GetComponentInParent<PlayerInventory>();
            }

            if (_playerController == null)
            {
                _playerController = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            }

            if (_playerCameraRig == null)
            {
                _playerCameraRig = GetComponentInChildren<PlayerCamera>() ?? GetComponentInParent<PlayerCamera>();
            }

            if (_hudController == null)
            {
                _hudController = FindAnyObjectByType<HUDController>();
            }

            AsegurarAudioSource();

            _playerColliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>
        /// Obtiene de forma defensiva o añade el componente AudioSource al objeto sin arrojar excepciones.
        /// </summary>
        public AudioSource AsegurarAudioSource()
        {
            try
            {
                if (_audioSource == null)
                {
                    _audioSource = GetComponent<AudioSource>();
                }

                if (_audioSource == null)
                {
                    _audioSource = GetComponentInChildren<AudioSource>();
                }

                if (_audioSource == null && gameObject != null)
                {
                    _audioSource = gameObject.AddComponent<AudioSource>();
                }

                if (_audioSource != null)
                {
                    _audioSource.playOnAwake = false;
                    _audioSource.spatialBlend = 0f; // 2D para sonidos del arma del jugador
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WeaponCombatController] No se pudo inicializar AudioSource: {ex.Message}");
            }

            return _audioSource;
        }

        private void ResolverMascaraCapas()
        {
            // Excluir capa Player si está configurada
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer != -1)
            {
                _hitMask &= ~(1 << playerLayer);
            }
        }

        private void InicializarPerfilesPredeterminados()
        {
            // 1. Agregar configuraciones base para todo el catálogo de armas
            AgregarOActualizarPerfil(new WeaponCombatConfig("bingala", auto: false, rate: 1.3f, ammo: 6, reload: 1.8f, dmg: 48f, knock: 8f, range: 90f));
            AgregarOActualizarPerfil(new WeaponCombatConfig("revolver", auto: false, rate: 2.2f, ammo: 6, reload: 1.7f, dmg: 38f, knock: 6f, range: 100f));
            AgregarOActualizarPerfil(new WeaponCombatConfig("demencia revolver", auto: false, rate: 2.2f, ammo: 6, reload: 1.7f, dmg: 38f, knock: 6f, range: 100f));
            AgregarOActualizarPerfil(new WeaponCombatConfig("muak", auto: true, rate: 8.5f, ammo: 30, reload: 2.2f, dmg: 18f, knock: 3f, range: 120f));
            AgregarOActualizarPerfil(new WeaponCombatConfig("trabeca", auto: false, rate: 1.4f, ammo: 4, reload: 2.4f, dmg: 65f, knock: 12f, range: 45f));
            AgregarOActualizarPerfil(new WeaponCombatConfig("canoduplo", auto: false, rate: 1.6f, ammo: 2, reload: 2.0f, dmg: 80f, knock: 14f, range: 50f));
            AgregarOActualizarPerfil(new WeaponCombatConfig("default", auto: false, rate: 2.5f, ammo: 12, reload: 1.5f, dmg: 25f, knock: 5f, range: 100f));

            // 2. Incorporar cualquier perfil personalizado configurado desde el Inspector
            if (_weaponConfigs != null)
            {
                foreach (var cfg in _weaponConfigs)
                {
                    if (cfg != null && !string.IsNullOrEmpty(cfg.weaponId))
                    {
                        _configsById[cfg.weaponId] = cfg;
                    }
                }
            }
        }

        private void AgregarOActualizarPerfil(WeaponCombatConfig cfg)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.weaponId)) return;
            _configsById[cfg.weaponId] = cfg;

            if (_weaponConfigs != null && !_weaponConfigs.Exists(c => string.Equals(c.weaponId, cfg.weaponId, StringComparison.OrdinalIgnoreCase)))
            {
                _weaponConfigs.Add(cfg);
            }
        }

        private void SuscribirEventos()
        {
            if (_weaponEquipManager != null)
            {
                _weaponEquipManager.OnWeaponEquipped += HandleWeaponEquipped;
                _weaponEquipManager.OnWeaponUnequipped += HandleWeaponUnequipped;
            }
        }

        private void DesuscribirEventos()
        {
            if (_weaponEquipManager != null)
            {
                _weaponEquipManager.OnWeaponEquipped -= HandleWeaponEquipped;
                _weaponEquipManager.OnWeaponUnequipped -= HandleWeaponUnequipped;
            }
        }

        private void SincronizarArmaInicial()
        {
            if (_weaponEquipManager != null && _weaponEquipManager.HasWeaponEquipped)
            {
                HandleWeaponEquipped(_weaponEquipManager.CurrentSpawnedWeapon, _weaponEquipManager.CurrentEquippedWeaponData);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // VINCULACIÓN Y RECEPCIÓN DE ARMA EQUIPADA
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Callback ejecutado cuando PlayerWeaponEquipManager instancia y equipa un modelo 3D bajo el WeaponHolder.
        /// </summary>
        public void HandleWeaponEquipped(GameObject weaponInstance, WeaponData weaponData)
        {
            // Cancelar cualquier recarga activa del arma anterior
            if (_reloadCoroutine != null)
            {
                StopCoroutine(_reloadCoroutine);
                _reloadCoroutine = null;
                _isReloading = false;
            }

            _currentEquippedModel = weaponInstance;
            _currentWeaponData = weaponData;

            // Determinar ID del arma
            string weaponId = "default";
            if (weaponData != null && !string.IsNullOrEmpty(weaponData.WeaponID))
            {
                weaponId = weaponData.WeaponID;
            }
            else if (_weaponEquipManager != null && !string.IsNullOrEmpty(_weaponEquipManager.CurrentEquippedWeaponId))
            {
                weaponId = _weaponEquipManager.CurrentEquippedWeaponId;
            }
            else if (weaponInstance != null)
            {
                weaponId = weaponInstance.name.Replace("Weapon3D_", "").Trim();
            }

            _currentWeaponId = weaponId;

            // Resolver configuración de combate
            _activeConfig = ResolverConfiguracionCombate(weaponId, weaponData);

            // Capacidad máxima de munición
            _maxAmmo = (weaponData != null && weaponData.MagazineSize > 0) ? weaponData.MagazineSize : _activeConfig.maxAmmo;

            // Restaurar munición guardada o inicializar cargador lleno
            if (_ammoMemoryByWeapon.TryGetValue(weaponId, out int savedAmmo))
            {
                _currentAmmo = Mathf.Clamp(savedAmmo, 0, _maxAmmo);
            }
            else
            {
                _currentAmmo = _maxAmmo;
                _ammoMemoryByWeapon[weaponId] = _currentAmmo;
            }

            // Encontrar o crear FirePoint y punto de luz en la boca del cañón
            ConfigurarFirePointYMuzzleFlash(weaponInstance);

            // Refrescar HUD
            ActualizarHUD();

            Debug.Log($"[WeaponCombatController] Arma '{_currentWeaponId}' lista para combate. Cargador: {_currentAmmo}/{_maxAmmo}, Cadencia: {_activeConfig.fireRate} d/s, Daño: {_activeConfig.damage}");
        }

        /// <summary>
        /// Callback ejecutado cuando se desequipa el arma activa.
        /// </summary>
        public void HandleWeaponUnequipped()
        {
            if (!string.IsNullOrEmpty(_currentWeaponId))
            {
                _ammoMemoryByWeapon[_currentWeaponId] = _currentAmmo;
            }

            if (_reloadCoroutine != null)
            {
                StopCoroutine(_reloadCoroutine);
                _reloadCoroutine = null;
                _isReloading = false;
            }

            if (_muzzleFlashLight != null)
            {
                _muzzleFlashLight.enabled = false;
            }

            _currentEquippedModel = null;
            _currentWeaponData = null;
            _currentWeaponId = "";
            _firePoint = null;
            _muzzleFlashLight = null;

            ActualizarHUD();
        }

        private WeaponCombatConfig ResolverConfiguracionCombate(string weaponId, WeaponData data)
        {
            if (_configsById.TryGetValue(weaponId, out WeaponCombatConfig cfg))
            {
                return cfg;
            }

            // Búsqueda flexible por subcadena (ej. "demencia revolver" encuentra "revolver")
            foreach (var kvp in _configsById)
            {
                if (weaponId.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    kvp.Key.IndexOf(weaponId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kvp.Value;
                }
            }

            // Si hay WeaponData asignado, derivar configuración
            if (data != null)
            {
                WeaponCombatConfig derived = new WeaponCombatConfig(
                    weaponId,
                    auto: data.IsAutomatic,
                    rate: data.FireRate > 0 ? data.FireRate : 2.5f,
                    ammo: data.MagazineSize > 0 ? data.MagazineSize : 12,
                    reload: data.ReloadTime > 0 ? data.ReloadTime : 1.6f,
                    dmg: data.BaseDamage > 0 ? data.BaseDamage : 25f,
                    knock: data.KnockbackForce > 0 ? data.KnockbackForce : 5f,
                    range: data.MaxRange > 0 ? data.MaxRange : _defaultWeaponRange
                );
                _configsById[weaponId] = derived;
                return derived;
            }

            // Fallback por defecto
            if (_configsById.TryGetValue("default", out WeaponCombatConfig defaultCfg))
            {
                return defaultCfg;
            }

            return new WeaponCombatConfig("default", auto: false, rate: 2.5f, ammo: 12, reload: 1.5f, dmg: 25f, knock: 5f, range: 100f);
        }

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DINÁMICA DE FIREPOINT Y MUZZLE FLASH
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Localiza o crea dinámicamente un FirePoint en la boca del cañón del modelo 3D equipado
        /// y prepara el componente de luz efímera para el destello (Muzzle Flash).
        /// </summary>
        public void ConfigurarFirePointYMuzzleFlash(GameObject weaponGo)
        {
            if (weaponGo == null)
            {
                _firePoint = null;
                _muzzleFlashLight = null;
                return;
            }

            // 1. Buscar si el modelo ya contiene un transform de cañón/boca explícito
            Transform existingPoint = BuscarTransformBocaCanon(weaponGo.transform);

            if (existingPoint != null)
            {
                _firePoint = existingPoint;
            }
            else
            {
                // 2. Calcular geométricamente el extremo frontal del cañón en base al Mesh/Render Bounds
                Vector3 localBarrelPos = CalcularPosicionFrontalCanon(weaponGo);

                // Aplicar offset configurable si existe
                if (_activeConfig != null && _activeConfig.firePointOffset != Vector3.zero)
                {
                    localBarrelPos += _activeConfig.firePointOffset;
                }

                // Crear GameObject FirePoint hijo del modelo
                GameObject fpGo = new GameObject("FirePoint_AutoMuzzle");
                fpGo.transform.SetParent(weaponGo.transform, false);
                fpGo.transform.localPosition = localBarrelPos;
                fpGo.transform.localRotation = Quaternion.identity;
                _firePoint = fpGo.transform;
            }

            // 3. Configurar componente Light efímero (Point Light)
            if (_firePoint != null)
            {
                _muzzleFlashLight = _firePoint.GetComponent<Light>();
                if (_muzzleFlashLight == null)
                {
                    _muzzleFlashLight = _firePoint.gameObject.AddComponent<Light>();
                }

                _muzzleFlashLight.type = LightType.Point;
                _muzzleFlashLight.color = _muzzleFlashColor;
                _muzzleFlashLight.range = _muzzleFlashRange;
                _muzzleFlashLight.shadows = LightShadows.None; // Máximo rendimiento sin costo de sombras
                _muzzleFlashLight.intensity = 0f;
                _muzzleFlashLight.enabled = false;
            }
        }

        private Transform BuscarTransformBocaCanon(Transform root)
        {
            string[] possibleNames = { "firepoint", "muzzle", "muzzleflash", "barrel", "boca", "canon", "shootpoint" };

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                string nameLower = child.name.ToLower();
                for (int i = 0; i < possibleNames.Length; i++)
                {
                    if (nameLower.Contains(possibleNames[i]))
                    {
                        return child;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Proyecta los vértices y límites geométricos (Mesh Bounds) de la malla en el espacio de la cámara
        /// para determinar con precisión milimétrica la punta más adelantada del cañón.
        /// </summary>
        private Vector3 CalcularPosicionFrontalCanon(GameObject weaponGo)
        {
            var meshRenderers = weaponGo.GetComponentsInChildren<MeshRenderer>(true);
            var skinnedRenderers = weaponGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            if (meshRenderers.Length == 0 && skinnedRenderers.Length == 0)
            {
                // Fallback si no tiene renderers visibles: desplazar ligeramente al frente
                return new Vector3(0f, 0f, 0.35f);
            }

            // Combinar límites en coordenadas mundiales
            Bounds combinedBounds = default;
            bool boundsInitialized = false;

            for (int i = 0; i < meshRenderers.Length; i++)
            {
                if (!boundsInitialized)
                {
                    combinedBounds = meshRenderers[i].bounds;
                    boundsInitialized = true;
                }
                else
                {
                    combinedBounds.Encapsulate(meshRenderers[i].bounds);
                }
            }

            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                if (!boundsInitialized)
                {
                    combinedBounds = skinnedRenderers[i].bounds;
                    boundsInitialized = true;
                }
                else
                {
                    combinedBounds.Encapsulate(skinnedRenderers[i].bounds);
                }
            }

            Transform camTransform = (_playerCamera != null) ? _playerCamera.transform : transform;

            // Proyectar las 8 esquinas del Bound mundial al espacio local de la cámara
            Vector3 center = combinedBounds.center;
            Vector3 extents = combinedBounds.extents;

            Vector3[] corners = new Vector3[8]
            {
                camTransform.InverseTransformPoint(center + new Vector3(-extents.x, -extents.y, -extents.z)),
                camTransform.InverseTransformPoint(center + new Vector3(-extents.x, -extents.y,  extents.z)),
                camTransform.InverseTransformPoint(center + new Vector3(-extents.x,  extents.y, -extents.z)),
                camTransform.InverseTransformPoint(center + new Vector3(-extents.x,  extents.y,  extents.z)),
                camTransform.InverseTransformPoint(center + new Vector3( extents.x, -extents.y, -extents.z)),
                camTransform.InverseTransformPoint(center + new Vector3( extents.x, -extents.y,  extents.z)),
                camTransform.InverseTransformPoint(center + new Vector3( extents.x,  extents.y, -extents.z)),
                camTransform.InverseTransformPoint(center + new Vector3( extents.x,  extents.y,  extents.z))
            };

            float maxZInCam = float.MinValue;
            float sumX = 0f;
            float maxYInCam = float.MinValue;

            for (int i = 0; i < corners.Length; i++)
            {
                if (corners[i].z > maxZInCam) maxZInCam = corners[i].z;
                if (corners[i].y > maxYInCam) maxYInCam = corners[i].y;
                sumX += corners[i].x;
            }

            float avgX = sumX / corners.Length;
            // El cañón suele situarse en la parte superior del frente del arma
            float barrelY = Mathf.Lerp(camTransform.InverseTransformPoint(center).y, maxYInCam, 0.75f);

            // Re-proyectar al mundo y luego al espacio local del modelo de arma
            Vector3 worldMuzzlePos = camTransform.TransformPoint(new Vector3(avgX, barrelY, maxZInCam + 0.03f));
            Vector3 localPos = weaponGo.transform.InverseTransformPoint(worldMuzzlePos);

            return localPos;
        }

        // ════════════════════════════════════════════════════════════════════
        // PROCESAMIENTO DE DISPARO POR RAYCAST
        // ════════════════════════════════════════════════════════════════════

        private void ProcesarEntradaDisparo()
        {
            if (!HasWeaponEquipped) return;

            bool isAuto = _activeConfig != null && _activeConfig.isAutomatic;
            bool triggerPressed = isAuto
                ? (Input.GetButton("Fire1") || Input.GetMouseButton(0))
                : (Input.GetButtonDown("Fire1") || Input.GetMouseButtonDown(0));

            if (triggerPressed)
            {
                TryShoot();
            }
        }

        /// <summary>
        /// Intenta ejecutar un disparo respetando munición, cadencia y estado de recarga.
        /// </summary>
        public bool TryShoot()
        {
            if (!HasWeaponEquipped) return false;

            // Bloquear disparo si se está recargando
            if (_isReloading)
            {
                return false;
            }

            // Verificar cargador vacío
            if (_currentAmmo <= 0)
            {
                try
                {
                    PlayAudio(_emptyClickSound);
                }
                catch { }

                if (_autoReloadOnEmpty && !_isReloading)
                {
                    StartCoroutine(ReloadRoutine());
                }
                return false;
            }

            // Respetar cadencia de fuego (fireRate)
            float fireRate = (_activeConfig != null) ? _activeConfig.fireRate : 2.5f;
            if (Time.time < _nextFireTime)
            {
                return false;
            }

            // Consumir munición de forma inmediata
            _currentAmmo--;
            GuardarMunicionEnMemoria();

            _nextFireTime = Time.time + (1f / Mathf.Max(0.05f, fireRate));

            // Sincronizar munición con PlayerInventory si está disponible
            if (_playerInventory != null && _playerInventory.ActiveWeaponInstance != null)
            {
                _playerInventory.ActiveWeaponInstance.CurrentMagAmmo = _currentAmmo;
                _playerInventory.NotifyAmmoChange();
            }

            // 1. Activar destello de luz efímero (Muzzle Flash) protegido
            try
            {
                DispararMuzzleFlash();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WeaponCombatController] Muzzle flash omitido: {ex.Message}");
            }

            // 2. Disparar raycast balístico desde el centro exacto de la Main Camera
            try
            {
                EjecutarDisparoRaycast();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WeaponCombatController] Error en ejecución de Raycast: {ex.Message}");
            }

            // 3. Actualizar HUD y disparar eventos prioritarios
            try
            {
                ActualizarHUD();
                OnWeaponFired?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WeaponCombatController] Error al actualizar HUD: {ex.Message}");
            }

            // 4. Audio y retroceso (opcionales: nunca interrumpen el combate)
            try
            {
                PlayAudio(_fireSound);
                AplicarRetrocesoCamara();
            }
            catch { }

            return true;
        }

        /// <summary>
        /// Lanza un Physics.Raycast originating strictly from the exact center of the Main Camera viewport (0.5, 0.5)
        /// forward along camera forward vector, detects colliders, logs impacts and applies damage to IDamageable.
        /// </summary>
        private void EjecutarDisparoRaycast()
        {
            if (_playerCamera == null)
            {
                _playerCamera = Camera.main ?? GetComponentInChildren<Camera>() ?? FindAnyObjectByType<Camera>();
            }

            if (_playerCamera == null)
            {
                Debug.LogWarning("[WeaponCombatController] No se encontró cámara para calcular el raycast de disparo.");
                return;
            }

            // Origen estricto: Centro exacto de la pantalla/viewport (0.5f, 0.5f, 0.0f)
            Vector3 rayOrigin = _playerCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 0.0f));
            Vector3 rayDirection = _playerCamera.transform.forward;

            float range = (_activeConfig != null && _activeConfig.weaponRange > 0f) ? _activeConfig.weaponRange : _defaultWeaponRange;
            float damage = (_activeConfig != null) ? _activeConfig.damage : 30f;
            float knockback = (_activeConfig != null) ? _activeConfig.knockbackForce : 5f;

            bool hitFound = Physics.Raycast(
                rayOrigin,
                rayDirection,
                out RaycastHit hit,
                range,
                _hitMask,
                QueryTriggerInteraction.Ignore
            );

            bool hitEnemy = false;

            if (hitFound)
            {
                // Ignorar colisiones con el propio jugador
                if (EsColisionadorDelJugador(hit.collider))
                {
                    return;
                }

                Debug.Log($"[WeaponCombatController] 💥 Impacto con '{_currentWeaponId}' en '{hit.collider.gameObject.name}' " +
                          $"en posición {hit.point} (Distancia: {hit.distance:F1}m).");

                // Buscar componente IDamageable en el objeto o sus padres
                IDamageable damageable = hit.collider.GetComponent<IDamageable>() ?? hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.IsAlive)
                {
                    hitEnemy = true;
                    bool isHeadshot = hit.collider.CompareTag("Head") || hit.collider.name.ToLower().Contains("head");
                    float finalDamage = isHeadshot ? (damage * 2.5f) : damage;

                    DamageInfo info = new DamageInfo(
                        amount: finalDamage,
                        attacker: gameObject,
                        hitPoint: hit.point,
                        hitNormal: hit.normal,
                        direction: rayDirection,
                        sourceType: DamageSourceType.Bullet,
                        isCritical: false,
                        isHeadshot: isHeadshot,
                        ignoreDefense: false,
                        knockbackForce: knockback
                    );

                    float damageDealt = damageable.TakeDamage(info);
                    Debug.Log($"[WeaponCombatController] Daño infligido a {hit.collider.gameObject.name}: {damageDealt:F1} HP (Headshot: {isHeadshot}, Vivo: {damageable.IsAlive}).");
                }
                else
                {
                    // Soporte de respaldo para otros scripts de enemigos sin IDamageable
                    hit.collider.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
                }

                // Aplicar impulso físico de retroceso a la entidad
                IKnockbackable knockbackable = hit.collider.GetComponent<IKnockbackable>() ?? hit.collider.GetComponentInParent<IKnockbackable>();
                if (knockbackable != null && knockback > 0f)
                {
                    knockbackable.ApplyKnockback(rayDirection * knockback);
                }
            }
            else
            {
                Debug.Log($"[WeaponCombatController] Disparo de '{_currentWeaponId}' ejecutado al aire (sin impacto en {range:F0}m).");
            }

            // Trazador visual en el Editor para verificar la trayectoria en Scene view
            if (_drawDebugRays)
            {
                float lineDist = hitFound ? hit.distance : range;
                Color rayColor = hitEnemy ? Color.green : (hitFound ? Color.yellow : Color.red);
                Debug.DrawRay(rayOrigin, rayDirection * lineDist, rayColor, _debugRayDuration);
            }
        }

        private bool EsColisionadorDelJugador(Collider col)
        {
            if (col == null) return false;
            if (col.transform.root == transform.root) return true;
            if (col.CompareTag("Player")) return true;

            if (_playerColliders != null)
            {
                for (int i = 0; i < _playerColliders.Length; i++)
                {
                    if (_playerColliders[i] == col) return true;
                }
            }
            return false;
        }

        private void AplicarRetrocesoCamara()
        {
            if (_playerController != null)
            {
                _playerController.ApplyRecoil(1.2f, 0.4f);
            }
            else if (_playerCameraRig != null)
            {
                _playerCameraRig.ApplyRecoil(1.2f, 0.4f);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DESTELLO DE LUZ EFÍMERO (MUZZLE FLASH)
        // ════════════════════════════════════════════════════════════════════

        private void DispararMuzzleFlash()
        {
            if (!_enableMuzzleFlash) return;

            // Si el FirePoint o la luz no están activos, intentar reconfigurarlos
            if (_muzzleFlashLight == null && _currentEquippedModel != null)
            {
                ConfigurarFirePointYMuzzleFlash(_currentEquippedModel);
            }

            if (_muzzleFlashLight != null)
            {
                if (_muzzleFlashCoroutine != null)
                {
                    StopCoroutine(_muzzleFlashCoroutine);
                }
                _muzzleFlashCoroutine = StartCoroutine(MuzzleFlashRoutine());
            }
        }

        private IEnumerator MuzzleFlashRoutine()
        {
            if (_muzzleFlashLight == null) yield break;

            // Intensidad aleatoria y activación de luz cálida
            _muzzleFlashLight.intensity = UnityEngine.Random.Range(_flashMinIntensity, _flashMaxIntensity);
            _muzzleFlashLight.enabled = true;

            yield return new WaitForSeconds(_muzzleFlashDuration);

            if (_muzzleFlashLight != null)
            {
                _muzzleFlashLight.enabled = false;
            }
            _muzzleFlashCoroutine = null;
        }

        // ════════════════════════════════════════════════════════════════════
        // SISTEMA DE RECARGA
        // ════════════════════════════════════════════════════════════════════

        private void ProcesarEntradaRecarga()
        {
            if (!HasWeaponEquipped) return;

            if (Input.GetKeyDown(_reloadKey) && !_isReloading)
            {
                if (_currentAmmo < _maxAmmo)
                {
                    StartCoroutine(ReloadRoutine());
                }
            }
        }

        /// <summary>
        /// Corrutina de recarga: bloquea el disparo durante 'reloadTime' y recarga el cargador al máximo.
        /// </summary>
        public IEnumerator ReloadRoutine()
        {
            if (_isReloading) yield break;

            _isReloading = true;
            OnReloadStarted?.Invoke();

            float reloadDuration = (_activeConfig != null && _activeConfig.reloadTime > 0f) ? _activeConfig.reloadTime : 1.6f;
            Debug.Log($"[WeaponCombatController] 🔄 Recargando arma '{_currentWeaponId}'... Duración: {reloadDuration:F2}s");

            try
            {
                PlayAudio(_reloadSound);
            }
            catch { }

            ActualizarHUD();

            yield return new WaitForSeconds(reloadDuration);

            // Reabastecer cargador
            _currentAmmo = _maxAmmo;
            GuardarMunicionEnMemoria();

            // Sincronizar con PlayerInventory si existe reserva
            if (_playerInventory != null && _playerInventory.ActiveWeaponInstance != null)
            {
                _playerInventory.ActiveWeaponInstance.CurrentMagAmmo = _currentAmmo;
                _playerInventory.NotifyAmmoChange();
            }

            _isReloading = false;
            _reloadCoroutine = null;

            ActualizarHUD();
            OnReloadFinished?.Invoke();

            Debug.Log($"[WeaponCombatController] ✅ Recarga completada para '{_currentWeaponId}'. Balas: {_currentAmmo}/{_maxAmmo}");
        }

        private void GuardarMunicionEnMemoria()
        {
            if (!string.IsNullOrEmpty(_currentWeaponId))
            {
                _ammoMemoryByWeapon[_currentWeaponId] = _currentAmmo;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ENLACE CON HUD Y RENDERIZADO VISUAL
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sincroniza el estado de munición con el HUDController y listeners externos en tiempo real.
        /// </summary>
        public void ActualizarHUD()
        {
            if (!HasWeaponEquipped)
            {
                if (_updateGlobalHUD && _hudController != null)
                {
                    _hudController.SetAmmoDisplayText("-- / --");
                }
                OnAmmoUpdated?.Invoke(0, 0);
                return;
            }

            if (_updateGlobalHUD)
            {
                if (_hudController == null)
                {
                    _hudController = FindAnyObjectByType<HUDController>();
                }

                if (_hudController != null)
                {
                    if (_isReloading)
                    {
                        _hudController.SetAmmoDisplayText("RECARGANDO...");
                    }
                    else
                    {
                        _hudController.UpdateAmmo(_currentAmmo, _maxAmmo);
                    }
                }
            }

            OnAmmoUpdated?.Invoke(_currentAmmo, _maxAmmo);
        }

        private void OnGUI()
        {
            if (!_enableOnGUIFallback) return;

            // Si el HUDController ya está dibujado por RuntimeDebugOverlay, no duplicar a menos que falte
            if (FindAnyObjectByType<RuntimeDebugOverlay>() != null) return;

            if (!HasWeaponEquipped) return;

            float pad = 20f;
            float topY = Screen.height - 45f;
            string hudText = _isReloading ? "🔫 BALAS: RECARGANDO..." : $"🔫 BALAS: {_currentAmmo} / {_maxAmmo}";

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = _isReloading ? new Color(1f, 0.8f, 0.2f) : Color.white;

            GUI.Label(new Rect(pad, topY, 260, 30), hudText, style);
        }

        private void PlayAudio(AudioClip clip)
        {
            if (clip == null) return;

            try
            {
                AudioSource source = AsegurarAudioSource();
                if (source != null && source.isActiveAndEnabled)
                {
                    source.PlayOneShot(clip);
                }
            }
            catch (MissingComponentException)
            {
                // Ignorar de forma silenciosa para garantizar continuidad del combate
            }
            catch (Exception ex)
            {
                // Silencioso: garantizado que no interrumpe el combate
                Debug.LogWarning($"[WeaponCombatController] Reproducción de sonido omitida: {ex.Message}");
            }
        }
    }
}

