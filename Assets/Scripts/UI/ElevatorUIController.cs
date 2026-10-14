// ============================================================================
// ElevatorUIController.cs — UI logic for the Elevator Interlude
// ============================================================================
// Manages the risk/reward decision UI:
//   - Merchant shop display (item costs, purchases, currency validation)
//   - Cursed Pact cards (buff vs permanent enemy mutator comparisons)
// Handles cursor unlock and signals ElevatorEventSystem when ready to ascend.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.UI
{
    public class ElevatorUIController : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private bool _isElevatorUIVisible = false;

        public bool IsElevatorUIVisible => _isElevatorUIVisible;
        public ElevatorResult ActiveEventType { get; private set; }

        public event Action<ElevatorResult> OnElevatorOpened;
        public event Action OnElevatorClosed;

        private ElevatorEventSystem _elevatorSystem;

        private void Awake()
        {
            _elevatorSystem = FindAnyObjectByType<ElevatorEventSystem>();
        }

        private void OnEnable()
        {
            GameEvents.OnElevatorResolved += OpenElevatorUI;
        }

        private void OnDisable()
        {
            GameEvents.OnElevatorResolved -= OpenElevatorUI;
        }

        public void OpenElevatorUI(ElevatorResult eventType)
        {
            ActiveEventType = eventType;
            _isElevatorUIVisible = true;

            // Unlock cursor for menu interaction
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            OnElevatorOpened?.Invoke(eventType);
            Debug.Log($"[ElevatorUIController] Elevator UI opened for {eventType}.");
        }

        public void CloseElevatorUI()
        {
            _isElevatorUIVisible = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            OnElevatorClosed?.Invoke();
        }

        // ── Merchant Actions ───────────────────────────────────────────────

        public IReadOnlyList<MerchantItemData> GetMerchantItems()
        {
            return _elevatorSystem != null ? _elevatorSystem.OfferedItems : new List<MerchantItemData>();
        }

        public bool TryBuyItem(MerchantItemData item)
        {
            if (_elevatorSystem == null || GameManager.Instance == null) return false;
            return _elevatorSystem.PurchaseItem(item, GameManager.Instance.Player);
        }

        // ── Cursed Pact Actions ────────────────────────────────────────────

        public IReadOnlyList<CursedPactData> GetOfferedPacts()
        {
            return _elevatorSystem != null ? _elevatorSystem.OfferedPacts : new List<CursedPactData>();
        }

        public void AcceptPact(CursedPactData pact)
        {
            if (_elevatorSystem != null)
            {
                _elevatorSystem.AcceptPact(pact);
            }
            AscendNextFloor();
        }

        public void DeclinePacts()
        {
            if (_elevatorSystem != null)
            {
                _elevatorSystem.DeclinePacts();
            }
            AscendNextFloor();
        }

        // ── Navigation ─────────────────────────────────────────────────────

        public void AscendNextFloor()
        {
            CloseElevatorUI();
            if (_elevatorSystem != null)
            {
                _elevatorSystem.LeaveElevator();
            }
        }
    }
}
