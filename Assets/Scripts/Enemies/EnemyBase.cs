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

    [RequireComponent(typeof(NavMeshAgent))]
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

        protected virtual void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
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

            _maxHealth = _data.BaseHP * hpMult;
            _currentHealth = _maxHealth;

            if (_agent != null)
            {
                _agent.speed = _data.MoveSpeed * _speedMult;
                _agent.stoppingDistance = _data.AttackRange * 0.8f;
            }

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

            // Debris throw mutator chance while chasing
            if (_canThrowDebris && distToPlayer > _data.AttackRange * 1.5f && Time.time - _lastAttackTime > _data.AttackCooldown * 1.5f)
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

            // Mutator: Explode on death
            if (_explodesOnDeath)
            {
                TriggerDeathExplosion();
            }

            // Drop currency & loot
            DropLoot();

            // Notify systems
            GameEvents.FireEnemyKilled(_data != null ? _data.EnemyID : "Grunt");

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

        protected virtual void TransitionToState(AIState newState)
        {
            _currentState = newState;
        }
    }
}
