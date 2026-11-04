// ============================================================================
// BossController.cs — Adaptive Boss Entity & Combat Execution
// ============================================================================
// Realizes the BossStats calculated by AdaptiveBossController.
// Implements tank-like imposing physical proportions:
//   - Significantly wider ("fat" tank proportions on X & Z axes)
//   - Height kept strictly under the boss room's ceiling limit
//   - CapsuleCollider and NavMeshAgent accurately calibrated to wider girth
//   - 2D Billboard sprite rendering facing the player camera (Doom/Wolfenstein style)
//   - Hit flash and Enraged visual feedback
// Executes Phase Transitions, Enrage modes, Area Denial hazard spawning,
// and Defense-Ignoring damage against Tank players.
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using DemenciaNoLimite.Combat;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Boss
{
    public class BossController : MonoBehaviour, IDamageable, IKnockbackable
    {
        [Header("Physical Scaling & Proportions")]
        [Tooltip("Target height strictly kept under boss arena ceiling (3.2m).")]
        [SerializeField] private float _targetHeight = 2.6f;
        [Tooltip("Significantly wider diameter (X & Z) for an imposing tank-like girth.")]
        [SerializeField] private float _targetDiameter = 2.6f;

        [Header("2D Sprite & Billboard")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Transform _visualTransform;
        [SerializeField] private bool _useBillboard = true;
        [SerializeField] private bool _lockVerticalPitch = true;
        [SerializeField] private Sprite _bossSprite;
        [SerializeField] private Color _hitFlashColor = new Color(1f, 0.25f, 0.25f, 1f);

        [Header("State & Stats")]
        [SerializeField] private BossStats _activeStats;
        [SerializeField] private int _currentPhase = 1;
        [SerializeField] private bool _isEnraged = false;
        [SerializeField] private float _currentHealth;

        [Header("Attack Radii & Timers")]
        [SerializeField] private float _cleaveRadius = 4.5f;
        [SerializeField] private float _grabDistance = 2.8f;
        [SerializeField] private float _areaDenialInterval = 12f;
        private float _lastAttackTime;
        private float _lastAreaDenialTime;

        [Header("Hazard Spawn Prefab Fallback")]
        [SerializeField] private GameObject _hazardZonePrefab;

        [Header("Ranged Attack & Projectiles")]
        [Tooltip("Rango máximo de disparo hacia el jugador.")]
        [SerializeField] private float _shootingRange = 25f;
        [Tooltip("Prefab opcional para proyectil personalizado de jefe.")]
        [SerializeField] private GameObject _customBulletPrefab;
        [Tooltip("Color forzado para las balas del jefe (dejar en transparente para usar el color temático por jefe).")]
        [SerializeField] private Color _bulletColorOverride = Color.clear;
        private float _lastShootTime;

        private NavMeshAgent _agent;
        private CapsuleCollider _capsuleCollider;
        private Transform _playerTransform;
        private AdaptiveBossController _mathController;
        private Camera _mainCamera;
        private Color _originalColor = Color.white;
        private Coroutine _hitFlashCoroutine;
        private bool _isInitialized;

        public event Action<float, float> OnHealthChanged; // current, max
        public event Action<bool> OnEnrageStateChanged;
        public event Action<int> OnPhaseChanged;

        public bool IsAlive => _currentHealth > 0f;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _activeStats?.MaxHP ?? 1000f;
        public bool IsEnraged => _isEnraged;
        public int CurrentPhase => _currentPhase;
        public BossStats ActiveStats => _activeStats;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null)
            {
                _agent = gameObject.AddComponent<NavMeshAgent>();
            }

            _capsuleCollider = GetComponent<CapsuleCollider>();
            if (_capsuleCollider == null)
            {
                _capsuleCollider = gameObject.AddComponent<CapsuleCollider>();
            }

            if (_agent != null && !_agent.isOnNavMesh)
            {
                _agent.enabled = false;
            }

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1)
            {
                gameObject.layer = enemyLayer;
            }
            tag = "Enemy";

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (_visualTransform == null)
            {
                _visualTransform = _spriteRenderer != null ? _spriteRenderer.transform : transform;
            }

            if (_spriteRenderer != null)
            {
                _originalColor = _spriteRenderer.color;
                if (_spriteRenderer.sprite == null && _bossSprite != null)
                {
                    _spriteRenderer.sprite = _bossSprite;
                }
            }
        }

        public void Initialize(BossStats stats, AdaptiveBossController mathController)
        {
            _activeStats = stats;
            _mathController = mathController;
            _currentHealth = stats.MaxHP;
            _currentPhase = 1;
            _isEnraged = false;

            // Apply imposing tank proportions and calibrate physical hitbox
            ApplyTankProportionsAndHitbox();

            // Setup movement speed and navigation
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.speed = stats.MoveSpeed;
                _agent.stoppingDistance = 2.8f;
            }

            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null) _playerTransform = playerGo.transform;

            SetupBossVisuals();

            _isInitialized = true;
            OnHealthChanged?.Invoke(_currentHealth, _activeStats.MaxHP);

            Debug.Log($"[BossController] {stats.BossID} awakened with {stats.MaxHP} HP, {stats.BaseDamage} DMG, scaled to imposing dimensions (W:{_targetDiameter}m, H:{_targetHeight}m).");
        }

        /// <summary>
        /// Enforces imposing tank-like proportions (wider girth on X & Z, height under ceiling)
        /// and expands CapsuleCollider and NavMeshAgent to cover the wider girth.
        /// </summary>
        public void ApplyTankProportionsAndHitbox()
        {
            float targetRadius = _targetDiameter * 0.5f;

            // If using 2D Sprite Billboard
            if (_spriteRenderer != null && _spriteRenderer.sprite != null)
            {
                transform.localScale = Vector3.one;

                if (_capsuleCollider == null) _capsuleCollider = GetComponent<CapsuleCollider>();
                if (_capsuleCollider == null) _capsuleCollider = gameObject.AddComponent<CapsuleCollider>();

                _capsuleCollider.height = _targetHeight;
                _capsuleCollider.radius = targetRadius;
                _capsuleCollider.center = new Vector3(0f, _targetHeight * 0.5f, 0f);
                _capsuleCollider.direction = 1; // Y-Axis

                if (_agent == null) _agent = GetComponent<NavMeshAgent>();
                if (_agent != null)
                {
                    _agent.height = _targetHeight;
                    _agent.radius = targetRadius;
                    _agent.baseOffset = 0f;
                    _agent.avoidancePriority = 10;
                }
                return;
            }

            // Detect if this is a Unity primitive capsule or custom model
            bool isPrimitiveCapsule = false;
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name.Contains("Capsule"))
            {
                isPrimitiveCapsule = true;
            }

            if (isPrimitiveCapsule)
            {
                // Primitive capsule has base height 2.0 and base diameter 1.0 (radius 0.5)
                float scaleY = _targetHeight / 2.0f;
                float scaleXZ = _targetDiameter / 1.0f;
                transform.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);

                if (_capsuleCollider == null) _capsuleCollider = GetComponent<CapsuleCollider>();
                if (_capsuleCollider != null)
                {
                    _capsuleCollider.height = 2.0f;
                    _capsuleCollider.radius = 0.5f;
                    _capsuleCollider.center = Vector3.zero;
                    _capsuleCollider.direction = 1; // Y-Axis
                }
            }
            else
            {
                // Custom model: widen X & Z by 1.6x, keep Y clamped
                Vector3 currentScale = transform.localScale;
                float scaleY = (_targetHeight / 2.0f);
                float scaleXZ = currentScale.x * 1.5f;
                transform.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);

                if (_capsuleCollider == null) _capsuleCollider = GetComponent<CapsuleCollider>();
                if (_capsuleCollider == null) _capsuleCollider = gameObject.AddComponent<CapsuleCollider>();

                float localScaleY = Mathf.Max(0.001f, transform.lossyScale.y);
                float localScaleXZ = Mathf.Max(0.001f, Mathf.Max(transform.lossyScale.x, transform.lossyScale.z));

                _capsuleCollider.height = _targetHeight / localScaleY;
                _capsuleCollider.radius = targetRadius / localScaleXZ;
                _capsuleCollider.direction = 1;
            }

            // Synchronize NavMeshAgent so wider bosses navigate without getting stuck
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
            {
                _agent.height = _targetHeight;
                _agent.radius = targetRadius;
                _agent.baseOffset = 0f;
                _agent.avoidancePriority = 10;
            }
        }

        private void SetupBossVisuals()
        {
            if (_spriteRenderer != null && _spriteRenderer.sprite != null)
            {
                // 2D Billboard boss active: do not create procedural primitive geometry
                return;
            }

            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                rend.material.color = new Color(0.40f, 0.04f, 0.08f); // Demonic obsidian-red flesh
            }

            // Remove existing attachments if re-initializing
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("Boss_Attachment_"))
                {
                    Destroy(child.gameObject);
                }
            }

            // 1. Massive Armored Shoulder Pauldron (Left)
            var pauldronL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pauldronL.name = "Boss_Attachment_Pauldron_L";
            pauldronL.transform.parent = transform;
            pauldronL.transform.localPosition = new Vector3(-0.45f, 0.45f, 0f);
            pauldronL.transform.localRotation = Quaternion.Euler(0f, 0f, 15f);
            pauldronL.transform.localScale = new Vector3(0.35f, 0.4f, 0.6f);
            var colPL = pauldronL.GetComponent<Collider>();
            if (colPL != null) Destroy(colPL);
            var rendPL = pauldronL.GetComponent<Renderer>();
            if (rendPL != null) rendPL.material.color = new Color(0.15f, 0.16f, 0.18f); // Heavy Iron

            // 2. Massive Armored Shoulder Pauldron (Right)
            var pauldronR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pauldronR.name = "Boss_Attachment_Pauldron_R";
            pauldronR.transform.parent = transform;
            pauldronR.transform.localPosition = new Vector3(0.45f, 0.45f, 0f);
            pauldronR.transform.localRotation = Quaternion.Euler(0f, 0f, -15f);
            pauldronR.transform.localScale = new Vector3(0.35f, 0.4f, 0.6f);
            var colPR = pauldronR.GetComponent<Collider>();
            if (colPR != null) Destroy(colPR);
            var rendPR = pauldronR.GetComponent<Renderer>();
            if (rendPR != null) rendPR.material.color = new Color(0.15f, 0.16f, 0.18f);

            // 3. Spiked Horns
            var hornL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hornL.name = "Boss_Attachment_Horn_L";
            hornL.transform.parent = transform;
            hornL.transform.localPosition = new Vector3(-0.25f, 0.85f, 0.15f);
            hornL.transform.localRotation = Quaternion.Euler(-25f, -20f, 0f);
            hornL.transform.localScale = new Vector3(0.15f, 0.5f, 0.15f);
            var colHL = hornL.GetComponent<Collider>();
            if (colHL != null) Destroy(colHL);
            var rendHL = hornL.GetComponent<Renderer>();
            if (rendHL != null) rendHL.material.color = Color.black;

            var hornR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hornR.name = "Boss_Attachment_Horn_R";
            hornR.transform.parent = transform;
            hornR.transform.localPosition = new Vector3(0.25f, 0.85f, 0.15f);
            hornR.transform.localRotation = Quaternion.Euler(-25f, 20f, 0f);
            hornR.transform.localScale = new Vector3(0.15f, 0.5f, 0.15f);
            var colHR = hornR.GetComponent<Collider>();
            if (colHR != null) Destroy(colHR);
            var rendHR = hornR.GetComponent<Renderer>();
            if (rendHR != null) rendHR.material.color = Color.black;

            // 4. Glowing Demonic Core
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Boss_Attachment_Core";
            core.transform.parent = transform;
            core.transform.localPosition = new Vector3(0f, 0.2f, 0.42f);
            core.transform.localScale = new Vector3(0.25f, 0.25f, 0.15f);
            var colCore = core.GetComponent<Collider>();
            if (colCore != null) Destroy(colCore);
            var rendCore = core.GetComponent<Renderer>();
            if (rendCore != null) rendCore.material.color = new Color(1.0f, 0.1f, 0.1f);
        }

        private void Update()
        {
            if (!_isInitialized || !IsAlive) return;

            if (_playerTransform == null)
            {
                var playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo != null) _playerTransform = playerGo.transform;
                return;
            }

            CheckEnrageCondition();

            float distToPlayer = Vector3.Distance(transform.position, _playerTransform.position);

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.speed = _mathController != null 
                    ? _mathController.GetBossMoveSpeed(_isEnraged) 
                    : _activeStats.MoveSpeed;

                _agent.SetDestination(_playerTransform.position);
            }
            else
            {
                Vector3 toPlayer = (_playerTransform.position - transform.position);
                toPlayer.y = 0f;
                float speed = _mathController != null 
                    ? _mathController.GetBossMoveSpeed(_isEnraged) 
                    : (_activeStats != null ? _activeStats.MoveSpeed : 3.5f);

                transform.position += toPlayer.normalized * (speed * Time.deltaTime);

                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toPlayer.normalized), Time.deltaTime * 5f);
                }
            }

            // Area denial special
            if (_activeStats.HasAreaDenial && Time.time - _lastAreaDenialTime >= _areaDenialInterval)
            {
                ExecuteAreaDenial();
            }

            // Grab attack against aggressive close players
            if (_activeStats.HasGrabAttack && distToPlayer <= _grabDistance && Time.time - _lastAttackTime >= _activeStats.AttackSpeed * 1.5f)
            {
                ExecuteGrabAttack();
            }
            // Standard cleave attack
            else if (distToPlayer <= _cleaveRadius && Time.time - _lastAttackTime >= (1f / Mathf.Max(0.1f, _activeStats.AttackSpeed)))
            {
                ExecuteCleaveAttack();
            }
            // Ranged projectile attack when player is at distance
            else if (distToPlayer <= _shootingRange && Time.time - _lastShootTime >= GetCurrentShootCooldown())
            {
                ExecuteRangedShoot(distToPlayer);
            }
        }

        private void LateUpdate()
        {
            if (_useBillboard)
            {
                UpdateBillboardOrientation();
            }
        }

        private void UpdateBillboardOrientation()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            Transform target = _visualTransform != null ? _visualTransform : (_spriteRenderer != null ? _spriteRenderer.transform : transform);

            if (_lockVerticalPitch)
            {
                // Cylindrical billboard (Y-axis upright only): sprite does not tilt vertically
                Vector3 camForward = _mainCamera.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.001f)
                {
                    target.rotation = Quaternion.LookRotation(camForward.normalized);
                }
            }
            else
            {
                // Full spherical billboard
                target.rotation = _mainCamera.transform.rotation;
            }
        }

        private void CheckEnrageCondition()
        {
            if (_isEnraged) return;

            if (_mathController != null && _mathController.ShouldEnrage(_currentHealth))
            {
                TriggerEnrage();
            }
        }

        private void TriggerEnrage()
        {
            _isEnraged = true;
            Debug.Log($"[BossController] {_activeStats.BossID} ENRAGED! Damage and speed drastically amplified!");
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1f, 0.45f, 0.45f, 1f);
            }
            OnEnrageStateChanged?.Invoke(true);
        }

        private float GetCurrentShootCooldown()
        {
            float baseCooldown = 1f / Mathf.Max(0.1f, _activeStats != null ? _activeStats.AttackSpeed * 0.75f : 0.8f);
            return _isEnraged ? baseCooldown * 0.65f : baseCooldown;
        }

        private Vector3 GetBulletSpawnOrigin()
        {
            Vector3 toPlayer = _playerTransform != null 
                ? (_playerTransform.position - transform.position) 
                : transform.forward;
            toPlayer.y = 0f;
            Vector3 dir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : transform.forward;

            float spawnHeight = _targetHeight * 0.55f;
            float forwardOffset = (_targetDiameter * 0.5f) + 0.4f;
            return transform.position + Vector3.up * spawnHeight + dir * forwardOffset;
        }

        private void ExecuteRangedShoot(float distToPlayer)
        {
            _lastShootTime = Time.time;
            _lastAttackTime = Time.time;

            string bossId = _activeStats != null && !string.IsNullOrEmpty(_activeStats.BossID) 
                ? _activeStats.BossID 
                : gameObject.name;

            if (bossId.Contains("CARNICERO") || bossId.Contains("Carnicero"))
            {
                // Carnicero: Escopeta de perdigones incandescentes
                int pellets = _isEnraged ? 6 : 4;
                float damage = (_activeStats != null ? _activeStats.BaseDamage : 15f) * (_isEnraged ? 0.45f : 0.35f);
                Color fireColor = _bulletColorOverride != Color.clear ? _bulletColorOverride : new Color(1f, 0.50f, 0.05f);
                FireShotgunSpread(pellets, 12f, damage, fireColor, 22f, 0.32f, 4.5f);
                Debug.Log($"[BossController] {bossId} dispara ráfaga de escopeta ({pellets} perdigones ígneos)!");
            }
            else if (bossId.Contains("MAQUINA") || bossId.Contains("Maquina"))
            {
                // Maquina: Ráfaga rápida de bio-plasma desde los múltiples escapes/boca
                int bursts = _isEnraged ? 5 : 3;
                float damage = (_activeStats != null ? _activeStats.BaseDamage : 25f) * 0.42f;
                Color toxicColor = _bulletColorOverride != Color.clear ? _bulletColorOverride : new Color(0.1f, 1f, 0.35f);
                StartCoroutine(FireBurstRoutine(bursts, 0.12f, damage, toxicColor, 25f, 0.30f, false, 3.5f));
                Debug.Log($"[BossController] {bossId} dispara ráfaga de plasma mecánico ({bursts} proyectiles tóxicos)!");
            }
            else if (bossId.Contains("DEMENCIA") || bossId.Contains("Demencia"))
            {
                // Demencia: Proyectil perforante de revólver maldito
                int shots = _isEnraged ? 2 : 1;
                float damage = (_activeStats != null ? _activeStats.BaseDamage : 40f) * 1.25f;
                Color crimsonColor = _bulletColorOverride != Color.clear ? _bulletColorOverride : new Color(1f, 0.08f, 0.20f);
                StartCoroutine(FireBurstRoutine(shots, 0.25f, damage, crimsonColor, 28f, 0.38f, true, 8f));
                Debug.Log($"[BossController] {bossId} dispara tiro directo de revólver maldito ({shots} bala pesada)!");
            }
            else
            {
                // Genérico / Default Boss
                float damage = _activeStats != null ? _activeStats.BaseDamage * 0.7f : 15f;
                Vector3 spawnOrigin = GetBulletSpawnOrigin();
                Vector3 targetChest = _playerTransform.position + Vector3.up * 1.0f;
                Vector3 fireDir = (targetChest - spawnOrigin).normalized;
                Color defColor = _bulletColorOverride != Color.clear ? _bulletColorOverride : new Color(1f, 0.2f, 0.2f);
                SpawnBossBullet(spawnOrigin, fireDir, 22f, damage, defColor, 0.35f, 0.25f, false, 5f);
            }
        }

        private void FireShotgunSpread(int count, float spreadAngle, float damage, Color color, float speed, float radius, float knockback)
        {
            Vector3 spawnOrigin = GetBulletSpawnOrigin();
            Vector3 targetChest = _playerTransform.position + Vector3.up * 1.0f;
            Vector3 baseDir = (targetChest - spawnOrigin).normalized;

            for (int i = 0; i < count; i++)
            {
                float yaw = UnityEngine.Random.Range(-spreadAngle, spreadAngle);
                float pitch = UnityEngine.Random.Range(-spreadAngle * 0.5f, spreadAngle * 0.5f);
                Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
                Vector3 fireDir = rot * baseDir;

                SpawnBossBullet(
                    spawnOrigin, 
                    fireDir, 
                    speed: speed + UnityEngine.Random.Range(-2f, 2f), 
                    damage: damage, 
                    bulletColor: color, 
                    radius: radius, 
                    trailWidth: radius * 0.8f,
                    ignoreDef: false,
                    knockback: knockback
                );
            }
        }

        private IEnumerator FireBurstRoutine(int count, float interval, float damage, Color color, float speed, float radius, bool ignoreDef = false, float knockback = 5f)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsAlive || _playerTransform == null) yield break;

                Vector3 spawnOrigin = GetBulletSpawnOrigin();
                Vector3 targetChest = _playerTransform.position + Vector3.up * 1.0f;
                Vector3 fireDir = (targetChest - spawnOrigin).normalized;

                if (i > 0)
                {
                    Vector3 jitter = UnityEngine.Random.insideUnitSphere * 0.04f;
                    fireDir = (fireDir + jitter).normalized;
                }

                SpawnBossBullet(spawnOrigin, fireDir, speed, damage, color, radius, radius * 0.85f, ignoreDef, knockback);

                if (i < count - 1)
                {
                    yield return new WaitForSeconds(interval);
                }
            }
        }

        private GameObject SpawnBossBullet(
            Vector3 origin, 
            Vector3 direction, 
            float speed, 
            float damage, 
            Color bulletColor, 
            float radius = 0.32f, 
            float trailWidth = 0.22f, 
            bool ignoreDef = false, 
            float knockback = 5f)
        {
            if (_customBulletPrefab != null)
            {
                var customObj = Instantiate(_customBulletPrefab, origin, Quaternion.LookRotation(direction));
                var p = customObj.GetComponent<Projectile>() ?? customObj.AddComponent<Projectile>();
                DamageInfo dInfo = new DamageInfo(damage, gameObject, origin, Vector3.up, direction, DamageSourceType.BossSpecial, _isEnraged, false, ignoreDef, knockback);
                p.Initialize(dInfo, direction, speed);
                return customObj;
            }

            // 1. Instanciar proyectil esférico de alto contraste visual
            GameObject bulletObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletObj.name = "Boss_Bullet";
            bulletObj.transform.position = origin;
            bulletObj.transform.rotation = Quaternion.LookRotation(direction);
            bulletObj.transform.localScale = Vector3.one * radius;

            // Trigger para no empujar físicas erráticas; Projectile realiza SphereCast cinemático
            var col = bulletObj.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            // 2. Material unlit/emisivo brillante y visible en la oscuridad
            var rend = bulletObj.GetComponent<Renderer>();
            Material bulletMat = null;
            if (rend != null)
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") 
                                  ?? Shader.Find("Sprites/Default") 
                                  ?? Shader.Find("Unlit/Color");
                if (unlitShader != null)
                {
                    bulletMat = new Material(unlitShader);
                    if (bulletMat.HasProperty("_BaseColor")) bulletMat.SetColor("_BaseColor", bulletColor);
                    if (bulletMat.HasProperty("_Color")) bulletMat.SetColor("_Color", bulletColor);
                    if (bulletMat.HasProperty("_EmissionColor"))
                    {
                        bulletMat.EnableKeyword("_EMISSION");
                        bulletMat.SetColor("_EmissionColor", bulletColor * 3.5f);
                    }
                    rend.material = bulletMat;
                }
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            // 3. TrailRenderer dinámico para que la trayectoria de la bala sea 100% visible
            var trail = bulletObj.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.startWidth = trailWidth;
            trail.endWidth = 0.03f;
            trail.material = bulletMat != null ? bulletMat : (rend != null ? rend.material : new Material(Shader.Find("Sprites/Default")));

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(bulletColor, 0.0f), new GradientColorKey(bulletColor, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.95f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
            );
            trail.colorGradient = gradient;

            // 4. Luz puntual que proyecta resplandor en paredes y suelo en tiempo real
            var light = bulletObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = bulletColor;
            light.range = 5.5f;
            light.intensity = 2.5f;

            // 5. Componente Projectile cinemático balístico
            var projectile = bulletObj.AddComponent<Projectile>();

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int mask = ~0;
            if (enemyLayer != -1) mask &= ~(1 << enemyLayer);
            projectile.SetCollisionMask(mask);

            DamageInfo damageInfo = new DamageInfo(
                amount: damage,
                attacker: gameObject,
                hitPoint: origin,
                hitNormal: Vector3.up,
                direction: direction,
                sourceType: DamageSourceType.BossSpecial,
                isCritical: _isEnraged,
                isHeadshot: false,
                ignoreDefense: ignoreDef || (_activeStats != null && _activeStats.DefenseIgnoreRatio > 0.4f),
                knockbackForce: knockback
            );

            projectile.Initialize(damageInfo, direction, speed, gravity: 0f, explosionRadius: 0f);

            return bulletObj;
        }

        private void ExecuteCleaveAttack()
        {
            _lastAttackTime = Time.time;

            PlayerStats playerStats = GameManager.Instance?.Player;
            float playerDef = playerStats != null ? playerStats.DamageReduction : 0f;

            float damage = _mathController != null 
                ? _mathController.CalculateBossDamage(_activeStats.BaseDamage, playerDef, _isEnraged)
                : _activeStats.BaseDamage;

            IDamageable player = _playerTransform.GetComponentInParent<IDamageable>();
            if (player != null && player.IsAlive)
            {
                Vector3 dir = (_playerTransform.position - transform.position).normalized;
                DamageInfo info = new DamageInfo(
                    amount: damage,
                    attacker: gameObject,
                    hitPoint: _playerTransform.position,
                    direction: dir,
                    sourceType: DamageSourceType.BossSpecial,
                    isCritical: _isEnraged,
                    ignoreDefense: _activeStats.DefenseIgnoreRatio > 0.4f,
                    knockbackForce: _isEnraged ? 10f : 5f
                );

                player.TakeDamage(info);
            }
        }

        private void ExecuteGrabAttack()
        {
            _lastAttackTime = Time.time;
            Debug.Log($"[BossController] {_activeStats.BossID} executes DEVASTATING GRAB attack!");

            IDamageable player = _playerTransform.GetComponentInParent<IDamageable>();
            if (player != null && player.IsAlive)
            {
                float grabDamage = _activeStats.BaseDamage * 1.8f;
                DamageInfo info = new DamageInfo(
                    amount: grabDamage,
                    attacker: gameObject,
                    hitPoint: _playerTransform.position,
                    direction: (_playerTransform.position - transform.position).normalized,
                    sourceType: DamageSourceType.BossSpecial,
                    isCritical: true,
                    ignoreDefense: true,
                    knockbackForce: 12f
                );

                player.TakeDamage(info);
            }
        }

        private void ExecuteAreaDenial()
        {
            _lastAreaDenialTime = Time.time;
            Debug.Log($"[BossController] {_activeStats.BossID} saturates the arena with corrupted miasma!");

            Vector3 spawnPoint = _playerTransform.position;
            if (_hazardZonePrefab != null)
            {
                Instantiate(_hazardZonePrefab, spawnPoint, Quaternion.identity);
            }
            else
            {
                var hazard = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                hazard.transform.position = spawnPoint;
                hazard.transform.localScale = new Vector3(5f, 0.1f, 5f);
                var col = hazard.GetComponent<Collider>();
                if (col != null) col.isTrigger = true;
                Destroy(hazard, 10f);
            }
        }

        public float TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return 0f;

            _currentHealth -= info.Amount;
            OnHealthChanged?.Invoke(_currentHealth, _activeStats.MaxHP);

            if (_spriteRenderer != null)
            {
                if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);
                _hitFlashCoroutine = StartCoroutine(HitFlashRoutine());
            }

            CheckPhaseTransition();

            if (_currentHealth <= 0f)
            {
                Die();
            }

            return info.Amount;
        }

        public void TakeDamage(float amount)
        {
            TakeDamage(new DamageInfo(amount: amount, attacker: null, sourceType: DamageSourceType.Bullet));
        }

        private IEnumerator HitFlashRoutine()
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = _hitFlashColor;
                yield return new WaitForSeconds(0.08f);
                _spriteRenderer.color = _isEnraged ? new Color(1f, 0.45f, 0.45f, 1f) : _originalColor;
            }
            _hitFlashCoroutine = null;
        }

        private void CheckPhaseTransition()
        {
            if (_activeStats.PhaseCount <= 1) return;

            float hpRatio = _currentHealth / _activeStats.MaxHP;

            if (_activeStats.PhaseCount == 2 && hpRatio <= 0.5f && _currentPhase == 1)
            {
                TransitionToPhase(2);
            }
            else if (_activeStats.PhaseCount == 3)
            {
                if (hpRatio <= 0.66f && _currentPhase == 1) TransitionToPhase(2);
                else if (hpRatio <= 0.33f && _currentPhase == 2) TransitionToPhase(3);
            }
        }

        private void TransitionToPhase(int newPhase)
        {
            _currentPhase = newPhase;
            Debug.Log($"[BossController] {_activeStats.BossID} entering PHASE {newPhase}!");
            OnPhaseChanged?.Invoke(newPhase);

            _activeStats.BaseDamage *= 1.25f;
            _activeStats.AttackSpeed *= 1.15f;
        }

        public void ApplyKnockback(Vector3 force)
        {
            // Bosses are immune to direct knockback
        }

        private void Die()
        {
            _currentHealth = 0f;
            OnHealthChanged?.Invoke(0f, _activeStats.MaxHP);

            Debug.Log($"[BossController] {_activeStats.BossID} SLAIN! The path forward opens!");

            GameEvents.FireBossDefeated(_activeStats.BossID);

            Destroy(gameObject, 1.5f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (_visualTransform == null && _spriteRenderer != null)
            {
                _visualTransform = _spriteRenderer.transform;
            }

            if (_spriteRenderer != null && _spriteRenderer.sprite == null && _bossSprite != null)
            {
                _spriteRenderer.sprite = _bossSprite;
            }
        }
#endif
    }
}
