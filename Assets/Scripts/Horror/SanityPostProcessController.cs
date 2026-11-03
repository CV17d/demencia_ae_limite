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

        // Propiedades públicas para lectura externa
        public float CurrentSanity => _currentSanity;
        public float TargetSanity => _targetSanity;
        public float ModerateThreshold => _moderateSanityThreshold;
        public float CriticalThreshold => _criticalSanityThreshold;
        public Volume TargetVolume => _targetVolume;
    }
}
