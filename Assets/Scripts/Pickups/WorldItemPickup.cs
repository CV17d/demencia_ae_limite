// ============================================================================
// WorldItemPickup.cs — Sistema de Ítems e Interacción en el Suelo (Sector Cero)
// ============================================================================
// Componente interactivo para objetos y armas situados en el entorno 3D.
// Detecta la proximidad del jugador mediante Triggers físicos, proyecta avisos
// diegéticos en pantalla ("Presiona [E] para recoger...") y coordina la entrega
// del recurso con PlayerInventory, PlayerWeaponEquipManager y PlayerHandController.
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.Events;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Player;
using DemenciaNoLimite.Horror;
using DemenciaNoLimite.UI;

#pragma warning disable CS0649

namespace DemenciaNoLimite.Pickups
{
    public enum WorldItemType
    {
        Weapon,         // Arma para equipar en el inventario del jugador
        Ammo,           // Munición de reserva para armas
        Health,         // Botiquín o inyector de curación
        SanitySerum,    // Tónico o suero de cordura (reduce la culpa/alucinaciones)
        Keycard,        // Tarjeta de seguridad o llave de acceso
        Currency,       // Fichas de torre para el Mercader
        CustomItem      // Objeto genérico o coleccionable narrativo
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Pickups/World Item Pickup")]
    public class WorldItemPickup : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE IDENTIDAD Y TIPO DE ÍTEM
        // ════════════════════════════════════════════════════════════════════

        [Header("Identidad del Ítem")]
        [Tooltip("Clasificación del recurso a recoger.")]
        [SerializeField] private WorldItemType _itemType = WorldItemType.Weapon;

        [Tooltip("Nombre legible mostrado en el indicador HUD (ej. 'Bingala', 'Demencia Revólver', 'Muak', 'Trabeca').")]
        [SerializeField] private string _itemName = "Pistola Táctica";

        [Tooltip("Descripción narrativa u observaciones opcionales del ítem.")]
        [TextArea(2, 3)]
        [SerializeField] private string _itemDescription = "";

        [Header("Configuración de Arma 3D (Solo si ItemType = Weapon)")]
        [Tooltip("Identificador único en el catálogo de armas (ej. 'bingala', 'revolver', 'demencia revolver', 'muak', 'trabeca'). Si está vacío, se deduce automáticamente del GameObject.")]
        [SerializeField] private string _weaponId = "";

        [Tooltip("Prefab 3D directo del arma a equipar (opcional). Si se asigna, se utiliza con máxima prioridad.")]
        [SerializeField] private GameObject _weapon3DPrefab;

        [Tooltip("ScriptableObject con las estadísticas, balística y arquetipo del arma.")]
        [SerializeField] private WeaponData _weaponData;

        [Header("Parámetros de Cantidad y Recurso")]
        [Tooltip("Cantidad entregada (ej. número de proyectiles, fichas o unidades).")]
        [SerializeField] private int _amount = 1;

        [Tooltip("Tipo de munición otorgada si el ítem es de tipo Munición.")]
        [SerializeField] private WeaponType _ammoType = WeaponType.Hitscan;

        [Tooltip("Cantidad de salud restaurada si el ítem es de Curación.")]
        [SerializeField] private float _healAmount = 25f;

        [Tooltip("Cantidad de culpa reducida si el ítem es Suero de Cordura.")]
        [SerializeField] private int _sanityRestoreAmount = 20;

        [Header("Inventario Dinámico (Survival Horror)")]
        [Tooltip("Datos del ítem en caso de integrarse con el panel de cuadrícula de InventoryUIController.")]
        [SerializeField] private InventoryItemData _inventoryItemData;

        // ════════════════════════════════════════════════════════════════════
        // ENTRADA E INTERACCIÓN POR TECLA
        // ════════════════════════════════════════════════════════════════════

        [Header("Entrada de Usuario e Interacción")]
        [Tooltip("Tecla requerida para recoger el ítem del suelo.")]
        [SerializeField] private KeyCode _interactKey = KeyCode.E;

        [Tooltip("Si está activo, requiere que el jugador esté mirando en dirección al ítem además de estar dentro del Trigger.")]
        [SerializeField] private bool _requireLookingAtItem = true;

