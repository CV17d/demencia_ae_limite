// ============================================================================
// ModuleConnector.cs — Conector Modular de Sockets para Generación Procedural
// ============================================================================
// Componente adjunto a los prefabs arquitectónicos modulares de la ciudad (Sector Cero).
// Expone y gestiona la lista de enchufes (sockets / transforms) disponibles
// para ensamblar pasillos, intersecciones, salas seguras y muros ciegos.
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
        // MÉTODOS DE GESTIÓN
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selecciona y remueve un socket aleatorio utilizando un generador pseudoaleatorio provisto.
        /// </summary>
        /// <param name="rng">Instancia de System.Random alimentada por la semilla del nivel.</param>
        /// <returns>Transform del socket seleccionado, o null si la lista está vacía.</returns>
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
        /// <param name="socket">Transform del socket que fue acoplado.</param>
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
        /// <param name="socket">Transform del socket a registrar.</param>
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
        /// Busca automáticamente todos los hijos que contengan 'Socket' o 'Door' en su nombre.
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
            if (availableSockets == null) return;

            // Dibuja flechas visuales para corroborar la dirección hacia afuera de cada socket
            Gizmos.color = Color.cyan;
            for (int i = 0; i < availableSockets.Count; i++)
            {
                Transform socket = availableSockets[i];
                if (socket == null) continue;

                Gizmos.DrawWireSphere(socket.position, 0.25f);
                Gizmos.DrawRay(socket.position, socket.forward * 1.5f);
            }
        }
    }
}
