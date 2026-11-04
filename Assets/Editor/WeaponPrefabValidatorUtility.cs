// ============================================================================
// WeaponPrefabValidatorUtility.cs — Herramienta de Auditoría y Reparación de Prefabs de Armas
// ============================================================================
// Recorre los prefabs en Assets/Prefabs/Entornos/Armas_Prefab/ y asegura que
// dispongan de todos los componentes obligatorios para el juego:
// 1. Renderizadores (MeshRenderer / SkinnedMeshRenderer) para cálculo de Mesh Bounds.
// 2. Colisionador sólido (MeshCollider convex=true o BoxCollider) para físicas y raycast.
// 3. Colisionador Trigger (SphereCollider) para interacción por proximidad.
// 4. Componente Rigidbody para simulación física y reposo en el suelo.
// 5. Componente WorldItemPickup con configuración dinámica de nombre, id y prefab.
// ============================================================================

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DemenciaNoLimite.Pickups;

namespace DemenciaNoLimite.Editor
{
    public static class WeaponPrefabValidatorUtility
    {
        public const string WEAPONS_PREFAB_FOLDER = "Assets/Prefabs/Entornos/Armas_Prefab";

        public class PrefabStatusReport
        {
            public string prefabPath;
            public string weaponName;
            public bool hasRenderers;
            public bool hasSolidCollider;
            public bool hasTriggerCollider;
            public bool hasRigidbody;
            public bool hasPickupScript;
            public bool wasModified;
            public List<string> repairLog = new List<string>();

            public bool IsFullyValid => hasRenderers && hasSolidCollider && hasRigidbody && hasPickupScript;
        }

        // ════════════════════════════════════════════════════════════════════
        // AUTO-VALIDACIÓN AL CARGAR O RECOMPILAR EL PROYECTO
        // ════════════════════════════════════════════════════════════════════