        [Tooltip("Ángulo de tolerancia visual máximo (en grados) para considerar que el jugador apunta al ítem.")]
        [Range(20f, 90f)]
        [SerializeField] private float _lookAtAngleThreshold = 65f;

        [Tooltip("Destruye el GameObject tras ser recogido. Si es falso, solo se desactiva.")]
        [SerializeField] private bool _destroyOnPickup = true;

        // ════════════════════════════════════════════════════════════════════
        // EFECTOS VISUALES Y AUDIO (FEEDBACK DIEGÉTICO)
        // ════════════════════════════════════════════════════════════════════

        [Header("Audio y Retroalimentación")]
        [Tooltip("Clip de sonido reproducido al recoger el ítem (recarga, clic mecánico, consumo).")]
        [SerializeField] private AudioClip _pickupSound;

        [Tooltip("Volumen de reproducción del sonido de recogida.")]
        [Range(0f, 1f)]
        [SerializeField] private float _pickupSoundVolume = 0.9f;

        [Header("Animación en Reposo (Flotación / Rotación en Suelo)")]
        [Tooltip("Si está activo, aplica un sutil balanceo vertical y giro al ítem para facilitar su visibilidad en pasillos oscuros.")]
        [SerializeField] private bool _enableIdleAnimation = true;

        [Tooltip("Velocidad de oscilación vertical.")]
        [SerializeField] private float _bobSpeed = 2.2f;

        [Tooltip("Amplitud de elevación vertical.")]
        [SerializeField] private float _bobHeight = 0.06f;

        [Tooltip("Velocidad de rotación constante sobre el eje Y (grados por segundo).")]
        [SerializeField] private float _rotateSpeed = 25f;

        // ════════════════════════════════════════════════════════════════════
        // INTERFAZ DE USUARIO (HUD / AVISO EN PANTALLA)
        // ════════════════════════════════════════════════════════════════════

        [Header("Interfaz de Pantalla (HUD)")]
        [Tooltip("Acción mostrada en el mensaje (ej. 'RECOGER', 'TOMAR', 'EQUIPAR').")]
        [SerializeField] private string _actionLabel = "RECOGER";

        [Tooltip("Elemento de UI externo opcional (Canvas en escena o WorldSpace) que se activa al entrar al trigger.")]
        [SerializeField] private GameObject _externalPromptUI;

        [Tooltip("Dibuja un indicador en pantalla diegético mediante OnGUI si no hay un Canvas externo asignado.")]
        [SerializeField] private bool _showBuiltInHUD = true;

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS
        // ════════════════════════════════════════════════════════════════════

        [Header("Eventos")]
        [Tooltip("Se dispara en el momento exacto en que el ítem es recogido con éxito.")]
        [SerializeField] private UnityEvent _onItemPickedUp;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y CACHÉ
        // ════════════════════════════════════════════════════════════════════

        private bool _isPlayerInRange = false;
        private bool _isPlayerInPhysicalTrigger = false;
        private GameObject _cachedPlayerGo;
        private PlayerManager _cachedPlayerManager;
        private Camera _mainCamera;
        private Vector3 _initialLocalPosition;
        private Collider _triggerCollider;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public WorldItemType ItemType => _itemType;
        public string ItemName => _itemName;
        public string ItemDescription => _itemDescription;
        public string WeaponId => ObtenerWeaponId();
        public GameObject Weapon3DPrefab => _weapon3DPrefab;
        public WeaponData WeaponData => _weaponData;
        public bool IsPlayerInRange => _isPlayerInRange;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _initialLocalPosition = transform.localPosition;
            AsegurarColliderTrigger();
            AutoConfigurarNombreArma();
        }

        private void Start()
        {
            if (_externalPromptUI != null)
            {
                _externalPromptUI.SetActive(false);
            }
        }

        private void Update()
        {
            // Animación suave de reposo en el suelo
            if (_enableIdleAnimation)
            {
                ActualizarAnimacionReposo();
            }

            // Detección continua de interacción por Raycast de la cámara
            ActualizarInteraccionPorRaycast();

            // Detección de interacción por tecla
            if (_isPlayerInRange)
            {
                ProcesarEntradaInteraccion();
            }
        }

        private void OnDisable()
        {
            _isPlayerInRange = false;
            if (_externalPromptUI != null)
            {
                _externalPromptUI.SetActive(false);
            }
        }

