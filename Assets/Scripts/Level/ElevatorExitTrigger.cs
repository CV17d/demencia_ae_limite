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
            // OBSOLETO / DESHABILITADO: El cambio de nivel se realiza exclusivamente
            // interactuando con el panel de control interactivo (ElevatorPanelController) mediante la tecla [E].
            // Se desactiva este script para evitar cualquier transición automática accidental.
            enabled = false;

            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DETECCIÓN DE COLISIÓN (ON TRIGGER ENTER) — DESACTIVADA
        // ════════════════════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            // Transición automática deshabilitada:
            // Toda la lógica de cambio de nivel ha sido aislada en ElevatorPanelController
            // supeditada a la pulsación explícita de la tecla [E] frente a la consola.
            Debug.LogWarning("[ElevatorExitTrigger] OnTriggerEnter ignorado: el descenso a un nuevo piso ahora depende exclusivamente del panel interactivo (ElevatorPanelController) con la tecla [E].");
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
