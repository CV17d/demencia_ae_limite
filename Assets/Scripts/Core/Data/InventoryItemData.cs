// ============================================================================
// InventoryItemData.cs — Arquitectura de Datos para Ítems de Inventario
// ============================================================================
// ScriptableObject base para representar armas, municiones, consumibles/salud
// y objetos clave (KeyItem) dentro del sistema de inventario estilo Survival Horror.
// ============================================================================

using System;
using UnityEngine;
using DemenciaNoLimite.Core.Data;

namespace DemenciaNoLimite.Core.Data
{
    /// <summary>
    /// Clasificación funcional del ítem en el inventario estilo Survival Horror.
    /// </summary>
    public enum ItemType
    {
        Weapon,             // Armas de fuego o cuerpo a cuerpo (no apilables)
        Ammo,               // Cajas o cartuchos de munición para armas
        Consumable,         // Botiquines, inyectores de salud, sueros de cordura, etc.
        KeyItem             // Llaves, tarjetas de acceso, documentos narrativos
    }

    [CreateAssetMenu(
        fileName = "New_InventoryItem",
        menuName = "Demencia/Inventory/Inventory Item Data",
        order = 10
    )]
    public class InventoryItemData : ScriptableObject
    {
        // ════════════════════════════════════════════════════════════════════
        // IDENTIDAD BÁSICA DEL ÍTEM
        // ════════════════════════════════════════════════════════════════════

        [Header("Identidad y Clasificación")]
        [Tooltip("Identificador único en minúsculas (ej. 'demencia_revolver', 'trabeca', 'bingala', 'medkit_basic').")]
        [SerializeField] private string _id = "";

        [Tooltip("Nombre legible mostrado en la interfaz (ej. 'Demencia Revólver', 'Trabeca', 'Bingala', 'Botiquín de Campaña').")]
        [SerializeField] private string _itemName = "Nuevo Ítem";

        [Tooltip("Icono 2D para la ranura del inventario.")]
        [SerializeField] private Sprite _itemIcon;

        [Tooltip("Tipo de recurso o función dentro del juego.")]
        [SerializeField] private ItemType _type = ItemType.Consumable;

        [Tooltip("Capacidad máxima de apilamiento en una sola ranura (las armas y objetos clave suelen ser 1).")]
        [Range(1, 999)]
        [SerializeField] private int _maxStack = 1;

        [Tooltip("Descripción narrativa y de uso mostrada en el panel lateral de detalles.")]
        [TextArea(3, 6)]
        [SerializeField] private string _description = "Sin descripción.";

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE COMBATE / ARMAS (Solo si Type == Weapon)
        // ════════════════════════════════════════════════════════════════════

        [Header("Configuración de Arma (ItemType = Weapon)")]
        [Tooltip("Clave de correspondencia con PlayerWeaponEquipManager / PlayerInventory (ej. 'demencia revolver', 'trabeca', 'bingala').")]
        [SerializeField] private string _weaponId = "";

        [Tooltip("ScriptableObject de balística y daño opcional.")]
        [SerializeField] private WeaponData _weaponData;

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE MUNICIÓN (Solo si Type == Ammo)
        // ════════════════════════════════════════════════════════════════════

        [Header("Configuración de Munición (ItemType = Ammo)")]
        [Tooltip("Tipo de balística a la que se suma esta munición de reserva.")]
        [SerializeField] private WeaponType _ammoWeaponType = WeaponType.Hitscan;

        [Tooltip("Cantidad de proyectiles que otorga un paquete al consumirse o añadirse.")]
        [SerializeField] private int _ammoAmount = 15;

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE CONSUMIBLES / SALUD (Solo si Type == Consumable)
        // ════════════════════════════════════════════════════════════════════

        [Header("Configuración de Consumible (ItemType = Consumable)")]
        [Tooltip("Cantidad de puntos de salud que restaura al usarse.")]
        [SerializeField] private float _healAmount = 35f;

        [Tooltip("Cantidad de culpa/locura que reduce en el SanityManager (0 si no aplica).")]
        [SerializeField] private int _sanityRestoreAmount = 15;

        // ════════════════════════════════════════════════════════════════════
        // REPRESENTACIÓN EN EL MUNDO 3D (AL TIRAR)
        // ════════════════════════════════════════════════════════════════════

