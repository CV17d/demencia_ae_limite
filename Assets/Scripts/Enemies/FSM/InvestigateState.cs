// ============================================================================
// InvestigateState.cs — Estado de Investigación y Búsqueda Cautelosa
// ============================================================================
// Conducido por ruidos percibidos (Ruta Pacifista/Sigilo).
// El enemigo reduce su velocidad, camina hacia la fuente del sonido mientras
// evalúa constantemente su cono de visión. Si llega y no encuentra nada,
// inspecciona el área durante unos segundos antes de regresar a Idle.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Enemies.FSM
{
    /// <summary>
    /// Comportamiento de inspección acústica. El enemigo se desplaza con sigilo
    /// hacia una coordenada sospechosa y rastrea visualmente posibles intrusos.
    /// </summary>
    public class InvestigateState : IEnemyState
    {
        private readonly EnemyBrain _brain;

        // Parámetros de inspección
        private const float CAUTIOUS_SPEED_MULTIPLIER = 0.55f;
        private const float ARRIVAL_DISTANCE_THRESHOLD = 0.6f;
        private const float SEARCH_DURATION = 3.0f;
        private const float VISION_SCAN_INTERVAL = 0.1f;

        private Vector3 _targetPosition;
        private float _originalAgentSpeed;
        private float _searchTimer;
        private float _visionScanTimer;
        private bool _hasArrivedAtPoint;

        /// <summary>
        /// Constructor con inyección del EnemyBrain.
        /// </summary>
        public InvestigateState(EnemyBrain brain)
        {
            _brain = brain;
        }

        /// <summary>
        /// Asigna las coordenadas del ruido para ser investigadas.
        /// </summary>
        /// <param name="position">Punto tridimensional donde ocurrió el sonido.</param>
        public void SetTargetPosition(Vector3 position)
        {
            _targetPosition = position;
        }

        /// <summary>
        /// Se ejecuta al ingresar al estado. Reduce la velocidad del agente y define la ruta.
        /// </summary>
        public void Enter()
        {
            _searchTimer = 0f;
            _visionScanTimer = 0f;
            _hasArrivedAtPoint = false;

            if (_brain.Agent != null)
            {
                _originalAgentSpeed = _brain.Agent.speed;
                _brain.Agent.speed = _originalAgentSpeed * CAUTIOUS_SPEED_MULTIPLIER;
                _brain.ResumeAgent();
                _brain.SetAgentDestination(_targetPosition);
            }
        }

        /// <summary>
        /// Se ejecuta cada frame. Escanea visión y evalúa la llegada al punto acústico.
        /// Cero asignaciones en memoria (GC).
        /// </summary>
        public void Execute()
        {
            // 1. ESCANEO VISUAL PASIVO
            _visionScanTimer += Time.deltaTime;
            if (_visionScanTimer >= VISION_SCAN_INTERVAL)
            {
                _visionScanTimer = 0f;

                if (_brain.Sensory != null && _brain.Sensory.CheckVision())
                {
                    // ¡Jugador avistado! Cortar investigación e iniciar cacería inmediata
                    if (_brain.ChaseStateInstance != null)
                    {
                        _brain.ChangeState(_brain.ChaseStateInstance);
                        return;
                    }
                }
            }

            // 2. TRAYECTORIA Y LLEGADA
            if (!_hasArrivedAtPoint)
            {
                if (_brain.Agent != null && !_brain.Agent.pathPending)
                {
                    if (_brain.Agent.remainingDistance <= ARRIVAL_DISTANCE_THRESHOLD)
                    {
                        _hasArrivedAtPoint = true;
                        _brain.StopAgent();
                    }
                }
                return;
            }

            // 3. FASE DE BÚSQUEDA EN EL LUGAR (Mirar alrededor durante 3s)
            _searchTimer += Time.deltaTime;

            // Rotación oscilatoria para emular inspección de los alrededores
            float lookYawSpeed = Mathf.Sin(_searchTimer * 2.5f) * 35f;
            _brain.transform.Rotate(0f, lookYawSpeed * Time.deltaTime, 0f);

            if (_searchTimer >= SEARCH_DURATION)
            {
                // No se halló nada: regresar a IdleState
                _brain.ChangeState(_brain.IdleStateInstance);
            }
        }

        /// <summary>
        /// Se ejecuta al salir del estado. Restaura la velocidad original del NavMeshAgent.
        /// </summary>
        public void Exit()
        {
            if (_brain.Agent != null)
            {
                _brain.Agent.speed = _originalAgentSpeed;
            }

            _hasArrivedAtPoint = false;
            _searchTimer = 0f;
        }
    }
}
