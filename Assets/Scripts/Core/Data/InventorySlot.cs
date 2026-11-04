// ============================================================================
// InventorySlot.cs — Instancia de Ranura de Inventario en Tiempo de Ejecución
// ============================================================================
// Modela el estado dinámico de cada espacio dentro de la cuadrícula:
// ítem contenido, cantidad apilada actual y si está equipado activamente.
// ============================================================================

using System;
using UnityEngine;

namespace DemenciaNoLimite.Core.Data
{
    [Serializable]
    public class InventorySlot
    {
        [SerializeField] private InventoryItemData _itemData;
        [SerializeField] private int _currentStack = 0;
        [SerializeField] private bool _isEquipped = false;

        public InventoryItemData ItemData
        {
            get => _itemData;
            set => _itemData = value;
        }

        // Compatibilidad con minúsculas solicitadas:
        public InventoryItemData itemData => _itemData;

        public int CurrentStack
        {
            get => _currentStack;
            set => _currentStack = Mathf.Clamp(value, 0, _itemData != null ? _itemData.MaxStack : 999);
        }

        // Compatibilidad con minúsculas solicitadas:
        public int currentStack
        {
            get => _currentStack;
            set => CurrentStack = value;
        }

        public int MaxStack => _itemData != null ? _itemData.MaxStack : 1;
        public int maxStack => MaxStack;

        public bool IsEquipped
        {
            get => _isEquipped;
            set => _isEquipped = value;
        }

        public bool isEquipped
        {
            get => _isEquipped;
            set => _isEquipped = value;
        }

        public bool IsEmpty => _itemData == null || _currentStack <= 0;

        public InventorySlot()
        {
            _itemData = null;
            _currentStack = 0;
            _isEquipped = false;
        }

        public InventorySlot(InventoryItemData data, int stack = 1, bool equipped = false)
        {
            _itemData = data;
            _currentStack = Mathf.Max(0, stack);
            _isEquipped = equipped;
        }

        /// <summary>
        /// Comprueba si el slot puede aceptar más unidades de un determinado ítem.
        /// </summary>
        public bool CanAcceptStack(InventoryItemData data, int amount = 1)
        {
            if (IsEmpty) return true;
            if (_itemData == null || data == null) return false;

            // Coincidencia por ID o referencia
            bool sameItem = _itemData == data || string.Equals(_itemData.Id, data.Id, StringComparison.OrdinalIgnoreCase);
            if (!sameItem) return false;

            return (_currentStack + amount) <= _itemData.MaxStack;
        }

        /// <summary>
        /// Añade cantidad al stack actual respetando el máximo. Retorna la cantidad sobrante que no cupo.
        /// </summary>
        public int AddStack(int amount)
        {
            if (_itemData == null) return amount;

            int spaceLeft = _itemData.MaxStack - _currentStack;
            int toAdd = Mathf.Min(spaceLeft, amount);
            _currentStack += toAdd;

            return amount - toAdd;
        }

        /// <summary>
        /// Consume una o varias unidades del stack. Retorna true si aún queda cantidad, false si quedó vacío.
        /// </summary>
        public bool Consume(int amount = 1)
        {
            _currentStack -= amount;
            if (_currentStack <= 0)
            {
                Clear();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Vacía la ranura por completo.
        /// </summary>
        public void Clear()
        {
            _itemData = null;
            _currentStack = 0;
            _isEquipped = false;
        }
    }
}
