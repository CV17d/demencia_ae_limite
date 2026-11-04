// ============================================================================
// PlayerManager.cs — Hub Central del Jugador y Puente de Estado (Unity 6)
// ============================================================================
// Conecta los componentes locales del jugador (FirstPersonController, PlayerCamera,
// PlayerHealth, etc.) y sirve de puente para la integracion de sistemas globales:
// - SanityManager (mecanica de culpa, distorsion por locura y alucinaciones).
// - GameManager (estado de partida, estadisticas PlayerStats y teletransporte).
// ============================================================================

using System;
using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Horror;

namespace DemenciaNoLimite.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class PlayerManager : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // INSTANCIA CENTRAL
        // ════════════════════════════════════════════════════════════════════

        public static PlayerManager Instance { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        // REFERENCIAS A COMPONENTES LOCALES
        // ════════════════════════════════════════════════════════════════════

        [Header("Componentes Locales del Jugador")]
        [Tooltip("Controlador de movimiento fisico.")]
        [SerializeField] private FirstPersonController _controller;

        [Tooltip("Controlador de vision y camara (hijo).")]
        [SerializeField] private PlayerCamera _playerCamera;

        [Tooltip("Controlador de colisiones nativo.")]
        [SerializeField] private CharacterController _characterController;

        [Tooltip("Componente de salud y dano recibido.")]
        [SerializeField] private PlayerHealth _health;

        [Tooltip("Inventario de armas y consumibles.")]
        [SerializeField] private PlayerInventory _inventory;

        [Tooltip("Gestor de balistica y disparo.")]
        [SerializeField] private PlayerWeaponHandler _weaponHandler;

        [Tooltip("Controlador de manos diegeticas e interfaz en primera persona.")]
        [SerializeField] private PlayerHandController _handController;

        [Tooltip("Gestor de instanciamiento y equipamiento de armas 3D en primera persona.")]
        [SerializeField] private PlayerWeaponEquipManager _weaponEquipManager;
        [SerializeField] private WeaponCombatController _combatController;

        // ════════════════════════════════════════════════════════════════════
        // REFERENCIAS GLOBALES (LOCURA / GAMEPLAY)
        // ════════════════════════════════════════════════════════════════════

        [Header("Integraciones Globales (Bridges)")]
        [Tooltip("Gestor del sistema de culpa y terror psicologico. Si es nulo, se busca la instancia activa.")]
        [SerializeField] private SanityManager _sanityManager;

        [Tooltip("Gestor de flujo del juego. Si es nulo, se enlaza automaticamente.")]
        [SerializeField] private GameManager _gameManager;

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS DEL JUGADOR
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Se dispara al actualizarse la culpa (int valor, float porcentaje 0..1).
        /// </summary>
        public event Action<int, float> OnGuiltUpdated;

        /// <summary>
        /// Se dispara al cambiar la salud (float actual, float maxima).
        /// </summary>
        public event Action<float, float> OnHealthChanged;

        /// <summary>
        /// Se dispara cuando el jugador muere.
        /// </summary>
        public event Action OnPlayerDeath;

        /// <summary>
        /// Se dispara al alternar el estado de sprint (correr).
        /// </summary>
        public event Action<bool> OnSprintStateChanged;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PUBLICAS
        // ════════════════════════════════════════════════════════════════════

        public FirstPersonController Controller => _controller;
        public PlayerCamera PlayerCamera => _playerCamera;
        public CharacterController CharacterController => _characterController;
        public PlayerHealth Health => _health;
        public PlayerInventory Inventory => _inventory;
        public PlayerWeaponHandler WeaponHandler => _weaponHandler;
        public PlayerHandController HandController => _handController;
        public PlayerWeaponEquipManager WeaponEquipManager => _weaponEquipManager;
        public WeaponCombatController CombatController => _combatController;

        public bool IsAlive => _health == null || _health.IsAlive;
        public float CurrentGuilt => _sanityManager != null ? _sanityManager.CurrentGuilt : (SanityManager.Instance != null ? SanityManager.Instance.CurrentGuilt : 0);
        public float InsanityPercentage => _sanityManager != null ? _sanityManager.InsanityPercentage : (SanityManager.Instance != null ? SanityManager.Instance.InsanityPercentage : 0f);

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private bool _lastSprintState;

        private void Update()
        {
            if (_controller != null && _controller.IsSprinting != _lastSprintState)
            {
                _lastSprintState = _controller.IsSprinting;
                OnSprintStateChanged?.Invoke(_lastSprintState);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            ResolveLocalComponents();
        }

        private void OnEnable()
        {
            BindGlobalSystems();
        }

        private void OnDisable()
        {
            UnbindGlobalSystems();
        }

        private void Start()
        {
            ResolveLocalComponents();
            BindGlobalSystems();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ENLACE DE COMPONENTES LOCALES
        // ════════════════════════════════════════════════════════════════════

        private void ResolveLocalComponents()
        {
            if (_controller == null) _controller = GetComponent<FirstPersonController>();
            if (_characterController == null) _characterController = GetComponent<CharacterController>();
            if (_playerCamera == null) _playerCamera = GetComponentInChildren<PlayerCamera>();
            if (_health == null) _health = GetComponent<PlayerHealth>();
            if (_inventory == null) _inventory = GetComponent<PlayerInventory>();
            if (_weaponHandler == null) _weaponHandler = GetComponent<PlayerWeaponHandler>();
            if (_handController == null)
            {
                _handController = GetComponentInChildren<PlayerHandController>();
                if (_handController == null)
                {
                    _handController = gameObject.AddComponent<PlayerHandController>();
                }
            }
            if (_weaponEquipManager == null) _weaponEquipManager = GetComponentInChildren<PlayerWeaponEquipManager>();
            if (_combatController == null)
            {
                _combatController = GetComponent<WeaponCombatController>() ?? GetComponentInChildren<WeaponCombatController>();
                if (_combatController == null)
                {
                    _combatController = gameObject.AddComponent<WeaponCombatController>();
                }
            }

            var audioSrc = GetComponent<AudioSource>();
            if (audioSrc == null)
            {
                audioSrc = gameObject.AddComponent<AudioSource>();
                audioSrc.playOnAwake = false;
                audioSrc.spatialBlend = 0f;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ENLACE CON SANITYMANAGER Y GAMEMANAGER
        // ════════════════════════════════════════════════════════════════════

        private void BindGlobalSystems()
        {
            if (_sanityManager == null)
            {
                _sanityManager = SanityManager.Instance ?? FindAnyObjectByType<SanityManager>();
            }

            if (_sanityManager != null)
            {
                _sanityManager.OnGuiltChanged -= HandleSanityGuiltChanged;
                _sanityManager.OnGuiltChanged += HandleSanityGuiltChanged;
            }

            if (_gameManager == null)
            {
                _gameManager = GameManager.Instance ?? FindAnyObjectByType<GameManager>();
            }

            DemenciaNoLimite.Core.Events.GameEvents.OnPlayerDamaged -= HandlePlayerDamaged;
            DemenciaNoLimite.Core.Events.GameEvents.OnPlayerDamaged += HandlePlayerDamaged;
            DemenciaNoLimite.Core.Events.GameEvents.OnPlayerHealed -= HandlePlayerHealed;
            DemenciaNoLimite.Core.Events.GameEvents.OnPlayerHealed += HandlePlayerHealed;
        }

        private void UnbindGlobalSystems()
        {
            if (_sanityManager != null)
            {
                _sanityManager.OnGuiltChanged -= HandleSanityGuiltChanged;
            }

            DemenciaNoLimite.Core.Events.GameEvents.OnPlayerDamaged -= HandlePlayerDamaged;
            DemenciaNoLimite.Core.Events.GameEvents.OnPlayerHealed -= HandlePlayerHealed;
        }

        private void HandlePlayerDamaged(float dmg)
        {
            float hp = _health != null ? _health.CurrentHealth : (_gameManager?.Player?.CurrentHP ?? 0f);
            float maxHp = _health != null ? _health.MaxHealth : (_gameManager?.Player?.MaxHP ?? 100f);
            OnHealthChanged?.Invoke(hp, maxHp);

            if (hp <= 0f)
            {
                OnPlayerDeath?.Invoke();
            }
        }

        private void HandlePlayerHealed(float heal)
        {
            float hp = _health != null ? _health.CurrentHealth : (_gameManager?.Player?.CurrentHP ?? 0f);
            float maxHp = _health != null ? _health.MaxHealth : (_gameManager?.Player?.MaxHP ?? 100f);
            OnHealthChanged?.Invoke(hp, maxHp);
        }

        private void HandleSanityGuiltChanged(int guilt, float normalizedGuilt)
        {
            OnGuiltUpdated?.Invoke(guilt, normalizedGuilt);
        }

        // ════════════════════════════════════════════════════════════════════
        // ACCIONES DE JUGADOR (FREEZE, TELEPORT, RECOIL)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Congela o reactiva el control de movimiento y vision del jugador
        /// (util para cinematicas, ascensores, QTEs o pantallas de pausa).
        /// </summary>
        public void SetPlayerFrozen(bool frozen)
        {
            if (_controller != null)
            {
                _controller.SetMovementEnabled(!frozen);
            }

            if (_playerCamera != null)
            {
                _playerCamera.SetLookEnabled(!frozen);
            }
        }

        /// <summary>
        /// Teletransporta al jugador de forma segura entre salas o pisos generados.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (_controller != null)
            {
                _controller.Teleport(position, rotation);
            }
            else if (_characterController != null)
            {
                _characterController.enabled = false;
                transform.position = position;
                transform.rotation = rotation;
                _characterController.enabled = true;
            }
            else
            {
                transform.position = position;
                transform.rotation = rotation;
            }
        }

        /// <summary>
        /// Aplica retroceso procedimental en la camara al disparar o recibir un impacto.
        /// </summary>
        public void ApplyRecoil(float verticalKick, float horizontalSpread)
        {
            if (_playerCamera != null)
            {
                _playerCamera.ApplyRecoil(verticalKick, horizontalSpread);
            }
        }
    }
}
