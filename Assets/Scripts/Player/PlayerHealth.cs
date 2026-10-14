// ============================================================================
// PlayerHealth.cs — Player health & damage receiver
// ============================================================================
// Synchronizes with PlayerStats on the GameManager.
// Triggers damage feedback, invulnerability frames, and death handling.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Combat;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Player
{
    public class PlayerHealth : MonoBehaviour, IDamageable, IKnockbackable
    {
        [Header("Invulnerability Settings")]
        [SerializeField] private float _invulnerabilityDuration = 0.6f;
        private float _lastDamageTime = -999f;

        [Header("Knockback Settings")]
        [SerializeField] private CharacterController _characterController;
        private Vector3 _knockbackVelocity;

        public bool IsAlive => CurrentHealth > 0f;
        public float CurrentHealth => GameManager.Instance?.Player?.CurrentHP ?? 100f;
        public float MaxHealth => GameManager.Instance?.Player?.MaxHP ?? 100f;

        private void Awake()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }
        }

        private void Update()
        {
            // Dampen knockback velocity
            if (_knockbackVelocity.sqrMagnitude > 0.01f)
            {
                if (_characterController != null && _characterController.enabled)
                {
                    _characterController.Move(_knockbackVelocity * Time.deltaTime);
                }
                _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, Time.deltaTime * 8f);
            }
        }

        public float TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return 0f;

            // Invulnerability window check
            if (Time.time - _lastDamageTime < _invulnerabilityDuration)
            {
                return 0f;
            }

            _lastDamageTime = Time.time;

            PlayerStats playerStats = GameManager.Instance?.Player;
            if (playerStats == null) return 0f;

            float actualDamageDealt = playerStats.TakeDamage(info.Amount, info.IgnoreDefense);

            GameEvents.FirePlayerDamaged(actualDamageDealt);

            if (info.KnockbackForce > 0f)
            {
                ApplyKnockback(info.Direction * info.KnockbackForce);
            }

            if (playerStats.CurrentHP <= 0f)
            {
                Die();
            }

            return actualDamageDealt;
        }

        public void ApplyKnockback(Vector3 force)
        {
            _knockbackVelocity += force;
        }

        private void Die()
        {
            Debug.Log("[PlayerHealth] Player has perished in the tower.");
            GameManager.Instance?.EndRun(RunEndReason.PlayerDeath);
        }
    }
}
