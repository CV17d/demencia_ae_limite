// ============================================================================
// ModuleConnector.cs — Conector Modular de Sockets y Conciencia Espacial
// ============================================================================
// Componente adjunto a los prefabs arquitectónicos modulares de la ciudad (Sector Cero).
// - Expone y gestiona la lista de enchufes (sockets / transforms) disponibles.
// - Delimita y calcula el volumen real (Bounds) de la pieza para prevención de solapamientos (Overlap Prevention).
// - Compatible con BoxCollider (Trigger), Renderer.bounds combinados y dimensiones manuales.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemenciaNoLimite.Level
{
    [SelectionBase]
    [DisallowMultipleComponent]
    public class ModuleConnector : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // ENCHUFES (SOCKETS)
        // ════════════════════════════════════════════════════════════════════

        [Header("Configuración de Conexiones")]
        [Tooltip("Lista de Transforms que representan los enchufes de salida del módulo. " +
                 "El eje Z positivo (forward / azul) del socket debe apuntar hacia afuera del módulo.")]
        public List<Transform> availableSockets = new List<Transform>();

        // ════════════════════════════════════════════════════════════════════
        // CONCIENCIA ESPACIAL Y VOLUMEN (BOUNDS)
        // ════════════════════════════════════════════════════════════════════

        [Header("Conciencia Espacial (Overlap Prevention)")]
        [Tooltip("BoxCollider que delimita el volumen físico o lógico de la pieza (idealmente IsTrigger). " +
                 "Si no se asigna, se busca en este GameObject o se calcula automáticamente desde los Renderers de sus hijos.")]
        [SerializeField] private BoxCollider _volumeCollider;

        [Tooltip("Volumen manual personalizado en espacio local. Si su tamaño es mayor a cero, anula el cálculo automático.")]
        [SerializeField] private Bounds _manualBounds = new Bounds(Vector3.zero, Vector3.zero);

        [Tooltip("Si es true, calcula automáticamente el volumen encapsulando los Renderers de todos sus hijos en espacio local.")]
        [SerializeField] private bool _autoCalculateFromRenderers = true;

        [Header("Caché de Volumen Local")]
        [SerializeField] private Bounds _cachedLocalBounds;
        [SerializeField] private bool _hasCachedBounds = false;

        // ════════════════════════════════════════════════════════════════════
        // PROPIEDADES Y CONSULTAS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Indica si el módulo aún cuenta con enchufes libres para conectar piezas.
        /// </summary>
        public bool HasAvailableSockets => availableSockets != null && availableSockets.Count > 0;

        /// <summary>
        /// Retorna la cantidad de sockets que aún no han sido acoplados.
        /// </summary>
        public int SocketCount => availableSockets != null ? availableSockets.Count : 0;

        // ════════════════════════════════════════════════════════════════════
        // CICLO DE VIDA Y VALIDACIÓN
        // ════════════════════════════════════════════════════════════════════

        private void Reset()
        {
            if (_volumeCollider == null)
            {
                _volumeCollider = GetComponent<BoxCollider>();
            }
            RecalcularBoundsInternos();
        }

        private void OnValidate()
        {
            if (!_hasCachedBounds || _cachedLocalBounds.size.sqrMagnitude < 0.001f)
            {
                RecalcularBoundsInternos();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // GESTIÓN DE VOLUMEN (BOUNDS)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retorna el volumen local (Bounds) de la pieza arquitectónica.
        /// Prioriza bounds manuales, luego BoxCollider asignado, luego Renderers combinados.
        /// </summary>
        public Bounds GetLocalBounds()
        {
            if (_manualBounds.size.sqrMagnitude > 0.001f)
            {
                return _manualBounds;
            }

            if (_hasCachedBounds && _cachedLocalBounds.size.sqrMagnitude > 0.001f)
            {
                return _cachedLocalBounds;
            }

            _cachedLocalBounds = CalculateLocalBounds();
            _hasCachedBounds = true;
            return _cachedLocalBounds;
        }

        /// <summary>
        /// Calcula el volumen local de la pieza en tiempo de ejecución o edición.
        /// </summary>
        public Bounds CalculateLocalBounds()
        {
            // 1. Si hay bounds manuales explícitos definidos en el inspector
            if (_manualBounds.size.sqrMagnitude > 0.001f)
            {
                return _manualBounds;
            }

            // 2. Si hay un BoxCollider configurado en la raíz o en los hijos
            if (_volumeCollider == null)
            {
                _volumeCollider = GetComponent<BoxCollider>();
                if (_volumeCollider == null)
                {
                    _volumeCollider = GetComponentInChildren<BoxCollider>();
                }
            }

            if (_volumeCollider != null)
            {
                // Si el collider está en un hijo, trasladamos su centro al espacio local del módulo raíz
                if (_volumeCollider.transform != transform)
                {
                    Vector3 worldCenter = _volumeCollider.transform.TransformPoint(_volumeCollider.center);
                    Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
                    Vector3 worldSize = Vector3.Scale(_volumeCollider.size, _volumeCollider.transform.lossyScale);
                    Vector3 localSize = new Vector3(
                        Mathf.Abs(worldSize.x / (transform.lossyScale.x > 0.0001f ? transform.lossyScale.x : 1f)),
                        Mathf.Abs(worldSize.y / (transform.lossyScale.y > 0.0001f ? transform.lossyScale.y : 1f)),
                        Mathf.Abs(worldSize.z / (transform.lossyScale.z > 0.0001f ? transform.lossyScale.z : 1f))
                    );
                    return new Bounds(localCenter, localSize);
                }

                return new Bounds(_volumeCollider.center, _volumeCollider.size);
            }

            // 3. Cálculo automático encapsulando los Renderer.bounds de todos los hijos
            if (_autoCalculateFromRenderers)
            {
                Renderer[] renderers = GetComponentsInChildren<Renderer>(false);
                if (renderers != null && renderers.Length > 0)
                {
                    Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
                    Bounds combined = new Bounds();
                    bool initialized = false;

                    for (int r = 0; r < renderers.Length; r++)
                    {
                        Renderer rend = renderers[r];
                        // Solo consideramos componentes de malla física/visual (muros, pisos, techos)
                        if (!(rend is MeshRenderer) && !(rend is SkinnedMeshRenderer)) continue;

                        Bounds rb = rend.bounds;
                        Vector3 min = rb.min;
                        Vector3 max = rb.max;

                        // Proyectamos las 8 esquinas del AABB del renderer en mundo al espacio local de este módulo
                        Vector3[] corners = new Vector3[8]
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

                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 localPoint = worldToLocal.MultiplyPoint3x4(corners[i]);
                            if (!initialized)
                            {
                                combined = new Bounds(localPoint, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                combined.Encapsulate(localPoint);
                            }
                        }
                    }

                    if (initialized && combined.size.sqrMagnitude > 0.001f)
                    {
                        return combined;
                    }
                }
            }

            // 4. Fallback estándar basado en celda unitaria de grid (4x4x4 centrado a 2 unidades del suelo)
            return new Bounds(new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 4f));
        }

        /// <summary>
        /// Proyecta el volumen local del módulo a coordenadas de mundo para una posición y rotación simuladas.
        /// </summary>
        /// <param name="worldPos">Posición simulada en el mundo.</param>
        /// <param name="worldRot">Rotación simulada en el mundo.</param>
        /// <returns>AABB del módulo en coordenadas de mundo.</returns>
        public Bounds CalculateWorldBounds(Vector3 worldPos, Quaternion worldRot)
        {
            Bounds local = GetLocalBounds();
            return TransformBounds(local, worldPos, worldRot);
        }

        /// <summary>
        /// Transforma un Bounds local a coordenadas de mundo a partir de una posición y rotación arbitrarias.
        /// </summary>
        public static Bounds TransformBounds(Bounds localBounds, Vector3 worldPosition, Quaternion worldRotation)
        {
            Vector3 worldCenter = worldPosition + (worldRotation * localBounds.center);
            Vector3 extents = localBounds.extents;

            Vector3 right = worldRotation * Vector3.right;
            Vector3 up = worldRotation * Vector3.up;
            Vector3 forward = worldRotation * Vector3.forward;

            Vector3 worldExtents = new Vector3(
                Mathf.Abs(right.x) * extents.x + Mathf.Abs(up.x) * extents.y + Mathf.Abs(forward.x) * extents.z,
                Mathf.Abs(right.y) * extents.x + Mathf.Abs(up.y) * extents.y + Mathf.Abs(forward.y) * extents.z,
                Mathf.Abs(right.z) * extents.x + Mathf.Abs(up.z) * extents.y + Mathf.Abs(forward.z) * extents.z
            );

            return new Bounds(worldCenter, worldExtents * 2f);
        }

        /// <summary>
        /// Obtiene la posición y rotación relativas de un socket con respecto a la raíz del módulo,
        /// soportando jerarquías anidadas arbitrarias creadas por los artistas 3D.
        /// </summary>
        public static void GetSocketRelativeTransform(Transform root, Transform socket, out Vector3 relativePos, out Quaternion relativeRot)
        {
            if (socket == null || root == null || socket == root)
            {
                relativePos = Vector3.zero;
                relativeRot = Quaternion.identity;
                return;
            }

            if (socket.parent == root)
            {
                relativePos = socket.localPosition;
                relativeRot = socket.localRotation;
                return;
            }

            // Jerarquías anidadas profundas: acumula transformaciones locales hacia la raíz
            Vector3 pos = socket.localPosition;
            Quaternion rot = socket.localRotation;
            Transform curr = socket.parent;

            while (curr != null && curr != root)
            {
                pos = curr.localPosition + (curr.localRotation * Vector3.Scale(pos, curr.localScale));
                rot = curr.localRotation * rot;
                curr = curr.parent;
            }

            relativePos = pos;
            relativeRot = rot;
        }

        // ════════════════════════════════════════════════════════════════════
        // MÉTODOS DE GESTIÓN DE SOCKETS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selecciona y remueve un socket aleatorio utilizando un generador determinista provisto.
        /// </summary>
        public Transform ConsumeRandomSocket(System.Random rng)
        {
            if (!HasAvailableSockets) return null;

            int index = rng.Next(0, availableSockets.Count);
            Transform socket = availableSockets[index];
            availableSockets.RemoveAt(index);
            return socket;
        }

        /// <summary>
        /// Remueve un socket específico de la lista de disponibles al haber sido conectado.
        /// </summary>
        public void RemoveSocket(Transform socket)
        {
            if (availableSockets != null && socket != null)
            {
                availableSockets.Remove(socket);
            }
        }

        /// <summary>
        /// Registra un nuevo socket disponible.
        /// </summary>
        public void RegisterSocket(Transform socket)
        {
            if (socket == null) return;

            if (availableSockets == null)
            {
                availableSockets = new List<Transform>();
            }

            if (!availableSockets.Contains(socket))
            {
                availableSockets.Add(socket);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // UTILIDADES DE EDITOR Y DEPURACIÓN
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recalcula y almacena en caché el volumen local del módulo.
        /// </summary>
        [ContextMenu("Recalcular Volumen (Bounds)")]
        public void RecalcularBoundsInternos()
        {
            _cachedLocalBounds = CalculateLocalBounds();
            _hasCachedBounds = true;
            Debug.Log($"[ModuleConnector] Volumen actualizado en '{gameObject.name}': Centro={_cachedLocalBounds.center}, Tamaño={_cachedLocalBounds.size}");
        }

        /// <summary>
        /// Busca automáticamente todos los hijos que contengan 'Socket', 'Door' o 'Enchufe' en su nombre.
        /// </summary>
        [ContextMenu("Buscar Sockets en Hijos")]
        public void AutoFindSockets()
        {
            availableSockets.Clear();
            Transform[] allChildren = GetComponentsInChildren<Transform>();

            for (int i = 0; i < allChildren.Length; i++)
            {
                Transform child = allChildren[i];
                if (child == transform) continue;

                string lowerName = child.name.ToLowerInvariant();
                if (lowerName.Contains("socket") || lowerName.Contains("door") || lowerName.Contains("enchufe"))
                {
                    availableSockets.Add(child);
                }
            }

            Debug.Log($"[ModuleConnector] Sockets encontrados en '{gameObject.name}': {availableSockets.Count}");
        }

        private void OnDrawGizmosSelected()
        {
            // 1. Dibuja flechas visuales para los enchufes disponibles
            if (availableSockets != null)
            {
                Gizmos.color = Color.cyan;
                for (int i = 0; i < availableSockets.Count; i++)
                {
                    Transform socket = availableSockets[i];
                    if (socket == null) continue;

                    Gizmos.DrawWireSphere(socket.position, 0.25f);
                    Gizmos.DrawRay(socket.position, socket.forward * 1.5f);
                }
            }

            // 2. Dibuja el volumen arquitectónico (Bounds) en el Scene View
            Bounds localBounds = GetLocalBounds();
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = new Color(0.1f, 1f, 0.4f, 0.65f);
            Gizmos.DrawWireCube(localBounds.center, localBounds.size);

            Gizmos.matrix = previousMatrix;
        }
    }
}
