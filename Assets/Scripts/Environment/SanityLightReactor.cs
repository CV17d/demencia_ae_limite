// ============================================================================
// SanityLightReactor.cs — Reacción Ambiental Lumínica por Cordura / Culpa
// ============================================================================
// Modifica dinámicamente el comportamiento de LightFlicker en base a la cordura
// del jugador gestionada por SanityManager / PlayerManager.
// A menor cordura (mayor culpa / terror):
// - Se incrementa drásticamente la probabilidad de apagones (blackoutChance).
// - El chisporroteo se vuelve ultra-rápido, errático y violento (menor waitTime).
// - Los picos de intensidad lumínica sufren sobrevoltaje y contrastes agresivos.
// Si la cordura se recupera, la iluminación regresa suavemente a sus valores base.
// ============================================================================

using System;
using UnityEngine;
using DemenciaNoLimite.Horror;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Environment
{
    [RequireComponent(typeof(LightFlicker))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Environment/Sanity Light Reactor")]
    public class SanityLightReactor : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // PARÁMETROS SERIALIZADOS (INSPECTOR)
        // ════════════════════════════════════════════════════════════════════

        [Header("Umbrales de Cordura (0.0 = Locura Total, 1.0 = Cordura Plena)")]
        [Tooltip("Umbral de cordura para entrar en estado de agitación eléctrica moderada.")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float moderateSanityThreshold = 0.50f;

        [Tooltip("Umbral de cordura para entrar en pánico eléctrico crítico / colapso mental.")]
        [Range(0.01f, 0.50f)]
        [SerializeField] private float criticalSanityThreshold = 0.25f;

        [Header("Modificadores en Estado Moderado (< 50% Cordura)")]
        [Tooltip("Probabilidad de apagón absoluto en estado moderado.")]
        [Range(0f, 1f)]
        [SerializeField] private float moderateBlackoutChance = 0.22f;

        [Tooltip("Factor multiplicador para acortar los tiempos de espera (menor valor = parpadeo más veloz).")]
        [Range(0.1f, 1f)]
        [SerializeField] private float moderateWaitMultiplier = 0.55f;

        [Tooltip("Multiplicador para los picos de intensidad máxima en estado moderado.")]
        [Range(1f, 3f)]
        [SerializeField] private float moderateIntensityMultiplier = 1.30f;

        [Header("Modificadores en Estado Crítico (< 25% Cordura)")]
        [Tooltip("Probabilidad extrema de apagón en estado crítico.")]
        [Range(0f, 1f)]
        [SerializeField] private float criticalBlackoutChance = 0.50f;

        [Tooltip("Factor multiplicador para acortar tiempos en estado crítico (estroboscópico casi continuo).")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float criticalWaitMultiplier = 0.25f;

        [Tooltip("Multiplicador de sobrevoltaje para picos lumínicos violentos en estado crítico.")]
        [Range(1f, 4f)]
        [SerializeField] private float criticalIntensityMultiplier = 1.75f;

        [Tooltip("Si está activo, fuerza un apagón momentáneo súbito al cruzar hacia el estado crítico.")]
        [SerializeField] private bool triggerJumpscareBlackout = true;

        [Tooltip("Duración en segundos del apagón repentino al entrar en estado crítico.")]
        [SerializeField] private float jumpscareBlackoutDuration = 1.25f;

        [Header("Transición y Suavizado")]
        [Tooltip("Velocidad de interpolación (lerp) para aplicar y restaurar los cambios lumínicos.")]
        [SerializeField] private float transitionSpeed = 3.0f;

        [Header("Referencias Opcionales")]
        [Tooltip("Referencia explícita al SanityManager. Si es nulo, se enlaza automáticamente con SanityManager.Instance.")]
        [SerializeField] private SanityManager sanityManager;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y CACHÉ
        // ════════════════════════════════════════════════════════════════════

        private LightFlicker _flicker;
        private bool _isSubscribed;
        private bool _wasInCriticalState;

        // Valores base capturados de LightFlicker en Awake
        private float _baseMinWait;
        private float _baseMaxWait;
        private float _baseMinInt;
        private float _baseMaxInt;
        private float _baseBlackout;

        // Valores objetivo hacia los cuales se interpola suavemente
        private float _targetMinWait;
        private float _targetMaxWait;
        private float _targetMinInt;
        private float _targetMaxInt;
        private float _targetBlackout;

        // Valores actuales en proceso de interpolación
        private float _currentMinWait;
        private float _currentMaxWait;
        private float _currentMinInt;
        private float _currentMaxInt;
        private float _currentBlackout;

        private float _lastKnownSanity = 1.0f;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public float LastKnownSanity => _lastKnownSanity;
        public bool IsInCriticalState => _lastKnownSanity <= criticalSanityThreshold;
        public bool IsInModerateState => _lastKnownSanity <= moderateSanityThreshold && !IsInCriticalState;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _flicker = GetComponent<LightFlicker>();

            // Almacenar valores base originales de la lámpara
            _baseMinWait = _flicker.MinWaitTime;
            _baseMaxWait = _flicker.MaxWaitTime;
            _baseMinInt = _flicker.MinIntensity;
            _baseMaxInt = _flicker.MaxIntensity;
            _baseBlackout = _flicker.BlackoutChance;

            // Inicializar objetivos y actuales en el estado base
            _targetMinWait = _currentMinWait = _baseMinWait;
            _targetMaxWait = _currentMaxWait = _baseMaxWait;
            _targetMinInt = _currentMinInt = _baseMinInt;
            _targetMaxInt = _currentMaxInt = _baseMaxInt;
            _targetBlackout = _currentBlackout = _baseBlackout;
        }

        private void Start()
        {
            TrySubscribeToSanitySource();
        }

        private void OnEnable()
        {
            TrySubscribeToSanitySource();
        }

        private void OnDisable()
        {
            UnsubscribeFromSanitySource();

            // Restaurar configuración base inmediatamente al desactivar el componente
            if (_flicker != null)
            {
                _flicker.SetDynamicParameters(_baseMinInt, _baseMaxInt, _baseMinWait, _baseMaxWait, _baseBlackout);
            }
        }

        private void Update()
        {
            // Reconexión diferida si el SanityManager fue instanciado tras el inicio
            if (!_isSubscribed)
            {
                TrySubscribeToSanitySource();
            }

            // Interpolación progresiva Zero-GC: solo opera si hay diferencia matemática
            if (Mathf.Abs(_currentMinWait - _targetMinWait) > 0.0001f ||
                Mathf.Abs(_currentMaxWait - _targetMaxWait) > 0.0001f ||
                Mathf.Abs(_currentMaxInt - _targetMaxInt) > 0.001f ||
                Mathf.Abs(_currentBlackout - _targetBlackout) > 0.001f)
            {
                float dt = Time.deltaTime;
                _currentMinWait = Mathf.MoveTowards(_currentMinWait, _targetMinWait, transitionSpeed * 0.05f * dt);
                _currentMaxWait = Mathf.MoveTowards(_currentMaxWait, _targetMaxWait, transitionSpeed * 0.15f * dt);
                _currentMinInt = Mathf.MoveTowards(_currentMinInt, _targetMinInt, transitionSpeed * 0.2f * dt);
                _currentMaxInt = Mathf.MoveTowards(_currentMaxInt, _targetMaxInt, transitionSpeed * _baseMaxInt * 0.5f * dt);
                _currentBlackout = Mathf.MoveTowards(_currentBlackout, _targetBlackout, transitionSpeed * 0.25f * dt);

                _flicker.SetDynamicParameters(_currentMinInt, _currentMaxInt, _currentMinWait, _currentMaxWait, _currentBlackout);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE SUSCRIPCIÓN A EVENTOS DE CORDURA
        // ════════════════════════════════════════════════════════════════════

        private void TrySubscribeToSanitySource()
        {
            if (_isSubscribed) return;

            // Prioridad 1: Referencia directa serializada
            if (sanityManager == null)
            {
                sanityManager = SanityManager.Instance;
            }

            if (sanityManager != null)
            {
                sanityManager.OnGuiltChanged += HandleGuiltChanged;
                _isSubscribed = true;

                // Leer estado actual de cordura inmediatamente
                float initialSanity = 1.0f - sanityManager.InsanityPercentage;
                RecalcularModificadores(initialSanity);
                return;
            }

            // Prioridad 2: PlayerManager global si SanityManager no está directo
            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.OnGuiltUpdated += HandleGuiltChanged;
                _isSubscribed = true;

                float initialSanity = 1.0f - PlayerManager.Instance.InsanityPercentage;
                RecalcularModificadores(initialSanity);
            }
        }

        private void UnsubscribeFromSanitySource()
        {
            if (!_isSubscribed) return;

            if (sanityManager != null)
            {
                sanityManager.OnGuiltChanged -= HandleGuiltChanged;
            }

            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.OnGuiltUpdated -= HandleGuiltChanged;
            }

            _isSubscribed = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // LÓGICA DE REACCIÓN AMBIENTAL
        // ════════════════════════════════════════════════════════════════════

        private void HandleGuiltChanged(int guilt, float normalizedGuilt)
        {
            // Cordura: Inverso de la culpa/locura normalizada (0.0 = locura máxima, 1.0 = lucidez)
            float sanity = Mathf.Clamp01(1.0f - normalizedGuilt);
            RecalcularModificadores(sanity);
        }

        /// <summary>
        /// Recalcula los valores de parpadeo objetivo en función del nivel de cordura del jugador.
        /// </summary>
        public void RecalcularModificadores(float currentSanity)
        {
            _lastKnownSanity = Mathf.Clamp01(currentSanity);

            // ESTADO 1: Cordura Normal / Estable (Mayor a moderateSanityThreshold)
            if (_lastKnownSanity > moderateSanityThreshold)
            {
                _targetMinWait = _baseMinWait;
                _targetMaxWait = _baseMaxWait;
                _targetMinInt = _baseMinInt;
                _targetMaxInt = _baseMaxInt;
                _targetBlackout = _baseBlackout;

                _wasInCriticalState = false;
                return;
            }

            // ESTADO 3: Pánico Crítico / Descomposición Mental (< criticalSanityThreshold)
            if (_lastKnownSanity <= criticalSanityThreshold)
            {
                _targetMinWait = Mathf.Max(0.005f, _baseMinWait * criticalWaitMultiplier);
                _targetMaxWait = Mathf.Max(_targetMinWait + 0.01f, _baseMaxWait * criticalWaitMultiplier);
                _targetMinInt = 0f;
                _targetMaxInt = _baseMaxInt * criticalIntensityMultiplier;
                _targetBlackout = criticalBlackoutChance;

                // Apagón repentino de jumpscare al cruzar la frontera crítica por primera vez
                if (triggerJumpscareBlackout && !_wasInCriticalState && _flicker != null)
                {
                    _flicker.TriggerBlackout(jumpscareBlackoutDuration);
                }

                _wasInCriticalState = true;
                return;
            }

            // ESTADO 2: Agitación Moderada (Entre criticalSanityThreshold y moderateSanityThreshold)
            // Interpolación progresiva proporcional a la caída entre ambos umbrales
            float t = Mathf.InverseLerp(moderateSanityThreshold, criticalSanityThreshold, _lastKnownSanity);

            float currentWaitMul = Mathf.Lerp(moderateWaitMultiplier, criticalWaitMultiplier, t);
            float currentIntMul = Mathf.Lerp(moderateIntensityMultiplier, criticalIntensityMultiplier, t);

            _targetMinWait = Mathf.Max(0.01f, _baseMinWait * currentWaitMul);
            _targetMaxWait = Mathf.Max(_targetMinWait + 0.02f, _baseMaxWait * currentWaitMul);
            _targetMinInt = Mathf.Lerp(_baseMinInt, 0f, t);
            _targetMaxInt = _baseMaxInt * currentIntMul;
            _targetBlackout = Mathf.Lerp(moderateBlackoutChance, criticalBlackoutChance, t);

            _wasInCriticalState = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS DE PRUEBA Y DEPURACIÓN EN INSPECTOR
        // ════════════════════════════════════════════════════════════════════

        [ContextMenu("Simular Cordura 100% (Normal)")]
        private void SimularCorduraNormal() => RecalcularModificadores(1.0f);

        [ContextMenu("Simular Cordura 40% (Moderada)")]
        private void SimularCorduraModerada() => RecalcularModificadores(0.40f);

        [ContextMenu("Simular Cordura 10% (Crítica)")]
        private void SimularCorduraCritica() => RecalcularModificadores(0.10f);

        private void OnValidate()
        {
            if (criticalSanityThreshold > moderateSanityThreshold)
            {
                criticalSanityThreshold = moderateSanityThreshold * 0.5f;
            }
        }
    }
}

