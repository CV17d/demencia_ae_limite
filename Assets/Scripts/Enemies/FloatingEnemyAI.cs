// ============================================================================
// FloatingEnemyAI.cs — IA de Enemigo Flotante 2D en Entorno 3D (Unity 6)
// ============================================================================
// Gestiona el comportamiento integral del enemigo espectral flotante:
// 1. Carga y asignación automática o dinámica de sprites desde:
//    "Assets/Art/2D/Enemigos_Normales/" (enemigo 001 baset y 001 pegando).
// 2. Persecución suave al jugador (Vector3.MoveTowards) con levitación sinusoidal
//    permanente en el eje Y (Mathf.Sin) y mantenimiento de altura respecto al suelo.
// 3. Cambio fluido de sprites durante el ataque (0.35s con 001 pegando) y retorno a base.
// 4. Orientación permanente hacia la cámara principal (Billboard) en LateUpdate.
// 5. Integración con el sistema de combate (IDamageable, IKnockbackable, daño al jugador).
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using DemenciaNoLimite.Combat;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Player;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DemenciaNoLimite.Enemies
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("Demencia No Limite/Enemies/Floating Enemy AI")]
    public class FloatingEnemyAI : MonoBehaviour, IDamageable, IKnockbackable
    {
        // ════════════════════════════════════════════════════════════════════
        // CONSTANTES DE RUTAS DE SPRITES
        // ════════════════════════════════════════════════════════════════════

        public const string BASE_SPRITE_PATH = "Assets/Art/2D/Enemigos_Normales/enemigo 001 baset.png";
        public const string ATTACK_SPRITE_PATH = "Assets/Art/2D/Enemigos_Normales/001 pegando.png";

        // ════════════════════════════════════════════════════════════════════
        // REFERENCIAS DE SPRITES Y RENDERER
        // ════════════════════════════════════════════════════════════════════

        [Header("Visuales y Sprites (Assets/Art/2D/Enemigos_Normales/)")]
        [Tooltip("Sprite base de flotación/reposo (enemigo 001 baset).")]
        [SerializeField] private Sprite _baseSprite;

        [Tooltip("Sprite durante el ataque/golpe (001 pegando).")]
        [SerializeField] private Sprite _attackSprite;

        [Tooltip("Componente SpriteRenderer que renderiza al enemigo.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // ════════════════════════════════════════════════════════════════════
        // ORIENTACIÓN (BILLBOARD HACIA LA CÁMARA)
        // ════════════════════════════════════════════════════════════════════

        [Header("Billboard (Orientación a la Cámara)")]
        [Tooltip("Transform específico del sprite si está en un hijo. Si es nulo, se rota este GameObject.")]
        [SerializeField] private Transform _visualTransform;

        [Tooltip("Cámara principal de referencia. Si es nula, se busca automáticamente Camera.main.")]
        [SerializeField] private Camera _mainCamera;

        [Tooltip("Si es true, bloquea la inclinación vertical (pitch) para mantener al enemigo erguido (estilo Doom retro).")]
        [SerializeField] private bool _lockVerticalPitch = false;

        // ════════════════════════════════════════════════════════════════════
        // PERSECUCIÓN, MOVIMIENTO Y LEVITACIÓN SINUSOIDAL
        // ════════════════════════════════════════════════════════════════════

        [Header("Movimiento y Persecución")]
        [Tooltip("Velocidad de movimiento en persecución.")]
        [SerializeField] private float _moveSpeed = 3.2f;

        [Tooltip("Radio de detección para comenzar a perseguir al jugador (en metros).")]
        [SerializeField] private float _detectionRadius = 22f;

        [Tooltip("Distancia de parada en el plano horizontal (XZ). El enemigo frena aquí para no colisionar ni traspasar al jugador.")]
        [SerializeField] private float _stoppingDistance = 1.8f;

        [Tooltip("Distancia máxima horizontal para poder iniciar el ataque cuerpo a cuerpo.")]
        [SerializeField] private float _attackRange = 2.0f;

        [Tooltip("Capa de colisión del suelo para calibrar la altura de levitación.")]
        [SerializeField] private LayerMask _groundLayer = ~0;

        [Header("Levitación Sinusoidal (Flotación Y)")]
        [Tooltip("Altura objetivo sobre el suelo o jugador.")]
        [SerializeField] private float _hoverHeight = 1.35f;

        [Tooltip("Amplitud de la onda sinusoidal (distancia que sube y baja al flotar).")]
        [SerializeField] private float _floatAmplitude = 0.18f;

        [Tooltip("Frecuencia o velocidad de la oscilación vertical.")]
        [SerializeField] private float _floatFrequency = 2.4f;

        // ════════════════════════════════════════════════════════════════════
        // SISTEMA DE COMBATE Y ATAQUE AL JUGADOR
        // ════════════════════════════════════════════════════════════════════

        [Header("Combate y Ataque")]
        [Tooltip("Daño infligido al jugador por cada golpe.")]
        [SerializeField] private float _attackDamage = 16f;

        [Tooltip("Tiempo de enfriamiento (cooldown) entre ataques consecutivos en segundos.")]
        [SerializeField] private float _attackCooldown = 1.3f;

        [Tooltip("Duración rápida en segundos en la que se muestra el sprite de ataque (0.15s - 0.20s) para una embestida ágil y visceral.")]
        [Range(0.05f, 0.50f)]
        [SerializeField] private float _attackSpriteDuration = 0.18f;

        [Tooltip("Fuerza de empuje físico (knockback) aplicada al jugador al golpear.")]
        [SerializeField] private float _attackKnockbackForce = 3.5f;

        [Tooltip("Micro-embestida hacia adelante al ejecutar el ataque para mayor tensión visceral.")]
        [SerializeField] private float _attackLungeForce = 0.75f;

        [Tooltip("Efecto de sonido al realizar el ataque.")]
        [SerializeField] private AudioClip _attackSound;

        // ════════════════════════════════════════════════════════════════════
        // SALUD, DAÑO Y RETROCESO (IDAMAGEABLE / IKNOCKBACKABLE)
        // ════════════════════════════════════════════════════════════════════

        [Header("Salud y Retroalimentación")]
        [Tooltip("Salud máxima del enemigo.")]
        [SerializeField] private float _maxHealth = 65f;

        [Tooltip("Salud actual del enemigo.")]
        [SerializeField] private float _currentHealth = 65f;

        [Tooltip("Color del destello al recibir un impacto de bala.")]
        [SerializeField] private Color _hitFlashColor = new Color(1f, 0.25f, 0.25f, 1f);

        [Tooltip("Sonido reproducido al recibir daño.")]
        [SerializeField] private AudioClip _hurtSound;

        [Tooltip("Sonido al morir.")]
        [SerializeField] private AudioClip _deathSound;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private Transform _playerTransform;
        private AudioSource _audioSource;
        private Color _originalColor = Color.white;
        private Coroutine _attackCoroutine;
        private Coroutine _hitFlashCoroutine;

        private float _nextAttackTime = 0f;
        private float _floatSeed = 0f;
        private bool _isAttacking = false;
        private bool _isAlive = true;
        private Vector3 _knockbackVelocity = Vector3.zero;
        private float _lastGroundY = 0f;
        private bool _hasFoundGround = false;

        // Propiedades de la interfaz IDamageable
        public bool IsAlive => _isAlive && _currentHealth > 0f;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _currentHealth = _maxHealth;
            _floatSeed = UnityEngine.Random.Range(0f, 100f);
            _lastGroundY = transform.position.y;
            _hasFoundGround = true;

            // Asegurar resolución de componentes requeridos
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (_spriteRenderer != null)
            {
                _originalColor = _spriteRenderer.color;
            }

            if (_visualTransform == null)
            {
                _visualTransform = transform;
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 1.0f; // 3D Audio
                _audioSource.playOnAwake = false;
                _audioSource.maxDistance = 25f;
            }

            // Capa Enemy automática
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1)
            {
                gameObject.layer = enemyLayer;
            }

            // Asegurar Collider para recepción de disparos por Raycast
            EnsureHitboxCollider();

            // Cargar o verificar asignación de sprites
            EnsureSpritesAssigned();
        }

        private void Start()
        {
            ResolveCameraAndPlayer();
            SetSprite(_baseSprite);
        }

        private void Update()
        {
            if (!_isAlive) return;

            // Procesar inercia de retroceso por impacto
            HandleKnockbackDecay();

            // Localizar jugador si aún no se tiene
            if (_playerTransform == null)
            {
                ResolveCameraAndPlayer();
                if (_playerTransform == null) return;
            }

            // Cálculo de distancia bidimensional sobre el plano horizontal (XZ)
            Vector3 toPlayer = _playerTransform.position - transform.position;
            Vector3 toPlayerXZ = new Vector3(toPlayer.x, 0f, toPlayer.z);
            float horizontalDist = toPlayerXZ.magnitude;

            // Persecución si está dentro del radio de detección
            if (horizontalDist <= _detectionRadius)
            {
                ExecuteFloatingMovement(horizontalDist, toPlayerXZ);

                // Comprobar rango de ataque sobre el plano horizontal y cooldown
                if (horizontalDist <= _attackRange && !_isAttacking && Time.time >= _nextAttackTime)
                {
                    _attackCoroutine = StartCoroutine(PerformAttackRoutine());
                }
            }
            else
            {
                // Flotación en el lugar cuando el jugador está lejos
                ExecuteIdleHover();
            }
        }

        private void LateUpdate()
        {
            // Billboard permanente orientado hacia la cámara principal
            UpdateBillboardOrientation();
        }

        // ════════════════════════════════════════════════════════════════════
        // PERSECUCIÓN Y LEVITACIÓN SINUSOIDAL
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mueve suavemente al enemigo hacia el jugador en XZ respetando la distancia de parada (stopping distance)
        /// y mantiene la levitación sinusoidal controlada en Y sin ascender al techo ni seguir la cámara.
        /// </summary>
        private void ExecuteFloatingMovement(float horizontalDist, Vector3 toPlayerXZ)
        {
            float targetY = ComputeTargetHoverY();

            if (horizontalDist > _stoppingDistance)
            {
                // Detenerse exactamente al llegar al radio de parada para no colisionar ni traspasar
                Vector3 stopTargetXZ = _playerTransform.position - (toPlayerXZ.normalized * _stoppingDistance);
                Vector3 targetDestination = new Vector3(stopTargetXZ.x, targetY, stopTargetXZ.z);

                Vector3 moveDelta = targetDestination - transform.position;
                float stepDist = Mathf.Min(moveDelta.magnitude, _moveSpeed * Time.deltaTime);
                Vector3 moveDir = moveDelta.normalized;

                // Verificación contra muros para no empujar al jugador ni traspasar esquinas
                if (_obstacleLayer == 0 || !Physics.Raycast(transform.position, moveDir, stepDist + 0.35f, _obstacleLayer, QueryTriggerInteraction.Ignore))
                {
                    transform.position = Vector3.MoveTowards(transform.position, targetDestination, stepDist);
                }
                else
                {
                    Vector3 curPos = transform.position;
                    curPos.y = Mathf.MoveTowards(curPos.y, targetY, _moveSpeed * Time.deltaTime);
                    transform.position = curPos;
                }
            }
            else
            {
                // Dentro de la distancia de parada: mantener altura Y flotante estable sin avanzar hacia el jugador
                Vector3 curPos = transform.position;
                curPos.y = Mathf.MoveTowards(curPos.y, targetY, _moveSpeed * Time.deltaTime);

                // Si el jugador camina hacia el enemigo y penetra su espacio, retroceder suavemente
                if (horizontalDist < _stoppingDistance * 0.85f && horizontalDist > 0.05f)
                {
                    Vector3 pushBackTargetXZ = _playerTransform.position - (toPlayerXZ.normalized * _stoppingDistance);
                    Vector3 targetPushBack = new Vector3(pushBackTargetXZ.x, curPos.y, pushBackTargetXZ.z);
                    curPos = Vector3.MoveTowards(curPos, targetPushBack, (_moveSpeed * 0.7f) * Time.deltaTime);
                }

                transform.position = curPos;
            }
        }

        /// <summary>
        /// Mantiene la flotación sinusoidal en reposo cuando el jugador está fuera del radio de detección.
        /// </summary>
        private void ExecuteIdleHover()
        {
            float targetY = ComputeTargetHoverY();
            Vector3 curPos = transform.position;
            curPos.y = Mathf.MoveTowards(curPos.y, targetY, (_moveSpeed * 0.5f) * Time.deltaTime);
            transform.position = curPos;
        }

        /// <summary>
        /// Calcula la altura objetivo Y anclada estrictamente a la superficie física del suelo transitable,
        /// con límite superior rígido al nivel del torso/cabeza del jugador para impedir ascensos indeseados hacia el techo.
        /// </summary>
        private float ComputeTargetHoverY()
        {
            float playerBaseY = _playerTransform != null ? _playerTransform.position.y : transform.position.y;

            // Filtrar capas de Player y Enemy para que el raycast NUNCA detecte al propio jugador ni al enemigo como suelo
            int playerLayer = _playerTransform != null ? _playerTransform.gameObject.layer : LayerMask.NameToLayer("Player");
            int enemyLayer = gameObject.layer;
            int excludeMask = 0;
            if (playerLayer != -1) excludeMask |= (1 << playerLayer);
            if (enemyLayer != -1) excludeMask |= (1 << enemyLayer);
            LayerMask groundOnlyMask = _groundLayer.value & ~excludeMask;

            // Proyectar hacia abajo desde una altura prudente sobre el suelo conocido (no desde arriba del techo)
            float rayOriginY = _hasFoundGround ? Mathf.Min(transform.position.y + 0.3f, _lastGroundY + 1.2f) : (playerBaseY + 1.2f);
            Vector3 rayStart = new Vector3(transform.position.x, rayOriginY, transform.position.z);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit groundHit, 5.0f, groundOnlyMask, QueryTriggerInteraction.Ignore))
            {
                // Solo aceptar superficies transitables
                if (groundHit.normal.y >= 0.6f)
                {
                    // Evitar saltos bruscos si colisiona con algún borde o techo
                    if (!_hasFoundGround || Mathf.Abs(groundHit.point.y - _lastGroundY) < 1.4f)
                    {
                        _lastGroundY = groundHit.point.y;
                        _hasFoundGround = true;
                    }
                }
            }
            else if (!_hasFoundGround)
            {
                _lastGroundY = playerBaseY;
                _hasFoundGround = true;
            }

            // Altura base anclada al suelo físico
            float baseY = _lastGroundY + _hoverHeight;

            // Onda sinusoidal permanente en el eje Y
            float sineWave = Mathf.Sin((Time.time * _floatFrequency) + _floatSeed) * _floatAmplitude;
            float finalTargetY = baseY + sineWave;

            // LÍMITE DE SEGURIDAD ESTRICTO:
            // Impide matemáticamente que el enemigo suba al techo o siga los ángulos de cámara del jugador
            if (_playerTransform != null)
            {
                float maxAllowedY = playerBaseY + _hoverHeight + _floatAmplitude + 0.15f;
                float minAllowedY = playerBaseY + 0.35f;
                finalTargetY = Mathf.Clamp(finalTargetY, minAllowedY, maxAllowedY);
            }

            return finalTargetY;
        }

        // ════════════════════════════════════════════════════════════════════
        // CAMBIO DE SPRITES Y SISTEMA DE ATAQUE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Rutina de ataque rápido y visceral: cambia de inmediato al sprite '001 pegando'
        /// durante un lapso muy breve y reactivo (0.18s), aplica micro-embestida y daño al jugador,
        /// y retorna velozmente al sprite base 'enemigo 001 baset'.
        /// </summary>
        private IEnumerator PerformAttackRoutine()
        {
            _isAttacking = true;
            _nextAttackTime = Time.time + _attackCooldown;

            // 1. Cambiar inmediatamente y con agilidad al sprite de ataque ("001 pegando")
            SetSprite(_attackSprite != null ? _attackSprite : _baseSprite);

            // 2. Reproducir audio de ataque
            if (_attackSound != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(_attackSound);
            }

            // 3. Micro-embestida visceral controlada (con tope defensivo anti-empuje y sin atravesar al jugador)
            if (_playerTransform != null && _attackLungeForce > 0f)
            {
                Vector3 lungeDir = (_playerTransform.position - transform.position);
                lungeDir.y = 0f;
                float currentDist = lungeDir.magnitude;
                // No embestir si ya estamos demasiado cerca (respetando distancia de seguridad mínima)
                if (currentDist > 1.4f)
                {
                    float safeLunge = Mathf.Min(_attackLungeForce * 0.08f, currentDist - 1.35f);
                    if (safeLunge > 0f)
                    {
                        // Raycast de comprobación para no atravesar muros al embestir
                        if (!Physics.Raycast(transform.position, lungeDir.normalized, safeLunge + 0.3f, _obstacleLayer, QueryTriggerInteraction.Ignore))
                        {
                            transform.position += lungeDir.normalized * safeLunge;
                        }
                    }
                }
            }

            // 4. Aplicar daño inmediato al sistema de salud del jugador
            DeliverDamageToPlayer();

            // 5. Transición rápida y visceral (0.18s) para sensación ágil y tensa
            yield return new WaitForSeconds(_attackSpriteDuration);

            // 6. Retornar inmediatamente al sprite base de flotación ("enemigo 001 baset")
            if (_isAlive)
            {
                SetSprite(_baseSprite);
            }

            _isAttacking = false;
            _attackCoroutine = null;
        }

        /// <summary>
        /// Localiza y aplica daño al jugador mediante IDamageable o el subsistema de salud del PlayerManager.
        /// </summary>
        private void DeliverDamageToPlayer()
        {
            if (_playerTransform == null) return;

            // Validación de rango defensivo en plano horizontal con holgura de golpe
            Vector2 enemyXZ = new Vector2(transform.position.x, transform.position.z);
            Vector2 playerXZ = new Vector2(_playerTransform.position.x, _playerTransform.position.z);
            float currentHorizontalDist = Vector2.Distance(enemyXZ, playerXZ);
            if (currentHorizontalDist > _attackRange * 1.35f) return;

            // 1. Verificar si el jugador ya ha muerto para no continuar golpeando un cadáver
            if (PlayerManager.Instance != null && !PlayerManager.Instance.IsAlive) return;

            IDamageable playerDamageable = _playerTransform.GetComponentInParent<IDamageable>() ?? _playerTransform.GetComponent<IDamageable>();
            if (playerDamageable != null && !playerDamageable.IsAlive) return;

            Vector3 attackDir = (_playerTransform.position - transform.position);
            attackDir.y = 0f; // Puramente horizontal (nunca empujar hacia el suelo o techo)
            attackDir.Normalize();

            DamageInfo damagePayload = new DamageInfo(
                amount: _attackDamage,
                attacker: gameObject,
                hitPoint: _playerTransform.position,
                hitNormal: -attackDir,
                direction: attackDir,
                sourceType: DamageSourceType.Melee,
                isCritical: false,
                isHeadshot: false,
                ignoreDefense: false,
                knockbackForce: Mathf.Clamp(_attackKnockbackForce, 0.5f, 2.5f) // Tope de fuerza controlado
            );

            // Intentar aplicar vía IDamageable en el jugador
            if (playerDamageable != null)
            {
                playerDamageable.TakeDamage(damagePayload);
                return;
            }

            // Enlace con PlayerManager y PlayerHealth
            if (PlayerManager.Instance != null && PlayerManager.Instance.Health != null)
            {
                PlayerManager.Instance.Health.TakeDamage(damagePayload);
                return;
            }

            // Respaldo de eventos globales del proyecto
            GameEvents.FirePlayerDamaged(_attackDamage);
        }

        // ════════════════════════════════════════════════════════════════════
        // BILLBOARD (ORIENTACIÓN PERMANENTE A LA CÁMARA)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Orienta el SpriteRenderer frontalmente hacia Camera.main en LateUpdate para mantener el volumen visual en 3D.
        /// </summary>
        private void UpdateBillboardOrientation()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            Transform target = _visualTransform != null ? _visualTransform : transform;

            if (_lockVerticalPitch)
            {
                // Modo cilíndrico (Y-axis only): el sprite se mantiene vertical sin inclinarse
                Vector3 camForward = _mainCamera.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.001f)
                {
                    target.rotation = Quaternion.LookRotation(camForward.normalized);
                }
            }
            else
            {
                // Modo esférico completo: se alinea exactamente con el plano visual de la cámara
                target.rotation = _mainCamera.transform.rotation;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RECEPCIÓN DE DAÑO Y RETROCESO (IDAMAGEABLE / IKNOCKBACKABLE)
        // ════════════════════════════════════════════════════════════════════

        public float TakeDamage(DamageInfo info)
        {
            if (!_isAlive) return 0f;

            float damageTaken = info.Amount;
            _currentHealth -= damageTaken;

            // Retroalimentación de audio
            if (_hurtSound != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(_hurtSound);
            }

            // Destello visual de impacto
            if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);
            _hitFlashCoroutine = StartCoroutine(HitFlashRoutine());

            // Aplicar impulso de retroceso
            if (info.KnockbackForce > 0f)
            {
                ApplyKnockback(info.Direction * info.KnockbackForce);
            }

            // Comprobar muerte
            if (_currentHealth <= 0f)
            {
                Die();
            }

            return damageTaken;
        }

        /// <summary>
        /// Sobrecarga para compatibilidad con llamadas genéricas SendMessage("TakeDamage", float).
        /// </summary>
        public void TakeDamage(float amount)
        {
            TakeDamage(new DamageInfo(amount: amount, attacker: null, sourceType: DamageSourceType.Bullet));
        }

        public void ApplyKnockback(Vector3 force)
        {
            _knockbackVelocity += force;
        }

        private void HandleKnockbackDecay()
        {
            if (_knockbackVelocity.sqrMagnitude > 0.01f)
            {
                transform.position += _knockbackVelocity * Time.deltaTime;
                _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, Time.deltaTime * 7f);
            }
        }

        private IEnumerator HitFlashRoutine()
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = _hitFlashColor;
                yield return new WaitForSeconds(0.08f);
                _spriteRenderer.color = _originalColor;
            }
            _hitFlashCoroutine = null;
        }

        private void Die()
        {
            if (!_isAlive) return;
            _isAlive = false;

            if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
            if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);

            // Sonido de muerte
            if (_deathSound != null)
            {
                AudioSource.PlayClipAtPoint(_deathSound, transform.position);
            }

            // Notificar evento global de muerte de enemigo
            GameEvents.FireEnemyKilled(transform.position);

            // Desactivar colisiones para no obstruir disparos o al jugador
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            // Desvanecer o destruir
            Destroy(gameObject, 0.05f);
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DINÁMICA Y CARGA AUTOMÁTICA DE SPRITES
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Asigna un sprite al SpriteRenderer manteniendo la referencia activa.
        /// </summary>
        public void SetSprite(Sprite sprite)
        {
            if (_spriteRenderer != null && sprite != null)
            {
                _spriteRenderer.sprite = sprite;
            }
        }

        /// <summary>
        /// Asegura que los sprites base y de ataque estén correctamente cargados y referenciados.
        /// </summary>
        public void EnsureSpritesAssigned()
        {
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (_baseSprite != null && _attackSprite != null)
            {
                if (_spriteRenderer != null && _spriteRenderer.sprite == null)
                {
                    _spriteRenderer.sprite = _baseSprite;
                }
                return;
            }

#if UNITY_EDITOR
            LoadSpritesViaAssetDatabase();
#endif

            if (_spriteRenderer != null && _spriteRenderer.sprite == null && _baseSprite != null)
            {
                _spriteRenderer.sprite = _baseSprite;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Carga automáticamente los sprites desde Assets/Art/2D/Enemigos_Normales/ en el Editor.
        /// Compatible tanto con Single Sprite como con Multiple Sprites (sub-assets).
        /// </summary>
        [ContextMenu("Cargar Sprites Automáticamente")]
        public void LoadSpritesViaAssetDatabase()
        {
            if (_baseSprite == null)
            {
                _baseSprite = ResolveSpriteAsset(BASE_SPRITE_PATH);
            }

            if (_attackSprite == null)
            {
                _attackSprite = ResolveSpriteAsset(ATTACK_SPRITE_PATH);
            }

            if (_spriteRenderer != null && _baseSprite != null && _spriteRenderer.sprite == null)
            {
                _spriteRenderer.sprite = _baseSprite;
            }

            EditorUtility.SetDirty(this);
        }

        private static Sprite ResolveSpriteAsset(string path)
        {
            // 1. Intento de carga directa como Sprite único
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp != null) return sp;

            // 2. Si está configurado en modo Sprite Multiple / Sliced, buscar entre todos los sub-activos
            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (allAssets != null)
            {
                foreach (var obj in allAssets)
                {
                    if (obj is Sprite s)
                    {
                        return s;
                    }
                }
            }

            return null;
        }

        private void Reset()
        {
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
            EnsureHitboxCollider();
            LoadSpritesViaAssetDatabase();
        }

        private void OnValidate()
        {
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_baseSprite == null || _attackSprite == null)
            {
                LoadSpritesViaAssetDatabase();
            }
        }
