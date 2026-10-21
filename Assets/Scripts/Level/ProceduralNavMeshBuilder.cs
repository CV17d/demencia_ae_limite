// ============================================================================
// ProceduralNavMeshBuilder.cs — Gestor de Horneado Asíncrono de NavMesh
// ============================================================================
// Se comunica con el componente NavMeshSurface (Unity.AI.Navigation) para generar
// la malla de navegación en tiempo real tras la instanciación de mapas modulares WFC.
//
// Evita el congelamiento (frame freezing / stuttering) del hilo principal
// mediante operaciones asíncronas distribuidas por corrutinas.
// Notifica a las IAs mediante GameEvents.OnNavMeshReady al finalizar.
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Level
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshSurface))]
    public class ProceduralNavMeshBuilder : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN SERIALIZADA
        // ════════════════════════════════════════════════════════════════════

        [Header("Componentes")]
        [Tooltip("Referencia al componente de navegación modular.")]
        [SerializeField] private NavMeshSurface _surface;

        [Header("Opciones de Horneado")]
        [Tooltip("Si está activo, hornea al iniciar la escena tras la espera de frames.")]
        [SerializeField] private bool _bakeOnStart = false;

        [Tooltip("Cantidad de frames de espera antes de iniciar el cálculo para asentar colliders.")]
        [SerializeField] private int _stabilizationFrameDelay = 1;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO
        // ════════════════════════════════════════════════════════════════════

        private Coroutine _bakingCoroutine;
        private bool _isBaking;

        public bool IsBaking => _isBaking;
        public NavMeshSurface Surface => _surface;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (_surface == null)
            {
                _surface = GetComponent<NavMeshSurface>();
            }
        }

        private void OnEnable()
        {
            // Suscripción al evento que emite el generador procedural al terminar la geometría
            GameEvents.OnMapGenerationCompleted += HandleMapGenerationCompleted;
        }

        private void OnDisable()
        {
            GameEvents.OnMapGenerationCompleted -= HandleMapGenerationCompleted;
            CancelBake();
        }

        private void Start()
        {
            if (_bakeOnStart)
            {
                RebuildNavMeshAsync();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MANEJO DE EVENTOS Y HORNEADO ASÍNCRONO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Callback disparado al finalizar la generación procedural del mapa.
        /// </summary>
        private void HandleMapGenerationCompleted()
        {
            RebuildNavMeshAsync();
        }

        /// <summary>
        /// Inicia el proceso de reconstrucción asíncrona del NavMesh.
        /// </summary>
        public void RebuildNavMeshAsync()
        {
            if (_isBaking)
            {
                Debug.LogWarning("[ProceduralNavMeshBuilder] Ya existe un horneado en progreso. Cancelando operación previa...");
                CancelBake();
            }

            _bakingCoroutine = StartCoroutine(BuildNavMeshRoutine());
        }

        /// <summary>
        /// Cancela cualquier proceso de horneado activo.
        /// </summary>
        public void CancelBake()
        {
            if (_bakingCoroutine != null)
            {
                StopCoroutine(_bakingCoroutine);
                _bakingCoroutine = null;
            }
            _isBaking = false;
        }

        /// <summary>
        /// Corrutina encargada de procesar el NavMesh de forma asíncrona
        /// para mantener fluidos los FPS y evitar congelamientos de pantalla.
        /// </summary>
        private IEnumerator BuildNavMeshRoutine()
        {
            if (_surface == null)
            {
                Debug.LogError("[ProceduralNavMeshBuilder] No se encontró NavMeshSurface en el GameObject.");
                yield break;
            }

            _isBaking = true;
            Debug.Log("[ProceduralNavMeshBuilder] Iniciando horneado asíncrono de NavMesh...");

            // 1. Espera de estabilización para permitir que Unity registre la geometría recién instanciada
            for (int i = 0; i < _stabilizationFrameDelay; i++)
            {
                yield return null;
            }

            // 2. Verificación y asignación de datos de malla de navegación
            if (_surface.navMeshData == null)
            {
                _surface.navMeshData = new NavMeshData();
            }

            // 3. Ejecución del horneado asíncrono
            AsyncOperation asyncOp = _surface.UpdateNavMeshData(_surface.navMeshData);

            if (asyncOp != null)
            {
                while (!asyncOp.isDone)
                {
                    yield return null;
                }
            }
            else
            {
                // Fallback seguro si la superficie requiere primer horneado síncrono controlado
                _surface.BuildNavMesh();
                yield return null;
            }

            _isBaking = false;
            _bakingCoroutine = null;

            Debug.Log("[ProceduralNavMeshBuilder] ¡NavMesh horneado exitosamente! Notificando a las IAs...");

            // 4. Notificación global al Event Bus para que los agentes EnemyBrain se activen
            GameEvents.FireNavMeshReady();
        }
    }
}
