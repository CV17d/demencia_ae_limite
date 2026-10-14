// ============================================================================
// ScreenShakeController.cs — Perlin trauma-decay procedural camera shake
// ============================================================================
// Uses trauma squared decay for physical, punchy screen impacts.
// Hooks directly to damage events, explosions, and boss stomps.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Horror
{
    public class ScreenShakeController : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _traumaDecay = 1.6f;
        [SerializeField] private float _maxPitchAngle = 10f;
        [SerializeField] private float _maxYawAngle = 10f;
        [SerializeField] private float _maxRollAngle = 8f;
        [SerializeField] private float _frequency = 25f;

        private float _trauma = 0f;
        private Transform _cameraTransform;

        public static ScreenShakeController Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            var cam = Camera.main;
            if (cam != null) _cameraTransform = cam.transform;
        }

        private void OnEnable()
        {
            GameEvents.OnPlayerDamaged += OnPlayerDamaged;
        }

        private void OnDisable()
        {
            GameEvents.OnPlayerDamaged -= OnPlayerDamaged;
        }

        private void Update()
        {
            if (_trauma > 0f)
            {
                // Quadratic falloff gives a visceral punch
                float shake = _trauma * _trauma;

                float pitch = (Mathf.PerlinNoise(Time.time * _frequency, 0f) - 0.5f) * 2f * _maxPitchAngle * shake;
                float yaw = (Mathf.PerlinNoise(0f, Time.time * _frequency) - 0.5f) * 2f * _maxYawAngle * shake;
                float roll = (Mathf.PerlinNoise(Time.time * _frequency, Time.time * _frequency) - 0.5f) * 2f * _maxRollAngle * shake;

                if (_cameraTransform != null)
                {
                    _cameraTransform.localRotation *= Quaternion.Euler(pitch, yaw, roll);
                }

                _trauma = Mathf.Clamp01(_trauma - _traumaDecay * Time.deltaTime);
            }
        }

        public void AddTrauma(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        private void OnPlayerDamaged(float damage)
        {
            // More damage = more camera trauma
            AddTrauma(Mathf.Clamp01(damage / 30f));
        }
    }
}