#endif

        // ════════════════════════════════════════════════════════════════════
        // RESOLUCIÓN DE DEPENDENCIAS Y HITBOX
        // ════════════════════════════════════════════════════════════════════

        private void ResolveCameraAndPlayer()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_playerTransform == null)
            {
                if (PlayerManager.Instance != null)
                {
                    _playerTransform = PlayerManager.Instance.transform;
                }
                else
                {
                    GameObject p = GameObject.FindGameObjectWithTag("Player");
                    if (p != null) _playerTransform = p.transform;
                }
            }
        }

        private void EnsureHitboxCollider()
        {
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                capsule = gameObject.AddComponent<CapsuleCollider>();
            }

            // Sincronizar dimensiones proporcionales a marcos de puerta 4x4
            // Con escala 0.36x: Altura resultante en mundo ~1.73m y radio ~0.38m
            capsule.radius = 1.05f;
            capsule.height = 4.8f;
            capsule.center = Vector3.zero; // Sprite centrado en (0, 0, 0)
            capsule.direction = 1;

            // Kinematic Rigidbody para que Unity gestione colisiones sin empujes físicos erráticos en Y
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // DEPURACIÓN Y GIZMOS
        // ════════════════════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            // Radio de detección (Amarillo)
            Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, _detectionRadius);

            // Radio de parada (Cian)
            Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, _stoppingDistance);

            // Rango de ataque (Rojo)
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, _attackRange);
        }
    }
}

