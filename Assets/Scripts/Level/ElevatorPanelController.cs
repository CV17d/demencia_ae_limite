// ============================================================================
// ElevatorPanelController.cs — Panel de Control Interactivo de Elevadores
// ============================================================================
// Gestiona la interacción en primera persona con el panel de mando de los
// elevadores de carga (SM_Elevador_Carga_8x8) en "Sector Cero".
//
// Responsabilidades:
// - Detección de proximidad y orientación del jugador (Trigger + Raycast opcional).
// - Retroalimentación visual en pantalla (UI OnGUI / Canvas).
// - Detección de entrada de usuario (Tecla [E] por defecto).
// ============================================================================

using System;
using UnityEngine;

namespace DemenciaNoLimite.Level
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class ElevatorPanelController : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE DETECCIÓN Y ENTRADA
        // ════════════════════════════════════════════════════════════════════

        [Header("Filtros de Detección")]
        [Tooltip("Tag requerido para que un objeto sea identificado como jugador.")]
        [SerializeField] private string _playerTag = "Player";

        [Tooltip("Distancia máxima de interacción si se utiliza verificación por Raycast o distancia directa.")]
        [SerializeField] private float _interactionDistance = 2.8f;

        [Tooltip("Si está activo, además del trigger se requiere que la cámara del jugador esté apuntando hacia el panel.")]
        [SerializeField] private bool _requireLookingAtPanel = true;

        [Tooltip("Ángulo máximo de tolerancia visual (grados) para considerar que el jugador mira al panel.")]
        [Range(10f, 90f)]
        [SerializeField] private float _lookAtAngleThreshold = 65f;

        [Header("Entrada de Usuario")]
        [Tooltip("Tecla requerida para accionar el panel.")]
        [SerializeField] private KeyCode _interactKey = KeyCode.E;

        [Header("Interfaz de Usuario (HUD)")]
        [Tooltip("Texto descriptivo mostrado en la pantalla cuando el panel es interactuable.")]
        [SerializeField] private string _promptActionText = "ACCIONAR PANEL DE ELEVADOR";

        [Tooltip("Subtexto de instrucciones mostrado bajo la acción principal.")]
        [SerializeField] private string _promptSubText = "[E] INICIAR SECUENCIA DE DESCENSO";

        [Tooltip("Elemento de UI en escena (Canvas/WorldSpace) opcional que se activa al estar cerca.")]
        [SerializeField] private GameObject _externalPromptUI;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private Collider _triggerCollider;
        private Transform _playerTransform;
        private Camera _mainCamera;
        private bool _isPlayerInTrigger = false;
        private bool _isPlayerLookingAtPanel = false;
        private bool _isActivating = false;
        private bool _isSequenceCompleted = false;

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Se dispara al entrar o salir del radio interactuable del panel.
        /// </summary>
        public event Action<bool> OnPlayerProximityChanged;

        /// <summary>
        /// Se dispara al presionar con éxito la tecla de interacción.
        /// </summary>
        public event Action OnPanelInteracted;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public bool IsPlayerInRange => _isPlayerInTrigger && (!_requireLookingAtPanel || _isPlayerLookingAtPanel);
        public bool IsActivating => _isActivating;
        public bool IsSequenceCompleted => _isSequenceCompleted;
        public KeyCode InteractKey => _interactKey;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _triggerCollider = GetComponent<Collider>();
            if (_triggerCollider != null && !_triggerCollider.isTrigger)
            {
                _triggerCollider.isTrigger = true;
            }

            _mainCamera = Camera.main;
        }

        private void Start()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_externalPromptUI != null)
            {
                _externalPromptUI.SetActive(false);
            }
        }

        private void Update()
        {
            if (_isSequenceCompleted || _isActivating) return;

            if (_isPlayerInTrigger)
            {
                ActualizarVerificacionVisual();

                if (IsPlayerInRange && Input.GetKeyDown(_interactKey))
                {
                    EjecutarInteraccion();
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DETECCIÓN FÍSICA Y VISUAL
        // ════════════════════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            if (!EsJugador(other)) return;

            _playerTransform = other.transform;
            _isPlayerInTrigger = true;
            ActualizarVerificacionVisual();

            OnPlayerProximityChanged?.Invoke(IsPlayerInRange);
            ActualizarUIExterna();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!EsJugador(other)) return;

            _isPlayerInTrigger = false;
            _isPlayerLookingAtPanel = false;
            _playerTransform = null;

            OnPlayerProximityChanged?.Invoke(false);
            ActualizarUIExterna();
        }

        private bool EsJugador(Collider other)
        {
            if (other.CompareTag(_playerTag)) return true;
            if (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag(_playerTag)) return true;
            return false;
        }

        private void ActualizarVerificacionVisual()
        {
            if (!_requireLookingAtPanel)
            {
                _isPlayerLookingAtPanel = true;
                ActualizarUIExterna();
                return;
            }

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                _isPlayerLookingAtPanel = true;
                ActualizarUIExterna();
                return;
            }

            // Vector desde la cámara del jugador hacia el panel
            Vector3 dirToPanel = (transform.position - _mainCamera.transform.position).normalized;
            float angle = Vector3.Angle(_mainCamera.transform.forward, dirToPanel);

            bool previousState = _isPlayerLookingAtPanel;
            _isPlayerLookingAtPanel = angle <= _lookAtAngleThreshold;

            if (previousState != _isPlayerLookingAtPanel)
            {
                OnPlayerProximityChanged?.Invoke(IsPlayerInRange);
                ActualizarUIExterna();
            }
        }

        private void ActualizarUIExterna()
        {
            if (_externalPromptUI != null)
            {
                _externalPromptUI.SetActive(IsPlayerInRange && !_isActivating && !_isSequenceCompleted);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PROCESAMIENTO DE INTERACCIÓN
        // ════════════════════════════════════════════════════════════════════

        private void EjecutarInteraccion()
        {
            if (_isActivating || _isSequenceCompleted) return;

            Debug.Log($"[ElevatorPanelController] Jugador interactuó con el panel '{name}' mediante tecla [{_interactKey}].");
            OnPanelInteracted?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════════
        // RENDERIZADO DE INTERFAZ EN PANTALLA (ON-GUI ATMOSFÉRICO)
        // ════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (!IsPlayerInRange || _isActivating || _isSequenceCompleted) return;

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            // Proyección al espacio de pantalla centrada sobre el panel físico
            Vector3 worldAnchor = transform.position + Vector3.up * 0.25f;
            Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldAnchor);

            // Descartar si el panel queda detrás de la cámara
            if (screenPos.z < 0.2f || screenPos.z > 20f) return;

            float alpha = Mathf.Clamp01(1f - (screenPos.z / 15f));
            float boxWidth = 280f;
            float boxHeight = 58f;
            float posX = (Screen.width * 0.5f) - (boxWidth * 0.5f);
            float posY = Screen.height * 0.68f; // Posicionado ergonómicamente bajo la retícula central

            // Fondo translúcido de grado industrial
            GUI.color = new Color(0.04f, 0.05f, 0.06f, alpha * 0.85f);
            GUI.Box(new Rect(posX, posY, boxWidth, boxHeight), GUIContent.none);

            // Borde superior iluminado (estilo terminal de Sector Cero)
            GUI.color = new Color(0.2f, 0.85f, 0.95f, alpha * 0.9f);
            GUI.Box(new Rect(posX, posY, boxWidth, 2f), GUIContent.none);

            // Texto de acción principal
            var styleTitle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 13
            };
            styleTitle.normal.textColor = new Color(0.95f, 0.95f, 0.95f, alpha);
            GUI.Label(new Rect(posX, posY + 6, boxWidth, 22), _promptActionText, styleTitle);

            // Texto de tecla de interacción
            var styleSub = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };
            styleSub.normal.textColor = new Color(0.25f, 0.9f, 0.95f, alpha);
            GUI.Label(new Rect(posX, posY + 28, boxWidth, 20), _promptSubText, styleSub);

            GUI.color = Color.white;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, _interactionDistance);
        }
    }
}
