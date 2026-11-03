// ============================================================================
// ElevatorLightPropagator.cs — Automatización de Iluminación para Sector Cero
// ============================================================================
// Utilidad de Editor que toma como plantilla el Point Light con LightFlicker
// del elevador modelo (SM_Elevador_Carga_8x8) y lo replica automáticamente
// en todos los demás prefabs arquitectónicos de la carpeta de entornos.
// ============================================================================

#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor;
using DemenciaNoLimite.Level;
using DemenciaNoLimite.Environment;

namespace DemenciaNoLimite.Editor
{
    public static class ElevatorLightPropagator
    {
        private const string TemplateElevatorPath = "Assets/Prefabs/Entornos/SectorCero/SM_Elevador_Carga_8x8.prefab";
        private const string TargetFolder = "Assets/Prefabs/Entornos/SectorCero";

        [MenuItem("Tools/Sector Cero/Copiar Luz de Elevador a Demás Prefabs", false, 100)]
        [MenuItem("Demencia/Sector Cero/Copiar Luz de Elevador a Demás Prefabs", false, 10)]
        public static void PropagarLuzElevador()
        {
            // 1. Validar existencia del prefab plantilla
            string templatePath = TemplateElevatorPath;
            if (!File.Exists(templatePath))
            {
                // Búsqueda de respaldo si la ruta fue movida
                string[] foundGuids = AssetDatabase.FindAssets("SM_Elevador_Carga_8x8 t:Prefab", new[] { "Assets/Prefabs" });
                if (foundGuids.Length > 0)
                {
                    templatePath = AssetDatabase.GUIDToAssetPath(foundGuids[0]);
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Error de Plantilla",
                        $"No se encontró el prefab plantilla del elevador en:\n{TemplateElevatorPath}\n\nAsegúrate de que el archivo exista en el proyecto.",
                        "Aceptar"
                    );
                    return;
                }
            }

            // 2. Confirmación previa del usuario
            bool proceed = EditorUtility.DisplayDialog(
                "Copiar Iluminación de Elevador",
                "Esta utilidad tomará la luz y el componente LightFlicker del elevador modelo y los replicará en todos los demás prefabs de 'Sector Cero'.\n\n" +
                "Si un prefab ya contiene una luz, será reemplazada para garantizar la configuración idéntica.\n\n" +
                "¿Deseas continuar?",
                "Sí, Proceder",
                "Cancelar"
            );

            if (!proceed) return;

