// ============================================================================
// PlayerWeaponHandler.cs — Shooting Mechanics & Ballistics Pipeline
// ============================================================================
// Uses a clean, reliable Physics.Raycast originating from the center of the Main Camera.
// Specifically targets objects on the "Enemy" LayerMask.
// Uses GetComponent<IDamageable>() to apply damage.
// Provides visual Debug.DrawRay for bullet tracing in the Scene View.
// Explicitly ignores the Player's own colliders.
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
        [Header("Camera & Raycast References")]
        [Tooltip("Main FPS Camera. If null, automatically resolves to Camera.main.")]
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private Transform _firePoint;
        [SerializeField] private PlayerController _controller;

        [Header("Targeting & Layer Configuration")]
        [Tooltip("Specific LayerMask for detecting enemies.")]
        [SerializeField] private LayerMask _enemyLayerMask;
        [SerializeField] private float _defaultMaxRange = 100f;
        [SerializeField] private float _debugRayDuration = 1.5f;

        [Header("Projectile Fallback Prefab")]
        [SerializeField] private GameObject _defaultProjectilePrefab;

        private PlayerInventory _inventory;
        private float _nextFireTime;
        private bool _isReloading;
        private Collider[] _playerColliders;

        public bool IsReloading => _isReloading;
        public LayerMask EnemyLayerMask => _enemyLayerMask;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            if (_controller == null) _controller = GetComponent<PlayerController>();

            ResolveCamera();
            ResolveEnemyLayerMask();

            _playerColliders = GetComponentsInChildren<Collider>();
        }

        private void Start()
        {
            ResolveCamera();
        }

        private void ResolveCamera()
        {
            if (_playerCamera == null)
            {
                _playerCamera = Camera.main ?? GetComponentInChildren<Camera>() ?? FindAnyObjectByType<Camera>();
            }
        }

        private void ResolveEnemyLayerMask()
        {
            // If enemyLayerMask is empty (0), attempt to set to "Enemy" layer
            if (_enemyLayerMask.value == 0)
            {
                int enemyLayer = LayerMask.NameToLayer("Enemy");
                if (enemyLayer != -1)
                {
                    _enemyLayerMask = 1 << enemyLayer;
                }
                else
                {
                    // Fallback to everything except player
                    int playerLayer = LayerMask.NameToLayer("Player");
                    _enemyLayerMask = playerLayer != -1 ? ~(1 << playerLayer) : ~0;
                }
            }
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

            // Ammo verification
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

            _nextFireTime = Time.time + data.FireInterval;

            if (_controller != null)
            {
                _controller.ApplyRecoil(data.RecoilStrength, data.SpreadAngle);
            }

            switch (data.Type)
            {
                case WeaponType.Hitscan:
                default:
                    ExecuteRaycastShot(data);
                    break;

                case WeaponType.Melee:
                    ExecuteMeleeAttack(data);
                    break;

                case WeaponType.Projectile:
                    ExecuteProjectileAttack(data);
                    break;
            }
        }

        /// <summary>
        /// Simple, reliable Physics.Raycast originating from the exact center of the Main Camera.
        /// Ignores the Player's own colliders, detects objects on the Enemy LayerMask, and applies IDamageable damage.
        /// </summary>
        private void ExecuteRaycastShot(WeaponData data)
        {
            ResolveCamera();

            // Origin & Direction strictly originating from the center of the Main Camera viewport (0.5, 0.5)
            Ray cameraCenterRay = (_playerCamera != null)
                ? _playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : new Ray(transform.position + Vector3.up * 1.5f, transform.forward);

            Vector3 origin = cameraCenterRay.origin + cameraCenterRay.direction * 0.25f;
            float maxRange = data.MaxRange > 0f ? data.MaxRange : _defaultMaxRange;
            int pellets = Mathf.Max(1, data.PelletsPerShot);

            // Construct mask excluding Player layer
            int playerLayer = gameObject.layer;
            LayerMask targetMask = _enemyLayerMask.value != 0
                ? (LayerMask)(_enemyLayerMask.value & ~(1 << playerLayer))
                : (LayerMask)(~(1 << playerLayer));

            for (int i = 0; i < pellets; i++)
            {
                Vector3 rayDir = CalculateSpread(cameraCenterRay.direction, data.SpreadAngle);

                bool hitFound = Physics.Raycast(
                    origin,
                    rayDir,
                    out RaycastHit hit,
                    maxRange,
                    targetMask,
                    QueryTriggerInteraction.Ignore
                );

                IDamageable target = null;
                bool isEnemyHit = false;

                if (hitFound)
                {
                    // Crucial: Ignore the player's own colliders
                    if (IsPlayerCollider(hit.collider))
                    {
                        continue;
                    }

                    // Query IDamageable on hit object or its parent hierarchy
                    target = hit.collider.GetComponent<IDamageable>() ?? hit.collider.GetComponentInParent<IDamageable>();

                    if (target != null && target.IsAlive)
                    {
                        isEnemyHit = true;

                        bool isHeadshot = hit.collider.CompareTag("Head") || 
                                          hit.collider.gameObject.name.ToLower().Contains("head");

                        PlayerStats stats = GameManager.Instance?.Player;
                        bool isCrit = Random.value < (stats?.CritChance ?? 0.05f);
                        float dmg = data.CalculateDamage(stats, isCrit, isHeadshot);

                        DamageInfo info = new DamageInfo(
                            amount: dmg,
                            attacker: gameObject,
                            hitPoint: hit.point,
                            hitNormal: hit.normal,
                            direction: rayDir,
                            sourceType: DamageSourceType.Bullet,
                            isCritical: isCrit,
                            isHeadshot: isHeadshot,
                            ignoreDefense: false,
                            knockbackForce: data.KnockbackForce
                        );

                        target.TakeDamage(info);
                        Debug.Log($"[PlayerWeaponHandler] Hit {hit.collider.gameObject.name} for {dmg:F1} DMG (Crit: {isCrit}, Headshot: {isHeadshot})");
                    }

                    // Apply physical impulse if entity reacts to knockback
                    IKnockbackable knockback = hit.collider.GetComponent<IKnockbackable>() ?? hit.collider.GetComponentInParent<IKnockbackable>();
                    if (knockback != null && data.KnockbackForce > 0f)
                    {
                        knockback.ApplyKnockback(rayDir * data.KnockbackForce);
                    }
                }

                // Visual Feedback: Debug.DrawRay in scene view to trace bullets
                // Green when hitting an enemy with IDamageable, Yellow on environment hit, Red on miss
                float lineDist = hitFound ? hit.distance : maxRange;
                Color rayColor = isEnemyHit ? Color.green : (hitFound ? Color.yellow : Color.red);
                Debug.DrawRay(origin, rayDir * lineDist, rayColor, _debugRayDuration);
            }
        }

        private bool IsPlayerCollider(Collider col)
        {
            if (col == null) return false;
            if (col.transform.root == transform.root) return true;
            if (col.CompareTag("Player")) return true;

            if (_playerColliders != null)
            {
                for (int i = 0; i < _playerColliders.Length; i++)
                {
                    if (_playerColliders[i] == col) return true;
                }
            }
            return false;
        }

        private void ExecuteMeleeAttack(WeaponData data)
        {
            ResolveCamera();
            Transform camTransform = _playerCamera != null ? _playerCamera.transform : transform;
            Vector3 origin = camTransform.position;
            Vector3 forward = camTransform.forward;

            Collider[] hits = Physics.OverlapSphere(origin + forward * (data.MeleeRange * 0.5f), data.MeleeRange, _enemyLayerMask);

            foreach (var col in hits)
            {
                if (IsPlayerCollider(col)) continue;

                Vector3 toTarget = (col.bounds.center - origin).normalized;
                float angle = Vector3.Angle(forward, toTarget);

                if (angle <= data.MeleeArc * 0.5f)
                {
                    IDamageable target = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
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
            ResolveCamera();
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

            GameObject projGo;
            if (_defaultProjectilePrefab != null)
            {
                projGo = Instantiate(_defaultProjectilePrefab, spawnPos, Quaternion.LookRotation(direction));
            }
            else
            {
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
