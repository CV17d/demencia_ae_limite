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
    /// Herramienta de Technical Artist para automatizar la importación, organización
    /// y configuración física de modelos 3D y Prefabs de props en SectorCero.
    /// </summary>
    public static class PropsOrganizerTool
    {
        private const string SOURCE_PATH = @"C:\Users\lilie\Videos\Modelos 3D";
        private const string PROPS_BASE_DIR = "Assets/Art/3D/SectorCero/Modelos_Props";
        private const string PREFABS_TARGET_DIR = "Assets/Prefabs/Entornos/Props";

        // Palabras clave de exclusión estricta (armas y municiones)
        private static readonly string[] EXCLUDED_KEYWORDS = new string[]
        {
            "Arma", "Revolver", "Gun", "Shotgun", "Pistol", "Bala"
        };

        // Carpetas de destino
        private const string SUBDIR_BANOS = "Baños";
        private const string SUBDIR_OFICINA = "Oficina";
        private const string SUBDIR_TORTURA = "Zona_Tortura_Carniceria";
        private const string SUBDIR_REGUERO = "Reguero_General";

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            EditorApplication.delayCall += () =>
            {
                // Auto-ejecución si aún no se han creado los prefabs
                if (!Directory.Exists(PREFABS_TARGET_DIR) || Directory.GetFiles(PREFABS_TARGET_DIR, "*.prefab").Length == 0)
                {
                    Debug.Log("<color=#38BDF8><b>[PropsOrganizerTool]</b> Detectados prefabs pendientes de creación. Ejecutando automatización...</color>");
                    EjecutarOrganizacionYCreacionPrefabs(false);
                }
            };
        }

        [MenuItem("Demencia/📦 Configurar e Importar Props y Prefabs", false, 10)]
        [MenuItem("Tools/Demencia/Configurar Props y Prefabs", false, 10)]
        public static void EjecutarMenuManual()
        {
            EjecutarOrganizacionYCreacionPrefabs(true);
        }

        public static void EjecutarOrganizacionYCreacionPrefabs(bool mostrarDialogo = true)
        {
            try
            {
                AssetDatabase.StartAssetEditing();

                // 1. Asegurar directorios
                AsegurarDirectorio(PROPS_BASE_DIR);
                AsegurarDirectorio(Path.Combine(PROPS_BASE_DIR, SUBDIR_BANOS));
                AsegurarDirectorio(Path.Combine(PROPS_BASE_DIR, SUBDIR_OFICINA));
                AsegurarDirectorio(Path.Combine(PROPS_BASE_DIR, SUBDIR_TORTURA));
                AsegurarDirectorio(Path.Combine(PROPS_BASE_DIR, SUBDIR_REGUERO));
                AsegurarDirectorio(PREFABS_TARGET_DIR);

                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
                AssetDatabase.StartAssetEditing();

                // 2. Copiar archivos desde origen si existe
                int archivosCopiados = 0;
                if (Directory.Exists(SOURCE_PATH))
                {
                    archivosCopiados = CopiarYOrganizarModelosDesdeOrigen();
                }

                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
                AssetDatabase.StartAssetEditing();

                // 3. Crear Prefabs y configurar física
                int prefabsCreados = CrearPrefabsParaModelosOrganizados();

                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string resumen = $"<b>[PropsOrganizerTool]</b> Proceso completado con éxito:\n" +
                                 $"• Archivos 3D organizados: {archivosCopiados}\n" +
                                 $"• Prefabs generados en '{PREFABS_TARGET_DIR}': {prefabsCreados}\n" +
                                 $"• Layer configurado: 'Default' (0)\n" +
                                 $"• Componente añadido: BoxCollider (isTrigger = false)";

                Debug.Log($"<color=#4ADE80>{resumen}</color>");

                if (mostrarDialogo)
                {
                    EditorUtility.DisplayDialog("Configuración de Props y Prefabs",
                        $"Proceso completado:\n\n• Prefabs generados: {prefabsCreados}\n• Guardados en: {PREFABS_TARGET_DIR}\n• Layer: Default\n• Colisionador: BoxCollider (Sólido)",
                        "Excelente");
                }
            }
            catch (Exception ex)
            {
                AssetDatabase.StopAssetEditing();
                Debug.LogError($"[PropsOrganizerTool] Error durante el proceso: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static int CopiarYOrganizarModelosDesdeOrigen()
        {
            int copiados = 0;
            var directoryInfo = new DirectoryInfo(SOURCE_PATH);

            foreach (var file in directoryInfo.GetFiles("*.*", SearchOption.AllDirectories))
            {
                string ext = file.Extension.ToLower();
                if (ext != ".fbx" && ext != ".obj") continue;

                // Verificación de exclusión estricta
                if (EsArchivoExcluido(file))
                {
                    continue;
                }

                // Determinar subcarpeta de destino
                string subcarpeta = ClasificarModelo(file.Name);
                string destinoDir = Path.Combine(PROPS_BASE_DIR, subcarpeta);
                if (!Directory.Exists(destinoDir))
                {
                    Directory.CreateDirectory(destinoDir);
                }

                string destinoArchivo = Path.Combine(destinoDir, file.Name);
                if (!File.Exists(destinoArchivo))
                {
                    File.Copy(file.FullName, destinoArchivo, true);
                    copiados++;
                }
            }

            return copiados;
        }

        private static bool EsArchivoExcluido(FileInfo file)
        {
            string nombre = file.Name;
            string rutaCompleta = file.FullName;

            // Ignorar carpeta 'Armas' o 'Municion Armas'
            if (rutaCompleta.IndexOf("Armas", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            foreach (var kw in EXCLUDED_KEYWORDS)
            {
                if (nombre.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string ClasificarModelo(string nombreArchivo)
        {
            string nombreLower = Path.GetFileNameWithoutExtension(nombreArchivo).ToLower().Trim();

            // Baños: Tina, Lava manos, retretes desportillados
            if (nombreLower == "tina" || nombreLower == "lava manos" || nombreLower == "retretes desportillados")
            {
                return SUBDIR_BANOS;
            }

            // Oficina: Escritorio, Estante, Sillas giratorias ergonómicas
            if (nombreLower == "escritorio" || nombreLower == "estante" || nombreLower.StartsWith("sillas giratorias ergonómicas"))
            {
                return SUBDIR_OFICINA;
            }

            // Zona_Tortura_Carniceria: Mesa Tortura
            if (nombreLower == "mesa tortura")
            {
                return SUBDIR_TORTURA;
            }

            // Reguero_General: todos los demás objetos pequeños
            return SUBDIR_REGUERO;
        }

        private static int CrearPrefabsParaModelosOrganizados()
        {
            int defaultLayer = LayerMask.NameToLayer("Default");
            int creados = 0;

            // Buscar todos los modelos dentro de PROPS_BASE_DIR
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { PROPS_BASE_DIR });

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                string extension = Path.GetExtension(assetPath).ToLower();
                if (extension != ".fbx" && extension != ".obj") continue;

                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (modelAsset == null) continue;

                string prefabName = Path.GetFileNameWithoutExtension(assetPath).Trim();
                string prefabPath = $"{PREFABS_TARGET_DIR}/{prefabName}.prefab";

                // Instanciar temporalmente para configurar componentes y física
                GameObject tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                if (tempInstance == null)
                {
                    tempInstance = UnityEngine.Object.Instantiate(modelAsset);
                }

                tempInstance.name = prefabName;

                // Configurar Layer: "Default" recursivamente (CRÍTICO: no "Escenarios")
                ConfigurarLayerRecursivo(tempInstance, defaultLayer);

                // Configurar BoxCollider con isTrigger = false
                ConfigurarBoxColliderSolido(tempInstance);

                // Guardar como Prefab Asset
                PrefabUtility.SaveAsPrefabAsset(tempInstance, prefabPath);

                // Limpiar instancia temporal
                UnityEngine.Object.DestroyImmediate(tempInstance);

                creados++;
            }

            return creados;
        }

        private static void ConfigurarLayerRecursivo(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                ConfigurarLayerRecursivo(child.gameObject, layer);
            }
        }

        private static void ConfigurarBoxColliderSolido(GameObject root)
        {
            // Remover cualquier collider existente en el root si se va a reconfigurar o usar BoxCollider
            BoxCollider boxCol = root.GetComponent<BoxCollider>();
            if (boxCol == null)
            {
                boxCol = root.AddComponent<BoxCollider>();
            }

            boxCol.isTrigger = false;

            // Calcular Bounds combinados de todos los Renderers hijos
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds combinedBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    combinedBounds.Encapsulate(renderers[i].bounds);
                }

                // Convertir bounds globales a espacio local del root
                Vector3 centerLocal = root.transform.InverseTransformPoint(combinedBounds.center);
                Vector3 lossy = root.transform.lossyScale;

                boxCol.center = centerLocal;
                boxCol.size = new Vector3(
                    lossy.x != 0 ? Mathf.Abs(combinedBounds.size.x / lossy.x) : combinedBounds.size.x,
                    lossy.y != 0 ? Mathf.Abs(combinedBounds.size.y / lossy.y) : combinedBounds.size.y,
                    lossy.z != 0 ? Mathf.Abs(combinedBounds.size.z / lossy.z) : combinedBounds.size.z
                );
            }
        }

        private static void AsegurarDirectorio(string relativePath)
        {
            if (!Directory.Exists(relativePath))
            {
                Directory.CreateDirectory(relativePath);
            }
        }
    }
}
#endif
