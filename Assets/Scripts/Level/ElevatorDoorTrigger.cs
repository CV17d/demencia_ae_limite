// ============================================================================
// ElevatorDoorTrigger.cs — Elevator Entry & Floor Progression Trigger
// ============================================================================
// Detects the player stepping into the elevator cabin volume.
// Fixes the progression softlock by initiating the clean 4-step floor transition
// lifecycle via GameManager without hanging or waiting on disconnected UI events.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Level
{
    [RequireComponent(typeof(Collider))]
    public class ElevatorDoorTrigger : MonoBehaviour
    {
        [Header("Elevator Configuration")]
        [SerializeField] private bool _isLocked = false;
        [SerializeField] private int _nextFloorNumber = 2;

        [Header("State")]
        [SerializeField] private bool _isTriggered = false;

        private Collider _cachedCollider;

        private void Awake()
        {
            _cachedCollider = GetComponent<Collider>();
            if (_cachedCollider != null && !_cachedCollider.isTrigger)
            {
                Debug.LogWarning("[ElevatorDoorTrigger] BoxCollider was not marked as IsTrigger! Auto-correcting to IsTrigger=true.");
                _cachedCollider.isTrigger = true;
            }
        }

        public void SetLocked(bool locked)
        {
            _isLocked = locked;
        }

        public void SetNextFloorNumber(int nextFloor)
        {
            _nextFloorNumber = nextFloor;
        }

        public void ResetTrigger()
        {
            _isTriggered = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // TRIGGER ENTRY & PROGRESSION SEQUENCE
        // ════════════════════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            // Debounce guard to prevent duplicate executions
            if (_isLocked || _isTriggered) return;

            // Check for Player tag or PlayerController script
            bool isPlayer = other.CompareTag("Player") ||
                            other.GetComponentInParent<PlayerController>() != null ||
                            (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player"));

            if (!isPlayer) return;

            _isTriggered = true;
            Debug.Log($"[ElevatorDoorTrigger] Player stepped into Elevator to Floor {_nextFloorNumber}! Executing transition sequence...");

            if (GameManager.Instance != null)
            {
                // Initiate the progression sequence:
                // a) Increment floor counter
                // b) Destroy previous floor container
                // c) Instantiate new floor
                // d) Safely teleport player
                GameManager.Instance.AdvanceToNextFloor();
            }
            else
            {
                Debug.LogError("[ElevatorDoorTrigger] Critical: GameManager.Instance is null! Cannot transition floor.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // IN-WORLD HUD / ELEVATOR CALLOUT
        // ════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (_isTriggered) return;

            var cam = Camera.main;
            if (cam == null) return;

            Vector3 worldPos = transform.position + Vector3.up * 1.8f;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            if (screenPos.z < 0.5f || screenPos.z > 30f) return;

            float alpha = Mathf.Clamp01(1f - (screenPos.z / 30f));
            float x = screenPos.x - 120f;
            float y = Screen.height - screenPos.y;

            GUI.color = new Color(0.1f, 0.95f, 0.35f, alpha);
            GUI.Label(new Rect(x, y - 22, 240, 24), $"<size=12><b>▲ ELEVADOR A PISO {_nextFloorNumber:D2} ▲</b></size>");
            GUI.color = new Color(1f, 1f, 1f, alpha * 0.9f);
            GUI.Label(new Rect(x + 15, y - 2, 210, 20), "<size=10>Entra para ascender al siguiente piso</size>");
            GUI.color = Color.white;
        }
    }
}
