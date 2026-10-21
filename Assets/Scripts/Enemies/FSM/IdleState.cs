// ============================================================================
// IdleState.cs — Estado de Reposo y Alerta Inicial
// ============================================================================
// Estado pasivo del enemigo mientras no ha detectado amenazas.
// Mantiene al NavMeshAgent quieto y monitorea la proximidad del jugador
// o alertas del sistema sensorial sin provocar Garbage Collection.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Enemies.FSM
{
    /// <summary>
    /// Comportamiento de reposo y escaneo pasivo para los enemigos.
    /// Detiene la marcha y evalúa condiciones de transición (Chase, Investigate).
    /// </summary>
    public class IdleState : IEnemyState
    {
        private readonly EnemyBrain _brain;
        private float _stateTimer;

        /// <summary>
        /// Constructor que inyecta la referencia del controlador central.
        /// </summary>
        /// <param name="brain">Instancia de EnemyBrain propietaria del estado.</param>
        public IdleState(EnemyBrain brain)
        {
            _brain = brain;
        }

        /// <summary>
        /// Se ejecuta al entrar al estado Idle. Detiene el NavMeshAgent.
        /// </summary>
        public void Enter()
        {
            _stateTimer = 0f;
            _brain.StopAgent();
        }

        /// <summary>
        /// Se ejecuta cada frame. Monitorea la presencia del jugador.
        /// Diseñado para cero allocations de memoria en Update().
        /// </summary>
        public void Execute()
        {
            _stateTimer += Time.deltaTime;

            if (_brain.PlayerTransform == null) return;

            // 1. Detección visual direccional mediante SensorySystem (Cono de 60° para sigilo)
            if (_brain.Sensory != null)
            {
                if (_brain.Sensory.CheckVision())
                {
                    OnTargetDetected();
                    return;
                }
            }
            else if (_brain.Data != null)
            {
                // Fallback omnidireccional si la entidad no cuenta con módulo sensorial
                if (_brain.GetDistanceToPlayer() <= _brain.Data.DetectionRange)
                {
                    OnTargetDetected();
                }
            }
        }

        /// <summary>
        /// Se ejecuta al abandonar el estado Idle.
        /// </summary>
        public void Exit()
        {
            _stateTimer = 0f;
        }

        /// <summary>
        /// Punto de extensión invocado al detectar una amenaza.
        /// En los siguientes pasos conectará con ChaseState o InvestigateState.
        /// </summary>
        protected virtual void OnTargetDetected()
        {
            if (_brain.ChaseStateInstance != null)
            {
                _brain.ChangeState(_brain.ChaseStateInstance);
            }
        }
    }
}
