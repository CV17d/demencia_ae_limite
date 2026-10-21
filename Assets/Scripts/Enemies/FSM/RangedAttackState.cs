// ============================================================================
// RangedAttackState.cs — Estado de Ataque a Distancia (Sicarios)
// ============================================================================
// Monitorea constantemente la línea de visión con RaycastNonAlloc.
// Si el objetivo está a la vista, dispara proyectiles balísticos no-hitscan
// respetando cadencia de fuego (cooldown). Si la línea de visión se rompe,
// transiciona a ChaseState para reposicionarse.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Combat;

namespace DemenciaNoLimite.Enemies.FSM
{
    /// <summary>
    /// Comportamiento de combate a distancia para tiradores.
    /// Valida cobertura visual y despacha proyectiles físicos con velocidad constante.
    /// </summary>
    public class RangedAttackState : IEnemyState
    {
        private readonly EnemyBrain _brain;
        private float _lastShotTime;

        // Umbrales para mantener la cadencia y distancia
        private const float MIN_ENGAGE_DISTANCE = 4.0f;
        private const float MAX_ENGAGE_DISTANCE = 12.0f;
        private const float EYE_HEIGHT_OFFSET = 1.5f;
        private const float TARGET_CHEST_OFFSET = 1.0f;

        /// <summary>
        /// Constructor con inyección de dependencia del EnemyBrain.
        /// </summary>
        public RangedAttackState(EnemyBrain brain)
        {
            _brain = brain;
        }

        /// <summary>
        /// Se ejecuta al ingresar al estado de disparo.
        /// Detiene al agente y alinea el cooldown.
        /// </summary>
        public void Enter()
        {
            _brain.StopAgent();

            // Permite un disparo inicial rápido si venía de reposo, o respeta el cooldown
            float cooldown = _brain.Data != null ? _brain.Data.AttackCooldown : 1.5f;
            _lastShotTime = Time.time - (cooldown * 0.5f);
        }

        /// <summary>
        /// Se ejecuta cada frame. Evalúa cobertura visual, encara al jugador y dispara si está listo.
        /// Diseñado sin generar Garbage Collection.
        /// </summary>
        public void Execute()
        {
            if (_brain.PlayerTransform == null)
            {
                _brain.ChangeState(_brain.IdleStateInstance);
                return;
            }

            Vector3 playerPos = _brain.PlayerTransform.position;
            float distanceToPlayer = _brain.GetDistanceToPlayer();

            // Si el jugador se acercó demasiado o huyó fuera del rango de fuego, reposicionar
            if (distanceToPlayer < MIN_ENGAGE_DISTANCE || distanceToPlayer > MAX_ENGAGE_DISTANCE)
            {
                if (_brain.ChaseStateInstance != null)
                {
                    _brain.ChangeState(_brain.ChaseStateInstance);
                    return;
                }
            }

            // Encarar al jugador continuamente
            _brain.RotateTowards(playerPos, 10f);

            // Verificación constante de línea de visión (CheckLineOfSight)
            Vector3 eyePos = _brain.transform.position + Vector3.up * EYE_HEIGHT_OFFSET;
            Vector3 targetCenter = playerPos + Vector3.up * TARGET_CHEST_OFFSET;

            bool hasLineOfSight = _brain.CheckLineOfSight(eyePos, targetCenter);

            if (!hasLineOfSight)
            {
                // El jugador se cubrió tras una pared: regresar a ChaseState para flanquear/reposicionar
                if (_brain.ChaseStateInstance != null)
                {
                    _brain.ChangeState(_brain.ChaseStateInstance);
                    return;
                }
            }

            // Disparo gobernado por cooldown
            float cooldown = _brain.Data != null ? _brain.Data.AttackCooldown : 1.5f;
            if (Time.time - _lastShotTime >= cooldown)
            {
                _lastShotTime = Time.time;
                FireProjectile(eyePos, targetCenter);
            }
        }

        /// <summary>
        /// Despacha un proyectil balístico físico hacia el jugador usando el componente Projectile.
        /// </summary>
        private void FireProjectile(Vector3 spawnOrigin, Vector3 targetPosition)
        {
            Vector3 fireDirection = (targetPosition - spawnOrigin).normalized;
            float speed = _brain.Data != null ? _brain.Data.ProjectileSpeed : 18f;
            float baseDamage = _brain.Data != null ? _brain.Data.BaseDamage : 8f;
            float finalDamage = baseDamage * _brain.DamageMultiplier;

            DamageInfo damageInfo = new DamageInfo(
                amount: finalDamage,
                attacker: _brain.gameObject,
                direction: fireDirection,
                sourceType: DamageSourceType.Bullet,
                knockbackForce: 2.0f
            );

            // Si hay un prefab asignado en EnemyBrain, se instancia; de lo contrario se usa el spawn por defecto
            GameObject projectileObj;
            if (_brain.ProjectilePrefab != null)
            {
                projectileObj = Object.Instantiate(_brain.ProjectilePrefab, spawnOrigin, Quaternion.LookRotation(fireDirection));
            }
            else
            {
                // Fallback seguro: Proyectil esférico estándar si no se ha asignado prefab en el inspector
                projectileObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                projectileObj.transform.position = spawnOrigin;
                projectileObj.transform.localScale = Vector3.one * 0.25f;

                var col = projectileObj.GetComponent<Collider>();
                if (col != null) col.isTrigger = true;

                var renderer = projectileObj.GetComponent<Renderer>();
                if (renderer != null) renderer.material.color = Color.yellow;
            }

            Projectile projectile = projectileObj.GetComponent<Projectile>();
            if (projectile == null)
            {
                projectile = projectileObj.AddComponent<Projectile>();
            }

            // Inicialización balística: sin hitscan, cinemática física continua
            projectile.Initialize(damageInfo, fireDirection, speed, gravity: 0f, explosionRadius: 0f);
        }

        /// <summary>
        /// Se ejecuta al abandonar el estado de ataque a distancia.
        /// </summary>
        public void Exit()
        {
        }
    }
}
