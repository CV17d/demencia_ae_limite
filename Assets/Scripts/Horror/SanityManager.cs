// ============================================================================
// SanityManager.cs — Gestor Central de Locura y Director de la Ruta Genocida
// ============================================================================
// Monitorea las bajas del jugador incrementando la variable de "Culpa" (0..100).
// Orquesta castigos psicológicos progresivos:
// - Manipulación Zero-GC del peso del URP Volume (tonos carmesí y distorsión).
// - Generación procedural de sombras y alucinaciones (PhantomEnemy) fuera de vista.
// - Activación de Quick Time Events falsos (FakeQTESystem) por estrés mental.
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Enemies;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Horror
{
    [DisallowMultipleComponent]
    public class SanityManager : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONSTANTES Y SINGLETON
        // ════════════════════════════════════════════════════════════════════

        public const int MAX_GUILT = 100;
        public const int GUILT_HALLUCINATION_THRESHOLD = 50;
        public const int GUILT_PARANOIA_QTE_THRESHOLD = 80;

        public static SanityManager Instance { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN SERIALIZADA
        // ════════════════════════════════════════════════════════════════════

        [Header("Variable de Culpa (Ruta Genocida)")]
        [Tooltip("Nivel acumulado de culpa por asesinatos cometidos (0 a 100).")]
        [Range(0, MAX_GUILT)]
        [SerializeField] private int _currentGuilt = 0;

        [Tooltip("Cantidad de culpa sumada por cada enemigo eliminado.")]
        [SerializeField] private int _guiltPerKill = 4;

        [Header("Manipulación URP Volume (Zero GC)")]
        [Tooltip("Componente Volume de URP configurado con perfiles de aberración, grano y tono carmesí.")]
        [SerializeField] private Volume _insanityVolume;

        [Tooltip("Velocidad de transición suave (lerp) para el peso del volumen.")]
        [SerializeField] private float _volumeLerpSpeed = 2.5f;

        [Header("Invocación de Alucinaciones (Phantom Enemy)")]
        [Tooltip("Prefab del enemigo alucinatorio (PhantomEnemy) que se disuelve al contacto.")]
        [SerializeField] private GameObject _phantomEnemyPrefab;

        [Tooltip("Intervalo en segundos entre cada chequeo para invocar alucinaciones.")]
        [SerializeField] private float _hallucinationCheckInterval = 9.0f;

        [Tooltip("Probabilidad (0% a 100%) de invocar una sombra si la culpa supera el 50%.")]
        [Range(0f, 100f)]
        [SerializeField] private float _hallucinationSpawnChance = 45f;

        [Tooltip("Distancia mínima al jugador para invocar alucinaciones a sus espaldas.")]
        [SerializeField] private float _minSpawnDistance = 6.0f;

        [Tooltip("Distancia máxima al jugador para invocar alucinaciones.")]
        [SerializeField] private float _maxSpawnDistance = 11.0f;

        [Header("Módulo de Paranoia (Fake QTE)")]
        [SerializeField] private FakeQTESystem _fakeQTESystem;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO INTERNO Y TEMPORIZADORES
        // ════════════════════════════════════════════════════════════════════

        private float _hallucinationTimer;
        private float _qteParanoiaTimer;

        // ════════════════════════════════════════════════════════════════════
        // EVENTOS PÚBLICOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Se dispara al modificarse el nivel de Culpa. Provee la culpa actual y el porcentaje normalizado (0.0 a 1.0).
        /// </summary>
        public event Action<int, float> OnGuiltChanged;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES PÚBLICAS
        // ════════════════════════════════════════════════════════════════════

        public int CurrentGuilt => _currentGuilt;
        public float InsanityPercentage => Mathf.Clamp01((float)_currentGuilt / MAX_GUILT);
        public Volume InsanityVolume => _insanityVolume;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (_fakeQTESystem == null)
            {
                _fakeQTESystem = GetComponentInChildren<FakeQTESystem>();
            }
        }

        private void OnEnable()
        {
            // Suscripción al Event Bus global para rastrear cada baja cometida en combate
            GameEvents.OnEnemyKilled += HandleEnemyKilled;
        }

        private void OnDisable()
        {
            GameEvents.OnEnemyKilled -= HandleEnemyKilled;
        }

        private void Start()
        {
            // Inicializar el peso del volumen en 0 sin modificar el sharedProfile (Zero GC)
            if (_insanityVolume != null)
            {
                _insanityVolume.weight = InsanityPercentage;
            }
        }

        private void Update()
        {
            // 1. MANIPULACIÓN ZERO-GC DEL VOLUMEN URP
            // Modificamos directamente el 'weight' del componente Volume.
            // No se clonan VolumeProfiles ni se generan Memory Leaks en el ciclo de dibujado.
            if (_insanityVolume != null)
            {
                _insanityVolume.weight = Mathf.MoveTowards(
                    _insanityVolume.weight,
                    InsanityPercentage,
                    Time.deltaTime * _volumeLerpSpeed
                );
            }

            // 2. TEMPORIZADOR PASIVO DE ALUCINACIONES (Culpa > 50%)
            if (_currentGuilt >= GUILT_HALLUCINATION_THRESHOLD)
            {
                _hallucinationTimer += Time.deltaTime;
                if (_hallucinationTimer >= _hallucinationCheckInterval)
                {
                    _hallucinationTimer = 0f;
                    EvaluarInvocacionAlucinacion();
                }

                // 3. PARANOIA EXTREMA: PROBABILIDAD DE QTE FALSO (Culpa > 80%)
                if (_currentGuilt >= GUILT_PARANOIA_QTE_THRESHOLD)
                {
                    _qteParanoiaTimer += Time.deltaTime;
                    if (_qteParanoiaTimer >= 22.0f) // Cada 22s de alta tensión
                    {
                        _qteParanoiaTimer = 0f;
                        if (UnityEngine.Random.value < 0.35f)
                        {
                            TriggerFakeQTE();
                        }
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE CULPA Y EVENTOS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Callback ejecutado cuando un enemigo muere en cualquier punto del mapa.
        /// </summary>
        /// <param name="deathPosition">Ubicación del asesinato.</param>
        private void HandleEnemyKilled(Vector3 deathPosition)
        {
            AddGuilt(_guiltPerKill);
        }

        /// <summary>
        /// Incrementa la culpa del jugador y recalcula el estado mental del director.
        /// </summary>
        /// <param name="amount">Cantidad de culpa a sumar.</param>
        public void AddGuilt(int amount)
        {
            _currentGuilt = Mathf.Clamp(_currentGuilt + amount, 0, MAX_GUILT);
            float percentage = InsanityPercentage;

            OnGuiltChanged?.Invoke(_currentGuilt, percentage);
            Debug.Log($"[SanityManager] Culpa incrementada a {_currentGuilt}/100 ({percentage * 100:F0}% de Locura).");
        }

        /// <summary>
        /// Reduce la culpa acumulada (útil para items de expiación o perdón).
        /// </summary>
        public void ReduceGuilt(int amount)
        {
            _currentGuilt = Mathf.Clamp(_currentGuilt - amount, 0, MAX_GUILT);
            float percentage = InsanityPercentage;

            OnGuiltChanged?.Invoke(_currentGuilt, percentage);
        }

        // ════════════════════════════════════════════════════════════════════
        // INVOCACIÓN PROCEDURAL DE ALUCINACIONES (PHANTOM ENEMY)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Evalúa matemáticamente la probabilidad de manifestar una aparición espectral.
        /// </summary>
        private void EvaluarInvocacionAlucinacion()
        {
            // A mayor culpa, mayor probabilidad escalada
            float guiltFactor = (_currentGuilt - GUILT_HALLUCINATION_THRESHOLD) / (float)(MAX_GUILT - GUILT_HALLUCINATION_THRESHOLD);
            float adjustedChance = Mathf.Lerp(_hallucinationSpawnChance, 85f, guiltFactor);

            if (UnityEngine.Random.Range(0f, 100f) <= adjustedChance)
            {
                SpawnPhantomEnemyBehindPlayer();
            }
        }

        /// <summary>
        /// Instancia un PhantomEnemy en el plano inverso de visión del jugador sobre el NavMesh.
        /// </summary>
        private void SpawnPhantomEnemyBehindPlayer()
        {
            Transform playerTransform = PlayerController.Instance != null ? PlayerController.Instance.transform : null;
            if (playerTransform == null) return;

            // Determinar el vector inverso de visión (a espaldas de la cámara o jugador)
            Vector3 behindDirection = -playerTransform.forward;
            if (Camera.main != null)
            {
                behindDirection = -Camera.main.transform.forward;
            }
            behindDirection.y = 0f;

            // Variación angular aleatoria de +/- 45 grados respecto a la espalda directa
            float randomAngle = UnityEngine.Random.Range(-45f, 45f);
            Vector3 spawnVector = Quaternion.Euler(0f, randomAngle, 0f) * behindDirection.normalized;

            float spawnDistance = UnityEngine.Random.Range(_minSpawnDistance, _maxSpawnDistance);
            Vector3 candidatePosition = playerTransform.position + spawnVector * spawnDistance;

            // Proyección estricta sobre el NavMesh para que el agente espectral pueda desplazarse
            if (NavMesh.SamplePosition(candidatePosition, out NavMeshHit navHit, 4.0f, NavMesh.AllAreas))
            {
                GameObject phantomObj;
                if (_phantomEnemyPrefab != null)
                {
                    phantomObj = Instantiate(_phantomEnemyPrefab, navHit.position, Quaternion.identity);
                }
                else
                {
                    // Fallback procedural seguro si no se asignó un prefab en el inspector
                    phantomObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    phantomObj.name = "Phantom_Hallucination_Clone";
                    phantomObj.transform.position = navHit.position;

                    var agent = phantomObj.AddComponent<NavMeshAgent>();
                    agent.speed = 6.5f;

                    var phantomComp = phantomObj.AddComponent<PhantomEnemy>();

                    var renderer = phantomObj.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material.color = new Color(0.1f, 0.1f, 0.1f, 0.7f); // Sombra translúcida
                    }
                }

                Debug.Log($"[SanityManager] ¡Alucinación espectral manifestada a espaldas del jugador en {navHit.position}!");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CONTROL DE EVENTOS PARANOICOS (FAKE QTE)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Dispara manualmente o por umbral de locura un Quick Time Event simulado.
        /// </summary>
        public void TriggerFakeQTE()
        {
            if (_fakeQTESystem != null)
            {
                _fakeQTESystem.TriggerFakeQTE();
            }
        }
    }
}
