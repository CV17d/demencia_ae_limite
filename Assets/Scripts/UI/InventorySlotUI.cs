// ============================================================================
// InventorySlotUI.cs — Vista y Comportamiento de Ranura Individual (Retro PS1)
// ============================================================================
// Representa visualmente un casillero cuadrado de la cuadrícula de inventario:
// - Marco y paleta estética Survival Horror PS1 (verde fósforo, ámbar, grafito).
// - Imagen central del ícono con preserveAspect (si existe).
// - Si no hay sprite, asigna un color visible de fondo y muestra el nombre en TMP_Text.
// - Texto inferior de cantidad (visible exclusivamente si currentStack > 1).
// - Feedback visual de selección activa y de arma actualmente equipada ("★").
// - Referencias ultra-defensivas a TMP_Text para evitar cualquier NullReferenceException.
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

        [Tooltip("Texto para mostrar el nombre del arma/ítem si no hay icono asignado.")]
        [SerializeField] private TMP_Text _nameText;

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

        [Header("Colores de Respaldo sin Ícono (Fallback)")]
        [SerializeField] private Color _fallbackWeaponColor = new Color(0.38f, 0.16f, 0.12f, 0.95f);
        [SerializeField] private Color _fallbackAmmoColor = new Color(0.35f, 0.30f, 0.10f, 0.95f);
        [SerializeField] private Color _fallbackConsumableColor = new Color(0.12f, 0.32f, 0.18f, 0.95f);
        [SerializeField] private Color _fallbackKeyColor = new Color(0.14f, 0.25f, 0.38f, 0.95f);

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
            ResolverReferenciasDefensivas();

            if (_slotButton != null)
            {
                _slotButton.onClick.RemoveAllListeners();
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
        /// Garantiza que el componente TMP_Text disponga de una fuente válida para renderizar
        /// y evita advertencias o cuadros de diálogo de TMP Importer durante el juego.
        /// </summary>
        public static void AsegurarFuenteTMP(TMP_Text tmp)
        {
            if (tmp == null) return;
            if (tmp.font == null)
            {
                try
                {
                    if (TMP_Settings.defaultFontAsset != null)
                    {
                        tmp.font = TMP_Settings.defaultFontAsset;
                        return;
                    }
                }
                catch { }

                try
                {
                    var fallbackFont = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                    if (fallbackFont != null && fallbackFont.Length > 0)
                    {
                        tmp.font = fallbackFont[0];
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Garantiza que ninguna referencia de UI sea nula para prevenir NullReferenceException.
        /// </summary>
        public void ResolverReferenciasDefensivas()
        {
            if (_backgroundImage == null)
            {
                _backgroundImage = GetComponent<Image>();
            }

            if (_selectionBorder == null)
            {
                var borderT = transform.Find("SelectionBorder");
                if (borderT != null) _selectionBorder = borderT.GetComponent<Image>();
            }

            if (_equippedBorder == null)
            {
                var eqT = transform.Find("EquippedBorder");
                if (eqT != null) _equippedBorder = eqT.GetComponent<Image>();
            }

            if (_iconImage == null)
            {
                var iconT = transform.Find("ItemIcon");
                if (iconT != null) _iconImage = iconT.GetComponent<Image>();
            }

            if (_stackText == null)
            {
                var stackT = transform.Find("StackText");
                if (stackT != null) _stackText = stackT.GetComponent<TMP_Text>();
            }

            if (_nameText == null)
            {
                var nameT = transform.Find("ItemNameText");
                if (nameT != null)
                {
                    _nameText = nameT.GetComponent<TMP_Text>();
                }
                else
                {
                    GameObject nameGo = new GameObject("ItemNameText");
                    nameGo.transform.SetParent(transform, false);
                    var rt = nameGo.AddComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.06f, 0.08f);
                    rt.anchorMax = new Vector2(0.94f, 0.92f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    _nameText = nameGo.AddComponent<TextMeshProUGUI>();
                }
            }

            AsegurarFuenteTMP(_nameText);
            AsegurarFuenteTMP(_stackText);

            if (_equippedBadge == null)
            {
                var badgeT = transform.Find("EquippedBadge");
                if (badgeT != null) _equippedBadge = badgeT.gameObject;
            }

            if (_slotButton == null)
            {
                _slotButton = GetComponent<Button>();
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

            ResolverReferenciasDefensivas();
            ActualizarVisuales();
        }

        /// <summary>
        /// Refresca los estados de selección, ícono, textos y badges.
        /// </summary>
        public void ActualizarVisuales()
        {
            ResolverReferenciasDefensivas();

            bool hasItem = _currentSlotData != null && !_currentSlotData.IsEmpty && _currentSlotData.ItemData != null;

            if (!hasItem)
            {
                // Slot vacío
                if (_iconImage != null) _iconImage.gameObject.SetActive(false);
                if (_nameText != null) _nameText.gameObject.SetActive(false);
                if (_stackText != null) _stackText.gameObject.SetActive(false);
                if (_equippedBadge != null) _equippedBadge.SetActive(false);
                if (_equippedBorder != null) _equippedBorder.gameObject.SetActive(false);
                if (_selectionBorder != null) _selectionBorder.gameObject.SetActive(_isSelected);
                if (_backgroundImage != null) _backgroundImage.color = _isSelected ? _colorSlotSelected : (_isHovered ? _colorSlotHover : _colorSlotNormal);
                return;
            }

            var item = _currentSlotData.ItemData;
            bool hasIcon = item.ItemIcon != null;

            // 1. GESTIÓN VISUAL DEL ÍCONO Y NOMBRE (FALLBACK SI NO HAY SPRITE)
            if (hasIcon)
            {
                // Mostrar Sprite asignado
                if (_iconImage != null)
                {
                    _iconImage.sprite = item.ItemIcon;
                    _iconImage.color = Color.white;
                    _iconImage.preserveAspect = true;
                    _iconImage.gameObject.SetActive(true);
                }

                if (_nameText != null)
                {
                    _nameText.gameObject.SetActive(false);
                }

                if (_backgroundImage != null)
                {
                    _backgroundImage.color = _isSelected ? _colorSlotSelected : (_isHovered ? _colorSlotHover : _colorSlotNormal);
                }
            }
            else
            {
                // NO TIENE ÍCONO: No dejar el cuadro en blanco/nulo.
                // Asignar un color visible de fondo y mostrar el texto con el nombre del arma.
                if (_iconImage != null)
                {
                    _iconImage.gameObject.SetActive(false);
                }

                Color fallbackBgColor = ObtenerColorVisibleFallback(item.Type);
                if (_backgroundImage != null)
                {
                    _backgroundImage.color = _isSelected ? Color.Lerp(fallbackBgColor, _colorSlotSelected, 0.5f) : (_isHovered ? fallbackBgColor * 1.2f : fallbackBgColor);
                }

                if (_nameText != null)
                {
                    _nameText.text = !string.IsNullOrEmpty(item.ItemName) ? item.ItemName.ToUpperInvariant() : "ARMA";
                    _nameText.textWrappingMode = TextWrappingModes.Normal;
                    _nameText.enableAutoSizing = true;
                    _nameText.fontSizeMin = 8f;
                    _nameText.fontSizeMax = 13f;
                    _nameText.fontStyle = FontStyles.Bold;
                    _nameText.alignment = TextAlignmentOptions.Center;
                    _nameText.color = Color.white;
                    AsegurarFuenteTMP(_nameText);
                    _nameText.gameObject.SetActive(true);
                }
            }

            // 2. TEXTO DE APILAMIENTO (Solo si currentStack > 1)
            if (_stackText != null)
            {
                if (_currentSlotData.CurrentStack > 1)
                {
                    _stackText.text = $"x{_currentSlotData.CurrentStack}";
                    _stackText.enableAutoSizing = true;
                    _stackText.fontSizeMin = 8;
                    _stackText.fontSizeMax = 14;
                    AsegurarFuenteTMP(_stackText);
                    _stackText.gameObject.SetActive(true);
                }
                else
                {
                    _stackText.gameObject.SetActive(false);
                }
            }

            // 3. INDICADOR DE ARMA EQUIPADA
            bool isEquipped = _currentSlotData.IsEquipped;
            if (_equippedBadge != null)
            {
                _equippedBadge.SetActive(isEquipped);
            }

            if (_equippedBorder != null)
            {
                _equippedBorder.color = _colorBorderEquipped;
                _equippedBorder.gameObject.SetActive(isEquipped);
            }

            // 4. BORDE Y FONDO DE SELECCIÓN
            if (_selectionBorder != null)
            {
                _selectionBorder.color = _isSelected ? _colorBorderSelected : _colorBorderNormal;
                _selectionBorder.gameObject.SetActive(_isSelected || _isHovered);
            }
        }

        private Color ObtenerColorVisibleFallback(ItemType type)
        {
            switch (type)
            {
                case ItemType.Weapon: return _fallbackWeaponColor;
                case ItemType.Ammo: return _fallbackAmmoColor;
                case ItemType.Consumable: return _fallbackConsumableColor;
                case ItemType.KeyItem: return _fallbackKeyColor;
                default: return new Color(0.22f, 0.22f, 0.25f, 0.95f);
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
        // CONFIGURACIÓN PROCEDURAL DE SLOTS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Configura de forma integral los componentes de UI si el slot fue generado proceduralmente.
        /// </summary>
        public void AutoConfigurarReferencias(Image bg, Image border, Image icon, TMP_Text stack, GameObject badge, TMP_Text nameText = null)
        {
            _backgroundImage = bg;
            _selectionBorder = border;
            _iconImage = icon;
            _stackText = stack;
            _equippedBadge = badge;
            _nameText = nameText;

            AsegurarFuenteTMP(_nameText);
            AsegurarFuenteTMP(_stackText);
            if (_equippedBadge != null)
            {
                var badgeTmp = _equippedBadge.GetComponent<TMP_Text>();
                if (badgeTmp != null) AsegurarFuenteTMP(badgeTmp);
            }
        }
    }
}
