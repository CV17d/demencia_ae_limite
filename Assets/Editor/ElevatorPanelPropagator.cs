// ============================================================================
// ElevatorPanelPropagator.cs — Automatización de Paneles de Control en Prefabs
// ============================================================================
// Herramienta de Editor para automatizar la inserción, configuración y guardado
// del panel de control interactivo (Panel_Control + ElevatorPanelController)
// en todos los prefabs de elevadores dentro de "Assets/Prefabs/Entornos/SectorCero/".
// Incluye creación programática de la consola/botonera visual 3D sin collider.
// ============================================================================

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using DemenciaNoLimite.Level;

namespace DemenciaNoLimite.Editor
{
    public static class ElevatorPanelPropagator
    {
        private const string TargetFolder = "Assets/Prefabs/Entornos/SectorCero";
        private const string PanelGameObjectName = "Panel_Control";
        private const string VisualMeshGameObjectName = "Panel_Visual";
        private const string ButtonLedGameObjectName = "Boton_Led";
        private const string ElevatorMaterialPath = "Assets/Art/3D/Texturas/materiales/elevador carga.mat";

        private static readonly Vector3 DefaultLocalPosition = new Vector3(0f, 1.5f, 1.8f);
        private static readonly Vector3 DefaultColliderSize = new Vector3(1.6f, 2.2f, 1.6f);
        private static readonly Vector3 ConsoleVisualScale = new Vector3(0.40f, 0.65f, 0.08f);
        private static readonly Vector3 ButtonLedScale = new Vector3(0.40f, 0.25f, 0.25f);
        private static readonly Vector3 ButtonLedLocalPos = new Vector3(0f, 0.12f, 0.52f);

        [MenuItem("Tools/Sector Cero/Añadir Panel a Elevadores", false, 110)]
        [MenuItem("Demencia/Sector Cero/Añadir Panel a Elevadores", false, 11)]
        public static void PropagarPanelesElevadores()
        {
            if (!AssetDatabase.IsValidFolder(TargetFolder))
            {
                EditorUtility.DisplayDialog(
                    "Error de Carpeta",
                    $"No se encontró la carpeta de entornos en:\n{TargetFolder}\n\nVerifica que la estructura de carpetas sea correcta.",
                    "Aceptar"
                );
                return;
            }

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

            string nombresPrefabs = string.Join("\n • ", elevatorPrefabPaths.ConvertAll(Path.GetFileName));
            bool proceed = EditorUtility.DisplayDialog(
                "Añadir Panel a Elevadores",
                $"Se detectaron {elevatorPrefabPaths.Count} prefabs de elevadores en Sector Cero:\n\n • {nombresPrefabs}\n\n" +
                "¿Deseas insertar/actualizar el objeto 'Panel_Control' con su botonera 3D visual (sin collider), " +
                "Trigger BoxCollider y ElevatorPanelController en cada uno de ellos?",
                "Sí, Proceder",
                "Cancelar"
            );

            if (!proceed) return;

            ProcesarPrefabsElevadores(elevatorPrefabPaths, false);
        }

        /// <summary>
        /// Ejecuta la propagación programática en modo silencioso o interactivo.
        /// </summary>
        public static int EjecutarPropagacionBatch(bool silencioso = true)
        {
            List<string> elevatorPrefabPaths = BuscarPrefabsElevadores();
            return ProcesarPrefabsElevadores(elevatorPrefabPaths, silencioso);
        }

        private static int ProcesarPrefabsElevadores(List<string> elevatorPrefabPaths, bool silencioso)
        {
            int updatedCount = 0;
            int errorCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < elevatorPrefabPaths.Count; i++)
                {
                    string path = elevatorPrefabPaths[i];
                    string fileName = Path.GetFileName(path);

                    if (!silencioso)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Configurando Paneles de Elevador",
                            $"Procesando: {fileName} ({i + 1}/{elevatorPrefabPaths.Count})",
                            (float)i / elevatorPrefabPaths.Count
                        );
                    }

                    GameObject rootGo = PrefabUtility.LoadPrefabContents(path);
                    if (rootGo == null)
                    {
                        Debug.LogError($"[ElevatorPanelPropagator] No se pudo cargar el prefab en memoria: {path}");
                        errorCount++;
                        continue;
                    }

