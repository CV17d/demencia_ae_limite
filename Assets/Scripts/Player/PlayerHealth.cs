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
        [Tooltip("Velocidad máxima permitida de empuje para evitar atravesar esquinas o muros.")]
        [SerializeField] private float _maxKnockbackSpeed = 1.2f;
        private Vector3 _lastSafeGroundedPosition;
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
            // 1. Guardar última posición segura en tierra firme
            if (_characterController != null && _characterController.enabled && _characterController.isGrounded)
            {
                _lastSafeGroundedPosition = transform.position;
            }

            // 2. Red de seguridad anti-vacío: rescate inmediato si es expulsado fuera del mapa
            if (_lastSafeGroundedPosition != Vector3.zero && transform.position.y < _lastSafeGroundedPosition.y - 3.5f)
            {
                _knockbackVelocity = Vector3.zero;
                if (_characterController != null) _characterController.enabled = false;
                transform.position = _lastSafeGroundedPosition + Vector3.up * 0.2f;
                if (_characterController != null) _characterController.enabled = true;
                Debug.LogWarning("[PlayerHealth] 🛡️ Jugador protegido contra caída al vacío. Reposicionado en suelo seguro.");
            }

            // 3. Amortiguar velocidad de retroceso (knockback) con chequeo preventivo contra muros
            if (_knockbackVelocity.sqrMagnitude > 0.01f)
            {
                float moveDist = _knockbackVelocity.magnitude * Time.deltaTime;
                Vector3 moveDir = _knockbackVelocity.normalized;

                // SphereCast preventivo: si hay una pared detrás, cancelar knockback antes de que la física penetre
                int obstacleMask = ~((1 << gameObject.layer) | (1 << LayerMask.NameToLayer("Enemy")));
                if (Physics.SphereCast(transform.position + Vector3.up * 0.8f, 0.35f, moveDir, out RaycastHit preHit, moveDist + 0.25f, obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    if (Mathf.Abs(preHit.normal.y) < 0.35f)
                    {
                        _knockbackVelocity = Vector3.zero;
                    }
                }

                if (_knockbackVelocity.sqrMagnitude > 0.01f && _characterController != null && _characterController.enabled)
                {
                    _characterController.Move(_knockbackVelocity * Time.deltaTime);
                }
                _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, Time.deltaTime * 14f);
            }
            else
            {
                _knockbackVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Detecta colisiones físicas durante el movimiento.
        /// Distingue paredes verticales (|hit.normal.y| < 0.3f) de rampas y suelos inclinados (hit.normal.y >= 0.3f),
        /// frenando o deslizando el knockback únicamente contra muros reales sin bloquear el paso en rampas ni congelar el movimiento.
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Solo actuar si hay una fuerza de retroceso (knockback) activa
            if (_knockbackVelocity.sqrMagnitude <= 0.01f) return;

            // 1. Distinguir paredes y obstáculos verticales de rampas, pendientes o escalones:
            // Pared vertical: normal casi horizontal (|hit.normal.y| < 0.3f).
            // Rampa o suelo inclinado: normal con inclinación vertical sustancial (|hit.normal.y| >= 0.3f).
            if (Mathf.Abs(hit.normal.y) < 0.3f)
            {
                // 2. Verificar si el knockback empuja en dirección directa al muro (dot < 0)
                float dot = Vector3.Dot(_knockbackVelocity, hit.normal);
                if (dot < 0f)
                {
                    // Deslizar el retroceso a lo largo de la pared (restando la componente que penetra el muro)
                    Vector3 normalHorizontal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                    _knockbackVelocity -= normalHorizontal * dot;

                    // 3. Ajuste fino en esquinas: si el empuje queda atrapado en una esquina (baja magnitud residual
                    // o choque frontal contra el vértice), extinguir el knockback para que el jugador nunca atraviese la pared,
                    // permitiendo que los inputs de movimiento del usuario continúen respondiendo al 100%.
                    if (_knockbackVelocity.sqrMagnitude < 0.15f || Vector3.Dot(_knockbackVelocity.normalized, -normalHorizontal) > 0.65f)
                    {
                        _knockbackVelocity = Vector3.zero;
                    }
                }
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

        /// <summary>
        /// Sobrecarga para compatibilidad con llamadas directas por valor y SendMessage("TakeDamage", float).
        /// </summary>
        public float TakeDamage(float amount)
        {
            return TakeDamage(new DamageInfo(
                amount: amount,
                attacker: null,
                hitPoint: transform.position,
                hitNormal: -transform.forward,
                direction: transform.forward,
                sourceType: DamageSourceType.Melee
            ));
        }

        public void ApplyKnockback(Vector3 force)
        {
            // Retroceso estrictamente en el plano horizontal (impide atravesar el suelo o salir volando al techo)
            force.y = 0f;

            // Limitar la magnitud total acumulada para impedir que el jugador sea catapultado fuera del mapa
            Vector3 targetVel = _knockbackVelocity + force;
            _knockbackVelocity = Vector3.ClampMagnitude(targetVel, _maxKnockbackSpeed);
        }

        private void Die()
        {
            Debug.Log("[PlayerHealth] Player has perished in the tower.");
            GameManager.Instance?.EndRun(RunEndReason.PlayerDeath);
        }
    }
}
