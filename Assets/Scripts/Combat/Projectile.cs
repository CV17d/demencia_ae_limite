// ============================================================================
// Projectile.cs — Kinematic & ballistic projectile system
// ============================================================================
// Used for shotgun slugs, grenade launchers, and enemy projectile attacks.
// Sweeps forward using SphereCast/Raycast to prevent tunnel clipping at high velocity.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Combat
{
    public class Projectile : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float _speed = 30f;
        [SerializeField] private float _gravity = 0f;
        [SerializeField] private float _maxLifetime = 5f;
        [SerializeField] private float _radius = 0.15f;
        [SerializeField] private LayerMask _collisionMask = ~0;

        [Header("Explosion")]
        [SerializeField] private float _explosionRadius = 0f;

        private DamageInfo _damageTemplate;
        private Vector3 _velocity;
        private float _spawnTime;
        private bool _isInitialized;

        public void Initialize(DamageInfo damageInfo, Vector3 initialDirection, float speed, float gravity = 0f, float explosionRadius = 0f)
        {
            _damageTemplate = damageInfo;
            _speed = speed;
            _gravity = gravity;
            _explosionRadius = explosionRadius;
            _velocity = initialDirection.normalized * _speed;
            _spawnTime = Time.time;
            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized) return;

            if (Time.time - _spawnTime >= _maxLifetime)
            {
                OnMaxLifetimeExpired();
                return;
            }

            // Apply gravity
            if (_gravity > 0f)
            {
                _velocity += Vector3.down * (_gravity * Time.deltaTime);
            }

            Vector3 step = _velocity * Time.deltaTime;
            float stepDistance = step.magnitude;
            Vector3 direction = step.normalized;

            // Continuous collision check
            if (Physics.SphereCast(transform.position, _radius, direction, out RaycastHit hit, stepDistance, _collisionMask, QueryTriggerInteraction.Ignore))
            {
                // Ignore self
                if (_damageTemplate.Attacker != null && hit.collider.gameObject == _damageTemplate.Attacker)
                {
                    transform.position += step;
                    return;
                }

                transform.position = hit.point;
                OnHit(hit);
                return;
            }

            transform.position += step;
            if (_velocity.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(_velocity);
            }
        }

        private void OnHit(RaycastHit hit)
        {
            if (_explosionRadius > 0.01f)
            {
                Explode(hit.point);
            }
            else
            {
                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive)
                {
                    DamageInfo info = _damageTemplate;
                    info.HitPoint = hit.point;
                    info.HitNormal = hit.normal;
                    info.Direction = _velocity.normalized;
                    target.TakeDamage(info);
                }

                IKnockbackable knockbackable = hit.collider.GetComponentInParent<IKnockbackable>();
                if (knockbackable != null && _damageTemplate.KnockbackForce > 0f)
                {
                    knockbackable.ApplyKnockback(_velocity.normalized * _damageTemplate.KnockbackForce);
                }
            }

            Destroy(gameObject);
        }

        private void Explode(Vector3 center)
        {
            Collider[] hits = Physics.OverlapSphere(center, _explosionRadius, _collisionMask);
            foreach (var col in hits)
            {
                IDamageable target = col.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive)
                {
                    float distance = Vector3.Distance(center, col.transform.position);
                    float falloff = 1f - Mathf.Clamp01(distance / _explosionRadius);

                    DamageInfo info = _damageTemplate;
                    info.Amount *= falloff;
                    info.HitPoint = col.transform.position;
                    info.Direction = (col.transform.position - center).normalized;
                    target.TakeDamage(info);
                }
            }
        }

        private void OnMaxLifetimeExpired()
        {
            if (_explosionRadius > 0.01f)
            {
                Explode(transform.position);
            }
            Destroy(gameObject);
        }
    }
}
