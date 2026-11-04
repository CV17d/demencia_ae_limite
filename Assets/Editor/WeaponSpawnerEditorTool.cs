// ============================================================================
// WeaponSpawnerEditorTool.cs — Herramienta de Editor para WeaponSpawner (Unity 6)
// ============================================================================
// Añade comandos de menú en Unity Editor para:
// 1. Configurar y auto-poblar el WeaponSpawner en la escena activa con el catálogo
//    válido (bingala, revolver, trabeca, canoduplo) excluyendo de raíz la "muak".
// 2. Ejecutar la generación aleatoria de armas en tiempo de diseño o PlayMode.
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using DemenciaNoLimite.Level;

namespace DemenciaNoLimite.Editor
{
    public static class WeaponSpawnerEditorTool
    {
        [MenuItem("Tools/Configurar Generador de Armas (Weapon Spawner)", false, 110)]
        [MenuItem("Demencia/🔫 Configurar Generador de Armas", false, 30)]
        public static void ConfigurarWeaponSpawnerEnEscena()
        {
            var spawner = Object.FindAnyObjectByType<WeaponSpawner>();
            if (spawner == null)
            {
                var coreManagers = GameObject.Find("=== CORE_MANAGERS ===");
                GameObject targetGo;
                if (coreManagers != null)
                {
                    targetGo = coreManagers;
                }
                else
                {
                    targetGo = new GameObject("=== WEAPON_SPAWNER ===");
                    Undo.RegisterCreatedObjectUndo(targetGo, "Crear Weapon Spawner");
                }

                spawner = targetGo.AddComponent<WeaponSpawner>();
                Undo.RegisterCreatedObjectUndo(spawner, "Añadir Componente WeaponSpawner");
            }

            // Auto-poblar el catálogo activo
            spawner.AutoPoblarCatalogoDesdeProyecto();

            Selection.activeGameObject = spawner.gameObject;
            EditorGUIUtility.PingObject(spawner.gameObject);

            Debug.Log("<color=#38BDF8><b>[WeaponSpawnerEditor]</b> WeaponSpawner configurado con éxito en la escena con el catálogo activo de armas (bingala, revolver, trabeca, canoduplo — sin 'muak').</color>");

            EditorUtility.DisplayDialog(
                "Weapon Spawner Configurado",
                "El componente WeaponSpawner ha sido configurado y seleccionado en la escena.\n\n" +
                "Catálogo de armas activo sincronizado:\n" +
                "• Bingala (Básica)\n" +
                "• Demencia Revólver (Especial)\n" +
                "• Trabeca (Escopeta)\n" +
                "• Canoduplo (Doble Cañón)\n\n" +
                "Nota: El arma 'muak' ha sido estrictamente excluida.",
                "Aceptar"
            );
        }

        [MenuItem("Tools/Generar Armas en Mapa Ahora", false, 111)]
        [MenuItem("Demencia/🔫 Generar Armas en Mapa Ahora", false, 31)]
        public static void GenerarArmasEnMapaMenu()
        {
            var spawner = Object.FindAnyObjectByType<WeaponSpawner>();
            if (spawner == null)
            {
                ConfigurarWeaponSpawnerEnEscena();
                spawner = Object.FindAnyObjectByType<WeaponSpawner>();
            }

            if (spawner != null)
            {
                spawner.LimpiarArmasInstanciadas();
                spawner.SpawnWeaponsForCurrentFloor();

                Debug.Log($"<color=#55FF55><b>[WeaponSpawnerEditor]</b> Generación manual ejecutada: {spawner.SpawnedWeaponsCount} arma(s) creadas en el mapa.</color>");
            }
        }
    }
}
#endif

