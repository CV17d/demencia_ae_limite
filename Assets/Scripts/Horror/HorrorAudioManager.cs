// ============================================================================
// HorrorAudioManager.cs — Dynamic soundscape, heartbeat tempo & audio decay
// ============================================================================
// Drives audio parameters based on Player HP and Mental Degradation:
//   - Heartbeat BPM rises from 60 to 160 BPM
//   - Low-frequency ambient pitch shifts downward with floor acts
//   - Hallucinatory whisper triggers at random intervals
// ============================================================================

using System;
using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Horror
{
    public class HorrorAudioManager : MonoBehaviour
    {
        [Header("Heartbeat Parameters")]
        [SerializeField] private float _minBPM = 60f;
        [SerializeField] private float _maxBPM = 160f;
        [SerializeField] private float _currentBPM = 60f;

        [Header("Hallucination Whispers")]
        [SerializeField] private float _minWhisperInterval = 8f;
        [SerializeField] private float _maxWhisperInterval = 25f;
        private float _nextWhisperTime;

        [Header("Audio Mixer Expose Parameters")]
        [SerializeField] private float _ambienceLowPassCutoff = 22000f; // Hz
        [SerializeField] private float _pitchDissonance = 1.0f;

        public event Action<string> OnPlayAudioStinger; // ID of audio stinger
        public event Action<float> OnHeartbeatTick;

        public float CurrentBPM => _currentBPM;
        public float AmbienceLowPassCutoff => _ambienceLowPassCutoff;
        public float PitchDissonance => _pitchDissonance;

        private void OnEnable()
        {
            GameEvents.OnPlayerDamaged += OnPlayerDamaged;
            GameEvents.OnDegradationTierReached += OnDegradationTierReached;
        }

        private void OnDisable()
        {
            GameEvents.OnPlayerDamaged -= OnPlayerDamaged;
            GameEvents.OnDegradationTierReached -= OnDegradationTierReached;
        }

        private void Start()
        {
            ScheduleNextWhisper();
        }

        private void Update()
        {
            UpdateHeartbeat();
            CheckWhisperTriggers();
            UpdateAudioFilterSettings();
        }

        private void UpdateHeartbeat()
        {
            var pStats = GameManager.Instance?.Player;
            if (pStats == null) return;

            float hpFactor = 1f - Mathf.Clamp01(pStats.CurrentHP / pStats.MaxHP);
            float degradationFactor = pStats.MentalDegradation;

            // Combined stress formula
            float totalStress = Mathf.Clamp01((hpFactor * 0.6f) + (degradationFactor * 0.4f));
            _currentBPM = Mathf.Lerp(_minBPM, _maxBPM, totalStress);
        }

        private void CheckWhisperTriggers()
        {
            var pStats = GameManager.Instance?.Player;
            if (pStats == null || pStats.MentalDegradation < 0.4f) return;

            if (Time.time >= _nextWhisperTime)
            {
                TriggerRandomWhisper();
                ScheduleNextWhisper();
            }
        }

        private void TriggerRandomWhisper()
        {
            string[] whisperKeys = { "WHISPER_PARANOIA", "WHISPER_WATCHING", "WHISPER_BETRAYAL", "WHISPER_FLESH" };
            string chosen = whisperKeys[UnityEngine.Random.Range(0, whisperKeys.Length)];
            Debug.Log($"[HorrorAudioManager] Hallucination sound triggered: {chosen}");
            OnPlayAudioStinger?.Invoke(chosen);
        }

        private void ScheduleNextWhisper()
        {
            var pStats = GameManager.Instance?.Player;
            float d = pStats != null ? pStats.MentalDegradation : 0f;

            // Higher degradation accelerates whisper frequency
            float interval = Mathf.Lerp(_maxWhisperInterval, _minWhisperInterval, d);
            _nextWhisperTime = Time.time + interval + UnityEngine.Random.Range(-2f, 2f);
        }

        private void UpdateAudioFilterSettings()
        {
            var pStats = GameManager.Instance?.Player;
            float d = pStats != null ? pStats.MentalDegradation : 0f;

            // Muffle realistic high frequencies when shattered
            _ambienceLowPassCutoff = Mathf.Lerp(22000f, 1200f, d * 0.8f);

            // Subtle pitch wobble at high horror
            if (d >= 0.5f)
            {
                _pitchDissonance = 1.0f + (Mathf.Sin(Time.time * 1.5f) * 0.05f * d);
            }
            else
            {
                _pitchDissonance = 1.0f;
            }
        }

        private void OnPlayerDamaged(float amount)
        {
            OnPlayAudioStinger?.Invoke("SFX_FLESH_IMPACT");
        }

        private void OnDegradationTierReached(DegradationTier tier)
        {
            if (tier >= DegradationTier.Disturbed)
            {
                OnPlayAudioStinger?.Invoke("SFX_SANITY_SPIKE");
            }
        }
    }
}
