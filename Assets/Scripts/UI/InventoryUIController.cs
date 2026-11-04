// ============================================================================
// InventoryUIController.cs — Controlador Integral de Inventario Survival Horror
// ============================================================================
// Controla el panel de cuadrícula dinámica estilo PS1 / Survival Horror:
// - Abre/cierra con la tecla [I], [Esc] o botón de retorno.
// - Congela/restaura Time.timeScale (0f / 1f) y desbloquea/bloquea el cursor del ratón.
// - Contenedor dinámico con GridLayoutGroup y ContentSizeFitter.
// - Generación de ranuras InventorySlotUI con marcos retro, iconos y stack count.
// - Panel lateral de detalles con TextMeshPro protegido contra cortes y desbordes.
// - Botones de acción contextuales: "Equipar" (armas), "Usar" (consumibles/munición) y "Tirar".
// - Compatibilidad nativa e inmediata con las armas actuales: Demencia Revólver, Trabeca y Bingala.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Player;
using DemenciaNoLimite.Horror;
using DemenciaNoLimite.Pickups;

namespace DemenciaNoLimite.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/UI/Inventory UI Controller")]
    public class InventoryUIController : MonoBehaviour
    {
        public static InventoryUIController Instance { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE ENTRADA Y TECLAS
        // ════════════════════════════════════════════════════════════════════

        [Header("Controles y Teclas")]
        [Tooltip("Tecla principal para abrir/cerrar el inventario (por defecto [I]).")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.I;

        [Tooltip("Tecla secundaria para abrir/cerrar (por defecto [Tab]).")]
        [SerializeField] private KeyCode _altToggleKey = KeyCode.Tab;

        [Tooltip("Tecla para cerrar si está abierto (por defecto [Escape]).")]
        [SerializeField] private KeyCode _escapeKey = KeyCode.Escape;

        [Tooltip("¿Pausar la escala de tiempo del juego (Time.timeScale = 0) al abrir?")]
        [SerializeField] private bool _pauseTimeWhenOpen = true;

        [Tooltip("¿Bloquear y ocultar el cursor del ratón al cerrar el inventario?")]
        [SerializeField] private bool _lockCursorOnClose = true;

        // ════════════════════════════════════════════════════════════════════
        // PARÁMETROS DEL INVENTARIO (CUADRÍCULA ESTILO SURVIVAL HORROR)
        // ════════════════════════════════════════════════════════════════════

        [Header("Capacidad de la Cuadrícula")]
        [Tooltip("Número total de casillas en el inventario (estándar Survival Horror: 12 ranuras).")]
        [Range(6, 24)]
        [SerializeField] private int _totalSlotsCount = 12;

        [Header("Ítems Iniciales / Catálogo")]
        [Tooltip("Lista de ScriptableObjects de ítems a registrar en las ranuras al iniciar.")]
        [SerializeField] private List<InventoryItemData> _startingItems = new List<InventoryItemData>();

        // ════════════════════════════════════════════════════════════════════
        // VINCULACIÓN CON CANVAS uGUI Y TEXTMESHPRO
        // ════════════════════════════════════════════════════════════════════

        [Header("Panel Principal uGUI")]
        [Tooltip("GameObject raíz del panel de inventario dentro del Canvas.")]
        [SerializeField] private GameObject _inventoryPanel;

        [Tooltip("Contenedor que posee los componentes GridLayoutGroup y ContentSizeFitter.")]
        [SerializeField] private Transform _gridContainer;

        [Tooltip("Prefab de la casilla de inventario (debe contener InventorySlotUI).")]
        [SerializeField] private GameObject _slotPrefab;

        [Header("Panel de Detalles y Acciones")]
        [Tooltip("Texto del título del ítem seleccionado.")]
        [SerializeField] private TMP_Text _detailNameText;

        [Tooltip("Texto que muestra la categoría (ARMA, MUNICIÓN, CONSUMIBLE, OBJETO CLAVE).")]
        [SerializeField] private TMP_Text _detailCategoryText;

        [Tooltip("Texto con la descripción completa del ítem (Auto-Sizing / Wrapping).")]
        [SerializeField] private TMP_Text _detailDescriptionText;

        [Tooltip("Imagen grande o previa del ítem seleccionado en el panel de detalles.")]
        [SerializeField] private Image _detailIconImage;

        [Tooltip("Botón contextual para Equipar / Desequipar armas.")]
        [SerializeField] private Button _equipActionButton;

        [Tooltip("Texto del botón de equipar.")]
        [SerializeField] private TMP_Text _equipButtonText;

        [Tooltip("Botón contextual para Usar consumibles de salud o recargar munición.")]
        [SerializeField] private Button _useActionButton;

        [Tooltip("Texto del botón de usar.")]
        [SerializeField] private TMP_Text _useButtonText;

        [Tooltip("Botón para soltar o tirar el ítem al suelo.")]
        [SerializeField] private Button _dropActionButton;

        [Tooltip("Botón para cerrar el inventario.")]
        [SerializeField] private Button _closeButton;

        // ════════════════════════════════════════════════════════════════════
        // AUDIO Y FEEDBACK RETRO
        // ════════════════════════════════════════════════════════════════════

        [Header("Audio y Efectos Sonoros")]
        [SerializeField] private AudioClip _openSound;
        [SerializeField] private AudioClip _closeSound;
        [SerializeField] private AudioClip _selectSound;
        [SerializeField] private AudioClip _equipSound;
        [SerializeField] private AudioClip _useSound;
        [SerializeField] private AudioClip _dropSound;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO DEL SISTEMA
        // ════════════════════════════════════════════════════════════════════

        private readonly List<InventorySlot> _slots = new List<InventorySlot>();
        private readonly List<InventorySlotUI> _slotViews = new List<InventorySlotUI>();
        private int _selectedSlotIndex = -1;
        private bool _isOpen = false;
        private float _previousTimeScale = 1.0f;
        private AudioSource _audioSource;

        private PlayerInventory _playerInventory;
        private PlayerWeaponEquipManager _weaponEquipManager;

        public bool IsOpen => _isOpen;
        public IReadOnlyList<InventorySlot> Slots => _slots;
        public int SelectedSlotIndex => _selectedSlotIndex;

        public event Action<bool> OnInventoryToggled;
        public event Action<int, InventorySlot> OnSlotSelected;
        public event Action OnInventoryUpdated;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            ConfigurarAudio();
            InicializarRanuras();
            VincularBotonesAccion();

            // Si no hay Canvas configurado en el Inspector, construir uno procedural PS1
            if (_inventoryPanel == null || _gridContainer == null)
            {
                ConstruirCanvasProceduralPS1();
            }

            if (_inventoryPanel != null)
            {
                _inventoryPanel.SetActive(false);
            }
        }

        private void Start()
        {
            ResolverReferenciasJugador();
            RegistrarArmasActualesPreexistentes();
            RefrescarVistaCompleta();
        }

        private void Update()
        {
            // Alternar con teclas [I] o [Tab]
            if (Input.GetKeyDown(_toggleKey) || (_altToggleKey != KeyCode.None && Input.GetKeyDown(_altToggleKey)))
            {
                ToggleInventory();
            }
            // Cerrar con [Escape] si está abierto
            else if (_isOpen && Input.GetKeyDown(_escapeKey))
            {
                CloseInventory();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_isOpen && _pauseTimeWhenOpen)
            {
                Time.timeScale = 1f;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE CURSOR, PAUSA Y APERTURA / CIERRE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Alterna el estado de apertura/cierre del panel del inventario.
        /// </summary>
        public void ToggleInventory()
        {
            if (_isOpen)
            {
                CloseInventory();
            }
            else
            {
                OpenInventory();
            }
        }

        /// <summary>
        /// Abre el inventario, congela el tiempo y libera el cursor para la interacción.
        /// </summary>
        public void OpenInventory()
        {
            if (_isOpen) return;

            ResolverReferenciasJugador();
            SincronizarArmasConPlayerInventory();

            _isOpen = true;
            _previousTimeScale = Time.timeScale;

            if (_pauseTimeWhenOpen)
            {
                Time.timeScale = 0f;
            }

            // Desbloquear y hacer visible el cursor del ratón
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_inventoryPanel != null)
            {
                _inventoryPanel.SetActive(true);
            }

            // Si no hay slot seleccionado, seleccionar el primero que tenga ítem
            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count || _slots[_selectedSlotIndex].IsEmpty)
            {
                _selectedSlotIndex = EncontrarPrimerSlotConItem();
            }

            RefrescarVistaCompleta();
            ReproducirSonido(_openSound);

            OnInventoryToggled?.Invoke(true);
            Debug.Log("[InventoryUIController] 🎒 Inventario Survival Horror abierto. Cursor liberado, escala de tiempo pausada.");
        }

        /// <summary>
        /// Cierra el inventario, restaura el tiempo y bloquea el cursor en primera persona.
        /// </summary>
        public void CloseInventory()
        {
            if (!_isOpen) return;

            _isOpen = false;

            if (_pauseTimeWhenOpen)
            {
                Time.timeScale = (_previousTimeScale > 0f) ? _previousTimeScale : 1f;
            }

            // Bloquear nuevamente el cursor del ratón en primera persona
            if (_lockCursorOnClose)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (_inventoryPanel != null)
            {
                _inventoryPanel.SetActive(false);
            }

            ReproducirSonido(_closeSound);

            OnInventoryToggled?.Invoke(false);
            Debug.Log("[InventoryUIController] 🔒 Inventario cerrado. Cursor bloqueado en primera persona y tiempo restaurado.");
        }

        // ════════════════════════════════════════════════════════════════════
        // INICIALIZACIÓN Y GESTIÓN DE RANURAS
        // ════════════════════════════════════════════════════════════════════

        private void InicializarRanuras()
        {
            _slots.Clear();
            for (int i = 0; i < _totalSlotsCount; i++)
            {
                _slots.Add(new InventorySlot());
            }

            // Cargar ítems iniciales asignados en el Inspector
            if (_startingItems != null)
            {
                foreach (var itemData in _startingItems)
                {
                    if (itemData != null)
                    {
                        AddItem(itemData, 1);
                    }
                }
            }
        }

        /// <summary>
        /// Registra automáticamente las armas existentes del proyecto (Demencia Revólver, Trabeca, Bingala)
        /// en las primeras ranuras del inventario.
        /// </summary>
        private void RegistrarArmasActualesPreexistentes()
        {
            // Catálogo predeterminado de armas emblemáticas de Demencia No Límite
            RegistrarArmaPorDefecto(
                id: "demencia revolver",
                nombre: "Demencia Revólver",
                descripcion: "Revólver de gran calibre cargado con tambor pesado. Fiable y letal a media distancia en los pisos de la Torre."
            );

            RegistrarArmaPorDefecto(
                id: "Trabeca",
                nombre: "Trabeca",
                descripcion: "Arma escopeta de cañón recortado. Dispersión devastadora y brutal a quemarropa contra aberraciones."
            );

            RegistrarArmaPorDefecto(
                id: "bingala",
                nombre: "Bingala",
                descripcion: "Pistola automática ligera modificada con cargador rápido. Disparo veloz ideal para control de masas."
            );
        }

        private void RegistrarArmaPorDefecto(string id, string nombre, string descripcion)
        {
            // Si ya existe en alguna ranura, no duplicar
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].ItemData != null &&
                    (string.Equals(_slots[i].ItemData.Id, id, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_slots[i].ItemData.ItemName, nombre, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }

            // Crear ítem ScriptableObject en runtime para el arma
            var weaponItem = InventoryItemData.CreateWeaponItem(id, nombre, descripcion, null);
            AddItem(weaponItem, 1);
        }

        /// <summary>
        /// Sincroniza la lista de ranuras con las armas activas de PlayerInventory y PlayerWeaponEquipManager.
        /// </summary>
        private void SincronizarArmasConPlayerInventory()
        {
            ResolverReferenciasJugador();

            if (_playerInventory == null) return;

            var activeWeapon = _playerInventory.ActiveWeaponInstance;
            string activeWeaponId = activeWeapon?.Data != null
                ? (!string.IsNullOrEmpty(activeWeapon.Data.WeaponID) ? activeWeapon.Data.WeaponID : activeWeapon.Data.DisplayName)
                : "";

            // También sincronizar todas las armas que ya estén en PlayerInventory.Slots
            var playerSlots = _playerInventory.Slots;
            if (playerSlots != null)
            {
                for (int i = 0; i < playerSlots.Count; i++)
                {
                    var wSlot = playerSlots[i];
                    if (wSlot == null || wSlot.Data == null) continue;

                    string wId = !string.IsNullOrEmpty(wSlot.Data.WeaponID) ? wSlot.Data.WeaponID : wSlot.Data.DisplayName;
                    string wName = !string.IsNullOrEmpty(wSlot.Data.DisplayName) ? wSlot.Data.DisplayName : wId;

                    // Buscar si ya la tenemos en _slots
                    bool alreadyTracked = false;
                    for (int s = 0; s < _slots.Count; s++)
                    {
                        if (_slots[s].ItemData != null && _slots[s].ItemData.Type == ItemType.Weapon)
                        {
                            if (string.Equals(_slots[s].ItemData.WeaponId, wId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(_slots[s].ItemData.ItemName, wName, StringComparison.OrdinalIgnoreCase))
                            {
                                alreadyTracked = true;
                                break;
                            }
                        }
                    }

                    if (!alreadyTracked)
                    {
                        var newItem = InventoryItemData.CreateWeaponItem(wId, wName, wSlot.Data.Description, wSlot.Data);
                        AddItem(newItem, 1);
                    }
                }
            }

            // Actualizar estado 'isEquipped' en cada ranura
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.ItemData != null && s.ItemData.Type == ItemType.Weapon)
                {
                    bool isEquipped = !string.IsNullOrEmpty(activeWeaponId) &&
                        (string.Equals(s.ItemData.WeaponId, activeWeaponId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(s.ItemData.ItemName, activeWeaponId, StringComparison.OrdinalIgnoreCase));

                    s.IsEquipped = isEquipped;
                }
                else
                {
                    s.IsEquipped = false;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // OPERACIONES DE INVENTARIO (AGREGAR, USAR, EQUIPAR, TIRAR)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Agrega un ítem al inventario, respetando apilamiento o buscando la primera ranura libre.
        /// </summary>
        public bool AddItem(InventoryItemData itemData, int amount = 1)
        {
            if (itemData == null || amount <= 0) return false;

            int remaining = amount;

            // 1. Intentar apilar en ranuras existentes que contengan el mismo ítem (si maxStack > 1)
            if (itemData.MaxStack > 1)
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (!_slots[i].IsEmpty && _slots[i].ItemData != null &&
                        string.Equals(_slots[i].ItemData.Id, itemData.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        remaining = _slots[i].AddStack(remaining);
                        if (remaining <= 0)
                        {
                            RefrescarVistaCompleta();
                            OnInventoryUpdated?.Invoke();
                            return true;
                        }
                    }
                }
            }

            // 2. Colocar en la primera ranura libre
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    int stackToAdd = Mathf.Min(remaining, itemData.MaxStack);
                    _slots[i] = new InventorySlot(itemData, stackToAdd, false);
                    remaining -= stackToAdd;

                    if (remaining <= 0)
                    {
                        RefrescarVistaCompleta();
                        OnInventoryUpdated?.Invoke();
                        return true;
                    }
                }
            }

            RefrescarVistaCompleta();
            OnInventoryUpdated?.Invoke();
            return remaining < amount;
        }

        /// <summary>
        /// Equipa el arma ubicada en el índice de ranura seleccionado.
        /// </summary>
        public void EquipSelectedItem()
        {
            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count) return;

            var slot = _slots[_selectedSlotIndex];
            if (slot.IsEmpty || slot.ItemData == null || slot.ItemData.Type != ItemType.Weapon) return;

            ResolverReferenciasJugador();

            string weaponId = slot.ItemData.WeaponId;
            string weaponName = slot.ItemData.ItemName;
            WeaponData weaponData = slot.ItemData.WeaponData;

            Debug.Log($"[InventoryUIController] ⚔️ Equipando arma desde inventario: '{weaponName}' ({weaponId})");

            // 1. Comunicar con PlayerInventory
            if (_playerInventory != null)
            {
                // Asegurar que el arma existe en PlayerInventory
                if (!_playerInventory.ContainsWeapon(weaponId) && !_playerInventory.ContainsWeapon(weaponName))
                {
                    if (weaponData == null)
                    {
                        weaponData = ScriptableObject.CreateInstance<WeaponData>();
                        weaponData.WeaponID = weaponId;
                        weaponData.DisplayName = weaponName;
                        weaponData.Description = slot.ItemData.Description;
                    }
                    _playerInventory.AddWeapon(weaponData, autoEquip: true);
                }
                else
                {
                    _playerInventory.EquipWeapon(weaponId);
                }
            }

            // 2. Comunicar con PlayerWeaponEquipManager para visualización 3D
            if (_weaponEquipManager == null)
            {
                _weaponEquipManager = PlayerWeaponEquipManager.Instance ?? FindAnyObjectByType<PlayerWeaponEquipManager>();
            }

            if (_weaponEquipManager != null)
            {
                _weaponEquipManager.EquipWeaponExplicit(weaponId, weaponData, null);
            }

            // 3. Actualizar flags de equipamiento en slots
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].IsEquipped = (i == _selectedSlotIndex);
            }

            ReproducirSonido(_equipSound);
            RefrescarVistaCompleta();
        }

        /// <summary>
        /// Consume el ítem seleccionado (aplica salud, cordura o munición).
        /// </summary>
        public void UseSelectedItem()
        {
            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count) return;

            var slot = _slots[_selectedSlotIndex];
            if (slot.IsEmpty || slot.ItemData == null) return;

            var item = slot.ItemData;
            bool consumedSuccess = false;

            // Procesar Consumible (Salud / Cordura)
            if (item.Type == ItemType.Consumable)
            {
                // 1. Curación de Salud
                if (item.HealAmount > 0f)
                {
                    if (GameManager.Instance != null && GameManager.Instance.Player != null)
                    {
                        GameManager.Instance.Player.Heal(item.HealAmount);
                        Debug.Log($"[InventoryUIController] 💊 Curación aplicada: +{item.HealAmount} HP.");
                        consumedSuccess = true;
                    }
                }

                // 2. Restauración de Cordura
                if (item.SanityRestoreAmount > 0)
                {
                    var sanityMgr = SanityManager.Instance ?? FindAnyObjectByType<SanityManager>();
                    if (sanityMgr != null)
                    {
                        sanityMgr.ReduceGuilt(item.SanityRestoreAmount);
                        Debug.Log($"[InventoryUIController] 🧠 Cordura restaurada: -{item.SanityRestoreAmount} Culpa.");
                        consumedSuccess = true;
                    }
                }
            }
            // Procesar Munición
            else if (item.Type == ItemType.Ammo)
            {
                ResolverReferenciasJugador();
                if (_playerInventory != null)
                {
                    _playerInventory.AddReserveAmmo(item.AmmoWeaponType, item.AmmoAmount);
                    Debug.Log($"[InventoryUIController] 📦 Munición recargada: +{item.AmmoAmount} de {item.AmmoWeaponType}.");
                    consumedSuccess = true;
                }
            }

            if (consumedSuccess)
            {
                ReproducirSonido(_useSound);

                // Descontar una unidad del stack
                slot.Consume(1);
                RefrescarVistaCompleta();
                OnInventoryUpdated?.Invoke();
            }
        }

        /// <summary>
        /// Tira o suelta el ítem seleccionado al suelo frente al jugador.
        /// </summary>
        public void DropSelectedItem()
        {
            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count) return;

            var slot = _slots[_selectedSlotIndex];
            if (slot.IsEmpty || slot.ItemData == null) return;

            var item = slot.ItemData;

            // Si es un arma equipada actualmente, desequiparla
            if (slot.IsEquipped)
            {
                if (_weaponEquipManager != null) _weaponEquipManager.UnequipCurrentWeapon();
                slot.IsEquipped = false;
            }

            // Instanciar en el mundo físico frente al jugador
            Transform playerT = PlayerManager.Instance?.transform ?? FindAnyObjectByType<FirstPersonController>()?.transform;
            Vector3 dropPosition = playerT != null
                ? playerT.position + playerT.forward * 1.2f + Vector3.up * 0.3f
                : transform.position + Vector3.forward * 1.2f;

            if (item.WorldDropPrefab != null)
            {
                Instantiate(item.WorldDropPrefab, dropPosition, Quaternion.identity);
            }
            else
            {
                // Fallback: crear un pickup físico representativo con WorldItemPickup
                GameObject dropGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dropGo.name = $"Dropped_{item.ItemName}";
                dropGo.transform.position = dropPosition;
                dropGo.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

                var pickup = dropGo.AddComponent<WorldItemPickup>();
                // Configurar tipo en WorldItemPickup
                if (item.Type == ItemType.Weapon)
                {
                    dropGo.name = $"Pickup_Weapon_{item.WeaponId}";
                }

                var rb = dropGo.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.AddForce((playerT != null ? playerT.forward : Vector3.forward) * 2f, ForceMode.Impulse);
            }

            ReproducirSonido(_dropSound);
            Debug.Log($"[InventoryUIController] 🗑️ Ítem soltado en el suelo: '{item.ItemName}'.");

            // Reducir cantidad o vaciar ranura
            slot.Consume(1);
            RefrescarVistaCompleta();
            OnInventoryUpdated?.Invoke();
        }

        /// <summary>
        /// Registra o recoge un arma en la cuadrícula del inventario.
        /// - Si el arma ya existe: no duplica la ranura y suma su munición a la reserva.
        /// - Si es un arma nueva: agrega dinámicamente una nueva ranura con sus datos y la equipa.
        /// </summary>
        public bool RegisterOrPickupWeapon(string weaponId, string displayName, WeaponData weaponData, Sprite icon, int ammoAmount, out bool wasAddedAsNewSlot)
        {
            wasAddedAsNewSlot = false;
            if (string.IsNullOrEmpty(weaponId)) return false;

            ResolverReferenciasJugador();

            // 1. COMPROBAR SI EL ARMA YA EXISTE EN EL INVENTARIO
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s != null && !s.IsEmpty && s.ItemData != null && s.ItemData.Type == ItemType.Weapon)
                {
                    if (string.Equals(s.ItemData.WeaponId, weaponId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.ItemData.ItemName, displayName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(s.ItemData.Id) && string.Equals(s.ItemData.Id, weaponId, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Ya tiene el arma: Sumar munición a la reserva en lugar de duplicar
                        WeaponType ammoType = (weaponData != null) ? weaponData.Type : (s.ItemData.WeaponData != null ? s.ItemData.WeaponData.Type : WeaponType.Hitscan);
                        int ammoToAdd = ammoAmount > 0 ? ammoAmount : ((weaponData != null && weaponData.MagazineSize > 0) ? weaponData.MagazineSize : 15);

                        if (_playerInventory != null)
                        {
                            _playerInventory.AddReserveAmmo(ammoType, ammoToAdd);
                        }

                        Debug.Log($"[InventoryUIController] 📦 Arma duplicada '{displayName}' recogida. Se sumaron {ammoToAdd} proyectiles a la reserva de {ammoType}.");

                        RefrescarVistaCompleta();
                        OnInventoryUpdated?.Invoke();
                        return true;
                    }
                }
            }

            // 2. ES UN ARMA NUEVA: AGREGAR RANURA A LA CUADRÍCULA
            wasAddedAsNewSlot = true;
            var newWeaponItem = InventoryItemData.CreateWeaponItem(
                id: weaponId,
                displayName: !string.IsNullOrEmpty(displayName) ? displayName : weaponId,
                desc: weaponData != null && !string.IsNullOrEmpty(weaponData.Description) ? weaponData.Description : "Arma de combate recuperada del sector de la Torre.",
                wData: weaponData
            );

            if (icon != null)
            {
                newWeaponItem.ItemIcon = icon;
            }

            // Buscar primer casillero libre o crear uno adicional si la cuadrícula está llena
            int targetSlotIndex = -1;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    targetSlotIndex = i;
                    break;
                }
            }

            if (targetSlotIndex < 0)
            {
                // Expandir cuadrícula dinámicamente
                var newSlot = new InventorySlot();
                _slots.Add(newSlot);
                targetSlotIndex = _slots.Count - 1;
            }

            _slots[targetSlotIndex] = new InventorySlot(newWeaponItem, 1, false);

            // Registrar en PlayerInventory
            if (_playerInventory != null)
            {
                if (weaponData == null)
                {
                    weaponData = ScriptableObject.CreateInstance<WeaponData>();
                    weaponData.WeaponID = weaponId;
                    weaponData.DisplayName = displayName;
                    weaponData.BaseDamage = 30f;
                    weaponData.MagazineSize = 12;
                }
                _playerInventory.AddWeapon(weaponData, autoEquip: true);
            }

            // Equipar en primera persona
            if (_weaponEquipManager == null)
            {
                _weaponEquipManager = PlayerWeaponEquipManager.Instance ?? FindAnyObjectByType<PlayerWeaponEquipManager>();
            }
            if (_weaponEquipManager != null)
            {
                _weaponEquipManager.EquipWeaponExplicit(weaponId, weaponData, null);
            }

            // Marcar equipada
            _selectedSlotIndex = targetSlotIndex;
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].IsEquipped = (i == targetSlotIndex);
            }

            RefrescarVistaCompleta();
            OnInventoryUpdated?.Invoke();

            Debug.Log($"[InventoryUIController] ✅ Nueva arma agregada a la cuadrícula: '{displayName}' (Ranura {targetSlotIndex + 1}).");
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // ACTUALIZACIÓN DE VISTA Y DETALLES
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Refresca la cuadrícula completa de ranuras y el panel lateral de detalles.
        /// </summary>
        public void RefrescarVistaCompleta()
        {
            if (_gridContainer == null) return;

            AsegurarVistasDeRanuras();

            for (int i = 0; i < _slots.Count; i++)
            {
                int index = i;
                if (i < _slotViews.Count && _slotViews[i] != null)
                {
                    bool isSelected = (index == _selectedSlotIndex);
                    _slotViews[i].SetupSlot(index, _slots[i], isSelected, OnSlotClicked);
                }
            }

            ActualizarPanelDetalles();
        }

        private void OnSlotClicked(int slotIndex)
        {
            _selectedSlotIndex = slotIndex;
            ReproducirSonido(_selectSound);

            for (int i = 0; i < _slotViews.Count; i++)
            {
                if (_slotViews[i] != null)
                {
                    _slotViews[i].SetSelected(i == _selectedSlotIndex);
                }
            }

            ActualizarPanelDetalles();

            // Si el slot contiene un arma, empuñarla de inmediato en primera persona
            if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slots.Count)
            {
                var slot = _slots[_selectedSlotIndex];
                if (slot != null && !slot.IsEmpty && slot.ItemData != null && slot.ItemData.Type == ItemType.Weapon)
                {
                    EquipSelectedItem();
                }
            }

            OnSlotSelected?.Invoke(_selectedSlotIndex, (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slots.Count) ? _slots[_selectedSlotIndex] : null);
        }

        private void ActualizarPanelDetalles()
        {
            bool hasValidSelection = _selectedSlotIndex >= 0 &&
                                     _selectedSlotIndex < _slots.Count &&
                                     !_slots[_selectedSlotIndex].IsEmpty &&
                                     _slots[_selectedSlotIndex].ItemData != null;

            if (!hasValidSelection)
            {
                if (_detailNameText != null) _detailNameText.text = "VACÍO";
                if (_detailCategoryText != null) _detailCategoryText.text = "---";
                if (_detailDescriptionText != null) _detailDescriptionText.text = "Selecciona un casillero para inspeccionar sus propiedades o utilizarlo.";
                if (_detailIconImage != null) _detailIconImage.gameObject.SetActive(false);

                if (_equipActionButton != null) _equipActionButton.gameObject.SetActive(false);
                if (_useActionButton != null) _useActionButton.gameObject.SetActive(false);
                if (_dropActionButton != null) _dropActionButton.gameObject.SetActive(false);
                return;
            }

            var slot = _slots[_selectedSlotIndex];
            var item = slot.ItemData;

            // 1. Título y Categoría
            if (_detailNameText != null)
            {
                _detailNameText.text = item.ItemName.ToUpperInvariant();
                ConfigurarTMPParaSeguridadDeTexto(_detailNameText);
            }

            if (_detailCategoryText != null)
            {
                _detailCategoryText.text = $"[ {ObtenerEtiquetaCategoria(item.Type)} ]";
            }

            // 2. Descripción narrativa
            if (_detailDescriptionText != null)
            {
                _detailDescriptionText.text = item.Description;
                ConfigurarTMPParaSeguridadDeTexto(_detailDescriptionText);
            }

            // 3. Previsualización de Icono
            if (_detailIconImage != null)
            {
                if (item.ItemIcon != null)
                {
                    _detailIconImage.sprite = item.ItemIcon;
                    _detailIconImage.color = Color.white;
                    _detailIconImage.preserveAspect = true;
                    _detailIconImage.gameObject.SetActive(true);
                }
                else
                {
                    _detailIconImage.gameObject.SetActive(false);
                }
            }

            // 4. Configurar Botones Contextuales
            ConfigurarBotonesContextuales(slot);
        }

        private void ConfigurarBotonesContextuales(InventorySlot slot)
        {
            var item = slot.ItemData;

            // Botón Equipar (Exclusivo para armas)
            if (_equipActionButton != null)
            {
                bool isWeapon = (item.Type == ItemType.Weapon);
                _equipActionButton.gameObject.SetActive(isWeapon);

                if (isWeapon && _equipButtonText != null)
                {
                    _equipButtonText.text = slot.IsEquipped ? "★ EQUIPADA" : "EQUIPAR";
                }
            }

            // Botón Usar (Consumibles y Munición)
            if (_useActionButton != null)
            {
                bool canUse = (item.Type == ItemType.Consumable || item.Type == ItemType.Ammo);
                _useActionButton.gameObject.SetActive(canUse);

                if (canUse && _useButtonText != null)
                {
                    _useButtonText.text = (item.Type == ItemType.Ammo) ? "RECARGAR" : "USAR";
                }
            }

            // Botón Tirar (Disponible para cualquier ítem no vacío)
            if (_dropActionButton != null)
            {
                _dropActionButton.gameObject.SetActive(true);
            }
        }

        private string ObtenerEtiquetaCategoria(ItemType type)
        {
            switch (type)
            {
                case ItemType.Weapon: return "CATEGORÍA: ARMA DE COMBATE";
                case ItemType.Ammo: return "CATEGORÍA: MUNICIÓN DE RESERVA";
                case ItemType.Consumable: return "CATEGORÍA: CONSUMIBLE / CURACIÓN";
                case ItemType.KeyItem: return "CATEGORÍA: OBJETO CLAVE";
                default: return "CATEGORÍA: ÍTEM DIVERSO";
            }
        }

        private void ConfigurarTMPParaSeguridadDeTexto(TMP_Text tmp)
        {
            if (tmp == null) return;
            InventorySlotUI.AsegurarFuenteTMP(tmp);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 10f;
            tmp.fontSizeMax = Mathf.Max(16f, tmp.fontSize);
        }

        private int EncontrarPrimerSlotConItem()
        {
            // Priorizar slot del arma actualmente equipada
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].IsEmpty && _slots[i].IsEquipped) return i;
            }

            // Fallback al primer slot con cualquier ítem
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].IsEmpty) return i;
            }
            return 0;
        }


        // ════════════════════════════════════════════════════════════════════
        // VINCULACIÓN DE BOTONES Y CONSTRUCCIÓN DE SLOTS
        // ════════════════════════════════════════════════════════════════════

        private void VincularBotonesAccion()
        {
            if (_equipActionButton != null)
            {
                _equipActionButton.onClick.RemoveAllListeners();
                _equipActionButton.onClick.AddListener(EquipSelectedItem);
            }

            if (_useActionButton != null)
            {
                _useActionButton.onClick.RemoveAllListeners();
                _useActionButton.onClick.AddListener(UseSelectedItem);
            }

            if (_dropActionButton != null)
            {
                _dropActionButton.onClick.RemoveAllListeners();
                _dropActionButton.onClick.AddListener(DropSelectedItem);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveAllListeners();
                _closeButton.onClick.AddListener(CloseInventory);
            }
        }

        private void AsegurarVistasDeRanuras()
        {
            if (_gridContainer == null) return;

            // Si ya hay suficientes vistas generadas
            while (_slotViews.Count < _slots.Count)
            {
                int nextIndex = _slotViews.Count;
                GameObject slotObj;

                if (_slotPrefab != null)
                {
                    slotObj = Instantiate(_slotPrefab, _gridContainer);
                }
                else
                {
                    slotObj = CrearRanuraProceduralPS1(_gridContainer, nextIndex);
                }

                var slotView = slotObj.GetComponent<InventorySlotUI>() ?? slotObj.AddComponent<InventorySlotUI>();
                _slotViews.Add(slotView);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RESOLUCIÓN DE REFERENCIAS Y AUDIO
        // ════════════════════════════════════════════════════════════════════

        private void ResolverReferenciasJugador()
        {
            if (_playerInventory == null)
            {
                _playerInventory = PlayerInventory.Instance
                    ?? PlayerManager.Instance?.Inventory
                    ?? FindAnyObjectByType<PlayerInventory>();
            }

            if (_weaponEquipManager == null)
            {
                _weaponEquipManager = PlayerWeaponEquipManager.Instance
                    ?? PlayerManager.Instance?.WeaponEquipManager
                    ?? FindAnyObjectByType<PlayerWeaponEquipManager>();
            }
        }

        private void ConfigurarAudio()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 0f;
            }
        }

        private void ReproducirSonido(AudioClip clip)
        {
            if (clip != null && _audioSource != null)
            {
                try { _audioSource.PlayOneShot(clip); } catch { }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR PROCEDURAL DE CANVAS uGUI PS1 (AUTONOMÍA TOTAL)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Genera de forma limpia y procedural toda la jerarquía de UI con GridLayoutGroup,
        /// ContentSizeFitter y paneles retro si la escena no tiene un Canvas previamente enlazado.
        /// </summary>
        private void ConstruirCanvasProceduralPS1()
        {
            // 1. Buscar o instanciar Canvas principal
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGo = new GameObject("Canvas_Inventory_SurvivalHorror");
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            // 2. Panel Raíz Flotante (Marco estilo PS1)
            GameObject rootPanel = new GameObject("Panel_Inventario_SurvivalHorror");
            rootPanel.transform.SetParent(canvas.transform, false);
            var rootRt = rootPanel.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(980f, 620f);

            var rootImg = rootPanel.AddComponent<Image>();
            rootImg.color = new Color(0.05f, 0.07f, 0.09f, 0.95f); // Grafito oscuro PS1

            _inventoryPanel = rootPanel;

            // 3. Encabezado Retro
            GameObject headerGo = new GameObject("Text_Encabezado");
            headerGo.transform.SetParent(rootPanel.transform, false);
            var headerRt = headerGo.AddComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -18f);
            headerRt.sizeDelta = new Vector2(0f, 40f);

            var headerTmp = headerGo.AddComponent<TextMeshProUGUI>();
            headerTmp.text = "INVENTARIO PERSONAL — SECTOR CERO";
            headerTmp.fontSize = 24;
            headerTmp.fontStyle = FontStyles.Bold;
            headerTmp.alignment = TextAlignmentOptions.Center;
            headerTmp.color = new Color(0.95f, 0.85f, 0.35f, 1.0f); // Ámbar CRT
            InventorySlotUI.AsegurarFuenteTMP(headerTmp);

            // 4. Panel Izquierdo: Contenedor con GridLayoutGroup y ContentSizeFitter
            GameObject gridPanelGo = new GameObject("Grid_Container_Panel");
            gridPanelGo.transform.SetParent(rootPanel.transform, false);
            var gridPanelRt = gridPanelGo.AddComponent<RectTransform>();
            gridPanelRt.anchorMin = new Vector2(0f, 0f);
            gridPanelRt.anchorMax = new Vector2(0.60f, 1f);
            gridPanelRt.offsetMin = new Vector2(30f, 30f);
            gridPanelRt.offsetMax = new Vector2(-15f, -70f);

            var gridLayout = gridPanelGo.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(116f, 116f); // Casillas cuadradas PS1 ampliadas
            gridLayout.spacing = new Vector2(12f, 12f);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 4; // 4 columnas x 3 filas = 12 casillas
            gridLayout.childAlignment = TextAnchor.UpperLeft;

            var fitter = gridPanelGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _gridContainer = gridPanelGo.transform;

            // 5. Panel Derecho: Detalles e Inspección
            GameObject detailPanelGo = new GameObject("Detail_Panel");
            detailPanelGo.transform.SetParent(rootPanel.transform, false);
            var detailRt = detailPanelGo.AddComponent<RectTransform>();
            detailRt.anchorMin = new Vector2(0.60f, 0f);
            detailRt.anchorMax = new Vector2(1f, 1f);
            detailRt.offsetMin = new Vector2(15f, 30f);
            detailRt.offsetMax = new Vector2(-30f, -70f);

            var detailBg = detailPanelGo.AddComponent<Image>();
            detailBg.color = new Color(0.08f, 0.11f, 0.14f, 0.85f);

            // Título de Ítem
            GameObject titleGo = new GameObject("Detail_ItemName");
            titleGo.transform.SetParent(detailPanelGo.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -16f);
            titleRt.sizeDelta = new Vector2(-30f, 38f);
            _detailNameText = titleGo.AddComponent<TextMeshProUGUI>();
            _detailNameText.fontSize = 20;
            _detailNameText.fontStyle = FontStyles.Bold;
            _detailNameText.alignment = TextAlignmentOptions.Left;
            _detailNameText.color = new Color(1.0f, 0.90f, 0.40f);
            ConfigurarTMPParaSeguridadDeTexto(_detailNameText);

            // Categoría
            GameObject catGo = new GameObject("Detail_Category");
            catGo.transform.SetParent(detailPanelGo.transform, false);
            var catRt = catGo.AddComponent<RectTransform>();
            catRt.anchorMin = new Vector2(0f, 1f);
            catRt.anchorMax = new Vector2(1f, 1f);
            catRt.pivot = new Vector2(0.5f, 1f);
            catRt.anchoredPosition = new Vector2(0f, -54f);
            catRt.sizeDelta = new Vector2(-30f, 24f);
            _detailCategoryText = catGo.AddComponent<TextMeshProUGUI>();
            _detailCategoryText.fontSize = 12;
            _detailCategoryText.color = new Color(0.35f, 0.85f, 0.95f);
            InventorySlotUI.AsegurarFuenteTMP(_detailCategoryText);

            // Previa de Icono en Detalle
            GameObject iconPreviewGo = new GameObject("Detail_IconPreview");
            iconPreviewGo.transform.SetParent(detailPanelGo.transform, false);
            var iconRt = iconPreviewGo.AddComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -85f);
            iconRt.sizeDelta = new Vector2(90f, 90f);
            _detailIconImage = iconPreviewGo.AddComponent<Image>();
            _detailIconImage.color = Color.white;
            _detailIconImage.preserveAspect = true;

            // Descripción Detallada
            GameObject descGo = new GameObject("Detail_Description");
            descGo.transform.SetParent(detailPanelGo.transform, false);
            var descRt = descGo.AddComponent<RectTransform>();
            descRt.anchorMin = new Vector2(0f, 0f);
            descRt.anchorMax = new Vector2(1f, 1f);
            descRt.offsetMin = new Vector2(20f, 160f);
            descRt.offsetMax = new Vector2(-20f, -185f);
            _detailDescriptionText = descGo.AddComponent<TextMeshProUGUI>();
            _detailDescriptionText.fontSize = 14;
            _detailDescriptionText.color = new Color(0.85f, 0.88f, 0.92f);
            ConfigurarTMPParaSeguridadDeTexto(_detailDescriptionText);

            // Botones de Acción
            _equipActionButton = CrearBotonProcedural(detailPanelGo.transform, "Btn_Equipar", "EQUIPAR", new Vector2(0f, -280f), new Color(0.18f, 0.45f, 0.25f), out _equipButtonText);
            _useActionButton = CrearBotonProcedural(detailPanelGo.transform, "Btn_Usar", "USAR", new Vector2(0f, -328f), new Color(0.20f, 0.35f, 0.55f), out _useButtonText);
            _dropActionButton = CrearBotonProcedural(detailPanelGo.transform, "Btn_Tirar", "TIRAR AL SUELO", new Vector2(0f, -376f), new Color(0.55f, 0.22f, 0.20f), out _);
            _closeButton = CrearBotonProcedural(detailPanelGo.transform, "Btn_Cerrar", "✖ CERRAR (Esc / I)", new Vector2(0f, -430f), new Color(0.18f, 0.18f, 0.22f), out _);

            VincularBotonesAccion();
        }

        private Button CrearBotonProcedural(Transform parent, string name, string label, Vector2 anchoredPos, Color bgColor, out TMP_Text labelTmp)
        {
            GameObject btnGo = new GameObject(name);
            btnGo.transform.SetParent(parent, false);
            var rt = btnGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(-30f, 40f);

            var img = btnGo.AddComponent<Image>();
            img.color = bgColor;

            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = bgColor * 1.35f;
            colors.pressedColor = bgColor * 0.75f;
            btn.colors = colors;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(btnGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;

            labelTmp = textGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = label;
            labelTmp.fontSize = 14;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color = Color.white;
            InventorySlotUI.AsegurarFuenteTMP(labelTmp);

            return btn;
        }

        private GameObject CrearRanuraProceduralPS1(Transform parent, int slotIndex)
        {
            GameObject slotGo = new GameObject($"Slot_{slotIndex:D2}");
            slotGo.transform.SetParent(parent, false);
            var rt = slotGo.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(116f, 116f);

            var bgImg = slotGo.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.10f, 0.12f, 0.90f);

            // Borde de selección
            GameObject borderGo = new GameObject("SelectionBorder");
            borderGo.transform.SetParent(slotGo.transform, false);
            var borderRt = borderGo.AddComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = new Vector2(-3f, -3f);
            borderRt.offsetMax = new Vector2(3f, 3f);
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.color = new Color(0.85f, 0.75f, 0.25f, 1.0f);
            borderGo.SetActive(false);

            // Icono central
            GameObject iconGo = new GameObject("ItemIcon");
            iconGo.transform.SetParent(slotGo.transform, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(80f, 80f);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.color = Color.white;
            iconImg.preserveAspect = true;
            iconGo.SetActive(false);

            // Stack text inferior
            GameObject stackGo = new GameObject("StackText");
            stackGo.transform.SetParent(slotGo.transform, false);
            var stackRt = stackGo.AddComponent<RectTransform>();
            stackRt.anchorMin = new Vector2(0f, 0f);
            stackRt.anchorMax = new Vector2(1f, 0f);
            stackRt.pivot = new Vector2(1f, 0f);
            stackRt.anchoredPosition = new Vector2(-6f, 4f);
            stackRt.sizeDelta = new Vector2(-12f, 22f);
            var stackTmp = stackGo.AddComponent<TextMeshProUGUI>();
            stackTmp.alignment = TextAlignmentOptions.BottomRight;
            stackTmp.fontSize = 13;
            stackTmp.fontStyle = FontStyles.Bold;
            stackTmp.color = new Color(1.0f, 1.0f, 1.0f, 0.95f);
            stackGo.SetActive(false);

            // Badge equipado
            GameObject badgeGo = new GameObject("EquippedBadge");
            badgeGo.transform.SetParent(slotGo.transform, false);
            var badgeRt = badgeGo.AddComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 1f);
            badgeRt.anchorMax = new Vector2(0f, 1f);
            badgeRt.pivot = new Vector2(0f, 1f);
            badgeRt.anchoredPosition = new Vector2(4f, -4f);
            badgeRt.sizeDelta = new Vector2(24f, 24f);
            var badgeTmp = badgeGo.AddComponent<TextMeshProUGUI>();
            badgeTmp.text = "★";
            badgeTmp.fontSize = 16;
            badgeTmp.fontStyle = FontStyles.Bold;
            badgeTmp.color = new Color(0.2f, 0.95f, 0.45f, 1f); // Verde fósforo
            badgeGo.SetActive(false);

            // Texto para nombre de arma si no hay ícono
            GameObject nameGo = new GameObject("ItemNameText");
            nameGo.transform.SetParent(slotGo.transform, false);
            var nameRt = nameGo.AddComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0.05f, 0.12f);
            nameRt.anchorMax = new Vector2(0.95f, 0.88f);
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = Vector2.zero;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.textWrappingMode = TextWrappingModes.Normal;
            nameTmp.enableAutoSizing = true;
            nameTmp.fontSizeMin = 8f;
            nameTmp.fontSizeMax = 12f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.color = Color.white;
            nameGo.SetActive(false);

            var slotUI = slotGo.AddComponent<InventorySlotUI>();
            slotUI.AutoConfigurarReferencias(bgImg, borderImg, iconImg, stackTmp, badgeGo, nameTmp);

            return slotGo;
        }
    }
}
