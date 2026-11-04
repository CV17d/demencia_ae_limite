// ============================================================================
// EnemySetupTool.cs — Herramienta de Editor para Enemigos Flotantes (Unity 6)
// ============================================================================
// Añade una opción de menú en Unity (Tools > Configurar Enemigo Flotante):
// 1. Instancia automáticamente un GameObject "EnemigoFlotante" en la escena activa.
// 2. Asigna y configura SpriteRenderer con los sprites de Assets/Art/2D/Enemigos_Normales/
//    (enemigo 001 baset y 001 pegando).
// 3. Añade CapsuleCollider calibrado para el entorno 3D.
// 4. Integra y parametriza el componente FloatingEnemyAI.
// 5. Guarda automáticamente el resultado como un Prefab en "Assets/Prefabs/EnemigoFlotante.prefab".
// ============================================================================

#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor;
using DemenciaNoLimite.Enemies;

namespace DemenciaNoLimite.Editor
{
    public static class EnemySetupTool
    {
        private const string BASE_SPRITE_PATH = "Assets/Art/2D/Enemigos_Normales/enemigo 001 baset.png";
        private const string ATTACK_SPRITE_PATH = "Assets/Art/2D/Enemigos_Normales/001 pegando.png";
        private const string PREFABS_DIR = "Assets/Prefabs";
        private const string PREFAB_FULL_PATH = "Assets/Prefabs/EnemigoFlotante.prefab";

        [MenuItem("Tools/Configurar Enemigo Flotante", false, 100)]
        [MenuItem("Demencia/👾 Configurar Enemigo Flotante", false, 25)]
        public static void CreateFloatingEnemyPrefab()
        {
            // ════════════════════════════════════════════════════════════════
            // 1. CARGA AUTOMÁTICA DE SPRITES
            // ════════════════════════════════════════════════════════════════
            Sprite baseSprite = LoadSpriteAsset(BASE_SPRITE_PATH);
            Sprite attackSprite = LoadSpriteAsset(ATTACK_SPRITE_PATH);

            if (baseSprite == null)
            {
                Debug.LogWarning($"[EnemySetupTool] No se encontró el sprite base en '{BASE_SPRITE_PATH}'. Verifica la ruta.");
            }
            if (attackSprite == null)
            {
                Debug.LogWarning($"[EnemySetupTool] No se encontró el sprite de ataque en '{ATTACK_SPRITE_PATH}'. Verifica la ruta.");
            }

            // ════════════════════════════════════════════════════════════════
            // 2. CREACIÓN DEL GAMEOBJECT EN LA ESCENA
            // ════════════════════════════════════════════════════════════════
            Vector3 spawnPosition = Vector3.zero + Vector3.up * 1.5f;
            if (SceneView.lastActiveSceneView != null)
            {
                spawnPosition = SceneView.lastActiveSceneView.pivot;
            }

            GameObject enemyGo = new GameObject("EnemigoFlotante");
            enemyGo.transform.position = spawnPosition;
            // Escala compacta calibrada (1.2x) para cruzar marcos y puertas 4x4 con total holgura
            enemyGo.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            Undo.RegisterCreatedObjectUndo(enemyGo, "Crear Enemigo Flotante");

            // Capa 'Enemy' si existe en la configuración del proyecto
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer != -1)
            {
                enemyGo.layer = enemyLayer;
            }

            // ════════════════════════════════════════════════════════════════
            // 3. SPRITERENDERER Y SOMBRAS EN ENTORNO 3D
            // ════════════════════════════════════════════════════════════════
            SpriteRenderer spriteRenderer = enemyGo.AddComponent<SpriteRenderer>();
            if (baseSprite != null)
            {
                spriteRenderer.sprite = baseSprite;
            }
            spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            spriteRenderer.receiveShadows = true;

            // ════════════════════════════════════════════════════════════════
            // 4. CAPSULE COLLIDER (SINCRONIZADO CON LA ESCALA 1.2x PARA IMPACTOS EXACTOS)
            // ════════════════════════════════════════════════════════════════
            CapsuleCollider capsule = enemyGo.AddComponent<CapsuleCollider>();
            capsule.radius = 0.35f; // ~0.42m de radio en mundo (escala 1.2)
            capsule.height = 1.6f;  // ~1.92m de altura en mundo (muy por debajo de las 4 unidades de las puertas)
            capsule.center = new Vector3(0f, 0.8f, 0f);
            capsule.direction = 1; // Eje Y

