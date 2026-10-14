// ============================================================================
// DamagePipeline.cs — Centralized mathematical processing for combat interactions
// ============================================================================
// Calculates final damage numbers considering attacker stats, defender defense,
// headshot multipliers, critical strikes, and active Cursed Pact mutators.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Combat
{
    public static class DamagePipeline
    {
        /// <summary>
        /// Calculates damage dealt by the player to an enemy.
        /// </summary>
        public static DamageInfo ProcessPlayerToEnemy(
            WeaponData weapon,
            PlayerStats player,
            EnemyData enemyData,
            EnemyBuffState enemyBuffs,
            RaycastHit hit,
            bool isHeadshot,
            GameObject playerGo)
        {
            // 1. Critical strike check
            bool isCrit = Random.value < player.CritChance;

            // 2. Base damage from weapon + player bonus
            float rawDamage = weapon.CalculateDamage(player, isCrit, isHeadshot);

            // 3. Enemy resistance from EnemyData & EnemyBuffState
            float resistance = enemyData.DamageResistance;
            
            // If pacts granted defensive buffs, factor them in:
            float enemyEffectiveHpMult = enemyBuffs != null ? enemyBuffs.HPMultiplier : 1f;

            float finalDamage = rawDamage * (1f - Mathf.Clamp01(resistance));

            // Knockback calculation
            Vector3 direction = (hit.point - playerGo.transform.position).normalized;
            float knockback = enemyData.ImmuneToKnockback ? 0f : weapon.KnockbackForce;

            return new DamageInfo(
                amount: finalDamage,
                attacker: playerGo,
                hitPoint: hit.point,
                hitNormal: hit.normal,
                direction: direction,
                sourceType: weapon.Type == WeaponType.Melee ? DamageSourceType.Melee : DamageSourceType.Bullet,
                isCritical: isCrit,
                isHeadshot: isHeadshot,
                ignoreDefense: false,
                knockbackForce: knockback
            );
        }

        /// <summary>
        /// Calculates damage dealt by an enemy to the player.
        /// </summary>
        public static DamageInfo ProcessEnemyToPlayer(
            EnemyData enemyData,
            EnemyBuffState enemyBuffs,
            PlayerStats player,
            Vector3 attackOrigin,
            Transform playerTransform,
            GameObject enemyGo)
        {
            float baseDmg = enemyData.GetAdjustedDamage(enemyBuffs);

            // Direction
            Vector3 dir = (playerTransform.position - attackOrigin).normalized;

            return new DamageInfo(
                amount: baseDmg,
                attacker: enemyGo,
                hitPoint: playerTransform.position,
                hitNormal: -dir,
                direction: dir,
                sourceType: enemyData.HasRangedAttack ? DamageSourceType.Bullet : DamageSourceType.Melee,
                isCritical: false,
                isHeadshot: false,
                ignoreDefense: false,
                knockbackForce: 4f
            );
        }

        /// <summary>
        /// Calculates damage dealt by a boss to the player, incorporating defense-ignore ratio.
        /// </summary>
        public static DamageInfo ProcessBossToPlayer(
            BossStats bossStats,
            PlayerStats player,
            bool isEnraged,
            Vector3 bossPosition,
            Transform playerTransform,
            GameObject bossGo)
        {
            float baseDmg = bossStats.BaseDamage;
            if (isEnraged)
            {
                baseDmg *= bossStats.EnrageDmgMultiplier;
            }

            Vector3 dir = (playerTransform.position - bossPosition).normalized;

            return new DamageInfo(
                amount: baseDmg,
                attacker: bossGo,
                hitPoint: playerTransform.position,
                hitNormal: -dir,
                direction: dir,
                sourceType: DamageSourceType.BossSpecial,
                isCritical: isEnraged,
                isHeadshot: false,
                ignoreDefense: bossStats.DefenseIgnoreRatio > 0.5f,
                knockbackForce: 8f
            );
        }
    }
}
