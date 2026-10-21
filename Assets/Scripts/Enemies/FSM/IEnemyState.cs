// ============================================================================
// IEnemyState.cs — Interfaz del Patrón Máquina de Estados Finitos (FSM)
// ============================================================================
// Define el contrato de ciclo de vida para los estados de la IA enemiga.
// Optimizado para ejecución continua en Update() con 0 asignaciones de memoria (GC).
// ============================================================================

namespace DemenciaNoLimite.Enemies.FSM
{
    /// <summary>
    /// Contrato que define el comportamiento de cualquier estado en la FSM del enemigo.
    /// Garantiza transiciones limpias y desacopladas entre comportamientos (Idle, Chase, Attack, Dead).
    /// </summary>
    public interface IEnemyState
    {
        /// <summary>
        /// Se ejecuta una sola vez al ingresar al estado.
        /// Utilizado para inicializar temporizadores, detener el NavMeshAgent o preparar animaciones.
        /// </summary>
        void Enter();

        /// <summary>
        /// Se ejecuta en cada frame durante el ciclo Update() del EnemyBrain mientras el estado esté activo.
        /// No debe generar recolección de basura (evitar 'new', 'LINQ' o búsquedas de jerarquía).
        /// </summary>
        void Execute();

        /// <summary>
        /// Se ejecuta una sola vez al salir del estado para transicionar a otro.
        /// Utilizado para limpiar temporizadores, reactivar componentes o resetear banderas.
        /// </summary>
        void Exit();
    }
}
