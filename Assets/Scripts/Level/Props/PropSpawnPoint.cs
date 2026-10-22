// ============================================================================
// PropSpawnPoint.cs — Nodo de Anclaje de Micro-Props Procedurales
// ============================================================================
// Componente liviano colocado en GameObjects vacíos dentro de habitaciones modulares.
// - Define el tipo de superficie o anclaje (Floor, Wall, Ceiling, Desk).
// - Define la probabilidad de aparición (ej. 75% aparece prop, 25% queda vacío).
// - Provee Gizmos de depuración en Scene View con código de colores según el tipo.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Level
{
    [SelectionBase]
    [DisallowMultipleComponent]
    public class PropSpawnPoint : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE NODO
        // ════════════════════════════════════════════════════════════════════

        [Header("Tipo y Probabilidad")]
        [Tooltip("Tipo de anclaje o superficie que define qué lista de props temáticos puede recibir.")]
        public PropSpawnType spawnType = PropSpawnType.Floor;

        [Range(0f, 1f)]
        [Tooltip("Probabilidad de que se instancie un prop en este punto (ej. 0.75 = 75% de probabilidad, 25% vacío).")]
        public float spawnChance = 0.75f;

        [Header("Variación Procedural")]
        [Tooltip("Aplica una rotación aleatoria sobre el eje de rotación natural del prop para romper la monotonía.")]
        public bool randomRotation = true;

        [Tooltip("Si es true, permite una variación sutil de escala.")]
        public bool randomScaleVariation = false;

        [Range(0.85f, 1f)]
        public float minScale = 0.9f;

        [Range(1f, 1.25f)]
        public float maxScale = 1.1f;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO DE EJECUCIÓN
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Referencia a la instancia de prop generada en este punto (si la hay).
        /// </summary>
        public GameObject SpawnedProp { get; private set; }

        public bool HasSpawned => SpawnedProp != null;

        /// <summary>
        /// Asigna y registra la instancia del prop generado.
        /// </summary>
        public void SetSpawnedProp(GameObject prop)
        {
            SpawnedProp = prop;
        }

        /// <summary>
        /// Destruye el prop instanciado para permitir regeneraciones en tiempo de diseño.
        /// </summary>
        public void ClearSpawnedProp()
        {
            if (SpawnedProp != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(SpawnedProp);
                }
                else
                {
                    DestroyImmediate(SpawnedProp);
                }
                SpawnedProp = null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GIZMOS DE DEPURACIÓN EN ESCENA
        // ════════════════════════════════════════════════════════════════════

        private void OnDrawGizmos()
        {
            // Código de colores según tipo de anclaje
            Color gizmoColor;
            switch (spawnType)
            {
                case PropSpawnType.Floor:
                    gizmoColor = new Color(0.2f, 1.0f, 0.3f, 0.8f); // Verde (Suelo)
                    break;
                case PropSpawnType.Wall:
                    gizmoColor = new Color(0.2f, 0.7f, 1.0f, 0.8f); // Azul cielo (Pared)
                    break;
                case PropSpawnType.Ceiling:
                    gizmoColor = new Color(1.0f, 0.85f, 0.1f, 0.8f); // Amarillo (Techo)
                    break;
                case PropSpawnType.Desk:
                    gizmoColor = new Color(1.0f, 0.45f, 0.1f, 0.8f); // Naranja (Mesa / Escritorio)
                    break;
                default:
                    gizmoColor = Color.white;
                    break;
            }

            Gizmos.color = gizmoColor;

            // Dibuja el punto central
            Gizmos.DrawWireSphere(transform.position, 0.12f);

            // Dibuja la orientación de la superficie
            switch (spawnType)
            {
                case PropSpawnType.Floor:
                case PropSpawnType.Desk:
                    // Disco o cruz horizontal en la base
                    Gizmos.DrawLine(transform.position - transform.right * 0.15f, transform.position + transform.right * 0.15f);
                    Gizmos.DrawLine(transform.position - transform.forward * 0.15f, transform.position + transform.forward * 0.15f);
                    Gizmos.DrawRay(transform.position, transform.up * 0.3f);
                    break;

                case PropSpawnType.Wall:
                    // Flecha que apunta hacia afuera de la pared (forward)
                    Gizmos.DrawRay(transform.position, transform.forward * 0.4f);
                    Gizmos.DrawLine(transform.position - transform.up * 0.15f, transform.position + transform.up * 0.15f);
                    break;

                case PropSpawnType.Ceiling:
                    // Flecha que cuelga hacia abajo (-up)
                    Gizmos.DrawRay(transform.position, -transform.up * 0.35f);
                    Gizmos.DrawLine(transform.position - transform.right * 0.15f, transform.position + transform.right * 0.15f);
                    break;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, 0.2f);
        }
    }
}