        [InitializeOnLoadMethod]
        private static void AutoVerificarEnCompilacion()
        {
            // Ejecutar escaneo en el siguiente frame libre de la cola de eventos del Editor
            EditorApplication.delayCall += () =>
            {
                var reports = RepararTodosLosPrefabs(true);
                int reparados = 0;
                foreach (var r in reports)
                {
                    if (r.wasModified) reparados++;
                }
                if (reparados > 0)
                {
                    Debug.Log($"<color=#38BDF8><b>[WeaponPrefabValidator]</b> Auto-verificación completada: {reparados} prefab(s) de armas fueron actualizados automáticamente.</color>");
                }
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // MENÚS SUPERIORES EN UNITY EDITOR
        // ════════════════════════════════════════════════════════════════════

        [MenuItem("Tools/Verificar y Reparar Prefabs de Armas", false, 100)]
        [MenuItem("Demencia/🔫 Verificar y Reparar Prefabs de Armas", false, 20)]
        public static void EjecutarVerificacionYReparacionMenu()
        {
            List<PrefabStatusReport> reports = RepararTodosLosPrefabs(true);

            int total = reports.Count;
            int reparados = 0;
            int validos = 0;

            foreach (var r in reports)
            {
                if (r.wasModified) reparados++;
                if (r.IsFullyValid) validos++;
            }

            string mensaje = $"Proceso completado sobre {total} prefab(s) de armas.\n\n" +
                             $"• Prefabs 100% Válidos: {validos}/{total}\n" +
                             $"• Prefabs Reparados / Actualizados: {reparados}\n\n" +
                             "Revisa la consola de Unity para ver el desglose detallado de cada componente.";

            EditorUtility.DisplayDialog("Verificación de Prefabs de Armas", mensaje, "Entendido");
        }

        [MenuItem("Tools/Ventana de Prefabs de Armas", false, 101)]
        [MenuItem("Demencia/🔫 Abrir Gestor de Prefabs de Armas", false, 21)]
        public static void AbrirVentanaGestor()
        {
            WeaponPrefabValidatorWindow.ShowWindow();
        }

        // ════════════════════════════════════════════════════════════════════
        // LÓGICA DE AUDITORÍA Y REPARACIÓN
        // ════════════════════════════════════════════════════════════════════

        public static List<PrefabStatusReport> RepararTodosLosPrefabs(bool applyChanges)
        {
            var reports = new List<PrefabStatusReport>();

            if (!Directory.Exists(WEAPONS_PREFAB_FOLDER))
            {
                Debug.LogWarning($"[WeaponPrefabValidator] No se encontró el directorio: {WEAPONS_PREFAB_FOLDER}");
                return reports;
            }

            string[] prefabFiles = Directory.GetFiles(WEAPONS_PREFAB_FOLDER, "*.prefab", SearchOption.TopDirectoryOnly);

            Debug.Log($"<color=#38BDF8><b>[WeaponPrefabValidator]</b> Iniciando escaneo de {prefabFiles.Length} prefabs en '{WEAPONS_PREFAB_FOLDER}'...</color>");

            foreach (string file in prefabFiles)
            {
                string relativePath = file.Replace('\\', '/');
                PrefabStatusReport report = AuditarYRepararPrefab(relativePath, applyChanges);
                reports.Add(report);
            }

            if (applyChanges)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return reports;
        }

        public static PrefabStatusReport AuditarYRepararPrefab(string prefabPath, bool applyChanges)
        {
            var report = new PrefabStatusReport
            {
                prefabPath = prefabPath,
                weaponName = Path.GetFileNameWithoutExtension(prefabPath),
                wasModified = false
            };

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                report.repairLog.Add($"Error: No se pudo cargar el prefab '{prefabPath}'.");
                return report;
            }

            try
            {
                // 1. Verificación de Renderizadores (MeshRenderer / SkinnedMeshRenderer)
                MeshRenderer[] meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
                SkinnedMeshRenderer[] skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int totalRenderers = meshRenderers.Length + skinnedRenderers.Length;
                report.hasRenderers = totalRenderers > 0;

                if (!report.hasRenderers)
                {
                    report.repairLog.Add("⚠️ ADVERTENCIA: No contiene MeshRenderer ni SkinnedMeshRenderer.");
                }

                // 2. Verificación y Reparación de Colisionadores Físicos y Triggers
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                bool hasSolid = false;
                bool hasTrigger = false;

                foreach (var c in colliders)
                {
                    if (c == null) continue;
                    if (c.isTrigger) hasTrigger = true;
                    else hasSolid = true;
                }

                report.hasSolidCollider = hasSolid;
                report.hasTriggerCollider = hasTrigger;

                if (applyChanges)
                {
                    // Si no tiene colisionador sólido, creamos uno adecuado a la geometría
                    if (!hasSolid)
                    {
                        MeshFilter mf = root.GetComponentInChildren<MeshFilter>(true);
                        if (mf != null && mf.sharedMesh != null && mf.gameObject == root)
                        {
                            MeshCollider mc = root.AddComponent<MeshCollider>();
                            mc.sharedMesh = mf.sharedMesh;
                            mc.convex = true;
                            mc.isTrigger = false;
                            report.hasSolidCollider = true;
                            report.wasModified = true;
                            report.repairLog.Add("Añadido MeshCollider (convex = true, isTrigger = false).");
                        }
                        else
                        {
                            BoxCollider box = root.AddComponent<BoxCollider>();
                            Bounds localBounds = CalcularBoundsLocales(root, meshRenderers, skinnedRenderers);
                            box.center = localBounds.center;
                            box.size = localBounds.size;
                            box.isTrigger = false;
                            report.hasSolidCollider = true;
                            report.wasModified = true;
                            report.repairLog.Add($"Añadido BoxCollider adaptado ({box.size.x:F2}m x {box.size.y:F2}m x {box.size.z:F2}m).");
                        }
                    }

                    // Si no tiene colisionador trigger de proximidad, añadimos un SphereCollider
                    if (!hasTrigger)
                    {
                        SphereCollider triggerSphere = root.AddComponent<SphereCollider>();
                        triggerSphere.isTrigger = true;
                        triggerSphere.radius = 1.8f;
                        report.hasTriggerCollider = true;
                        report.wasModified = true;
                        report.repairLog.Add("Añadido SphereCollider Trigger (radio = 1.8m) para detección por proximidad.");
                    }
                }

                // 3. Verificación y Reparación de Rigidbody
                Rigidbody rb = root.GetComponent<Rigidbody>();
                report.hasRigidbody = (rb != null);

                if (applyChanges && rb == null)
                {
                    rb = root.AddComponent<Rigidbody>();
                    rb.mass = 1.5f;
                    rb.linearDamping = 0.5f;
                    rb.angularDamping = 0.5f;
                    rb.useGravity = true;
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;

                    report.hasRigidbody = true;
                    report.wasModified = true;
                    report.repairLog.Add("Añadido Rigidbody (mass=1.5, useGravity=true, interpolate=true).");
                }

                // 4. Verificación y Reparación de WorldItemPickup
                WorldItemPickup pickup = root.GetComponent<WorldItemPickup>();
                report.hasPickupScript = (pickup != null);

                if (applyChanges)
                {
                    if (pickup == null)
                    {
                        pickup = root.AddComponent<WorldItemPickup>();
                        report.wasModified = true;
                        report.repairLog.Add("Añadido componente WorldItemPickup.");
                    }

                    // Configurar propiedades serializadas de WorldItemPickup de forma dinámica
                    SerializedObject so = new SerializedObject(pickup);
                    so.Update();

                    string fileName = Path.GetFileNameWithoutExtension(prefabPath);
                    string friendlyName = DeducirNombreLegible(fileName);
                    string weaponId = DeducirIdArma(fileName);

                    SerializedProperty propType = so.FindProperty("_itemType");
                    if (propType != null) propType.enumValueIndex = (int)WorldItemType.Weapon;

                    SerializedProperty propName = so.FindProperty("_itemName");
                    if (propName != null) propName.stringValue = friendlyName;

                    SerializedProperty propWeaponId = so.FindProperty("_weaponId");
                    if (propWeaponId != null) propWeaponId.stringValue = weaponId;

                    SerializedProperty propPrefab = so.FindProperty("_weapon3DPrefab");
                    if (propPrefab != null)
                    {
                        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        propPrefab.objectReferenceValue = prefabAsset;
                    }

                    SerializedProperty propKey = so.FindProperty("_interactKey");
                    if (propKey != null) propKey.intValue = (int)KeyCode.E;

                    SerializedProperty propLook = so.FindProperty("_requireLookingAtItem");
                    if (propLook != null) propLook.boolValue = true;

                    SerializedProperty propHUD = so.FindProperty("_showBuiltInHUD");
                    if (propHUD != null) propHUD.boolValue = true;

                    SerializedProperty propDestroy = so.FindProperty("_destroyOnPickup");
                    if (propDestroy != null) propDestroy.boolValue = true;

                    SerializedProperty propIdleAnim = so.FindProperty("_enableIdleAnimation");
                    if (propIdleAnim != null) propIdleAnim.boolValue = true;

                    SerializedProperty propAction = so.FindProperty("_actionLabel");
                    if (propAction != null) propAction.stringValue = "RECOGER";

                    if (so.hasModifiedProperties)
                    {
                        so.ApplyModifiedProperties();
                        report.wasModified = true;
                        report.repairLog.Add($"WorldItemPickup configurado dinámicamente: '{friendlyName}' (ID: '{weaponId}').");
                    }

                    report.hasPickupScript = true;
                }

                // 5. Guardar modificaciones si el prefab fue alterado
                if (applyChanges && report.wasModified)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log($"<color=#4ADE80><b>[WeaponPrefabValidator]</b> ✔ Prefab '{report.weaponName}' reparado y guardado con éxito.</color>\n" +
                              $"Detalles: {string.Join(" | ", report.repairLog)}");
                }
                else if (report.IsFullyValid)
                {
                    Debug.Log($"<color=#A7F3D0>[WeaponPrefabValidator] ✔ Prefab '{report.weaponName}' está 100% verificado y conforme.</color>");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WeaponPrefabValidator] Error al procesar '{prefabPath}': {ex.Message}");
                report.repairLog.Add($"Excepción: {ex.Message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return report;
        }

        // ════════════════════════════════════════════════════════════════════
        // UTILIDADES AUXILIARES
        // ════════════════════════════════════════════════════════════════════

        public static Bounds CalcularBoundsLocales(GameObject root, MeshRenderer[] meshRenderers, SkinnedMeshRenderer[] skinnedRenderers)
        {
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            if (meshRenderers != null)
            {
                for (int i = 0; i < meshRenderers.Length; i++)
                {
                    if (meshRenderers[i] == null) continue;
                    Bounds wb = meshRenderers[i].bounds;
                    Vector3 min = wb.min;
                    Vector3 max = wb.max;

                    Vector3[] corners = new Vector3[]
                    {
                        new Vector3(min.x, min.y, min.z),
                        new Vector3(min.x, min.y, max.z),
                        new Vector3(min.x, max.y, min.z),
                        new Vector3(min.x, max.y, max.z),
                        new Vector3(max.x, min.y, min.z),
                        new Vector3(max.x, min.y, max.z),
                        new Vector3(max.x, max.y, min.z),
                        new Vector3(max.x, max.y, max.z)
                    };

                    foreach (var pt in corners)
                    {
                        Vector3 localPt = root.transform.InverseTransformPoint(pt);
                        if (!hasBounds)
                        {
                            localBounds = new Bounds(localPt, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localPt);
                        }
                    }
                }
            }

            if (skinnedRenderers != null)
            {
                for (int i = 0; i < skinnedRenderers.Length; i++)
                {
                    if (skinnedRenderers[i] == null) continue;
                    Bounds wb = skinnedRenderers[i].bounds;
                    Vector3 min = wb.min;
                    Vector3 max = wb.max;

                    Vector3[] corners = new Vector3[]
                    {
                        new Vector3(min.x, min.y, min.z),
                        new Vector3(min.x, min.y, max.z),
                        new Vector3(min.x, max.y, min.z),
                        new Vector3(min.x, max.y, max.z),
                        new Vector3(max.x, min.y, min.z),
                        new Vector3(max.x, min.y, max.z),
                        new Vector3(max.x, max.y, min.z),
                        new Vector3(max.x, max.y, max.z)
                    };

                    foreach (var pt in corners)
                    {
                        Vector3 localPt = root.transform.InverseTransformPoint(pt);
                        if (!hasBounds)
                        {
                            localBounds = new Bounds(localPt, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localPt);
                        }
                    }
                }
            }

            if (!hasBounds)
            {
                localBounds = new Bounds(Vector3.zero, new Vector3(0.35f, 0.20f, 0.10f));
            }

            return localBounds;
        }

        private static string DeducirNombreLegible(string fileName)
        {
            string lower = fileName.ToLowerInvariant().Trim();
            if (lower.Contains("bingala")) return "Bingala";
            if (lower.Contains("revolver") || lower.Contains("demencia")) return "Demencia Revólver";
            if (lower.Contains("muak")) return "Muak";
            if (lower.Contains("trabeca")) return "Trabeca";

            return char.ToUpperInvariant(fileName[0]) + fileName.Substring(1);
        }

        private static string DeducirIdArma(string fileName)
        {
            string lower = fileName.ToLowerInvariant().Trim();
            if (lower.Contains("bingala")) return "bingala";
            if (lower.Contains("revolver") || lower.Contains("demencia")) return "demencia revolver";
            if (lower.Contains("muak")) return "muak";
            if (lower.Contains("trabeca")) return "trabeca";

            return lower;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // VENTANA DE INTERFAZ GRÁFICA EN EL EDITOR
    // ════════════════════════════════════════════════════════════════════

    public class WeaponPrefabValidatorWindow : EditorWindow
    {
        private List<WeaponPrefabValidatorUtility.PrefabStatusReport> _reports = new List<WeaponPrefabValidatorUtility.PrefabStatusReport>();
        private Vector2 _scrollPos;

        public static void ShowWindow()
        {
            var win = GetWindow<WeaponPrefabValidatorWindow>("Gestor de Prefabs de Armas");
            win.minSize = new Vector2(620, 420);
            win.ActualizarAuditoria();
            win.Show();
        }

        private void OnEnable()
        {
            ActualizarAuditoria();
        }

        private void ActualizarAuditoria()
        {
            _reports = WeaponPrefabValidatorUtility.RepararTodosLosPrefabs(false);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            GUILayout.Label("🔫 Auditoría y Verificación de Prefabs de Armas 3D", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Asegura que cada arma en 'Assets/Prefabs/Entornos/Armas_Prefab/' posea Renderers válidos, " +
                "Collider físico, SphereCollider Trigger de proximidad, Rigidbody y WorldItemPickup.",
                MessageType.Info
            );

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 Re-escanear", GUILayout.Height(28), GUILayout.Width(130)))
            {
                ActualizarAuditoria();
            }

            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUILayout.Button("⚡ Reparar y Configurar Todos los Prefabs", GUILayout.Height(28)))
            {
                _reports = WeaponPrefabValidatorUtility.RepararTodosLosPrefabs(true);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            if (_reports == null || _reports.Count == 0)
            {
                EditorGUILayout.LabelField("No se encontraron prefabs en el directorio.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                foreach (var rep in _reports)
                {
                    DrawPrefabRow(rep);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPrefabRow(WeaponPrefabValidatorUtility.PrefabStatusReport rep)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{rep.weaponName}</b>", EditorStyles.label, GUILayout.Width(160));

            DrawBadge("Renderers", rep.hasRenderers);
            DrawBadge("Collider Sólido", rep.hasSolidCollider);
            DrawBadge("Trigger", rep.hasTriggerCollider);
            DrawBadge("Rigidbody", rep.hasRigidbody);
            DrawBadge("WorldItemPickup", rep.hasPickupScript);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Reparar", GUILayout.Width(75)))
            {
                WeaponPrefabValidatorUtility.AuditarYRepararPrefab(rep.prefabPath, true);
                ActualizarAuditoria();
            }

            if (GUILayout.Button("Ver", GUILayout.Width(45)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(rep.prefabPath);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            EditorGUILayout.EndHorizontal();

            if (rep.repairLog != null && rep.repairLog.Count > 0)
            {
                EditorGUILayout.Space(2);
                foreach (var log in rep.repairLog)
                {
                    EditorGUILayout.LabelField($"  • {log}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void DrawBadge(string title, bool isOk)
        {
            Color prev = GUI.color;
            GUI.color = isOk ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f);
            GUILayout.Label(isOk ? $"[✔ {title}]" : $"[✘ {title}]", EditorStyles.miniLabel, GUILayout.Width(90));
            GUI.color = prev;
        }
    }
}
#endif
