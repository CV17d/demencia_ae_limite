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
        [SerializeField] private int _nextFloorNumber = 2;
        private bool _isTriggered = false;

        public void SetLocked(bool locked)
        {
            _isLocked = locked;
        }

        public void SetNextFloorNumber(int nextFloor)
        {
            _nextFloorNumber = nextFloor;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isLocked || _isTriggered) return;

            if (other.CompareTag("Player") || other.GetComponentInParent<DemenciaNoLimite.Player.PlayerController>() != null)
            {
                _isTriggered = true;
                Debug.Log($"[ElevatorDoorTrigger] Player stepped into elevator to Floor {_nextFloorNumber}. Transitioning!");
                GameManager.Instance?.OnCurrentFloorCleared();
            }
        }

        private void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 worldPos = transform.position + Vector3.up * 2.0f;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            if (screenPos.z < 0.5f || screenPos.z > 35f) return;

            float alpha = Mathf.Clamp01(1f - (screenPos.z / 35f));
            float x = screenPos.x - 110f;
            float y = Screen.height - screenPos.y;

            GUI.color = new Color(0.1f, 0.95f, 0.3f, alpha);
            GUI.Label(new Rect(x, y - 22, 220, 24), $"<size=12><b>▲ ELEVADOR A PISO {_nextFloorNumber:D2} ▲</b></size>");
            GUI.color = new Color(1f, 1f, 1f, alpha * 0.85f);
            GUI.Label(new Rect(x + 15, y - 2, 190, 20), "<size=10>Entra para ascender</size>");
            GUI.color = Color.white;
        }
    }
}