        [Header("Mundo 3D y Descarte")]
        [Tooltip("Prefab del objeto físico con WorldItemPickup que se instanciará al soltar o tirar el ítem al suelo.")]
        [SerializeField] private GameObject _worldDropPrefab;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public string Id
        {
            get => string.IsNullOrEmpty(_id) ? name : _id;
            set => _id = value;
        }

        public string ItemName
        {
            get => _itemName;
            set => _itemName = value;
        }

        // Compatibilidad con minúsculas solicitadas en el prompt:
        public string itemName => _itemName;

        public Sprite ItemIcon
        {
            get => _itemIcon;
            set => _itemIcon = value;
        }

        // Compatibilidad con minúsculas solicitadas en el prompt:
        public Sprite itemIcon => _itemIcon;

        public ItemType Type
        {
            get => _type;
            set => _type = value;
        }

        // Compatibilidad con minúsculas solicitadas en el prompt:
        public ItemType type => _type;

        public int MaxStack
        {
            get => _maxStack;
            set => _maxStack = Mathf.Max(1, value);
        }

        // Compatibilidad con minúsculas solicitadas en el prompt:
        public int maxStack => _maxStack;

        public string Description
        {
            get => _description;
            set => _description = value;
        }

        // Compatibilidad con minúsculas solicitadas en el prompt:
        public string description => _description;

        public string WeaponId
        {
            get => string.IsNullOrEmpty(_weaponId) ? _itemName : _weaponId;
            set => _weaponId = value;
        }

        public WeaponData WeaponData
        {
            get => _weaponData;
            set => _weaponData = value;
        }

        public WeaponType AmmoWeaponType
        {
            get => _ammoWeaponType;
            set => _ammoWeaponType = value;
        }

        public int AmmoAmount
        {
            get => _ammoAmount;
            set => _ammoAmount = value;
        }

        public float HealAmount
        {
            get => _healAmount;
            set => _healAmount = value;
        }

        public int SanityRestoreAmount
        {
            get => _sanityRestoreAmount;
            set => _sanityRestoreAmount = value;
        }

        public GameObject WorldDropPrefab
        {
            get => _worldDropPrefab;
            set => _worldDropPrefab = value;
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS FACTORY PARA INSTANCIACIÓN EN TIEMPO DE EJECUCIÓN
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Crea una instancia en memoria de InventoryItemData configurada para un arma.
        /// </summary>
        public static InventoryItemData CreateWeaponItem(string id, string displayName, string desc, WeaponData wData = null)
        {
            var item = ScriptableObject.CreateInstance<InventoryItemData>();
            item.name = $"Item_Weapon_{id}";
            item._id = id;
            item._itemName = displayName;
            item._type = ItemType.Weapon;
            item._maxStack = 1;
            item._description = desc;
            item._weaponId = id;
            item._weaponData = wData;
            return item;
        }

        /// <summary>
        /// Crea una instancia en memoria de InventoryItemData para consumibles de salud o cordura.
        /// </summary>
        public static InventoryItemData CreateConsumableItem(string id, string displayName, string desc, float heal, int sanity, int maxStack = 10)
        {
            var item = ScriptableObject.CreateInstance<InventoryItemData>();
            item.name = $"Item_Consumable_{id}";
            item._id = id;
            item._itemName = displayName;
            item._type = ItemType.Consumable;
            item._maxStack = maxStack;
            item._description = desc;
            item._healAmount = heal;
            item._sanityRestoreAmount = sanity;
            return item;
        }

        /// <summary>
        /// Crea una instancia en memoria de InventoryItemData para cajas de munición.
        /// </summary>
        public static InventoryItemData CreateAmmoItem(string id, string displayName, string desc, WeaponType ammoType, int amount, int maxStack = 50)
        {
            var item = ScriptableObject.CreateInstance<InventoryItemData>();
            item.name = $"Item_Ammo_{id}";
            item._id = id;
            item._itemName = displayName;
            item._type = ItemType.Ammo;
            item._maxStack = maxStack;
            item._description = desc;
            item._ammoWeaponType = ammoType;
            item._ammoAmount = amount;
            return item;
        }

        /// <summary>
        /// Crea una instancia en memoria de InventoryItemData para llaves y objetos de misión.
        /// </summary>
        public static InventoryItemData CreateKeyItem(string id, string displayName, string desc)
        {
            var item = ScriptableObject.CreateInstance<InventoryItemData>();
            item.name = $"Item_Key_{id}";
            item._id = id;
            item._itemName = displayName;
            item._type = ItemType.KeyItem;
            item._maxStack = 1;
            item._description = desc;
            return item;
        }
    }
}
