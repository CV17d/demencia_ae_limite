// ============================================================================
// ChaseState.cs — Estado de Persecución y Reposicionamiento Táctico
// ============================================================================
// Gestiona el acercamiento al jugador según el arquetipo de combate:
// - Melee (Adictos/Pandilleros): Persigue directamente al jugador para acortar distancia.
// - Ranged (Sicarios): Mantiene una distancia óptima de combate entre 5 y 10 metros,
//   retrocediendo si el jugador se acerca demasiado y avanzando si se aleja.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Enemies.FSM
{
    /// <summary>
    /// Comportamiento de persecución adaptable para enemigos cuerpo a cuerpo y a distancia.
    /// Actualiza el destino en el NavMesh y evalúa rangos para transicionar al ataque.
    /// </summary>
    public class ChaseState : IEnemyState
    {
        private readonly EnemyBrain _brain;

        // Intervalo de actualización del destino en el NavMesh para ahorrar ciclos de CPU
        private const float NAV_UPDATE_INTERVAL = 0.15f;
        private float _navTimer;

        // Parámetros de rango para enemigos a distancia (Sicarios)
        private const float RANGED_MIN_DISTANCE = 5.0f;
        private const float RANGED_MAX_DISTANCE = 10.0f;

        /// <summary>
        /// Constructor que inyecta la referencia del cerebro de la entidad.
        /// </summary>
        /// <param name="brain">Controlador central del enemigo.</param>
        public ChaseState(EnemyBrain brain)
        {
            _brain = brain;
        }

        /// <summary>
        /// Se ejecuta al ingresar al estado de persecución. Reanuda el NavMeshAgent.
        /// </summary>
        public void Enter()
        {
            _navTimer = 0f;
            _brain.ResumeAgent();
        }

        /// <summary>
        /// Se ejecuta cada frame. Evalúa distancias, actualiza destino y conmuta estados de ataque.
        /// Diseñado sin asignaciones dinámicas (cero GC).
        /// </summary>
        public void Execute()
        {
            if (_brain.PlayerTransform == null || _brain.Data == null)
            {
                _brain.ChangeState(_brain.IdleStateInstance);
                return;
            }

            float distanceToPlayer = _brain.GetDistanceToPlayer();

            // Si el jugador superó por mucho el rango de detección, regresar a Idle
            if (distanceToPlayer > _brain.Data.DetectionRange * 1.5f)
            {
                _brain.ChangeState(_brain.IdleStateInstance);
                return;
            }

            bool isRanged = _brain.Data.HasRangedAttack;

            if (isRanged)
            {
                ExecuteRangedChase(distanceToPlayer);
            }
            else
            {
                ExecuteMeleeChase(distanceToPlayer);
            }
        }

        /// <summary>
        /// Lógica de persecución directa para enemigos cuerpo a cuerpo (Melee).
        /// </summary>
        private void ExecuteMeleeChase(float distanceToPlayer)
        {
            // Si ya está en rango de ataque cuerpo a cuerpo, transicionar a MeleeAttackState
            if (distanceToPlayer <= _brain.Data.AttackRange)
            {
                if (_brain.MeleeAttackStateInstance != null)
                {
                    _brain.ChangeState(_brain.MeleeAttackStateInstance);
                    return;
                }
            }

            // Actualización periódica de la ruta en el NavMesh
            _navTimer += Time.deltaTime;
            if (_navTimer >= NAV_UPDATE_INTERVAL)
            {
                _navTimer = 0f;
                _brain.SetAgentDestination(_brain.PlayerTransform.position);
            }
        }

        /// <summary>
        /// Lógica de posicionamiento táctico para enemigos a distancia (Ranged / Sicarios).
        /// Mantiene al enemigo entre 5 y 10 metros del jugador.
        /// </summary>
        private void ExecuteRangedChase(float distanceToPlayer)
        {
            Vector3 playerPos = _brain.PlayerTransform.position;
            Vector3 myPos = _brain.transform.position;

            // CASO 1: El jugador está demasiado cerca (< 5m). Retroceder.
            if (distanceToPlayer < RANGED_MIN_DISTANCE)
            {
                _navTimer += Time.deltaTime;
                if (_navTimer >= NAV_UPDATE_INTERVAL)
                {
                    _navTimer = 0f;
                    Vector3 retreatDir = (myPos - playerPos).normalized;
                    Vector3 retreatTarget = myPos + retreatDir * (RANGED_MIN_DISTANCE * 0.8f);
                    _brain.SetAgentDestination(retreatTarget);
                }
                return;
            }

            // CASO 2: El jugador está demasiado lejos (> 10m). Avanzar.
            if (distanceToPlayer > RANGED_MAX_DISTANCE)
            {
                _navTimer += Time.deltaTime;
                if (_navTimer >= NAV_UPDATE_INTERVAL)
                {
                    _navTimer = 0f;
                    _brain.SetAgentDestination(playerPos);
                }
                return;
            }

            // CASO 3: En el rango óptimo (5m a 10m).
            // Detenerse, rotar hacia el jugador y verificar línea de visión para atacar.
            _brain.StopAgent();
            _brain.RotateTowards(playerPos, 8f);

            Vector3 eyePos = _brain.transform.position + Vector3.up * 1.5f;
            Vector3 targetCenter = playerPos + Vector3.up * 1.0f;

            if (_brain.CheckLineOfSight(eyePos, targetCenter))
            {
                if (_brain.RangedAttackStateInstance != null)
                {
                    _brain.ChangeState(_brain.RangedAttackStateInstance);
                }
            }
        }

        /// <summary>
        /// Se ejecuta al salir del estado de persecución.
        /// </summary>
        public void Exit()
        {
            _navTimer = 0f;
        }
    }
}
