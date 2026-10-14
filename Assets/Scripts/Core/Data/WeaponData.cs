// ============================================================================
// WeaponData.cs — ScriptableObject defining a weapon archetype
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Core.Data
{
    public enum WeaponType
    {
        Melee,
        Hitscan,     // Instant raycast (pistols, rifles)
        Projectile,  // Physical projectile (shotgun slugs, grenades)
    }

    public enum WeaponRarity
    {
        Improvised,  // Starting weapons — pipes, shivs
        Standard,    // Common pickups
        Military,    // Rare, powerful
        Aberrant     // Cursed / mutated weapons from pacts
    }

    [CreateAssetMenu(
        fileName = "New Weapon",
        menuName = "Demencia/Combat/Weapon Data",
        order    = 1
    )]
    public class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        public string WeaponID;
        public string DisplayName;
        [TextArea(2, 4)]
        public string Description;
        public WeaponType   Type;
        public WeaponRarity Rarity;

        [Header("Damage")]
        public float BaseDamage        = 10f;
        public float CritMultiplier    = 2f;
        public float HeadshotMultiplier = 3f;

        [Header("Fire Rate")]
        public float FireRate          = 1f;   // Shots per second
        public float ChargeTime        = 0f;   // 0 = no charge mechanic
        public bool  IsAutomatic       = false;

        [Header("Ammo")]
        public int   MaxAmmo           = 30;
        public int   MagazineSize      = 10;
        public float ReloadTime        = 1.5f;
        public bool  InfiniteAmmo      = false; // Melee weapons

        [Header("Range & Spread")]
        public float MaxRange          = 50f;
        public float SpreadAngle       = 2f;   // Degrees of inaccuracy
        public int   PelletsPerShot    = 1;    // >1 for shotguns

        [Header("Melee Specifics")]
        public float MeleeRange        = 2.5f;
        public float MeleeArc          = 90f;  // Swing arc in degrees
        public float KnockbackForce    = 5f;

        [Header("Projectile Specifics")]
        public float ProjectileSpeed   = 30f;
        public float ProjectileGravity = 0f;
        public float ExplosionRadius   = 0f;   // 0 = no explosion

        [Header("Visual/Audio References (Prefab names)")]
        public string MuzzleFlashPrefab;
        public string ProjectilePrefab;
        public string ImpactEffectPrefab;
        public string FireSoundID;
        public string ReloadSoundID;

        [Header("Camera Effects")]
        public float RecoilStrength    = 1f;
        public float ScreenShake       = 0.1f;

        // ── Computed ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns the final damage for a single hit, incorporating player stats.
        /// </summary>
        public float CalculateDamage(PlayerStats player, bool isCrit, bool isHeadshot)
        {
            float dmg = BaseDamage + player.BaseDamage;

            if (isCrit)     dmg *= CritMultiplier;
            if (isHeadshot) dmg *= HeadshotMultiplier;

            return dmg;
        }

        /// <summary>
        /// Returns the actual fire interval in seconds.
        /// </summary>
        public float FireInterval => 1f / Mathf.Max(0.01f, FireRate);
    }
}
