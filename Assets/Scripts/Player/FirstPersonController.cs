// ============================================================================
// FirstPersonController.cs — Controlador Fisico en Primera Persona para Unity 6
// ============================================================================
// Maneja el movimiento terrestre, aceleracion/frenado, deteccion de suelo robusta
// mediante Physics.CheckSphere y fisica de gravedad realista.
// Disenado para acoplarse en la capsula raiz del jugador.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Player
{
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class FirstPersonController : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACION DE VELOCIDAD Y MOVIMIENTO
        // ════════════════════════════════════════════════════════════════════

        [Header("Velocidades de Movimiento")]
        [Tooltip("Velocidad normal de caminata en metros por segundo.")]
        [SerializeField] private float _walkSpeed = 4.5f;

        [Tooltip("Velocidad al correr (sprint) en metros por segundo.")]
        [SerializeField] private float _sprintSpeed = 7.5f;

        [Tooltip("Suavizado de aceleracion al iniciar el movimiento.")]
        [SerializeField] private float _acceleration = 14f;

        [Tooltip("Suavizado de deceleracion al soltar las teclas de movimiento.")]
        [SerializeField] private float _deceleration = 10f;

        // ════════════════════════════════════════════════════════════════════
        // FISICA Y GRAVEDAD
        // ════════════════════════════════════════════════════════════════════

        [Header("Fisica y Gravedad")]
        [Tooltip("Fuerza de gravedad vertical aplicada al jugador (valor negativo realista).")]
        [SerializeField] private float _gravity = -19.62f;

        [Tooltip("Altura maxima del salto en metros.")]
        [SerializeField] private float _jumpHeight = 1.2f;

        [Tooltip("Velocidad terminal de caida para evitar atravesar geometria a altas velocidades.")]
        [SerializeField] private float _terminalVelocity = -50f;

        // ════════════════════════════════════════════════════════════════════
        // VALIDACION ROBUSTA DE SUELO (Physics.CheckSphere)
        // ════════════════════════════════════════════════════════════════════

        [Header("Deteccion Robusta de Suelo (CheckSphere)")]
        [Tooltip("Transform opcional en los pies. Si no se asigna, se calcula a partir del CharacterController.")]
        [SerializeField] private Transform _groundCheckPoint;

        [Tooltip("Offset manual para el punto de contacto en caso de no usar un transform dedicado.")]
        [SerializeField] private Vector3 _groundCheckOffset = new Vector3(0f, 0.05f, 0f);

        [Tooltip("Radio de la esfera de chequeo de suelo.")]
        [Range(0.05f, 0.6f)]
        [SerializeField] private float _groundCheckRadius = 0.32f;

        [Tooltip("Capas consideradas como suelo solido.")]
        [SerializeField] private LayerMask _groundLayers = ~0;

        // ════════════════════════════════════════════════════════════════════
        // CONTROLES E INPUT
        // ════════════════════════════════════════════════════════════════════

        [Header("Controles")]
        [SerializeField] private KeyCode _sprintKey = KeyCode.LeftShift;
        [SerializeField] private KeyCode _jumpKey = KeyCode.Space;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private CharacterController _characterController;
        private Vector3 _currentHorizontalVelocity;
        private float _verticalVelocity;
        private bool _isGrounded;
        private bool _canMove = true;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PUBLICAS
        // ════════════════════════════════════════════════════════════════════

        public CharacterController CharacterController => _characterController;
        public bool IsGrounded => _isGrounded;
        public bool IsSprinting { get; private set; }
        public bool IsMoving { get; private set; }
        public float WalkSpeed => _walkSpeed;
        public float SprintSpeed => _sprintSpeed;
        public float CurrentSpeed => _currentHorizontalVelocity.magnitude;
        public Vector3 Velocity => _characterController != null ? _characterController.velocity : Vector3.zero;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();

            // Configurar defaults recomendados para Unity 6 CharacterController si estan en valores por defecto
            if (_characterController != null)
            {
                if (_characterController.skinWidth < 0.05f) _characterController.skinWidth = 0.08f;
                if (_characterController.minMoveDistance <= 0f) _characterController.minMoveDistance = 0f;
            }
        }

        private void Update()
        {
            UpdateGroundCheck();

            if (!_canMove)
            {
                ApplyGravityOnly();
                return;
            }

            HandleMovement();
            HandleJumpAndGravity();
        }

        // ════════════════════════════════════════════════════════════════════
        // DETECCION DE SUELO (Physics.CheckSphere)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Comprueba si el jugador esta en contacto con el suelo usando una esfera fisica en sus pies.
        /// Evita las perdidas de contacto en pendientes o uniones de mallas del CharacterController nativo.
        /// </summary>
        private void UpdateGroundCheck()
        {
            Vector3 spherePosition = GetGroundCheckPosition();

            // Descartar la propia capa del jugador para que no colisione consigo mismo
            int effectiveMask = _groundLayers.value & ~(1 << gameObject.layer);

            bool sphereHit = Physics.CheckSphere(
                spherePosition,
                _groundCheckRadius,
                effectiveMask,
                QueryTriggerInteraction.Ignore
            );

            // Validacion combinada: CheckSphere prioritario con soporte del CharacterController
            _isGrounded = sphereHit || (_characterController != null && _characterController.isGrounded);

            // Si esta en el suelo y cayendo, fijar una presion descendente suave (-2m/s)
            // Esto "pega" el CharacterController a rampas y escalones evitando saltitos indeseados.
            if (_isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }
        }

        /// <summary>
        /// Obtiene el punto exacto donde se proyecta la esfera en la base de la capsula.
        /// </summary>
        public Vector3 GetGroundCheckPosition()
        {
            if (_groundCheckPoint != null)
            {
                return _groundCheckPoint.position;
            }

            if (_characterController != null)
            {
                // Base del CharacterController en espacio mundial
                float bottomY = _characterController.center.y - (_characterController.height * 0.5f) + _characterController.radius;
                return transform.TransformPoint(new Vector3(_characterController.center.x, bottomY, _characterController.center.z)) + _groundCheckOffset;
            }

            return transform.position + _groundCheckOffset;
        }

        // ════════════════════════════════════════════════════════════════════
        // MOVIMIENTO HORIZONTAL (Caminar / Correr)
        // ════════════════════════════════════════════════════════════════════

        private void HandleMovement()
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputZ = Input.GetAxisRaw("Vertical");

            // Vector direccional respecto a la orientacion horizontal del cuerpo del jugador
            Vector3 targetDirection = (transform.right * inputX + transform.forward * inputZ).normalized;
            IsMoving = targetDirection.sqrMagnitude > 0.01f;

            // Determinar si corre (Shift presionado y moviendose)
            IsSprinting = IsMoving && Input.GetKey(_sprintKey);
            float targetSpeed = IsSprinting ? _sprintSpeed : _walkSpeed;

            Vector3 targetVelocity = targetDirection * targetSpeed;

            // Aceleracion o frenado progresivo para control organico sin deslizamientos
            float accelRate = IsMoving ? _acceleration : _deceleration;
            _currentHorizontalVelocity = Vector3.MoveTowards(
                _currentHorizontalVelocity,
                targetVelocity,
                accelRate * Time.deltaTime
            );

            // Aplicar traslacion horizontal
            _characterController.Move(_currentHorizontalVelocity * Time.deltaTime);
        }

        // ════════════════════════════════════════════════════════════════════
        // SALTO Y GRAVEDAD VERTICAL
        // ════════════════════════════════════════════════════════════════════

        private void HandleJumpAndGravity()
        {
            // Salto activable solo si el contacto de suelo es valido
            if (Input.GetKeyDown(_jumpKey) && _isGrounded)
            {
                // Formula cinematica v = sqrt(h * -2g)
                _verticalVelocity = Mathf.Sqrt(_jumpHeight * -2f * _gravity);
            }

            // Aplicar aceleracion por gravedad
            if (!_isGrounded)
            {
                _verticalVelocity += _gravity * Time.deltaTime;
                if (_verticalVelocity < _terminalVelocity)
                {
                    _verticalVelocity = _terminalVelocity;
                }
            }

            // Aplicar traslacion vertical
            Vector3 verticalMove = new Vector3(0f, _verticalVelocity, 0f);
            _characterController.Move(verticalMove * Time.deltaTime);
        }

        private void ApplyGravityOnly()
        {
            if (!_isGrounded)
            {
                _verticalVelocity += _gravity * Time.deltaTime;
                if (_verticalVelocity < _terminalVelocity)
                {
                    _verticalVelocity = _terminalVelocity;
                }
            }

            _characterController.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
        }

        // ════════════════════════════════════════════════════════════════════
        // ACCIONES Y TELETRANSPORTE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Teletransporta al jugador de forma segura deshabilitando temporalmente el CharacterController.
        /// </summary>
        public void Teleport(Vector3 destination, Quaternion rotation)
        {
            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            transform.position = destination;
            transform.rotation = rotation;
            _currentHorizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;

            if (_characterController != null)
            {
                _characterController.enabled = true;
            }
        }

        /// <summary>
        /// Habilita o deshabilita la capacidad de movimiento del jugador (cinematicas, QTEs, inventario).
        /// </summary>
        public void SetMovementEnabled(bool enabled)
        {
            _canMove = enabled;
            if (!enabled)
            {
                _currentHorizontalVelocity = Vector3.zero;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DEPURACION VISUAL (GIZMOS EN SCENE VIEW)
        // ════════════════════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            Vector3 spherePosition = GetGroundCheckPosition();
            Gizmos.color = _isGrounded ? new Color(0f, 1f, 0f, 0.45f) : new Color(1f, 0f, 0f, 0.45f);
            Gizmos.DrawSphere(spherePosition, _groundCheckRadius);
            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(spherePosition, _groundCheckRadius);
        }
    }
}

