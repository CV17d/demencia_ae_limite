// ============================================================================
// BossController.cs — Adaptive Boss Entity & Combat Execution
// ============================================================================
// Realizes the BossStats calculated by AdaptiveBossController.
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
        [Header("State & Stats")]
        [SerializeField] private BossStats _activeStats;
        [SerializeField] private int _currentPhase = 1;
        [SerializeField] private bool _isEnraged = false;
        [SerializeField] private float _currentHealth;

        [Header("Attack Radii & Timers")]
        [SerializeField] private float _cleaveRadius = 4.0f;
        [SerializeField] private float _grabDistance = 2.2f;
        [SerializeField] private float _areaDenialInterval = 12f;
        private float _lastAttackTime;
        private float _lastAreaDenialTime;

        [Header("Hazard Spawn Prefab Fallback")]
        [SerializeField] private GameObject _hazardZonePrefab;

        private NavMeshAgent _agent;
        private Transform _playerTransform;
        private AdaptiveBossController _mathController;
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
            if (_agent != null && !_agent.isOnNavMesh)
            {
                _agent.enabled = false;
            }
        }

        public void Initialize(BossStats stats, AdaptiveBossController mathController)
        {
            _activeStats = stats;
            _mathController = mathController;
            _currentHealth = stats.MaxHP;
            _currentPhase = 1;
            _isEnraged = false;

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.speed = stats.MoveSpeed;
                _agent.stoppingDistance = 2.5f;
            }

            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null) _playerTransform = playerGo.transform;

            SetupBossVisuals();

            _isInitialized = true;
            OnHealthChanged?.Invoke(_currentHealth, _activeStats.MaxHP);

            Debug.Log($"[BossController] {stats.BossID} awakened with {stats.MaxHP} HP, {stats.BaseDamage} DMG.");
        }

        private void SetupBossVisuals()
        {
            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                rend.material.color = new Color(0.45f, 0.05f, 0.1f); // Dark Demonic Flesh
            }

            // Horns / Crown
            var hornL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hornL.name = "Boss_Horn_L";
            hornL.transform.parent = transform;
            hornL.transform.localPosition = new Vector3(-0.4f, 1.8f, 0.2f);
            hornL.transform.localRotation = Quaternion.Euler(-25f, -20f, 0f);
            hornL.transform.localScale = new Vector3(0.25f, 0.9f, 0.25f);
            var colL = hornL.GetComponent<Collider>();
            if (colL != null) Destroy(colL);
            var rendHL = hornL.GetComponent<Renderer>();
            if (rendHL != null) rendHL.material.color = Color.black;

            var hornR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hornR.name = "Boss_Horn_R";
            hornR.transform.parent = transform;
            hornR.transform.localPosition = new Vector3(0.4f, 1.8f, 0.2f);
            hornR.transform.localRotation = Quaternion.Euler(-25f, 20f, 0f);
            hornR.transform.localScale = new Vector3(0.25f, 0.9f, 0.25f);
            var colR = hornR.GetComponent<Collider>();
            if (colR != null) Destroy(colR);
            var rendHR = hornR.GetComponent<Renderer>();
            if (rendHR != null) rendHR.material.color = Color.black;
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

            // Check Enrage
            CheckEnrageCondition();

            // Track target
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
                // Fallback direct pursuit towards player in arena
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
            OnEnrageStateChanged?.Invoke(true);
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
                // Grab deals 1.8x damage and ignores defense
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
                Destroy(hazard, 10f); // Temporary toxic hazard pool
            }
        }

        public float TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return 0f;

            _currentHealth -= info.Amount;
            OnHealthChanged?.Invoke(_currentHealth, _activeStats.MaxHP);

            // Check phase transition
            CheckPhaseTransition();

            if (_currentHealth <= 0f)
            {
                Die();
            }

            return info.Amount;
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

            // Empower stats per phase
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
    }
}
