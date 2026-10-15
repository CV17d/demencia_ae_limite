// ============================================================================
// LevelGenerator.cs — 30-Floor Pre-Generation Tower & Layout Synthesizer
// ============================================================================
// Generates all 30 floors of the tower:
//   - Floor 1: Tutorial Lobby (Safe starter area with weapon testing)
//   - Floors 10, 20, 30: Climax Boss Rooms (Act 1, 2, and Final Tower Pinnacle)
//   - Floors 2-9, 11-19, 21-29: Unique procedurally generated roguelike floors
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Core
{
    public class FloorLayout
    {
        public int FloorNumber = 1;
        public int Width;
        public int Height;
        public CellType[,] Grid;            // 2D cell grid
        public Vector2Int SpawnPoint;       // Player start position
        public Vector2Int ExitPoint;        // Elevator trigger position
        public Vector2Int SafeRoomEntrance;
        public RectInt SafeRoomBounds;
        public List<RectInt> ArchitecturalRooms = new List<RectInt>();
        public List<Vector2Int> EnemySpawnPoints = new List<Vector2Int>();
        public List<Vector2Int> HazardPoints     = new List<Vector2Int>();
        public List<Vector2Int> ItemSpawnPoints  = new List<Vector2Int>();
    }

    public enum CellType
    {
        Wall,           // Interior partition wall
        Floor,          // Standard walkable room floor
        Corridor,       // Walkable hallway
        SpawnRoom,      // Player spawn chamber
        ExitRoom,       // Elevator / boss gate chamber
        HazardZone,     // Area denial hazards
        SecretRoom,     // Rare, Luck-gated secret cache
        SafeRoom,       // Lateral fortified safe haven with merchant window
        ExteriorWall,   // Structural perimeter outer wall of skyscraper
        ExteriorWindow, // Exterior perimeter panoramic glass window
        MerchantCounter // Physical service counter separating player and merchant
    }

    public class LevelGenerator : MonoBehaviour
    {
        public const float StandardCellSize = 3.0f;
        public const float StandardWallHeight = 3.2f;
        public const float StandardCeilingHeight = 3.2f;

        [Header("Procedural Floor Boundaries")]
        [SerializeField] private Vector2Int _minBuildingSize = new Vector2Int(18, 18);
        [SerializeField] private Vector2Int _maxBuildingSize = new Vector2Int(30, 30);

        [Header("Room Parameters")]
        [SerializeField] private int _minRoomSize = 4;
        [SerializeField] private int _maxRoomSize = 8;
        [SerializeField] private int _minRooms    = 4;
        [SerializeField] private int _maxRooms    = 9;

        [Header("Boss Arena")]
        [SerializeField] private Vector2Int _bossArenaSize = new Vector2Int(22, 22);

        private System.Random _rng;
        public FloorLayout LastGeneratedLayout { get; private set; }

        /// <summary>
        /// Generates the layout for a given floor configuration.
        /// </summary>
        public FloorLayout GenerateFloor(FloorConfig config)
        {
            int floorNumber = config != null ? config.FloorNumber : 1;
            int seed = GenerateSeed(floorNumber);
            _rng = new System.Random(seed);

            FloorLayout layout;

            if (floorNumber == 1 || (config != null && config.Type == FloorType.TutorialLobby))
            {
                layout = GenerateTutorialLobby(config);
            }
            else if (config != null && config.Type == FloorType.BossRoom)
            {
                layout = GenerateBossPenthouse(config);
            }
            else
            {
                layout = GenerateRealisticSkyscraperFloor(config);
            }

            layout.FloorNumber = floorNumber;

            // Distinguish perimeter walls and panoramic windows
            ClassifyExteriorEnvelope(layout);

            LastGeneratedLayout = layout;

            Debug.Log($"[LevelGenerator] Floor {floorNumber} ({config?.Type}) generated: " +
                      $"{layout.Width}x{layout.Height} grid | " +
                      $"Spawn: {layout.SpawnPoint} | Elevator: {layout.ExitPoint}");

            return layout;
        }

        /// <summary>
        /// Generates layouts for all 30 floors of the tower in one loop.
        /// </summary>
        public Dictionary<int, FloorLayout> GenerateAllTowerLayouts()
        {
            var dict = new Dictionary<int, FloorLayout>();
            for (int f = 1; f <= FloorConfig.MAX_TOWER_FLOORS; f++)
            {
                FloorConfig cfg = FloorConfig.Build(f);
                dict[f] = GenerateFloor(cfg);
            }
            return dict;
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. FLOOR 1: TUTORIAL LOBBY (Safe Starter Floor)
        // ════════════════════════════════════════════════════════════════════

        private FloorLayout GenerateTutorialLobby(FloorConfig config)
        {
            int w = 20;
            int h = 18;

            var layout = new FloorLayout
            {
                Width = w,
                Height = h,
                Grid = new CellType[w, h]
            };

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    bool isBorder = (x == 0 || x == w - 1 || y == 0 || y == h - 1);
                    layout.Grid[x, y] = isBorder ? CellType.ExteriorWall : CellType.Wall;
                }
            }

            // Spawn Lobby Room (Center South)
            RectInt lobby = new RectInt(4, 3, 12, 6);
            CarveRoom(layout, lobby);
            layout.ArchitecturalRooms.Add(lobby);

            layout.SpawnPoint = new Vector2Int(10, 5);
            layout.Grid[layout.SpawnPoint.x, layout.SpawnPoint.y] = CellType.SpawnRoom;

            // Wide Corridor leading to Elevator Safe Room (Center North)
            for (int y = 9; y <= 12; y++)
            {
                for (int x = 9; x <= 11; x++)
                {
                    layout.Grid[x, y] = CellType.Corridor;
                }
            }

            // Elevator Safe Room
            RectInt safeRoom = new RectInt(7, 12, 6, 4);
            CarveRoom(layout, safeRoom);
            for (int x = safeRoom.x; x < safeRoom.x + safeRoom.width; x++)
            {
                for (int y = safeRoom.y; y < safeRoom.y + safeRoom.height; y++)
                {
                    layout.Grid[x, y] = CellType.SafeRoom;
                }
            }
            layout.SafeRoomBounds = safeRoom;
            layout.ExitPoint = new Vector2Int(10, 14);
            layout.Grid[layout.ExitPoint.x, layout.ExitPoint.y] = CellType.ExitRoom;

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. PROCEDURAL ROGUELIKE FLOORS (Floors 2-9, 11-19, 21-29)
        // ════════════════════════════════════════════════════════════════════

        private FloorLayout GenerateRealisticSkyscraperFloor(FloorConfig config)
        {
            float difficulty = config != null ? config.BaseDifficulty : 1.0f;
            float scale = Mathf.Clamp01((difficulty - 1f) / 2.0f);

            int width = Mathf.RoundToInt(Mathf.Lerp(_minBuildingSize.x, _maxBuildingSize.x, scale));
            int height = Mathf.RoundToInt(Mathf.Lerp(_minBuildingSize.y, _maxBuildingSize.y, scale));

            if (width % 2 != 0) width++;
            if (height % 2 != 0) height++;

            var layout = new FloorLayout
            {
                Width = width,
                Height = height,
                Grid = new CellType[width, height]
            };

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    layout.Grid[x, y] = CellType.Wall;
                }
            }

            int roomTarget = (int)Mathf.Lerp(_minRooms, _maxRooms, config != null ? config.MazeComplexity : 0.5f);
            var rooms = PlaceNonOverlappingRooms(layout, roomTarget);

            ConnectRoomsSequential(layout, rooms);

            // Spawn Room in first room
            layout.SpawnPoint = GetRoomCenter(rooms[0]);
            layout.Grid[layout.SpawnPoint.x, layout.SpawnPoint.y] = CellType.SpawnRoom;

            // Safe Room & Exit Elevator in last room
            var lastRoom = rooms[rooms.Count - 1];
            layout.ExitPoint = GetRoomCenter(lastRoom);
            layout.Grid[layout.ExitPoint.x, layout.ExitPoint.y] = CellType.ExitRoom;
            layout.SafeRoomBounds = lastRoom;

            // Spawns & Hazards
            int enemyCount = config != null ? config.BaseEnemyCount : 5;
            PlaceEnemySpawns(layout, rooms, enemyCount);

            int hazardCount = config != null ? config.BaseHazardCount : 2;
            PlaceHazards(layout, hazardCount);

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. BOSS PENTHOUSE ARENAS (Floors 10, 20, 30)
        // ════════════════════════════════════════════════════════════════════

        private FloorLayout GenerateBossPenthouse(FloorConfig config)
        {
            int w = _bossArenaSize.x;
            int h = _bossArenaSize.y;

            var layout = new FloorLayout
            {
                Width = w,
                Height = h,
                Grid = new CellType[w, h]
            };

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    bool isBorder = (x == 0 || x == w - 1 || y == 0 || y == h - 1);
                    layout.Grid[x, y] = isBorder ? CellType.ExteriorWall : CellType.Floor;
                }
            }

            // Player entrance at South
            layout.SpawnPoint = new Vector2Int(w / 2, 2);
            layout.Grid[layout.SpawnPoint.x, layout.SpawnPoint.y] = CellType.SpawnRoom;

            // Exit elevator at North
            layout.ExitPoint = new Vector2Int(w / 2, h - 3);
            layout.Grid[layout.ExitPoint.x, layout.ExitPoint.y] = CellType.ExitRoom;

            // Four symmetrical defensive pillars
            int offset = 5;
            layout.Grid[offset, offset] = CellType.Wall;
            layout.Grid[w - 1 - offset, offset] = CellType.Wall;
            layout.Grid[offset, h - 1 - offset] = CellType.Wall;
            layout.Grid[w - 1 - offset, h - 1 - offset] = CellType.Wall;

            // Environmental hazards
            int hazardCount = config != null ? config.BaseHazardCount : 4;
            for (int i = 0; i < hazardCount; i++)
            {
                int hx = _rng.Next(3, w - 3);
                int hy = _rng.Next(4, h - 4);
                if (layout.Grid[hx, hy] == CellType.Floor)
                {
                    layout.HazardPoints.Add(new Vector2Int(hx, hy));
                    layout.Grid[hx, hy] = CellType.HazardZone;
                }
            }

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // GEOMETRIC HELPER METHODS
        // ════════════════════════════════════════════════════════════════════

        private void ClassifyExteriorEnvelope(FloorLayout layout)
        {
            int w = layout.Width;
            int h = layout.Height;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    bool isEdge = (x == 0 || x == w - 1 || y == 0 || y == h - 1);
                    if (!isEdge) continue;

                    bool isCorner = (x == 0 && y == 0) ||
                                    (x == 0 && y == h - 1) ||
                                    (x == w - 1 && y == 0) ||
                                    (x == w - 1 && y == h - 1);

                    if (isCorner)
                    {
                        layout.Grid[x, y] = CellType.ExteriorWall;
                        continue;
                    }

                    // Windows in regular rhythmic facade intervals
                    if ((x + y) % 2 == 0)
                    {
                        layout.Grid[x, y] = CellType.ExteriorWindow;
                    }
                    else
                    {
                        layout.Grid[x, y] = CellType.ExteriorWall;
                    }
                }
            }
        }

        private List<RectInt> PlaceNonOverlappingRooms(FloorLayout layout, int targetCount)
        {
            var rooms = new List<RectInt>();
            int attempts = 0;
            int maxAttempts = targetCount * 25;

            while (rooms.Count < targetCount && attempts < maxAttempts)
            {
                attempts++;

                int roomW = _rng.Next(_minRoomSize, _maxRoomSize + 1);
                int roomH = _rng.Next(_minRoomSize, _maxRoomSize + 1);

                int rx = _rng.Next(2, layout.Width - roomW - 2);
                int ry = _rng.Next(2, layout.Height - roomH - 2);

                var candidate = new RectInt(rx, ry, roomW, roomH);

                bool overlaps = false;
                foreach (var existing in rooms)
                {
                    var padded = new RectInt(
                        existing.x - 1, existing.y - 1,
                        existing.width + 2, existing.height + 2
                    );
                    if (padded.Overlaps(candidate))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    rooms.Add(candidate);
                    CarveRoom(layout, candidate);
                }
            }

            if (rooms.Count < 2)
            {
                var fallback1 = new RectInt(2, 2, 5, 5);
                var fallback2 = new RectInt(layout.Width - 7, layout.Height - 7, 5, 5);
                if (rooms.Count == 0) { rooms.Add(fallback1); CarveRoom(layout, fallback1); }
                if (rooms.Count == 1) { rooms.Add(fallback2); CarveRoom(layout, fallback2); }
            }

            layout.ArchitecturalRooms = rooms;
            return rooms;
        }

        private void CarveRoom(FloorLayout layout, RectInt room)
        {
            for (int x = room.x; x < room.x + room.width; x++)
            {
                for (int y = room.y; y < room.y + room.height; y++)
                {
                    if (x > 0 && x < layout.Width - 1 && y > 0 && y < layout.Height - 1)
                    {
                        layout.Grid[x, y] = CellType.Floor;
                    }
                }
            }
        }

        private void ConnectRoomsSequential(FloorLayout layout, List<RectInt> rooms)
        {
            rooms.Sort((a, b) =>
            {
                float distA = Vector2.Distance(GetRoomCenter(a), new Vector2(layout.Width / 2f, layout.Height / 2f));
                float distB = Vector2.Distance(GetRoomCenter(b), new Vector2(layout.Width / 2f, layout.Height / 2f));
                return distA.CompareTo(distB);
            });

            for (int i = 0; i < rooms.Count - 1; i++)
            {
                Vector2Int from = GetRoomCenter(rooms[i]);
                Vector2Int to   = GetRoomCenter(rooms[i + 1]);
                CarveLCorridor(layout, from, to);
            }
        }

        private void CarveLCorridor(FloorLayout layout, Vector2Int from, Vector2Int to)
        {
            int startX = Mathf.Min(from.x, to.x);
            int endX   = Mathf.Max(from.x, to.x);
            for (int x = startX; x <= endX; x++)
            {
                if (x > 0 && x < layout.Width - 1 && from.y > 0 && from.y < layout.Height - 1)
                {
                    if (layout.Grid[x, from.y] == CellType.Wall)
                        layout.Grid[x, from.y] = CellType.Corridor;
                }
            }

            int startY = Mathf.Min(from.y, to.y);
            int endY   = Mathf.Max(from.y, to.y);
            for (int y = startY; y <= endY; y++)
            {
                if (to.x > 0 && to.x < layout.Width - 1 && y > 0 && y < layout.Height - 1)
                {
                    if (layout.Grid[to.x, y] == CellType.Wall)
                        layout.Grid[to.x, y] = CellType.Corridor;
                }
            }
        }

        private void PlaceEnemySpawns(FloorLayout layout, List<RectInt> rooms, int count)
        {
            var eligibleRooms = rooms.Count > 2 ? rooms.GetRange(1, rooms.Count - 2) : rooms;
            int placed = 0;
            int attempts = 0;

            while (placed < count && attempts < count * 20)
            {
                attempts++;
                var room = eligibleRooms[_rng.Next(0, eligibleRooms.Count)];
                int ex = _rng.Next(room.x, room.x + room.width);
                int ey = _rng.Next(room.y, room.y + room.height);

                if (ex > 0 && ex < layout.Width - 1 && ey > 0 && ey < layout.Height - 1)
                {
                    var pt = new Vector2Int(ex, ey);
                    if ((layout.Grid[ex, ey] == CellType.Floor || layout.Grid[ex, ey] == CellType.Corridor) &&
                        pt != layout.SpawnPoint &&
                        pt != layout.ExitPoint &&
                        !layout.EnemySpawnPoints.Contains(pt))
                    {
                        layout.EnemySpawnPoints.Add(pt);
                        placed++;
                    }
                }
            }
        }

        private void PlaceHazards(FloorLayout layout, int count)
        {
            int placed = 0;
            int attempts = 0;

            while (placed < count && attempts < count * 20)
            {
                attempts++;
                int hx = _rng.Next(2, layout.Width - 2);
                int hy = _rng.Next(2, layout.Height - 2);

                if (layout.Grid[hx, hy] == CellType.Corridor || layout.Grid[hx, hy] == CellType.Floor)
                {
                    var pt = new Vector2Int(hx, hy);
                    if (!layout.HazardPoints.Contains(pt) &&
                        pt != layout.SpawnPoint &&
                        pt != layout.ExitPoint)
                    {
                        layout.HazardPoints.Add(pt);
                        layout.Grid[hx, hy] = CellType.HazardZone;
                        placed++;
                    }
                }
            }
        }

        private Vector2Int GetRoomCenter(RectInt room)
        {
            return new Vector2Int(room.x + room.width / 2, room.y + room.height / 2);
        }

        private int GenerateSeed(int floorNumber)
        {
            return System.Environment.TickCount ^ (floorNumber * 7919);
        }
    }
}
