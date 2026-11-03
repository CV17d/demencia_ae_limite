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
        // CONFIGURACIÓN DE EFECTO QUEMADURA (BURNED BY EXPLOSION)
        // ════════════════════════════════════════════════════════════════════

        [Header("Temporizador de Quemadura por Explosión")]
        [Tooltip("Tiempo en segundos que permanece visible la mano quemada antes de restablecerse.")]
        [SerializeField] private float _burnDuration = 3.0f;

        [Tooltip("Estado al que regresará la mano tras finalizar la duración de la quemadura.")]
        [SerializeField] private HandState _stateAfterBurn = HandState.Hidden;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        [Header("Estado Actual (Solo Lectura)")]
        [SerializeField] private HandState _currentState = HandState.Hidden;
        private HandState _previousState = HandState.Hidden;

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
        public Transform FloatingItemAnchor => _floatingItemAnchor;
        public GameObject CurrentFloatingItem => _currentFloatingItemInstance;
        public bool IsHoldingItem => _currentState == HandState.HoldingItem;
        public bool IsEquipped => _currentState == HandState.EquippedItem;
        public bool IsBurned => _currentState == HandState.BurnedByExplosion;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            AsegurarReferenciasRenderizado();
            AsegurarCargaDeSprites();
            AsegurarPuntoDeAnclaje();
        }

        private void Start()
        {
            // Aplicar el estado inicial configurado
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
        /// Destruye y remueve cualquier ítem flotante actualmente instanciado en la palma.
        /// </summary>
        public void ClearFloatingItem()
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

            // Retornar al estado previo si era válido o al configurado por defecto
            HandState targetState = (_previousState != HandState.BurnedByExplosion) ? _previousState : _stateAfterBurn;
            SetHandState(targetState);
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
            if (_handImage == null)
            {
                _handImage = GetComponent<Image>();
                if (_handImage == null)
                {
                    _handImage = GetComponentInChildren<Image>(true);
                }
            }

            if (_handSpriteRenderer == null)
            {
                _handSpriteRenderer = GetComponent<SpriteRenderer>();
                if (_handSpriteRenderer == null)
                {
                    _handSpriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
                }
            }
        }

        private void AsegurarPuntoDeAnclaje()
        {
            if (_floatingItemAnchor == null)
            {
                Transform existing = transform.Find("FloatingItemAnchor");
                if (existing != null)
                {
                    _floatingItemAnchor = existing;
                }
                else
                {
                    GameObject anchorGo = new GameObject("FloatingItemAnchor");
                    anchorGo.transform.SetParent(transform, false);
                    anchorGo.transform.localPosition = new Vector3(0f, 0.12f, 0f);
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
        /// Prioridades: Inspector > AssetDatabase (Editor) > Resources.Load > Carga directa de textura en disco.
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

            // Intento vía Resources
            Sprite resSprite = Resources.Load<Sprite>(resourcesPath);
            if (resSprite != null) return resSprite;

            // Fallback de contingencia: Lectura directa de bytes de textura en runtime
            try
            {
                string fullPath = System.IO.Path.Combine(Application.dataPath, "Art/2D/Manos", fileName);
                if (System.IO.File.Exists(fullPath))
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(tex, bytes))
                    {
                        tex.name = fileName;
                        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerHandController] No fue posible cargar sprite desde archivo '{fileName}': {ex.Message}");
            }

            return null;
        }
    }
}
