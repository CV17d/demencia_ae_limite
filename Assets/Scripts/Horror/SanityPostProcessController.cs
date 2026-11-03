// ============================================================================
// SanityPostProcessController.cs — Controlador Dinámico de Post-Procesado URP
// ============================================================================
// Conecta el estado de cordura/culpa del jugador con un Volume de URP para
// orquestar de manera progresiva y fluida la degradación visual de terror:
// - Aberración cromática (distorsión espectral periférica)
// - Viñeteado claustrofóbico (oscurecimiento y tinte carmesí)
// - Grano de película (ruido analógico sucio e industrial)
// - Distorsión de lente (deformación de perspectiva y claustrofobia)
// - Ajustes de color (desaturación cadavérica y contraste abrasivo)
// Diseñado con arquitectura Zero-GC y transiciones no lineales por umbrales.
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DemenciaNoLimite.Horror
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Volume))]
    public class SanityPostProcessController : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // ESTRUCTURAS DE DATOS DE POST-PROCESADO POR UMBRALES
        // ════════════════════════════════════════════════════════════════════

        [System.Serializable]
        public struct PostProcessTierSettings
        {
            [Header("Aberración Cromática")]
            [Range(0f, 1f)] public float chromaticAberrationIntensity;

            [Header("Viñeteado")]
            [Range(0f, 1f)] public float vignetteIntensity;
            [Range(0f, 1f)] public float vignetteSmoothness;
            public Color vignetteColor;

            [Header("Grano de Película")]
            [Range(0f, 1f)] public float filmGrainIntensity;

            [Header("Distorsión de Lente")]
            [Range(-1f, 1f)] public float lensDistortionIntensity;

            [Header("Ajustes de Color")]
            [Range(-100f, 100f)] public float saturation;
            [Range(-100f, 100f)] public float contrast;
            public Color colorFilter;

            public static PostProcessTierSettings CreateNormalDefault()
            {
                return new PostProcessTierSettings
                {
                    chromaticAberrationIntensity = 0.05f,
                    vignetteIntensity = 0.20f,
                    vignetteSmoothness = 0.25f,
                    vignetteColor = new Color(0.02f, 0.02f, 0.02f, 1f),
                    filmGrainIntensity = 0.10f,
                    lensDistortionIntensity = 0.0f,
                    saturation = 0.0f,
                    contrast = 0.0f,
                    colorFilter = Color.white
                };
            }

            public static PostProcessTierSettings CreateModerateDefault()
            {
                return new PostProcessTierSettings
                {
                    chromaticAberrationIntensity = 0.35f,
                    vignetteIntensity = 0.45f,
                    vignetteSmoothness = 0.35f,
                    vignetteColor = new Color(0.12f, 0.03f, 0.03f, 1f),
                    filmGrainIntensity = 0.45f,
                    lensDistortionIntensity = -0.15f,
                    saturation = -25.0f,
                    contrast = 15.0f,
                    colorFilter = new Color(0.92f, 0.88f, 0.88f, 1f)
                };
            }

            public static PostProcessTierSettings CreateCriticalDefault()
            {
                return new PostProcessTierSettings
                {
                    chromaticAberrationIntensity = 0.85f,
                    vignetteIntensity = 0.65f,
                    vignetteSmoothness = 0.55f,
                    vignetteColor = new Color(0.28f, 0.02f, 0.02f, 1f),
                    filmGrainIntensity = 0.85f,
                    lensDistortionIntensity = -0.38f,
                    saturation = -65.0f,
                    contrast = 38.0f,
                    colorFilter = new Color(0.85f, 0.65f, 0.65f, 1f)
                };
            }

            public static PostProcessTierSettings Lerp(in PostProcessTierSettings a, in PostProcessTierSettings b, float t)
            {
                t = Mathf.Clamp01(t);
                return new PostProcessTierSettings
                {
                    chromaticAberrationIntensity = Mathf.Lerp(a.chromaticAberrationIntensity, b.chromaticAberrationIntensity, t),
                    vignetteIntensity = Mathf.Lerp(a.vignetteIntensity, b.vignetteIntensity, t),
                    vignetteSmoothness = Mathf.Lerp(a.vignetteSmoothness, b.vignetteSmoothness, t),
                    vignetteColor = Color.Lerp(a.vignetteColor, b.vignetteColor, t),
                    filmGrainIntensity = Mathf.Lerp(a.filmGrainIntensity, b.filmGrainIntensity, t),
                    lensDistortionIntensity = Mathf.Lerp(a.lensDistortionIntensity, b.lensDistortionIntensity, t),
                    saturation = Mathf.Lerp(a.saturation, b.saturation, t),
                    contrast = Mathf.Lerp(a.contrast, b.contrast, t),
                    colorFilter = Color.Lerp(a.colorFilter, b.colorFilter, t)
                };
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN SERIALIZADA EN INSPECTOR
        // ════════════════════════════════════════════════════════════════════

        [Header("Referencia al Volumen URP")]
        [Tooltip("Volumen URP que aloja el perfil con los overrides. Si no se asigna, se toma automáticamente del GameObject.")]
        [SerializeField] private Volume _targetVolume;

        [Header("Umbrales de Cordura (0.0 = Colapso / 1.0 = Lúcido)")]
        [Tooltip("Nivel de cordura a partir del cual se inician efectos moderados (ej. 0.50 = 50% de cordura).")]
        [Range(0.01f, 0.99f)]
        [SerializeField] private float _moderateSanityThreshold = 0.50f;

        [Tooltip("Nivel de cordura a partir del cual el jugador entra en colapso crítico (ej. 0.25 = 25% de cordura).")]
        [Range(0.01f, 0.99f)]
        [SerializeField] private float _criticalSanityThreshold = 0.25f;

        [Header("Velocidad de Interpolación (Smoothing)")]
        [Tooltip("Velocidad de degradación al perder cordura (transición más rápida ante el horror).")]
        [SerializeField] private float _deteriorationSpeed = 2.8f;

        [Tooltip("Velocidad de recuperación al sanar cordura (transición más lenta y pausada).")]
        [SerializeField] private float _recoverySpeed = 1.2f;

        [Tooltip("Velocidad de desvanecimiento de pulsos de estrés o sobresaltos.")]
        [SerializeField] private float _stressPulseDecay = 2.5f;

        [Header("Perfiles de Post-Procesado por Umbral")]
        [Tooltip("Efectos con cordura alta (estado lúcido/calma).")]
        [SerializeField] private PostProcessTierSettings _normalSettings = PostProcessTierSettings.CreateNormalDefault();

        [Tooltip("Efectos al cruzar el umbral moderado de locura.")]
        [SerializeField] private PostProcessTierSettings _moderateSettings = PostProcessTierSettings.CreateModerateDefault();

        [Tooltip("Efectos en estado crítico de colapso psicológico.")]
        [SerializeField] private PostProcessTierSettings _criticalSettings = PostProcessTierSettings.CreateCriticalDefault();

        // ════════════════════════════════════════════════════════════════════
        // ESTADO DE CORDURA Y MONITOREO
        // ════════════════════════════════════════════════════════════════════

        [Header("Monitoreo en Tiempo Real (Solo Lectura)")]
        [Tooltip("Nivel de cordura actual percibido (1.0 = lúcido, 0.0 = locura total).")]
        [Range(0f, 1f)]
        [SerializeField] private float _currentSanity = 1.0f;

        [Tooltip("Nivel objetivo de cordura hacia el cual interpola el sistema.")]
        [Range(0f, 1f)]
        [SerializeField] private float _targetSanity = 1.0f;

        [Tooltip("Intensidad de pulso temporal por estrés/sustos.")]
        [SerializeField] private float _stressPulseIntensity = 0f;

        // ════════════════════════════════════════════════════════════════════
        // CACHÉ ZERO-GC DE OVERRIDES URP
        // ════════════════════════════════════════════════════════════════════

        private VolumeProfile _activeProfile;
        private ChromaticAberration _chromaticAberration;
        private Vignette _vignette;
        private FilmGrain _filmGrain;
        private LensDistortion _lensDistortion;
        private ColorAdjustments _colorAdjustments;

        // Valores numéricos locales cacheados para interpolación sin boxing
        private float _currentCAIntensity;
        private float _currentVigIntensity;
        private float _currentVigSmoothness;
        private Color _currentVigColor;
        private float _currentGrainIntensity;
        private float _currentDistortionIntensity;
        private float _currentSaturation;
        private float _currentContrast;
        private Color _currentColorFilter;

        private bool _isInitialized = false;

        // Propiedades públicas para lectura externa
        public float CurrentSanity => _currentSanity;
        public float TargetSanity => _targetSanity;
        public float ModerateThreshold => _moderateSanityThreshold;
        public float CriticalThreshold => _criticalSanityThreshold;
        public Volume TargetVolume => _targetVolume;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR (AWAKE / INITIALIZATION)
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            InitializeOverrides();
        }

        /// <summary>
        /// Localiza el Volume, instancia/enlaza el perfil de manera segura y cachea
        /// las referencias a los overrides URP para garantizar 0 GC en el ciclo de render.
        /// </summary>
        public void InitializeOverrides()
        {
            if (_isInitialized) return;

            if (_targetVolume == null)
            {
                _targetVolume = GetComponent<Volume>();
            }

            if (_targetVolume == null)
            {
                Debug.LogError("[SanityPostProcessController] No se encontró un componente Volume en el GameObject ni se asignó en el Inspector.");
                return;
            }

            // Acceder a .profile clona el perfil en memoria para la sesión activa,
            // evitando mutar accidentalmente los archivos de assets (.asset) en el proyecto.
            _activeProfile = _targetVolume.profile != null ? _targetVolume.profile : _targetVolume.sharedProfile;
            if (_activeProfile == null)
            {
                Debug.LogWarning("[SanityPostProcessController] El Volume no tiene un VolumeProfile asignado. Creando uno en memoria.");
                _activeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _targetVolume.profile = _activeProfile;
            }

            // Cachear y activar overrides URP requeridos
            CacheAndEnableOverride(ref _chromaticAberration);
            CacheAndEnableOverride(ref _vignette);
            CacheAndEnableOverride(ref _filmGrain);
            CacheAndEnableOverride(ref _lensDistortion);
            CacheAndEnableOverride(ref _colorAdjustments);

            // Asegurar que las variables de control tengan activo su 'overrideState'
            if (_chromaticAberration != null)
            {
                _chromaticAberration.active = true;
                _chromaticAberration.intensity.overrideState = true;
            }

            if (_vignette != null)
            {
                _vignette.active = true;
                _vignette.intensity.overrideState = true;
                _vignette.smoothness.overrideState = true;
                _vignette.color.overrideState = true;
            }

            if (_filmGrain != null)
            {
                _filmGrain.active = true;
                _filmGrain.intensity.overrideState = true;
            }

            if (_lensDistortion != null)
            {
                _lensDistortion.active = true;
                _lensDistortion.intensity.overrideState = true;
            }

            if (_colorAdjustments != null)
            {
                _colorAdjustments.active = true;
                _colorAdjustments.saturation.overrideState = true;
                _colorAdjustments.contrast.overrideState = true;
                _colorAdjustments.colorFilter.overrideState = true;
            }

            // Inicializar valores con el estado normal
            ApplyInstantSettings(_normalSettings);
            _isInitialized = true;
        }

        private void CacheAndEnableOverride<T>(ref T component) where T : VolumeComponent
        {
            if (!_activeProfile.TryGet(out component))
            {
                // Si el perfil no contiene el componente, lo añadimos de manera no destructiva
                component = _activeProfile.Add<T>(false);
            }

            if (component != null)
            {
                component.active = true;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // BUCLE DE ACTUALIZACIÓN ZERO-GC (INTERPOLACIÓN DINÁMICA)
        // ════════════════════════════════════════════════════════════════════

        private void Update()
        {
            if (!_isInitialized) return;

            float dt = Time.deltaTime;

            // 1. Interpolación de la cordura observada hacia el objetivo
            float lerpSpeed = (_targetSanity < _currentSanity) ? _deteriorationSpeed : _recoverySpeed;
            _currentSanity = Mathf.MoveTowards(_currentSanity, _targetSanity, dt * lerpSpeed);

            // 2. Desvanecimiento de pulsos de estrés
            if (_stressPulseIntensity > 0f)
            {
                _stressPulseIntensity = Mathf.MoveTowards(_stressPulseIntensity, 0f, dt * _stressPulseDecay);
            }

            // 3. Cálculo de la configuración meta basada en los umbrales de cordura
            ComputeTargetSettings(_currentSanity, out PostProcessTierSettings target);

            // 4. Inyección no lineal del pulso de estrés temporal
            if (_stressPulseIntensity > 0.001f)
            {
                target.chromaticAberrationIntensity = Mathf.Clamp01(target.chromaticAberrationIntensity + _stressPulseIntensity * 0.45f);
                target.lensDistortionIntensity = Mathf.Clamp(target.lensDistortionIntensity - _stressPulseIntensity * 0.25f, -1f, 1f);
                target.vignetteIntensity = Mathf.Clamp01(target.vignetteIntensity + _stressPulseIntensity * 0.20f);
            }

            // 5. Suavizado numérico Zero-GC hacia los valores objetivos
            float step = dt * lerpSpeed * 1.5f;
            _currentCAIntensity = Mathf.MoveTowards(_currentCAIntensity, target.chromaticAberrationIntensity, step);
            _currentVigIntensity = Mathf.MoveTowards(_currentVigIntensity, target.vignetteIntensity, step);
            _currentVigSmoothness = Mathf.MoveTowards(_currentVigSmoothness, target.vignetteSmoothness, step);
            _currentVigColor = Color.Lerp(_currentVigColor, target.vignetteColor, step);
            _currentGrainIntensity = Mathf.MoveTowards(_currentGrainIntensity, target.filmGrainIntensity, step);
            _currentDistortionIntensity = Mathf.MoveTowards(_currentDistortionIntensity, target.lensDistortionIntensity, step);
            _currentSaturation = Mathf.MoveTowards(_currentSaturation, target.saturation, step * 40f);
            _currentContrast = Mathf.MoveTowards(_currentContrast, target.contrast, step * 35f);
            _currentColorFilter = Color.Lerp(_currentColorFilter, target.colorFilter, step);

            // 6. Aplicación directa a los overrides URP sin allocs ni clonaciones
            if (_chromaticAberration != null)
            {
                _chromaticAberration.intensity.value = _currentCAIntensity;
            }

            if (_vignette != null)
            {
                _vignette.intensity.value = _currentVigIntensity;
                _vignette.smoothness.value = _currentVigSmoothness;
                _vignette.color.value = _currentVigColor;
            }

            if (_filmGrain != null)
            {
                _filmGrain.intensity.value = _currentGrainIntensity;
            }

            if (_lensDistortion != null)
            {
                _lensDistortion.intensity.value = _currentDistortionIntensity;
            }

            if (_colorAdjustments != null)
            {
                _colorAdjustments.saturation.value = _currentSaturation;
                _colorAdjustments.contrast.value = _currentContrast;
                _colorAdjustments.colorFilter.value = _currentColorFilter;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MATEMÁTICA DE UMBRALES Y PROGRESIÓN
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Evalúa la posición de la cordura relativa a los umbrales moderado y crítico
        /// para interpolar de manera suave y continua los parámetros de post-procesamiento.
        /// </summary>
        public void ComputeTargetSettings(float sanity, out PostProcessTierSettings result)
        {
            sanity = Mathf.Clamp01(sanity);

            if (sanity >= _moderateSanityThreshold)
            {
                // Zona 1: Lúcido (1.0) hacia Umbral Moderado (ej. 0.50)
                float t = Mathf.InverseLerp(1.0f, _moderateSanityThreshold, sanity);
                result = PostProcessTierSettings.Lerp(_normalSettings, _moderateSettings, t);
            }
            else if (sanity >= _criticalSanityThreshold)
            {
                // Zona 2: Umbral Moderado hacia Umbral Crítico (ej. 0.50 a 0.25)
                float t = Mathf.InverseLerp(_moderateSanityThreshold, _criticalSanityThreshold, sanity);
                result = PostProcessTierSettings.Lerp(_moderateSettings, _criticalSettings, t);
            }
            else
            {
                // Zona 3: Estado Crítico a Colapso Mental Total (0.25 a 0.0)
                float t = Mathf.InverseLerp(_criticalSanityThreshold, 0f, sanity);
                // Intensificación extrema más allá del umbral crítico
                PostProcessTierSettings extremeSettings = _criticalSettings;
                extremeSettings.chromaticAberrationIntensity = Mathf.Min(1.0f, _criticalSettings.chromaticAberrationIntensity + 0.15f);
                extremeSettings.vignetteIntensity = Mathf.Min(0.85f, _criticalSettings.vignetteIntensity + 0.15f);
                extremeSettings.filmGrainIntensity = Mathf.Min(1.0f, _criticalSettings.filmGrainIntensity + 0.15f);
                extremeSettings.lensDistortionIntensity = Mathf.Max(-0.65f, _criticalSettings.lensDistortionIntensity - 0.20f);
                extremeSettings.saturation = Mathf.Max(-100f, _criticalSettings.saturation - 25f);
                extremeSettings.contrast = Mathf.Min(60f, _criticalSettings.contrast + 15f);

                result = PostProcessTierSettings.Lerp(_criticalSettings, extremeSettings, t);
            }
        }

        private void ApplyInstantSettings(in PostProcessTierSettings s)
        {
            _currentCAIntensity = s.chromaticAberrationIntensity;
            _currentVigIntensity = s.vignetteIntensity;
            _currentVigSmoothness = s.vignetteSmoothness;
            _currentVigColor = s.vignetteColor;
            _currentGrainIntensity = s.filmGrainIntensity;
            _currentDistortionIntensity = s.lensDistortionIntensity;
            _currentSaturation = s.saturation;
            _currentContrast = s.contrast;
            _currentColorFilter = s.colorFilter;

            if (_chromaticAberration != null) _chromaticAberration.intensity.value = _currentCAIntensity;
            if (_vignette != null)
            {
                _vignette.intensity.value = _currentVigIntensity;
                _vignette.smoothness.value = _currentVigSmoothness;
                _vignette.color.value = _currentVigColor;
            }
            if (_filmGrain != null) _filmGrain.intensity.value = _currentGrainIntensity;
            if (_lensDistortion != null) _lensDistortion.intensity.value = _currentDistortionIntensity;
            if (_colorAdjustments != null)
            {
                _colorAdjustments.saturation.value = _currentSaturation;
                _colorAdjustments.contrast.value = _currentContrast;
                _colorAdjustments.colorFilter.value = _currentColorFilter;
            }
        }

        /// <summary>
        /// Dispara un pico temporal de distorsión y aberración por sobresalto o daño súbito.
        /// </summary>
        /// <param name="intensity">Fuerza del pulso (0.0 a 1.0).</param>
        public void TriggerStressPulse(float intensity)
        {
            _stressPulseIntensity = Mathf.Clamp01(_stressPulseIntensity + intensity);
        }
    }
}
