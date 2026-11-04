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
    /// Inyector de Loot Estratégico de Alta Densidad para SectorCero (Survival Horror).
    /// Reglas implementadas:
    /// 1. Variedad Máxima: Carga dinámica de todos los prefabs en Assets/Prefabs/Loot/ con igual
    ///    probabilidad estadística (Random.Range) evitando repeticiones seguidas.
    /// 2. Densidad Escalada por Tamaño de Cuarto (Suelo a Y = 0.1f):
    ///    - Cuartos Grandes ("arena", "20x20", "zona", "grande"): 8-15 intentos al 85%, X/Z en [-7f, 7f].
    ///    - Cuartos Medianos ("oficina", "baño", "cuarto"): 4-8 intentos al 75%, X/Z en [-3.5f, 3.5f].
    ///    - Pasillos ("pasillo", "conector", "corr"): 1-4 intentos al 50%, X/Z en [-1.5f, 1.5f].
    /// 3. Acumulación en HeroProps (Muebles):
    ///    - 1er objeto: 100% de probabilidad (offset Y = 1.1f).
    ///    - 2do objeto: 85% de probabilidad (offset X = 0.4f, Y = 1.1f, Z = 0.2f).
    ///    - 3er objeto: 50% de probabilidad (offset X = -0.4f, Y = 1.1f, Z = -0.3f).
    /// 4. Limpieza previa total de "Loot_" y guardado persistente con PrefabUtility.
    /// </summary>
    public static class StrategicLootInjector
    {
        public const string SCENARIOS_FOLDER = "Assets/Prefabs/Entornos/SectorCero";
        public const string LOOT_FOLDER = "Assets/Prefabs/Loot";

        private struct ConfiguracionSuelo
        {
            public int MinIntentos;
            public int MaxIntentosInclusive;
            public float ProbabilidadExito;
            public float RangoEsparcimiento;
            public string Categoria;
        }

        // ════════════════════════════════════════════════════════════════════
        // ENTRADA DE MENÚ EN UNITY EDITOR
        // ════════════════════════════════════════════════════════════════════

        [MenuItem("Demencia/Inyectar Loot Proporcional y Variado", false, 2)]
        [MenuItem("Tools/Demencia/Inyectar Loot Proporcional y Variado", false, 2)]
        public static void EjecutarInyeccionLootMenu()
        {
            EjecutarInyeccionLoot(true);
        }

        /// <summary>
        /// Ejecuta el proceso de inyección de loot con variedad estadística y densidad proporcional.
        /// </summary>
        public static void EjecutarInyeccionLoot(bool mostrarDialogo = true)
        {
            // 1. CARGA DINÁMICA DE TODOS LOS PREFABS DE BOTÍN
            GameObject[] lootPrefabs = CargarPrefabsLoot();

            if (lootPrefabs == null || lootPrefabs.Length == 0)
            {
                string errorMsg = $"[StrategicLootInjector] ERROR: No se encontraron prefabs de Loot en '{LOOT_FOLDER}'. " +
                                  "Asegúrate de que la carpeta contenga ítems de botín antes de inyectar.";
                Debug.LogError(errorMsg);
                if (mostrarDialogo)
                {
                    EditorUtility.DisplayDialog("Inyección de Loot Fallida", errorMsg, "Aceptar");
                }
                return;
            }

            // Cargar lista de escenarios en SectorCero
            string[] scenarioGuids = AssetDatabase.FindAssets("t:Prefab", new[] { SCENARIOS_FOLDER });
            if (scenarioGuids == null || scenarioGuids.Length == 0)
            {
                Debug.LogWarning($"[StrategicLootInjector] No se encontraron escenarios en '{SCENARIOS_FOLDER}'.");
                return;
            }

            Debug.Log($"<color=#38BDF8><b>[StrategicLootInjector]</b> Iniciando inyección sobre {scenarioGuids.Length} escenarios " +
                      $"utilizando {lootPrefabs.Length} prefabs de botín únicos con randomización total...</color>");

            int totalEscenariosProcesados = 0;
            int lootSobreMuebles = 0;
            int lootEnSuelo = 0;
            int lootLimpiados = 0;

            GameObject ultimoLootInstanciado = null;

            AssetDatabase.StartAssetEditing();

            try
            {
                for (int i = 0; i < scenarioGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(scenarioGuids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    string scenarioName = Path.GetFileNameWithoutExtension(path);

                    // Omitir muros ciegos o tapones decorativos que no son habitables
                    if (scenarioName.IndexOf("Muro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        scenarioName.IndexOf("Tapon", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("Inyectando Loot Proporcional",
                        $"Procesando: {scenarioName} ({i + 1}/{scenarioGuids.Length})",
                        (float)i / scenarioGuids.Length);

                    GameObject scenarioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (scenarioPrefab == null) continue;

                    // Instanciar en la escena para operar sobre la jerarquía
                    GameObject scenarioInstance = (GameObject)PrefabUtility.InstantiatePrefab(scenarioPrefab);
                    if (scenarioInstance == null)
                    {
                        scenarioInstance = UnityEngine.Object.Instantiate(scenarioPrefab);
                    }

                    scenarioInstance.transform.position = Vector3.zero;
                    scenarioInstance.transform.rotation = Quaternion.identity;

                    try
                    {
                        // A) LIMPIEZA: Buscar y destruir cualquier hijo con prefijo "Loot_"
                        int limpiados = LimpiarLootPrevio(scenarioInstance);
                        lootLimpiados += limpiados;

                        // B) BUSCAR HERO PROPS (Muebles grandes)
                        List<Transform> heroProps = BuscarHeroProps(scenarioInstance);

                        // C) ACUMULACIÓN EN HERO PROPS
                        if (heroProps.Count > 0)
                        {
                            foreach (Transform heroProp in heroProps)
                            {
                                // 1er objeto: 100% de probabilidad (offset Y = 1.1f)
                                GameObject loot1 = ObtenerLootAleatorio(lootPrefabs, ref ultimoLootInstanciado);
                                Vector3 pos1 = heroProp.localPosition + new Vector3(0f, 1.1f, 0f);
                                Quaternion rot1 = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                                InstanciarLoot(scenarioInstance, loot1, pos1, rot1);
                                lootSobreMuebles++;

                                // 2do objeto: 85% de probabilidad (offset X = 0.4f, Y = 1.1f, Z = 0.2f)
                                if (UnityEngine.Random.value <= 0.85f)
                                {
                                    GameObject loot2 = ObtenerLootAleatorio(lootPrefabs, ref ultimoLootInstanciado);
                                    Vector3 pos2 = heroProp.localPosition + new Vector3(0.4f, 1.1f, 0.2f);
                                    Quaternion rot2 = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                                    InstanciarLoot(scenarioInstance, loot2, pos2, rot2);
                                    lootSobreMuebles++;
                                }

                                // 3er objeto: 50% de probabilidad (offset X = -0.4f, Y = 1.1f, Z = -0.3f)
                                if (UnityEngine.Random.value <= 0.50f)
                                {
                                    GameObject loot3 = ObtenerLootAleatorio(lootPrefabs, ref ultimoLootInstanciado);
                                    Vector3 pos3 = heroProp.localPosition + new Vector3(-0.4f, 1.1f, -0.3f);
                                    Quaternion rot3 = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                                    InstanciarLoot(scenarioInstance, loot3, pos3, rot3);
                                    lootSobreMuebles++;
                                }
                            }
                        }

                        // D) DENSIDAD ESCALADA POR TAMAÑO DE CUARTO (Suelo a Y = 0.1f)
                        ConfiguracionSuelo configSuelo = ObtenerConfiguracionSuelo(scenarioName);
                        int intentosSuelo = UnityEngine.Random.Range(configSuelo.MinIntentos, configSuelo.MaxIntentosInclusive + 1);

                        for (int k = 0; k < intentosSuelo; k++)
                        {
                            if (UnityEngine.Random.value <= configSuelo.ProbabilidadExito)
                            {
                                GameObject lootElegido = ObtenerLootAleatorio(lootPrefabs, ref ultimoLootInstanciado);
                                float offsetX = UnityEngine.Random.Range(-configSuelo.RangoEsparcimiento, configSuelo.RangoEsparcimiento);
                                float offsetZ = UnityEngine.Random.Range(-configSuelo.RangoEsparcimiento, configSuelo.RangoEsparcimiento);
                                Vector3 spawnPos = new Vector3(offsetX, 0.1f, offsetZ);
                                Quaternion spawnRot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                                InstanciarLoot(scenarioInstance, lootElegido, spawnPos, spawnRot);
                                lootEnSuelo++;
                            }
                        }

                        // E) GUARDADO PERSISTENTE DE CAMBIOS
                        PrefabUtility.SavePrefabAsset(scenarioInstance);
                        PrefabUtility.SaveAsPrefabAsset(scenarioInstance, path);

                        totalEscenariosProcesados++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[StrategicLootInjector] Error procesando '{scenarioName}': {ex.Message}\n{ex.StackTrace}");
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

            string resumen = $"<b>[StrategicLootInjector] ✔ Inyección de Loot Finalizada:</b>\n" +
                             $"• Escenarios evaluados y actualizados: {totalEscenariosProcesados}\n" +
                             $"• Catálogo de prefabs utilizados: {lootPrefabs.Length} modelos\n" +
                             $"• Botín anterior limpiado: {lootLimpiados}\n" +
                             $"• Botín sobre HeroProps (hasta 3 ítems): {lootSobreMuebles}\n" +
                             $"• Botín proporcional en suelo: {lootEnSuelo}\n" +
                             $"• Total ítems generados: {lootSobreMuebles + lootEnSuelo}";

            Debug.Log($"<color=#4ADE80>{resumen}</color>");

            if (mostrarDialogo)
            {
                EditorUtility.DisplayDialog("Inyección de Loot Proporcional",
                    $"Proceso finalizado exitosamente:\n\n" +
                    $"• Escenarios procesados: {totalEscenariosProcesados}\n" +
                    $"• Modelos únicos en catálogo: {lootPrefabs.Length}\n" +
                    $"• Loot sobre HeroProps: {lootSobreMuebles}\n" +
                    $"• Loot distribuido en suelo: {lootEnSuelo}\n" +
                    $"• Total botín inyectado: {lootSobreMuebles + lootEnSuelo}\n" +
                    $"• Objetos antiguos eliminados: {lootLimpiados}\n\n" +
                    "Todos los prefabs fueron actualizados y guardados.",
                    "Excelente");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS DE APOYO Y RANDOMIZACIÓN
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Carga dinámicamente todos los prefabs presentes en la carpeta de Loot.
        /// </summary>
        private static GameObject[] CargarPrefabsLoot()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { LOOT_FOLDER });
            List<GameObject> lista = new List<GameObject>();

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

            return lista.ToArray();
        }

        /// <summary>
        /// Selecciona un loot garantizando equiprobabilidad entre todos los modelos
        /// y evitando repeticiones contiguas inmediatas.
        /// </summary>
        private static GameObject ObtenerLootAleatorio(GameObject[] lootPrefabs, ref GameObject ultimoLootElegido)
        {
            if (lootPrefabs == null || lootPrefabs.Length == 0) return null;
            if (lootPrefabs.Length == 1) return lootPrefabs[0];

            int index = UnityEngine.Random.Range(0, lootPrefabs.Length);
            GameObject seleccionado = lootPrefabs[index];

            int reintentos = 0;
            while (seleccionado == ultimoLootElegido && reintentos < 10)
            {
                index = UnityEngine.Random.Range(0, lootPrefabs.Length);
                seleccionado = lootPrefabs[index];
                reintentos++;
            }

            ultimoLootElegido = seleccionado;
            return seleccionado;
        }

        /// <summary>
        /// Determina los parámetros de densidad y esparcimiento en suelo según el nombre del cuarto.
        /// </summary>
        private static ConfiguracionSuelo ObtenerConfiguracionSuelo(string scenarioName)
        {
            string lower = scenarioName.ToLowerInvariant();

            // 1. CUARTOS GRANDES ("arena", "20x20", "zona", "grande")
            if (lower.Contains("arena") || lower.Contains("20x20") || lower.Contains("zona") || lower.Contains("grande"))
            {
                return new ConfiguracionSuelo
                {
                    MinIntentos = 8,
                    MaxIntentosInclusive = 15,
                    ProbabilidadExito = 0.85f,
                    RangoEsparcimiento = 7f,
                    Categoria = "Grande"
                };
            }

            // 2. PASILLOS ("pasillo", "conector", "corr")
            if (lower.Contains("pasillo") || lower.Contains("conector") || lower.Contains("corr"))
            {
                return new ConfiguracionSuelo
                {
                    MinIntentos = 1,
                    MaxIntentosInclusive = 4,
                    ProbabilidadExito = 0.50f,
                    RangoEsparcimiento = 1.5f,
                    Categoria = "Pasillo"
                };
            }

            // 3. CUARTOS MEDIANOS ("oficina", "baño", "cuarto" y salas generales)
            return new ConfiguracionSuelo
            {
                MinIntentos = 4,
                MaxIntentosInclusive = 8,
                ProbabilidadExito = 0.75f,
                RangoEsparcimiento = 3.5f,
                Categoria = "Mediano"
            };
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

        private static List<Transform> BuscarHeroProps(GameObject root)
        {
            List<Transform> list = new List<Transform>();
            Transform[] descendientes = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in descendientes)
            {
                if (t == null || t == root.transform) continue;

                if (t.name.IndexOf("HeroProp_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    list.Add(t);
                }
            }

            return list;
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
