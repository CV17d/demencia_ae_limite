// ============================================================================
// ElevatorPanelPropagator.cs — Automatización de Paneles de Control en Prefabs
// ============================================================================
// Herramienta de Editor para automatizar la inserción, configuración y guardado
// del panel de control interactivo (Panel_Control + ElevatorPanelController)
// en todos los prefabs de elevadores dentro de "Assets/Prefabs/Entornos/SectorCero/".
// ============================================================================

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace DemenciaNoLimite.Editor
{
    public static class ElevatorPanelPropagator
    {
        private const string TargetFolder = "Assets/Prefabs/Entornos/SectorCero";
        private const string PanelGameObjectName = "Panel_Control";
        private static readonly Vector3 DefaultLocalPosition = new Vector3(0f, 1.5f, 1.8f);
        private static readonly Vector3 DefaultColliderSize = new Vector3(1.6f, 2.2f, 1.6f);

        [MenuItem("Tools/Sector Cero/Añadir Panel a Elevadores", false, 110)]
        [MenuItem("Demencia/Sector Cero/Añadir Panel a Elevadores", false, 11)]
        public static void PropagarPanelesElevadores()
        {
            // 1. Validar la existencia de la carpeta de prefabs
            if (!AssetDatabase.IsValidFolder(TargetFolder))
            {
                EditorUtility.DisplayDialog(
                    "Error de Carpeta",
                    $"No se encontró la carpeta de entornos en:\n{TargetFolder}\n\nVerifica que la estructura de carpetas sea correcta.",
                    "Aceptar"
                );
                return;
            }

            // 2. Localizar todos los prefabs que corresponden a elevadores
            List<string> elevatorPrefabPaths = BuscarPrefabsElevadores();
            if (elevatorPrefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Sin Elevadores Encontrados",
                    $"No se encontraron archivos .prefab con el patrón 'Elevador' en:\n{TargetFolder}",
                    "Aceptar"
                );
                return;
            }

            // 3. Confirmación interactiva previa con el usuario
            string nombresPrefabs = string.Join("\n • ", elevatorPrefabPaths.ConvertAll(Path.GetFileName));
            bool proceed = EditorUtility.DisplayDialog(
                "Añadir Panel a Elevadores",
                $"Se detectaron {elevatorPrefabPaths.Count} prefabs de elevadores en Sector Cero:\n\n • {nombresPrefabs}\n\n" +
                "¿Deseas insertar/actualizar el objeto 'Panel_Control' con BoxCollider Trigger y ElevatorPanelController en cada uno de ellos?",
                "Sí, Proceder",
                "Cancelar"
            );

            if (!proceed) return;

            Debug.Log($"[ElevatorPanelPropagator] Iniciando procesamiento de {elevatorPrefabPaths.Count} prefabs de elevadores...");
        }

        /// <summary>
        /// Escanea el directorio de entornos y devuelve las rutas de todos los prefabs
        /// cuyo nombre contenga 'Elevador' (insensible a mayúsculas/minúsculas).
        /// </summary>
        public static List<string> BuscarPrefabsElevadores()
        {
            var result = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetFolder });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);

                if (fileName.IndexOf("Elevador", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(path);
                }
            }

            return result;
        }

        /// <summary>
        /// Localiza o instancia de manera programática el GameObject hijo 'Panel_Control',
        /// establece sus coordenadas locales frente a la cabina y configura su BoxCollider como Trigger.
        /// </summary>
        /// <param name="rootGo">Raíz del prefab del elevador cargado en memoria.</param>
        /// <returns>Instancia del GameObject 'Panel_Control' debidamente configurado.</returns>
        public static GameObject ConfigurarObjetoPanelControl(GameObject rootGo)
        {
            if (rootGo == null) return null;

            // 1. Buscar si ya existe un hijo con el nombre asignado
            Transform existingPanel = rootGo.transform.Find(PanelGameObjectName);
            GameObject panelGo;

            if (existingPanel != null)
            {
                panelGo = existingPanel.gameObject;
            }
            else
            {
                // Instanciar de manera limpia como hijo del prefab
                panelGo = new GameObject(PanelGameObjectName);
                panelGo.transform.SetParent(rootGo.transform, false);
            }

            // 2. Configurar posición relativa frente a la consola de la cabina
            panelGo.transform.localPosition = DefaultLocalPosition;
            panelGo.transform.localRotation = Quaternion.identity;
            panelGo.transform.localScale = Vector3.one;

            // 3. Añadir y configurar BoxCollider como Is Trigger
            BoxCollider boxCollider = panelGo.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = panelGo.AddComponent<BoxCollider>();
            }

            boxCollider.isTrigger = true;
            boxCollider.size = DefaultColliderSize;
            boxCollider.center = Vector3.zero;

            return panelGo;
        }
    }
}
#endif
