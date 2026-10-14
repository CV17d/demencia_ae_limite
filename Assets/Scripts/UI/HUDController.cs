// ============================================================================
// HUDController.cs — Model-View-Presenter (MVP) for in-game HUD
// ============================================================================
// Aggregates data from GameManager, PlayerStats, MentalDegradation, and PlayerInventory.
// Exposes formatted strings and normalized floats for UI text/sliders to bind directly.
//
// NOTE: All compass, radar, waypoint indicators, and navigation markers are
// strictly excluded. The player must navigate the 27-floor tower and locate
// the exit elevator exclusively through manual spatial exploration.
// ============================================================================

using System;
using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.UI
{
    public class HUDController : MonoBehaviour
    {
        // ── Exposed State Properties ───────────────────────────────────────
        public float HealthNormalized { get; private set; } = 1.0f;
        public string HealthDisplayText { get; private set; } = "100 / 100";

        public float SanityNormalized { get; private set; } = 0.0f;
        public string SanityTierDisplayText { get; private set; } = "STABLE";

        public string FloorDisplayText { get; private set; } = "PISO 01 / 27";
        public string CurrencyDisplayText { get; private set; } = "0 FICHAS";
        public string AmmoDisplayText { get; private set; } = "-- / --";

        public event Action OnHUDRefreshed;

        private PlayerInventory _cachedInventory;

        private void OnEnable()
        {
            GameEvents.OnFloorReady += OnFloorChanged;
            GameEvents.OnDegradationChanged += OnDegradationChanged;
            GameEvents.OnDegradationTierReached += OnDegradationTierReached;
            GameEvents.OnPlayerDamaged += OnPlayerDamagedOrHealed;
            GameEvents.OnPlayerHealed += OnPlayerDamagedOrHealed;
        }

        private void OnDisable()
        {
            GameEvents.OnFloorReady -= OnFloorChanged;
            GameEvents.OnDegradationChanged -= OnDegradationChanged;
            GameEvents.OnDegradationTierReached -= OnDegradationTierReached;
            GameEvents.OnPlayerDamaged -= OnPlayerDamagedOrHealed;
            GameEvents.OnPlayerHealed -= OnPlayerDamagedOrHealed;

            if (_cachedInventory != null)
            {
                _cachedInventory.OnAmmoUpdated -= OnAmmoUpdated;
            }
        }

        private void Start()
        {
            HookInventory();
            RefreshAll();
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.Player != null)
            {
                CurrencyDisplayText = $"{gm.Currency} FICHAS";
                HealthNormalized = Mathf.Clamp01(gm.Player.CurrentHP / Mathf.Max(1f, gm.Player.MaxHP));
                HealthDisplayText = $"{Mathf.CeilToInt(gm.Player.CurrentHP)} / {Mathf.CeilToInt(gm.Player.MaxHP)}";
                SanityNormalized = gm.Player.MentalDegradation;
                OnHUDRefreshed?.Invoke();
            }
        }

        private void HookInventory()
        {
            var inv = FindAnyObjectByType<PlayerInventory>();
            if (inv != null)
            {
                _cachedInventory = inv;
                _cachedInventory.OnAmmoUpdated += OnAmmoUpdated;
                _cachedInventory.NotifyAmmoChange();
            }
        }

        private void OnAmmoUpdated(int currentMag, int reserve)
        {
            AmmoDisplayText = $"{currentMag} | {reserve}";
            OnHUDRefreshed?.Invoke();
        }

        private void OnFloorChanged(FloorConfig config)
        {
            FloorDisplayText = config.Type == FloorType.BossRoom 
                ? $"PISO {config.FloorNumber:D2} - [ENCUENTRO DE JEFE]"
                : $"PISO {config.FloorNumber:D2} / 27 - ACTO {(int)config.Tier + 1}";
            OnHUDRefreshed?.Invoke();
        }

        private void OnDegradationChanged(float val)
        {
            SanityNormalized = val;
            OnHUDRefreshed?.Invoke();
        }

        private void OnDegradationTierReached(DegradationTier tier)
        {
            SanityTierDisplayText = tier.ToString().ToUpper();
            OnHUDRefreshed?.Invoke();
        }

        private void OnPlayerDamagedOrHealed(float val)
        {
            RefreshAll();
        }

        public void RefreshAll()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Player == null) return;

            HealthNormalized = Mathf.Clamp01(gm.Player.CurrentHP / Mathf.Max(1f, gm.Player.MaxHP));
            HealthDisplayText = $"{Mathf.CeilToInt(gm.Player.CurrentHP)} / {Mathf.CeilToInt(gm.Player.MaxHP)}";
            SanityNormalized = gm.Player.MentalDegradation;
            CurrencyDisplayText = $"{gm.Currency} FICHAS";

            OnHUDRefreshed?.Invoke();
        }
    }
}
