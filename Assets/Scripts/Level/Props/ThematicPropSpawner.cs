// ============================================================================
// ThematicPropSpawner.cs — Gestor de Decoración y Micro-Props por Habitación
// ============================================================================
// Componente colocado en la raíz de cada habitación prefab (junto a ModuleConnector).
// - Selecciona aleatoriamente un único tema (PropThemeData) de su lista possibleThemes.
// - Localiza todos los PropSpawnPoint hijos y evalúa la probabilidad de cada uno.
// - Instancia props deterministas utilizando la semilla global del nivel o de la habitación.
// - Emparenta los props a su propio PropSpawnPoint para mantener una jerarquía limpia y óptima.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemenciaNoLimite.Level
{
    [DisallowMultipleComponent]
    public class ThematicPropSpawner : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURACIÓN DE TEMAS Y COMPORTAMIENTO
        // ════════════════════════════════════════════════════════════════════

        [Header("Perfiles Temáticos")]
        [Tooltip("Lista de posibles perfiles temáticos asignables a esta habitación (Oficina, Industrial, Médico, etc.).")]
        public List<PropThemeData> possibleThemes = new List<PropThemeData>();

        [Header("Opciones de Ejecución")]
        [Tooltip("Si está activo, ejecuta la decoración temática automáticamente en Start().")]
        [SerializeField] private bool _spawnOnStart = true;

        [Tooltip("Si es true, cada prop instanciado es hijo de su PropSpawnPoint correspondiente. " +
                 "Si es false, se agrupan bajo un objeto contenedor 'Props_Root' en la raíz.")]
        [SerializeField] private bool _parentToSpawnPoint = true;

        // ════════════════════════════════════════════════════════════════════
        // ESTADO DE EJECUCIÓN
        // ════════════════════════════════════════════════════════════════════

        [Header("Estado de Depuración")]
        [SerializeField] private PropThemeData _selectedTheme;
        [SerializeField] private bool _hasSpawned = false;
        [SerializeField] private int _spawnedPropsCount = 0;

        public PropThemeData SelectedTheme => _selectedTheme;
        public bool HasSpawned => _hasSpawned;
        public int SpawnedPropsCount => _spawnedPropsCount;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA MONOBEHAVIOUR
        // ════════════════════════════════════════════════════════════════════

        private void Start()
        {
            if (_spawnOnStart && !_hasSpawned)
            {
                // Intenta sincronizarse con la semilla del generador de nivel si existe en la escena
                string seed = null;
                LevelGenerator levelGen = FindAnyObjectByType<LevelGenerator>();
                if (levelGen != null && !string.IsNullOrEmpty(levelGen.ActiveSeed))
                {
                    seed = levelGen.ActiveSeed;
                }

                SpawnThematicProps(seed);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GENERACIÓN PROCEDURAL DE PROPS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ejecuta la selección de tema y la instanciación determinista de micro-props en todos los PropSpawnPoints hijos.
        /// </summary>
        /// <param name="levelSeed">Semilla alfanumérica global del nivel para garantizar determinismo.</param>
        [ContextMenu("Decorar Habitación Ahora")]
        public void SpawnThematicPropsManual()
        {
            SpawnThematicProps(null);
        }

        public void SpawnThematicProps(string levelSeed = null)
        {
            if (possibleThemes == null || possibleThemes.Count == 0)
            {
                Debug.LogWarning($"[ThematicPropSpawner] No hay perfiles temáticos asignados en '{gameObject.name}'. Se omite la decoración.");
                return;
            }

            // Limpieza previa si ya se había decorado en editor
            if (_hasSpawned)
            {
                ClearSpawnedProps();
            }

            // ── 1. DETERMINISMO MATEMÁTICO DE SEMILLA ─────────────────────────
            // Combinamos la semilla global con la posición espacial de la habitación
            // para que cada habitación tenga su propia secuencia pseudoaleatoria determinista.
            int seedHash;
            if (!string.IsNullOrEmpty(levelSeed))
            {
                int positionHash = transform.position.GetHashCode();
                seedHash = levelSeed.GetHashCode() ^ positionHash;
            }
            else
            {
                seedHash = transform.position.GetHashCode() ^ System.DateTime.UtcNow.Millisecond;
            }

            System.Random rng = new System.Random(seedHash);
            UnityEngine.Random.InitState(seedHash);

            // ── 2. SELECCIÓN DE UN ÚNICO TEMA PARA ESTA HABITACIÓN ────────────
            int themeIndex = rng.Next(0, possibleThemes.Count);
            _selectedTheme = possibleThemes[themeIndex];

            if (_selectedTheme == null)
            {
                Debug.LogWarning($"[ThematicPropSpawner] El tema seleccionado en el índice {themeIndex} es nulo en '{gameObject.name}'.");
                return;
            }

            // ── 3. BÚSQUEDA Y PROCESAMIENTO DE PUNTOS DE ANCLAJE ──────────────
            PropSpawnPoint[] spawnPoints = GetComponentsInChildren<PropSpawnPoint>(true);
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                _hasSpawned = true;
                return;
            }

            // Contenedor alternativo si no se emparenta a los spawn points
            Transform customRoot = null;
            if (!_parentToSpawnPoint)
            {
                Transform existing = transform.Find("Props_Root");
                if (existing != null)
                {
                    customRoot = existing;
                }
                else
                {
                    GameObject rootGo = new GameObject("Props_Root");
                    rootGo.transform.SetParent(transform, false);
                    customRoot = rootGo.transform;
                }
            }

            _spawnedPropsCount = 0;

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                PropSpawnPoint point = spawnPoints[i];
                if (point == null) continue;

                // Evaluación probabilística del nodo (ej. 75% aparece, 25% vacío)
                double roll = rng.NextDouble();
                if (roll > point.spawnChance)
                {
                    continue; // Nodo vacío por probabilidad
                }

                // Obtención del prefab aleatorio de la lista correspondiente al tipo de anclaje
                GameObject propPrefab = _selectedTheme.GetRandomProp(point.spawnType, rng);
                if (propPrefab == null)
                {
                    continue; // El tema no posee prefabs para este tipo de anclaje
                }

                // ── 4. INSTANCIACIÓN Y EMPARENTADO LIMPIO ─────────────────────
                Transform parentTransform = _parentToSpawnPoint ? point.transform : customRoot;
                GameObject propInstance = Instantiate(propPrefab, point.transform.position, point.transform.rotation, parentTransform);
                propInstance.name = $"Prop_{point.spawnType}_{propPrefab.name}";

                // Aplicar variación de rotación opcional
                if (point.randomRotation)
                {
                    float randomAngle = (float)(rng.NextDouble() * 360.0);
                    switch (point.spawnType)
                    {
                        case PropSpawnType.Floor:
                        case PropSpawnType.Desk:
                        case PropSpawnType.Ceiling:
                            // Rotación sobre eje vertical local (Y)
                            propInstance.transform.Rotate(Vector3.up, randomAngle, Space.Self);
                            break;
                        case PropSpawnType.Wall:
                            // En paredes se mantiene la orientación hacia afuera, permitiendo ligera variación en Z
                            float tilt = (float)((rng.NextDouble() - 0.5) * 12.0); // +/- 6 grados
                            propInstance.transform.Rotate(Vector3.forward, tilt, Space.Self);
                            break;
                    }
                }

                // Aplicar variación de escala opcional
                if (point.randomScaleVariation && point.maxScale > point.minScale)
                {
                    float scaleFactor = Mathf.Lerp(point.minScale, point.maxScale, (float)rng.NextDouble());
                    propInstance.transform.localScale = Vector3.Scale(propInstance.transform.localScale, Vector3.one * scaleFactor);
                }

                point.SetSpawnedProp(propInstance);
                _spawnedPropsCount++;
            }

            _hasSpawned = true;
            Debug.Log($"[ThematicPropSpawner] Habitación '{gameObject.name}' ambientada con el tema '{_selectedTheme.themeName}' ({_spawnedPropsCount} props generados de {spawnPoints.Length} nodos).");
        }

        /// <summary>
        /// Elimina todos los props instanciados para permitir regeneración en edición.
        /// </summary>
        [ContextMenu("Limpiar Props")]
        public void ClearSpawnedProps()
        {
            PropSpawnPoint[] spawnPoints = GetComponentsInChildren<PropSpawnPoint>(true);
            if (spawnPoints != null)
            {
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    if (spawnPoints[i] != null)
                    {
                        spawnPoints[i].ClearSpawnedProp();
                    }
                }
            }

            Transform customRoot = transform.Find("Props_Root");
            if (customRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(customRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(customRoot.gameObject);
                }
            }

            _hasSpawned = false;
            _spawnedPropsCount = 0;
            _selectedTheme = null;
        }
    }
}
