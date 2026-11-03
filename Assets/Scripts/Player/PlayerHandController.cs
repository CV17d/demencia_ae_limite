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

        [Tooltip("Desplazamiento adicional (offset) relativo a la esquina inferior derecha.")]
        [SerializeField] private Vector2 _handOffset = Vector2.zero;

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

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            AsegurarReferenciasRenderizado();
            AsegurarCargaDeSprites();
            AsegurarPuntoDeAnclaje();
        }

        private void OnEnable()
        {
            SuscribirEventosInventario();
        }

        private void Start()
        {
            // Garantizar que la mano comience siempre visible en el estado base configurado
            if (_currentState == HandState.Hidden)
            {
                _currentState = (_defaultState != HandState.Hidden) ? _defaultState : HandState.EquippedItem;
            }

            ActualizarVisualesEstado(_currentState);
        }

        private void Update()
        {
            // Animación diegética de levitación suave sobre la palma
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

            // 2. Si no existe, instanciar Canvas_Manos por código
            if (canvasGo == null)
            {
                canvasGo = new GameObject("Canvas_Manos");
                if (mainCam != null)
                {
                    canvasGo.transform.SetParent(mainCam.transform, false);
                }

                Canvas canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;

                CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                canvasGo.AddComponent<GraphicRaycaster>();
            }

            // 3. Buscar o crear el objeto ManoPersonaje dentro del Canvas
            Transform manoTransform = canvasGo.transform.Find("ManoPersonaje");
            GameObject manoGo;
            if (manoTransform != null)
            {
                manoGo = manoTransform.gameObject;
            }
            else
            {
                manoGo = new GameObject("ManoPersonaje");
                manoGo.transform.SetParent(canvasGo.transform, false);
            }

            // 4. Configurar RectTransform anclado en la esquina inferior derecha
            RectTransform rect = manoGo.GetComponent<RectTransform>() ?? manoGo.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = _handSize;
            rect.anchoredPosition = _handOffset;

            // 5. Configurar componente Image
            Image img = manoGo.GetComponent<Image>() ?? manoGo.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;

            _handImage = img;

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
                    GameObject anchorGo = new GameObject("FloatingItemAnchor");
                    anchorGo.transform.SetParent(parentToUse, false);

                    if (parentToUse is RectTransform)
                    {
                        RectTransform anchorRect = anchorGo.AddComponent<RectTransform>();
                        anchorRect.anchorMin = new Vector2(0.5f, 0.65f);
                        anchorRect.anchorMax = new Vector2(0.5f, 0.65f);
                        anchorRect.pivot = new Vector2(0.5f, 0.5f);
                        anchorRect.anchoredPosition = Vector2.zero;
                        anchorRect.sizeDelta = new Vector2(160f, 160f);
                    }
                    else
                    {
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