        private void OnValidate()
        {
            AutoConfigurarNombreArma();
        }

        // ════════════════════════════════════════════════════════════════════
        // RESOLUCIÓN INTELIGENTE DE ID Y NOMBRE DE ARMA
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Configura programáticamente la identidad, nombre y prefab 3D del arma para este pickup.
        /// </summary>
        public void ConfigurarComoArma(string weaponId, string itemName, GameObject weapon3DPrefab, WeaponData weaponData = null)
        {
            _itemType = WorldItemType.Weapon;
            _weaponId = weaponId;
            _itemName = !string.IsNullOrEmpty(itemName) ? itemName : weaponId;
            _weapon3DPrefab = weapon3DPrefab;
            _weaponData = weaponData;
        }

        /// <summary>
        /// Resuelve de forma robusta y autónoma el identificador del arma, analizando campos serializados,
        /// prefabs asignados, datos de ScriptableObject y nombres en la jerarquía (ej. 'bingala', 'demencia revolver').
        /// </summary>
        public string ObtenerWeaponId()
        {
            // 1. Si se configuró un ID explícito
            if (!string.IsNullOrWhiteSpace(_weaponId))
            {
                return _weaponId.Trim();
            }

            // 2. Si se asignó un prefab 3D directo
            if (_weapon3DPrefab != null && !string.IsNullOrWhiteSpace(_weapon3DPrefab.name))
            {
                return _weapon3DPrefab.name.Trim();
            }

            // 3. Si se asignó un WeaponData
            if (_weaponData != null)
            {
                if (!string.IsNullOrWhiteSpace(_weaponData.WeaponID)) return _weaponData.WeaponID.Trim();
                if (!string.IsNullOrWhiteSpace(_weaponData.DisplayName)) return _weaponData.DisplayName.Trim();
                if (!string.IsNullOrWhiteSpace(_weaponData.name)) return _weaponData.name.Trim();
            }

            // 4. Analizar el nombre del propio GameObject (limpiando prefijos y sufijos de clonación/prefabs)
            string cleanedGoName = gameObject.name
                .Replace("(Clone)", "")
                .Replace("Pickup_", "")
                .Replace("Pickup", "")
                .Replace("Item_", "")
                .Replace("SM_", "")
                .Replace("WP_", "")
                .Replace("Arma_", "")
                .Trim();

            string lowerGo = cleanedGoName.ToLowerInvariant();
            if (lowerGo.Contains("bingala")) return "bingala";
            if (lowerGo.Contains("revolver") || lowerGo.Contains("demencia")) return "demencia revolver";
            if (lowerGo.Contains("muak")) return "muak";
            if (lowerGo.Contains("trabeca")) return "Trabeca";

            // 5. Analizar el nombre legible si no es el placeholder genérico
            if (!string.IsNullOrWhiteSpace(_itemName) && _itemName != "Pistola Táctica")
            {
                string lowerItem = _itemName.ToLowerInvariant();
                if (lowerItem.Contains("bingala")) return "bingala";
                if (lowerItem.Contains("revolver") || lowerItem.Contains("demencia")) return "demencia revolver";
                if (lowerItem.Contains("muak")) return "muak";
                if (lowerItem.Contains("trabeca")) return "Trabeca";
                return _itemName.Replace(" ", "");
            }

            return cleanedGoName;
        }

