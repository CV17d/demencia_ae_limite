// ============================================================================
// SetupEnemyAttackSound.cs — Importador & Configurador de Sonido de Ataque
// ============================================================================
// Importa automáticamente el rugido de criatura/monstruo a Assets/Audio/Enemies/
// y lo parametriza como sonido de ataque 3D en los prefabs de enemigos y jefes (Unity 6).
// ============================================================================

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using DemenciaNoLimite.Enemies;
using DemenciaNoLimite.Boss;

namespace DemenciaNoLimite.EditorTools
{
    public static class SetupEnemyAttackSound
    {
        private const string SOURCE_FOLDER = @"C:\Users\lilie\Downloads\audio";
        private const string DEST_FOLDER_PATH = "Assets/Audio/Enemies";
        private const string DEST_ASSET_PATH = "Assets/Audio/Enemies/RugidoAtaqueEnemigo.mp3";
        private const string MENU_PATH = "Demencia/Audio/Importar e Instalar Rugido de Enemigo";

        // Parámetros de audio 3D para el ataque de enemigos
        private const bool PLAY_ON_AWAKE = false;
        private const bool LOOP = false;
        private const float SPATIAL_BLEND = 1.0f; // 100% 3D posicional
        private const float MIN_DISTANCE = 1.5f;
        private const float MAX_DISTANCE = 20.0f;
        private const AudioRolloffMode ROLLOFF_MODE = AudioRolloffMode.Logarithmic;

        [MenuItem(MENU_PATH, false, 55)]
        public static void ImportAndSetupAttackSound()
        {
            // ════════════════════════════════════════════════════════════════
            // 1. LOCALIZACIÓN Y COPIA DEL ARCHIVO DE AUDIO
            // ════════════════════════════════════════════════════════════════
            string sourcePath = FindSourceAudioFile();
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogError($"[SetupEnemyAttackSound] No se encontró ningún archivo con 'RUGIDO CRIATURA' en '{SOURCE_FOLDER}'.");
                EditorUtility.DisplayDialog(
                    "Error al Importar Rugido",
                    $"No se encontró el archivo de rugido en:\n\n{SOURCE_FOLDER}\n\nVerifica que el archivo comience por 'RUGIDO CRIATURA TERROR'.",
                    "Aceptar"
                );
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string destFolderSystem = Path.Combine(projectRoot, DEST_FOLDER_PATH);
            string destFileSystem = Path.Combine(projectRoot, DEST_ASSET_PATH);

            if (!Directory.Exists(destFolderSystem))
            {
                Directory.CreateDirectory(destFolderSystem);
            }

            try
            {
                File.Copy(sourcePath, destFileSystem, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SetupEnemyAttackSound] Error al copiar el archivo de audio: {ex.Message}");
                return;
            }

            // ════════════════════════════════════════════════════════════════
            // 2. IMPORTACIÓN Y CARGA COMO AUDIOCLIP EN ASSETDATABASE
            // ════════════════════════════════════════════════════════════════
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(DEST_ASSET_PATH, ImportAssetOptions.ForceUpdate);

            AudioClip roarClip = AssetDatabase.LoadAssetAtPath<AudioClip>(DEST_ASSET_PATH);
            if (roarClip == null)
            {
                Debug.LogError($"[SetupEnemyAttackSound] No se pudo cargar el AudioClip en '{DEST_ASSET_PATH}'.");
                return;
            }

            // ════════════════════════════════════════════════════════════════
            // 3. IDENTIFICACIÓN DE PREFABS DE ENEMIGOS Y JEFES
            // ════════════════════════════════════════════════════════════════
            string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
            List<string> targetEnemyPrefabPaths = new List<string>();

            foreach (string guid in allPrefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                string normalizedPath = path.Replace('\\', '/');
                if (IsExcludedEnvironmentPath(normalizedPath)) continue;

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabAsset == null) continue;

                if (IsTargetEnemyPrefab(prefabAsset, normalizedPath))
                {
                    targetEnemyPrefabPaths.Add(path);
                }
            }

            if (targetEnemyPrefabPaths.Count == 0)
            {
                Debug.LogWarning("[SetupEnemyAttackSound] No se encontraron prefabs de enemigos aplicables en 'Assets/Prefabs/'.");
                return;
            }

            // ════════════════════════════════════════════════════════════════
            // 4. CONFIGURACIÓN DEL AUDIOSOURCE Y ASIGNACIÓN DE ATTACKCLIP
            // ════════════════════════════════════════════════════════════════
            int updatedCount = 0;
            List<string> updatedNames = new List<string>();

