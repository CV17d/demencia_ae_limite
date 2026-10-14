// ============================================================================
// PlayerWeaponHandler.cs — Attack execution, ballistics & reload pipeline
// ============================================================================
// Interacts with PlayerInventory, PlayerController, and DamagePipeline.
// Supports Melee cones, Hitscan raycasts with headshot multipliers, and Projectiles.
// ============================================================================

using System.Collections;
using UnityEngine;
using DemenciaNoLimite.Combat;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;

namespace DemenciaNoLimite.Player
{
    [RequireComponent(typeof(PlayerInventory))]
    public class PlayerWeaponHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _firePoint;
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private PlayerController _controller;
        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Projectile Fallback Prefab")]
        [SerializeField] private GameObject _defaultProjectilePrefab;

        private PlayerInventory _inventory;
        private float _nextFireTime;
        private bool _isReloading;

        public bool IsReloading => _isReloading;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            if (_controller == null) _controller = GetComponent<PlayerController>();
            if (_playerCamera == null) _playerCamera = Camera.main;
        }

        private void Update()
        {
            if (GameManager.Instance != null && !GameManager.Instance.IsRunActive)
                return;

            HandleWeaponSwitchInput();

            WeaponSlotInstance active = _inventory.ActiveWeaponInstance;
            if (active == null) return;

            // Attack input
            bool attackPressed = active.Data.IsAutomatic
                ? Input.GetButton("Fire1")
                : Input.GetButtonDown("Fire1");

            if (attackPressed && Time.time >= _nextFireTime && !_isReloading)
            {
                TryAttack(active);
            }

            // Reload input
            if (Input.GetKeyDown(KeyCode.R) && !_isReloading && !active.Data.InfiniteAmmo)
            {
                if (active.CurrentMagAmmo < active.Data.MagazineSize)
                {
                    StartCoroutine(ReloadRoutine(active));
                }
            }
        }

        private void HandleWeaponSwitchInput()
        {
            if (_isReloading) return;

            if (Input.GetKeyDown(KeyCode.Alpha1)) _inventory.EquipSlot(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) _inventory.EquipSlot(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) _inventory.EquipSlot(2);

            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (scroll > 0.05f) _inventory.PreviousWeapon();
            else if (scroll < -0.05f) _inventory.NextWeapon();
        }

        private void TryAttack(WeaponSlotInstance weaponInstance)
        {
            WeaponData data = weaponInstance.Data;

            // Check ammo
            if (!data.InfiniteAmmo && weaponInstance.CurrentMagAmmo <= 0)
            {
                StartCoroutine(ReloadRoutine(weaponInstance));
                return;
            }

            // Consume ammo
            if (!data.InfiniteAmmo)
            {
                weaponInstance.CurrentMagAmmo--;
                _inventory.NotifyAmmoChange();
            }

            // Set fire delay
            _nextFireTime = Time.time + data.FireInterval;

            // Apply recoil kick
            if (_controller != null)
            {
                _controller.ApplyRecoil(data.RecoilStrength, data.SpreadAngle);
            }

            // Execute based on type
            switch (data.Type)
            {
                case WeaponType.Melee:
                    ExecuteMeleeAttack(data);
                    break;

                case WeaponType.Hitscan:
                    ExecuteHitscanAttack(data);
                    break;

                case WeaponType.Projectile:
                    ExecuteProjectileAttack(data);
                    break;
            }
        }

        private void ExecuteHitscanAttack(WeaponData data)
        {
            Transform camTransform = _playerCamera != null ? _playerCamera.transform : transform;
            Vector3 origin = camTransform.position;

            for (int i = 0; i < data.PelletsPerShot; i++)
            {
                Vector3 spreadDir = CalculateSpread(camTransform.forward, data.SpreadAngle);

                if (Physics.Raycast(origin, spreadDir, out RaycastHit hit, data.MaxRange, _hitMask, QueryTriggerInteraction.Ignore))
                {
                    bool isHeadshot = hit.collider.CompareTag("Head") || hit.collider.gameObject.name.ToLower().Contains("head");

                    IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                    if (target != null && target.IsAlive)
                    {
                        PlayerStats stats = GameManager.Instance?.Player;
                        bool isCrit = Random.value < (stats?.CritChance ?? 0.05f);
                        float dmg = data.CalculateDamage(stats, isCrit, isHeadshot);

                        DamageInfo info = new DamageInfo(
                            amount: dmg,
                            attacker: gameObject,
                            hitPoint: hit.point,
                            hitNormal: hit.normal,
                            direction: spreadDir,
                            sourceType: DamageSourceType.Bullet,
                            isCritical: isCrit,
                            isHeadshot: isHeadshot,
                            ignoreDefense: false,
                            knockbackForce: data.KnockbackForce
                        );

                        target.TakeDamage(info);
                    }

                    IKnockbackable knockback = hit.collider.GetComponentInParent<IKnockbackable>();
                    if (knockback != null && data.KnockbackForce > 0f)
                    {
                        knockback.ApplyKnockback(spreadDir * data.KnockbackForce);
                    }
                }
            }
        }

        private void ExecuteMeleeAttack(WeaponData data)
        {
            Transform camTransform = _playerCamera != null ? _playerCamera.transform : transform;
            Vector3 origin = camTransform.position;
            Vector3 forward = camTransform.forward;

            Collider[] hits = Physics.OverlapSphere(origin + forward * (data.MeleeRange * 0.5f), data.MeleeRange, _hitMask);

            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;

                Vector3 toTarget = (col.bounds.center - origin).normalized;
                float angle = Vector3.Angle(forward, toTarget);

                if (angle <= data.MeleeArc * 0.5f)
                {
                    IDamageable target = col.GetComponentInParent<IDamageable>();
                    if (target != null && target.IsAlive)
                    {
                        PlayerStats stats = GameManager.Instance?.Player;
                        bool isCrit = Random.value < (stats?.CritChance ?? 0.05f);
                        float dmg = data.CalculateDamage(stats, isCrit, false);

                        DamageInfo info = new DamageInfo(
                            amount: dmg,
                            attacker: gameObject,
                            hitPoint: col.ClosestPoint(origin),
                            hitNormal: -forward,
                            direction: forward,
                            sourceType: DamageSourceType.Melee,
                            isCritical: isCrit,
                            isHeadshot: false,
                            ignoreDefense: false,
                            knockbackForce: data.KnockbackForce
                        );

                        target.TakeDamage(info);
                    }
                }
            }
        }

        private void ExecuteProjectileAttack(WeaponData data)
        {
            Transform camTransform = _playerCamera != null ? _playerCamera.transform : transform;
            Vector3 spawnPos = _firePoint != null ? _firePoint.position : camTransform.position + camTransform.forward * 0.5f;
            Vector3 direction = CalculateSpread(camTransform.forward, data.SpreadAngle);

            PlayerStats stats = GameManager.Instance?.Player;
            bool isCrit = Random.value < (stats?.CritChance ?? 0.05f);
            float baseDmg = data.CalculateDamage(stats, isCrit, false);

            DamageInfo info = new DamageInfo(
                amount: baseDmg,
                attacker: gameObject,
                direction: direction,
                sourceType: DamageSourceType.Bullet,
                isCritical: isCrit,
                knockbackForce: data.KnockbackForce
            );

            GameObject projGo = null;
            if (_defaultProjectilePrefab != null)
            {
                projGo = Instantiate(_defaultProjectilePrefab, spawnPos, Quaternion.LookRotation(direction));
            }
            else
            {
                // Fallback primitive if no prefab assigned yet
                projGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                projGo.transform.position = spawnPos;
                projGo.transform.localScale = Vector3.one * 0.2f;
                var col = projGo.GetComponent<Collider>();
                if (col != null) col.isTrigger = true;
            }

            var projComp = projGo.GetComponent<Projectile>();
            if (projComp == null)
            {
                projComp = projGo.AddComponent<Projectile>();
            }

            projComp.Initialize(info, direction, data.ProjectileSpeed, data.ProjectileGravity, data.ExplosionRadius);
        }

        private IEnumerator ReloadRoutine(WeaponSlotInstance instance)
        {
            _isReloading = true;
            int needed = instance.Data.MagazineSize - instance.CurrentMagAmmo;

            yield return new WaitForSeconds(instance.Data.ReloadTime);

            if (_inventory.ConsumeReserveAmmo(instance.Data.Type, needed, out int gained))
            {
                instance.CurrentMagAmmo += gained;
            }

            _isReloading = false;
            _inventory.NotifyAmmoChange();
        }

        private Vector3 CalculateSpread(Vector3 forward, float angle)
        {
            if (angle <= 0.001f) return forward;
            Quaternion spreadRot = Quaternion.Euler(
                Random.Range(-angle, angle),
                Random.Range(-angle, angle),
                0f
            );
            return spreadRot * forward;
        }
    }
}
