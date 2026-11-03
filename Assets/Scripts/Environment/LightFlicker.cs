// ============================================================================
// LightFlicker.cs — Parpadeo Atmosférico de Iluminación (Sector Cero)
// ============================================================================
// Simula el fallo eléctrico, fluctuaciones de voltaje y chisporroteo de bombillas
// y tubos fluorescentes en los pasillos industriales de "Sector Cero".
// Diseñado para generar tensión e imprevisibilidad psicológica en Survival Horror.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Environment
{
    [RequireComponent(typeof(Light))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Environment/Light Flicker")]
    public class LightFlicker : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // PARÁMETROS SERIALIZADOS (INSPECTOR)
        // ════════════════════════════════════════════════════════════════════

        [Header("Rango de Intensidad Lumínica")]
        [Tooltip("Intensidad mínima alcanzada durante caídas de voltaje o apagones breves.")]
        [SerializeField] private float minIntensity = 0.1f;

        [Tooltip("Intensidad máxima alcanzada durante picos de voltaje o iluminación estable.")]
        [SerializeField] private float maxIntensity = 1.8f;

        [Header("Frecuencia y Temporización (Segundos)")]
        [Tooltip("Tiempo de espera mínimo entre cada cambio aleatorio de intensidad (chisporroteo rápido).")]
        [SerializeField] private float minWaitTime = 0.03f;

        [Tooltip("Tiempo de espera máximo entre cada cambio aleatorio de intensidad (pausa o estabilidad breve).")]
        [SerializeField] private float maxWaitTime = 0.25f;

        [Header("Efectos Adicionales de Survival Horror (Opcionales)")]
        [Tooltip("Probabilidad (0 a 1) de que el cambio apague la luz a cero absoluto por un instante.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float blackoutChance = 0.08f;

        [Tooltip("Renderer opcional de la lámpara/tubo para sincronizar la emisión de luz con la bombilla física.")]
        [SerializeField] private Renderer lampMeshRenderer;

        [Tooltip("Nombre de la propiedad de color de emisión en el shader (por defecto '_EmissionColor' en URP/Standard).")]
        [SerializeField] private string emissionColorProperty = "_EmissionColor";

        [Tooltip("AudioSource opcional para reproducir chasquidos o arcos eléctricos al parpadear.")]
        [SerializeField] private AudioSource electricalBuzzAudio;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y CACHÉ
        // ════════════════════════════════════════════════════════════════════

        private Light _lightSource;
        private float _timer;
        private MaterialPropertyBlock _propBlock;
        private int _emissionColorId;
        private Color _baseEmissionColor = Color.white;
        private bool _hasEmissiveRenderer;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public Light LightSource => _lightSource;
        public float CurrentIntensity => _lightSource != null ? _lightSource.intensity : 0f;
        public bool IsActiveFlickering { get; set; } = true;

        public float MinIntensity { get => minIntensity; set => minIntensity = Mathf.Max(0f, value); }
        public float MaxIntensity { get => maxIntensity; set => maxIntensity = Mathf.Max(0f, value); }
        public float MinWaitTime { get => minWaitTime; set => minWaitTime = Mathf.Max(0.001f, value); }
        public float MaxWaitTime { get => maxWaitTime; set => maxWaitTime = Mathf.Max(0.001f, value); }
        public float BlackoutChance { get => blackoutChance; set => blackoutChance = Mathf.Clamp01(value); }

        /// <summary>
        /// Configura dinámicamente los parámetros de parpadeo (utilizado por SanityLightReactor o directores de eventos).
        /// </summary>
        public void SetDynamicParameters(float newMinInt, float newMaxInt, float newMinWait, float newMaxWait, float newBlackout)
        {
            minIntensity = Mathf.Max(0f, newMinInt);
            maxIntensity = Mathf.Max(minIntensity, newMaxInt);
            minWaitTime = Mathf.Max(0.001f, newMinWait);
            maxWaitTime = Mathf.Max(minWaitTime, newMaxWait);
            blackoutChance = Mathf.Clamp01(newBlackout);
        }

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            // Caché del componente de luz requerido
            _lightSource = GetComponent<Light>();

            // Configuración opcional de MaterialPropertyBlock para evitar instanciar materiales
            if (lampMeshRenderer != null)
            {
                _propBlock = new MaterialPropertyBlock();
                _emissionColorId = Shader.PropertyToID(emissionColorProperty);

                if (lampMeshRenderer.sharedMaterial != null && lampMeshRenderer.sharedMaterial.HasProperty(_emissionColorId))
                {
                    _baseEmissionColor = lampMeshRenderer.sharedMaterial.GetColor(_emissionColorId);
                    if (_baseEmissionColor == Color.black)
                    {
                        _baseEmissionColor = _lightSource != null ? _lightSource.color : Color.white;
                    }
                    _hasEmissiveRenderer = true;
                }
            }

            // Inicialización determinista del temporizador
            _timer = Random.Range(minWaitTime, maxWaitTime);
        }

        private void OnEnable()
        {
            _timer = Random.Range(minWaitTime, maxWaitTime);
        }

        private void Update()
        {
            if (!IsActiveFlickering || _lightSource == null) return;

            // Temporizador decreciente
            _timer -= Time.deltaTime;

            if (_timer <= 0f)
            {
                AplicarParpadeoAleatorio();

                // Reinicio del temporizador con intervalo aleatorio
                _timer = Random.Range(minWaitTime, maxWaitTime);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // LÓGICA DE PARPADEO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Aplica una nueva intensidad aleatoria dentro de los límites y sincroniza componentes visuales/sonoros.
        /// </summary>
        private void AplicarParpadeoAleatorio()
        {
            float targetIntensity;

            // Posibilidad de micro-apagón tenso
            if (blackoutChance > 0f && Random.value < blackoutChance)
            {
                targetIntensity = 0f;
            }
            else
            {
                targetIntensity = Random.Range(minIntensity, maxIntensity);
            }

            // Aplicar intensidad en la fuente lumínica
            _lightSource.intensity = targetIntensity;
            Debug.Log("Intensidad actual: " + targetIntensity);

            // Sincronizar emisión en la malla de la lámpara (si existe)
            if (_hasEmissiveRenderer && lampMeshRenderer != null)
            {
                float normalizedFactor = Mathf.InverseLerp(minIntensity, maxIntensity, targetIntensity);
                Color currentEmission = _baseEmissionColor * normalizedFactor;

                lampMeshRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(_emissionColorId, currentEmission);
                lampMeshRenderer.SetPropertyBlock(_propBlock);
            }

            // Sonido de fallo eléctrico aleatorio si se configuró un AudioSource
            if (electricalBuzzAudio != null && targetIntensity <= minIntensity * 1.2f)
            {
                if (!electricalBuzzAudio.isPlaying && Random.value < 0.4f)
                {
                    electricalBuzzAudio.pitch = Random.Range(0.85f, 1.2f);
                    electricalBuzzAudio.Play();
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS PÚBLICOS DE CONTROL EXTERNO (EVENTOS / TRIGGERS)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fuerza un valor de intensidad fijo y detiene o reactiva el parpadeo.
        /// </summary>
        public void SetLightState(bool flickering, float fixedIntensity = -1f)
        {
            IsActiveFlickering = flickering;

            if (_lightSource != null && fixedIntensity >= 0f)
            {
                _lightSource.intensity = fixedIntensity;
            }
        }

        /// <summary>
        /// Fuerza un apagón repentino durante un tiempo determinado antes de reiniciar el parpadeo.
        /// </summary>
        public void TriggerBlackout(float blackoutDuration)
        {
            if (_lightSource != null)
            {
                _lightSource.intensity = 0f;
            }
            _timer = blackoutDuration;
        }

        // ════════════════════════════════════════════════════════════════════
        // VALIDACIÓN Y CONFIGURACIÓN AUTOMÁTICA EN EDITOR
        // ════════════════════════════════════════════════════════════════════

        private void Reset()
        {
            _lightSource = GetComponent<Light>();
            if (_lightSource != null)
            {
                maxIntensity = Mathf.Max(_lightSource.intensity, 1.2f);
                minIntensity = maxIntensity * 0.15f;
            }
            else
            {
                minIntensity = 0.1f;
                maxIntensity = 1.8f;
            }

            minWaitTime = 0.03f;
            maxWaitTime = 0.25f;
            blackoutChance = 0.08f;
        }

        private void OnValidate()
        {
            if (minIntensity < 0f) minIntensity = 0f;
            if (maxIntensity < minIntensity) maxIntensity = minIntensity;

            if (minWaitTime < 0.005f) minWaitTime = 0.005f;
            if (maxWaitTime < minWaitTime) maxWaitTime = minWaitTime;

            blackoutChance = Mathf.Clamp01(blackoutChance);
        }
    }
}

