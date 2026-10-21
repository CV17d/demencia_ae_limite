// ============================================================================
// SensorySystem.cs — Módulo Sensorial de Percepción y Sigilo (Ruta Pacifista)
// ============================================================================
// Gestiona la detección visual (cono de visión mediante Vector3.Dot) y auditiva
// (suscripción a GameEvents.OnNoiseMade) de los enemigos.
//
// Optimizaciones Críticas:
// - Cero Garbage Collection en la evaluación periódica de visión.
// - RaycastNonAlloc con buffer preasignado para validar obstrucciones de visión.
// - Desacoplamiento total de la audición mediante el bus de eventos de C#.
// ============================================================================

using System;
using UnityEngine;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Enemies.FSM;

namespace DemenciaNoLimite.Enemies.Sensors
{
    [RequireComponent(typeof(EnemyBrain))]
    public class SensorySystem : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // PARÁMETROS SERIALIZADOS
        // ════════════════════════════════════════════════════════════════════

        [Header("Configuración de Visión")]
        [Tooltip("Ángulo de corte para el cono de visión (60 grados).")]
        [SerializeField] private float _fieldOfViewAngle = 60f;

        [Tooltip("Rango de visión por defecto si EnemyData no especifica DetectionRange.")]
        [SerializeField] private float _defaultVisionRange = 15f;

        [Tooltip("Altura de los ojos del enemigo respecto a su base.")]
        [SerializeField] private float _eyeHeightOffset = 1.6f;

        [Tooltip("Altura objetivo del jugador a la que apunta la línea de visión (tórax).")]
        [SerializeField] private float _targetHeightOffset = 1.0f;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y BUFFERS
        // ════════════════════════════════════════════════════════════════════

        private EnemyBrain _brain;
        private readonly RaycastHit[] _visionRayBuffer = new RaycastHit[2];
        private float _cosFovThreshold;

        // Evento opcional para notificar a otros observadores cuando el jugador entra al campo visual
        public event Action OnPlayerSighted;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _brain = GetComponent<EnemyBrain>();

            // Precalculamos el coseno del ángulo de visión para evitar recalcularlo en cada frame
            _cosFovThreshold = Mathf.Cos(_fieldOfViewAngle * Mathf.Deg2Rad);
        }

        private void OnEnable()
        {
            GameEvents.OnNoiseMade += HandleNoiseMade;
        }

        private void OnDisable()
        {
            GameEvents.OnNoiseMade -= HandleNoiseMade;
        }

        // ════════════════════════════════════════════════════════════════════
        // SISTEMA DE VISIÓN (CONO VISUAL VECTOR3.DOT)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Evalúa si el jugador se encuentra dentro del cono visual y sin paredes intermedias.
        /// Utiliza el producto escalar (Vector3.Dot) y un RaycastNonAlloc para evitar allocations.
        /// </summary>
        /// <returns>True si el jugador es visible directamente.</returns>
        public bool CheckVision()
        {
            if (_brain == null || _brain.PlayerTransform == null) return false;

            Vector3 eyePosition = transform.position + Vector3.up * _eyeHeightOffset;
            Vector3 targetPosition = _brain.PlayerTransform.position + Vector3.up * _targetHeightOffset;
            Vector3 toPlayer = targetPosition - eyePosition;
            float distanceToPlayer = toPlayer.magnitude;

            float maxRange = _brain.Data != null ? _brain.Data.DetectionRange : _defaultVisionRange;
            if (distanceToPlayer > maxRange) return false;

            // Dirección normalizada calculada directamente sin invocar .normalized redundante
            Vector3 directionToPlayer = toPlayer / distanceToPlayer;

            // Comparación de ángulo mediante producto escalar (Vector3.Dot)
            float dotProduct = Vector3.Dot(transform.forward, directionToPlayer);

            if (dotProduct >= _cosFovThreshold)
            {
                // El jugador está dentro del cono de 60°.
                // Verificamos si hay colisionadores de entorno bloqueando la visión (RaycastNonAlloc)
                int hitCount = Physics.RaycastNonAlloc(
                    eyePosition,
                    directionToPlayer,
                    _visionRayBuffer,
                    distanceToPlayer,
                    _brain.EnvironmentLayer,
                    QueryTriggerInteraction.Ignore
                );

                if (hitCount == 0)
                {
                    OnPlayerSighted?.Invoke();
                    return true;
                }
            }

            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // SISTEMA DE AUDICIÓN (EVENT-DRIVEN SUSCRIPCIÓN)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Callback ejecutado cuando el jugador u otra fuente emite un ruido en el mapa.
        /// </summary>
        /// <param name="noisePosition">Ubicación tridimensional de la fuente sonora.</param>
        /// <param name="noiseRadius">Radio acústico de alcance del ruido.</param>
        private void HandleNoiseMade(Vector3 noisePosition, float noiseRadius)
        {
            if (_brain == null || !_brain.IsAlive) return;

            // Si el enemigo ya está combatiendo activamente, prioriza el combate actual
            if (_brain.CurrentState == _brain.ChaseStateInstance ||
                _brain.CurrentState == _brain.MeleeAttackStateInstance ||
                _brain.CurrentState == _brain.RangedAttackStateInstance)
            {
                return;
            }

            float distanceToNoise = Vector3.Distance(transform.position, noisePosition);

            if (distanceToNoise <= noiseRadius)
            {
                // Notificar al controlador central para investigar la coordenada del sonido
                _brain.AlertInvestigation(noisePosition);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GIZMOS DEPURACIÓN
        // ════════════════════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            Vector3 eyePos = transform.position + Vector3.up * _eyeHeightOffset;
            float maxRange = _brain != null && _brain.Data != null ? _brain.Data.DetectionRange : _defaultVisionRange;

            // Dibujar límites del cono visual (60 grados)
            Gizmos.color = Color.cyan;
            Quaternion leftRayRotation = Quaternion.AngleAxis(-_fieldOfViewAngle, Vector3.up);
            Quaternion rightRayRotation = Quaternion.AngleAxis(_fieldOfViewAngle, Vector3.up);

            Vector3 leftRayDirection = leftRayRotation * transform.forward;
            Vector3 rightRayDirection = rightRayRotation * transform.forward;

            Gizmos.DrawRay(eyePos, leftRayDirection * maxRange);
            Gizmos.DrawRay(eyePos, rightRayDirection * maxRange);
            Gizmos.DrawRay(eyePos, transform.forward * maxRange);
        }
    }
}