        private void AutoConfigurarNombreArma()
        {
            if (_itemType != WorldItemType.Weapon) return;

            string detectedId = ObtenerWeaponId();
            if (string.IsNullOrEmpty(detectedId)) return;

            // Si el nombre aún tiene el valor por defecto genérico "Pistola Táctica", adaptarlo diegéticamente
            if (string.IsNullOrEmpty(_itemName) || _itemName == "Pistola Táctica")
            {
                if (detectedId.Equals("bingala", StringComparison.OrdinalIgnoreCase)) _itemName = "Bingala";
                else if (detectedId.IndexOf("revolver", StringComparison.OrdinalIgnoreCase) >= 0 || detectedId.IndexOf("demencia", StringComparison.OrdinalIgnoreCase) >= 0) _itemName = "Demencia Revólver";
                else if (detectedId.Equals("trabeca", StringComparison.OrdinalIgnoreCase)) _itemName = "Trabeca";
                else if (detectedId.IndexOf("canoduplo", StringComparison.OrdinalIgnoreCase) >= 0) _itemName = "Canoduplo";
                else if (detectedId.Equals("muak", StringComparison.OrdinalIgnoreCase)) _itemName = "Muak (Obsoleta)";
                else
                {
                    _itemName = char.ToUpperInvariant(detectedId[0]) + detectedId.Substring(1);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DETECCIÓN FÍSICA MEDIANTE TRIGGER
        // ════════════════════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            if (EsJugadorValido(other, out GameObject playerGo))
            {
                _cachedPlayerGo = playerGo;
                _cachedPlayerManager = playerGo.GetComponentInParent<PlayerManager>() ?? PlayerManager.Instance;
                _isPlayerInPhysicalTrigger = true;
                _isPlayerInRange = true;

                if (_externalPromptUI != null)
                {
                    _externalPromptUI.SetActive(true);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (EsJugadorValido(other, out GameObject playerGo))
            {
                _isPlayerInPhysicalTrigger = false;
                if (_cachedPlayerGo == playerGo || _cachedPlayerGo == null)
                {
                    _isPlayerInRange = false;
                    _cachedPlayerGo = null;
                    _cachedPlayerManager = null;

                    if (_externalPromptUI != null)
                    {
                        _externalPromptUI.SetActive(false);
                    }
                }
            }
        }

        private bool EsJugadorValido(Collider col, out GameObject playerGo)
        {
            playerGo = null;
            if (col == null) return false;

            // Validación por Tag
            if (col.CompareTag("Player"))
            {
                playerGo = col.gameObject;
                return true;
            }

            // Validación por CharacterController o FirstPersonController
            if (col.GetComponent<CharacterController>() != null || col.GetComponentInParent<FirstPersonController>() != null)
            {
                playerGo = col.transform.root.gameObject;
                return true;
            }

            // Validación por PlayerManager
            var pm = col.GetComponentInParent<PlayerManager>();
            if (pm != null)
            {
                playerGo = pm.gameObject;
                return true;
            }

            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // LÓGICA DE RECOGIDA E INTEGRACIÓN DE SISTEMAS
        // ════════════════════════════════════════════════════════════════════

        private void ProcesarEntradaInteraccion()
        {
            if (!Input.GetKeyDown(_interactKey)) return;

            // Si se requiere mirar al ítem, validar ángulo con la cámara
            if (_requireLookingAtItem && !EstaMirandoAlItem())
            {
                return;
            }

            EjecutarRecogida();
        }

        /// <summary>
        /// Ejecuta la transferencia del ítem al jugador, instancia el arma 3D en el WeaponHolder,
        /// actualiza la mano diegética y retira el objeto del mundo.
        /// </summary>
        public void EjecutarRecogida()
        {
            PlayerManager pm = _cachedPlayerManager ?? PlayerManager.Instance;
            GameObject playerTarget = _cachedPlayerGo ?? pm?.gameObject;

            if (pm == null && playerTarget != null)
            {
                pm = playerTarget.GetComponentInParent<PlayerManager>();
            }

            // 1. Procesar según la categoría de ítem
            switch (_itemType)
            {
                case WorldItemType.Weapon:
                    RecogerArma(pm, playerTarget);
                    break;

                case WorldItemType.Ammo:
                    RecogerMunicion(pm, playerTarget);
                    break;

                case WorldItemType.Health:
                    RecogerSalud(pm);
                    break;

                case WorldItemType.SanitySerum:
                    RecogerSueroCordura();
                    break;

                case WorldItemType.Currency:
                    RecogerMonedas();
                    break;

                case WorldItemType.Keycard:
                case WorldItemType.CustomItem:
                    RecogerObjetoGenerico(pm);
                    break;
            }

            // Sincronizar con el nuevo sistema de cuadrícula del inventario (si aplica)
            if (_inventoryItemData != null && InventoryUIController.Instance != null)
            {
                InventoryUIController.Instance.AddItem(_inventoryItemData, Mathf.Max(1, _amount));
            }

            // 2. Notificar y actualizar la mano diegética del jugador
            ActualizarManoJugador(pm);

            // 3. Reproducir audio de feedback
            ReproducirSonidoRecogida();

            // 4. Disparar eventos registrados
            _onItemPickedUp?.Invoke();

            Debug.Log($"[WorldItemPickup] Ítem recogido: '{_itemName}' ({_itemType}) por el jugador.");

            // 5. Destruir o desactivar el objeto en el suelo
            FinalizarObjetoEnMundo();
        }

        private void RecogerArma(PlayerManager pm, GameObject playerTarget)
        {
            string resolvedId = ObtenerWeaponId();
            Debug.Log($"[WorldItemPickup] Procesando recogida de arma: '{resolvedId}' (GameObject: '{gameObject.name}')");

            // 1. Resolver inventario del jugador de forma ultra-defensiva (Singleton > PlayerManager > Target > Find)
            PlayerInventory inventory = PlayerInventory.Instance;
            if (inventory == null && pm != null)
            {
                inventory = pm.Inventory ?? pm.GetComponent<PlayerInventory>();
            }
            if (inventory == null && playerTarget != null)
            {
                inventory = playerTarget.GetComponentInParent<PlayerInventory>() 
                         ?? playerTarget.GetComponentInChildren<PlayerInventory>();
            }
            if (inventory == null)
            {
                inventory = FindAnyObjectByType<PlayerInventory>();
            }

            // Autogenerar PlayerInventory en el jugador si aún no existe
            if (inventory == null)
            {
                if (pm != null)
                {
                    inventory = pm.gameObject.AddComponent<PlayerInventory>();
                }
                else if (playerTarget != null)
                {
                    inventory = playerTarget.AddComponent<PlayerInventory>();
                }
            }

            // 2. Resolver gestor de equipamiento de armas 3D
            PlayerWeaponEquipManager equipManager = PlayerWeaponEquipManager.Instance;
            if (equipManager == null && pm != null)
            {
                equipManager = pm.WeaponEquipManager ?? pm.GetComponent<PlayerWeaponEquipManager>();
            }
            if (equipManager == null && playerTarget != null)
            {
                equipManager = playerTarget.GetComponentInChildren<PlayerWeaponEquipManager>() 
                            ?? playerTarget.GetComponentInParent<PlayerWeaponEquipManager>();
            }
            if (equipManager == null)
            {
                equipManager = FindAnyObjectByType<PlayerWeaponEquipManager>();
            }

            // 3. Preparar o buscar el WeaponData correspondiente
            WeaponData weaponToEquip = _weaponData;
            if (weaponToEquip == null && equipManager != null)
            {
                var mapping = equipManager.BuscarMapping(resolvedId, null);
                if (mapping != null && mapping.weaponData != null)
                {
                    weaponToEquip = mapping.weaponData;
                }
            }

            if (weaponToEquip == null)
            {
                weaponToEquip = ScriptableObject.CreateInstance<WeaponData>();
                weaponToEquip.WeaponID = resolvedId;
                weaponToEquip.DisplayName = (!string.IsNullOrEmpty(_itemName) && _itemName != "Pistola Táctica") ? _itemName : resolvedId;
                weaponToEquip.Type = _ammoType;
                weaponToEquip.MagazineSize = 12;
                weaponToEquip.MaxAmmo = 60;
                weaponToEquip.BaseDamage = 35f;
            }
            else if (string.IsNullOrEmpty(weaponToEquip.WeaponID))
            {
                weaponToEquip.WeaponID = resolvedId;
            }

            // 4. Agregar de forma obligatoria y garantizada al inventario
            if (inventory != null)
            {
                bool added = inventory.AddWeapon(weaponToEquip, autoEquip: true);
                Debug.Log($"[WorldItemPickup] ✅ Arma '{weaponToEquip.DisplayName}' ({weaponToEquip.WeaponID}) registrada en PlayerInventory. Éxito: {added}. Ranuras totales: {inventory.Slots.Count}");
            }
            else
            {
                Debug.LogError("[WorldItemPickup] Error crítico: No se pudo resolver ni crear PlayerInventory para registrar el arma.");
            }

            // 5. Llamada directa y garantizada a PlayerWeaponEquipManager para instanciar el modelo 3D bajo el WeaponHolder
            if (equipManager != null)
            {
                equipManager.EquipWeaponExplicit(resolvedId, weaponToEquip, _weapon3DPrefab);
            }
            else
            {
                Debug.LogWarning("[WorldItemPickup] No se encontró PlayerWeaponEquipManager en la escena para instanciar el arma 3D bajo la Main Camera.");
            }
        }

        private void RecogerMunicion(PlayerManager pm, GameObject playerTarget)
        {
            PlayerInventory inventory = pm != null ? pm.Inventory : playerTarget?.GetComponentInParent<PlayerInventory>();
            if (inventory != null)
            {
                inventory.AddReserveAmmo(_ammoType, Mathf.Max(1, _amount));
            }
        }

        private void RecogerSalud(PlayerManager pm)
        {
            if (GameManager.Instance != null && GameManager.Instance.Player != null)
            {
                GameManager.Instance.Player.Heal(_healAmount);
            }
            else if (pm != null && pm.Health != null)
            {
                GameManager.Instance?.Player?.Heal(_healAmount);
            }
        }

        private void RecogerSueroCordura()
        {
            if (SanityManager.Instance != null)
            {
                SanityManager.Instance.ReduceGuilt(_sanityRestoreAmount);
            }
        }

        private void RecogerMonedas()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Currency += Mathf.Max(1, _amount);
            }
        }

        private void RecogerObjetoGenerico(PlayerManager pm)
        {
            // Registro para llaves u objetivos de misión
        }

        private void ActualizarManoJugador(PlayerManager pm)
        {
            PlayerHandController hand = pm != null ? pm.HandController : null;
            if (hand == null)
            {
                hand = FindAnyObjectByType<PlayerHandController>();
            }

            if (hand != null)
            {
                hand.SetHandState(HandState.EquippedItem);
            }
        }

        private void ReproducirSonidoRecogida()
        {
            if (_pickupSound != null)
            {
                AudioSource.PlayClipAtPoint(_pickupSound, transform.position, _pickupSoundVolume);
            }
        }

        private void FinalizarObjetoEnMundo()
        {
            _isPlayerInRange = false;

            if (_externalPromptUI != null)
            {
                _externalPromptUI.SetActive(false);
            }

            if (_destroyOnPickup)
            {
                Destroy(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // VALIDACIÓN VISUAL (LOOK AT CHECK) Y ANIMACIÓN
        // ════════════════════════════════════════════════════════════════════

        private bool EstaMirandoAlItem()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return true;

            Vector3 dirToItem = (transform.position - _mainCamera.transform.position).normalized;
            float angle = Vector3.Angle(_mainCamera.transform.forward, dirToItem);

            return angle <= _lookAtAngleThreshold;
        }

        /// <summary>
        /// Detecta la interacción por Raycast directo cuando el jugador mira al ítem con la retícula.
        /// </summary>
        private void ActualizarInteraccionPorRaycast()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            // Si ya está físicamente dentro del trigger de proximidad, preservar estado
            if (_isPlayerInPhysicalTrigger) return;

            Ray ray = new Ray(_mainCamera.transform.position, _mainCamera.transform.forward);
            float maxRaycastDist = 3.2f;

            if (Physics.Raycast(ray, out RaycastHit hit, maxRaycastDist))
            {
                if (hit.collider != null && (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform)))
                {
                    if (!_isPlayerInRange)
                    {
                        PlayerManager pm = PlayerManager.Instance ?? FindAnyObjectByType<PlayerManager>();
                        _cachedPlayerManager = pm;
                        _cachedPlayerGo = pm != null ? pm.gameObject : null;
                        _isPlayerInRange = true;

                        if (_externalPromptUI != null)
                        {
                            _externalPromptUI.SetActive(true);
                        }
                    }
                    return;
                }
            }

            // Si estaba en rango por raycast y ya no se apunta al ítem, salir de rango
            if (_isPlayerInRange && !_isPlayerInPhysicalTrigger)
            {
                _isPlayerInRange = false;
                if (_externalPromptUI != null)
                {
                    _externalPromptUI.SetActive(false);
                }
            }
        }

        private void ActualizarAnimacionReposo()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                // Si el objeto está cayendo o acelerado por físicas, actualizar punto de anclaje y no forzar posición
                if (rb.linearVelocity.sqrMagnitude > 0.01f)
                {
                    _initialLocalPosition = transform.localPosition;
                    return;
                }
            }

            // Rotación suave continua
            if (_rotateSpeed > 0f)
            {
                transform.Rotate(Vector3.up, _rotateSpeed * Time.deltaTime, Space.World);
            }

            // Levitación senoidal suave
            if (_bobHeight > 0f)
            {
                float bobOffset = Mathf.Sin(Time.time * _bobSpeed) * _bobHeight;
                transform.localPosition = _initialLocalPosition + new Vector3(0f, bobOffset, 0f);
            }
        }

        private void AsegurarColliderTrigger()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);

