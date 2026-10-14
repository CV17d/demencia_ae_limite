// ============================================================================
// ElevatorEventSystem.cs — Risk/Reward Interlude Between Floors
// ============================================================================
// Between every floor, the player enters an elevator where RNG (weighted by
// the AI Director and Luck) determines one of two events:
//
//   1. MERCHANT — Buy upgrades / heal using Fichas (currency)
//   2. CURSED PACT — Accept a player buff + permanent enemy buff
//
// The system uses ScriptableObject-based data (CursedPactData, MerchantItemData)
// for content and an event-driven architecture for communication.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Core
{
    public class ElevatorEventSystem : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ════════════════════════════════════════════════════════════════════

        [Header("Event Probabilities")]
        [Tooltip("Base chance of getting a Merchant (vs Cursed Pact). 0.6 = 60%.")]
        [Range(0f, 1f)]
        [SerializeField] private float _baseMerchantChance = 0.55f;

        [Header("Pact Pool")]
        [Tooltip("All available Cursed Pacts. Drag ScriptableObjects here.")]
        [SerializeField] private List<CursedPactData> _pactPool = new List<CursedPactData>();

        [Header("Merchant Pool")]
        [Tooltip("All available Merchant items. Drag ScriptableObjects here.")]
        [SerializeField] private List<MerchantItemData> _merchantPool = new List<MerchantItemData>();

        [Header("Merchant Configuration")]
        [Tooltip("Number of items the merchant offers per visit.")]
        [SerializeField] private int _merchantSlotCount = 3;

        [Header("Pact Configuration")]
        [Tooltip("Number of pact options presented to the player.")]
        [SerializeField] private int _pactChoiceCount = 2;

        // ── State ──────────────────────────────────────────────────────────

        /// <summary>The event type currently active in the elevator.</summary>
        public ElevatorResult CurrentEventType { get; private set; }

        /// <summary>Pacts offered this elevator visit (if pact event).</summary>
        public List<CursedPactData> OfferedPacts { get; private set; } = new List<CursedPactData>();

        /// <summary>Items offered by the merchant (if merchant event).</summary>
        public List<MerchantItemData> OfferedItems { get; private set; } = new List<MerchantItemData>();

        /// <summary>The next floor the player will proceed to.</summary>
        private int _nextFloorNumber;

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called by GameManager when the player enters the elevator.
        /// Rolls for event type and prepares the offerings.
        /// </summary>
        public void TriggerEvent(PlayerStats player, EnemyBuffState enemyBuffs, int nextFloor)
        {
            _nextFloorNumber = nextFloor;

            // ── Roll for event type ────────────────────────────────────────
            float merchantChance = CalculateMerchantChance(player, nextFloor);
            float roll = Random.value;

            CurrentEventType = (roll <= merchantChance)
                ? ElevatorResult.Merchant
                : ElevatorResult.CursedPact;

            // ── Prepare offerings ──────────────────────────────────────────
            if (CurrentEventType == ElevatorResult.Merchant)
            {
                PreparemerchantOfferings(player, nextFloor);
            }
            else
            {
                PreparePactOfferings(player, nextFloor);
            }

            GameEvents.FireElevatorResolved(CurrentEventType);

            Debug.Log($"[Elevator] Event: {CurrentEventType} | " +
                      $"Roll: {roll:F3} (needed ≤{merchantChance:F3} for Merchant) | " +
                      $"Next floor: {nextFloor}");
        }

        // ════════════════════════════════════════════════════════════════════
        // MERCHANT LOGIC
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selects merchant items from the pool, weighted by AI Director
        /// and filtered by floor availability.
        /// </summary>
        private void PreparemerchantOfferings(PlayerStats player, int floor)
        {
            OfferedItems.Clear();

            // Filter available items for this floor
            var available = _merchantPool
                .Where(item => item != null && item.IsAvailableOnFloor(floor))
                .ToList();

            if (available.Count == 0)
            {
                Debug.LogWarning("[Elevator] No merchant items available for this floor!");
                return;
            }

            // Weighted random selection without replacement
            int toSelect = Mathf.Min(_merchantSlotCount, available.Count);

            for (int i = 0; i < toSelect; i++)
            {
                float totalWeight = available.Sum(item => CalculateItemWeight(item, player));

                if (totalWeight <= 0f)
                {
                    // Fallback: equal weight
                    OfferedItems.Add(available[Random.Range(0, available.Count)]);
                    continue;
                }

                float roll = Random.value * totalWeight;
                float cumulative = 0f;

                for (int j = 0; j < available.Count; j++)
                {
                    cumulative += CalculateItemWeight(available[j], player);
                    if (roll <= cumulative)
                    {
                        OfferedItems.Add(available[j]);
                        available.RemoveAt(j); // No replacement
                        break;
                    }
                }
            }

            Debug.Log($"[Elevator] Merchant offers: " +
                      string.Join(", ", OfferedItems.Select(i => i.DisplayName)));
        }

        /// <summary>
        /// Called by UI when the player purchases a merchant item.
        /// </summary>
        public bool PurchaseItem(MerchantItemData item, PlayerStats player)
        {
            var gm = GameManager.Instance;
            if (gm == null) return false;

            if (gm.Currency < item.Cost)
            {
                Debug.Log($"[Elevator] Cannot afford {item.DisplayName}: " +
                          $"Cost={item.Cost}, Have={gm.Currency}");
                return false;
            }

            gm.Currency -= item.Cost;
            item.Apply(player);
            GameEvents.FireItemPurchased(item);

            Debug.Log($"[Elevator] Purchased: {item.DisplayName} for {item.Cost} Fichas. " +
                      $"Remaining: {gm.Currency}");

            return true;
        }

        /// <summary>
        /// Called by UI when the player chooses to leave the merchant.
        /// </summary>
        public void LeaveElevator()
        {
            GameManager.Instance?.ResumeFromElevator(_nextFloorNumber);
        }

        // ════════════════════════════════════════════════════════════════════
        // CURSED PACT LOGIC
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selects pact options from the pool, weighted by AI Director
        /// and filtered by floor availability.
        /// </summary>
        private void PreparePactOfferings(PlayerStats player, int floor)
        {
            OfferedPacts.Clear();

            var available = _pactPool
                .Where(pact => pact != null && pact.IsAvailableOnFloor(floor))
                .ToList();

            if (available.Count == 0)
            {
                Debug.LogWarning("[Elevator] No pacts available! Falling back to merchant.");
                CurrentEventType = ElevatorResult.Merchant;
                PreparemerchantOfferings(player, floor);
                return;
            }

            int toSelect = Mathf.Min(_pactChoiceCount, available.Count);

            for (int i = 0; i < toSelect; i++)
            {
                float totalWeight = available.Sum(p => CalculatePactWeight(p, player));

                if (totalWeight <= 0f)
                {
                    OfferedPacts.Add(available[Random.Range(0, available.Count)]);
                    continue;
                }

                float roll = Random.value * totalWeight;
                float cumulative = 0f;

                for (int j = 0; j < available.Count; j++)
                {
                    cumulative += CalculatePactWeight(available[j], player);
                    if (roll <= cumulative)
                    {
                        OfferedPacts.Add(available[j]);
                        available.RemoveAt(j);
                        break;
                    }
                }
            }

            Debug.Log($"[Elevator] Pact choices: " +
                      string.Join(", ", OfferedPacts.Select(p => p.DisplayName)));
        }

        /// <summary>
        /// Called by UI when the player accepts a Cursed Pact.
        /// Applies both the player buff AND the enemy buff.
        /// </summary>
        public void AcceptPact(CursedPactData pact)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // Apply player buff
            pact.ApplyPlayerBuff(gm.Player);

            // Apply permanent enemy buff
            gm.EnemyBuffs.ApplyPact(pact);

            GameEvents.FirePactAccepted(pact);

            Debug.Log($"[Elevator] Pact ACCEPTED: '{pact.DisplayName}' — " +
                      $"Player: +{pact.BonusDamage} DMG, +{pact.BonusMaxHP} HP | " +
                      $"Enemies: HP x{pact.EnemyHPMultiplier}, DMG x{pact.EnemyDamageMultiplier}");
        }

        /// <summary>
        /// Called by UI when the player declines all pacts.
        /// No buffs or debuffs are applied.
        /// </summary>
        public void DeclinePacts()
        {
            Debug.Log("[Elevator] Pacts DECLINED. No changes applied.");
            LeaveElevator();
        }

        // ════════════════════════════════════════════════════════════════════
        // PROBABILITY CALCULATIONS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calculates the actual chance of getting a Merchant event.
        /// Modified by:
        ///   - Player health (low HP → higher merchant chance)
        ///   - Luck (high luck → higher merchant chance)
        ///   - Pacts already taken (many pacts → higher merchant chance)
        ///   - Floor progression (late game → lower merchant chance)
        /// </summary>
        private float CalculateMerchantChance(PlayerStats player, int nextFloor)
        {
            float chance = _baseMerchantChance;

            // Low HP increases merchant chance (mercy mechanic)
            float hpRatio = player.CurrentHP / player.MaxHP;
            if (hpRatio < 0.4f)
            {
                chance += (0.4f - hpRatio) * 0.5f; // Up to +20% at very low HP
            }

            // Luck increases merchant chance
            chance += (player.Luck - 0.5f) * 0.2f; // ±10% from Luck

            // Many pacts → more merchants (give the player a break)
            chance += player.PactsTaken * 0.05f;

            // Late game → fewer merchants (pressure increases)
            float progressionPenalty = (nextFloor / (float)GameManager.TOTAL_FLOORS) * 0.15f;
            chance -= progressionPenalty;

            return Mathf.Clamp(chance, 0.15f, 0.85f);
        }

        /// <summary>
        /// Calculates the selection weight for a merchant item.
        /// Items that complement the player's weaknesses are weighted higher.
        /// </summary>
        private float CalculateItemWeight(MerchantItemData item, PlayerStats player)
        {
            float weight = item.BaseWeight;

            // Healing items are more valuable at low HP
            if (item.HealAmount > 0f || item.HealPercent > 0f)
            {
                float hpRatio = player.CurrentHP / player.MaxHP;
                weight *= Mathf.Lerp(2f, 0.5f, hpRatio); // 2x weight at low HP
            }

            // Damage items more valuable for tanks (low damage builds)
            if (item.BonusDamage > 0f)
            {
                weight *= Mathf.Lerp(1.5f, 0.8f, player.GlassCannonRatio);
            }

            // Defense items more valuable for glass cannons
            if (item.BonusDamageReduction > 0f || item.BonusMaxHP > 0f)
            {
                weight *= Mathf.Lerp(0.8f, 1.5f, player.GlassCannonRatio);
            }

            return Mathf.Max(0.1f, weight);
        }

        /// <summary>
        /// Calculates the selection weight for a Cursed Pact.
        /// Pacts that are "tempting" for the current build are weighted higher.
        /// </summary>
        private float CalculatePactWeight(CursedPactData pact, PlayerStats player)
        {
            float weight = pact.BaseSelectionWeight;

            // Pacts offering damage are more tempting for tanks
            if (pact.BonusDamage > 0f)
            {
                weight *= Mathf.Lerp(1.5f, 0.8f, player.GlassCannonRatio);
            }

            // Pacts offering HP are more tempting for glass cannons
            if (pact.BonusMaxHP > 0f)
            {
                weight *= Mathf.Lerp(0.8f, 1.5f, player.GlassCannonRatio);
            }

            // Pacts with harsh enemy buffs are slightly less likely at high degradation
            // (the game is already punishing enough at that point)
            if (player.MentalDegradation > 0.6f)
            {
                float harshness = pact.EnemyHPMultiplier * pact.EnemyDamageMultiplier;
                if (harshness > 1.5f)
                {
                    weight *= 0.7f;
                }
            }

            return Mathf.Max(0.1f, weight);
        }
    }
}
