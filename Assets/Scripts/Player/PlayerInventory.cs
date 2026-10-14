// ============================================================================
// PlayerInventory.cs — Weapon slots, ammo pools & loadout management
// ============================================================================
// Tracks equipped weapons, reserve ammunition, and triggers swap events.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core.Data;

namespace DemenciaNoLimite.Player
{
    public class PlayerInventory : MonoBehaviour
    {
        [Header("Weapon Slots")]
        [SerializeField] private WeaponData[] _startingWeapons;
        [SerializeField] private int _maxWeaponSlots = 3;

        private readonly List<WeaponSlotInstance> _slots = new List<WeaponSlotInstance>();
        private int _currentSlotIndex = 0;

        // Reserve ammo pools per weapon type
        private readonly Dictionary<WeaponType, int> _ammoReserves = new Dictionary<WeaponType, int>();

        public event Action<WeaponSlotInstance> OnActiveWeaponChanged;
        public event Action<int, int> OnAmmoUpdated; // currentMag, reserveAmmo

        public WeaponSlotInstance ActiveWeaponInstance => 
            (_slots.Count > 0 && _currentSlotIndex >= 0 && _currentSlotIndex < _slots.Count) 
                ? _slots[_currentSlotIndex] 
                : null;

        private void Awake()
        {
            // Initialize standard ammo pools
            _ammoReserves[WeaponType.Hitscan] = 60;
            _ammoReserves[WeaponType.Projectile] = 20;
            _ammoReserves[WeaponType.Melee] = 0;

            if (_startingWeapons != null)
            {
                foreach (var w in _startingWeapons)
                {
                    if (w != null) AddWeapon(w);
                }
            }
        }

        private void Start()
        {
            if (_slots.Count > 0)
            {
                EquipSlot(0);
            }
        }

        public bool AddWeapon(WeaponData weaponData)
        {
            if (weaponData == null) return false;

            if (_slots.Count < _maxWeaponSlots)
            {
                var instance = new WeaponSlotInstance(weaponData);
                _slots.Add(instance);
                return true;
            }

            // Replace active weapon slot if full
            if (_slots.Count > 0)
            {
                _slots[_currentSlotIndex] = new WeaponSlotInstance(weaponData);
                EquipSlot(_currentSlotIndex);
                return true;
            }

            return false;
        }

        public void EquipSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;

            _currentSlotIndex = slotIndex;
            var active = ActiveWeaponInstance;
            OnActiveWeaponChanged?.Invoke(active);
            NotifyAmmoChange();
        }

        public void NextWeapon()
        {
            if (_slots.Count <= 1) return;
            int next = (_currentSlotIndex + 1) % _slots.Count;
            EquipSlot(next);
        }

        public void PreviousWeapon()
        {
            if (_slots.Count <= 1) return;
            int prev = (_currentSlotIndex - 1 + _slots.Count) % _slots.Count;
            EquipSlot(prev);
        }

        public int GetReserveAmmo(WeaponType type)
        {
            return _ammoReserves.TryGetValue(type, out int val) ? val : 0;
        }

        public void AddReserveAmmo(WeaponType type, int amount)
        {
            if (!_ammoReserves.ContainsKey(type))
            {
                _ammoReserves[type] = 0;
            }
            _ammoReserves[type] += amount;
            NotifyAmmoChange();
        }

        public bool ConsumeReserveAmmo(WeaponType type, int amount, out int ammoConsumed)
        {
            ammoConsumed = 0;
            if (!_ammoReserves.TryGetValue(type, out int current) || current <= 0)
            {
                return false;
            }

            ammoConsumed = Mathf.Min(current, amount);
            _ammoReserves[type] -= ammoConsumed;
            NotifyAmmoChange();
            return true;
        }

        public void NotifyAmmoChange()
        {
            var active = ActiveWeaponInstance;
            if (active != null)
            {
                int reserve = active.Data.InfiniteAmmo ? 999 : GetReserveAmmo(active.Data.Type);
                OnAmmoUpdated?.Invoke(active.CurrentMagAmmo, reserve);
            }
            else
            {
                OnAmmoUpdated?.Invoke(0, 0);
            }
        }
    }

    /// <summary>
    /// Runtime wrapper around a WeaponData ScriptableObject tracking current chamber/magazine count.
    /// </summary>
    public class WeaponSlotInstance
    {
        public WeaponData Data { get; }
        public int CurrentMagAmmo { get; set; }

        public WeaponSlotInstance(WeaponData data)
        {
            Data = data;
            CurrentMagAmmo = data.MagazineSize;
        }
    }
}
