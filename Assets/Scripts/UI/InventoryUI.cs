// ============================================================================
// InventoryUI.cs — Interfaz Interactiva de Inventario y Cambio Rápido de Armas
// ============================================================================
// Permite al jugador abrir la interfaz del inventario mediante teclas configurables ([I], [Tab]),
// listar todas las armas cargadas en las ranuras con botones interactivos que muestran
// su nombre ("Bingala", "Revólver", "Muak", etc.), munición y estado actual.
//
// Al hacer clic en un arma:
// 1. Invoca la selección activa en PlayerInventory.
// 2. Se comunica directamente con PlayerWeaponEquipManager.Instance para destruir/guardar
//    el arma actual e instanciar/equipar inmediatamente el prefab correspondiente bajo WeaponHolder.
// 3. Cierra opcionalmente el inventario de forma automática y restaura el cursor del ratón
//    (Cursor.lockState = Locked, Cursor.visible = false), devolviendo el control al jugador al instante.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/UI/Inventory UI")]
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI Instance { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE ENTRADA Y TECLAS
        // ════════════════════════════════════════════════════════════════════

        [Header("Control de Entrada y Teclas")]
        [Tooltip("Tecla principal para abrir/cerrar el inventario (por defecto [I]).")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.I;

        [Tooltip("Tecla secundaria para abrir/cerrar el inventario (por defecto [Tab]).")]
        [SerializeField] private KeyCode _altToggleKey = KeyCode.Tab;

        [Tooltip("¿Permitir alternar el inventario mediante las teclas configuradas?")]
        [SerializeField] private bool _enableKeyToggle = true;

        // ════════════════════════════════════════════════════════════════════
        // COMPORTAMIENTO Y FLUJO DE JUEGO
        // ════════════════════════════════════════════════════════════════════

        [Header("Comportamiento y Retorno al Juego")]
        [Tooltip("¿Cerrar automáticamente el panel al hacer clic en un arma y equiparla?")]
        [SerializeField] private bool _autoCloseOnEquip = true;

        [Tooltip("¿Restaurar y bloquear automáticamente el cursor del mouse al cerrar el inventario?")]
        [SerializeField] private bool _lockCursorOnClose = true;

        [Tooltip("¿Pausar la escala de tiempo del juego (Time.timeScale = 0) mientras el inventario esté abierto?")]
        [SerializeField] private bool _pauseTimeWhenOpen = false;

        // ════════════════════════════════════════════════════════════════════
        // COMPONENTES UGUI (OPCIONALES PARA CANVAS NATIVO)
        // ════════════════════════════════════════════════════════════════════

        [Header("Componentes uGUI (Opcional si existe Canvas)")]
        [Tooltip("Panel raíz del inventario en el Canvas. Si es nulo, el script utiliza renderizado procedural OnGUI.")]
        [SerializeField] private GameObject _inventoryPanel;

        [Tooltip("Contenedor donde se generan los botones de las ranuras.")]
        [SerializeField] private Transform _slotButtonsContainer;

        [Tooltip("Prefab del botón para cada ranura de arma (debe contener Button y Text / TextMeshPro).")]
        [SerializeField] private GameObject _slotButtonPrefab;

        [Tooltip("Botón opcional para cerrar el panel manualmente.")]
        [SerializeField] private Button _closeButton;

        // ════════════════════════════════════════════════════════════════════
        // RENDERIZADO VISUAL ONGUI (FALLBACK AUTOMÁTICO SIN SETUP)
        // ════════════════════════════════════════════════════════════════════

        [Header("Renderizado Visual OnGUI")]
        [Tooltip("¿Habilitar renderizado OnGUI interactivo si no existe panel de Canvas asignado?")]
        [SerializeField] private bool _enableOnGUIFallback = true;

        [Tooltip("Color de fondo de la ventana flotante de inventario.")]
        [SerializeField] private Color _windowBackgroundColor = new Color(0.07f, 0.08f, 0.12f, 0.95f);

        [Tooltip("Color de texto/borde para el arma actualmente equipada.")]
        [SerializeField] private Color _equippedColor = new Color(0.2f, 0.95f, 0.45f, 1.0f);

        [Tooltip("Clip de sonido al hacer clic en un botón de arma.")]
        [SerializeField] private AudioClip _clickSound;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y REFERENCIAS
        // ════════════════════════════════════════════════════════════════════

        private bool _isOpen = false;
        private PlayerInventory _inventory;
        private PlayerWeaponEquipManager _equipManager;
        private AudioSource _audioSource;

        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _subStyle;
        private GUIStyle _slotButtonStyle;
        private GUIStyle _slotButtonEquippedStyle;
        private GUIStyle _closeButtonStyle;
        private Texture2D _panelTex;
        private Texture2D _activeSlotTex;
        private Texture2D _inactiveSlotTex;

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        public event Action<bool> OnInventoryToggled;
        public event Action<int, WeaponData> OnWeaponEquippedFromUI;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public bool IsOpen => _isOpen;
        public PlayerInventory Inventory => _inventory;
        public PlayerWeaponEquipManager EquipManager => _equipManager;

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
                Instance = this;
            }

            ResolverReferencias();
            ConfigurarAudio();

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(CloseInventory);
            }

            if (_inventoryPanel != null)
            {
                _inventoryPanel.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(CloseInventory);
            }

            if (_panelTex != null) Destroy(_panelTex);
            if (_activeSlotTex != null) Destroy(_activeSlotTex);
            if (_inactiveSlotTex != null) Destroy(_inactiveSlotTex);
        }

        private void Start()
        {
            ResolverReferencias();
        }

        private void Update()
        {
            if (!_enableKeyToggle) return;

            // Alternar con teclas configuradas ([I] o [Tab])
            if (Input.GetKeyDown(_toggleKey) || (_altToggleKey != KeyCode.None && Input.GetKeyDown(_altToggleKey)))
            {
                ToggleInventory();
            }

            // Tecla Escape para salir del inventario si está abierto
            if (_isOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseInventory();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CONTROL DE VISIBILIDAD (ABRIR / CERRAR)
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
        /// Abre el inventario, desbloquea el cursor del ratón y muestra las armas disponibles.
        /// </summary>
        public void OpenInventory()
        {
            ResolverReferencias();
            _isOpen = true;

            // Desbloquear cursor para permitir clic e interacción con los botones
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_pauseTimeWhenOpen)
            {
                Time.timeScale = 0f;
            }

            // Sincronizar Canvas nativo si está configurado
            if (_inventoryPanel != null)
            {
                _inventoryPanel.SetActive(true);
                ReconstruirBotonesUGUI();
            }

            OnInventoryToggled?.Invoke(true);
            Debug.Log("[InventoryUI] 🎒 Inventario abierto. Cursor interactivo activado.");
        }

        /// <summary>
        /// Cierra el inventario y restaura automáticamente el estado del cursor para volver a la acción.
        /// </summary>
        public void CloseInventory()
        {
            _isOpen = false;

            // Restaurar estado del cursor del mouse
            if (_lockCursorOnClose)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (_pauseTimeWhenOpen)
            {
                Time.timeScale = 1f;
            }

            if (_inventoryPanel != null)
            {
                _inventoryPanel.SetActive(false);
            }

            OnInventoryToggled?.Invoke(false);
            Debug.Log("[InventoryUI] 🔒 Inventario cerrado. Control del jugador restaurado.");
        }

        // ════════════════════════════════════════════════════════════════════
        // SELECCIÓN Y EQUIPAMIENTO DE ARMAS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Método invocado al hacer clic en el botón de una ranura dentro del inventario.
        /// </summary>
        public void EquipWeaponSlot(int slotIndex)
        {
            ResolverReferencias();

            if (_inventory == null)
            {
                Debug.LogWarning("[InventoryUI] No se encontró componente PlayerInventory.");
                return;
            }

            var slots = _inventory.Slots;
            if (slotIndex < 0 || slotIndex >= slots.Count)
            {
                Debug.LogWarning($"[InventoryUI] Índice de ranura fuera de rango: {slotIndex}");
                return;
            }

            var selectedInstance = slots[slotIndex];
            if (selectedInstance == null || selectedInstance.Data == null)
            {
                Debug.LogWarning($"[InventoryUI] La ranura {slotIndex} no contiene un arma válida.");
                return;
            }

            WeaponData weaponData = selectedInstance.Data;
            string weaponId = !string.IsNullOrEmpty(weaponData.WeaponID) ? weaponData.WeaponID : weaponData.DisplayName;
            string weaponName = !string.IsNullOrEmpty(weaponData.DisplayName) ? weaponData.DisplayName : weaponId;

            Debug.Log($"[InventoryUI] 🖱️ Clic en ranura [{slotIndex + 1}]: Seleccionando '{weaponName}' ({weaponId})...");

            // 1. Notificar y actualizar la ranura activa en PlayerInventory
            _inventory.EquipSlot(slotIndex);

            // 2. Comunicación directa con PlayerWeaponEquipManager para asegurar destrucción del arma anterior e instanciación del nuevo prefab
            if (_equipManager == null)
            {
                _equipManager = PlayerWeaponEquipManager.Instance ?? FindAnyObjectByType<PlayerWeaponEquipManager>();
            }

            if (_equipManager != null)
            {
                _equipManager.EquipWeaponExplicit(weaponData.WeaponID, weaponData, null);
                Debug.Log($"[InventoryUI] ✅ PlayerWeaponEquipManager sincronizado exitosamente con '{weaponId}'.");
            }

            // Reproducir sonido de clic si está configurado
            PlayClickSound();

            // 3. Disparar evento para listeners externos
            OnWeaponEquippedFromUI?.Invoke(slotIndex, weaponData);

            // 4. Cierre automático y retorno al juego
            if (_autoCloseOnEquip)
            {
                CloseInventory();
            }
            else
            {
                // Si permanece abierto, refrescar Canvas uGUI
                if (_inventoryPanel != null && _inventoryPanel.activeSelf)
                {
                    ReconstruirBotonesUGUI();
                }
            }
        }

        /// <summary>
        /// Equipa un arma por su referencia de WeaponData.
        /// </summary>
        public bool EquipWeaponByData(WeaponData weaponData)
        {
            if (weaponData == null || _inventory == null) return false;

            for (int i = 0; i < _inventory.Slots.Count; i++)
            {
                if (_inventory.Slots[i] != null && _inventory.Slots[i].Data == weaponData)
                {
                    EquipWeaponSlot(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Equipa un arma buscando por su ID de texto o nombre legible.
        /// </summary>
        public bool EquipWeaponById(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId) || _inventory == null) return false;

            for (int i = 0; i < _inventory.Slots.Count; i++)
            {
                var s = _inventory.Slots[i];
                if (s != null && s.Data != null)
                {
                    if (string.Equals(s.Data.WeaponID, weaponId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.Data.DisplayName, weaponId, StringComparison.OrdinalIgnoreCase))
                    {
                        EquipWeaponSlot(i);
                        return true;
                    }
                }
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // RESOLUCIÓN DE REFERENCIAS Y AUDIO
        // ════════════════════════════════════════════════════════════════════

        private void ResolverReferencias()
        {
            if (_inventory == null)
            {
                _inventory = PlayerInventory.Instance ?? FindAnyObjectByType<PlayerInventory>();
            }

            if (_equipManager == null)
            {
                _equipManager = PlayerWeaponEquipManager.Instance ?? FindAnyObjectByType<PlayerWeaponEquipManager>();
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

        private void PlayClickSound()
        {
            if (_clickSound != null && _audioSource != null)
            {
                try
                {
                    _audioSource.PlayOneShot(_clickSound);
                }
                catch { }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GENERACIÓN Y VINCULACIÓN EN CANVAS NATIVO (UGUI)
        // ════════════════════════════════════════════════════════════════════

        private void ReconstruirBotonesUGUI()
        {
            if (_slotButtonsContainer == null || _slotButtonPrefab == null || _inventory == null) return;

            // Limpiar hijos existentes
            foreach (Transform child in _slotButtonsContainer)
            {
                Destroy(child.gameObject);
            }

            var slots = _inventory.Slots;
            int currentActive = _inventory.CurrentSlotIndex;

            for (int i = 0; i < slots.Count; i++)
            {
                int slotIndex = i;
                var slot = slots[i];
                if (slot == null || slot.Data == null) continue;

                GameObject btnObj = Instantiate(_slotButtonPrefab, _slotButtonsContainer);
                Button btn = btnObj.GetComponent<Button>() ?? btnObj.GetComponentInChildren<Button>();

                string weaponName = !string.IsNullOrEmpty(slot.Data.DisplayName) ? slot.Data.DisplayName : slot.Data.WeaponID;
                bool isEquipped = (slotIndex == currentActive);
                string labelText = isEquipped ? $"★ {weaponName} [EQUIPADA]" : $"{weaponName} ({slot.CurrentMagAmmo})";

                Text legacyText = btnObj.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = labelText;
                    if (isEquipped) legacyText.color = _equippedColor;
                }

                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => EquipWeaponSlot(slotIndex));
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RENDERIZADO VISUAL INTERACTIVO ONGUI (MODAL STANDALONE)
        // ════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (!_isOpen) return;

            // Si hay un panel de Canvas uGUI activo, no duplicar dibujo OnGUI
            if (_inventoryPanel != null && _inventoryPanel.activeInHierarchy) return;

            if (!_enableOnGUIFallback) return;

            InitGUIStyles();

            // Oscurecer ligeramente el fondo del juego
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Dimensiones de la ventana central de inventario
            float winWidth = Mathf.Min(560f, Screen.width - 40f);
            float winHeight = Mathf.Min(480f, Screen.height - 40f);
            float winX = (Screen.width - winWidth) * 0.5f;
            float winY = (Screen.height - winHeight) * 0.5f;

            Rect windowRect = new Rect(winX, winY, winWidth, winHeight);

            // Contenedor principal
            GUI.Box(windowRect, GUIContent.none, _windowStyle);

            GUILayout.BeginArea(new Rect(winX + 24f, winY + 20f, winWidth - 48f, winHeight - 40f));
            {
                // Encabezado
                GUILayout.Label("🎒 INVENTARIO DE ARMAS", _headerStyle);
                GUILayout.Label("Haz clic en cualquier ranura para equipar el arma al instante.", _subStyle);
                GUILayout.Space(16f);

                // Obtener ranuras activas
                ResolverReferencias();
                var slots = (_inventory != null) ? _inventory.Slots : null;
                int currentActive = (_inventory != null) ? _inventory.CurrentSlotIndex : -1;

                if (slots != null && slots.Count > 0)
                {
                    for (int i = 0; i < slots.Count; i++)
                    {
                        int slotIndex = i;
                        var slot = slots[i];
                        if (slot == null || slot.Data == null) continue;

                        bool isEquipped = (slotIndex == currentActive);
                        string weaponName = !string.IsNullOrEmpty(slot.Data.DisplayName) ? slot.Data.DisplayName : slot.Data.WeaponID;
                        string ammoInfo = slot.Data.InfiniteAmmo ? "∞" : $"{slot.CurrentMagAmmo} / {_inventory.GetReserveAmmo(slot.Data.Type)}";

                        string buttonText = isEquipped
                            ? $"★  [RANURA {i + 1}]  {weaponName.ToUpper()}  |  Munición: {ammoInfo}   —   [EQUIPADA]"
                            : $"➔  [RANURA {i + 1}]  {weaponName.ToUpper()}  |  Munición: {ammoInfo}   —   [CLIC PARA EQUIPAR]";

                        GUIStyle activeStyle = isEquipped ? _slotButtonEquippedStyle : _slotButtonStyle;

                        if (GUILayout.Button(buttonText, activeStyle, GUILayout.Height(52f)))
                        {
                            EquipWeaponSlot(slotIndex);
                        }

                        GUILayout.Space(8f);
                    }
                }
                else
                {
                    GUILayout.Label("No hay armas en el inventario actualmente.", _subStyle);
                    GUILayout.Space(12f);
                }

                GUILayout.FlexibleSpace();

                // Botón inferior para cerrar inventario y volver a la acción
                if (GUILayout.Button("✖  CERRAR INVENTARIO Y VOLVER AL JUEGO  (Esc / Tab)", _closeButtonStyle, GUILayout.Height(42f)))
                {
                    CloseInventory();
                }
            }
            GUILayout.EndArea();
        }

        private void InitGUIStyles()
        {
            if (_windowStyle != null) return;

            _panelTex = CreateColorTexture(_windowBackgroundColor);
            _activeSlotTex = CreateColorTexture(new Color(0.12f, 0.32f, 0.18f, 0.92f));
            _inactiveSlotTex = CreateColorTexture(new Color(0.14f, 0.16f, 0.22f, 0.90f));

            _windowStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _panelTex },
                padding = new RectOffset(16, 16, 16, 16)
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1.0f, 0.88f, 0.35f) }
            };

            _subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.85f, 0.88f, 0.92f) }
            };

            _slotButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white, background = _inactiveSlotTex },
                hover = { textColor = Color.yellow },
                padding = new RectOffset(18, 18, 10, 10)
            };

            _slotButtonEquippedStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = _equippedColor, background = _activeSlotTex },
                hover = { textColor = Color.white },
                padding = new RectOffset(18, 18, 10, 10)
            };

            _closeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.95f, 0.4f, 0.35f) }
            };
        }

        private Texture2D CreateColorTexture(Color col)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }
    }
}
