// ============================================================================
// BossHealthBarUI.cs — UI Presenter for Adaptive Boss encounters
// ============================================================================
// Binds to BossController health changes and enrage states.
// Automatically shows/hides when entering and clearing boss rooms.
// ============================================================================

using System;
using UnityEngine;
using DemenciaNoLimite.Boss;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.UI
{
    public class BossHealthBarUI : MonoBehaviour
    {
        [Header("State")]
        public bool IsVisible { get; private set; } = false;
        public string BossName { get; private set; } = "";
        public float HealthNormalized { get; private set; } = 1.0f;
        public bool IsBossEnraged { get; private set; } = false;

        public event Action OnBossUIUpdated;

        private BossController _activeBoss;

        private void OnEnable()
        {
            GameEvents.OnFloorReady += OnFloorReady;
            GameEvents.OnBossDefeated += OnBossDefeated;
        }

        private void OnDisable()
        {
            GameEvents.OnFloorReady -= OnFloorReady;
            GameEvents.OnBossDefeated -= OnBossDefeated;
            UnbindBoss();
        }

        private void OnFloorReady(FloorConfig config)
        {
            if (config.Type != FloorType.BossRoom)
            {
                Hide();
                return;
            }

            // Find spawned boss in arena
            _activeBoss = FindAnyObjectByType<BossController>();
            if (_activeBoss != null)
            {
                BindBoss(_activeBoss);
            }
        }

        private void BindBoss(BossController boss)
        {
            _activeBoss = boss;
            BossName = boss.ActiveStats != null ? boss.ActiveStats.BossID.Replace("_", " ") : "TITAN ABERRANTE";
            HealthNormalized = Mathf.Clamp01(boss.CurrentHealth / boss.MaxHealth);
            IsBossEnraged = boss.IsEnraged;
            IsVisible = true;

            boss.OnHealthChanged += OnBossHealthChanged;
            boss.OnEnrageStateChanged += OnBossEnraged;

            OnBossUIUpdated?.Invoke();
        }

        private void UnbindBoss()
        {
            if (_activeBoss != null)
            {
                _activeBoss.OnHealthChanged -= OnBossHealthChanged;
                _activeBoss.OnEnrageStateChanged -= OnBossEnraged;
                _activeBoss = null;
            }
        }

        private void OnBossHealthChanged(float current, float max)
        {
            HealthNormalized = Mathf.Clamp01(current / Mathf.Max(1f, max));
            OnBossUIUpdated?.Invoke();
        }

        private void OnBossEnraged(bool enraged)
        {
            IsBossEnraged = enraged;
            OnBossUIUpdated?.Invoke();
        }

        private void OnBossDefeated(string bossID)
        {
            Hide();
        }

        public void Hide()
        {
            IsVisible = false;
            UnbindBoss();
            OnBossUIUpdated?.Invoke();
        }
    }
}
