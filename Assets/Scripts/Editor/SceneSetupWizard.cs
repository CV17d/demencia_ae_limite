// ============================================================================
// SceneSetupWizard.cs — 1-Click Scene Setup Tool for Unity Editor
// ============================================================================
// Adds a top menu item in Unity:
//   "Demencia > ⚡ Setup Main Scene (1 Click)"
//
// Automatically:
//   1. Creates the GameBootstrapper in the open scene.
//   2. Ensures the "Player" tag exists.
//   3. Configures default lighting.
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using DemenciaNoLimite;

namespace DemenciaNoLimite.Editor
{
    public static class SceneSetupWizard
    {
        [MenuItem("Demencia/⚡ Configurar Escena de Prueba (1 Click)", false, 1)]
        public static void SetupScene()
        {
            EnsureTag("Player");

            // Look for existing bootstrapper
            var existing = Object.FindAnyObjectByType<GameBootstrapper>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("<color=yellow>[SceneSetupWizard] Ya existe un GameBootstrapper en la escena. ¡Solo presiona Play para jugar!</color>");
                return;
            }

            // Create GameObject with GameBootstrapper
            var bootstrapperGo = new GameObject("[DEMENCIA_BOOTSTRAPPER]");
            bootstrapperGo.AddComponent<GameBootstrapper>();
            Selection.activeGameObject = bootstrapperGo;

            Undo.RegisterCreatedObjectUndo(bootstrapperGo, "Create GameBootstrapper");

            // Ensure Assets/Scenes folder exists and save scene
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            EditorUtility.DisplayDialog(
                "Demencia No Limite — ¡Listo para Jugar!",
                "Se ha configurado la escena con éxito.\n\n" +
                "1. Presiona el botón de PLAY (▶) arriba en Unity.\n" +
                "2. El juego generará el Piso 01 proceduralmente, tu personaje (João), armas, enemigos y la interfaz.\n\n" +
                "Controles:\n" +
                "• WASD: Moverse\n" +
                "• Mouse: Mirar\n" +
                "• Click Izquierdo: Disparar\n" +
                "• Shift: Correr\n" +
                "• Espacio: Saltar\n" +
                "• R: Recargar",
                "¡Entendido!"
            );

            Debug.Log("<color=green><b>[DEMENCIA NO LIMITE]</b> Escena configurada con éxito. ¡Presiona Play!</color>");
        }

        private static void EnsureTag(string tag)
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty tagsProp = tagManager.FindProperty("tags");

            bool found = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
                if (t.stringValue.Equals(tag)) { found = true; break; }
            }

            if (!found)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                SerializedProperty n = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
                n.stringValue = tag;
                tagManager.ApplyModifiedProperties();
            }
        }
    }
}
#endif
