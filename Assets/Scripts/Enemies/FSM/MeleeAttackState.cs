// ============================================================================
// MeleeAttackState.cs — Estado de Ataque Cuerpo a Cuerpo (Adictos/Pandilleros)
// ============================================================================
// Orquesta la secuencia de ataque físico en tres fases sin generar basura (GC):
// 1. Telegraph Visual (0.5s): El agente se detiene y encara al jugador.
// 2. Impacto: Ejecuta un SphereCastNonAlloc frontal para validar el área de daño.
// 3. Recuperación: Breve espera de enfriamiento antes de regresar a ChaseState.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Combat;

namespace DemenciaNoLimite.Enemies.FSM
{
    /// <summary>
    /// Comportamiento de ataque cuerpo a cuerpo con anticipación telegrafiada y detección volumétrica.
    /// </summary>
    public class MeleeAttackState : IEnemyState
    {
        private readonly EnemyBrain _brain;

        // Tiempos de la animación / coreografía de ataque
        private const float TELEGRAPH_DURATION = 0.5f;
        private const float SPHERECAST_RADIUS = 0.75f;
        private const float ATTACK_RANGE_OFFSET = 0.5f;

        private float _stateTimer;
        private bool _attackExecuted;

        /// <summary>
        /// Constructor con inyección del controlador EnemyBrain.
        /// </summary>
        public MeleeAttackState(EnemyBrain brain)
        {
            _brain = brain;
        }

        /// <summary>
        /// Se ejecuta al ingresar al estado de ataque.
        /// Detiene el agente inmediatamente para la fase de preparación/telegraph.
        /// </summary>
        public void Enter()
        {
            _stateTimer = 0f;
            _attackExecuted = false;

            _brain.StopAgent();
        }

        /// <summary>
        /// Se ejecuta cada frame. Administra el telegraph, impacto y cooldown sin allocations.
        /// </summary>
        public void Execute()
        {
            if (_brain.PlayerTransform == null)
            {
                _brain.ChangeState(_brain.IdleStateInstance);
                return;
            }

            _stateTimer += Time.deltaTime;

            // FASE 1: Telegraph visual (0.0s a 0.5s).
            // El enemigo fija la mirada en el jugador mientras prepara el golpe.
            if (_stateTimer < TELEGRAPH_DURATION)
            {
                _brain.RotateTowards(_brain.PlayerTransform.position, 12f);
                return;
            }

            // FASE 2: Ejecución del golpe al cumplir exactamente los 0.5s de telegraph.
            if (!_attackExecuted)
            {
                _attackExecuted = true;
                ExecuteSphereCastAttack();
            }

            // FASE 3: Enfriamiento posterior al ataque (Cooldown).
            float totalCooldown = TELEGRAPH_DURATION + (_brain.Data != null ? _brain.Data.AttackCooldown : 1.0f);
            if (_stateTimer >= totalCooldown)
            {
                // Regresar a persecución táctica
                if (_brain.ChaseStateInstance != null)
                {
                    _brain.ChangeState(_brain.ChaseStateInstance);
                }
            }
        }

        /// <summary>
        /// Lanza un barrido volumétrico SphereCastNonAlloc frontal para aplicar daño físico.
        /// </summary>
        private void ExecuteSphereCastAttack()
        {
            Vector3 origin = _brain.transform.position + Vector3.up * 1.0f;
            Vector3 forwardDirection = _brain.transform.forward;
            float attackDistance = (_brain.Data != null ? _brain.Data.AttackRange : 2.0f) + ATTACK_RANGE_OFFSET;

            // Invocación del método con buffer preasignado en EnemyBrain (0 GC)
            int hitCount = _brain.PerformSphereCastNonAlloc(
                origin,
                SPHERECAST_RADIUS,
                forwardDirection,
                attackDistance,
                _brain.PlayerLayer
            );

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _brain.GetSphereCastHit(i);
                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();

                if (damageable != null && damageable.IsAlive)
                {
                    float baseDamage = _brain.Data != null ? _brain.Data.BaseDamage : 10f;
                    float finalDamage = baseDamage * _brain.DamageMultiplier;

                    DamageInfo damageInfo = new DamageInfo(
                        amount: finalDamage,
                        attacker: _brain.gameObject,
                        hitPoint: hit.point,
                        hitNormal: hit.normal,
                        direction: forwardDirection,
                        sourceType: DamageSourceType.Melee,
                        isCritical: false,
                        isHeadshot: false,
                        ignoreDefense: false,
                        knockbackForce: 4.5f
                    );

                    damageable.TakeDamage(damageInfo);
                    break; // Solo impactar una vez al objetivo principal
                }
            }
        }

        /// <summary>
        /// Se ejecuta al salir del estado de ataque cuerpo a cuerpo.
        /// </summary>
        public void Exit()
        {
            _stateTimer = 0f;
            _attackExecuted = false;
        }
    }
}
