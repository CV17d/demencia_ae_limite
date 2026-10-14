// ============================================================================
// MerchantInventoryData.cs — ScriptableObject for merchant item pools
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Core.Data
{
    /// <summary>
    /// Defines a single purchasable item in the merchant's inventory.
    /// </summary>
    [CreateAssetMenu(
        fileName = "New Merchant Item",
        menuName = "Demencia/Elevator Events/Merchant Item",
        order    = 2
    )]
    public class MerchantItemData : ScriptableObject
    {
        [Header("Identity")]
        public string ItemID;
        public string DisplayName;

        [TextArea(2, 4)]
        public string Description;

        [Header("Cost")]
        [Tooltip("Cost in 'Fichas' (tokens) — the game's currency.")]
        public int   Cost = 50;

        [Header("Effects")]
        public float BonusMaxHP           = 0f;
        public float BonusDamage          = 0f;
        public float BonusAttackSpeed     = 0f;
        public float BonusMoveSpeed       = 0f;
        public float BonusJumpHeight      = 0f;
        public float BonusCritChance      = 0f;
        public float BonusDamageReduction = 0f;
        public float HealAmount           = 0f;
        public float HealPercent          = 0f; // 0..1, heals % of MaxHP

        [Header("Availability")]
        public int   MinFloor = 1;
        public int   MaxFloor = 0; // 0 = always available

        [Header("Selection")]
        [Tooltip("Base weight for RNG selection into the merchant's pool.")]
        public float BaseWeight = 1f;

        /// <summary>
        /// Applies the item's effects to the player.
        /// </summary>
        public void Apply(PlayerStats stats)
        {
            stats.MaxHP           += BonusMaxHP;
            stats.BaseDamage      += BonusDamage;
            stats.AttackSpeed     += BonusAttackSpeed;
            stats.MoveSpeed       += BonusMoveSpeed;
            stats.JumpHeight      += BonusJumpHeight;
            stats.CritChance      += BonusCritChance;
            stats.DamageReduction += BonusDamageReduction;

            if (HealAmount > 0f)
                stats.Heal(HealAmount);

            if (HealPercent > 0f)
                stats.Heal(stats.MaxHP * HealPercent);
        }

        public bool IsAvailableOnFloor(int floor)
        {
            if (floor < MinFloor) return false;
            if (MaxFloor > 0 && floor > MaxFloor) return false;
            return true;
        }
    }
}