            // 3. Buscar todos los prefabs en la carpeta destino
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetFolder });
            if (guids == null || guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Aviso", $"No se encontraron prefabs en la carpeta:\n{TargetFolder}", "Aceptar");
                return;
            }

            // 4. Cargar el contenido del prefab plantilla para extraer la estructura de luz
            GameObject templateRoot = PrefabUtility.LoadPrefabContents(templatePath);
            if (templateRoot == null)
            {
                EditorUtility.DisplayDialog("Error", "No se pudo cargar el prefab plantilla en memoria.", "Aceptar");
                return;
            }

            Light templateLight = templateRoot.GetComponentInChildren<Light>(true);
            if (templateLight == null)
            {
                EditorUtility.DisplayDialog("Error", $"El prefab '{templatePath}' no contiene ningún componente Light.", "Aceptar");
                PrefabUtility.UnloadPrefabContents(templateRoot);
                return;
            }

            GameObject templateLightGo = templateLight.gameObject;
            int updatedCount = 0;
            int skippedCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    string fileName = Path.GetFileName(path);

                    EditorUtility.DisplayProgressBar(
                        "Propagando Iluminación en Sector Cero",
                        $"Procesando: {fileName} ({i + 1}/{guids.Length})",
                        (float)i / guids.Length
                    );

                    // Excluir el elevador plantilla
                    if (path.Equals(templatePath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        skippedCount++;
                        continue;
                    }

                    // Excluir muros ciegos y tapones de cierre (no son habitaciones transitables)
                    if (fileName.Contains("Muro_Ciego") || fileName.Contains("Tapon"))
                    {
                        skippedCount++;
                        continue;
                    }

                    // Cargar el prefab destino de forma aislada
                    GameObject targetRoot = PrefabUtility.LoadPrefabContents(path);
                    if (targetRoot == null) continue;

                    try
                    {
                        // Si ya tiene una luz previa, removerla para actualizarla con la nueva plantilla
                        Light existingLight = targetRoot.GetComponentInChildren<Light>(true);
                        if (existingLight != null)
                        {
                            Object.DestroyImmediate(existingLight.gameObject);
                        }

                        // Instanciar clon exacto de la luz del elevador (mantiene Light, LightFlicker y URP Data)
                        GameObject newLightGo = Object.Instantiate(templateLightGo, targetRoot.transform);
                        newLightGo.name = templateLightGo.name;

                        // Garantizar que la luz cuente con el reactor de cordura ambiental
                        if (newLightGo.GetComponent<SanityLightReactor>() == null)
                        {
                            newLightGo.AddComponent<SanityLightReactor>();
                        }

                        // Posicionar en el techo del módulo según sus dimensiones arquitectónicas
                        Vector3 optimalPosition = CalculateOptimalLightPosition(targetRoot, templateLightGo.transform.localPosition);
                        newLightGo.transform.localPosition = optimalPosition;
                        newLightGo.transform.localRotation = templateLightGo.transform.localRotation;
                        newLightGo.transform.localScale = Vector3.one;

                        // Ajustar rango de iluminación si la sala es de gran altura (arenas o reactores)
                        Light newLightComp = newLightGo.GetComponent<Light>();
                        if (newLightComp != null)
                        {
                            float minRequiredRange = optimalPosition.y * 1.6f;
                            if (newLightComp.range < minRequiredRange)
                            {
                                newLightComp.range = Mathf.Max(newLightComp.range, minRequiredRange);
                            }
                        }

                        // Guardar cambios directamente en el asset del prefab
                        PrefabUtility.SaveAsPrefabAsset(targetRoot, path);
                        updatedCount++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[ElevatorLightPropagator] Error procesando '{path}': {ex.Message}");
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(targetRoot);
                    }
                }
            }
            finally
            {
                // Liberar plantilla y finalizar edición por lotes
                PrefabUtility.UnloadPrefabContents(templateRoot);
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            // 5. Informe final al usuario
            Debug.Log($"[ElevatorLightPropagator] Proceso completado: {updatedCount} prefabs actualizados con éxito ({skippedCount} omitidos).");
            EditorUtility.DisplayDialog(
                "Proceso Completado",
                $"Se ha copiado exitosamente la iluminación del elevador.\n\n" +
                $"• Prefabs actualizados: {updatedCount}\n" +
                $"• Prefabs omitidos (plantilla/tapones): {skippedCount}",
                "Aceptar"
            );
        }

        /// <summary>
        /// Calcula la posición óptima centrada en el techo de la sala basándose en ModuleConnector o Renderers.
        /// </summary>
        private static Vector3 CalculateOptimalLightPosition(GameObject root, Vector3 fallbackPos)
        {
            // 1. Prioridad: Bounds de ModuleConnector si está presente
            var connector = root.GetComponent<ModuleConnector>();
            if (connector != null)
            {
                Bounds b = connector.CalculateLocalBounds();
                if (b.size.sqrMagnitude > 1f)
                {
                    return new Vector3(b.center.x, b.max.y - 0.45f, b.center.z);
                }
            }

            // 2. Cálculo secundario mediante Renderers combinados
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds combined = new Bounds();
                bool initialized = false;

                foreach (var r in renderers)
                {
                    if (r is MeshRenderer || r is SkinnedMeshRenderer)
                    {
                        Vector3 localCenter = root.transform.InverseTransformPoint(r.bounds.center);
                        if (!initialized)
                        {
                            combined = new Bounds(localCenter, Vector3.zero);
                            initialized = true;
                        }
                        combined.Encapsulate(root.transform.InverseTransformPoint(r.bounds.min));
                        combined.Encapsulate(root.transform.InverseTransformPoint(r.bounds.max));
                    }
                }

                if (initialized && combined.size.sqrMagnitude > 1f)
                {
                    return new Vector3(combined.center.x, combined.max.y - 0.45f, combined.center.z);
                }
            }

            // 3. Fallback a coordenadas por defecto
            return fallbackPos;
        }
    }
}
#endif

