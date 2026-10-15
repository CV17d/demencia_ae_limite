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
        [SerializeField] private Vector2Int _minBuildingSize = new Vector2Int(30, 30);
        [SerializeField] private Vector2Int _maxBuildingSize = new Vector2Int(50, 50);

        [Header("Room Parameters (Labyrinth Architecture)")]
        [SerializeField] private int _minRoomSize = 4;
        [SerializeField] private int _maxRoomSize = 7;
        [SerializeField] private int _minRooms    = 10;
        [SerializeField] private int _maxRooms    = 24;

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
            int floorNumber = config != null ? config.FloorNumber : 2;

            // Progression scaling across tower: Floor 2 up to Floor 29
            float progress = Mathf.Clamp01((floorNumber - 2f) / 27f);

            // Dynamically expand building footprint as player ascends: 32x32 up to 50x50
            int width = Mathf.RoundToInt(Mathf.Lerp(_minBuildingSize.x, _maxBuildingSize.x, progress));
            int height = Mathf.RoundToInt(Mathf.Lerp(_minBuildingSize.y, _maxBuildingSize.y, progress));

            // Organic variance (+-2 cells) based on floor seed
            width += (_rng.Next(-1, 2) * 2);
            height += (_rng.Next(-1, 2) * 2);
            width = Mathf.Clamp(width, 30, 52);
            height = Mathf.Clamp(height, 30, 52);

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

            // Target room count: 10-15 on early floors, scaling up to 18-24 on high floors
            int minR = Mathf.RoundToInt(Mathf.Lerp(_minRooms, _minRooms + 6, progress));
            int maxR = Mathf.RoundToInt(Mathf.Lerp(_minRooms + 4, _maxRooms, progress));
            int roomTarget = _rng.Next(minR, maxR + 1);

            var rooms = PlaceNonOverlappingRooms(layout, roomTarget);

            // Interconnected branching labyrinth network with cycles, dead-ends, and loops
            ConnectRoomsLabyrinthine(layout, rooms);

            // 1. Pick Spawn Room at one of the outer perimeter corners
            RectInt spawnRoom = SelectSpawnRoom(layout, rooms);
            layout.SpawnPoint = GetRoomCenter(spawnRoom);
            layout.Grid[layout.SpawnPoint.x, layout.SpawnPoint.y] = CellType.SpawnRoom;

            // 2. Identify the room with the MAXIMUM reachable topological distance from Spawn
            // This guarantees the elevator is hidden deep within the furthest wing of the maze!
            RectInt elevatorRoom = FindFurthestReachableRoom(layout, rooms, layout.SpawnPoint);
            layout.ExitPoint = GetRoomCenter(elevatorRoom);
            layout.SafeRoomBounds = elevatorRoom;

            // Convert elevator room into Safe Room
            for (int x = elevatorRoom.x; x < elevatorRoom.x + elevatorRoom.width; x++)
            {
                for (int y = elevatorRoom.y; y < elevatorRoom.y + elevatorRoom.height; y++)
                {
                    if (x > 0 && x < layout.Width - 1 && y > 0 && y < layout.Height - 1)
                    {
                        if (layout.Grid[x, y] == CellType.Floor)
                            layout.Grid[x, y] = CellType.SafeRoom;
                    }
                }
            }
            layout.Grid[layout.ExitPoint.x, layout.ExitPoint.y] = CellType.ExitRoom;

            // 3. Spawns & Hazards distributed across labyrinth wings
            int enemyCount = config != null ? config.BaseEnemyCount : (6 + floorNumber / 2);
            PlaceEnemySpawns(layout, rooms, enemyCount);

            int hazardCount = config != null ? config.BaseHazardCount : (2 + floorNumber / 4);
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
            int maxAttempts = targetCount * 60;

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

            // Fallback safety: ensure minimum 4 rooms even in worst rng packing
            if (rooms.Count < 4)
            {
                var fallback1 = new RectInt(2, 2, 5, 5);
                var fallback2 = new RectInt(layout.Width - 7, layout.Height - 7, 5, 5);
                var fallback3 = new RectInt(2, layout.Height - 7, 5, 5);
                var fallback4 = new RectInt(layout.Width - 7, 2, 5, 5);
                if (!rooms.Contains(fallback1)) { rooms.Add(fallback1); CarveRoom(layout, fallback1); }
                if (!rooms.Contains(fallback2)) { rooms.Add(fallback2); CarveRoom(layout, fallback2); }
                if (!rooms.Contains(fallback3)) { rooms.Add(fallback3); CarveRoom(layout, fallback3); }
                if (!rooms.Contains(fallback4)) { rooms.Add(fallback4); CarveRoom(layout, fallback4); }
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

        private RectInt SelectSpawnRoom(FloorLayout layout, List<RectInt> rooms)
        {
            if (rooms.Count == 0) return new RectInt(2, 2, 4, 4);

            // Pick a room nearest to one of the 4 outer perimeter corners of the skyscraper
            Vector2[] corners = new Vector2[]
            {
                new Vector2(3f, 3f),
                new Vector2(layout.Width - 4f, 3f),
                new Vector2(3f, layout.Height - 4f),
                new Vector2(layout.Width - 4f, layout.Height - 4f)
            };
            Vector2 chosenCorner = corners[_rng.Next(0, corners.Length)];

            RectInt bestRoom = rooms[0];
            float bestDist = float.MaxValue;
            foreach (var r in rooms)
            {
                float d = Vector2.Distance(GetRoomCenter(r), chosenCorner);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestRoom = r;
                }
            }
            return bestRoom;
        }

        /// <summary>
        /// Runs a 4-directional Breadth-First Search (BFS) over the walkable grid from the Spawn Room
        /// and identifies the room with the MAXIMUM reachable walkable path distance.
        /// This mathematically guarantees the elevator is placed at the deepest end of the labyrinth.
        /// </summary>
        private RectInt FindFurthestReachableRoom(FloorLayout layout, List<RectInt> rooms, Vector2Int startPos)
        {
            if (rooms.Count <= 1) return rooms[0];

            int w = layout.Width;
            int h = layout.Height;
            int[,] dist = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    dist[x, y] = -1;

            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(startPos);
            dist[startPos.x, startPos.y] = 0;

            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            while (queue.Count > 0)
            {
                Vector2Int curr = queue.Dequeue();
                int currentDist = dist[curr.x, curr.y];

                for (int i = 0; i < 4; i++)
                {
                    int nx = curr.x + dx[i];
                    int ny = curr.y + dy[i];

                    if (nx >= 0 && nx < w && ny >= 0 && ny < h && dist[nx, ny] == -1)
                    {
                        CellType cell = layout.Grid[nx, ny];
                        if (cell == CellType.Floor || cell == CellType.Corridor ||
                            cell == CellType.SafeRoom || cell == CellType.SpawnRoom ||
                            cell == CellType.ExitRoom || cell == CellType.HazardZone)
                        {
                            dist[nx, ny] = currentDist + 1;
                            queue.Enqueue(new Vector2Int(nx, ny));
                        }
                    }
                }
            }

            RectInt furthestRoom = rooms[rooms.Count - 1];
            int maxDistance = -1;

            foreach (var r in rooms)
            {
                Vector2Int c = GetRoomCenter(r);
                if (c == startPos) continue;

                int d = dist[c.x, c.y];
                if (d > maxDistance)
                {
                    maxDistance = d;
                    furthestRoom = r;
                }
            }

            // Fallback if unreachable by BFS
            if (maxDistance <= 0)
            {
                float maxEuclidean = -1f;
                foreach (var r in rooms)
                {
                    Vector2Int c = GetRoomCenter(r);
                    if (c == startPos) continue;
                    float d = Vector2Int.Distance(startPos, c);
                    if (d > maxEuclidean)
                    {
                        maxEuclidean = d;
                        furthestRoom = r;
                    }
                }
            }

            return furthestRoom;
        }

        private struct LabyrinthEdge
        {
            public int From;
            public int To;
            public float Distance;
        }

        /// <summary>
        /// Connects rooms using a Minimum Spanning Tree (Kruskal's) plus 35% cycle/loop edges.
        /// Produces a complex, realistic labyrinth with branches, multiple pathways, and dead-ends.
        /// </summary>
        private void ConnectRoomsLabyrinthine(FloorLayout layout, List<RectInt> rooms)
        {
            if (rooms.Count < 2) return;

            var edges = new List<LabyrinthEdge>();
            for (int i = 0; i < rooms.Count; i++)
            {
                for (int j = i + 1; j < rooms.Count; j++)
                {
                    float d = Vector2.Distance(GetRoomCenter(rooms[i]), GetRoomCenter(rooms[j]));
                    edges.Add(new LabyrinthEdge { From = i, To = j, Distance = d });
                }
            }
            edges.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Disjoint Set Union
            int[] parent = new int[rooms.Count];
            for (int i = 0; i < rooms.Count; i++) parent[i] = i;

            int Find(int i)
            {
                if (parent[i] == i) return i;
                return parent[i] = Find(parent[i]);
            }

            void Union(int i, int j)
            {
                int rootA = Find(i);
                int rootB = Find(j);
                if (rootA != rootB) parent[rootA] = rootB;
            }

            var selectedEdges = new List<LabyrinthEdge>();
            var remainingEdges = new List<LabyrinthEdge>();

            foreach (var edge in edges)
            {
                if (Find(edge.From) != Find(edge.To))
                {
                    Union(edge.From, edge.To);
                    selectedEdges.Add(edge);
                }
                else
                {
                    remainingEdges.Add(edge);
                }
            }

            // Add 35% extra cycles/loops so players have intersecting corridors and branches
            int extraLoops = Mathf.Min(remainingEdges.Count, Mathf.Max(2, rooms.Count / 3));
            for (int i = 0; i < extraLoops; i++)
            {
                selectedEdges.Add(remainingEdges[i]);
            }

            // Carve corridors
            foreach (var edge in selectedEdges)
            {
                Vector2Int from = GetRoomCenter(rooms[edge.From]);
                Vector2Int to   = GetRoomCenter(rooms[edge.To]);
                CarveDoglegCorridor(layout, from, to);
            }
        }

        private void CarveDoglegCorridor(FloorLayout layout, Vector2Int from, Vector2Int to)
        {
            bool xFirst = _rng.Next(0, 2) == 0;
            if (xFirst)
            {
                CarveCorridorLine(layout, from.x, from.y, to.x, from.y);
                CarveCorridorLine(layout, to.x, from.y, to.x, to.y);
            }
            else
            {
                CarveCorridorLine(layout, from.x, from.y, from.x, to.y);
                CarveCorridorLine(layout, from.x, to.y, to.x, to.y);
            }
        }

        private void CarveCorridorLine(FloorLayout layout, int x0, int y0, int x1, int y1)
        {
            int minX = Mathf.Min(x0, x1);
            int maxX = Mathf.Max(x0, x1);
            int minY = Mathf.Min(y0, y1);
            int maxY = Mathf.Max(y0, y1);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (x > 0 && x < layout.Width - 1 && y > 0 && y < layout.Height - 1)
                    {
                        if (layout.Grid[x, y] == CellType.Wall)
                        {
                            layout.Grid[x, y] = CellType.Corridor;
                        }
                    }
                }
            }
        }

        private void PlaceEnemySpawns(FloorLayout layout, List<RectInt> rooms, int count)
        {
            var eligibleRooms = new List<RectInt>();
            foreach (var r in rooms)
            {
                Vector2Int c = GetRoomCenter(r);
                if (c != layout.SpawnPoint && c != layout.ExitPoint)
                {
                    eligibleRooms.Add(r);
                }
            }
            if (eligibleRooms.Count == 0) eligibleRooms = rooms;

            int placed = 0;
            int attempts = 0;

            while (placed < count && attempts < count * 35)
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
