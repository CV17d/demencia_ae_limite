// ============================================================================
// EnemyBase.cs — Modular AI Finite State Machine & Combat Entity
// ============================================================================
// Drives Idle, Patrol, Chase, Attack, and Flee states.
// Integrates EnemyData base stats with EnemyBuffState mutators from Cursed Pacts:
//   - Revive once
//   - Explode on death
//   - Dodge incoming attacks
//   - Throw debris
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using DemenciaNoLimite.Combat;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Enemies
{
    public enum AIState
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        Flee,
        Stunned,
        Dead
    }

    public class EnemyBase : MonoBehaviour, IDamageable, IKnockbackable
    {
        [Header("Data Definition")]
        [SerializeField] protected EnemyData _data;

        [Header("AI State")]
        [SerializeField] protected AIState _currentState = AIState.Idle;

        [Header("Combat Layers")]
        [SerializeField] protected LayerMask _playerLayer = ~0;

        protected NavMeshAgent _agent;
        protected Transform _playerTarget;
        protected float _currentHealth;
        protected float _maxHealth;
        protected float _lastAttackTime;
        protected bool _hasRevivedOnce;
        protected bool _isInitialized;

        // Buff overrides from EnemyBuffState
        protected float _damageMult = 1f;
        protected float _speedMult = 1f;
        protected bool _canThrowDebris;
        protected bool _canReviveOnce;
        protected bool _explodesOnDeath;
        protected bool _canDodge;

        public bool IsAlive => _currentState != AIState.Dead && _currentHealth > 0f;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public EnemyData Data => _data;

        [Header("Hitbox & Scale Configuration")]
        [Tooltip("Standard model height (meters) for indoor ceiling compatibility (typically 1.75m - 2.0m).")]
        [SerializeField] protected float _targetHeight = 1.8f;
        [SerializeField] protected float _targetRadius = 0.38f;
        [SerializeField] protected bool _autoFitColliderOnAwake = true;

        protected virtual void Awake()
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1 && gameObject.layer != enemyLayer)
            {
                gameObject.layer = enemyLayer;
            }

            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null && !_agent.isOnNavMesh)
            {
                _agent.enabled = false;
            }

            if (_autoFitColliderOnAwake)
            {
                FitHitboxToModel();
            }
        }

        /// <summary>
        /// Calibrates CapsuleCollider and NavMeshAgent to accurately match visual mesh bounds.
        /// Prevents invisible hitboxes and ceiling/doorway collisions.
        /// </summary>
        [ContextMenu("Fit Hitbox To Model")]
        public void FitHitboxToModel()
        {
            var cap = GetComponent<CapsuleCollider>();
            if (cap == null) cap = gameObject.AddComponent<CapsuleCollider>();

            var renderers = GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(transform.position, Vector3.zero);
            bool foundRenderer = false;
            foreach (var r in renderers)
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    if (!foundRenderer) { b = r.bounds; foundRenderer = true; }
                    else { b.Encapsulate(r.bounds); }
                }
            }

            if (foundRenderer)
            {
                Vector3 localMin = transform.InverseTransformPoint(b.min);
                Vector3 localMax = transform.InverseTransformPoint(b.max);
                float localHeight = Mathf.Abs(localMax.y - localMin.y);
                float localRadius = Mathf.Max(Mathf.Abs(localMax.x - localMin.x), Mathf.Abs(localMax.z - localMin.z)) * 0.45f;
                float localCenterY = (localMax.y + localMin.y) * 0.5f;

                cap.height = Mathf.Max(0.5f, localHeight);
                cap.radius = Mathf.Clamp(localRadius, 0.25f, 0.55f);
                cap.center = new Vector3(0f, localCenterY, 0f);
                cap.direction = 1; // Y-Axis
            }
            else
            {
                cap.height = _targetHeight;
                cap.radius = _targetRadius;
                cap.center = new Vector3(0f, _targetHeight * 0.5f, 0f);
                cap.direction = 1;
            }

            if (_agent != null)
            {
                _agent.height = cap.height * transform.lossyScale.y;
                _agent.radius = cap.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            }
        }


        public virtual void Initialize(EnemyData data, EnemyBuffState buffState)
        {
            _data = data;
            
            // Apply pact mutators
            float hpMult = buffState != null ? buffState.HPMultiplier : 1f;
            _damageMult = buffState != null ? buffState.DamageMultiplier : 1f;
            _speedMult = buffState != null ? buffState.SpeedMultiplier : 1f;

            _canThrowDebris = buffState != null && buffState.CanThrowDebris;
            _canReviveOnce = buffState != null && buffState.CanReviveOnce;
            _explodesOnDeath = buffState != null && buffState.ExplodesOnDeath;
            _canDodge = buffState != null && buffState.CanDodgeProjectiles;

            _maxHealth = (_data != null ? _data.BaseHP : 50f) * hpMult;
            _currentHealth = _maxHealth;

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.speed = (_data != null ? _data.MoveSpeed : 3.5f) * _speedMult;
                _agent.stoppingDistance = (_data != null ? _data.AttackRange : 2f) * 0.8f;
            }

            SetupVisualIdentity();

            // Find player
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null)
            {
                _playerTarget = playerGo.transform;
            }

            _currentState = AIState.Idle;
            _isInitialized = true;
        }

        protected virtual void Update()
        {
            if (!_isInitialized || _currentState == AIState.Dead) return;

            // Target search fallback
            if (_playerTarget == null)
            {
                var playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo != null) _playerTarget = playerGo.transform;
                return;
            }

            float distToPlayer = Vector3.Distance(transform.position, _playerTarget.position);

            switch (_currentState)
            {
                case AIState.Idle:
                    UpdateIdle(distToPlayer);
                    break;
                case AIState.Patrol:
                    UpdatePatrol(distToPlayer);
                    break;
                case AIState.Chase:
                    UpdateChase(distToPlayer);
                    break;
                case AIState.Attack:
                    UpdateAttack(distToPlayer);
                    break;
                case AIState.Flee:
                    UpdateFlee(distToPlayer);
                    break;
            }
        }

        protected virtual void UpdateIdle(float distToPlayer)
        {
            if (distToPlayer <= _data.DetectionRange)
            {
                TransitionToState(AIState.Chase);
            }
        }

        protected virtual void UpdatePatrol(float distToPlayer)
        {
            if (distToPlayer <= _data.DetectionRange)
            {
                TransitionToState(AIState.Chase);
            }
        }

        protected virtual void UpdateChase(float distToPlayer)
        {
            // Flee condition
            if (_data.FleesAtLowHP && (_currentHealth / _maxHealth) <= _data.FleeHPThreshold)
            {
                TransitionToState(AIState.Flee);
                return;
            }

            if (distToPlayer <= _data.AttackRange)
            {
                TransitionToState(AIState.Attack);
                return;
            }

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(_playerTarget.position);
            }
            else
            {
                // Fallback direct pursuit with obstacle sliding
                Vector3 toPlayer = (_playerTarget.position - transform.position);
                toPlayer.y = 0f;
                Vector3 moveDir = toPlayer.normalized;

                if (Physics.Raycast(transform.position + Vector3.up * 0.5f, moveDir, out RaycastHit wallHit, 1.2f))
                {
                    moveDir = Vector3.ProjectOnPlane(moveDir, wallHit.normal).normalized;
                }

                float speed = (_data != null ? _data.MoveSpeed : 3.5f) * _speedMult;
                transform.position += moveDir * (speed * Time.deltaTime);

                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toPlayer.normalized), Time.deltaTime * 6f);
                }
            }

            // Debris throw mutator chance while chasing
            if (_canThrowDebris && distToPlayer > (_data != null ? _data.AttackRange : 2f) * 1.5f && Time.time - _lastAttackTime > (_data != null ? _data.AttackCooldown : 1.5f) * 1.5f)
            {
                TryThrowDebris();
            }
        }

        protected virtual void UpdateAttack(float distToPlayer)
        {
            if (distToPlayer > _data.AttackRange * 1.2f)
            {
                TransitionToState(AIState.Chase);
                return;
            }

            // Face player
            Vector3 lookDir = (_playerTarget.position - transform.position).normalized;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 8f);
            }

            if (Time.time - _lastAttackTime >= _data.AttackCooldown)
            {
                ExecuteAttack();
                _lastAttackTime = Time.time;
            }
        }

        protected virtual void UpdateFlee(float distToPlayer)
        {
            Vector3 awayDir = (transform.position - _playerTarget.position).normalized;
            Vector3 fleePos = transform.position + awayDir * 8f;

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(fleePos);
            }
        }

        protected virtual void ExecuteAttack()
        {
            IDamageable playerDamageable = _playerTarget.GetComponentInParent<IDamageable>();
            if (playerDamageable != null && playerDamageable.IsAlive)
            {
                float dmg = _data.BaseDamage * _damageMult;
                Vector3 dir = (_playerTarget.position - transform.position).normalized;

                DamageInfo info = new DamageInfo(
                    amount: dmg,
                    attacker: gameObject,
                    hitPoint: _playerTarget.position,
                    hitNormal: -dir,
                    direction: dir,
                    sourceType: _data.HasRangedAttack ? DamageSourceType.Bullet : DamageSourceType.Melee,
                    isCritical: false,
                    isHeadshot: false,
                    ignoreDefense: false,
                    knockbackForce: 3f
                );

                playerDamageable.TakeDamage(info);
            }
        }

        protected virtual void TryThrowDebris()
        {
            _lastAttackTime = Time.time;
            Debug.Log($"[EnemyBase] {gameObject.name} throws concrete debris due to Cursed Pact!");

            Vector3 throwOrigin = transform.position + Vector3.up * 1.2f;
            Vector3 throwDir = (_playerTarget.position + Vector3.up * 0.5f - throwOrigin).normalized;

            var debris = GameObject.CreatePrimitive(PrimitiveType.Cube);
            debris.transform.position = throwOrigin;
            debris.transform.localScale = Vector3.one * 0.4f;

            var proj = debris.AddComponent<Projectile>();
            DamageInfo info = new DamageInfo(
                amount: _data.BaseDamage * 1.2f * _damageMult,
                attacker: gameObject,
                direction: throwDir,
                sourceType: DamageSourceType.Melee,
                knockbackForce: 6f
            );
            proj.Initialize(info, throwDir, 18f, 9.8f, 0f);
        }

        public virtual float TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return 0f;

            // Pact mutator: Dodge chance
            if (_canDodge && Random.value < 0.25f && _currentState == AIState.Chase)
            {
                ExecuteDodge();
                return 0f;
            }

            _currentHealth -= info.Amount;

            if (_currentState == AIState.Idle)
            {
                TransitionToState(AIState.Chase);
            }

            if (_currentHealth <= 0f)
            {
                if (_canReviveOnce && !_hasRevivedOnce)
                {
                    Revive();
                }
                else
                {
                    Die();
                }
            }

            return info.Amount;
        }

        protected virtual void ExecuteDodge()
        {
            Vector3 side = transform.right * (Random.value > 0.5f ? 1f : -1f) * 3f;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.Move(side);
            }
        }

        protected virtual void Revive()
        {
            _hasRevivedOnce = true;
            _currentHealth = _maxHealth * 0.5f;
            Debug.Log($"[EnemyBase] {gameObject.name} resurrected by Cursed Pact demonic surge!");
        }

        public virtual void ApplyKnockback(Vector3 force)
        {
            if (_agent != null && _agent.isOnNavMesh && !_data.ImmuneToKnockback)
            {
                _agent.Move(force * Time.deltaTime);
            }
        }

        protected virtual void Die()
        {
            _currentState = AIState.Dead;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.enabled = false;
            }

            // Disable colliders immediately so dead enemies don't block bullets or player pathing
            var colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            // Mutator: Explode on death
            if (_explodesOnDeath)
            {
                TriggerDeathExplosion();
            }

            // Drop currency & loot
            DropLoot();

            // Notify systems
            GameEvents.FireEnemyKilled(transform.position);

            Destroy(gameObject, 0.2f);
        }

        protected virtual void TriggerDeathExplosion()
        {
            Vector3 pos = transform.position;
            Collider[] hits = Physics.OverlapSphere(pos, 3.5f, _playerLayer);
            foreach (var col in hits)
            {
                IDamageable d = col.GetComponentInParent<IDamageable>();
                if (d != null && d.IsAlive)
                {
                    DamageInfo info = new DamageInfo(
                        amount: _data.BaseDamage * 1.5f,
                        attacker: gameObject,
                        hitPoint: col.transform.position,
                        direction: (col.transform.position - pos).normalized,
                        sourceType: DamageSourceType.Explosion,
                        knockbackForce: 8f
                    );
                    d.TakeDamage(info);
                }
            }
        }

        protected virtual void DropLoot()
        {
            if (_data == null) return;
            int currency = _data.RollCurrencyDrop();
            if (currency > 0 && GameManager.Instance != null)
            {
                GameManager.Instance.Currency += currency;
            }
        }

        protected virtual void SetupVisualIdentity()
        {
            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                rend.material.color = new Color(0.85f, 0.12f, 0.15f); // Crimson mutant flesh
            }

            // Glowing yellow eyes
            var eyeL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eyeL.name = "Eye_L";
            eyeL.transform.parent = transform;
            eyeL.transform.localPosition = new Vector3(-0.15f, 0.6f, 0.35f);
            eyeL.transform.localScale = new Vector3(0.1f, 0.06f, 0.1f);
            var colL = eyeL.GetComponent<Collider>();
            if (colL != null) Destroy(colL);
            var rendL = eyeL.GetComponent<Renderer>();
            if (rendL != null) rendL.material.color = Color.yellow;

            var eyeR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eyeR.name = "Eye_R";
            eyeR.transform.parent = transform;
            eyeR.transform.localPosition = new Vector3(0.15f, 0.6f, 0.35f);
            eyeR.transform.localScale = new Vector3(0.1f, 0.06f, 0.1f);
            var colR = eyeR.GetComponent<Collider>();
            if (colR != null) Destroy(colR);
            var rendR = eyeR.GetComponent<Renderer>();
            if (rendR != null) rendR.material.color = Color.yellow;
        }

        protected virtual void OnGUI()
        {
            if (!IsAlive) return;

            var cam = Camera.main;
            if (cam == null) return;

            var cap = GetComponent<CapsuleCollider>();
            float headY = (cap != null)
                ? (cap.center.y + cap.height * 0.5f) * transform.lossyScale.y
                : (_targetHeight > 0 ? _targetHeight : 1.8f);

            Vector3 headPos = transform.position + Vector3.up * (headY + 0.2f);
            Vector3 screenPos = cam.WorldToScreenPoint(headPos);


            if (screenPos.z < 0.5f || screenPos.z > 30f) return;

            float barW = 70f;
            float barH = 8f;
            float x = screenPos.x - (barW * 0.5f);
            float y = Screen.height - screenPos.y;

            float alpha = Mathf.Clamp01(1f - (screenPos.z / 30f));

            GUI.color = new Color(0.1f, 0.1f, 0.1f, alpha * 0.85f);
            GUI.DrawTexture(new Rect(x, y, barW, barH), Texture2D.whiteTexture);

            float fill = Mathf.Clamp01(_currentHealth / Mathf.Max(1f, _maxHealth));
            GUI.color = new Color(0.95f, 0.15f, 0.2f, alpha);
            GUI.DrawTexture(new Rect(x, y, barW * fill, barH), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 0.9f, 0.9f, alpha);
            string enemyName = _data != null ? _data.DisplayName : "MUTANTE";
            GUI.Label(new Rect(x - 20, y - 18, barW + 40, 18), $"<size=11><b>{enemyName}</b></size>");
            GUI.color = Color.white;
        }

        protected virtual void TransitionToState(AIState newState)
        {
            _currentState = newState;
        }
    }
}