            // ════════════════════════════════════════════════════════════════
            // 5. AUDIOSOURCE 3D
            // ════════════════════════════════════════════════════════════════
            AudioSource audio = enemyGo.AddComponent<AudioSource>();
            audio.spatialBlend = 1.0f; // Audio espacial 3D
            audio.playOnAwake = false;
            audio.rolloffMode = AudioRolloffMode.Logarithmic;
            audio.minDistance = 1.5f;
            audio.maxDistance = 22f;

            // ════════════════════════════════════════════════════════════════
            // 6. FLOATING ENEMY AI Y SERIALIZACIÓN DE CAMPOS (ATAQUE RÁPIDO 0.18s)
            // ════════════════════════════════════════════════════════════════
            FloatingEnemyAI floatingAI = enemyGo.AddComponent<FloatingEnemyAI>();

            SerializedObject serializedAI = new SerializedObject(floatingAI);
            var baseSpriteProp = serializedAI.FindProperty("_baseSprite");
            var attackSpriteProp = serializedAI.FindProperty("_attackSprite");
            var rendererProp = serializedAI.FindProperty("_spriteRenderer");
            var visualTransProp = serializedAI.FindProperty("_visualTransform");
            var attackDurationProp = serializedAI.FindProperty("_attackSpriteDuration");

            if (baseSpriteProp != null) baseSpriteProp.objectReferenceValue = baseSprite;
            if (attackSpriteProp != null) attackSpriteProp.objectReferenceValue = attackSprite;
            if (rendererProp != null) rendererProp.objectReferenceValue = spriteRenderer;
            if (visualTransProp != null) visualTransProp.objectReferenceValue = enemyGo.transform;
            if (attackDurationProp != null) attackDurationProp.floatValue = 0.18f;

            serializedAI.ApplyModifiedPropertiesWithoutUndo();

            // ════════════════════════════════════════════════════════════════
            // 7. GUARDADO AUTOMÁTICO COMO PREFAB
            // ════════════════════════════════════════════════════════════════
            if (!AssetDatabase.IsValidFolder(PREFABS_DIR))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(
                enemyGo,
                PREFAB_FULL_PATH,
                InteractionMode.UserAction
            );

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ════════════════════════════════════════════════════════════════
            // 8. FEEDBACK VISUAL Y SELECCIÓN EN EL EDITOR
            // ════════════════════════════════════════════════════════════════
            Selection.activeGameObject = enemyGo;
            if (prefabAsset != null)
            {
                EditorGUIUtility.PingObject(prefabAsset);
            }

            Debug.Log($"<color=#55FF55><b>[EnemySetupTool]</b> ¡Enemigo Flotante configurado con éxito!</color>\n" +
                      $"• GameObject en escena: <i>{enemyGo.name}</i>\n" +
                      $"• Prefab generado y conectado en: <i>{PREFAB_FULL_PATH}</i>");

            EditorUtility.DisplayDialog(
                "Configuración Exitosa",
                $"Se ha creado 'EnemigoFlotante' en la escena y se ha guardado automáticamente como Prefab en:\n\n{PREFAB_FULL_PATH}\n\n" +
                $"Sprites asignados:\n• Base: {(baseSprite != null ? baseSprite.name : "Nulo")}\n• Ataque: {(attackSprite != null ? attackSprite.name : "Nulo")}",
                "Aceptar"
            );
        }

        /// <summary>
        /// Carga un Sprite desde la ruta del proyecto. Soporta tanto modo Single Sprite como Multiple/Sliced.
        /// </summary>
        private static Sprite LoadSpriteAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;

            // 1. Intento de carga directa como Sprite único
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null) return sprite;

            // 2. Si está configurado en modo Multiple / Sliced, buscar entre todos los sub-activos del archivo
            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (allAssets != null)
            {
                foreach (var obj in allAssets)
                {
                    if (obj is Sprite s)
                    {
                        return s;
                    }
                }
            }

            return null;
        }
    }
}
#endif

