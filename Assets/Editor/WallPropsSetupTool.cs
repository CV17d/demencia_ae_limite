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
    /// Herramienta de Technical Artist para organizar y configurar objetos decorativos de pared (Props_Pared).
    /// Acciones:
    /// 1. Crear el directorio Assets/Prefabs/Entornos/Props_Pared/.
    /// 2. Clasificar y mover prefabs de pared (perchero, grameras, estante, brazos robóticos, ganchos).
    /// 3. Garantizar BoxCollider sólido (isTrigger = false) para impedir el paso del jugador.
    /// 4. Guardar cambios con PrefabUtility.
    /// </summary>
    public static class WallPropsSetupTool
    {
        public const string PROPS_SRC_FOLDER = "Assets/Prefabs/Entornos/Props";
        public const string WALL_PROPS_FOLDER = "Assets/Prefabs/Entornos/Props_Pared";

        private static readonly string[] WALL_PROP_KEYWORDS = new string[]
        {
            "perchero",
            "grameras",
            "basculas",
            "estante",
            "brazos",
            "roboticos",
            "ganchos",
            "carnicero"
        };

        // ════════════════════════════════════════════════════════════════════
        // ENTRADA DE MENÚ EN UNITY EDITOR
        // ════════════════════════════════════════════════════════════════════

        [MenuItem("Demencia/🧱 Organizar Props de Pared y Colisiones Sólidas", false, 3)]
        [MenuItem("Tools/Demencia/Organizar Props de Pared y Colisiones Sólidas", false, 3)]
        public static void EjecutarMenuManual()
        {
            EjecutarPipelinePropsPared(true);
        }

        /// <summary>
        /// Ejecuta la creación de directorio, migración y configuración de colisiones sólidas.
        /// </summary>
        public static void EjecutarPipelinePropsPared(bool mostrarDialogo = true)
        {
            // 1. CREAR DIRECTORIO DE PARED
            AsegurarDirectorio(WALL_PROPS_FOLDER);

            // 2. CLASIFICAR Y MOVER PREFABS
            int prefabsMovidos = MoverPrefabsDePared();

            // 3. AJUSTE DE COLISIONES SÓLIDAS (BoxCollider + isTrigger = false)
            int collidersConfigurados = ConfigurarColisionesSolidas();

            // 4. REGISTRO Y REPORTE
            string resumen = $"<b>[WallPropsSetupTool] ✔ Organización de Props de Pared Finalizada:</b>\n" +
                             $"• Carpeta de destino: {WALL_PROPS_FOLDER}\n" +
                             $"• Prefabs reubicados en esta ejecución: {prefabsMovidos}\n" +
                             $"• Prefabs verificados con colisiones sólidas (isTrigger = FALSE): {collidersConfigurados}";

            Debug.Log($"<color=#4ADE80>{resumen}</color>");

            if (mostrarDialogo)
            {
                EditorUtility.DisplayDialog("Props de Pared Configurados",
                    $"Proceso completado con éxito:\n\n" +
                    $"• Carpeta: {WALL_PROPS_FOLDER}\n" +
                    $"• Prefabs movidos: {prefabsMovidos}\n" +
                    $"• Prefabs verificados: {collidersConfigurados}\n" +
                    $"• Físicas: BoxCollider con isTrigger = FALSE (Sólidos e impenetrables).\n\n" +
                    "Revisa la consola para más información.",
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

        private static int MoverPrefabsDePared()
        {
            int movidos = 0;
            if (!Directory.Exists(PROPS_SRC_FOLDER)) return 0;

            string[] propGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PROPS_SRC_FOLDER });
            foreach (string guid in propGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                string lower = fileName.ToLowerInvariant();

                if (WALL_PROP_KEYWORDS.Any(kw => lower.Contains(kw)))
                {
                    string targetPath = $"{WALL_PROPS_FOLDER}/{Path.GetFileName(assetPath)}";
                    string error = AssetDatabase.MoveAsset(assetPath, targetPath);

                    if (string.IsNullOrEmpty(error))
                    {
                        movidos++;
                    }
                    else
                    {
                        // Fallback seguro a nivel de sistema de archivos
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
        // 3. AJUSTE DE COLISIONES SÓLIDAS (BoxCollider + isTrigger = false)
        // ════════════════════════════════════════════════════════════════════

        private static int ConfigurarColisionesSolidas()
        {
            int ajustados = 0;
            string[] wallPropGuids = AssetDatabase.FindAssets("t:Prefab", new[] { WALL_PROPS_FOLDER });

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in wallPropGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    if (root == null) continue;

                    bool modificado = false;

                    // 1. BoxCollider en la raíz del prefab
                    BoxCollider boxCol = root.GetComponent<BoxCollider>();
                    if (boxCol == null)
                    {
                        boxCol = root.AddComponent<BoxCollider>();
                        modificado = true;
                    }

                    if (boxCol.isTrigger)
                    {
                        boxCol.isTrigger = false;
                        modificado = true;
                    }

                    // 2. Garantizar que todos los colliders descendientes sean sólidos (isTrigger = false)
                    Collider[] allColliders = root.GetComponentsInChildren<Collider>(true);
                    foreach (var c in allColliders)
                    {
                        if (c.isTrigger)
                        {
                            c.isTrigger = false;
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
    }
}
#endif

