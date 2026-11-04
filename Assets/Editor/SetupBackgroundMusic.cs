// ============================================================================
// SetupBackgroundMusic.cs — Importador & Configurador de Música de Fondo
// ============================================================================
// Importa automáticamente el track ambiental de Survival Horror a Assets/Audio/Music
// y configura o actualiza el GameObject 'MusicaFondo' con un AudioSource 2D
// en la escena activa (Unity 6).
// ============================================================================

#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace DemenciaNoLimite.EditorTools
{
    public static class SetupBackgroundMusic
    {
        private const string SOURCE_LOCAL_PATH = @"C:\Users\lilie\Downloads\audio\MELANCHOLIA Music Box Sad, creepy song.mp3";
        private const string DEST_FOLDER_PATH = "Assets/Audio/Music";
        private const string DEST_ASSET_PATH = "Assets/Audio/Music/MELANCHOLIA_MusicBox.mp3";
        private const string GAME_OBJECT_NAME = "MusicaFondo";
        private const string MENU_PATH = "Demencia/Audio/Importar e Instalar Música de Fondo";

        [MenuItem(MENU_PATH, false, 60)]
        public static void ImportAndSetupMusic()
        {
            // ════════════════════════════════════════════════════════════════
            // 1. VALIDACIÓN Y COPIA DEL ARCHIVO FUENTE
            // ════════════════════════════════════════════════════════════════
            if (!File.Exists(SOURCE_LOCAL_PATH))
            {
                Debug.LogError($"[SetupBackgroundMusic] No se encontró el archivo de audio en la ruta:\n{SOURCE_LOCAL_PATH}");
                EditorUtility.DisplayDialog(
                    "Error al Importar Música",
                    $"No se encontró el archivo de audio en:\n\n{SOURCE_LOCAL_PATH}\n\nVerifica que el archivo exista en tu carpeta de descargas.",
                    "Aceptar"
                );
                return;
            }

            // Obtener rutas del sistema de archivos dentro del proyecto
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string destSystemFolder = Path.Combine(projectRoot, DEST_FOLDER_PATH);
            string destSystemFilePath = Path.Combine(projectRoot, DEST_ASSET_PATH);

            if (!Directory.Exists(destSystemFolder))
            {
                Directory.CreateDirectory(destSystemFolder);
            }

            try
            {
                File.Copy(SOURCE_LOCAL_PATH, destSystemFilePath, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SetupBackgroundMusic] Error al copiar el archivo de música: {ex.Message}");
                return;
            }

            // ════════════════════════════════════════════════════════════════
            // 2. REFRESH DE ASSETS Y CARGA DE AUDIOCLIP
            // ════════════════════════════════════════════════════════════════
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(DEST_ASSET_PATH, ImportAssetOptions.ForceUpdate);

            AudioClip musicClip = AssetDatabase.LoadAssetAtPath<AudioClip>(DEST_ASSET_PATH);
            if (musicClip == null)
            {
                Debug.LogError($"[SetupBackgroundMusic] No se pudo cargar el AudioClip en '{DEST_ASSET_PATH}'.");
                return;
            }

            // ════════════════════════════════════════════════════════════════
            // 3. CONFIGURACIÓN DEL GAMEOBJECT EN LA ESCENA ACTIVA
            // ════════════════════════════════════════════════════════════════
            GameObject musicGo = GameObject.Find(GAME_OBJECT_NAME);
            bool isNewObject = false;

            if (musicGo == null)
            {
                musicGo = new GameObject(GAME_OBJECT_NAME);
                Undo.RegisterCreatedObjectUndo(musicGo, "Crear MusicaFondo");
                isNewObject = true;
            }

            AudioSource audioSource = musicGo.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = Undo.AddComponent<AudioSource>(musicGo);
            }
            else
            {
                Undo.RecordObject(audioSource, "Configurar AudioSource MusicaFondo");
            }

            // Parámetros estrictos de audio ambiental 2D
            audioSource.clip = musicClip;
            audioSource.playOnAwake = true;
            audioSource.loop = true;
            audioSource.spatialBlend = 0.0f; // Audio 2D puro de fondo
            audioSource.volume = 0.45f;
            audioSource.priority = 0; // Prioridad máxima de reproducción

            // Marcar dirty para persistir los cambios en la escena
            EditorUtility.SetDirty(musicGo);
            EditorUtility.SetDirty(audioSource);
            Scene activeScene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(activeScene);

            // ════════════════════════════════════════════════════════════════
            // 4. LOG DE CONFIRMACIÓN Y RETROALIMENTACIÓN
            // ════════════════════════════════════════════════════════════════
            Selection.activeGameObject = musicGo;
            EditorGUIUtility.PingObject(musicClip);

            Debug.Log($"<color=#55FF55><b>[SetupBackgroundMusic]</b> ¡Música de fondo instalada exitosamente!</color>\n" +
                      $" • Clip importado: <i>{DEST_ASSET_PATH}</i>\n" +
                      $" • GameObject: <i>{GAME_OBJECT_NAME}</i> ({(isNewObject ? "Creado" : "Actualizado")})\n" +
                      $" • Configuración: 2D (SpatialBlend = 0.0), Loop = True, Volume = 0.45, PlayOnAwake = True\n" +
                      $" • Escena marcada como sucia: <i>{activeScene.name}</i>");

            EditorUtility.DisplayDialog(
                "Música de Fondo Configurada",
                $"Se ha importado y configurado la música de fondo con éxito:\n\n" +
                $"• Archivo: {DEST_ASSET_PATH}\n" +
                $"• GameObject: {GAME_OBJECT_NAME}\n" +
                $"• Modo: 2D Estéreo Continuo (Loop, Vol: 0.45)\n\n" +
                $"La escena activa ({activeScene.name}) ha sido marcada para guardar.",
                "Excelente"
            );
        }
    }
}
#endif

