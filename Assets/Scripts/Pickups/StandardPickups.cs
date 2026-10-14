// ============================================================================
// Standard Pickups & Loot Drops
// ============================================================================
// Contains:
//   - FichaPickup: Tower tokens for the Elevator Merchant
//   - HealthPickup: Blood syringe / medkit restoring player HP
//   - AmmoPickup: Restores weapon ammo reserves
//   - LootTable: Spawns pickups upon enemy death factoring in Player Luck
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Pickups
{
    public class FichaPickup : PickupBase
    {
        [SerializeField] private int _amount = 10;

        public void SetAmount(int amount) => _amount = amount;

        public override bool CanPickup(GameObject player) => true;

        public override void Collect(GameObject player)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Currency += _amount;
                Debug.Log($"[FichaPickup] Collected {_amount} Fichas. Total: {GameManager.Instance.Currency}");
            }
        }
    }

    public class HealthPickup : PickupBase
    {
        [SerializeField] private float _healAmount = 25f;

        public override bool CanPickup(GameObject player)
        {
            var pStats = GameManager.Instance?.Player;
            return pStats != null && pStats.CurrentHP < pStats.MaxHP;
        }

        public override void Collect(GameObject player)
        {
            GameManager.Instance?.Player?.Heal(_healAmount);
            Debug.Log($"[HealthPickup] Player healed by {_healAmount} HP.");
        }
    }

    public class AmmoPickup : PickupBase
    {
        [SerializeField] private WeaponType _weaponType = WeaponType.Hitscan;
        [SerializeField] private int _ammoAmount = 20;

        public override bool CanPickup(GameObject player) => true;

        public override void Collect(GameObject player)
        {
            var inventory = player.GetComponentInParent<PlayerInventory>();
            if (inventory != null)
            {
                inventory.AddReserveAmmo(_weaponType, _ammoAmount);
                Debug.Log($"[AmmoPickup] Restored {_ammoAmount} rounds for {_weaponType}.");
            }
        }
    }

    public static class LootTable
    {
        public static void DropFromEnemy(Vector3 position, EnemyData data, float playerLuck)
        {
            // Drop Fichas
            int fichas = data != null ? data.RollCurrencyDrop() : Random.Range(5, 12);
            if (fichas > 0)
            {
                var fichaGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                fichaGo.name = "Pickup_Ficha";
                fichaGo.transform.position = position + Vector3.up * 0.3f;
                fichaGo.transform.localScale = new Vector3(0.3f, 0.05f, 0.3f);
                var pickup = fichaGo.AddComponent<FichaPickup>();
                pickup.SetAmount(fichas);
            }

            // Luck-based health or ammo roll
            float bonusRoll = Random.value;
            float threshold = 0.15f + (playerLuck * 0.20f); // Up to 35% with Luck

            if (bonusRoll < threshold * 0.5f)
            {
                // Drop Health
                var hpGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hpGo.name = "Pickup_Medkit";
                hpGo.transform.position = position + Vector3.up * 0.3f + Vector3.right * 0.5f;
                hpGo.transform.localScale = Vector3.one * 0.35f;
                hpGo.AddComponent<HealthPickup>();
            }
            else if (bonusRoll < threshold)
            {
                // Drop Ammo
                var ammoGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ammoGo.name = "Pickup_AmmoBox";
                ammoGo.transform.position = position + Vector3.up * 0.3f + Vector3.left * 0.5f;
                ammoGo.transform.localScale = Vector3.one * 0.3f;
                ammoGo.AddComponent<AmmoPickup>();
            }
        }
    }
}
