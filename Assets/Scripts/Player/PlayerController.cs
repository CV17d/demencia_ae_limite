// ============================================================================
// PlayerController.cs — First-Person FPS movement & camera controller
// ============================================================================
// Drives movement based on PlayerStats (MoveSpeed, JumpHeight).
// Includes camera pitch/yaw clamping, head bob, sprint, and recoil recovery.
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;

namespace DemenciaNoLimite.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Camera & View")]
        [SerializeField] private Transform _cameraHolder;
        [SerializeField] private float _mouseSensitivity = 2.0f;
        [SerializeField] private float _minPitch = -85f;
        [SerializeField] private float _maxPitch = 85f;

        [Header("Physics & Gravity")]
        [SerializeField] private float _gravity = -18f;
        [SerializeField] private float _groundCheckDistance = 0.2f;
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Movement Modifiers")]
        [SerializeField] private float _sprintMultiplier = 1.4f;

        [Header("Head Bobbing")]
        [SerializeField] private float _bobFrequency = 10f;
        [SerializeField] private float _bobHorizontalAmplitude = 0.05f;
        [SerializeField] private float _bobVerticalAmplitude = 0.05f;
        private float _bobTimer;
        private Vector3 _originalCameraLocalPos;

        [Header("Recoil Spring")]
        [SerializeField] private float _recoilSnappiness = 15f;
        [SerializeField] private float _recoilReturnSpeed = 8f;
        private Vector3 _currentRecoilRotation;
        private Vector3 _targetRecoilRotation;

        private CharacterController _characterController;
        private float _verticalVelocity;
        private float _cameraPitch;
        private bool _isGrounded;

        public bool IsGrounded => _isGrounded;
        public bool IsMoving { get; private set; }
        public bool IsSprinting { get; private set; }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            ResolveCameraHolder();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Start()
        {
            ResolveCameraHolder();
        }

        public void SetCameraHolder(Transform camHolder)
        {
            _cameraHolder = camHolder;
            if (_cameraHolder != null)
            {
                _originalCameraLocalPos = _cameraHolder.localPosition;
            }
        }

        private void ResolveCameraHolder()
        {
            if (_cameraHolder == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    _cameraHolder = cam.transform;
                }
            }

            if (_cameraHolder != null && _originalCameraLocalPos == Vector3.zero)
            {
                _originalCameraLocalPos = _cameraHolder.localPosition;
            }
        }

        private void Update()
        {
            // Do not move if run is not in active state or player is dead
            if (GameManager.Instance != null && !GameManager.Instance.IsRunActive)
                return;

            HandleGroundCheck();
            HandleMouseLook();
            HandleMovement();
            HandleHeadBob();
            UpdateRecoil();
        }

        private void HandleGroundCheck()
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            bool rayGrounded = Physics.Raycast(origin, Vector3.down, _groundCheckDistance + 0.15f, _groundMask, QueryTriggerInteraction.Ignore);
            _isGrounded = (_characterController != null && _characterController.isGrounded) || rayGrounded;

            if (_isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f; // Slight downward snap to keep grounded on slopes
            }
        }

        private void HandleMouseLook()
        {
            float mouseX = Input.GetAxisRaw("Mouse X") * _mouseSensitivity;
            float mouseY = Input.GetAxisRaw("Mouse Y") * _mouseSensitivity;

            // Yaw: rotate character body
            transform.Rotate(Vector3.up * mouseX);

            // Pitch: rotate camera holder
            _cameraPitch -= mouseY;
            _cameraPitch = Mathf.Clamp(_cameraPitch, _minPitch, _maxPitch);

            if (_cameraHolder != null)
            {
                _cameraHolder.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f) * Quaternion.Euler(_currentRecoilRotation);
            }
        }

        private void HandleMovement()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            Vector3 moveDirection = (transform.right * horizontal + transform.forward * vertical).normalized;
            IsMoving = moveDirection.sqrMagnitude > 0.01f;

            // Player stats link
            PlayerStats stats = GameManager.Instance?.Player;
            float baseSpeed = stats != null ? stats.MoveSpeed : 5f;

            IsSprinting = Input.GetKey(KeyCode.LeftShift) && IsMoving;
            float targetSpeed = baseSpeed * (IsSprinting ? _sprintMultiplier : 1.0f);

            // Jump
            if (Input.GetButtonDown("Jump") && _isGrounded)
            {
                float jumpHeight = stats != null ? stats.JumpHeight : 1.2f;
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * _gravity);
            }

            // Apply gravity
            _verticalVelocity += _gravity * Time.deltaTime;

            Vector3 finalMove = (moveDirection * targetSpeed) + (Vector3.up * _verticalVelocity);
            _characterController.Move(finalMove * Time.deltaTime);
        }

        private void HandleHeadBob()
        {
            if (_cameraHolder == null) return;

            if (IsMoving && _isGrounded)
            {
                float speedFactor = IsSprinting ? 1.4f : 1.0f;
                _bobTimer += Time.deltaTime * _bobFrequency * speedFactor;

                float xOffset = Mathf.Cos(_bobTimer / 2f) * _bobHorizontalAmplitude;
                float yOffset = Mathf.Sin(_bobTimer) * _bobVerticalAmplitude;

                _cameraHolder.localPosition = _originalCameraLocalPos + new Vector3(xOffset, yOffset, 0f);
            }
            else
            {
                _bobTimer = 0f;
                _cameraHolder.localPosition = Vector3.Lerp(_cameraHolder.localPosition, _originalCameraLocalPos, Time.deltaTime * 6f);
            }
        }

        private void UpdateRecoil()
        {
            _targetRecoilRotation = Vector3.Lerp(_targetRecoilRotation, Vector3.zero, _recoilReturnSpeed * Time.deltaTime);
            _currentRecoilRotation = Vector3.Slerp(_currentRecoilRotation, _targetRecoilRotation, _recoilSnappiness * Time.deltaTime);
        }

        /// <summary>
        /// Applies procedural camera kickback from firing a weapon.
        /// </summary>
        public void ApplyRecoil(float verticalKick, float horizontalSpread)
        {
            float randomX = Random.Range(-horizontalSpread, horizontalSpread);
            _targetRecoilRotation += new Vector3(-verticalKick, randomX, 0f);
        }

        /// <summary>
        /// Instantly snaps player position (e.g. on floor spawn or elevator entry).
        /// </summary>
        public void Teleport(Vector3 destination, Quaternion rotation)
        {
            _characterController.enabled = false;
            transform.position = destination;
            transform.rotation = rotation;
            _verticalVelocity = 0f;
            _characterController.enabled = true;
        }
    }
}
