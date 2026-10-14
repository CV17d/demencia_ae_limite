// ============================================================================
// PhantomEnemy.cs — Hallucinatory phantom spawned during severe Mental Degradation
// ============================================================================
// At Fractured (0.6+) and Shattered (0.8+) tiers, the player's mind manifests
// hostile apparitions. They mimic attack charges and audio cues, but dissolve
// into smoke/static upon taking damage or reaching the player.
// ============================================================================

using UnityEngine;
using UnityEngine.AI;
using DemenciaNoLimite.Combat;

namespace DemenciaNoLimite.Enemies
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class PhantomEnemy : MonoBehaviour, IDamageable
    {
        [Header("Phantom Behavior")]
        [SerializeField] private float _chaseSpeed = 7.5f;
        [SerializeField] private float _lifetime = 8.0f;
        [SerializeField] private float _flickerFrequency = 14f;

        private NavMeshAgent _agent;
        private Transform _playerTransform;
        private float _spawnTime;
        private Renderer[] _renderers;

        public bool IsAlive => true;
        public float CurrentHealth => 1f;
        public float MaxHealth => 1f;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _renderers = GetComponentsInChildren<Renderer>();
            _spawnTime = Time.time;

            var col = GetComponent<Collider>();
            if (col == null)
            {
                var cap = gameObject.AddComponent<CapsuleCollider>();
                cap.height = 1.8f;
                cap.radius = 0.38f;
                cap.center = new Vector3(0f, 0.9f, 0f);
                cap.direction = 1;
            }
        }


        private void Start()
        {
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null)
            {
                _playerTransform = playerGo.transform;
            }

            if (_agent != null)
            {
                _agent.speed = _chaseSpeed;
                _agent.stoppingDistance = 0.5f;
            }
        }

        private void Update()
        {
            if (Time.time - _spawnTime >= _lifetime)
            {
                Dissolve();
                return;
            }

            if (_playerTransform != null && _agent != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(_playerTransform.position);

                // If touches player, vanish with hallucination jump-scare feedback
                if (Vector3.Distance(transform.position, _playerTransform.position) < 1.2f)
                {
                    OnTouchPlayer();
                    return;
                }
            }

            // Hallucinatory jitter / flicker
            float sin = Mathf.Sin(Time.time * _flickerFrequency);
            bool visible = sin > -0.2f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null) _renderers[i].enabled = visible;
            }
        }

        public float TakeDamage(DamageInfo info)
        {
            // Instantly vanishes on hit
            Dissolve();
            return 1f;
        }

        private void OnTouchPlayer()
        {
            Debug.Log("[PhantomEnemy] Phantom dissolved through player's mind.");
            Dissolve();
        }

        private void Dissolve()
        {
            Destroy(gameObject);
        }
    }
}
