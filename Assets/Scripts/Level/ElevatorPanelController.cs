// ============================================================================
// ElevatorPanelController.cs — Panel de Control Interactivo de Elevadores
// ============================================================================
// Gestiona la interacción en primera persona con el panel de mando de los
// elevadores de carga (SM_Elevador_Carga_8x8) en "Sector Cero".
//
// Responsabilidades:
// - Detección de proximidad y orientación del jugador (Trigger + Raycast opcional).
// - Retroalimentación visual en pantalla (UI OnGUI / Canvas).
// - Secuencia de activación con bloqueo temporal de movimiento del jugador.
// - Reproducción de efectos mecánicos (relés, arranque de motor y vibración de cámara).
// - Activación de parpadeo eléctrico agresivo en las luces de cabina (LightFlicker).
// - Comunicación directa con el sistema de pisos procedural (LevelGenerator / GameManager)
//   para ejecutar la transición al siguiente nivel o piso de la torre.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Environment;
using DemenciaNoLimite.Horror;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Level
{
    public enum ElevatorDirection
    {
        SiguientePiso,
        BajarPiso,
        SubirPiso
    }

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

        [Header("Configuración de Rol y Estado Inicial")]
        [Tooltip("Define si el panel de control inicia habilitado para interacción. En elevadores de spawn/entrada debe ser false para evitar escapes iniciales, y en elevadores de salida debe ser true.")]
        [SerializeField] private bool _startEnabled = true;

        [Tooltip("Indica si el panel está actualmente activo y responde a la interacción del jugador.")]
        [SerializeField] private bool _isInteractable = true;

        [Header("Interfaz de Usuario (HUD)")]
        [Tooltip("Texto descriptivo mostrado en la pantalla cuando el panel es interactuable.")]
        [SerializeField] private string _promptActionText = "PANEL DE CONTROL DE ELEVADOR";

        [Tooltip("Subtexto de instrucciones mostrado bajo la acción principal.")]
        [SerializeField] private string _promptSubText = "[E] INICIAR SECUENCIA";

        [Tooltip("Elemento de UI en escena (Canvas/WorldSpace) opcional que se activa al estar cerca.")]
        [SerializeField] private GameObject _externalPromptUI;

        // ════════════════════════════════════════════════════════════════════
        // SECUENCIA DE AUDIO, LUCES Y BLOQUEO DEL JUGADOR
        // ════════════════════════════════════════════════════════════════════

        [Header("Tiempos de Secuencia")]
        [Tooltip("Duración en segundos de la secuencia mecánica antes de ejecutar el cambio de piso.")]
        [Range(1f, 8f)]
        [SerializeField] private float _sequenceDuration = 2.6f;

        [Tooltip("Si está activo, congela el movimiento del jugador mientras la secuencia esté en marcha.")]
        [SerializeField] private bool _lockPlayerMovement = true;

        [Header("Efectos de Audio Mecánico")]
        [Tooltip("AudioSource para efectos 3D. Si no se asigna, se busca o añade automáticamente.")]
        [SerializeField] private AudioSource _audioSource;

        [Tooltip("Clip de sonido al accionar el interruptor/relé inicial.")]
        [SerializeField] private AudioClip _relayClickClip;

        [Tooltip("Clip de arranque del motor de tracción y engranajes pesados.")]
        [SerializeField] private AudioClip _motorStartClip;

        [Tooltip("Clip de zumbido continuo o traqueteo de descenso.")]
        [SerializeField] private AudioClip _engineLoopClip;

        [Header("Comportamiento de Iluminación (LightFlicker)")]
        [Tooltip("Luces de la cabina del elevador que parpadearán violentamente durante la activación.")]
        [SerializeField] private List<LightFlicker> _cabinLights = new List<LightFlicker>();

        [Tooltip("Frecuencia mínima de espera en parpadeo agresivo (segundos).")]
        [SerializeField] private float _aggressiveMinWait = 0.015f;

        [Tooltip("Frecuencia máxima de espera en parpadeo agresivo (segundos).")]
        [SerializeField] private float _aggressiveMaxWait = 0.05f;

        [Tooltip("Probabilidad de apagón instantáneo durante el arranque del motor (0.0 a 1.0).")]
        [Range(0f, 1f)]
        [SerializeField] private float _aggressiveBlackoutChance = 0.45f;

        [Header("Efectos de Cámara (Screen Shake)")]
        [Tooltip("Intensidad de trauma aplicado a la cámara por la vibración del motor.")]
        [Range(0f, 1f)]
        [SerializeField] private float _cameraShakeTrauma = 0.40f;

        // ════════════════════════════════════════════════════════════════════
        // INTEGRACIÓN CON EL SISTEMA DE PISOS / TORRE
        // ════════════════════════════════════════════════════════════════════

        [Header("Transición de Pisos")]
        [Tooltip("Dirección del desplazamiento en la torre/sector.")]
        [SerializeField] private ElevatorDirection _direction = ElevatorDirection.SiguientePiso;

        [Tooltip("Referencia explícita a LevelGenerator. Si no se asigna, se buscará automáticamente en la escena.")]
        [SerializeField] private LevelGenerator _levelGenerator;

        [Tooltip("Evento de Unity configurable desde el Inspector para disparar cinemáticas o integraciones personalizadas.")]
        [SerializeField] private UnityEvent _onFloorTransitionInvoked;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y CACHÉ
        // ════════════════════════════════════════════════════════════════════

        private Collider _triggerCollider;
        private Transform _playerTransform;
        private Camera _mainCamera;
        private FirstPersonController _cachedPlayerController;
        private CharacterController _cachedCharacterController;

        private bool _isPlayerInTrigger = false;
        private bool _isPlayerLookingAtPanel = false;
        private bool _isActivating = false;
        private bool _isSequenceCompleted = false;

        // Caché de parámetros de iluminación originales para restauración
        private struct LightOriginalParams
        {
            public float minInt;
            public float maxInt;
            public float minWait;
            public float maxWait;
            public float blackout;
        }
        private readonly List<LightOriginalParams> _originalLightParams = new List<LightOriginalParams>();

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Se dispara al entrar o salir del radio interactuable del panel.
        /// </summary>
        public event Action<bool> OnPlayerProximityChanged;

        /// <summary>
        /// Se dispara al iniciar la secuencia de activación mecánica del panel.
        /// </summary>
        public event Action OnPanelActivationStarted;

        /// <summary>
        /// Se dispara al completarse la secuencia mecánica previa al cambio de piso.
        /// </summary>
        public event Action OnPanelSequenceFinished;

        /// <summary>
        /// Se dispara en el momento exacto en que se solicita la transición de piso. Provee el piso destino.
        /// </summary>
        public event Action<int> OnFloorTransitionTriggered;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public bool IsPlayerInRange => _isInteractable && _isPlayerInTrigger && (!_requireLookingAtPanel || _isPlayerLookingAtPanel);
        public bool IsActivating => _isActivating;
        public bool IsSequenceCompleted => _isSequenceCompleted;
        public bool IsInteractable => _isInteractable;
        public bool StartEnabled { get => _startEnabled; set => _startEnabled = value; }
        public KeyCode InteractKey => _interactKey;
        public ElevatorDirection Direction { get => _direction; set => _direction = value; }

        /// <summary>
        /// Habilita o deshabilita la capacidad del panel para ser accionado por el jugador.
        /// Utilizado por el generador de niveles para silenciar el elevador inicial y activar el de salida.
        /// </summary>
        public void HabilitarPanel(bool habilitar)
        {
            _isInteractable = habilitar;
            enabled = habilitar;
            ActualizarUIExterna();
            Debug.Log($"[ElevatorPanelController] Panel '{name}' en elevador '{transform.root.name}' {(habilitar ? "HABILITADO" : "DESHABILITADO")}.");
        }

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _isInteractable = _startEnabled;

            _triggerCollider = GetComponent<Collider>();
            if (_triggerCollider != null && !_triggerCollider.isTrigger)
            {
                _triggerCollider.isTrigger = true;
            }

            InicializarAudioSource();
            LocalizarLucesCabina();
            ResolverGeneradorDeNivel();
            _mainCamera = Camera.main;

            if (!_startEnabled)
            {
                enabled = false;
            }
        }

        private void Start()
        {
            _isInteractable = _startEnabled;

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_levelGenerator == null)
            {
                ResolverGeneradorDeNivel();
            }

            if (_externalPromptUI != null)
            {
                _externalPromptUI.SetActive(false);
            }
        }

        private void Update()
        {
            if (!_isInteractable || _isSequenceCompleted || _isActivating) return;

            if (_isPlayerInTrigger)
            {
                ActualizarVerificacionVisual();

                // Flujo estricto: El jugador debe estar en el trigger de la consola, mirando hacia ella
                // y presionar expresamente la tecla [E] (Input.GetKeyDown(KeyCode.E)).
                if (IsPlayerInRange && (Input.GetKeyDown(_interactKey) || Input.GetKeyDown(KeyCode.E)))
                {
                    IniciarSecuenciaActivacion();
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // INICIALIZACIÓN Y CACHÉ
        // ════════════════════════════════════════════════════════════════════

        private void ResolverGeneradorDeNivel()
        {
            if (_levelGenerator == null)
            {
                _levelGenerator = FindAnyObjectByType<LevelGenerator>();
            }
        }

        private void InicializarAudioSource()
        {
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }

            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 1.0f; // Audio 3D industrial
                _audioSource.minDistance = 2.0f;
                _audioSource.maxDistance = 16.0f;
                _audioSource.rolloffMode = AudioRolloffMode.Linear;
            }
        }

        private void LocalizarLucesCabina()
        {
            if (_cabinLights == null || _cabinLights.Count == 0)
            {
                _cabinLights = new List<LightFlicker>();

                // 1. Buscar en hijos directos
                _cabinLights.AddRange(GetComponentsInChildren<LightFlicker>());

                // 2. Si no hay en hijos, buscar en la raíz del prefab del elevador
                if (_cabinLights.Count == 0 && transform.root != null)
                {
                    _cabinLights.AddRange(transform.root.GetComponentsInChildren<LightFlicker>());
                }
            }

            // Guardar configuración original de las luces
            _originalLightParams.Clear();
            foreach (var light in _cabinLights)
            {
                if (light != null)
                {
                    _originalLightParams.Add(new LightOriginalParams
                    {
                        minInt = light.MinIntensity,
                        maxInt = light.MaxIntensity,
                        minWait = light.MinWaitTime,
                        maxWait = light.MaxWaitTime,
                        blackout = light.BlackoutChance
                    });
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DETECCIÓN FÍSICA Y VISUAL
        // ════════════════════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            // Seguridad: Ninguna función de cambio de nivel (LevelGenerator) se ejecuta automáticamente aquí.
            if (!EsJugador(other)) return;

            _playerTransform = other.transform;
            _isPlayerInTrigger = true;
            CachearComponentesJugador(other.gameObject);
            ActualizarVerificacionVisual();

            OnPlayerProximityChanged?.Invoke(IsPlayerInRange);
            ActualizarUIExterna();
        }

        private void OnTriggerStay(Collider other)
        {
            // Seguridad: Mantiene la detección del jugador dentro del trigger sin invocar ninguna transición.
            if (!_isInteractable || _isSequenceCompleted || _isActivating) return;

            if (!_isPlayerInTrigger && EsJugador(other))
            {
                _playerTransform = other.transform;
                _isPlayerInTrigger = true;
                CachearComponentesJugador(other.gameObject);
                ActualizarVerificacionVisual();
            }
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
            if (other.GetComponentInParent<FirstPersonController>() != null) return true;
            if (other.GetComponentInParent<PlayerManager>() != null) return true;
            return false;
        }

        private void CachearComponentesJugador(GameObject playerObj)
        {
            _cachedPlayerController = playerObj.GetComponentInParent<FirstPersonController>();
            if (_cachedPlayerController == null && PlayerManager.Instance != null)
            {
                _cachedPlayerController = PlayerManager.Instance.Controller;
            }

            _cachedCharacterController = playerObj.GetComponentInParent<CharacterController>();
            if (_cachedCharacterController == null && PlayerManager.Instance != null)
            {
                _cachedCharacterController = PlayerManager.Instance.CharacterController;
            }
        }

        private void ActualizarVerificacionVisual()
        {
            if (!_requireLookingAtPanel)
            {
                _isPlayerLookingAtPanel = true;
                ActualizarUIExterna();
                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null && _playerTransform != null)
                {
                    _mainCamera = _playerTransform.GetComponentInChildren<Camera>();
                }
                if (_mainCamera == null)
                {
                    _mainCamera = FindAnyObjectByType<Camera>();
                }
            }

            if (_mainCamera == null)
            {
                _isPlayerLookingAtPanel = true;
                ActualizarUIExterna();
                return;
            }

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
        // SECUENCIA DE ACTIVACIÓN MECÁNICA Y AUDIO-VISUAL
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Inicia la rutina de activación del elevador bloqueando al jugador,
        /// desatando chispazos en las luces y arrancando el motor mecánico.
        /// </summary>
        public void IniciarSecuenciaActivacion()
        {
            if (_isActivating || _isSequenceCompleted) return;

            StartCoroutine(RutinaSecuenciaActivacion());
        }

        private IEnumerator RutinaSecuenciaActivacion()
        {
            _isActivating = true;
            ActualizarUIExterna();

            Debug.Log($"[ElevatorPanelController] Iniciando secuencia de arranque de elevador en '{name}'...");
            OnPanelActivationStarted?.Invoke();

            // Desactivar temporalmente cualquier sensor pasivo en el mismo prefab para evitar doble disparo
            DesactivarTriggersSecundarios();

            // 1. Bloquear temporalmente el movimiento físico del jugador
            if (_lockPlayerMovement)
            {
                BloquearMovimientoJugador(true);
            }

            // 2. Sonido inicial: Switch/relé electromagnético
            ReproducirAudioSeguro(_relayClickClip, 1.0f);

            // Pequeña pausa de tensión de 0.22s antes del arranque del motor
            yield return new WaitForSeconds(0.22f);

            // 3. Activar parpadeo violento y errático en las luces de cabina (LightFlicker)
            ActivarParpadeoAgresivoLuces(true);

            // 4. Sonido de arranque de motor y vibración de cámara
            ReproducirAudioSeguro(_motorStartClip, 1.0f);
            IniciarLoopAudioSeguro(_engineLoopClip);

            // Sacudida de cámara (Screen Shake) por la tracción mecánica
            if (ScreenShakeController.Instance != null && _cameraShakeTrauma > 0f)
            {
                ScreenShakeController.Instance.AddTrauma(_cameraShakeTrauma);
            }

            // Esperar el tiempo de tensión mecánica
            float remainingTime = Mathf.Max(0.5f, _sequenceDuration - 0.22f);
            float timer = 0f;

            while (timer < remainingTime)
            {
                timer += Time.deltaTime;

                // Micro-temblores continuos durante el descenso
                if (ScreenShakeController.Instance != null && timer % 0.4f < Time.deltaTime)
                {
                    ScreenShakeController.Instance.AddTrauma(0.08f);
                }

                yield return null;
            }

            Debug.Log("[ElevatorPanelController] Secuencia mecánica completada. Ejecutando cambio de piso...");
            DetenerLoopAudioSeguro();

            _isSequenceCompleted = true;
            _isActivating = false;

            OnPanelSequenceFinished?.Invoke();

            // 5. Ejecutar la transición de piso en la torre procedural
            EjecutarTransicionDePiso();
        }

        private void DesactivarTriggersSecundarios()
        {
            if (transform.root == null) return;

            // 1. Desactivar y neutralizar de forma agresiva cualquier ElevatorExitTrigger
            var exitTriggers = transform.root.GetComponentsInChildren<ElevatorExitTrigger>(true);
            foreach (var exitTrigger in exitTriggers)
            {
                if (exitTrigger != null)
                {
                    exitTrigger.enabled = false;
                    Collider c = exitTrigger.GetComponent<Collider>();
                    if (c != null) c.enabled = false;
                    if (exitTrigger.gameObject.name.Contains("ZonaTrigger"))
                    {
                        exitTrigger.gameObject.SetActive(false);
                    }
                }
            }

            // 2. Desactivar el objeto ZonaTrigger si existe en la jerarquía del elevador
            Transform zona = transform.root.Find("ZonaTrigger");
            if (zona != null)
            {
                zona.gameObject.SetActive(false);
            }
        }

        private void BloquearMovimientoJugador(bool bloquear)
        {
            if (_cachedPlayerController == null)
            {
                if (_playerTransform != null) _cachedPlayerController = _playerTransform.GetComponentInParent<FirstPersonController>();
                if (_cachedPlayerController == null && PlayerManager.Instance != null) _cachedPlayerController = PlayerManager.Instance.Controller;
                if (_cachedPlayerController == null)
                {
                    GameObject p = GameObject.FindGameObjectWithTag(_playerTag);
                    if (p != null) _cachedPlayerController = p.GetComponent<FirstPersonController>();
                }
            }

            if (_cachedPlayerController != null)
            {
                _cachedPlayerController.SetMovementEnabled(!bloquear);
            }

            if (bloquear)
            {
                if (_cachedCharacterController == null)
                {
                    if (_playerTransform != null) _cachedCharacterController = _playerTransform.GetComponentInParent<CharacterController>();
                    if (_cachedCharacterController == null && PlayerManager.Instance != null) _cachedCharacterController = PlayerManager.Instance.CharacterController;
                }

                if (PlayerManager.Instance != null)
                {
                    PlayerManager.Instance.SetPlayerFrozen(bloquear);
                }
            }
            else
            {
                if (PlayerManager.Instance != null)
                {
                    PlayerManager.Instance.SetPlayerFrozen(false);
                }
            }
        }

        private void ActivarParpadeoAgresivoLuces(bool agresivo)
        {
            for (int i = 0; i < _cabinLights.Count; i++)
            {
                var light = _cabinLights[i];
                if (light == null) continue;

                if (agresivo)
                {
                    light.SetDynamicParameters(
                        0.0f,
                        light.MaxIntensity * 1.35f,
                        _aggressiveMinWait,
                        _aggressiveMaxWait,
                        _aggressiveBlackoutChance
                    );
                }
                else if (i < _originalLightParams.Count)
                {
                    var orig = _originalLightParams[i];
                    light.SetDynamicParameters(orig.minInt, orig.maxInt, orig.minWait, orig.maxWait, orig.blackout);
                }
            }
        }

        private void ReproducirAudioSeguro(AudioClip clip, float volumen = 1.0f)
        {
            if (_audioSource == null) return;

            if (clip != null)
            {
                _audioSource.PlayOneShot(clip, volumen);
            }
            else
            {
                Debug.Log("[ElevatorPanelController] Clip de audio no asignado (placeholder), continuando sin sonido...");
            }
        }

        private void IniciarLoopAudioSeguro(AudioClip clip)
        {
            if (_audioSource == null) return;

            if (clip != null)
            {
                _audioSource.clip = clip;
                _audioSource.loop = true;
                _audioSource.Play();
            }
            else
            {
                Debug.Log("[ElevatorPanelController] Clip de bucle de motor no asignado (placeholder), omitiendo loop...");
            }
        }

        private void DetenerLoopAudioSeguro()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
                _audioSource.loop = false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // EJECUCIÓN DEL CAMBIO DE PISO PROCEDURAL
        // ════════════════════════════════════════════════════════════════════

        private void EjecutarTransicionDePiso()
        {
            int targetFloor = 1;

            if (GameManager.Instance != null)
            {
                targetFloor = _direction == ElevatorDirection.BajarPiso
                    ? GameManager.Instance.CurrentFloorNumber + 1
                    : (_direction == ElevatorDirection.SubirPiso
                        ? Mathf.Max(1, GameManager.Instance.CurrentFloorNumber - 1)
                        : GameManager.Instance.CurrentFloorNumber + 1);
            }
            else if (_levelGenerator != null)
            {
                targetFloor = _levelGenerator.PisoActual + 1;
            }

            OnFloorTransitionTriggered?.Invoke(targetFloor);
            _onFloorTransitionInvoked?.Invoke();

            // 1. Prioridad: LevelGenerator si existe en escena (maneja regeneración procedural de Sector Cero)
            if (_levelGenerator == null)
            {
                ResolverGeneradorDeNivel();
            }

            if (_levelGenerator != null)
            {
                Debug.Log($"[ElevatorPanelController] Solicitando GenerarSiguientePiso() a LevelGenerator (Piso actual: {_levelGenerator.PisoActual})...");
                _levelGenerator.GenerarSiguientePiso();
                return;
            }

            // 2. Alternativa: GameManager (arquitectura de torre por teletransporte vertical)
            if (GameManager.Instance != null)
            {
                Debug.Log($"[ElevatorPanelController] Solicitando TeleportToFloor({targetFloor}) a GameManager...");
                GameManager.Instance.TeleportToFloor(targetFloor);
                BloquearMovimientoJugador(false);
                return;
            }

            Debug.LogError("[ElevatorPanelController] No se encontró ni LevelGenerator ni GameManager en la escena para procesar el cambio de piso.");
            BloquearMovimientoJugador(false);
        }

        // ════════════════════════════════════════════════════════════════════
        // RENDERIZADO DE INTERFAZ EN PANTALLA (ON-GUI ATMOSFÉRICO)
        // ════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (!IsPlayerInRange || _isActivating || _isSequenceCompleted) return;

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            Vector3 worldAnchor = transform.position + Vector3.up * 0.25f;
            Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldAnchor);

            if (screenPos.z < 0.2f || screenPos.z > 20f) return;

            float alpha = Mathf.Clamp01(1f - (screenPos.z / 15f));
            float boxWidth = 280f;
            float boxHeight = 58f;
            float posX = (Screen.width * 0.5f) - (boxWidth * 0.5f);
            float posY = Screen.height * 0.68f;

            GUI.color = new Color(0.04f, 0.05f, 0.06f, alpha * 0.85f);
            GUI.Box(new Rect(posX, posY, boxWidth, boxHeight), GUIContent.none);

            GUI.color = new Color(0.2f, 0.85f, 0.95f, alpha * 0.9f);
            GUI.Box(new Rect(posX, posY, boxWidth, 2f), GUIContent.none);

            var styleTitle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 13
            };
            styleTitle.normal.textColor = new Color(0.95f, 0.95f, 0.95f, alpha);
            GUI.Label(new Rect(posX, posY + 6, boxWidth, 22), _promptActionText, styleTitle);

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

        // ════════════════════════════════════════════════════════════════════
        // MENÚS CONTEXTUALES DE DEPURACIÓN EN EDITOR
        // ════════════════════════════════════════════════════════════════════

        [ContextMenu("Probar / Iniciar Secuencia de Activación")]
        private void TestIniciarSecuencia()
        {
            IniciarSecuenciaActivacion();
        }

        [ContextMenu("Probar / Forzar Cambio de Piso")]
        private void TestForzarCambioPiso()
        {
            EjecutarTransicionDePiso();
        }
    }
}