                    try
                    {
                        // Determinar si es elevador de salida (meta) o de entrada (spawn)
                        bool esElevadorSalida = fileName.IndexOf("Mercader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                fileName.IndexOf("Ruleta", StringComparison.OrdinalIgnoreCase) >= 0;

                        // 1. Desactivar y neutralizar triggers pasivos o automáticos (ElevatorExitTrigger / ZonaTrigger)
                        DesactivarTriggersAutomaticos(rootGo);

                        // 2. Configurar o crear el GameObject hijo 'Panel_Control' con su BoxCollider Trigger
                        GameObject panelGo = ConfigurarObjetoPanelControl(rootGo);

                        // 3. Crear o actualizar el objeto visual 3D de la consola (asegurando remoción de colliders bloqueantes)
                        ConfigurarObjetoVisualBotonera(panelGo);

                        // 4. Adjuntar o configurar el componente ElevatorPanelController distinguiendo roles
                        ConfigurarComponenteControlador(panelGo, esElevadorSalida);

                        // 5. Persistir los cambios en el archivo .prefab del disco
                        PrefabUtility.SaveAsPrefabAsset(rootGo, path);
                        updatedCount++;
                        Debug.Log($"[ElevatorPanelPropagator] Prefab '{fileName}' configurado exitosamente. Rol: {(esElevadorSalida ? "SALIDA / META (Habilitado)" : "ENTRADA / SPAWN (Deshabilitado)")}.");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ElevatorPanelPropagator] Error procesando prefab '{fileName}': {ex.Message}");
                        errorCount++;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(rootGo);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                if (!silencioso)
                {
                    EditorUtility.ClearProgressBar();
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (!silencioso)
            {
                EditorUtility.DisplayDialog(
                    "Propagación Completada",
                    $"Proceso finalizado exitosamente:\n\n" +
                    $" • Prefabs de elevador actualizados: {updatedCount}\n" +
                    (errorCount > 0 ? $" • Prefabs con errores: {errorCount}\n" : "") +
                    $"\nCada elevador cuenta ahora con su modelo visual 3D (sin colisionador), BoxCollider Trigger y ElevatorPanelController.",
                    "Aceptar"
                );
            }

            return updatedCount;
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
        public static GameObject ConfigurarObjetoPanelControl(GameObject rootGo)
        {
            if (rootGo == null) return null;

            Transform existingPanel = rootGo.transform.Find(PanelGameObjectName);
            GameObject panelGo;

            if (existingPanel != null)
            {
                panelGo = existingPanel.gameObject;
            }
            else
            {
                panelGo = new GameObject(PanelGameObjectName);
                panelGo.transform.SetParent(rootGo.transform, false);
            }

            panelGo.transform.localPosition = DefaultLocalPosition;
            panelGo.transform.localRotation = Quaternion.identity;
            panelGo.transform.localScale = Vector3.one;

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

        /// <summary>
        /// Crea o actualiza un modelo visual 3D estilizado (consola/botonera de pared)
        /// dentro de Panel_Control, garantizando de forma estricta la eliminación de
        /// cualquier Collider propio para que no bloquee el BoxCollider Trigger del padre.
        /// </summary>
        public static GameObject ConfigurarObjetoVisualBotonera(GameObject panelGo)
        {
            if (panelGo == null) return null;

            // 1. Verificar o crear el cuerpo principal de la consola
            Transform existingVisual = panelGo.transform.Find(VisualMeshGameObjectName);
            GameObject visualGo;

            if (existingVisual != null)
            {
                visualGo = existingVisual.gameObject;
            }
            else
            {
                visualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visualGo.name = VisualMeshGameObjectName;
                visualGo.transform.SetParent(panelGo.transform, false);
            }

            // 2. ELIMINACIÓN ESTRICTA DEL COLLIDER VISUAL
            // Previene que el mesh sólido bloquee la entrada del jugador o altere el Trigger
            RemoverColisionadoresRecursivamente(visualGo);

            // 3. Configurar escala y orientación de consola delgada montada en pared
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localRotation = Quaternion.identity;
            visualGo.transform.localScale = ConsoleVisualScale;

            // 4. Intentar asignar material de elevador industrial si está disponible en disco
            AsignarMaterialVisual(visualGo, ElevatorMaterialPath);

            // 5. Botonera / Luz indicadora frontal
            Transform existingBtn = visualGo.transform.Find(ButtonLedGameObjectName);
            GameObject btnGo;

            if (existingBtn != null)
            {
                btnGo = existingBtn.gameObject;
            }
            else
            {
                btnGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btnGo.name = ButtonLedGameObjectName;
                btnGo.transform.SetParent(visualGo.transform, false);
            }

            RemoverColisionadoresRecursivamente(btnGo);
            btnGo.transform.localPosition = ButtonLedLocalPos;
            btnGo.transform.localRotation = Quaternion.identity;
            btnGo.transform.localScale = ButtonLedScale;

            return visualGo;
        }

        private static void RemoverColisionadoresRecursivamente(GameObject go)
        {
            if (go == null) return;

            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                if (col != null)
                {
                    UnityEngine.Object.DestroyImmediate(col);
                }
            }
        }

        private static void AsignarMaterialVisual(GameObject targetGo, string materialPath)
        {
            if (targetGo == null) return;

            MeshRenderer mr = targetGo.GetComponent<MeshRenderer>();
            if (mr == null) return;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat != null)
            {
                mr.sharedMaterial = mat;
            }
        }

        /// <summary>
        /// Asegura la presencia y configuración del script ElevatorPanelController en el objeto del panel,
        /// diferenciando elevadores de salida (habilitados) y elevadores de spawn/entrada (bloqueados).
        /// </summary>
        public static ElevatorPanelController ConfigurarComponenteControlador(GameObject panelGo, bool esElevadorSalida = true)
        {
            if (panelGo == null) return null;

            ElevatorPanelController controller = panelGo.GetComponent<ElevatorPanelController>();
            if (controller == null)
            {
                controller = panelGo.AddComponent<ElevatorPanelController>();
            }

            controller.StartEnabled = esElevadorSalida;
            controller.enabled = esElevadorSalida;

            SerializedObject so = new SerializedObject(controller);
            SerializedProperty startEnabledProp = so.FindProperty("_startEnabled");
            if (startEnabledProp != null) startEnabledProp.boolValue = esElevadorSalida;

            SerializedProperty isInteractableProp = so.FindProperty("_isInteractable");
            if (isInteractableProp != null) isInteractableProp.boolValue = esElevadorSalida;

            SerializedProperty actionTextProp = so.FindProperty("_promptActionText");
            SerializedProperty subTextProp = so.FindProperty("_promptSubText");
            if (!esElevadorSalida)
            {
                if (actionTextProp != null) actionTextProp.stringValue = "ELEVADOR DE ENTRADA (BLOQUEADO)";
                if (subTextProp != null) subTextProp.stringValue = "ACCESO DESHABILITADO EN ESTE PISO";
            }
            else
            {
                if (actionTextProp != null) actionTextProp.stringValue = "PANEL DE CONTROL DE ELEVADOR";
                if (subTextProp != null) subTextProp.stringValue = "[E] INICIAR SECUENCIA";
            }
            so.ApplyModifiedProperties();

            return controller;
        }

        /// <summary>
        /// Desactiva y neutraliza cualquier sensor automático previo (como ElevatorExitTrigger o ZonaTrigger)
        /// para garantizar que la transición al nuevo piso dependa 100% de la pulsación de la tecla [E] en el panel.
        /// </summary>
        public static void DesactivarTriggersAutomaticos(GameObject rootGo)
        {
            if (rootGo == null) return;

            var exitTriggers = rootGo.GetComponentsInChildren<ElevatorExitTrigger>(true);
            foreach (var trigger in exitTriggers)
            {
                trigger.enabled = false;
                Collider col = trigger.GetComponent<Collider>();
                if (col != null)
                {
                    col.enabled = false;
                }
                if (trigger.gameObject.name.Contains("ZonaTrigger"))
                {
                    trigger.gameObject.SetActive(false);
                }
            }

            Transform zona = rootGo.transform.Find("ZonaTrigger");
            if (zona != null)
            {
                zona.gameObject.SetActive(false);
            }
        }
    }
}
#endif
