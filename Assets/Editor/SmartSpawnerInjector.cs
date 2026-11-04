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
    /// Sistema definitivo de inyección procedural y Hero Props para escenarios modulares de SectorCero.
    /// Automatiza la limpieza profunda, colocación temática de muebles principales (Hero Props)
    /// y dispersión orgánica de reguero procedural respetando la zona segura del jugador.
    /// </summary>
    public static class SmartSpawnerInjector
    {
        // ════════════════════════════════════════════════════════════════════
        // CONSTANTES DE RUTAS Y FILTROS ESTRICTOS
        // ════════════════════════════════════════════════════════════════════

        public const string SCENARIOS_FOLDER = "Assets/Prefabs/Entornos/SectorCero";
        public const string PROPS_FOLDER = "Assets/Prefabs/Entornos/Props";
        public const string FORBIDDEN_WEAPONS_FOLDER = "Assets/Prefabs/Entornos/Armas_Prefab";

        // Exclusión total: armas, municiones y derivados
        private static readonly string[] EXCLUDED_PROP_KEYWORDS = new string[]
        {
            "Arma", "Revolver", "Gun", "Shotgun", "Pistol", "Bala"
        };

        // Fase 0: Palabras clave de limpieza total
        private static readonly string[] CLEANUP_KEYWORDS = new string[]
        {
            "Punto_Spawn", "Spawner", "HeroProp_", "Revolver", "Arma"
        };

        // ════════════════════════════════════════════════════════════════════
        // PUNTO DE ENTRADA: MENÚ SUPERIOR EN UNITY EDITOR
        // ════════════════════════════════════════════════════════════════════

        [MenuItem("Demencia/Inyección Definitiva de Escenarios", false, 1)]
        [MenuItem("Tools/Demencia/Inyección Definitiva de Escenarios", false, 1)]
        public static void EjecutarInyeccionDefinitivaMenu()
        {
            EjecutarInyeccionCompleta(true);
        }

        /// <summary>
        /// Ejecuta el proceso completo de inyección procedural en todos los escenarios de SectorCero.
        /// </summary>
        /// <param name="mostrarDialogo">Indica si debe mostrar una ventana modal de resumen al finalizar.</param>
        public static void EjecutarInyeccionCompleta(bool mostrarDialogo = true)
        {
            int totalEscenarios = 0;
            int totalHeroPropsInyectados = 0;
            int totalSpawnersRegueroInyectados = 0;
            int totalObjetosLimpiados = 0;

            try
            {
                // 1. CARGA DE RECURSOS (EXCLUSIVAMENTE PROPS, CERO ARMAS)
                List<GameObject> propsDisponibles = CargarPropsValidos();

                if (propsDisponibles == null || propsDisponibles.Count == 0)
                {
                    string errorMsg = $"[SmartSpawnerInjector] ERROR CRÍTICO: No se encontraron prefabs válidos en '{PROPS_FOLDER}'. " +
                                      "Verifica que la carpeta contenga los props organizados antes de ejecutar la inyección.";
                    Debug.LogError(errorMsg);
                    if (mostrarDialogo)
                    {
                        EditorUtility.DisplayDialog("Inyección Fallida", errorMsg, "Aceptar");
                    }
                    return;
                }

                // Cargar todos los prefabs de escenarios en SectorCero
                string[] scenarioGuids = AssetDatabase.FindAssets("t:Prefab", new[] { SCENARIOS_FOLDER });
                if (scenarioGuids == null || scenarioGuids.Length == 0)
                {
                    Debug.LogWarning($"[SmartSpawnerInjector] No se encontraron escenarios en '{SCENARIOS_FOLDER}'.");
                    return;
                }

                Debug.Log($"<color=#38BDF8><b>[SmartSpawnerInjector]</b> Iniciando inyección procedural sobre {scenarioGuids.Length} escenarios " +
                          $"utilizando {propsDisponibles.Count} props de catálogo (Armas_Prefab 100% excluido)...</color>");

                // Deshabilitar autorefresco durante el procesamiento en lote
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < scenarioGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(scenarioGuids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    string scenarioName = Path.GetFileNameWithoutExtension(path);

                    // Omitir elementos que no son salas ni pasillos transitables (ej. muros de cierre)
                    if (scenarioName.IndexOf("Muro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        scenarioName.IndexOf("Tapon", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("Inyectando Escenarios Definitivos",
                        $"Procesando: {scenarioName} ({i + 1}/{scenarioGuids.Length})",
                        (float)i / scenarioGuids.Length);

                    GameObject scenarioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (scenarioPrefab == null) continue;

                    // Instanciar temporalmente en la escena para sincronizar físicas y raycasts
                    GameObject scenarioInstance = (GameObject)PrefabUtility.InstantiatePrefab(scenarioPrefab);
                    if (scenarioInstance == null)
                    {
                        scenarioInstance = UnityEngine.Object.Instantiate(scenarioPrefab);
                    }

                    scenarioInstance.transform.position = Vector3.zero;
                    scenarioInstance.transform.rotation = Quaternion.identity;
                    Physics.SyncTransforms();

                    try
                    {
                        // FASE 0: LIMPIEZA TOTAL PREVIA
                        int limpiados = LimpiarHijosPrevios(scenarioInstance);
                        totalObjetosLimpiados += limpiados;

                        // FASE 1: HERO PROPS (Muebles grandes en cuartos temáticos)
                        bool heroColocado = InyectarHeroProp(scenarioInstance, scenarioName, propsDisponibles);
                        if (heroColocado) totalHeroPropsInyectados++;

                        // FASE 2: REGUERO PROCEDURAL DINÁMICO
                        int regueroColocados = InyectarRegueroProcedural(scenarioInstance, scenarioName, propsDisponibles);
                        totalSpawnersRegueroInyectados += regueroColocados;

                        // FASE 5: GUARDADO CON PREFAB UTILITY
                        PrefabUtility.SavePrefabAsset(scenarioInstance);
                        PrefabUtility.SaveAsPrefabAsset(scenarioInstance, path);

                        totalEscenarios++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SmartSpawnerInjector] Excepción procesando escenario '{scenarioName}': {ex.Message}\n{ex.StackTrace}");
                    }
                    finally
                    {
                        // Limpiar instancia temporal de la escena de trabajo
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

            // Reporte final en consola y diálogo modal
            string resumen = $"<b>[SmartSpawnerInjector] ✔ Inyección Finalizada con Éxito:</b>\n" +
                             $"• Escenarios procesados y guardados: {totalEscenarios}\n" +
                             $"• Objetos obsoletos limpiados (Fase 0): {totalObjetosLimpiados}\n" +
                             $"• Hero Props colocados (Fase 1): {totalHeroPropsInyectados}\n" +
                             $"• Spawners de reguero orgánico (Fase 2): {totalSpawnersRegueroInyectados}\n" +
                             $"• Exclusión total: 0 armas o municiones cargadas.";

            Debug.Log($"<color=#4ADE80>{resumen}</color>");

            if (mostrarDialogo)
            {
                EditorUtility.DisplayDialog("Inyección Definitiva de Escenarios",
                    $"Proceso completado satisfactoriamente:\n\n" +
                    $"• Escenarios inyectados: {totalEscenarios}\n" +
                    $"• Hero Props: {totalHeroPropsInyectados}\n" +
                    $"• Spawners de reguero: {totalSpawnersRegueroInyectados}\n" +
                    $"• Spawners antiguos eliminados: {totalObjetosLimpiados}\n\n" +
                    "Revisa la consola para más detalles.",
                    "Excelente");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. CARGA DE RECURSOS CON EXCLUSIÓN ESTRICTA DE ARMAS
        // ════════════════════════════════════════════════════════════════════

        private static List<GameObject> CargarPropsValidos()
        {
            List<GameObject> props = new List<GameObject>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PROPS_FOLDER });

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                // Verificación de seguridad 1: jamás cargar de Armas_Prefab
                if (assetPath.IndexOf(FORBIDDEN_WEAPONS_FOLDER, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    assetPath.IndexOf("Armas_Prefab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    assetPath.IndexOf("Modelos_Armas", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(assetPath);

                // Verificación de seguridad 2: excluir armas o balas por nombre
                if (EsNombreArmaOMunicion(fileName))
                {
                    continue;
                }

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefabAsset != null)
                {
                    props.Add(prefabAsset);
                }
            }

            return props;
        }

        private static bool EsNombreArmaOMunicion(string nombre)
        {
            foreach (var kw in EXCLUDED_PROP_KEYWORDS)
            {
                if (nombre.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. FASE 0: LIMPIEZA TOTAL PREVIA
        // ════════════════════════════════════════════════════════════════════

        private static int LimpiarHijosPrevios(GameObject root)
        {
            int destruidos = 0;
            Transform[] descendientes = root.GetComponentsInChildren<Transform>(true);
            List<GameObject> paraEliminar = new List<GameObject>();

            foreach (Transform t in descendientes)
            {
                if (t == null || t == root.transform) continue;

                string objName = t.gameObject.name;
                foreach (string kw in CLEANUP_KEYWORDS)
                {
                    if (objName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        paraEliminar.Add(t.gameObject);
                        break;
                    }
                }
            }

            // Destrucción en orden inverso para preservar integridad del árbol
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

        // ════════════════════════════════════════════════════════════════════
        // 3. FASE 1: HERO PROPS (MUEBLES GRANDES EN ZONA SEGURA)
        // ════════════════════════════════════════════════════════════════════

        private static bool InyectarHeroProp(GameObject scenarioInstance, string scenarioName, List<GameObject> propsDisponibles)
        {
            // Regla estricta: Si el escenario es un pasillo o conector, no lleva Hero Prop
            if (EsPasilloOConector(scenarioName))
            {
                return false;
            }

            // Identificar temática según el nombre del escenario
            string lowerName = scenarioName.ToLowerInvariant();
            List<string> candidatasHero = new List<string>();

            // Baño ("baño", "bano"): Tina, Lava manos, retretes desportillados
            if (lowerName.Contains("baño") || lowerName.Contains("bano") || lowerName.Contains("vestuario"))
            {
                candidatasHero.AddRange(new[] { "Tina", "Lava manos", "retretes desportillados" });
            }
            // Oficina ("oficina", "cyberpunk"): Escritorio, Estante, Sillas giratorias ergonómicas
            else if (lowerName.Contains("oficina") || lowerName.Contains("cyberpunk") ||
                     lowerName.Contains("seguridad") || lowerName.Contains("monitoreo") ||
                     lowerName.Contains("servidor") || lowerName.Contains("archivo"))
            {
                candidatasHero.AddRange(new[] { "Escritorio", "Estante", "Sillas giratorias ergonómicas" });
            }
            // Zona especial ("zona", "carniceria", "tortura"): Mesa Tortura
            else if (lowerName.Contains("zona") || lowerName.Contains("carniceria") || lowerName.Contains("tortura") ||
                     lowerName.Contains("arena") || lowerName.Contains("laboratorio") || lowerName.Contains("rehen"))
            {
                candidatasHero.Add("Mesa Tortura");
            }

            if (candidatasHero.Count == 0)
            {
                return false;
            }

            // Seleccionar aleatoriamente una opción de la temática
            string eleccionHero = candidatasHero[UnityEngine.Random.Range(0, candidatasHero.Count)];
            GameObject heroPrefab = BuscarPrefabPorNombre(propsDisponibles, eleccionHero);

            if (heroPrefab == null)
            {
                return false;
            }

            // Generar posición X y Z con exclusión del centro (zona de dona: radio >= 2f)
            bool esCuartoGrande = EsArenaOCuartoGrande(scenarioName);
            float radioMin = 2.0f;
            float radioMax = esCuartoGrande ? 6.5f : 3.0f;

            float angulo = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float radio = UnityEngine.Random.Range(radioMin, radioMax);
            float posX = Mathf.Cos(angulo) * radio;
            float posZ = Mathf.Sin(angulo) * radio;

            // Physics.Raycast desde Y = 6f hacia abajo
            float posY = 0.1f; // Fallback
            Ray ray = new Ray(new Vector3(posX, 6f, posZ), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f))
            {
                posY = hit.point.y + 0.05f;
            }

            // Rotación aleatoria sobre el eje Y
            Quaternion rotacion = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            // Instanciar como Prefab hijo anidado
            GameObject heroInstance = (GameObject)PrefabUtility.InstantiatePrefab(heroPrefab, scenarioInstance.transform);
            if (heroInstance == null)
            {
                heroInstance = UnityEngine.Object.Instantiate(heroPrefab, scenarioInstance.transform);
            }

            heroInstance.transform.localPosition = new Vector3(posX, posY, posZ);
            heroInstance.transform.localRotation = rotacion;
            heroInstance.name = $"HeroProp_{heroPrefab.name}";

            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. FASE 2: REGUERO PROCEDURAL DINÁMICO
        // ════════════════════════════════════════════════════════════════════

        private static int InyectarRegueroProcedural(GameObject scenarioInstance, string scenarioName, List<GameObject> propsDisponibles)
        {
            int cantidadSpawners;
            float minArea, maxArea;

            bool esPasillo = EsPasilloOConector(scenarioName);
            bool esArena = EsArenaOCuartoGrande(scenarioName);

            // Determinar densidad y límites de área según tipo de cuarto
            if (esPasillo)
            {
                // Pasillos / Conectores: Random.Range(0, 3) spawners. Área X/Z: [-1.5f, 1.5f]
                cantidadSpawners = UnityEngine.Random.Range(0, 3);
                minArea = -1.5f;
                maxArea = 1.5f;
            }
            else if (esArena)
            {
                // Arenas / Cuartos grandes: Random.Range(18, 30) spawners. Área X/Z: [-8f, 8f]
                cantidadSpawners = UnityEngine.Random.Range(18, 30);
                minArea = -8f;
                maxArea = 8f;
            }
            else
            {
                // Cuartos medianos (resto): Random.Range(6, 12) spawners. Área X/Z: [-3.5f, 3.5f]
                cantidadSpawners = UnityEngine.Random.Range(6, 12);
                minArea = -3.5f;
                maxArea = 3.5f;
            }

            int inyectados = 0;

            for (int i = 0; i < cantidadSpawners; i++)
            {
                // Seleccionar un prop aleatorio de la colección válida
                GameObject selectedProp = propsDisponibles[UnityEngine.Random.Range(0, propsDisponibles.Count)];
                string propNameLower = selectedProp.name.ToLowerInvariant();

                bool esObjetoTecho = propNameLower.Contains("techo") || propNameLower.Contains("luz") || propNameLower.Contains("lampara");

                float x = 0f;
                float z = 0f;
                float y = 0.1f;

                if (esObjetoTecho)
                {
                    // REGLA DE TECHO: Objetos de techo o lámparas van a Y = 7.9f
                    x = UnityEngine.Random.Range(minArea, maxArea);
                    z = UnityEngine.Random.Range(minArea, maxArea);
                    y = 7.9f;
                }
                else
                {
                    // REGLA DE PISO + ZONA SEGURA DEL JUGADOR: radio mínimo de 2f respecto a (0,0)
                    bool posicionValida = false;
                    int intentos = 0;

                    while (!posicionValida && intentos < 50)
                    {
                        intentos++;
                        x = UnityEngine.Random.Range(minArea, maxArea);
                        z = UnityEngine.Random.Range(minArea, maxArea);

                        float distSq = x * x + z * z;

                        if (esPasillo)
                        {
                            // En pasillos estrechos (1.5x1.5), se prioriza distancia al centro >= 2f o >= 1.2f
                            if (distSq >= 4.0f || (intentos > 20 && distSq >= 1.44f))
                            {
                                posicionValida = true;
                            }
                        }
                        else
                        {
                            // En cuartos medianos y arenas: estrictamente >= 2 unidades del centro (0,0)
                            if (distSq >= 4.0f)
                            {
                                posicionValida = true;
                            }
                        }
                    }

                    // REGLA DE PISO: Physics.Raycast desde Y = 6f hacia abajo
                    Ray ray = new Ray(new Vector3(x, 6f, z), Vector3.down);
                    if (Physics.Raycast(ray, out RaycastHit hit, 10f))
                    {
                        y = hit.point.y + 0.05f;
                    }
                    else
                    {
                        y = 0.1f; // Fallback
                    }
                }

                // Rotación aleatoria en Y
                Quaternion rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                // Instanciar Prefab hijo anidado
                GameObject propInstance = (GameObject)PrefabUtility.InstantiatePrefab(selectedProp, scenarioInstance.transform);
                if (propInstance == null)
                {
                    propInstance = UnityEngine.Object.Instantiate(selectedProp, scenarioInstance.transform);
                }

                propInstance.transform.localPosition = new Vector3(x, y, z);
                propInstance.transform.localRotation = rot;
                propInstance.name = $"Spawner_{selectedProp.name}_{i + 1}";

                inyectados++;
            }

            return inyectados;
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS AUXILIARES DE CLASIFICACIÓN Y BÚSQUEDA
        // ════════════════════════════════════════════════════════════════════

        private static bool EsPasilloOConector(string scenarioName)
        {
            string lower = scenarioName.ToLowerInvariant();
            return lower.Contains("pasillo") || lower.Contains("conector") || lower.Contains("corr") ||
                   lower.Contains("tunel") || lower.Contains("cruce") || lower.Contains("interseccion") ||
                   lower.Contains("rampa");
        }

        private static bool EsArenaOCuartoGrande(string scenarioName)
        {
            string lower = scenarioName.ToLowerInvariant();
            return lower.Contains("arena") || lower.Contains("20x20") || lower.Contains("24x24") ||
                   lower.Contains("28x28") || lower.Contains("zona") || lower.Contains("hangar") ||
                   lower.Contains("reactor") || lower.Contains("fabrica") || lower.Contains("ensamblaje");
        }

        private static GameObject BuscarPrefabPorNombre(List<GameObject> props, string keyword)
        {
            string kwLower = keyword.ToLowerInvariant().Trim();

            // 1. Coincidencia exacta de nombre
            var match = props.FirstOrDefault(p => p.name.Trim().Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // 2. Coincidencia que comience con el término
            match = props.FirstOrDefault(p => p.name.ToLowerInvariant().Trim().StartsWith(kwLower));
            if (match != null) return match;

            // 3. Coincidencia parcial que contenga la palabra clave
            return props.FirstOrDefault(p => p.name.ToLowerInvariant().Contains(kwLower));
        }
    }
}
#endif
