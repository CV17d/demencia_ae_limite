// ============================================================================
// ElevatorDoorTrigger.cs — Exit portal & floor completion trigger
// ============================================================================
// Detects player arrival at the exit of a maze or boss room.
// Notifies GameManager to transition to the Elevator risk/reward state.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;

namespace DemenciaNoLimite.Level
{
    [RequireComponent(typeof(Collider))]
    public class ElevatorDoorTrigger : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private bool _isLocked = false;
        private bool _isTriggered = false;

        public void SetLocked(bool locked)
        {
            _isLocked = locked;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isLocked || _isTriggered) return;

            if (other.CompareTag("Player") || other.GetComponentInParent<DemenciaNoLimite.Player.PlayerController>() != null)
            {
                _isTriggered = true;
                Debug.Log("[ElevatorDoorTrigger] Player stepped into the elevator shaft. Transitioning!");
                GameManager.Instance?.OnCurrentFloorCleared();
            }
        }
    }
}