            try
            {
                for (int i = 0; i < targetEnemyPrefabPaths.Count; i++)
                {
                    string path = targetEnemyPrefabPaths[i];
                    string filename = Path.GetFileNameWithoutExtension(path);

                    EditorUtility.DisplayProgressBar(
                        "Asignando Rugido a Enemigos",
                        $"Configurando: {filename} ({i + 1}/{targetEnemyPrefabPaths.Count})",
                        (float)i / targetEnemyPrefabPaths.Count
                    );

                    GameObject contents = PrefabUtility.LoadPrefabContents(path);
                    if (contents == null) continue;

                    try
                    {
                        // 1. Añadir o recuperar AudioSource
                        AudioSource audioSource = contents.GetComponent<AudioSource>();
                        if (audioSource == null)
                        {
                            audioSource = contents.AddComponent<AudioSource>();
                        }

                        // Parámetros de audio 3D posicional para combate
                        audioSource.playOnAwake = PLAY_ON_AWAKE;
                        audioSource.loop = LOOP;
                        audioSource.spatialBlend = SPATIAL_BLEND;
                        audioSource.minDistance = MIN_DISTANCE;
                        audioSource.maxDistance = MAX_DISTANCE;
                        audioSource.rolloffMode = ROLLOFF_MODE;
                        audioSource.clip = roarClip;

                        // 2. Asignación directa en FloatingEnemyAI
                        FloatingEnemyAI floatingAI = contents.GetComponent<FloatingEnemyAI>();
                        if (floatingAI != null)
                        {
                            SerializedObject so = new SerializedObject(floatingAI);
                            so.Update();

                            var clipProp = so.FindProperty("attackClip") ?? so.FindProperty("_attackSound");
                            if (clipProp != null)
                            {
                                clipProp.objectReferenceValue = roarClip;
                            }

                            var srcProp = so.FindProperty("audioSource");
                            if (srcProp != null)
                            {
                                srcProp.objectReferenceValue = audioSource;
                            }

                            so.ApplyModifiedProperties();
                        }

                        // 3. Asignación en cualquier otro componente de combate del prefab
                        MonoBehaviour[] components = contents.GetComponentsInChildren<MonoBehaviour>(true);
                        foreach (var mb in components)
                        {
                            if (mb == null || mb == floatingAI) continue;
                            SerializedObject so = new SerializedObject(mb);
                            so.Update();
                            bool changed = false;

                            string[] candidateFields = { "attackClip", "_attackSound", "_attackClip", "roarClip", "_roarClip", "attackSound" };
                            foreach (var fieldName in candidateFields)
                            {
                                var prop = so.FindProperty(fieldName);
                                if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
                                {
                                    prop.objectReferenceValue = roarClip;
                                    changed = true;
                                }
                            }

                            if (changed) so.ApplyModifiedProperties();
                        }

                        // Guardar prefab
                        PrefabUtility.SavePrefabAsset(contents);
                        updatedCount++;
                        updatedNames.Add(filename);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // ════════════════════════════════════════════════════════════════
            // 5. REPORTE EN CONSOLA Y DIÁLOGO
            // ════════════════════════════════════════════════════════════════
            string summaryList = string.Join("\n - ", updatedNames);
            Debug.Log($"<color=#55FF55><b>[SetupEnemyAttackSound]</b> ¡Sonido de ataque asignado exitosamente a {updatedCount} prefabs!</color>\n" +
                      $" • Clip importado: <i>{DEST_ASSET_PATH}</i>\n" +
                      $" • Spatial Blend: 1.0 (3D)\n" +
                      $" • Rango: {MIN_DISTANCE}m a {MAX_DISTANCE}m (Logarithmic)\n" +
                      $" • Prefabs actualizados:\n - {summaryList}");

            EditorUtility.DisplayDialog(
                "Sonido de Ataque Configurado",
                $"Se importó 'RugidoAtaqueEnemigo.mp3' y se asignó en {updatedCount} prefabs de enemigos:\n\n - {summaryList}\n\nLos cambios fueron guardados en el disco.",
                "Excelente"
            );
        }

        /// <summary>
        /// Busca el archivo de audio del rugido en la carpeta de descargas.
        /// </summary>
        private static string FindSourceAudioFile()
        {
            if (!Directory.Exists(SOURCE_FOLDER)) return null;

            string[] files = Directory.GetFiles(SOURCE_FOLDER, "*.*");
            foreach (var file in files)
            {
                string name = Path.GetFileName(file).ToUpperInvariant();
                if (name.Contains("RUGIDO CRIATURA") || name.Contains("RUGIDO") || name.Contains("SONIDO MONSTRUO"))
                {
                    return file;
                }
            }

            return null;
        }

        /// <summary>
        /// Determina si un prefab corresponde a una entidad de combate enemiga o jefe.
        /// </summary>
        private static bool IsTargetEnemyPrefab(GameObject prefab, string path)
        {
            string lowerName = prefab.name.ToLowerInvariant();

            if (prefab.GetComponentInChildren<FloatingEnemyAI>(true) != null) return true;
            if (prefab.GetComponentInChildren<EnemyBase>(true) != null) return true;
            if (prefab.GetComponentInChildren<BossController>(true) != null) return true;

            if (lowerName.Contains("enemigoflotante") || lowerName.Contains("boss_") || lowerName.Contains("jefe"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Excluye carpetas de props, muros, entornos modulares y armas.
        /// </summary>
        private static bool IsExcludedEnvironmentPath(string path)
        {
            string lower = path.ToLowerInvariant();
            return lower.Contains("/props/") ||
                   lower.Contains("/props_pared/") ||
                   lower.Contains("/sectorcero/") ||
                   lower.Contains("/armas_prefab/") ||
                   lower.Contains("/loot/") ||
                   lower.Contains("/ui/");
        }
    }
}
#endif

