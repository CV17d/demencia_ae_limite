// ============================================================================
// IPickup.cs & PickupBase.cs — Collectible items & magnetic attraction logic
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Pickups
{
    public interface IPickup
    {
        bool CanPickup(GameObject player);
        void Collect(GameObject player);
    }

    [RequireComponent(typeof(Collider))]
    public abstract class PickupBase : MonoBehaviour, IPickup
    {
        [Header("Attraction Physics")]
        [SerializeField] private float _detectionRadius = 4f;
        [SerializeField] private float _magnetSpeed = 10f;
        [SerializeField] private float _bobSpeed = 2f;
        [SerializeField] private float _bobHeight = 0.2f;

        private Transform _playerTransform;
        private Vector3 _startPosition;
        private bool _isAttracting;

        protected virtual void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            _startPosition = transform.position;
        }

        protected virtual void Update()
        {
            if (_playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
            }

            if (_playerTransform != null)
            {
                float dist = Vector3.Distance(transform.position, _playerTransform.position);
                if (dist <= _detectionRadius && CanPickup(_playerTransform.gameObject))
                {
                    _isAttracting = true;
                }
            }

            if (_isAttracting && _playerTransform != null)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    _playerTransform.position + Vector3.up * 0.5f,
                    _magnetSpeed * Time.deltaTime
                );
            }
            else
            {
                // Idle hover bob
                float newY = _startPosition.y + Mathf.Sin(Time.time * _bobSpeed) * _bobHeight;
                transform.position = new Vector3(_startPosition.x, newY, _startPosition.z);
            }
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") || other.GetComponentInParent<DemenciaNoLimite.Player.PlayerController>() != null)
            {
                GameObject playerGo = other.gameObject;
                if (CanPickup(playerGo))
                {
                    Collect(playerGo);
                    Destroy(gameObject);
                }
            }
        }

        public abstract bool CanPickup(GameObject player);
        public abstract void Collect(GameObject player);
    }
}
