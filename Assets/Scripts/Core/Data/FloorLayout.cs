// ============================================================================
// FloorLayout.cs — Contenedores de Datos Arquitectónicos de Piso y Sector
// ============================================================================
// Define la estructura de datos FloorLayout y la enumeración CellType para la
// matriz arquitectónica, puntos de spawn y zonificación de peligros.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace DemenciaNoLimite.Core
{
    /// <summary>
    /// Representa la estructura y distribución espacial de un piso o sector generado.
    /// Contiene la cuadrícula de celdas, puntos de spawn de entidades y zonas de peligro.
    /// </summary>
    public class FloorLayout
    {
        public int FloorNumber = 1;
        public int Width;
        public int Height;
        public CellType[,] Grid;            // Cuadrícula 2D de celdas
        public Vector2Int SpawnPoint;       // Posición de inicio del jugador
        public Vector2Int ExitPoint;        // Disparador de salida / elevador
        public Vector2Int SafeRoomEntrance;
        public RectInt SafeRoomBounds;
        public List<RectInt> ArchitecturalRooms = new List<RectInt>();
        public List<Vector2Int> EnemySpawnPoints = new List<Vector2Int>();
        public List<Vector2Int> HazardPoints     = new List<Vector2Int>();
        public List<Vector2Int> ItemSpawnPoints  = new List<Vector2Int>();
    }

    /// <summary>
    /// Clasificación tipológica de una celda dentro de la cuadrícula arquitectónica.
    /// </summary>
    public enum CellType
    {
        Wall,           // Pared divisoria interior
        Floor,          // Suelo estándar transitable
        Corridor,       // Pasillo transitable
        SpawnRoom,      // Sala de inicio del jugador
        ExitRoom,       // Sala de salida / elevador / compuerta de jefe
        HazardZone,     // Área de riesgo ambiental
        SecretRoom,     // Sala secreta sujeta a estadística de Suerte
        SafeRoom,       // Refugio seguro con mostrador de mercader
        ExteriorWall,   // Muro perimetral exterior
        ExteriorWindow, // Ventanal panorámico perimetral exterior
        MerchantCounter // Mostrador de servicio que separa al mercader del jugador
    }
}
