#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DemenciaNoLimite.Editor
{
    /// <summary>
    /// Herramienta de Technical Artist para verificar, organizar y configurar
    /// los objetos recolectables (Loot) del juego, asegurando que todos posean
    /// BoxCollider con isTrigger = true para evitar bugs físicos al spawnear.
    /// </summary>
    public static class LootSetupTool
    {
        public const string LOOT_FOLDER = "Assets/Prefabs/Loot";

        [MenuItem("Demencia/💊 Configurar y Verificar Loot (Triggers)", false, 5)]
        [MenuItem("Tools/Demencia/Configurar y Verificar Loot", false, 5)]
        public static void EjecutarMenuManual()
        {
            VerificarYConfigurarLoot(true);
        }

        public static int VerificarYConfigurarLoot(bool mostrarDialogo = true)
        {
            if (!Directory.Exists(LOOT_FOLDER))
            {
                Directory.CreateDirectory(LOOT_FOLDER);
                AssetDatabase.Refresh();
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { LOOT_FOLDER });
            int procesados = 0;
            int ajustados = 0;
            List<string> reporte = new List<string>();

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    if (root == null) continue;

                    bool modificado = false;

                    // 1. Obtener o añadir BoxCollider
                    BoxCollider boxCol = root.GetComponent<BoxCollider>();
                    if (boxCol == null)
                    {
                        boxCol = root.AddComponent<BoxCollider>();
                        modificado = true;
                    }

                    // 2. CRÍTICO: Asegurar isTrigger = true
                    if (!boxCol.isTrigger)
                    {
                        boxCol.isTrigger = true;
                        modificado = true;
                    }

                    // 3. Asegurar que cualquier otro Collider hijo también sea Trigger si existe
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
                        ajustados++;
                    }

                    reporte.Add($"• {root.name}: BoxCollider (isTrigger = true)");
                    PrefabUtility.UnloadPrefabContents(root);
                    procesados++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string mensajeConsola = $"<b>[LootSetupTool]</b> Verificación de Loot completada ({procesados} prefabs en '{LOOT_FOLDER}'):\n" +
                                   string.Join("\n", reporte);
            Debug.Log($"<color=#4ADE80>{mensajeConsola}</color>");

            if (mostrarDialogo)
            {
                EditorUtility.DisplayDialog("Configuración de Loot",
                    $"Proceso finalizado con éxito:\n\n" +
                    $"• Total Prefabs en Loot: {procesados}\n" +
                    $"• Prefabs ajustados: {ajustados}\n" +
                    $"• Colisionadores: BoxCollider con isTrigger = TRUE garantizado.\n\n" +
                    "Los objetos son atravesables y seguros para el generador.",
                    "Entendido");
            }

            return procesados;
        }
    }
}
#endif

