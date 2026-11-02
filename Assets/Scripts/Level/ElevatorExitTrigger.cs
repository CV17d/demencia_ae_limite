// ============================================================================
// ElevatorExitTrigger.cs — Sensor Físico de Salida y Descenso en Elevador
// ============================================================================
// Se adjunta a un GameObject con BoxCollider (Is Trigger activado) dentro
// del prefab del elevador de salida.
// Detecta la entrada del jugador mediante su tag ("Player"), bloquea ejecuciones
// repetidas mediante un candado booleano y solicita a LevelGenerator el descenso
// al siguiente piso.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Level
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class ElevatorExitTrigger : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN SERIALIZADA
        // ════════════════════════════════════════════════════════════════════

        [Header("Referencias")]
        [Tooltip("Referencia al generador de nivel. Si no se asigna, se buscará automáticamente en la escena.")]
        [SerializeField] private LevelGenerator _levelGenerator;

        [Header("Filtros de Detección")]
        [Tooltip("Tag que debe tener exactamente el objeto que entra al sensor para activar el descenso.")]
        [SerializeField] private string _playerTag = "Player";

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO (CANDADO LÓGICO)
        // ════════════════════════════════════════════════════════════════════

        [Header("Estado")]
        [Tooltip("Indica si el sensor ya fue activado para evitar disparos múltiples.")]
        [SerializeField] private bool hasTriggered = false;

        public bool HasTriggered => hasTriggered;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            // Asegura que el collider esté configurado como Trigger
            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
            }

            // Resolución inicial de LevelGenerator si no fue asignado por Inspector
            if (_levelGenerator == null)
            {
                _levelGenerator = FindAnyObjectByType<LevelGenerator>();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DETECCIÓN DE COLISIÓN (ON TRIGGER ENTER)
        // ════════════════════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            // Candado booleano: si ya fue activado, descartar inmediatamente
            if (hasTriggered) return;

            // Validar que el objeto que entra tenga exactamente el tag especificado ("Player")
            if (!other.CompareTag(_playerTag)) return;

            // Activar candado para evitar bucles o llamadas múltiples mientras el jugador permanece adentro
            hasTriggered = true;

            Debug.Log("Jugador entró al elevador. Iniciando descenso...");

            // Buscar LevelGenerator si aún no se tiene la referencia
            if (_levelGenerator == null)
            {
                _levelGenerator = FindAnyObjectByType<LevelGenerator>();
            }

            if (_levelGenerator != null)
            {
                _levelGenerator.GenerarSiguientePiso();
            }
            else
            {
                Debug.LogError("[ElevatorExitTrigger] Error: No se encontró ninguna instancia de LevelGenerator en la escena.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Permite reiniciar el candado manualmente si el elevador se reutiliza.
        /// </summary>
        public void ResetTrigger()
        {
            hasTriggered = false;
        }
    }
}
