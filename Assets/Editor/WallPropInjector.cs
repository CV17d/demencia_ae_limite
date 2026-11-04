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
    /// Inyector de Props de Pared adaptativo y seguro contra explosiones físicas para SectorCero.
    /// Características técnicas:
    /// 1. Preparación Automática: Añade MeshCollider a cualquier MeshRenderer sin colisionador en el cuarto.
    /// 2. Raycast Adaptativo: Lanza 4 rayos cardinales locales a 1.5m con alcance extendido a 15m.
    /// 3. Inyección Segura:
    ///    - Offset normal de +0.05m (hit.normal * 0.05f) para evitar que el prop se incruste en la malla.
    ///    - LookRotation(hit.normal) para orientar el prop mirando al interior del cuarto.
    ///    - Desfase vertical aleatorio en Y [-0.3f, 0.4f].
    /// 4. Neutralización Física: Fuerza isTrigger = true en todos los colliders del prop instanciado,
    ///    eliminando al 100% las fuerzas físicas reactivas o expulsiones entre geometrías.
    /// </summary>
    public static class WallPropInjector
    {
        public const string SCENARIOS_FOLDER = "Assets/Prefabs/Entornos/SectorCero";
        public const string WALL_PROPS_FOLDER = "Assets/Prefabs/Entornos/Props_Pared";
        public const float RAYCAST_MAX_DISTANCE = 15.0f;
        public const float SPAWN_PROBABILITY = 0.60f;
        public const float RAYCAST_HEIGHT = 1.5f;
        public const float WALL_NORMAL_OFFSET = 0.05f;

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
        /// Ejecuta el proceso de detección adaptativa por Raycast e inyección segura en todos los escenarios.
        /// </summary>
        public static void EjecutarInyeccionWallProps(bool mostrarDialogo = true)
        {
            // 1. CARGA DE PREFABS DE PARED
            List<GameObject> wallPropPrefabs = CargarPrefabsWallProps();

            if (wallPropPrefabs == null || wallPropPrefabs.Count == 0)
            {
                string errorMsg = $"[WallPropInjector] ERROR: No se encontraron prefabs en '{WALL_PROPS_FOLDER}'. " +
                                  "Asegúrate de que la carpeta contenga prefabs de pared antes de inyectar.";
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

            Debug.Log($"<color=#38BDF8><b>[WallPropInjector]</b> Iniciando inyección segura de Props de Pared sobre {scenarioGuids.Length} escenarios " +
                      $"(Raycast adaptativo 15m, offset normal 0.05m e isTrigger forzado)...</color>");

            int totalEscenariosProcesados = 0;
            int totalWallPropsInyectados = 0;
            int totalWallPropsLimpiados = 0;
            int totalCollidersParedGenerados = 0;

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

                    EditorUtility.DisplayProgressBar("Inyectando Props de Pared (Físicas Seguras)",
                        $"Procesando: {scenarioName} ({i + 1}/{scenarioGuids.Length})",
                        (float)i / scenarioGuids.Length);

                    GameObject scenarioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (scenarioPrefab == null) continue;

                    // Instanciar temporalmente en la escena para operar con el motor de físicas
                    GameObject scenarioInstance = (GameObject)PrefabUtility.InstantiatePrefab(scenarioPrefab);
                    if (scenarioInstance == null)
                    {
                        scenarioInstance = UnityEngine.Object.Instantiate(scenarioPrefab);
                    }

                    scenarioInstance.transform.position = Vector3.zero;
                    scenarioInstance.transform.rotation = Quaternion.identity;

                    try
                    {
                        // 1. LIMPIEZA PREVIA: Destruir cualquier hijo con prefijo "WallProp_"
                        int limpiados = LimpiarWallPropsPrevios(scenarioInstance);
                        totalWallPropsLimpiados += limpiados;

                        // 2. PREPARACIÓN AUTOMÁTICA DE PAREDES:
                        // Garantiza que cualquier MeshRenderer sin Collider obtenga un MeshCollider
                        int collidersNuevos = PrepararCollidersDeParedes(scenarioInstance);
                        totalCollidersParedGenerados += collidersNuevos;

                        // Sincronizar matrices físicas antes del Raycast
                        Physics.SyncTransforms();

                        // 3. RAYCAST ADAPTATIVO (15m en 4 direcciones cardinales locales)
                        Vector3 origenLocal = new Vector3(0f, RAYCAST_HEIGHT, 0f);
                        Vector3 origenMundo = scenarioInstance.transform.TransformPoint(origenLocal);

                        Vector3[] direcciones = new Vector3[]
                        {
                            scenarioInstance.transform.forward,  // Adelante (Z+)
                            -scenarioInstance.transform.forward, // Atrás (Z-)
                            -scenarioInstance.transform.right,   // Izquierda (X-)
                            scenarioInstance.transform.right     // Derecha (X+)
                        };

                        foreach (Vector3 dir in direcciones)
                        {
                            // Lanzar Raycast ignorando triggers (solo muros sólidos)
                            if (Physics.Raycast(origenMundo, dir, out RaycastHit hit, RAYCAST_MAX_DISTANCE, ~0, QueryTriggerInteraction.Ignore))
                            {
                                // 4. INYECCIÓN SEGURA (60% probabilidad)
                                if (UnityEngine.Random.value <= SPAWN_PROBABILITY)
                                {
                                    GameObject propElegido = wallPropPrefabs[UnityEngine.Random.Range(0, wallPropPrefabs.Count)];

                                    // Posición crítica anti-incrustación: hit.point + (hit.normal * 0.05f)
                                    // Altura aleatoria entre -0.3f y 0.4f
                                    float offsetVertical = UnityEngine.Random.Range(-0.3f, 0.4f);
                                    Vector3 posicionFinal = hit.point + (hit.normal * WALL_NORMAL_OFFSET) + new Vector3(0f, offsetVertical, 0f);

                                    // Rotación crítica: LookRotation(hit.normal)
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

                                    // 5. NEUTRALIZACIÓN FÍSICA (CRÍTICO: isTrigger = true)
                                    // Desactiva fuerzas físicas de colisión para evitar explosiones/empujes
                                    NeutralizarColisionesFisicas(propInstance);

                                    totalWallPropsInyectados++;
                                }
                            }
                        }

                        // 6. GUARDADO DE CAMBIOS
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

            string resumen = $"<b>[WallPropInjector] ✔ Inyección Segura de Props de Pared Finalizada:</b>\n" +
                             $"• Escenarios evaluados y guardados: {totalEscenariosProcesados}\n" +
                             $"• Props de pared inyectados (60% éxito): {totalWallPropsInyectados}\n" +
                             $"• Mallas de pared preparadas con MeshCollider: {totalCollidersParedGenerados}\n" +
                             $"• Props antiguos eliminados: {totalWallPropsLimpiados}\n" +
                             $"• Físicas protegidas: Offset normal (0.05m) e isTrigger = TRUE en todos los props.";

            Debug.Log($"<color=#4ADE80>{resumen}</color>");

            if (mostrarDialogo)
            {
                EditorUtility.DisplayDialog("Inyección Segura de Props de Pared",
                    $"Proceso completado exitosamente sin conflictos físicos:\n\n" +
                    $"• Escenarios procesados: {totalEscenariosProcesados}\n" +
                    $"• Props inyectados: {totalWallPropsInyectados}\n" +
                    $"• Colliders de pared generados: {totalCollidersParedGenerados}\n" +
                    $"• Muros antiguos limpiados: {totalWallPropsLimpiados}\n\n" +
                    "Configuración aplicada:\n" +
                    "✔ Raycast adaptativo a 15m\n" +
                    "✔ Separación normal de 5cm (sin incrustación)\n" +
                    "✔ isTrigger = TRUE (sin explosiones de físicas)",
                    "Excelente");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS AUXILIARES DE PREVENCIÓN FÍSICA
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Itera por todos los Transform hijos. Si un objeto tiene MeshRenderer pero carece
        /// de Collider, le añade un MeshCollider para asegurar que el Raycast siempre encuentre el muro.
        /// </summary>
        private static int PrepararCollidersDeParedes(GameObject root)
        {
            int agregados = 0;
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);

            foreach (MeshRenderer mr in renderers)
            {
                if (mr == null) continue;

                Collider col = mr.GetComponent<Collider>();
                if (col == null)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        MeshCollider mc = mr.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        agregados++;
                    }
                }
            }

            return agregados;
        }

        /// <summary>
        /// Neutraliza cualquier fuerza física forzando isTrigger = true en todos los Colliders
        /// del prop de pared instanciado, impidiendo explosiones físicas por superposición.
        /// </summary>
        private static void NeutralizarColisionesFisicas(GameObject propInstance)
        {
            Collider[] colliders = propInstance.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in colliders)
            {
                if (c != null)
                {
                    c.isTrigger = true;
                }
            }
        }

        /// <summary>
        /// Carga todos los prefabs de pared disponibles en Assets/Prefabs/Entornos/Props_Pared.
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
