// ============================================================================
// InventorySlotUI.cs — Vista y Comportamiento de Ranura Individual (Retro PS1)
// ============================================================================
// Representa visualmente un casillero cuadrado de la cuadrícula de inventario:
// - Marco y paleta estética Survival Horror PS1 (verde fósforo, ámbar, grafito).
// - Imagen central del ícono con preserveAspect.
// - Texto inferior de cantidad (visible exclusivamente si currentStack > 1).
// - Feedback visual de selección activa y de arma actualmente equipada ("★").
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DemenciaNoLimite.Core.Data;

namespace DemenciaNoLimite.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/UI/Inventory Slot UI")]
    public class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        // ════════════════════════════════════════════════════════════════════
        // REFERENCIAS VISUALES UGUI & TEXTMESHPRO
        // ════════════════════════════════════════════════════════════════════

        [Header("Contenedores y Fondos")]
        [Tooltip("Imagen de fondo de la ranura (marco retro).")]
        [SerializeField] private Image _backgroundImage;

        [Tooltip("Borde o marco de realce cuando el slot está seleccionado.")]
        [SerializeField] private Image _selectionBorder;

        [Tooltip("Borde o indicador cuando el ítem es un arma y está EQUIPADA.")]
        [SerializeField] private Image _equippedBorder;

        [Header("Contenido del Ítem")]
        [Tooltip("Imagen que muestra el sprite/icono 2D del ítem.")]
        [SerializeField] private Image _iconImage;

        [Tooltip("Texto en la esquina inferior con el número de apilamiento (solo si > 1).")]
        [SerializeField] private TMP_Text _stackText;

        [Tooltip("Badge o indicador visual 'EQ' o '★' para armas equipadas.")]
        [SerializeField] private GameObject _equippedBadge;

        [Tooltip("Botón uGUI opcional para interacción por eventos estándar.")]
        [SerializeField] private Button _slotButton;

        // ════════════════════════════════════════════════════════════════════
        // PALETA RETRO PS1 / SURVIVAL HORROR
        // ════════════════════════════════════════════════════════════════════

        [Header("Paleta Retro PS1")]
        [SerializeField] private Color _colorSlotNormal = new Color(0.08f, 0.10f, 0.12f, 0.90f);
        [SerializeField] private Color _colorSlotHover = new Color(0.16f, 0.22f, 0.26f, 0.95f);
        [SerializeField] private Color _colorSlotSelected = new Color(0.25f, 0.40f, 0.35f, 1.0f);
        [SerializeField] private Color _colorBorderNormal = new Color(0.25f, 0.28f, 0.32f, 0.8f);
        [SerializeField] private Color _colorBorderSelected = new Color(0.85f, 0.75f, 0.25f, 1.0f); // Ámbar retro
        [SerializeField] private Color _colorBorderEquipped = new Color(0.20f, 0.95f, 0.45f, 1.0f); // Verde fósforo

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private int _slotIndex = -1;
        private InventorySlot _currentSlotData;
        private bool _isSelected = false;
        private bool _isHovered = false;
        private Action<int> _onSlotClicked;

        public int SlotIndex => _slotIndex;
        public InventorySlot CurrentSlotData => _currentSlotData;
        public bool IsSelected => _isSelected;

        private void Awake()
        {
            if (_slotButton != null)
            {
                _slotButton.onClick.AddListener(TriggerClick);
            }
        }

        private void OnDestroy()
        {
            if (_slotButton != null)
            {
                _slotButton.onClick.RemoveListener(TriggerClick);
            }
        }

        /// <summary>
        /// Vincula los datos del slot y configura los elementos visuales.
        /// </summary>
        public void SetupSlot(int index, InventorySlot slotData, bool isSelected, Action<int> onClickCallback)
        {
            _slotIndex = index;
            _currentSlotData = slotData;
            _isSelected = isSelected;
            _onSlotClicked = onClickCallback;

            ActualizarVisuales();
        }

        /// <summary>
        /// Refresca los estados de selección, ícono, textos y badges.
        /// </summary>
        public void ActualizarVisuales()
        {
            bool hasItem = _currentSlotData != null && !_currentSlotData.IsEmpty && _currentSlotData.ItemData != null;

            // 1. Icono del Ítem
            if (_iconImage != null)
            {
                if (hasItem && _currentSlotData.ItemData.ItemIcon != null)
                {
                    _iconImage.sprite = _currentSlotData.ItemData.ItemIcon;
                    _iconImage.color = Color.white;
                    _iconImage.preserveAspect = true;
                    _iconImage.gameObject.SetActive(true);
                }
                else if (hasItem)
                {
                    // Si el ítem no tiene Sprite asignado, mostramos un fallback de color diegético
                    _iconImage.sprite = null;
                    _iconImage.color = ObtenerColorPorTipo(_currentSlotData.ItemData.Type);
                    _iconImage.gameObject.SetActive(true);
                }
                else
                {
                    _iconImage.gameObject.SetActive(false);
                }
            }

            // 2. Texto de Apilamiento (currentStack > 1)
            if (_stackText != null)
            {
                if (hasItem && _currentSlotData.CurrentStack > 1)
                {
                    _stackText.text = $"x{_currentSlotData.CurrentStack}";
                    _stackText.enableAutoSizing = true;
                    _stackText.fontSizeMin = 8;
                    _stackText.fontSizeMax = 14;
                    _stackText.gameObject.SetActive(true);
                }
                else
                {
                    _stackText.gameObject.SetActive(false);
                }
            }

            // 3. Indicador de Arma Equipada
            bool isEquipped = hasItem && _currentSlotData.IsEquipped;
            if (_equippedBadge != null)
            {
                _equippedBadge.SetActive(isEquipped);
            }

            if (_equippedBorder != null)
            {
                _equippedBorder.color = _colorBorderEquipped;
                _equippedBorder.gameObject.SetActive(isEquipped);
            }

            // 4. Borde y Fondo de Selección
            if (_selectionBorder != null)
            {
                _selectionBorder.color = _isSelected ? _colorBorderSelected : _colorBorderNormal;
                _selectionBorder.gameObject.SetActive(_isSelected || _isHovered);
            }

            if (_backgroundImage != null)
            {
                _backgroundImage.color = _isSelected ? _colorSlotSelected : (_isHovered ? _colorSlotHover : _colorSlotNormal);
            }
        }

        private Color ObtenerColorPorTipo(ItemType type)
        {
            switch (type)
            {
                case ItemType.Weapon: return new Color(0.85f, 0.35f, 0.25f, 0.8f);
                case ItemType.Ammo: return new Color(0.90f, 0.80f, 0.20f, 0.8f);
                case ItemType.Consumable: return new Color(0.25f, 0.85f, 0.40f, 0.8f);
                case ItemType.KeyItem: return new Color(0.35f, 0.70f, 0.95f, 0.8f);
                default: return Color.gray;
            }
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            ActualizarVisuales();
        }

        private void TriggerClick()
        {
            _onSlotClicked?.Invoke(_slotIndex);
        }

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS DE PUNTERO / RATÓN
        // ════════════════════════════════════════════════════════════════════

        public void OnPointerClick(PointerEventData eventData)
        {
            TriggerClick();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            ActualizarVisuales();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            ActualizarVisuales();
        }

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR PROCEDURAL DE SLOTS (SI NO SE USA PREFAB)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Configura automáticamente los componentes de UI si el slot fue generado proceduralmente.
        /// </summary>
        public void AutoConfigurarReferencias(Image bg, Image border, Image icon, TMP_Text stack, GameObject badge)
        {
            _backgroundImage = bg;
            _selectionBorder = border;
            _iconImage = icon;
            _stackText = stack;
            _equippedBadge = badge;
        }
    }
}
