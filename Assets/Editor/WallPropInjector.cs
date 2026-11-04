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
    /// Inyector de Props de Pared para escenarios modulares de SectorCero.
    /// Utiliza 4 Raycasts laterales cardinales a 1.5m de altura para detectar muros,
    /// alinear props contra la pared mirando al interior del cuarto (LookRotation(hit.normal))
    /// y aplicar variación orgánica de altura (+-0.5m).
    /// </summary>
    public static class WallPropInjector
    {
        public const string SCENARIOS_FOLDER = "Assets/Prefabs/Entornos/SectorCero";
        public const string WALL_PROPS_FOLDER = "Assets/Prefabs/Entornos/Props_Pared";
        public const float RAYCAST_MAX_DISTANCE = 10.0f;
        public const float SPAWN_PROBABILITY = 0.60f;
        public const float RAYCAST_HEIGHT = 1.5f;

        // ════════════════════════════════════════════════════════════════════
        // ENTRADA DE MENÚ EN UNITY EDITOR
        // ════════════════════════════════════════════════════════════════════

        [MenuItem("Demencia/Inyectar Props de Pared", false, 3)]
        [MenuItem("Tools/Demencia/Inyectar Props de Pared", false, 3)]
        public static void EjecutarInyeccionMenu()
        {
            EjecutarInyeccionWallProps(true);
        }

        /// <summary>
        /// Ejecuta el proceso de detección de muros e inyección de props de pared en todos los escenarios.
        /// </summary>
        public static void EjecutarInyeccionWallProps(bool mostrarDialogo = true)
        {
            // 1. CARGA DE PREFABS DE PARED
            List<GameObject> wallPropPrefabs = CargarPrefabsWallProps();

            if (wallPropPrefabs == null || wallPropPrefabs.Count == 0)
            {
                string errorMsg = $"[WallPropInjector] ERROR: No se encontraron prefabs en '{WALL_PROPS_FOLDER}'. " +
                                  "Asegúrate de haber organizado los props de pared antes de inyectar.";
                Debug.LogError(errorMsg);
                if (mostrarDialogo)
                {
                    EditorUtility.DisplayDialog("Inyección Cancelada", errorMsg, "Aceptar");
                }
                return;
            }

            string[] scenarioGuids = AssetDatabase.FindAssets("t:Prefab", new[] { SCENARIOS_FOLDER });
            if (scenarioGuids == null || scenarioGuids.Length == 0)
            {
                Debug.LogWarning($"[WallPropInjector] No se encontraron escenarios en '{SCENARIOS_FOLDER}'.");
                return;
            }

            Debug.Log($"<color=#38BDF8><b>[WallPropInjector]</b> Iniciando inyección de Props de Pared sobre {scenarioGuids.Length} escenarios " +
                      $"utilizando {wallPropPrefabs.Count} modelos únicos con Raycasts laterales...</color>");

            int totalEscenariosProcesados = 0;
            int totalWallPropsInyectados = 0;
            int totalWallPropsLimpiados = 0;

            AssetDatabase.StartAssetEditing();

            try
            {
                for (int i = 0; i < scenarioGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(scenarioGuids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    string scenarioName = Path.GetFileNameWithoutExtension(path);

                    // Omitir elementos decorativos no habitables (muros ciegos, tapones)
                    if (scenarioName.IndexOf("Muro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        scenarioName.IndexOf("Tapon", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("Inyectando Props de Pared",
                        $"Procesando: {scenarioName} ({i + 1}/{scenarioGuids.Length})",
                        (float)i / scenarioGuids.Length);

                    GameObject scenarioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (scenarioPrefab == null) continue;

                    // Instanciar en la escena temporalmente para habilitar físicas y raycasts precisos
                    GameObject scenarioInstance = (GameObject)PrefabUtility.InstantiatePrefab(scenarioPrefab);
                    if (scenarioInstance == null)
                    {
                        scenarioInstance = UnityEngine.Object.Instantiate(scenarioPrefab);
                    }

                    scenarioInstance.transform.position = Vector3.zero;
                    scenarioInstance.transform.rotation = Quaternion.identity;

                    // Forzar sincronización de las matrices de colisión en el motor de físicas
                    Physics.SyncTransforms();

                    try
                    {
                        // 1. LIMPIEZA: Eliminar cualquier objeto hijo cuyo nombre comience con "WallProp_"
                        int limpiados = LimpiarWallPropsPrevios(scenarioInstance);
                        totalWallPropsLimpiados += limpiados;

                        // 2. BÚSQUEDA DE PAREDES: Origen en el centro exacto del cuarto a 1.5m de altura
                        Vector3 origenLocal = new Vector3(0f, RAYCAST_HEIGHT, 0f);
                        Vector3 origenMundo = scenarioInstance.transform.TransformPoint(origenLocal);

                        // 4 Direcciones cardinales locales
                        Vector3[] direcciones = new Vector3[]
                        {
                            scenarioInstance.transform.forward,  // Adelante (Z+)
                            -scenarioInstance.transform.forward, // Atrás (Z-)
                            -scenarioInstance.transform.right,   // Izquierda (X-)
                            scenarioInstance.transform.right     // Derecha (X+)
                        };

                        int propsEnEsteCuarto = 0;

                        foreach (Vector3 dir in direcciones)
                        {
                            // Lanzar raycast ignorando triggers (para detectar solo muros sólidos)
                            if (Physics.Raycast(origenMundo, dir, out RaycastHit hit, RAYCAST_MAX_DISTANCE, ~0, QueryTriggerInteraction.Ignore))
                            {
                                // 3. INYECCIÓN Y ALINEACIÓN (60% de probabilidad por pared detectada)
                                if (UnityEngine.Random.value <= SPAWN_PROBABILITY)
                                {
                                    GameObject propElegido = wallPropPrefabs[UnityEngine.Random.Range(0, wallPropPrefabs.Count)];

                                    // Posición exacta del impacto + offset vertical aleatorio [-0.5f, 0.5f]
                                    float offsetVertical = UnityEngine.Random.Range(-0.5f, 0.5f);
                                    Vector3 posicionFinal = hit.point + new Vector3(0f, offsetVertical, 0f);

                                    // Rotación crítica: LookRotation(hit.normal) para que la espalda quede pegada al muro
                                    // y el frente del prop apunte hacia el interior del cuarto
                                    Quaternion rotacionFinal = Quaternion.LookRotation(hit.normal);

                                    // Instanciar como hijo del escenario
                                    GameObject propInstance = (GameObject)PrefabUtility.InstantiatePrefab(propElegido, scenarioInstance.transform);
                                    if (propInstance == null)
                                    {
                                        propInstance = UnityEngine.Object.Instantiate(propElegido, scenarioInstance.transform);
                                    }

                                    propInstance.transform.position = posicionFinal;
                                    propInstance.transform.rotation = rotacionFinal;
                                    propInstance.name = $"WallProp_{propElegido.name}";

                                    totalWallPropsInyectados++;
                                    propsEnEsteCuarto++;
                                }
                            }
                        }

                        // 4. GUARDADO DE CAMBIOS
                        PrefabUtility.SavePrefabAsset(scenarioInstance);
                        PrefabUtility.SaveAsPrefabAsset(scenarioInstance, path);

                        totalEscenariosProcesados++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[WallPropInjector] Error procesando '{scenarioName}': {ex.Message}\n{ex.StackTrace}");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(scenarioInstance);
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

            string resumen = $"<b>[WallPropInjector] ✔ Inyección de Props de Pared Finalizada:</b>\n" +
                             $"• Escenarios evaluados: {totalEscenariosProcesados}\n" +
                             $"• Props de pared inyectados (60% éxito): {totalWallPropsInyectados}\n" +
                             $"• Objetos WallProp obsoletos limpiados: {totalWallPropsLimpiados}\n" +
                             $"• Catálogo utilizado: {wallPropPrefabs.Count} prefabs sólidos";

            Debug.Log($"<color=#4ADE80>{resumen}</color>");

            if (mostrarDialogo)
            {
                EditorUtility.DisplayDialog("Props de Pared Inyectados",
                    $"Proceso completado exitosamente:\n\n" +
                    $"• Escenarios procesados: {totalEscenariosProcesados}\n" +
                    $"• Props de pared colocados: {totalWallPropsInyectados}\n" +
                    $"• Props antiguos eliminados: {totalWallPropsLimpiados}\n" +
                    $"• Alineación: LookRotation(hit.normal) hacia el interior.\n" +
                    $"• Variación de altura: +-0.5m aplicada.\n\n" +
                    "Revisa la consola de Unity para más detalles.",
                    "Excelente");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS AUXILIARES
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Carga todos los prefabs disponibles en la carpeta Props_Pared.
        /// </summary>
        private static List<GameObject> CargarPrefabsWallProps()
        {
            List<GameObject> lista = new List<GameObject>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { WALL_PROPS_FOLDER });

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

        /// <summary>
        /// Busca y destruye cualquier objeto hijo cuyo nombre comience con "WallProp_".
        /// </summary>
        private static int LimpiarWallPropsPrevios(GameObject root)
        {
            int destruidos = 0;
            Transform[] descendientes = root.GetComponentsInChildren<Transform>(true);
            List<GameObject> paraEliminar = new List<GameObject>();

            foreach (Transform t in descendientes)
            {
                if (t == null || t == root.transform) continue;

                if (t.name.StartsWith("WallProp_", StringComparison.OrdinalIgnoreCase))
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
    }
}
#endif

