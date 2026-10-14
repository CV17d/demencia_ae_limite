// ============================================================================
// MentalDegradationHook.cs — Post-processing & Camera FOV Hallucination Bridge
// ============================================================================
// Listens to Mental Degradation updates and translates them into shader/post-process
// parameters (Vignette, Chromatic Aberration, Grain, Distortion) and camera FOV warps.
// Ready to bind with URP Volume or custom post-process materials without visual dependencies.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Horror
{
    public class MentalDegradationHook : MonoBehaviour
    {
        [Header("Camera Reference")]
        [SerializeField] private Camera _targetCamera;
        [SerializeField] private float _baseFOV = 75f;

        [Header("Computed Post-Processing Weights (0..1)")]
        [SerializeField] private float _vignetteWeight;
        [SerializeField] private float _chromaticAberrationWeight;
        [SerializeField] private float _filmGrainWeight;
        [SerializeField] private float _lensDistortionWeight;
        [SerializeField] private float _saturationScalar = 1.0f;

        private MentalDegradationSystem _degradationSystem;
        private float _currentDegradation = 0f;

        // Exposed properties for URP Volume / Post-process materials to bind
        public float VignetteWeight => _vignetteWeight;
        public float ChromaticAberrationWeight => _chromaticAberrationWeight;
        public float FilmGrainWeight => _filmGrainWeight;
        public float LensDistortionWeight => _lensDistortionWeight;
        public float SaturationScalar => _saturationScalar;

        private void Awake()
        {
            if (_targetCamera == null) _targetCamera = Camera.main;
            _degradationSystem = FindAnyObjectByType<MentalDegradationSystem>();
        }

        private void OnEnable()
        {
            GameEvents.OnDegradationChanged += OnDegradationChanged;
            GameEvents.OnDegradationTierReached += OnTierReached;
        }

        private void OnDisable()
        {
            GameEvents.OnDegradationChanged -= OnDegradationChanged;
            GameEvents.OnDegradationTierReached -= OnTierReached;
        }

        private void Update()
        {
            // Apply sinusoidal FOV warp if degradation is active
            if (_targetCamera != null && _degradationSystem != null)
            {
                float fovOffset = _degradationSystem.GetFOVOffset();
                _targetCamera.fieldOfView = Mathf.Lerp(_targetCamera.fieldOfView, _baseFOV + fovOffset, Time.deltaTime * 5f);
            }

            // Smoothly calculate post-processing weights
            UpdateVisualWeights();
        }

        private void OnDegradationChanged(float newValue)
        {
            _currentDegradation = newValue;
        }

        private void OnTierReached(DegradationTier tier)
        {
            Debug.Log($"[MentalDegradationHook] Post-processing tier transition applied: {tier}");
        }

        private void UpdateVisualWeights()
        {
            float d = _currentDegradation;

            // Vignette starts from Uneasy (0.2+)
            _vignetteWeight = Mathf.Clamp01(Mathf.InverseLerp(0.2f, 1.0f, d) * 0.65f);

            // Chromatic aberration starts from Disturbed (0.4+)
            _chromaticAberrationWeight = Mathf.Clamp01(Mathf.InverseLerp(0.4f, 1.0f, d) * 0.85f);

            // Film grain intensifies as mental decay takes hold
            _filmGrainWeight = Mathf.Clamp01(Mathf.InverseLerp(0.1f, 1.0f, d) * 0.75f);

            // Lens distortion pulses at Fractured & Shattered tiers (0.6+)
            if (d >= 0.6f)
            {
                float pulse = (Mathf.Sin(Time.time * 2.5f) + 1f) * 0.5f;
                _lensDistortionWeight = Mathf.Lerp(0.1f, 0.45f, (d - 0.6f) / 0.4f) * pulse;
            }
            else
            {
                _lensDistortionWeight = 0f;
            }

            // Saturation: reality drains into bleak, grim monochrome at Shattered tier
            _saturationScalar = Mathf.Lerp(1.0f, -0.6f, Mathf.InverseLerp(0.3f, 1.0f, d));
        }
    }
}
