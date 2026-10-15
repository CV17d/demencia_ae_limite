// ============================================================================
// ElevatorDoorTrigger.cs — Instant Floor Teleportation Volume
// ============================================================================
// Acts as a clean, instantaneous teleporter between pre-generated tower floors.
// When the player steps into the cabin:
//   - Identifies the next floor in the 30-floor stack.
//   - Updates CurrentFloorNumber in GameManager.
//   - Teleports the player to the pre-generated Start Room of the next floor
//     (Y moves up 100 units to the next pre-generated floor).
//   - No runtime geometry destruction or generation, eliminating void drops.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Level
{
    [RequireComponent(typeof(Collider))]
    public class ElevatorDoorTrigger : MonoBehaviour
    {
        [Header("Floor Configuration")]
        [SerializeField] private int _currentFloorNumber = 1;
        [SerializeField] private int _nextFloorNumber = 2;
        [SerializeField] private bool _isLocked = false;
        [SerializeField] private bool _isTriggered = false;

        private Collider _cachedCollider;

        private void Awake()
        {
            _cachedCollider = GetComponent<Collider>();
            if (_cachedCollider != null && !_cachedCollider.isTrigger)
            {
                _cachedCollider.isTrigger = true;
            }
        }

        public void SetFloorNumbers(int currentFloor, int nextFloor)
        {
            _currentFloorNumber = currentFloor;
            _nextFloorNumber = nextFloor;
        }

        public void SetLocked(bool locked)
        {
            _isLocked = locked;
        }

        public void ResetTrigger()
        {
            _isTriggered = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isLocked || _isTriggered) return;

            bool isPlayer = other.CompareTag("Player") ||
                            other.GetComponentInParent<PlayerController>() != null ||
                            (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player"));

            if (!isPlayer) return;

            _isTriggered = true;
            Debug.Log($"[ElevatorDoorTrigger] Player entered Elevator on Floor {_currentFloorNumber}. Teleporting to Floor {_nextFloorNumber}...");

            if (GameManager.Instance != null)
            {
                // Pure teleportation to next pre-generated floor
                GameManager.Instance.TeleportToFloor(_nextFloorNumber);
            }
            else
            {
                Debug.LogError("[ElevatorDoorTrigger] GameManager.Instance is null! Cannot teleport player.");
            }
        }

        private void OnGUI()
        {
            if (_isTriggered) return;

            var cam = Camera.main;
            if (cam == null) return;

            Vector3 worldPos = transform.position + Vector3.up * 1.8f;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            if (screenPos.z < 0.5f || screenPos.z > 25f) return;

            float alpha = Mathf.Clamp01(1f - (screenPos.z / 25f));
            float x = screenPos.x - 120f;
            float y = Screen.height - screenPos.y;

            GUI.color = new Color(0.1f, 0.95f, 0.35f, alpha);
            if (_nextFloorNumber > FloorConfig.MAX_TOWER_FLOORS)
            {
                GUI.Label(new Rect(x, y - 22, 240, 24), $"<size=12><b>▲ CIMA DE LA TORRE (VICTORIA) ▲</b></size>");
            }
            else
            {
                GUI.Label(new Rect(x, y - 22, 240, 24), $"<size=12><b>▲ TELETRANSPORTAR A PISO {_nextFloorNumber:D2} ▲</b></size>");
            }
            GUI.color = new Color(1f, 1f, 1f, alpha * 0.9f);
            GUI.Label(new Rect(x + 15, y - 2, 210, 20), "<size=10>Entra para ascender inmediatamente</size>");
            GUI.color = Color.white;
        }
    }
}
