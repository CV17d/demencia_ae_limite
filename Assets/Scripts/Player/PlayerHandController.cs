// ============================================================================
// PlayerHandController.cs — Controlador de Manos e Interfaz Diegética en Primera Persona
// ============================================================================
// Gestiona la visualización 2D/UI de las manos del protagonista en "Sector Cero".
//
// Máquina de Estados:
// - Hidden: Manos ocultas (cinemáticas, nado, etc.).
// - HoldingItem: Mano abierta (mano demencia_E.png) con anclaje flotante (FloatingItemAnchor).
// - EquippedItem: Mano empuñada (mano demencia.png) para armas y combate.
// - BurnedByExplosion: Mano carbonizada (mano explosion demencia.png) con retorno automático (3s).
//
// Diseñado para alta fidelidad diegética, carga automática de assets y cero basura en memoria.
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DemenciaNoLimite.Player
{
    public enum HandState
    {
        Hidden,
        HoldingItem,
        EquippedItem,
        BurnedByExplosion
    }

    [DisallowMultipleComponent]
    public class PlayerHandController : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONSTANTES DE CARGA Y RUTAS
        // ════════════════════════════════════════════════════════════════════

        private const string PathManoEquipada = "Assets/Art/2D/Manos/mano demencia.png";
        private const string PathManoSosteniendo = "Assets/Art/2D/Manos/mano demencia_E.png";
        private const string PathManoQuemada = "Assets/Art/2D/Manos/mano explosion demencia.png";

        private const string ResourcesManoEquipada = "Manos/mano demencia";
        private const string ResourcesManoSosteniendo = "Manos/mano demencia_E";
        private const string ResourcesManoQuemada = "Manos/mano explosion demencia";

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE SPRITES (INSPECTOR O CARGA AUTOMÁTICA)
        // ════════════════════════════════════════════════════════════════════

        [Header("Sprites de Manos (Inspector o Carga por Código)")]
        [Tooltip("Mano empuñada para equipamiento / armas (mano demencia.png).")]
        [SerializeField] private Sprite _equippedHandSprite;

        [Tooltip("Mano abierta para ítems flotantes (mano demencia_E.png).")]
        [SerializeField] private Sprite _holdingHandSprite;

        [Tooltip("Mano quemada por proximidad a explosiones (mano explosion demencia.png).")]
        [SerializeField] private Sprite _burnedHandSprite;

        // ════════════════════════════════════════════════════════════════════
        // COMPONENTES DE RENDERIZADO VISUAL
        // ════════════════════════════════════════════════════════════════════

        [Header("Autogeneración de Canvas / UI (Cero Configuración Manual)")]
        [Tooltip("Si está activo, crea automáticamente el Canvas y el objeto ManoPersonaje si no existen.")]
        [SerializeField] private bool _autoGenerateCanvasIfMissing = true;

        [Tooltip("Dimensiones por defecto de la imagen de la mano en el Canvas (1920x1080).")]
        [SerializeField] private Vector2 _handSize = new Vector2(550f, 550f);

        [Tooltip("Desplazamiento adicional (offset) relativo a la esquina inferior derecha. Un valor negativo en X la desplaza hacia la izquierda.")]
        [SerializeField] private Vector2 _handOffset = new Vector2(-80f, 0f);

        [Header("Renderizado Visual (UI o 2D en Cámara)")]
        [Tooltip("Componente Image de Canvas UI donde se proyecta la mano (Recomendado para HUD).")]
        [SerializeField] private Image _handImage;

        [Tooltip("Componente SpriteRenderer alternativo si las manos se posicionan en el espacio 3D de la cámara.")]
        [SerializeField] private SpriteRenderer _handSpriteRenderer;

        // ════════════════════════════════════════════════════════════════════
        // ANCLAJE DE ÍTEM FLOTANTE (FLOATING ITEM ANCHOR)
        // ════════════════════════════════════════════════════════════════════

        [Header("Punto de Anclaje Flotante (Floating Item)")]
        [Tooltip("Punto de anclaje donde se posiciona el ítem u holograma sobre la palma de la mano abierta.")]
        [SerializeField] private Transform _floatingItemAnchor;

        [Tooltip("Si está activo, aplica una suave oscilación vertical de levitación al ítem flotante.")]
        [SerializeField] private bool _animateFloatingItem = true;

        [Tooltip("Velocidad de oscilación del ítem flotante.")]
        [SerializeField] private float _floatBobSpeed = 2.5f;

        [Tooltip("Amplitud vertical de oscilación en unidades locales.")]
        [SerializeField] private float _floatBobAmplitude = 0.04f;

        [Tooltip("Velocidad de rotación continua del ítem flotante en el eje Y (grados/segundo).")]
        [SerializeField] private float _floatRotateSpeed = 25f;

        // ════════════════════════════════════════════════════════════════════
        // BALANCEO PROCEDIMENTAL AL CAMINAR (HEAD / HAND BOBBING)
        // ════════════════════════════════════════════════════════════════════

        [Header("Balanceo Procedimental al Caminar (Bobbing)")]
        [Tooltip("Activa el balanceo rítmico de la mano al desplazarse.")]
        [SerializeField] private bool _enableHandBobbing = true;

        [Tooltip("Frecuencia base del vaivén de pasos al caminar.")]
        [SerializeField] private float _walkBobFrequency = 8.5f;

        [Tooltip("Multiplicador de frecuencia de pasos al correr.")]
        [SerializeField] private float _sprintBobMultiplier = 1.35f;

        [Tooltip("Amplitud de oscilación lateral (eje X).")]
        [SerializeField] private float _bobAmountX = 14f;

        [Tooltip("Amplitud de oscilación vertical (eje Y).")]
        [SerializeField] private float _bobAmountY = 16f;

        [Tooltip("Velocidad de interpolación para volver al reposo o suavizar el balanceo.")]
        [SerializeField] private float _bobSmoothing = 10f;

        // ════════════════════════════════════════════════════════════════════
        // REACCIÓN AL PARPADEO E ILUMINACIÓN AMBIENTAL
        // ════════════════════════════════════════════════════════════════════

        [Header("Reacción a Iluminación y Parpadeo (Lighting & Flicker)")]
        [Tooltip("Activa la reacción de brillo y color de la mano a las luces titilantes de la torre.")]
        [SerializeField] private bool _enableLightFlickerReaction = true;

        [Tooltip("Color base de la mano en condiciones ideales.")]
        [SerializeField] private Color _baseHandColor = Color.white;

        [Tooltip("Brillo mínimo en zonas de oscuridad o apagón absoluto.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _minAmbientBrightness = 0.35f;

        [Tooltip("Brillo máximo bajo focos intensos o picos de chisporroteo.")]
        [Range(1f, 2.5f)]
        [SerializeField] private float _maxLightBrightness = 1.35f;

        [Tooltip("Velocidad de respuesta e interpolación lumínica sobre la mano.")]
        [SerializeField] private float _lightLerpSpeed = 14f;

        [Tooltip("Radio en metros para muestrear la luz ambiental más cercana.")]
        [SerializeField] private float _lightSamplingRadius = 15f;

        [Tooltip("Intervalo en segundos para refrescar la búsqueda de la luz más cercana (Zero-GC).")]
        [SerializeField] private float _lightSearchInterval = 0.3f;

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE ESTADO BASE Y PERMANENCIA
        // ════════════════════════════════════════════════════════════════════

        [Header("Estado Base / Exploración Libre")]
        [Tooltip("Estado por defecto durante la exploración regular del jugador. La mano permanece siempre visible.")]
        [SerializeField] private HandState _defaultState = HandState.EquippedItem;

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE EFECTO QUEMADURA (BURNED BY EXPLOSION)
        // ════════════════════════════════════════════════════════════════════

        [Header("Temporizador de Quemadura por Explosión")]
        [Tooltip("Tiempo en segundos que permanece visible la mano quemada antes de restablecerse.")]
        [SerializeField] private float _burnDuration = 3.0f;

        [Tooltip("Estado al que regresará la mano tras finalizar la duración de la quemadura (nunca Hidden).")]
        [SerializeField] private HandState _stateAfterBurn = HandState.EquippedItem;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        [Header("Estado Actual (Solo Lectura)")]
        [SerializeField] private HandState _currentState = HandState.EquippedItem;
        private HandState _previousState = HandState.EquippedItem;

        private GameObject _currentFloatingItemInstance;
        private Coroutine _burnCoroutine;
        private Vector3 _anchorBaseLocalPosition;

        // Variables de bobbing e iluminación
        private RectTransform _handRect;
        private Vector2 _currentBobOffset = Vector2.zero;
        private float _bobTimer = 0f;

        private PlayerManager _playerManager;
        private CharacterController _characterController;
        private Camera _playerCamera;

        private Light _nearestLight;
        private Light[] _cachedLights;
        private float _nextLightSearchTime = 0f;
        private float _nextFullLightsCacheTime = 0f;
        private float _currentBrightness = 1f;

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Se dispara al cambiar el estado de la mano, proveyendo el nuevo estado activo.
        /// </summary>
        public event Action<HandState> OnHandStateChanged;

        /// <summary>
        /// Se dispara al instanciar o remover un ítem flotante en la palma.
        /// </summary>
        public event Action<GameObject> OnFloatingItemChanged;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public HandState CurrentState => _currentState;
        public HandState DefaultState => _defaultState;
        public Transform FloatingItemAnchor => _floatingItemAnchor;
        public GameObject CurrentFloatingItem => _currentFloatingItemInstance;
        public bool IsHoldingItem => _currentState == HandState.HoldingItem;
        public bool IsEquipped => _currentState == HandState.EquippedItem;
        public bool IsBurned => _currentState == HandState.BurnedByExplosion;
        public bool IsVisible => _currentState != HandState.Hidden;

        /// <summary>
        /// Desplazamiento base de la mano relativo a la esquina inferior derecha.
        /// Un valor negativo en X desplaza la mano hacia la izquierda.
        /// </summary>
        public Vector2 HandOffset
        {
            get => _handOffset;
            set
            {
                _handOffset = value;
                if (_handRect != null) _handRect.anchoredPosition = _handOffset + _currentBobOffset;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            AsegurarReferenciasRenderizado();
            AsegurarCargaDeSprites();
            AsegurarPuntoDeAnclaje();
            InicializarReferenciasJugador();
        }

        private void OnEnable()
        {
            SuscribirEventosInventario();
        }

        private void Start()
        {
            InicializarReferenciasJugador();

            // Garantizar que la mano comience siempre visible en el estado base configurado
            if (_currentState == HandState.Hidden)
            {
                _currentState = (_defaultState != HandState.Hidden) ? _defaultState : HandState.EquippedItem;
            }

            ActualizarVisualesEstado(_currentState);
        }

        private void Update()
        {
            // 1. Balanceo procedural al caminar / correr (Head/Hand Bobbing)
            if (_enableHandBobbing)
            {
                ActualizarBalanceoCaminar();
            }

            // 2. Reacción dinámica a la iluminación y parpadeo de luces en la torre
            if (_enableLightFlickerReaction)
            {
                ActualizarIluminacionDinamica();
            }

            // 3. Animación diegética de levitación suave sobre la palma
            if (_animateFloatingItem && _currentState == HandState.HoldingItem && _currentFloatingItemInstance != null && _floatingItemAnchor != null)
            {
                ActualizarAnimacionItemFlotante();
            }
        }

        private void OnDisable()
        {
            DesuscribirEventosInventario();

            if (_burnCoroutine != null)
            {
                StopCoroutine(_burnCoroutine);
                _burnCoroutine = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                AsegurarCargaDeSprites();
                AsegurarReferenciasRenderizado();
            }
        }

        [ContextMenu("Cargar Sprites de Manos")]
        public void ForzarCargaSpritesEditor()
        {
            _equippedHandSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(PathManoEquipada);
            _holdingHandSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(PathManoSosteniendo);
            _burnedHandSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(PathManoQuemada);
            Debug.Log($"[PlayerHandController] Sprites cargados desde Editor: Equipada={_equippedHandSprite != null}, Sosteniendo={_holdingHandSprite != null}, Quemada={_burnedHandSprite != null}");
        }
#endif

        // ════════════════════════════════════════════════════════════════════
        // MÁQUINA DE ESTADOS Y CONTROL PÚBLICO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cambia el estado visual de la mano ejecutando la transición correspondiente.
        /// </summary>
        /// <param name="newState">Nuevo estado a aplicar.</param>
        public void SetHandState(HandState newState)
        {
            if (_currentState == newState) return;

            // Si estábamos en quemadura y se fuerza otro estado manualmente, cancelar el temporizador
            if (_currentState == HandState.BurnedByExplosion && newState != HandState.BurnedByExplosion)
            {
                if (_burnCoroutine != null)
                {
                    StopCoroutine(_burnCoroutine);
                    _burnCoroutine = null;
                }
            }

            _previousState = _currentState;
            _currentState = newState;

            ActualizarVisualesEstado(newState);

            // Iniciar temporizador automático si el nuevo estado es quemadura por explosión
            if (newState == HandState.BurnedByExplosion)
            {
                if (_burnCoroutine != null) StopCoroutine(_burnCoroutine);
                _burnCoroutine = StartCoroutine(RutinaTemporizadorQuemadura());
            }

            OnHandStateChanged?.Invoke(_currentState);
        }

        /// <summary>
        /// Muestra un ítem flotante sobre la palma abierta (mano demencia_E.png),
        /// instanciando el prefab especificado en el FloatingItemAnchor y cambiando el estado a HoldingItem.
        /// </summary>
        /// <param name="itemPrefab">Prefab del objeto o recurso a instanciar en la mano.</param>
        public void DisplayFloatingItem(GameObject itemPrefab)
        {
            ClearFloatingItem();

            SetHandState(HandState.HoldingItem);

            if (itemPrefab == null || _floatingItemAnchor == null) return;

            _currentFloatingItemInstance = Instantiate(itemPrefab, _floatingItemAnchor);
            _currentFloatingItemInstance.transform.localPosition = Vector3.zero;
            _currentFloatingItemInstance.transform.localRotation = Quaternion.identity;
            _currentFloatingItemInstance.transform.localScale = Vector3.one;

            // Desactivar colisionadores y físicas para que no interfieran con el jugador en primera persona
            DesactivarFisicasItem(_currentFloatingItemInstance);

            OnFloatingItemChanged?.Invoke(_currentFloatingItemInstance);
        }

        /// <summary>
        /// Destruye y remueve cualquier ítem flotante actualmente instanciado en la palma,
        /// y opcionalmente retorna la mano al estado base visible (EquippedItem / exploración).
        /// </summary>
        /// <param name="returnToDefaultState">Si es true y la mano estaba en HoldingItem, retorna al estado base visible.</param>
        public void ClearFloatingItem(bool returnToDefaultState = true)
        {
            if (_currentFloatingItemInstance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_currentFloatingItemInstance);
                }
                else
                {
                    DestroyImmediate(_currentFloatingItemInstance);
                }

                _currentFloatingItemInstance = null;
                OnFloatingItemChanged?.Invoke(null);
            }

            if (_floatingItemAnchor != null)
            {
                _floatingItemAnchor.localPosition = _anchorBaseLocalPosition;
            }

            if (returnToDefaultState && _currentState == HandState.HoldingItem)
            {
                SetHandState(_defaultState != HandState.Hidden ? _defaultState : HandState.EquippedItem);
            }
        }

        /// <summary>
        /// Restablece la mano al estado base visible de exploración libre (EquippedItem / mano empuñada base).
        /// </summary>
        public void ResetToDefaultHand()
        {
            ClearFloatingItem(returnToDefaultState: false);
            SetHandState(_defaultState != HandState.Hidden ? _defaultState : HandState.EquippedItem);
        }

        /// <summary>
        /// Oculta o restaura la mano exclusivamente para eventos especiales (cinemáticas o menús modales).
        /// En el gameplay regular, la mano permanece renderizada de forma permanente.
        /// </summary>
        /// <param name="hide">True para ocultar en cinemática (Hidden); False para restaurar la vista base visible.</param>
        public void SetCinematicHidden(bool hide)
        {
            if (hide)
            {
                SetHandState(HandState.Hidden);
            }
            else if (_currentState == HandState.Hidden)
            {
                SetHandState(_defaultState != HandState.Hidden ? _defaultState : HandState.EquippedItem);
            }
        }

        /// <summary>
        /// Atajo público para aplicar el estado de mano quemada por explosión.
        /// </summary>
        public void TriggerExplosionBurn()
        {
            SetHandState(HandState.BurnedByExplosion);
        }

        // ════════════════════════════════════════════════════════════════════
        // LÓGICA INTERNA DE VISUALES Y ESTADOS
        // ════════════════════════════════════════════════════════════════════

        private void ActualizarVisualesEstado(HandState state)
        {
            switch (state)
            {
                case HandState.Hidden:
                    AplicarVisibilidadRenderer(false);
                    ActualizarAnclajeFlotante(false);
                    break;

                case HandState.HoldingItem:
                    AplicarSprite(_holdingHandSprite);
                    AplicarVisibilidadRenderer(true);
                    ActualizarAnclajeFlotante(true);
                    break;

                case HandState.EquippedItem:
                    AplicarSprite(_equippedHandSprite);
                    AplicarVisibilidadRenderer(true);
                    ActualizarAnclajeFlotante(false);
                    break;

                case HandState.BurnedByExplosion:
                    AplicarSprite(_burnedHandSprite);
                    AplicarVisibilidadRenderer(true);
                    ActualizarAnclajeFlotante(false);
                    break;
            }
        }

        private void AplicarSprite(Sprite sprite)
        {
            if (sprite == null) return;

            if (_handImage != null)
            {
                _handImage.sprite = sprite;
            }

            if (_handSpriteRenderer != null)
            {
                _handSpriteRenderer.sprite = sprite;
            }
        }

        private void AplicarVisibilidadRenderer(bool visible)
        {
            if (_handImage != null)
            {
                _handImage.enabled = visible;
            }

            if (_handSpriteRenderer != null)
            {
                _handSpriteRenderer.enabled = visible;
            }
        }

        private void ActualizarAnclajeFlotante(bool activo)
        {
            if (_floatingItemAnchor != null)
            {
                _floatingItemAnchor.gameObject.SetActive(activo);
            }
        }

        private IEnumerator RutinaTemporizadorQuemadura()
        {
            yield return new WaitForSeconds(_burnDuration);

            _burnCoroutine = null;

            // Retornar al estado previo si era un estado visible válido; de lo contrario, usar _stateAfterBurn (o _defaultState) garantizando que nunca sea Hidden
            HandState fallback = (_stateAfterBurn != HandState.Hidden && _stateAfterBurn != HandState.BurnedByExplosion)
                ? _stateAfterBurn
                : (_defaultState != HandState.Hidden ? _defaultState : HandState.EquippedItem);

            HandState targetState = (_previousState != HandState.BurnedByExplosion && _previousState != HandState.Hidden)
                ? _previousState
                : fallback;

            SetHandState(targetState);
        }

        private void SuscribirEventosInventario()
        {
            PlayerInventory inventory = GetComponentInParent<PlayerInventory>() ?? FindAnyObjectByType<PlayerInventory>();
            if (inventory != null)
            {
                inventory.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
                inventory.OnActiveWeaponChanged += HandleActiveWeaponChanged;
            }
        }

        private void DesuscribirEventosInventario()
        {
            PlayerInventory inventory = GetComponentInParent<PlayerInventory>() ?? FindAnyObjectByType<PlayerInventory>();
            if (inventory != null)
            {
                inventory.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
            }
        }

        private void HandleActiveWeaponChanged(WeaponSlotInstance weapon)
        {
            // Al cambiar de arma, si la mano no está en quemadura ni inspeccionando ítem, mostrar la mano empuñada
            if (_currentState != HandState.BurnedByExplosion && _currentState != HandState.HoldingItem && _currentState != HandState.Hidden)
            {
                SetHandState(HandState.EquippedItem);
            }
        }

        private void ActualizarAnimacionItemFlotante()
        {
            float bobOffset = Mathf.Sin(Time.time * _floatBobSpeed) * _floatBobAmplitude;
            _floatingItemAnchor.localPosition = _anchorBaseLocalPosition + new Vector3(0f, bobOffset, 0f);

            if (_floatRotateSpeed > 0f)
            {
                _currentFloatingItemInstance.transform.Rotate(Vector3.up, _floatRotateSpeed * Time.deltaTime, Space.Self);
            }
        }

        private void DesactivarFisicasItem(GameObject itemGo)
        {
            if (itemGo == null) return;

            Collider[] colliders = itemGo.GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                col.enabled = false;
            }

            Rigidbody[] rigidbodies = itemGo.GetComponentsInChildren<Rigidbody>(true);
            foreach (var rb in rigidbodies)
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PROCEDURAL BOBBING & DINÁMICA DE ILUMINACIÓN
        // ════════════════════════════════════════════════════════════════════

        private void InicializarReferenciasJugador()
        {
            if (_playerManager == null)
            {
                _playerManager = PlayerManager.Instance ?? GetComponentInParent<PlayerManager>() ?? FindAnyObjectByType<PlayerManager>();
            }

            if (_characterController == null)
            {
                _characterController = GetComponentInParent<CharacterController>();
                if (_characterController == null && _playerManager != null)
                {
                    _characterController = _playerManager.CharacterController;
                }
            }

            if (_playerCamera == null)
            {
                _playerCamera = Camera.main ?? GetComponentInParent<Camera>() ?? FindAnyObjectByType<Camera>();
            }

            if (_handRect == null && _handImage != null)
            {
                _handRect = _handImage.rectTransform;
            }
        }

        private void ActualizarBalanceoCaminar()
        {
            if (_handRect == null)
            {
                if (_handImage != null) _handRect = _handImage.rectTransform;
                if (_handRect == null) return;
            }

            bool isMoving = false;
            bool isSprinting = false;

            if (_playerManager != null && _playerManager.Controller != null)
            {
                isMoving = _playerManager.Controller.IsMoving;
                isSprinting = _playerManager.Controller.IsSprinting;
            }
            else if (_characterController != null)
            {
                Vector3 hVel = _characterController.velocity;
                hVel.y = 0f;
                isMoving = hVel.sqrMagnitude > 0.05f;
                isSprinting = isMoving && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            }
            else
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                isMoving = (h * h + v * v) > 0.01f;
                isSprinting = isMoving && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            }

            if (isMoving)
            {
                float freq = _walkBobFrequency * (isSprinting ? _sprintBobMultiplier : 1f);
                _bobTimer += Time.deltaTime * freq;

                // Eje X: Balanceo lateral alternado a media frecuencia
                float targetX = Mathf.Cos(_bobTimer * 0.5f) * _bobAmountX * (isSprinting ? 1.25f : 1f);
                // Eje Y: Elevación y descenso con cada zancada
                float targetY = Mathf.Sin(_bobTimer) * _bobAmountY * (isSprinting ? 1.35f : 1f);

                Vector2 targetOffset = new Vector2(targetX, targetY);
                _currentBobOffset = Vector2.Lerp(_currentBobOffset, targetOffset, Time.deltaTime * _bobSmoothing);
            }
            else
            {
                // Regreso suave al reposo (Lerp)
                _currentBobOffset = Vector2.Lerp(_currentBobOffset, Vector2.zero, Time.deltaTime * _bobSmoothing);
            }

            _handRect.anchoredPosition = _handOffset + _currentBobOffset;
        }

        private void ActualizarIluminacionDinamica()
        {
            if (_handImage == null && _handSpriteRenderer == null) return;

            // Refrescar periódicamente la búsqueda de la luz más cercana (Zero-GC)
            if (Time.time >= _nextLightSearchTime)
            {
                _nextLightSearchTime = Time.time + _lightSearchInterval;
                BuscarLuzMasCercana();
            }

            float targetBrightness = _minAmbientBrightness;
            Color targetTint = _baseHandColor;

            if (_nearestLight != null && _nearestLight.isActiveAndEnabled)
            {
                Vector3 playerPos = (_playerCamera != null) ? _playerCamera.transform.position : transform.position;
                float dist = Vector3.Distance(_nearestLight.transform.position, playerPos);

                if (dist <= _nearestLight.range)
                {
                    float normalizedDist = Mathf.Clamp01(dist / Mathf.Max(_nearestLight.range, 0.01f));
                    float atten = 1f - (normalizedDist * normalizedDist);

                    // Muestrea la intensidad de la luz (que fluctúa con LightFlicker.cs en tiempo real)
                    float lightIntensity = _nearestLight.intensity * atten;

                    targetBrightness = Mathf.Clamp(_minAmbientBrightness + lightIntensity * 0.65f, _minAmbientBrightness, _maxLightBrightness);
                    targetTint = Color.Lerp(_baseHandColor, _nearestLight.color, 0.45f);
                }
            }
            else
            {
                // Parpadeo sutil atmosférico en zonas de penumbra
                float atmosphericFlicker = Mathf.PerlinNoise(Time.time * 3.5f, 0.5f) * 0.12f;
                targetBrightness = Mathf.Clamp(_minAmbientBrightness + atmosphericFlicker, _minAmbientBrightness, _maxLightBrightness);
            }

            // Interpolación suave de respuesta lumínica
            _currentBrightness = Mathf.Lerp(_currentBrightness, targetBrightness, Time.deltaTime * _lightLerpSpeed);

            Color finalColor = targetTint * _currentBrightness;
            finalColor.a = 1f;

            if (_handImage != null)
            {
                _handImage.color = finalColor;
            }

            if (_handSpriteRenderer != null)
            {
                _handSpriteRenderer.color = finalColor;
            }
        }

        private void BuscarLuzMasCercana()
        {
            if (Time.time >= _nextFullLightsCacheTime || _cachedLights == null)
            {
                _nextFullLightsCacheTime = Time.time + 2.5f;
                _cachedLights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            }

            if (_cachedLights == null || _cachedLights.Length == 0)
            {
                _nearestLight = null;
                return;
            }

            Vector3 playerPos = (_playerCamera != null) ? _playerCamera.transform.position : transform.position;
            float closestDistSqr = _lightSamplingRadius * _lightSamplingRadius;
            Light closest = null;

            for (int i = 0; i < _cachedLights.Length; i++)
            {
                Light l = _cachedLights[i];
                if (l == null || !l.isActiveAndEnabled || l.type == LightType.Directional) continue;

                float distSqr = (l.transform.position - playerPos).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    closest = l;
                }
            }

            _nearestLight = closest;
        }

        // ════════════════════════════════════════════════════════════════════
        // INICIALIZACIÓN, CACHÉ Y CARGA ROBUSTA POR CÓDIGO
        // ════════════════════════════════════════════════════════════════════

        private void AsegurarReferenciasRenderizado()
        {
            if (_handImage == null && _handSpriteRenderer == null)
            {
                _handImage = GetComponent<Image>();
                if (_handImage == null)
                {
                    _handImage = GetComponentInChildren<Image>(true);
                }
            }

            if (_handImage == null && _handSpriteRenderer == null && _autoGenerateCanvasIfMissing)
            {
                AsegurarCanvasYRenderizadoPorCodigo();
            }

            if (_handSpriteRenderer == null && _handImage == null)
            {
                _handSpriteRenderer = GetComponent<SpriteRenderer>();
                if (_handSpriteRenderer == null)
                {
                    _handSpriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
                }
            }
        }

        /// <summary>
        /// Crea de forma 100% autónoma por código el Canvas, CanvasScaler y el objeto ManoPersonaje
        /// anclado a la esquina inferior derecha con resolución base de 1920x1080.
        /// </summary>
        public void AsegurarCanvasYRenderizadoPorCodigo()
        {
            if (_handImage != null) return;

            // 1. Buscar si ya existe un Canvas_Manos en la escena o bajo la cámara principal
            Camera mainCam = Camera.main ?? GetComponentInChildren<Camera>() ?? FindAnyObjectByType<Camera>();
            GameObject canvasGo = null;

            if (mainCam != null)
            {
                Transform childCanvas = mainCam.transform.Find("Canvas_Manos");
                if (childCanvas != null) canvasGo = childCanvas.gameObject;
            }

            if (canvasGo == null)
            {
                canvasGo = GameObject.Find("Canvas_Manos");
            }

            // 2. Si no existe, instanciar Canvas_Manos por código con componentes UI tipados
            if (canvasGo == null)
            {
                canvasGo = new GameObject("Canvas_Manos", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                if (mainCam != null)
                {
                    canvasGo.transform.SetParent(mainCam.transform, false);
                }

                Canvas canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;

                CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            // 3. Buscar o crear el objeto ManoPersonaje dentro del Canvas garantizando RectTransform
            Transform manoTransform = canvasGo.transform.Find("ManoPersonaje");
            GameObject manoGo;
            if (manoTransform != null)
            {
                manoGo = manoTransform.gameObject;
            }
            else
            {
                manoGo = new GameObject("ManoPersonaje", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                manoGo.transform.SetParent(canvasGo.transform, false);
            }

            // 4. Configurar RectTransform validando su existencia
            RectTransform rect = manoGo.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = manoGo.AddComponent<RectTransform>();
            }

            if (rect != null)
            {
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                rect.sizeDelta = _handSize;
                rect.anchoredPosition = _handOffset;
                _handRect = rect;
            }

            // 5. Configurar componente Image
            Image img = manoGo.GetComponent<Image>() ?? manoGo.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;

            _handImage = img;
            _handRect = rect;

            // 6. Asignar el sprite inicial si ya están disponibles
            if (_handImage.sprite == null && _equippedHandSprite != null)
            {
                _handImage.sprite = _equippedHandSprite;
            }
        }

        private void AsegurarPuntoDeAnclaje()
        {
            if (_floatingItemAnchor == null)
            {
                Transform parentToUse = (_handImage != null) ? _handImage.transform : transform;
                Transform existing = parentToUse.Find("FloatingItemAnchor");
                if (existing != null)
                {
                    _floatingItemAnchor = existing;
                }
                else
                {
                    GameObject anchorGo;
                    if (parentToUse is RectTransform)
                    {
                        anchorGo = new GameObject("FloatingItemAnchor", typeof(RectTransform));
                        anchorGo.transform.SetParent(parentToUse, false);

                        RectTransform anchorRect = anchorGo.GetComponent<RectTransform>();
                        if (anchorRect != null)
                        {
                            anchorRect.anchorMin = new Vector2(0.5f, 0.65f);
                            anchorRect.anchorMax = new Vector2(0.5f, 0.65f);
                            anchorRect.pivot = new Vector2(0.5f, 0.5f);
                            anchorRect.anchoredPosition = Vector2.zero;
                            anchorRect.sizeDelta = new Vector2(160f, 160f);
                        }
                    }
                    else
                    {
                        anchorGo = new GameObject("FloatingItemAnchor");
                        anchorGo.transform.SetParent(parentToUse, false);
                        anchorGo.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                    }

                    _floatingItemAnchor = anchorGo.transform;
                }
            }

            if (_floatingItemAnchor != null)
            {
                _anchorBaseLocalPosition = _floatingItemAnchor.localPosition;
            }
        }

        /// <summary>
        /// Garantiza que los tres sprites existan cargándolos por código si no fueron asignados en el Inspector.
        /// Prioridades: Inspector > AssetDatabase (Editor) > Resources.Load.
        /// </summary>
        public void AsegurarCargaDeSprites()
        {
            if (_equippedHandSprite == null)
            {
                _equippedHandSprite = CargarSprite(PathManoEquipada, ResourcesManoEquipada, "mano demencia.png");
            }

            if (_holdingHandSprite == null)
            {
                _holdingHandSprite = CargarSprite(PathManoSosteniendo, ResourcesManoSosteniendo, "mano demencia_E.png");
            }

            if (_burnedHandSprite == null)
            {
                _burnedHandSprite = CargarSprite(PathManoQuemada, ResourcesManoQuemada, "mano explosion demencia.png");
            }
        }

        private Sprite CargarSprite(string assetPath, string resourcesPath, string fileName)
        {
#if UNITY_EDITOR
            Sprite editorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (editorSprite != null) return editorSprite;
#endif

            // Intento vía Resources si se ubican bajo Resources/Manos/
            Sprite resSprite = Resources.Load<Sprite>(resourcesPath);
            if (resSprite != null) return resSprite;

            return null;
        }

        // ════════════════════════════════════════════════════════════════════
        // AUTO-INICIALIZACIÓN GLOBAL EN RUNTIME (CERO CONFIGURACIÓN MANUAL)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Inicializa o garantiza la existencia de PlayerHandController y su Canvas diegético en la escena.
        /// </summary>
        public static PlayerHandController ObtenerOInstanciarEnEscena()
        {
            PlayerHandController existing = FindAnyObjectByType<PlayerHandController>();
            if (existing != null)
            {
                existing.AsegurarCanvasYRenderizadoPorCodigo();
                return existing;
            }

            if (PlayerManager.Instance != null)
            {
                return PlayerManager.Instance.gameObject.AddComponent<PlayerHandController>();
            }

            Camera mainCam = Camera.main ?? FindAnyObjectByType<Camera>();
            if (mainCam != null)
            {
                return mainCam.gameObject.AddComponent<PlayerHandController>();
            }

            GameObject handHost = new GameObject("PlayerHandController_Auto");
            return handHost.AddComponent<PlayerHandController>();
        }
    }
}
