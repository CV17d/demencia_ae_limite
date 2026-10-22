// ============================================================================
// PropThemeData.cs — Perfil Temático de Micro-Props Procedurales
// ============================================================================
// ScriptableObject que encapsula el catálogo de props arquitectónicos y de ambientación
// para un tema específico (ej. Oficina, Industrial, Médico, Laboratorio).
// Divide los prefabs según su anclaje o superficie de colocación.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemenciaNoLimite.Level
{
    /// <summary>
    /// Tipo de anclaje o superficie para la colocación de micro-props.
    /// </summary>
    public enum PropSpawnType
    {
        Floor,      // Props sobre el piso (cajas, bidones, escombros, manchas)
        Wall,       // Props adosados a muros (cuadros, tuberías, interruptores, cables, carteles)
        Ceiling,    // Props colgados del techo (lámparas, cables colgantes, rejillas de ventilación)
        Desk        // Props sobre mesas o escritorios (papeles, monitores, tazas, teclados)
    }

    [CreateAssetMenu(
        fileName = "PropTheme_Nuevo",
        menuName = "Demencia/Props/Perfil Temático de Props",
        order    = 50
    )]
    public class PropThemeData : ScriptableObject
    {
        // ════════════════════════════════════════════════════════════════════
        // IDENTIFICACIÓN DEL TEMA
        // ════════════════════════════════════════════════════════════════════

        [Header("Identificación Temática")]
        [Tooltip("Identificador único de sistema (ej. THEME_OFFICE, THEME_INDUSTRIAL).")]
        public string themeID = "THEME_DEFAULT";

        [Tooltip("Nombre legible del tema para depuración y UI (ej. Oficina, Industrial, Médico).")]
        public string themeName = "Oficina";

        [TextArea(2, 4)]
        [Tooltip("Descripción del contexto visual y ambientación de este tema.")]
        public string description = "Mobiliario y ambientación de oficina abandonada de la Torre de la Frontera.";

        // ════════════════════════════════════════════════════════════════════
        // CATÁLOGOS POR TIPO DE ANCLAJE
        // ════════════════════════════════════════════════════════════════════

        [Header("Props por Tipo de Anclaje")]
        [Tooltip("Props apoyados sobre el suelo (cajas, bidones, basura, escombros).")]
        public List<GameObject> floorProps = new List<GameObject>();

        [Tooltip("Props montados sobre paredes (cuadros, carteles, interruptores, tuberías, cables).")]
        public List<GameObject> wallProps = new List<GameObject>();

        [Tooltip("Props colgados o montados en el techo (lámparas, ductos, cables colgantes).")]
        public List<GameObject> ceilingProps = new List<GameObject>();

        [Tooltip("Props pequeños sobre mesas y escritorios (tazas, carpetas, monitores, instrumental).")]
        public List<GameObject> deskProps = new List<GameObject>();

        // ════════════════════════════════════════════════════════════════════
        // CONSULTAS Y MÉTODOS DE APOYO
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Obtiene la lista de prefabs asociada a un tipo de anclaje específico.
        /// </summary>
        public List<GameObject> GetPropsList(PropSpawnType spawnType)
        {
            switch (spawnType)
            {
                case PropSpawnType.Floor:   return floorProps;
                case PropSpawnType.Wall:    return wallProps;
                case PropSpawnType.Ceiling: return ceilingProps;
                case PropSpawnType.Desk:    return deskProps;
                default:                    return null;
            }
        }

        /// <summary>
        /// Comprueba si este perfil temático contiene al menos un prefab para el anclaje dado.
        /// </summary>
        public bool HasPropsFor(PropSpawnType spawnType)
        {
            List<GameObject> list = GetPropsList(spawnType);
            return list != null && list.Count > 0;
        }

        /// <summary>
        /// Retorna un prefab aleatorio del tipo de anclaje especificado.
        /// Permite inyectar System.Random para asegurar determinismo procedural.
        /// </summary>
        public GameObject GetRandomProp(PropSpawnType spawnType, System.Random rng = null)
        {
            List<GameObject> list = GetPropsList(spawnType);
            if (list == null || list.Count == 0) return null;

            int index = rng != null ? rng.Next(0, list.Count) : UnityEngine.Random.Range(0, list.Count);
            return list[index];
        }

        /// <summary>
        /// Cantidad total de prefabs registrados en este tema.
        /// </summary>
        public int TotalPropsCount =>
            (floorProps?.Count ?? 0) +
            (wallProps?.Count ?? 0) +
            (ceilingProps?.Count ?? 0) +
            (deskProps?.Count ?? 0);
    }
}