            // 1. Si ya existe un collider marcado expresamente como Trigger, usarlo
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].isTrigger)
                {
                    _triggerCollider = colliders[i];
                    return;
                }
            }

            // 2. Si hay Rigidbody, mantener los colisionadores físicos sólidos (para reposar en el suelo)
            // y añadir un SphereCollider trigger secundario para proximidad
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                SphereCollider triggerSphere = gameObject.AddComponent<SphereCollider>();
                triggerSphere.isTrigger = true;
                triggerSphere.radius = 1.8f;
                _triggerCollider = triggerSphere;
            }
            else
            {
                // Si no hay Rigidbody, el collider existente puede operar como Trigger
                _triggerCollider = GetComponent<Collider>();
                if (_triggerCollider != null)
                {
                    _triggerCollider.isTrigger = true;
                }
                else
                {
                    // Fallback autónomo si el objeto carece de colisionador
                    BoxCollider defaultBox = gameObject.AddComponent<BoxCollider>();
                    defaultBox.isTrigger = true;
                    _triggerCollider = defaultBox;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // INTERFAZ DE USUARIO (ON GUI — HUD DIEGÉTICO)
        // ════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (!_showBuiltInHUD || !_isPlayerInRange) return;

            // Solo mostrar el aviso si el jugador está mirando al ítem (si está habilitada la opción)
            if (_requireLookingAtItem && !EstaMirandoAlItem()) return;

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            // Calcular distancia para atenuación de opacidad
            float dist = Vector3.Distance(_mainCamera.transform.position, transform.position);
            float alpha = Mathf.Clamp01(1f - (dist / 8f));
            if (alpha <= 0.05f) return;

            // Dimensiones del HUD inferior centrado
            float boxWidth = 320f;
            float boxHeight = 52f;
            float posX = (Screen.width * 0.5f) - (boxWidth * 0.5f);
            float posY = Screen.height * 0.72f;

            // Fondo translúcido industrial
            GUI.color = new Color(0.04f, 0.06f, 0.08f, alpha * 0.88f);
            GUI.Box(new Rect(posX, posY, boxWidth, boxHeight), GUIContent.none);

            // Borde superior temático de Sector Cero
            Color accentColor = (_itemType == WorldItemType.Weapon) ? new Color(0.2f, 0.85f, 0.95f, alpha) :
                                (_itemType == WorldItemType.Health) ? new Color(0.95f, 0.25f, 0.25f, alpha) :
                                (_itemType == WorldItemType.SanitySerum) ? new Color(0.75f, 0.35f, 0.95f, alpha) :
                                new Color(0.95f, 0.8f, 0.25f, alpha);

            GUI.color = accentColor;
            GUI.Box(new Rect(posX, posY, boxWidth, 2.5f), GUIContent.none);

            // Texto de instrucción principal: [E] RECOGER
            var styleAction = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            styleAction.normal.textColor = accentColor;
            string keyLabel = $"[{_interactKey}]";
            GUI.Label(new Rect(posX, posY + 4, boxWidth, 20), $"{keyLabel} {_actionLabel.ToUpper()}", styleAction);

            // Nombre del ítem
            var styleItem = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 14
            };
            styleItem.normal.textColor = new Color(0.95f, 0.95f, 0.95f, alpha);
            GUI.Label(new Rect(posX, posY + 24, boxWidth, 24), _itemName, styleItem);

            GUI.color = Color.white;
        }

        // ════════════════════════════════════════════════════════════════════
        // GIZMOS DE DEPURACIÓN EN ESCENA
        // ════════════════════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
            Collider col = GetComponent<Collider>();
            if (col is SphereCollider sphere)
            {
                Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius);
            }
            else if (col is BoxCollider box)
            {
                Gizmos.DrawWireCube(transform.position + box.center, box.size);
            }
            else
            {
                Gizmos.DrawWireSphere(transform.position, 1.8f);
            }
        }
    }
}
