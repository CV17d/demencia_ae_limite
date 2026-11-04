// ============================================================================
// PlayerCamera.cs — Vision y Cabeza en Primera Persona para Unity 6
// ============================================================================
// Se adjunta a la Camara (objeto hijo del jugador).
// Controla la rotacion vertical (Pitch) con bloqueo (Clamp) entre -80 y 80 grados,
// mientras que la rotacion horizontal (Yaw) rota el cuerpo completo del jugador.
// Soporta control de cursor y amortiguacion de retroceso (recoil) procedimental.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class PlayerCamera : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // REFERENCIAS
        // ════════════════════════════════════════════════════════════════════

        [Header("Referencias")]
        [Tooltip("Transform del cuerpo raiz del jugador (se rotara sobre el eje Y horizontal).")]
        [SerializeField] private Transform _playerBody;

        // ════════════════════════════════════════════════════════════════════
        // SENSIBILIDAD Y LIMITES DE ROTACION
        // ════════════════════════════════════════════════════════════════════

        [Header("Sensibilidad del Ratón")]
        [Tooltip("Sensibilidad horizontal (Yaw).")]
        [SerializeField] private float _sensitivityX = 2.0f;

        [Tooltip("Sensibilidad vertical (Pitch).")]
        [SerializeField] private float _sensitivityY = 2.0f;

        [Tooltip("Invierte la rotacion del eje vertical si se desea.")]
        [SerializeField] private bool _invertY = false;

        [Header("Bloqueo del Eje Vertical (Clamp)")]
        [Tooltip("Angulo minimo de vision hacia abajo (limite -80 grados).")]
        [Range(-89f, 0f)]
        [SerializeField] private float _minPitch = -80f;

        [Tooltip("Angulo maximo de vision hacia arriba (limite 80 grados).")]
        [Range(0f, 89f)]
        [SerializeField] private float _maxPitch = 80f;

        // ════════════════════════════════════════════════════════════════════
        // RETROCESO (RECOIL SPRING)
        // ════════════════════════════════════════════════════════════════════

        [Header("Amortiguacion de Retroceso (Armas/Impactos)")]
        [SerializeField] private float _recoilSnappiness = 20f;
        [SerializeField] private float _recoilReturnSpeed = 9f;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private float _pitch;
        private bool _canLook = true;
        private Vector3 _currentRecoil;
        private Vector3 _targetRecoil;

        public bool CanLook => _canLook;
        public float CurrentPitch => _pitch;
        public Transform PlayerBody => _playerBody;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            ResolvePlayerBody();
            InitializeCursor();

            // Sincronizar el pitch inicial con la rotacion local actual
            Vector3 currentEuler = transform.localEulerAngles;
            _pitch = currentEuler.x > 180f ? currentEuler.x - 360f : currentEuler.x;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        }

        private void Start()
        {
            ResolvePlayerBody();
        }

        private void Update()
        {
            UpdateRecoil();

            if (!_canLook || Cursor.lockState != CursorLockMode.Locked) return;

            HandleMouseLook();
        }

        // ════════════════════════════════════════════════════════════════════
        // RESOLUCION AUTOMATICA DE REFERENCIAS
        // ════════════════════════════════════════════════════════════════════

        private void ResolvePlayerBody()
        {
            if (_playerBody == null)
            {
                // Buscar primero en el padre directo (capsula raiz)
                if (transform.parent != null)
                {
                    _playerBody = transform.parent;
                }
                else
                {
                    // Fallback al root o CharacterController superior
                    var cc = GetComponentInParent<CharacterController>();
                    if (cc != null)
                    {
                        _playerBody = cc.transform;
                    }
                    else
                    {
                        _playerBody = transform.root;
                    }
                }
            }
        }

        private void InitializeCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // PROCESAMIENTO DE VISION CON RATON (MOUSE LOOK)
        // ════════════════════════════════════════════════════════════════════

        private void HandleMouseLook()
        {
            float mouseX = Input.GetAxisRaw("Mouse X") * _sensitivityX;
            float mouseY = Input.GetAxisRaw("Mouse Y") * _sensitivityY;

            if (_invertY) mouseY = -mouseY;

            // 1. ROTACION VERTICAL (Cámara local)
            _pitch -= mouseY;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

            // Aplicar la rotacion combinada de pitch + resorte de retroceso
            transform.localRotation = Quaternion.Euler(_pitch + _currentRecoil.x, _currentRecoil.y, _currentRecoil.z);

            // 2. ROTACION HORIZONTAL (Cuerpo completo del jugador)
            if (_playerBody != null && Mathf.Abs(mouseX) > 0.0001f)
            {
                _playerBody.Rotate(Vector3.up * mouseX);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RESORTE DE RETROCESO (RECOIL)
        // ════════════════════════════════════════════════════════════════════

        private void UpdateRecoil()
        {
            _targetRecoil = Vector3.Lerp(_targetRecoil, Vector3.zero, _recoilReturnSpeed * Time.deltaTime);
            _currentRecoil = Vector3.Slerp(_currentRecoil, _targetRecoil, _recoilSnappiness * Time.deltaTime);
        }

        /// <summary>
        /// Aplica una sacudida procedimental de retroceso a la camara al disparar.
        /// </summary>
        public void ApplyRecoil(float verticalKick, float horizontalSpread)
        {
            float randomYaw = Random.Range(-horizontalSpread, horizontalSpread);
            _targetRecoil += new Vector3(-verticalKick, randomYaw, 0f);
        }

        // ════════════════════════════════════════════════════════════════════
        // CONTROL PUBLICO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Permite congelar o habilitar la rotacion de la camara (cinematicas, menus, QTEs).
        /// </summary>
        public void SetLookEnabled(bool enabled)
        {
            _canLook = enabled;
        }

        /// <summary>
        /// Permite reasignar el cuerpo raiz del jugador en tiempo de ejecucion.
        /// </summary>
        public void SetPlayerBody(Transform body)
        {
            _playerBody = body;
        }

        /// <summary>
        /// Ajusta la sensibilidad de la camara en tiempo de ejecucion (menu de opciones).
        /// </summary>
        public void SetSensitivity(float sensitivity)
        {
            _sensitivityX = sensitivity;
            _sensitivityY = sensitivity;
        }
    }
}

