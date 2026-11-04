// ============================================================================
// SetupEnemyAudioSource.cs — Configuración Automática de Audio 3D para Enemigos
// ============================================================================
// Localiza automáticamente todos los prefabs de enemigos en el proyecto
// e inyecta/configura el componente AudioSource con parámetros estrictos
// de audio espacial 3D optimizados para Survival Horror (Unity 6).
// ============================================================================

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using DemenciaNoLimite.Enemies;
using DemenciaNoLimite.Enemies.FSM;

namespace DemenciaNoLimite.EditorTools
{
    public static class SetupEnemyAudioSource
    {
        private const string MENU_PATH = "Demencia/Audio/Configurar AudioSource en Enemigos";

        // Parámetros de audio espacial Survival Horror
        private const bool PLAY_ON_AWAKE = false;
        private const bool LOOP = false;
        private const float SPATIAL_BLEND = 1.0f; // 100% 3D
        private const AudioRolloffMode ROLLOFF_MODE = AudioRolloffMode.Logarithmic;
        private const float MIN_DISTANCE = 1.0f;
        private const float MAX_DISTANCE = 18.0f;
        private const float DOPPLER_LEVEL = 0.0f;

        [MenuItem(MENU_PATH, false, 50)]
        public static void ConfigureEnemyAudioSources()
        {
            string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            List<string> enemyPrefabPaths = new List<string>();

            // ════════════════════════════════════════════════════════════════
            // 1. IDENTIFICACIÓN Y FILTRADO RIGUROSO DE PREFABS DE ENEMIGOS
            // ════════════════════════════════════════════════════════════════
            foreach (string guid in allPrefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                // Descartar carpetas que no corresponden a enemigos
                string normalizedPath = path.Replace('\\', '/');
                if (IsExcludedEnvironmentPath(normalizedPath)) continue;

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabAsset == null) continue;

                if (IsEnemyPrefab(prefabAsset, normalizedPath))
                {
                    enemyPrefabPaths.Add(path);
                }
            }

            if (enemyPrefabPaths.Count == 0)
            {
                Debug.LogWarning("[SetupEnemyAudioSource] No se encontraron prefabs de enemigos para configurar en el proyecto.");
                EditorUtility.DisplayDialog(
                    "Configuración de AudioSource",
                    "No se identificaron prefabs de enemigos en el proyecto.",
                    "Entendido"
                );
                return;
            }

            // ════════════════════════════════════════════════════════════════
            // 2. CONFIGURACIÓN DEL AUDIOSOURCE 3D Y GUARDADO DE PREFABS
            // ════════════════════════════════════════════════════════════════
            int updatedCount = 0;
            List<string> updatedNames = new List<string>();

            try
            {
                for (int i = 0; i < enemyPrefabPaths.Count; i++)
                {
                    string path = enemyPrefabPaths[i];
                    string filename = Path.GetFileNameWithoutExtension(path);

                    EditorUtility.DisplayProgressBar(
                        "Configurando AudioSource en Enemigos",
                        $"Procesando: {filename} ({i + 1}/{enemyPrefabPaths.Count})",
                        (float)i / enemyPrefabPaths.Count
                    );

                    GameObject contents = PrefabUtility.LoadPrefabContents(path);
                    if (contents == null) continue;

                    try
                    {
                        AudioSource audioSource = contents.GetComponent<AudioSource>();
                        if (audioSource == null)
                        {
                            audioSource = contents.AddComponent<AudioSource>();
                        }

                        // Parámetros estrictos de Survival Horror
                        audioSource.playOnAwake = PLAY_ON_AWAKE;
                        audioSource.loop = LOOP;
                        audioSource.spatialBlend = SPATIAL_BLEND;
                        audioSource.rolloffMode = ROLLOFF_MODE;
                        audioSource.minDistance = MIN_DISTANCE;
                        audioSource.maxDistance = MAX_DISTANCE;
                        audioSource.dopplerLevel = DOPPLER_LEVEL;

                        // Guardar cambios en el prefab original
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
            // 3. LOG DE REPORTE Y CONFIRMACIÓN
            // ════════════════════════════════════════════════════════════════
            string summaryList = string.Join("\n - ", updatedNames);
            Debug.Log($"<color=#55FF55><b>[SetupEnemyAudioSource]</b> ¡Operación exitosa! Se configuraron {updatedCount} prefabs de enemigos con AudioSource 3D espacial.</color>\n" +
                      $"Parámetros aplicados:\n" +
                      $" • Spatial Blend: 1.0 (3D Puro)\n" +
                      $" • Rolloff: Logarithmic (Min: {MIN_DISTANCE}m, Max: {MAX_DISTANCE}m)\n" +
                      $" • Doppler Level: 0.0 (Sin distorsión)\n" +
                      $" • PlayOnAwake: False, Loop: False\n" +
                      $"Prefabs actualizados:\n - {summaryList}");

            EditorUtility.DisplayDialog(
                "AudioSource 3D Configurado",
                $"Se han actualizado con éxito {updatedCount} prefabs de enemigos con audio espacial 3D:\n\n - {summaryList}\n\nLos cambios fueron guardados en el disco.",
                "Excelente"
            );
        }

        /// <summary>
        /// Determina si un prefab representa una entidad enemiga basándose en componentes, tags o nombre.
        /// </summary>
        private static bool IsEnemyPrefab(GameObject prefab, string path)
        {
            string name = prefab.name.ToLowerInvariant();

            // 1. Verificación por componentes conocidos de enemigos
            if (prefab.GetComponentInChildren<EnemyBase>(true) != null) return true;
            if (prefab.GetComponentInChildren<FloatingEnemyAI>(true) != null) return true;
            if (prefab.GetComponentInChildren<EnemyBrain>(true) != null) return true;
            if (prefab.GetComponentInChildren<PhantomEnemy>(true) != null) return true;

            // 2. Verificación por cualquier MonoBehaviour con nombre relacionado a enemigos
            MonoBehaviour[] scripts = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var script in scripts)
            {
                if (script == null) continue;
                string scriptTypeName = script.GetType().Name.ToLowerInvariant();
                if (scriptTypeName.Contains("enemy") ||
                    scriptTypeName.Contains("monstruo") ||
                    scriptTypeName.Contains("boss") ||
                    scriptTypeName.Contains("jefe"))
                {
                    return true;
                }
            }

            // 3. Verificación por Tag de Unity
            if (prefab.CompareTag("Enemy")) return true;

            // 4. Verificación por nombre de archivo / GameObject (excluyendo entornos)
            if (name.Contains("enemy") ||
                name.Contains("enemigo") ||
                name.Contains("monstruo") ||
                name.Contains("boss"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Excluye carpetas de props, muros, entornos modulares y armas que puedan contener palabras clave similares.
        /// </summary>
        private static bool IsExcludedEnvironmentPath(string path)
        {
            string lower = path.ToLowerInvariant();
            return lower.Contains("/props/") ||
                   lower.Contains("/props_pared/") ||
                   lower.Contains("/sectorcero/") ||
                   lower.Contains("/armas_prefab/") ||
                   lower.Contains("/loot/") ||
                   lower.Contains("/ui/") ||
                   lower.Contains("/materials/") ||
                   lower.Contains("/textures/");
        }
    }
}
#endif

