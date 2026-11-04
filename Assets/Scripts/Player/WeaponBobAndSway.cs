// ============================================================================
// WeaponBobAndSway.cs — Vaivén Procedimental (Bobbing) e Inercia (Mouse Sway) en Unity 6
// ============================================================================
// Simula físicamente el movimiento dinámico del WeaponHolder y las armas 3D en primera persona:
// 1. Bobbing por Pasos: Modulación senoidal (Mathf.Sin / Mathf.Cos) sincronizada con la
//    velocidad del CharacterController, diferenciando caminata de carrera (sprint).
// 2. Inercia y Arrastre (Mouse Sway): Lag posicional y rotación angular (Pitch, Yaw, Roll)
//    al girar la vista de la cámara.
// 3. Respiración en Reposo (Idle Breathing): Oscilación orgánica cuando el jugador está inmóvil.
// 4. Interpolación Fluida: Vector3.Lerp y Quaternion.Slerp para transiciones libres de tirones.
// 5. No Destructivo: Opera como capa acumulativa sobre la posición/rotación base del WeaponHolder.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Player
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Player/Weapon Bob And Sway")]
    public class WeaponBobAndSway : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // REFERENCIAS PRINCIPALES
        // ════════════════════════════════════════════════════════════════════

        [Header("Contenedor Objetivo")]
        [Tooltip("Transform del contenedor WeaponHolder que sostiene el arma 3D. Si es nulo, se busca automáticamente.")]
        [SerializeField] private Transform _weaponHolder;

        [Tooltip("Cámara principal del jugador.")]
        [SerializeField] private Camera _playerCamera;

        [Header("Controladores de Jugador (Auto-Resueltos)")]
        [SerializeField] private FirstPersonController _fpsController;
        [SerializeField] private CharacterController _characterController;

        // ════════════════════════════════════════════════════════════════════
        // PARÁMETROS DE VAIVÉN POR PASOS (BOBBING)
        // ════════════════════════════════════════════════════════════════════

        [Header("Vaivén Procedimental por Pasos (Weapon Bobbing)")]
        [Tooltip("Activa el vaivén cíclico al desplazarse sobre el suelo.")]
        [SerializeField] private bool _enableBobbing = true;

        [Tooltip("Frecuencia base de los pasos al caminar (Hz). Sincronizado a 8.5Hz con PlayerHandController.")]
        [SerializeField] private float _walkBobFrequency = 8.5f;

        [Tooltip("Multiplicador de frecuencia al correr (sprint).")]
        [SerializeField] private float _sprintBobMultiplier = 1.35f;

        [Tooltip("Amplitud de oscilación lateral (eje local X). Alterna izquierda/derecha por paso.")]
        [SerializeField] private float _bobAmountX = 0.015f;

        [Tooltip("Amplitud de oscilación vertical (eje local Y). Dos impulsos por zancada.")]
        [SerializeField] private float _bobAmountY = 0.018f;

        [Tooltip("Inclinación angular de ladeo en Z (Roll) al dar cada paso.")]
        [SerializeField] private float _bobTiltAngle = 1.2f;

        [Tooltip("Velocidad de suavizado del bobbing (interpolación libre de tirones).")]
        [SerializeField] private float _bobSmoothing = 10f;

        // ════════════════════════════════════════════════════════════════════
        // PARÁMETROS DE RESPIRACIÓN EN REPOSO (IDLE BREATHING)
        // ════════════════════════════════════════════════════════════════════

        [Header("Respiración en Reposo (Idle Breathing)")]
        [Tooltip("Aplica una sutil oscilación senoidal cuando el jugador está inmóvil.")]
        [SerializeField] private bool _enableIdleBreathing = true;

        [Tooltip("Frecuencia del ciclo de respiración en reposo.")]
        [SerializeField] private float _idleBreathingSpeed = 1.8f;

        [Tooltip("Amplitud del desplazamiento de respiración.")]
        [SerializeField] private float _idleBreathingAmount = 0.003f;

        // ════════════════════════════════════════════════════════════════════
        // PARÁMETROS DE INERCIA Y SWAY POR RATÓN
        // ════════════════════════════════════════════════════════════════════

        [Header("Inercia y Arrastre por Ratón (Mouse Sway)")]
        [Tooltip("Activa la inercia del arma en respuesta a la rotación de la cámara.")]
        [SerializeField] private bool _enableMouseSway = true;

        [Tooltip("Sensibilidad de retraso posicional inverso.")]
        [SerializeField] private float _swayPosAmount = 0.02f;

        [Tooltip("Límite máximo de desplazamiento posicional por inercia.")]
        [SerializeField] private float _maxSwayPos = 0.04f;

        [Tooltip("Sensibilidad de rotación angular (Pitch / Yaw) de arrastre.")]
        [SerializeField] private float _swayRotAmount = 2.5f;

        [Tooltip("Inclinación de ladeo (Roll) al girar la vista rápidamente.")]
        [SerializeField] private float _swayRollAmount = 3.5f;

        [Tooltip("Límite máximo de rotación angular por inercia (grados).")]
        [SerializeField] private float _maxSwayRotAngle = 6.0f;

        [Tooltip("Velocidad de suavizado del retorno del arma a su posición neutral.")]
        [SerializeField] private float _swaySmoothSpeed = 9f;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private Vector3 _initialLocalPosition;
        private Quaternion _initialLocalRotation;
        private bool _isBaseTransformCaptured = false;

        private float _bobTimer = 0f;
        private Vector3 _currentBobPosition = Vector3.zero;
        private Quaternion _currentBobRotation = Quaternion.identity;
        private Vector3 _currentSwayPosition = Vector3.zero;
        private Quaternion _currentSwayRotation = Quaternion.identity;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public bool EnableBobbing { get => _enableBobbing; set => _enableBobbing = value; }
        public bool EnableMouseSway { get => _enableMouseSway; set => _enableMouseSway = value; }
        public Transform TargetWeaponHolder => _weaponHolder;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            ResolverReferencias();
            CapturarTransformBase();
        }

        private void Start()
        {
            ResolverReferencias();
            CapturarTransformBase();
        }

        private void Update()
        {
            ActualizarMotionProcedural();
        }

        // ════════════════════════════════════════════════════════════════════
        // RESOLUCIÓN DE REFERENCIAS Y CAPTURA BASE
        // ════════════════════════════════════════════════════════════════════

        public void ResolverReferencias()
        {
            // 1. Resolver Cámara Principal
            if (_playerCamera == null)
            {
                _playerCamera = Camera.main ?? GetComponentInParent<Camera>() ?? FindAnyObjectByType<Camera>();
            }

            // 2. Resolver WeaponHolder
            if (_weaponHolder == null)
            {
                if (_playerCamera != null)
                {
                    Transform found = _playerCamera.transform.Find("WeaponHolder");
                    if (found != null) _weaponHolder = found;
                }

                if (_weaponHolder == null)
                {
                    var equipManager = GetComponentInParent<PlayerWeaponEquipManager>() ?? FindAnyObjectByType<PlayerWeaponEquipManager>();
                    if (equipManager != null && equipManager.WeaponHolder != null)
                    {
                        _weaponHolder = equipManager.WeaponHolder;
                    }
                }

                if (_weaponHolder == null && transform.name.Contains("WeaponHolder"))
                {
                    _weaponHolder = transform;
                }
            }

            // 3. Resolver Controladores de Jugador
            if (_fpsController == null)
            {
                _fpsController = GetComponentInParent<FirstPersonController>() ?? FindAnyObjectByType<FirstPersonController>();
            }

            if (_characterController == null)
            {
                _characterController = _fpsController?.CharacterController ?? GetComponentInParent<CharacterController>() ?? FindAnyObjectByType<CharacterController>();
            }
        }

        private void CapturarTransformBase()
        {
            if (_weaponHolder != null && !_isBaseTransformCaptured)
            {
                _initialLocalPosition = _weaponHolder.localPosition;
                _initialLocalRotation = _weaponHolder.localRotation;
                _isBaseTransformCaptured = true;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PIPELINE DE MOTION PROCEDURAL
        // ════════════════════════════════════════════════════════════════════

        private void ActualizarMotionProcedural()
        {
            if (_weaponHolder == null) return;

            // 1. Calcular Vaivén Cíclico por Pasos (o Respiración en Reposo)
            CalcularBobbing(out Vector3 targetBobPos, out Quaternion targetBobRot);

            // 2. Calcular Inercia y Sway por Entrada del Ratón
            CalcularMouseSway(out Vector3 targetSwayPos, out Quaternion targetSwayRot);

            // 3. Suavizado de traslaciones mediante Vector3.Lerp libre de tirones
            _currentBobPosition = Vector3.Lerp(_currentBobPosition, targetBobPos, Time.deltaTime * _bobSmoothing);
            _currentSwayPosition = Vector3.Lerp(_currentSwayPosition, targetSwayPos, Time.deltaTime * _swaySmoothSpeed);

            // 4. Suavizado de rotaciones angulares mediante Quaternion.Slerp orgánico
            _currentBobRotation = Quaternion.Slerp(_currentBobRotation, targetBobRot, Time.deltaTime * _bobSmoothing);
            _currentSwayRotation = Quaternion.Slerp(_currentSwayRotation, targetSwayRot, Time.deltaTime * _swaySmoothSpeed);

            // 5. Aplicar como capa no destructiva sobre la posición y rotación base del WeaponHolder
            _weaponHolder.localPosition = _initialLocalPosition + _currentBobPosition + _currentSwayPosition;
            _weaponHolder.localRotation = _initialLocalRotation * _currentBobRotation * _currentSwayRotation;
        }

        private void CalcularBobbing(out Vector3 bobPos, out Quaternion bobRot)
        {
            bobPos = Vector3.zero;
            bobRot = Quaternion.identity;

            if (!_enableBobbing) return;

            // Obtener estado cinemático del jugador
            Vector3 velocity = Vector3.zero;
            bool isGrounded = true;
            bool isSprinting = false;

            if (_fpsController != null)
            {
                velocity = _fpsController.Velocity;
                isGrounded = _fpsController.IsGrounded;
                isSprinting = _fpsController.IsSprinting;
            }
            else if (_characterController != null)
            {
                velocity = _characterController.velocity;
                isGrounded = _characterController.isGrounded;
                isSprinting = Input.GetKey(KeyCode.LeftShift);
            }
            else
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                if (Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f)
                {
                    isSprinting = Input.GetKey(KeyCode.LeftShift);
                    velocity = new Vector3(h, 0f, v).normalized * (isSprinting ? 7f : 4.5f);
                }
            }

            Vector3 horizVelocity = new Vector3(velocity.x, 0f, velocity.z);
            float speed = horizVelocity.magnitude;

            // Movimiento activo sobre el suelo (> 0.15 m/s)
            if (isGrounded && speed > 0.15f)
            {
                float freq = _walkBobFrequency * (isSprinting ? _sprintBobMultiplier : 1f);
                _bobTimer += Time.deltaTime * freq;

                float speedFactor = Mathf.Clamp(speed / 4.5f, 0.35f, 1.5f);
                float sprintMult = isSprinting ? 1.25f : 1f;

                // Eje Local X: vaivén lateral alternado (Mathf.Cos en medio ciclo)
                float targetX = Mathf.Cos(_bobTimer * 0.5f) * _bobAmountX * speedFactor * sprintMult;

                // Eje Local Y: dos impulsos por zancada (Mathf.Sin)
                float sinY = Mathf.Sin(_bobTimer);
                float targetY = (sinY * 0.7f - Mathf.Abs(sinY) * 0.3f) * _bobAmountY * speedFactor * sprintMult;

                bobPos = new Vector3(targetX, targetY, 0f);

                // Ladeo angular sutil (Roll) en Z
                float targetTiltZ = -Mathf.Cos(_bobTimer * 0.5f) * _bobTiltAngle * speedFactor;
                bobRot = Quaternion.Euler(0f, 0f, targetTiltZ);
            }
            else
            {
                // En reposo: respiración sutil senoidal
                if (_enableIdleBreathing)
                {
                    float idleY = Mathf.Sin(Time.time * _idleBreathingSpeed) * _idleBreathingAmount;
                    float idleX = Mathf.Cos(Time.time * (_idleBreathingSpeed * 0.5f)) * (_idleBreathingAmount * 0.5f);
                    bobPos = new Vector3(idleX, idleY, 0f);
                }
            }
        }

        private void CalcularMouseSway(out Vector3 swayPos, out Quaternion swayRot)
        {
            swayPos = Vector3.zero;
            swayRot = Quaternion.identity;

            if (!_enableMouseSway) return;

            float mouseX = Input.GetAxisRaw("Mouse X");
            float mouseY = Input.GetAxisRaw("Mouse Y");

            // Desplazamiento posicional inverso (lag físico)
            float moveMult = _swayPosAmount * -0.5f;
            float posX = Mathf.Clamp(mouseX * moveMult, -_maxSwayPos, _maxSwayPos);
            float posY = Mathf.Clamp(mouseY * moveMult, -_maxSwayPos, _maxSwayPos);
            swayPos = new Vector3(posX, posY, 0f);

            // Rotación angular de arrastre
            float rotPitch = Mathf.Clamp(mouseY * _swayRotAmount, -_maxSwayRotAngle, _maxSwayRotAngle);
            float rotYaw = Mathf.Clamp(-mouseX * _swayRotAmount, -_maxSwayRotAngle, _maxSwayRotAngle);
            float rotRoll = Mathf.Clamp(mouseX * _swayRollAmount, -_maxSwayRotAngle * 1.25f, _maxSwayRotAngle * 1.25f);

            swayRot = Quaternion.Euler(rotPitch, rotYaw, rotRoll);
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS PÚBLICOS DE CALIBRACIÓN
        // ════════════════════════════════════════════════════════════════════

        public void SetBaseTransform(Vector3 position, Quaternion rotation)
        {
            _initialLocalPosition = position;
            _initialLocalRotation = rotation;
            _isBaseTransformCaptured = true;
        }

        public void SetBobAmount(float amountX, float amountY)
        {
            _bobAmountX = Mathf.Max(0f, amountX);
            _bobAmountY = Mathf.Max(0f, amountY);
        }

        public void ResetMotion()
        {
            _currentBobPosition = Vector3.zero;
            _currentBobRotation = Quaternion.identity;
            _currentSwayPosition = Vector3.zero;
            _currentSwayRotation = Quaternion.identity;
            _bobTimer = 0f;

            if (_weaponHolder != null)
            {
                _weaponHolder.localPosition = _initialLocalPosition;
                _weaponHolder.localRotation = _initialLocalRotation;
            }
        }
    }
}

