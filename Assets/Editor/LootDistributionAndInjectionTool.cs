#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DemenciaNoLimite.Editor
{
    /// <summary>
    /// Herramienta integral de Technical Artist y Tools Programmer para:
    /// 1. Crear el directorio de Loot.
    /// 2. Clasificar y mover prefabs recolectables (llaves, frascos, maletines, munición, medicinas).
    /// 3. Garantizar BoxCollider con isTrigger = true en todos los ítems de botín.
    /// 4. Inyectar automáticamente el botín estratégico en los escenarios (sobre HeroProps en cuartos y esquinas en pasillos).
    /// 5. Registrar métricas detalladas en la consola de Unity.
    /// </summary>
    public static class LootDistributionAndInjectionTool
    {
        public const string PROPS_FOLDER = "Assets/Prefabs/Entornos/Props";
        public const string LOOT_FOLDER = "Assets/Prefabs/Loot";
        public const string SCENARIOS_FOLDER = "Assets/Prefabs/Entornos/SectorCero";

        private static readonly string[] LOOT_SEARCH_PATTERNS = new string[]
        {
            "keycard", "frascos", "maletine", "maletin", "bala", "balas", "botiquin", "medkit", "pastillas"
        };

        // ════════════════════════════════════════════════════════════════════
        // ENTRADA DE MENÚ EN UNITY EDITOR Y AUTO-EJECUCIÓN
        // ════════════════════════════════════════════════════════════════════

        [InitializeOnLoadMethod]
        private static void AutoEjecutarEnCompilacion()
        {
            EditorApplication.delayCall += () =>
            {
                EjecutarPipelineCompleto(false);
            };
        }

        [MenuItem("Demencia/⚡ Organizar Loot e Inyectar en Escenarios", false, 0)]
        [MenuItem("Tools/Demencia/Organizar Loot e Inyectar en Escenarios", false, 0)]
        public static void EjecutarMenuManual()
        {
            EjecutarPipelineCompleto(true);
        }

        [MenuItem("Demencia/🚀 Pipeline Maestro (1. Hero Props + 2. Botín)", false, 1)]
        [MenuItem("Tools/Demencia/Pipeline Maestro (Hero Props + Botín)", false, 1)]
        public static void EjecutarPipelineMaestro()
        {
            Debug.Log("<color=#38BDF8><b>[Pipeline Maestro]</b> Paso 1/2: Inyectando Hero Props y Spawners procedurales...</color>");
            SmartSpawnerInjector.EjecutarInyeccionCompleta(false);

            Debug.Log("<color=#38BDF8><b>[Pipeline Maestro]</b> Paso 2/2: Clasificando y distribuyendo botín estratégico...</color>");
            EjecutarPipelineCompleto(true);
        }

        /// <summary>
        /// Ejecuta en un solo paso la creación de carpeta, migración de prefabs,
        /// ajuste de triggers físicos y la inyección procedural de botín en SectorCero.
        /// </summary>
        public static void EjecutarPipelineCompleto(bool mostrarDialogo = true)
        {
            // 1. CREAR DIRECTORIO DE LOOT
            AsegurarDirectorio(LOOT_FOLDER);

            // 2. CLASIFICAR Y MOVER PREFABS DE BOTÍN
            int prefabsMovidos = MoverPrefabsDeBotin();

            // 3. AJUSTE DE COLISIONES (CRÍTICO: BoxCollider + isTrigger = true)
            int collidersAjustados = ConfigurarColisionesLoot();

            // 4. INYECCIÓN AUTOMÁTICA DE BOTÍN EN ESCENARIOS
            (int cuartosInyectados, int totalLootSpawned) = InyectarBotinEnEscenarios();

            // 5. REGISTRO
            string resumenLog = $"<b>[LootDistributionTool] ✔ Pipeline Completo Finalizado:</b>\n" +
                                $"• Prefabs movidos/reorganizados en Loot: {prefabsMovidos}\n" +
                                $"• Prefabs en Loot verificados con isTrigger=true: {collidersAjustados}\n" +
                                $"• Escenarios con botín instanciado: {cuartosInyectados}\n" +
                                $"• Total ítems de Loot inyectados: {totalLootSpawned}";

            Debug.Log($"<color=#4ADE80>{resumenLog}</color>");

            if (mostrarDialogo)
            {
                EditorUtility.DisplayDialog("Organización e Inyección de Botín",
                    $"Proceso completado con éxito:\n\n" +
                    $"• Prefabs movidos a Loot: {prefabsMovidos}\n" +
                    $"• Escenarios con botín: {cuartosInyectados}\n" +
                    $"• Total ítems inyectados: {totalLootSpawned}\n" +
                    $"• Físicas seguras: BoxCollider con isTrigger = TRUE garantizado.\n\n" +
                    "Revisa la consola para más detalles.",
                    "Excelente");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 1 & 2. CREACIÓN DE DIRECTORIO Y MOVIMIENTO DE PREFABS
        // ════════════════════════════════════════════════════════════════════

        private static void AsegurarDirectorio(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }

        private static int MoverPrefabsDeBotin()
        {
            int movidos = 0;
            if (!Directory.Exists(PROPS_FOLDER)) return 0;

            string[] propGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PROPS_FOLDER });
            foreach (string guid in propGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                string lower = fileName.ToLowerInvariant();

                if (LOOT_SEARCH_PATTERNS.Any(pattern => lower.Contains(pattern)))
                {
                    string targetPath = $"{LOOT_FOLDER}/{Path.GetFileName(assetPath)}";
                    string error = AssetDatabase.MoveAsset(assetPath, targetPath);
                    if (string.IsNullOrEmpty(error))
                    {
                        movidos++;
                    }
                    else
                    {
                        string fullSource = Path.GetFullPath(assetPath);
                        string fullTarget = Path.GetFullPath(targetPath);
                        if (File.Exists(fullSource) && fullSource != fullTarget)
                        {
                            File.Copy(fullSource, fullTarget, true);
                            File.Delete(fullSource);

                            string metaSource = fullSource + ".meta";
                            string metaTarget = fullTarget + ".meta";
                            if (File.Exists(metaSource))
                            {
                                File.Copy(metaSource, metaTarget, true);
                                File.Delete(metaSource);
                            }
                            movidos++;
                        }
                    }
                }
            }

            if (movidos > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return movidos;
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. AJUSTE DE COLISIONES (BoxCollider + isTrigger = true)
        // ════════════════════════════════════════════════════════════════════

        private static int ConfigurarColisionesLoot()
        {
            int ajustados = 0;
            string[] lootGuids = AssetDatabase.FindAssets("t:Prefab", new[] { LOOT_FOLDER });

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in lootGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    if (root == null) continue;

                    bool modificado = false;

                    // BoxCollider en el root
                    BoxCollider boxCol = root.GetComponent<BoxCollider>();
                    if (boxCol == null)
                    {
                        boxCol = root.AddComponent<BoxCollider>();
                        modificado = true;
                    }

                    if (!boxCol.isTrigger)
                    {
                        boxCol.isTrigger = true;
                        modificado = true;
                    }

                    // Asegurar que todos los demás colliders hijos también sean triggers
                    Collider[] allColliders = root.GetComponentsInChildren<Collider>(true);
                    foreach (var c in allColliders)
                    {
                        if (!c.isTrigger)
                        {
                            c.isTrigger = true;
                            modificado = true;
                        }
                    }

                    if (modificado)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }

                    PrefabUtility.UnloadPrefabContents(root);
                    ajustados++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return ajustados;
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. INYECCIÓN AUTOMÁTICA DE BOTÍN EN ESCENARIOS
        // ════════════════════════════════════════════════════════════════════

        private static (int cuartosInyectados, int totalLootSpawned) InyectarBotinEnEscenarios()
        {
            List<GameObject> lootPrefabs = CargarPrefabsDeLoot();
            if (lootPrefabs.Count == 0)
            {
                Debug.LogWarning("[LootDistributionTool] No hay prefabs de Loot para inyectar.");
                return (0, 0);
            }

            string[] scenarioGuids = AssetDatabase.FindAssets("t:Prefab", new[] { SCENARIOS_FOLDER });
            int cuartosConLoot = 0;
            int totalLoot = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < scenarioGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(scenarioGuids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    string scenarioName = Path.GetFileNameWithoutExtension(path);

                    // Omitir elementos no transitables
                    if (scenarioName.IndexOf("Muro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        scenarioName.IndexOf("Tapon", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("Inyectando Botín",
                        $"Escenario: {scenarioName} ({i + 1}/{scenarioGuids.Length})",
                        (float)i / scenarioGuids.Length);

                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    if (root == null) continue;

                    try
                    {
                        // Limpiar instancias previas de Loot_
                        LimpiarLootPrevio(root);

                        // Buscar si existe un HeroProp_
                        Transform heroProp = BuscarHeroProp(root);

                        bool inyecto = false;

                        if (heroProp != null)
                        {
                            // 1. DENSIDAD EN HERO PROPS:
                            // 100% de probabilidad: SIEMPRE debe aparecer 1 objeto de botín encima del mueble (offset Y = 1.1f)
                            GameObject loot1 = lootPrefabs[UnityEngine.Random.Range(0, lootPrefabs.Count)];
                            Vector3 spawnPos1 = heroProp.localPosition + new Vector3(0f, 1.1f, 0f);
                            Quaternion spawnRot1 = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                            InstanciarLoot(root, loot1, spawnPos1, spawnRot1);
                            inyecto = true;
                            totalLoot++;

                            // 70% de probabilidad de un SEGUNDO objeto extra diferente (offset 0.4f, 1.1f, 0.4f)
                            if (UnityEngine.Random.value <= 0.70f)
                            {
                                GameObject loot2 = ObtenerItemDiferente(lootPrefabs, loot1);
                                Vector3 spawnPos2 = heroProp.localPosition + new Vector3(0.4f, 1.1f, 0.4f);
                                Quaternion spawnRot2 = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                                InstanciarLoot(root, loot2, spawnPos2, spawnRot2);
                                totalLoot++;
                            }
                        }
                        else
                        {
                            // 2. DENSIDAD EN SUELO Y PASILLOS (Cuartos sin Hero Props):
                            // Bucle for entre 3 y 6 objetos por escenario, 80% probabilidad cada ciclo
                            int intentosGeneracion = UnityEngine.Random.Range(3, 7);
                            for (int k = 0; k < intentosGeneracion; k++)
                            {
                                if (UnityEngine.Random.value <= 0.80f)
                                {
                                    GameObject lootElegido = lootPrefabs[UnityEngine.Random.Range(0, lootPrefabs.Count)];
                                    float offsetX = UnityEngine.Random.Range(-2.5f, 2.5f);
                                    float offsetZ = UnityEngine.Random.Range(-2.5f, 2.5f);
                                    Vector3 spawnPos = new Vector3(offsetX, 0.1f, offsetZ);
                                    Quaternion spawnRot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                                    InstanciarLoot(root, lootElegido, spawnPos, spawnRot);
                                    inyecto = true;
                                    totalLoot++;
                                }
                            }
                        }

                        if (inyecto)
                        {
                            cuartosConLoot++;
                        }

                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[LootDistributionTool] Error en '{scenarioName}': {ex.Message}");
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return (cuartosConLoot, totalLoot);
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS AUXILIARES
        // ════════════════════════════════════════════════════════════════════

        private static List<GameObject> CargarPrefabsDeLoot()
        {
            List<GameObject> lista = new List<GameObject>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { LOOT_FOLDER });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    lista.Add(prefab);
                }
            }

            return lista;
        }

        private static GameObject ObtenerItemDiferente(List<GameObject> catalogo, GameObject itemAExcluir)
        {
            if (catalogo == null || catalogo.Count == 0) return null;
            if (catalogo.Count == 1) return catalogo[0];

            List<GameObject> candidatos = catalogo.Where(p => p != itemAExcluir).ToList();
            if (candidatos.Count > 0)
            {
                return candidatos[UnityEngine.Random.Range(0, candidatos.Count)];
            }

            return catalogo[UnityEngine.Random.Range(0, catalogo.Count)];
        }

        private static int LimpiarLootPrevio(GameObject root)
        {
            int destruidos = 0;
            Transform[] descendientes = root.GetComponentsInChildren<Transform>(true);
            List<GameObject> paraEliminar = new List<GameObject>();

            foreach (Transform t in descendientes)
            {
                if (t == null || t == root.transform) continue;

                if (t.name.StartsWith("Loot_", StringComparison.OrdinalIgnoreCase))
                {
                    paraEliminar.Add(t.gameObject);
                }
            }

            for (int i = paraEliminar.Count - 1; i >= 0; i--)
            {
                if (paraEliminar[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(paraEliminar[i]);
                    destruidos++;
                }
            }

            return destruidos;
        }

        private static Transform BuscarHeroProp(GameObject root)
        {
            Transform[] descendientes = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in descendientes)
            {
                if (t == null || t == root.transform) continue;

                if (t.name.IndexOf("HeroProp_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return t;
                }
            }

            return null;
        }

        private static void InstanciarLoot(GameObject scenarioInstance, GameObject lootPrefab, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject lootInstance = (GameObject)PrefabUtility.InstantiatePrefab(lootPrefab, scenarioInstance.transform);
            if (lootInstance == null)
            {
                lootInstance = UnityEngine.Object.Instantiate(lootPrefab, scenarioInstance.transform);
            }

            lootInstance.transform.localPosition = localPosition;
            lootInstance.transform.localRotation = localRotation;
            lootInstance.name = $"Loot_{lootPrefab.name}";
        }
    }
}
#endif
