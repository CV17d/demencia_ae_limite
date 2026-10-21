// ============================================================================
// EnemyBrain.cs — Controlador Central de IA y Ejecutor de FSM
// ============================================================================
// Orquesta los estados de la IA (IEnemyState), la navegación con NavMeshAgent,
// y la recepción de daño y empuje (IDamageable, IKnockbackable).
//
// Optimizaciones Críticas:
// - Cero allocations en Update() para evitar Garbage Collection (GC).
// - Preasignación de buffers para Physics.SphereCastNonAlloc y OverlapSphereNonAlloc.
// - Cacheo directo del Transform del jugador (vía PlayerController.Instance).
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.AI;
using DemenciaNoLimite.Combat;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Enemies.FSM
{
    [SelectionBase]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class EnemyBrain : MonoBehaviour, IDamageable, IKnockbackable
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN SERIALIZADA
        // ════════════════════════════════════════════════════════════════════

        [Header("Datos de Configuración")]
        [Tooltip("ScriptableObject con los parámetros base del enemigo (HP, velocidad, rangos).")]
        [SerializeField] private EnemyData _enemyData;

        [Header("Capas de Detección e Impacto")]
        [Tooltip("LayerMask asignada exclusivamente al jugador para chequeos físicos.")]
        [SerializeField] private LayerMask _playerLayer = ~0;

        [Tooltip("LayerMask del entorno y obstáculos (paredes, columnas) para línea de visión.")]
        [SerializeField] private LayerMask _environmentLayer = ~0;

        [Header("Dimensiones y Colisión")]
        [SerializeField] private float _modelHeight = 1.8f;
        [SerializeField] private float _modelRadius = 0.38f;

        // ════════════════════════════════════════════════════════════════════
        // BUFFERS PREASIGNADOS (ZERO-ALLOCATION PHYSICS)
        // ════════════════════════════════════════════════════════════════════

        private const int MAX_SPHERECAST_HITS = 8;
        private const int MAX_OVERLAP_HITS = 16;
        private const int MAX_RAYCAST_HITS = 4;

        private readonly RaycastHit[] _sphereCastBuffer = new RaycastHit[MAX_SPHERECAST_HITS];
        private readonly Collider[] _overlapBuffer = new Collider[MAX_OVERLAP_HITS];
        private readonly RaycastHit[] _raycastBuffer = new RaycastHit[MAX_RAYCAST_HITS];

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y COMPONENTES CACHEADOS
        // ════════════════════════════════════════════════════════════════════

        private NavMeshAgent _agent;
        private CapsuleCollider _capsuleCollider;
        private Transform _playerTransform;
        private IDamageable _playerDamageable;

        private IEnemyState _currentState;
        private float _currentHealth;
        private float _maxHealth;
        private bool _isInitialized;

        // Multiplicadores dinámicos aplicados por pactos o dificultades
        private float _damageMultiplier = 1f;
        private float _speedMultiplier = 1f;

        // Instancias preexistentes de estados base para evitar 'new' en tiempo de ejecución
        public IdleState IdleStateInstance { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Se dispara al producirse una transición de estado en la FSM.
        /// </summary>
        public event Action<IEnemyState> OnStateChanged;

        /// <summary>
        /// Se dispara al recibir daño válido.
        /// </summary>
        public event Action<DamageInfo> OnDamageTaken;

        /// <summary>
        /// Se dispara al morir la entidad.
        /// </summary>
        public event Action OnDeath;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public bool IsAlive => _currentHealth > 0f;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public EnemyData Data => _enemyData;
        public NavMeshAgent Agent => _agent;
        public Transform PlayerTransform => _playerTransform;
        public IDamageable PlayerDamageable => _playerDamageable;
        public LayerMask PlayerLayer => _playerLayer;
        public LayerMask EnvironmentLayer => _environmentLayer;
        public IEnemyState CurrentState => _currentState;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _capsuleCollider = GetComponent<CapsuleCollider>();

            ConfigurarColisionador();

            // Instanciamos los estados base una sola vez en el ciclo de vida
            IdleStateInstance = new IdleState(this);
        }

        private void Start()
        {
            InicializarObjetivoJugador();
            InicializarEstadisticas();

            // El estado por defecto inicial es Idle
            ChangeState(IdleStateInstance);
        }

        private void Update()
        {
            if (!IsAlive || !_isInitialized) return;

            // Ejecución polimórfica continua sin boxing ni asignación de memoria
            _currentState?.Execute();
        }

        // ════════════════════════════════════════════════════════════════════
        // MÁQUINA DE ESTADOS FINITOS (FSM)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Transiciona de manera segura hacia un nuevo estado en la FSM.
        /// Invoca Exit() en el estado saliente y Enter() en el entrante.
        /// </summary>
        /// <param name="newState">Instancia de estado que implementa IEnemyState.</param>
        public void ChangeState(IEnemyState newState)
        {
            if (newState == null || _currentState == newState) return;

            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();

            OnStateChanged?.Invoke(_currentState);
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE NAVEGACIÓN Y OBJETIVO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cachea la referencia del jugador sin realizar búsquedas por string en Update().
        /// Prioriza el Singleton PlayerController.Instance.
        /// </summary>
        private void InicializarObjetivoJugador()
        {
            if (PlayerController.Instance != null)
            {
                _playerTransform = PlayerController.Instance.transform;
                _playerDamageable = PlayerController.Instance.GetComponentInParent<IDamageable>();
            }
            else
            {
                // Búsqueda de rescate segura ejecutada una sola vez en Start()
                var playerController = FindAnyObjectByType<PlayerController>();
                if (playerController != null)
                {
                    _playerTransform = playerController.transform;
                    _playerDamageable = playerController.GetComponentInParent<IDamageable>();
                }
            }
        }

        /// <summary>
        /// Configura el destino en el NavMeshAgent solo si la posición es válida y está activo.
        /// </summary>
        public bool SetAgentDestination(Vector3 destination)
        {
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                return _agent.SetDestination(destination);
            }
            return false;
        }

        /// <summary>
        /// Detiene el movimiento del agente en el NavMesh de forma limpia.
        /// </summary>
        public void StopAgent()
        {
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Reanuda el movimiento del agente en el NavMesh.
        /// </summary>
        public void ResumeAgent()
        {
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
            }
        }

        /// <summary>
        /// Rota al enemigo suavemente hacia una posición dada en el plano horizontal (eje Y).
        /// </summary>
        public void RotateTowards(Vector3 targetPosition, float rotationSpeed)
        {
            Vector3 direction = (targetPosition - transform.position);
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
            }
        }

        /// <summary>
        /// Retorna la distancia euclidiana al jugador en metros.
        /// Retorna float.MaxValue si el jugador no existe.
        /// </summary>
        public float GetDistanceToPlayer()
        {
            if (_playerTransform == null) return float.MaxValue;
            return Vector3.Distance(transform.position, _playerTransform.position);
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS DE FÍSICA ZERO-ALLOCATION (BUFFERS NON-ALLOC)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Realiza un SphereCast sin asignar memoria en el Garbage Collector.
        /// El resultado se deposita en el buffer interno _sphereCastBuffer.
        /// </summary>
        /// <param name="origin">Punto de inicio del barrido.</param>
        /// <param name="radius">Radio de la esfera.</param>
        /// <param name="direction">Dirección normalizada del barrido.</param>
        /// <param name="distance">Distancia máxima del barrido.</param>
        /// <param name="layerMask">Máscara de capas a evaluar.</param>
        /// <returns>Cantidad de impactos detectados.</returns>
        public int PerformSphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, float distance, LayerMask layerMask)
        {
            return Physics.SphereCastNonAlloc(origin, radius, direction, _sphereCastBuffer, distance, layerMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Obtiene el impacto del buffer de SphereCast por índice.
        /// </summary>
        public RaycastHit GetSphereCastHit(int index)
        {
            return _sphereCastBuffer[index];
        }

        /// <summary>
        /// Realiza un OverlapSphere sin asignar memoria en el Garbage Collector.
        /// El resultado se deposita en el buffer interno _overlapBuffer.
        /// </summary>
        /// <param name="center">Centro del radio de evaluación.</param>
        /// <param name="radius">Radio de la esfera.</param>
        /// <param name="layerMask">Máscara de capas a evaluar.</param>
        /// <returns>Cantidad de colisionadores detectados.</returns>
        public int PerformOverlapSphereNonAlloc(Vector3 center, float radius, LayerMask layerMask)
        {
            return Physics.OverlapSphereNonAlloc(center, radius, _overlapBuffer, layerMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Obtiene el colisionador del buffer de OverlapSphere por índice.
        /// </summary>
        public Collider GetOverlapHit(int index)
        {
            return _overlapBuffer[index];
        }

        /// <summary>
        /// Verifica línea de visión directa mediante RaycastNonAlloc filtrando por entorno.
        /// </summary>
        /// <param name="origin">Origen de los ojos del enemigo.</param>
        /// <param name="target">Posición del objetivo a evaluar.</param>
        /// <returns>True si no hay obstáculos intermedios en la capa de entorno.</returns>
        public bool CheckLineOfSight(Vector3 origin, Vector3 target)
        {
            Vector3 direction = (target - origin);
            float distance = direction.magnitude;

            if (distance < 0.001f) return true;

            int hits = Physics.RaycastNonAlloc(origin, direction.normalized, _raycastBuffer, distance, _environmentLayer, QueryTriggerInteraction.Ignore);
            return hits == 0;
        }

        // ════════════════════════════════════════════════════════════════════
        // COMBATE (IDamageable, IKnockbackable)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Aplica daño al enemigo respetando invulnerabilidades y disparando eventos.
        /// </summary>
        public float TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return 0f;

            _currentHealth = Mathf.Max(0f, _currentHealth - info.Amount);
            OnDamageTaken?.Invoke(info);

            if (_currentHealth <= 0f)
            {
                Die();
            }

            return info.Amount;
        }

        /// <summary>
        /// Aplica una fuerza física o desplazamiento de impacto sobre el NavMeshAgent.
        /// </summary>
        public void ApplyKnockback(Vector3 force)
        {
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                if (_enemyData == null || !_enemyData.ImmuneToKnockback)
                {
                    _agent.Move(force * Time.deltaTime);
                }
            }
        }

        /// <summary>
        /// Gestiona la muerte del enemigo, apagando componentes y notificando al Event Bus.
        /// </summary>
        protected virtual void Die()
        {
            StopAgent();
            if (_agent != null) _agent.enabled = false;
            if (_capsuleCollider != null) _capsuleCollider.enabled = false;

            OnDeath?.Invoke();

            string enemyId = _enemyData != null ? _enemyData.EnemyID : "Enemy_Default";
            GameEvents.FireEnemyKilled(enemyId);

            Destroy(gameObject, 0.25f);
        }

        // ════════════════════════════════════════════════════════════════════
        // INICIALIZACIÓN Y CONFIGURACIÓN
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Inicializa los parámetros de vida, velocidad y multiplicadores de combate.
        /// </summary>
        private void InicializarEstadisticas()
        {
            _maxHealth = _enemyData != null ? _enemyData.BaseHP : 50f;
            _currentHealth = _maxHealth;

            if (_agent != null)
            {
                _agent.speed = (_enemyData != null ? _enemyData.MoveSpeed : 3.5f) * _speedMultiplier;
                _agent.stoppingDistance = _enemyData != null ? _enemyData.AttackRange * 0.8f : 1.5f;
            }

            _isInitialized = true;
        }

        /// <summary>
        /// Configura dimensiones precisas de cápsula para evitar trabas en puertas o techos.
        /// </summary>
        private void ConfigurarColisionador()
        {
            if (_capsuleCollider != null)
            {
                _capsuleCollider.height = _modelHeight;
                _capsuleCollider.radius = _modelRadius;
                _capsuleCollider.center = new Vector3(0f, _modelHeight * 0.5f, 0f);
            }

            if (_agent != null)
            {
                _agent.height = _modelHeight;
                _agent.radius = _modelRadius;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DEPURACIÓN VISUAL EN EDITOR
        // ════════════════════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (_enemyData == null) return;

            // Rango de detección
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _enemyData.DetectionRange);

            // Rango de ataque
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _enemyData.AttackRange);
        }
    }
}
