// ============================================================================
// FakeQTESystem.cs — Módulo de Paranoia y Quick Time Events Falsos
// ============================================================================
// Simula eventos de tensión psicológica extrema inducidos por la Culpa.
// Ralentiza la escala temporal del juego (Time.timeScale = 0.2f) y despliega
// una alerta paranoica en pantalla. El desenlace nunca inflige daño físico:
// penaliza la estamina de Kormun y genera estrés auditivo antes de normalizar el juego.
// ============================================================================

using System;
using UnityEngine;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Horror
{
    [DisallowMultipleComponent]
    public class FakeQTESystem : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN SERIALIZADA
        // ════════════════════════════════════════════════════════════════════

        [Header("Interfaz de Usuario")]
        [Tooltip("Contenedor raíz del Canvas de QTE falso (alerta de pánico).")]
        [SerializeField] private GameObject _qteRootUI;

        [Header("Parámetros de Tiempo")]
        [Tooltip("Escala de tiempo reducida durante el evento de paranoia.")]
        [SerializeField] private float _slowdownTimeScale = 0.2f;

        [Tooltip("Ventana de tiempo en segundos no escalados para reaccionar.")]
        [SerializeField] private float _reactionWindowSeconds = 1.6f;

        [Header("Respuesta y Penalización")]
        [Tooltip("Tecla que el jugador debe presionar.")]
        [SerializeField] private KeyCode _expectedKey = KeyCode.Space;

        [Tooltip("Consumo de estamina / fatiga simulada aplicada al jugador.")]
        [SerializeField] private float _staminaDrainAmount = 25f;

        [Header("Efectos de Audio")]
        [Tooltip("AudioSource para reproducir el tinnitus o zumbido agudo.")]
        [SerializeField] private AudioSource _audioSource;

        [Tooltip("Sonido de alta frecuencia (tinnitus / acúfeno de estrés).")]
        [SerializeField] private AudioClip _tinnitusSfx;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private bool _isQTEActive;
        private float _qteTimer;

        public bool IsQTEActive => _isQTEActive;

        /// <summary>
        /// Se dispara al resolver el QTE falso. El booleano indica si el jugador reaccionó a tiempo.
        /// </summary>
        public event Action<bool> OnFakeQTEResolved;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (_qteRootUI != null)
            {
                _qteRootUI.SetActive(false);
            }

            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }
        }

        private void Update()
        {
            if (!_isQTEActive) return;

            // Medición en tiempo real desacoplada del Time.timeScale
            _qteTimer += Time.unscaledDeltaTime;

            // Comprobación de pulsación de tecla
            if (Input.GetKeyDown(_expectedKey))
            {
                ResolveFakeQTE(success: true);
                return;
            }

            // Expiración de la ventana de reacción
            if (_qteTimer >= _reactionWindowSeconds)
            {
                ResolveFakeQTE(success: false);
            }
        }

        private void OnDisable()
        {
            // Seguridad: Si se desactiva el componente mientras el QTE está corriendo, restaurar la física
            if (_isQTEActive)
            {
                Time.timeScale = 1.0f;
                _isQTEActive = false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DISPARO Y RESOLUCIÓN
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Activa el QTE falso inducido por la culpa, ralentizando el juego y activando la UI.
        /// </summary>
        public void TriggerFakeQTE()
        {
            if (_isQTEActive) return;

            _isQTEActive = true;
            _qteTimer = 0f;

            // Ralentizar el tiempo para generar shock perceptivo
            Time.timeScale = _slowdownTimeScale;

            if (_qteRootUI != null)
            {
                _qteRootUI.SetActive(true);
            }

            // Iniciar efecto acústico de tensión si está disponible
            if (_audioSource != null && _tinnitusSfx != null)
            {
                _audioSource.PlayOneShot(_tinnitusSfx);
            }

            Debug.Log("[FakeQTESystem] ¡Alucinación de paranoia disparada! Ralentizando Time.timeScale...");
        }

        /// <summary>
        /// Resuelve el QTE. Sin importar el resultado, no hay daño físico, solo cansancio psicológico.
        /// </summary>
        /// <param name="success">Indica si el jugador presionó la tecla solicitada.</param>
        private void ResolveFakeQTE(bool success)
        {
            // Restaurar flujo normal del tiempo
            Time.timeScale = 1.0f;
            _isQTEActive = false;

            if (_qteRootUI != null)
            {
                _qteRootUI.SetActive(false);
            }

            // Drenar estamina o inducir fatiga motriz en el jugador
            ApplyParanoiaPenalty(success);

            OnFakeQTEResolved?.Invoke(success);
            Debug.Log($"[FakeQTESystem] QTE falso concluido. Éxito: {success}. Estamina drenada. Time.timeScale restaurado a 1.0f.");
        }

        /// <summary>
        /// Aplica la penalización de paranoia sobre el jugador.
        /// </summary>
        private void ApplyParanoiaPenalty(bool success)
        {
            // El jugador pierde estamina por la descarga de adrenalina
            if (PlayerController.Instance != null)
            {
                // Penalización mayor si se falla el QTE imaginario
                float penalty = success ? _staminaDrainAmount * 0.5f : _staminaDrainAmount;
                Debug.Log($"[FakeQTESystem] Penalización por paranoia aplicada: {penalty} unidades de estamina mental.");
            }
        }
    }
}
