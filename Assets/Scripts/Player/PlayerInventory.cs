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
        public static PlayerInventory Instance { get; private set; }

        [Header("Weapon Slots")]
        [SerializeField] private WeaponData[] _startingWeapons;
        [SerializeField] private int _maxWeaponSlots = 3;

        private readonly List<WeaponSlotInstance> _slots = new List<WeaponSlotInstance>();
        private int _currentSlotIndex = 0;

        // Reserve ammo pools per weapon type
        private readonly Dictionary<WeaponType, int> _ammoReserves = new Dictionary<WeaponType, int>();

        public event Action<WeaponSlotInstance> OnActiveWeaponChanged;
        public event Action<int, int> OnAmmoUpdated; // currentMag, reserveAmmo
        public event Action OnInventoryChanged;

        public IReadOnlyList<WeaponSlotInstance> Slots => _slots;
        public int CurrentSlotIndex => _currentSlotIndex;
        public int MaxWeaponSlots => _maxWeaponSlots;

        public WeaponSlotInstance ActiveWeaponInstance => 
            (_slots.Count > 0 && _currentSlotIndex >= 0 && _currentSlotIndex < _slots.Count) 
                ? _slots[_currentSlotIndex] 
                : null;

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

            // Initialize standard ammo pools
            _ammoReserves[WeaponType.Hitscan] = 60;
            _ammoReserves[WeaponType.Projectile] = 20;
            _ammoReserves[WeaponType.Melee] = 0;

            if (_startingWeapons != null)
            {
                foreach (var w in _startingWeapons)
                {
                    if (w != null) AddWeapon(w, autoEquip: false);
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            if (_slots.Count > 0)
            {
                EquipSlot(0);
            }
        }

        public bool AddWeapon(WeaponData weaponData, bool autoEquip = true)
        {
            if (weaponData == null) return false;

            if (_slots.Count < _maxWeaponSlots)
            {
                var instance = new WeaponSlotInstance(weaponData);
                _slots.Add(instance);
                OnInventoryChanged?.Invoke();

                if (autoEquip || _slots.Count == 1)
                {
                    EquipSlot(_slots.Count - 1);
                }
                return true;
            }

            // Replace active weapon slot if full
            if (_slots.Count > 0)
            {
                _slots[_currentSlotIndex] = new WeaponSlotInstance(weaponData);
                OnInventoryChanged?.Invoke();
                EquipSlot(_currentSlotIndex);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Equipa la ranura indicada y comunica el cambio a PlayerWeaponEquipManager.
        /// </summary>
        public void EquipSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;

            _currentSlotIndex = slotIndex;
            var active = ActiveWeaponInstance;
            OnActiveWeaponChanged?.Invoke(active);
            NotifyAmmoChange();

            // Comunicación directa con PlayerWeaponEquipManager para asegurar instanciación y reemplazo del viewmodel
            var equipMgr = PlayerWeaponEquipManager.Instance ?? FindAnyObjectByType<PlayerWeaponEquipManager>();
            if (equipMgr != null)
            {
                if (active != null && active.Data != null)
                {
                    equipMgr.EquipWeaponExplicit(active.Data.WeaponID, active.Data, null);
                }
                else
                {
                    equipMgr.UnequipCurrentWeapon();
                }
            }
        }

        /// <summary>
        /// Busca y equipa el arma que coincida con el WeaponData proporcionado.
        /// </summary>
        public bool EquipWeapon(WeaponData weaponData)
        {
            if (weaponData == null) return false;

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i] != null && (_slots[i].Data == weaponData || string.Equals(_slots[i].Data?.WeaponID, weaponData.WeaponID, StringComparison.OrdinalIgnoreCase)))
                {
                    EquipSlot(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Busca y equipa el arma por su identificador de texto o nombre legible.
        /// </summary>
        public bool EquipWeapon(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return false;

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i] != null && _slots[i].Data != null)
                {
                    if (string.Equals(_slots[i].Data.WeaponID, weaponId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_slots[i].Data.DisplayName, weaponId, StringComparison.OrdinalIgnoreCase))
                    {
                        EquipSlot(i);
                        return true;
                    }
                }
            }
            return false;
        }

        public bool ContainsWeapon(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return false;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i] != null && _slots[i].Data != null &&
                    string.Equals(_slots[i].Data.WeaponID, weaponId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
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
