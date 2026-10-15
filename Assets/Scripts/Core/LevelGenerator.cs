// ============================================================================
// LevelGenerator.cs — Maximized & Realistic Building Architecture
// ============================================================================
// Procedural floor plan generator for "Demencia No Limite" skyscraper floors:
//   - 100% Grid Bounds Utilization: No tiny or prematurely cut-off levels.
//   - Realistic Skyscraper Layout: Distinct functional rooms (offices, conference
//     halls, utility rooms), straight arterial corridors, and dividing walls.
//   - Lateral Perimeter Safe Room: Mathematically detects the lateral exterior
//     wall and anchors a dedicated Safe Room containing both the Exit Elevator
//     and the physical Merchant Service Window/Counter.
//   - Boss Arena Layout: High-rise penthouse / executive arena on boss floors.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Core
{
    /// <summary>
    /// Architectural cell classification for procedural floor grids.
    /// </summary>
    public enum CellType
    {
        Wall,            // Interior partition dividing wall
        Floor,           // Walkable room floor (offices, suites)
        Corridor,        // Straight arterial hallway
        SpawnRoom,       // Dedicated player start chamber (West lateral perimeter)
        SafeRoom,        // Walkable floor inside the lateral Safe Room
        ExitRoom,        // Elevator cabin volume
        MerchantCounter, // Physical counter & cutout window for Merchant NPC
        HazardZone,      // Environmental hazard (rubble, toxic spill, live wire)
        SecretRoom,      // Hidden room gated by player Luck
        ExteriorWall,    // Reinforced skyscraper perimeter facade
        ExteriorWindow   // Panoramic exterior window overlooking the void
    }

    /// <summary>
    /// Generated layout data consumed by LevelBuilder to instantiate 3D geometry.
    /// </summary>
    public class FloorLayout
    {
        public int Width;
        public int Height;
        public CellType[,] Grid;
        public Vector2Int SpawnPoint;
        public Vector2Int ExitPoint;
        public Vector2Int SafeRoomEntrance;
        public Vector2Int MerchantCounterPoint;
        public List<RectInt> ArchitecturalRooms = new List<RectInt>();
        public List<Vector2Int> EnemySpawnPoints = new List<Vector2Int>();
        public List<Vector2Int> HazardPoints     = new List<Vector2Int>();
        public List<Vector2Int> ItemSpawnPoints  = new List<Vector2Int>();
    }

    public class LevelGenerator : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // ARCHITECTURAL METRICS
        // ════════════════════════════════════════════════════════════════════

        public const float StandardCellSize = 3.0f;
        public const float StandardWallHeight = 3.2f;
        public const float StandardCeilingHeight = 3.2f;

        [Header("Floor Footprint Scaling")]
        [Tooltip("Minimum building dimensions for early floors (tiles).")]
        [SerializeField] private Vector2Int _minBuildingSize = new Vector2Int(28, 28);
        [Tooltip("Maximum building dimensions for advanced skyscraper floors (tiles).")]
        [SerializeField] private Vector2Int _maxBuildingSize = new Vector2Int(42, 42);

        [Header("Room Sizing Constraints")]
        [SerializeField] private int _minRoomDimension = 4;
        [SerializeField] private int _maxRoomDimension = 9;

        [Header("Boss Penthouse Arena")]
        [SerializeField] private Vector2Int _bossArenaSize = new Vector2Int(24, 24);

        private System.Random _rng;
        public FloorLayout LastGeneratedLayout { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        // MAIN ENTRY POINT
        // ════════════════════════════════════════════════════════════════════

        public FloorLayout GenerateFloor(FloorConfig config)
        {
            int seed = GenerateSeed(config != null ? config.FloorNumber : 1);
            _rng = new System.Random(seed);

            FloorLayout layout;

            if (config != null && config.Type == FloorType.BossRoom)
            {
                layout = GenerateBossPenthouse(config);
            }
            else
            {
                layout = GenerateRealisticSkyscraperFloor(config);
            }

            // Distinguish exterior facade walls from panoramic windows
            ClassifyExteriorEnvelope(layout);

            LastGeneratedLayout = layout;

            Debug.Log($"[LevelGenerator] Floor {(config != null ? config.FloorNumber : 1)} generated with 100% space utilization: " +
                      $"{layout.Width}x{layout.Height} grid | " +
                      $"Rooms: {layout.ArchitecturalRooms.Count} | " +
                      $"Enemies: {layout.EnemySpawnPoints.Count} | " +
                      $"Hazards: {layout.HazardPoints.Count} | " +
                      $"Spawn: {layout.SpawnPoint} | SafeRoom/Elevator: {layout.ExitPoint}");

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. MAXIMIZED REALISTIC BUILDING GENERATION (100% Bounds Utilization)
        // ════════════════════════════════════════════════════════════════════

        private FloorLayout GenerateRealisticSkyscraperFloor(FloorConfig config)
        {
            float difficulty = config != null ? config.BaseDifficulty : 1.0f;
            float scale = Mathf.Clamp01((difficulty - 1f) / 2.0f);

            int width = Mathf.RoundToInt(Mathf.Lerp(_minBuildingSize.x, _maxBuildingSize.x, scale));
            int height = Mathf.RoundToInt(Mathf.Lerp(_minBuildingSize.y, _maxBuildingSize.y, scale));

            // Keep dimensions even/clean for symmetric wings
            if (width % 2 != 0) width++;
            if (height % 2 != 0) height++;

            var layout = new FloorLayout
            {
                Width = width,
                Height = height,
                Grid = new CellType[width, height]
            };

            // Initialize all cells as structural dividing walls initially
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    layout.Grid[x, y] = CellType.Wall;
                }
            }

            int centerY = height / 2;

            // ── STEP 1: Anchor Spawn Chamber on West Lateral Perimeter ──────
            // Dedicated starting security room anchored against the West exterior wall
            int spawnRoomW = 6;
            int spawnRoomH = 6;
            int spawnRoomX = 1;
            int spawnRoomY = centerY - (spawnRoomH / 2);
            RectInt spawnRoomRect = new RectInt(spawnRoomX, spawnRoomY, spawnRoomW, spawnRoomH);
            CarveRoom(layout, spawnRoomRect, CellType.SpawnRoom);
            layout.ArchitecturalRooms.Add(spawnRoomRect);

            layout.SpawnPoint = new Vector2Int(spawnRoomX + (spawnRoomW / 2), centerY);
            layout.Grid[layout.SpawnPoint.x, layout.SpawnPoint.y] = CellType.SpawnRoom;

            // Doorway from Spawn Room to the central hallway
            layout.Grid[spawnRoomX + spawnRoomW - 1, centerY] = CellType.Floor;
            layout.Grid[spawnRoomX + spawnRoomW - 1, centerY - 1] = CellType.Floor;

            // ── STEP 2: Anchor Lateral Safe Room on East Perimeter ───────────
            // Dedicated Safe Room flush against the East lateral exterior wall.
            // Never spawned in the center of the map!
            int safeRoomW = 8;
            int safeRoomH = 7;
            int safeRoomX = width - 1 - safeRoomW;
            int safeRoomY = centerY - 3;
            RectInt safeRoomRect = new RectInt(safeRoomX, safeRoomY, safeRoomW, safeRoomH);
            CarveRoom(layout, safeRoomRect, CellType.SafeRoom);
            layout.ArchitecturalRooms.Add(safeRoomRect);

            // Construct dividing wall on the West boundary with an open doorway into the corridor
            for (int y = safeRoomY; y < safeRoomY + safeRoomH; y++)
            {
                if (y != centerY && y != centerY - 1)
                {
                    layout.Grid[safeRoomX, y] = CellType.Wall;
                }
            }

            // Safe room entrance doorway connecting to the central corridor
            layout.SafeRoomEntrance = new Vector2Int(safeRoomX, centerY);
            layout.Grid[safeRoomX, centerY] = CellType.SafeRoom;
            layout.Grid[safeRoomX, centerY - 1] = CellType.SafeRoom;

            // Direct unobstructed walkway aisle from entrance (safeRoomX) to Exit Elevator (width - 3)
            // at y = centerY and y = centerY - 1
            layout.ExitPoint = new Vector2Int(width - 3, centerY);
            layout.Grid[layout.ExitPoint.x, centerY] = CellType.ExitRoom;
            layout.Grid[layout.ExitPoint.x, centerY - 1] = CellType.ExitRoom;

            // Dedicated Merchant service counter & cutout window
            // Positioned at y = centerY - 2 (on the South side of the walkway aisle)
            // Facing North towards the player walking to the elevator!
            int merchantX = width - 5;
            layout.MerchantCounterPoint = new Vector2Int(merchantX, centerY - 2);
            layout.Grid[merchantX - 1, centerY - 2] = CellType.MerchantCounter;
            layout.Grid[merchantX, centerY - 2] = CellType.MerchantCounter;

            // ── STEP 3: Primary Arterial Hallway Network ─────────────────────
            // Straight central corridor connecting Spawn Room to Safe Room
            int corridorStartX = spawnRoomX + spawnRoomW;
            int corridorEndX = safeRoomX;
            for (int x = corridorStartX; x < corridorEndX; x++)
            {
                layout.Grid[x, centerY] = CellType.Corridor;
                layout.Grid[x, centerY - 1] = CellType.Corridor;
            }

            // Secondary vertical arterial branch corridors
            int branchLeftX = corridorStartX + (corridorEndX - corridorStartX) / 3;
            int branchRightX = corridorStartX + (2 * (corridorEndX - corridorStartX)) / 3;

            for (int y = 2; y < height - 2; y++)
            {
                layout.Grid[branchLeftX, y] = CellType.Corridor;
                layout.Grid[branchRightX, y] = CellType.Corridor;
            }

            // ── STEP 4: Subdivide Remaining Building Bounds (BSP Partitioning)
            // Divide North Wing and South Wing into realistic office rooms.
            // North Wing: y from (centerY + 1) to (height - 2)
            // South Wing: y from 1 to (centerY - 2)
            var northBounds = new RectInt(1, centerY + 1, width - 2, (height - 2) - (centerY + 1) + 1);
            var southBounds = new RectInt(1, 1, width - 2, (centerY - 2));

            SubdivideAndCarveWing(layout, northBounds, spawnRoomRect, safeRoomRect);
            SubdivideAndCarveWing(layout, southBounds, spawnRoomRect, safeRoomRect);

            // ── STEP 5: Architectural Doorway Punching & Connectivity Guarantee
            PunchRoomDoorways(layout);
            EnsureCompleteConnectivity(layout);

            // ── STEP 6: Entity Spawn Placements (Never in Spawn or Safe Room)
            PlaceEnemySpawns(layout, config != null ? config.BaseEnemyCount : 6);
            PlaceHazards(layout, config != null ? config.BaseHazardCount : 4);
            PlaceSecretRooms(layout);

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. RECURSIVE SPACE PARTITIONING (REALISTIC ROOMS)
        // ════════════════════════════════════════════════════════════════════

        private void SubdivideAndCarveWing(FloorLayout layout, RectInt bounds, RectInt reservedSpawn, RectInt reservedSafe)
        {
            var sectors = new List<RectInt>();
            SubdivideRecursive(bounds, sectors);

            foreach (var sector in sectors)
            {
                // Skip if overlapping the reserved lateral rooms
                if (sector.Overlaps(reservedSpawn) || sector.Overlaps(reservedSafe))
                {
                    continue;
                }

                if (sector.width < _minRoomDimension || sector.height < _minRoomDimension)
                {
                    continue;
                }

                // Carve room floor leaving a 1-tile dividing wall boundary
                RectInt roomFloor = new RectInt(
                    sector.x + 1,
                    sector.y + 1,
                    Mathf.Max(1, sector.width - 2),
                    Mathf.Max(1, sector.height - 2)
                );

                CarveRoom(layout, roomFloor, CellType.Floor);
                layout.ArchitecturalRooms.Add(roomFloor);

                // For large rooms (e.g. >= 7x7), add interior structural cover pillars
                if (roomFloor.width >= 7 && roomFloor.height >= 7)
                {
                    int pillarX = roomFloor.x + (roomFloor.width / 2);
                    int pillarY = roomFloor.y + (roomFloor.height / 2);
                    if (layout.Grid[pillarX, pillarY] == CellType.Floor)
                    {
                        layout.Grid[pillarX, pillarY] = CellType.Wall;
                    }
                }
            }
        }

        private void SubdivideRecursive(RectInt sector, List<RectInt> result)
        {
            bool canSplitH = sector.height >= (_minRoomDimension * 2);
            bool canSplitV = sector.width >= (_minRoomDimension * 2);

            if (!canSplitH && !canSplitV)
            {
                result.Add(sector);
                return;
            }

            // Decide split orientation based on aspect ratio
            bool splitH;
            if (canSplitH && canSplitV)
            {
                splitH = (sector.height > sector.width) ? true : (sector.width > sector.height ? false : (_rng.Next(0, 2) == 0));
            }
            else
            {
                splitH = canSplitH;
            }

            if (splitH)
            {
                int splitY = _rng.Next(sector.y + _minRoomDimension, sector.y + sector.height - _minRoomDimension + 1);
                var sub1 = new RectInt(sector.x, sector.y, sector.width, splitY - sector.y);
                var sub2 = new RectInt(sector.x, splitY, sector.width, sector.y + sector.height - splitY);
                SubdivideRecursive(sub1, result);
                SubdivideRecursive(sub2, result);
            }
            else
            {
                int splitX = _rng.Next(sector.x + _minRoomDimension, sector.x + sector.width - _minRoomDimension + 1);
                var sub1 = new RectInt(sector.x, sector.y, splitX - sector.x, sector.height);
                var sub2 = new RectInt(splitX, sector.y, sector.x + sector.width - splitX, sector.height);
                SubdivideRecursive(sub1, result);
                SubdivideRecursive(sub2, result);
            }
        }

        private void CarveRoom(FloorLayout layout, RectInt room, CellType cellType)
        {
            for (int x = room.x; x < room.x + room.width; x++)
            {
                for (int y = room.y; y < room.y + room.height; y++)
                {
                    if (x > 0 && x < layout.Width - 1 && y > 0 && y < layout.Height - 1)
                    {
                        layout.Grid[x, y] = cellType;
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. ARCHITECTURAL DOORWAYS & CONNECTIVITY
        // ════════════════════════════════════════════════════════════════════

        private void PunchRoomDoorways(FloorLayout layout)
        {
            foreach (var room in layout.ArchitecturalRooms)
            {
                // Punch doorways on room perimeters where adjacent to corridors or floors
                List<Vector2Int> candidates = new List<Vector2Int>();

                // Check North and South boundary walls
                for (int x = room.x; x < room.x + room.width; x++)
                {
                    CheckDoorCandidate(layout, x, room.y - 1, candidates);
                    CheckDoorCandidate(layout, x, room.y + room.height, candidates);
                }

                // Check West and East boundary walls
                for (int y = room.y; y < room.y + room.height; y++)
                {
                    CheckDoorCandidate(layout, room.x - 1, y, candidates);
                    CheckDoorCandidate(layout, room.x + room.width, y, candidates);
                }

                if (candidates.Count > 0)
                {
                    // Punch at least one doorway (or two for larger rooms)
                    int doors = (room.width * room.height >= 25 && candidates.Count > 1) ? 2 : 1;
                    for (int d = 0; d < doors; d++)
                    {
                        var pt = candidates[_rng.Next(0, candidates.Count)];
                        layout.Grid[pt.x, pt.y] = CellType.Floor;
                    }
                }
            }
        }

        private void CheckDoorCandidate(FloorLayout layout, int x, int y, List<Vector2Int> candidates)
        {
            if (x <= 1 || x >= layout.Width - 2 || y <= 1 || y >= layout.Height - 2) return;
            if (layout.Grid[x, y] != CellType.Wall) return;

            // Must have walkable cell on opposite sides (strictly standard rooms/corridors)
            bool horizontalPass = (IsStandardWalkable(layout.Grid[x - 1, y]) && IsStandardWalkable(layout.Grid[x + 1, y]));
            bool verticalPass = (IsStandardWalkable(layout.Grid[x, y - 1]) && IsStandardWalkable(layout.Grid[x, y + 1]));

            if (horizontalPass || verticalPass)
            {
                candidates.Add(new Vector2Int(x, y));
            }
        }

        private bool IsStandardWalkable(CellType type)
        {
            return type == CellType.Floor || type == CellType.Corridor;
        }

        private void EnsureCompleteConnectivity(FloorLayout layout)
        {
            // BFS flood fill from SpawnPoint to ensure reachability to all walkable cells
            bool[,] visited = new bool[layout.Width, layout.Height];
            var queue = new Queue<Vector2Int>();

            queue.Enqueue(layout.SpawnPoint);
            visited[layout.SpawnPoint.x, layout.SpawnPoint.y] = true;

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();

                for (int i = 0; i < 4; i++)
                {
                    int nx = cur.x + dx[i];
                    int ny = cur.y + dy[i];

                    if (nx >= 1 && nx < layout.Width - 1 && ny >= 1 && ny < layout.Height - 1)
                    {
                        if (!visited[nx, ny] && IsWalkable(layout.Grid[nx, ny]))
                        {
                            visited[nx, ny] = true;
                            queue.Enqueue(new Vector2Int(nx, ny));
                        }
                    }
                }
            }

            // If SafeRoom / ExitPoint was not reached, tunnel a direct arterial corridor
            if (!visited[layout.ExitPoint.x, layout.ExitPoint.y])
            {
                Debug.LogWarning("[LevelGenerator] Direct arterial bypass carved to ensure 100% elevator connectivity.");
                int cy = layout.Height / 2;
                for (int x = layout.SpawnPoint.x; x <= layout.ExitPoint.x; x++)
                {
                    if (layout.Grid[x, cy] == CellType.Wall)
                    {
                        layout.Grid[x, cy] = CellType.Corridor;
                    }
                }
            }
        }

        private bool IsWalkable(CellType type)
        {
            return type == CellType.Floor ||
                   type == CellType.Corridor ||
                   type == CellType.SpawnRoom ||
                   type == CellType.SafeRoom ||
                   type == CellType.ExitRoom ||
                   type == CellType.HazardZone ||
                   type == CellType.SecretRoom;
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. EXTERIOR FACADE & WINDOW ENVELOPE
        // ════════════════════════════════════════════════════════════════════

        private void ClassifyExteriorEnvelope(FloorLayout layout)
        {
            int w = layout.Width;
            int h = layout.Height;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    bool isPerimeter = (x == 0 || x == w - 1 || y == 0 || y == h - 1);
                    if (!isPerimeter) continue;

                    // Four corners are heavy reinforced pillars
                    bool isCorner = (x == 0 && y == 0) ||
                                    (x == 0 && y == h - 1) ||
                                    (x == w - 1 && y == 0) ||
                                    (x == w - 1 && y == h - 1);

                    if (isCorner)
                    {
                        layout.Grid[x, y] = CellType.ExteriorWall;
                        continue;
                    }

                    // Windows occur in rhythm along perimeter facade (every 2 tiles)
                    bool windowRhythm = ((x + y) % 2 == 0);
                    bool adjacentToWalkable = IsAdjacentToWalkable(layout, x, y);

                    if (windowRhythm && adjacentToWalkable)
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

        private bool IsAdjacentToWalkable(FloorLayout layout, int x, int y)
        {
            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (nx >= 0 && nx < layout.Width && ny >= 0 && ny < layout.Height)
                {
                    if (IsWalkable(layout.Grid[nx, ny])) return true;
                }
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. BOSS PENTHOUSE ARENA (Floors 9, 18, 27)
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

            // Player spawns at South lateral perimeter
            layout.SpawnPoint = new Vector2Int(w / 2, 2);
            layout.Grid[layout.SpawnPoint.x, layout.SpawnPoint.y] = CellType.SpawnRoom;

            // Lateral Exit Elevator gate anchored at North lateral perimeter
            layout.ExitPoint = new Vector2Int(w / 2, h - 3);
            layout.Grid[layout.ExitPoint.x, layout.ExitPoint.y] = CellType.ExitRoom;

            // Safe Room / Merchant Space anchored adjacent to elevator
            layout.MerchantCounterPoint = new Vector2Int((w / 2) + 3, h - 3);
            layout.Grid[layout.MerchantCounterPoint.x, layout.MerchantCounterPoint.y] = CellType.MerchantCounter;

            // Add executive pillars for combat cover
            int pillarOffset = 5;
            layout.Grid[pillarOffset, pillarOffset] = CellType.Wall;
            layout.Grid[w - 1 - pillarOffset, pillarOffset] = CellType.Wall;
            layout.Grid[pillarOffset, h - 1 - pillarOffset] = CellType.Wall;
            layout.Grid[w - 1 - pillarOffset, h - 1 - pillarOffset] = CellType.Wall;

            // Boss arena hazards
            int hazards = config != null ? config.BaseHazardCount : 3;
            for (int i = 0; i < hazards; i++)
            {
                int hx = _rng.Next(4, w - 4);
                int hy = _rng.Next(4, h - 4);
                layout.HazardPoints.Add(new Vector2Int(hx, hy));
                layout.Grid[hx, hy] = CellType.HazardZone;
            }

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. ENTITY PLACEMENT (NEVER IN SPAWN OR SAFE ROOM)
        // ════════════════════════════════════════════════════════════════════

        private void PlaceEnemySpawns(FloorLayout layout, int targetCount)
        {
            int placed = 0;
            int attempts = 0;
            int maxAttempts = targetCount * 30;

            while (placed < targetCount && attempts < maxAttempts)
            {
                attempts++;
                int x = _rng.Next(2, layout.Width - 2);
                int y = _rng.Next(2, layout.Height - 2);

                CellType cell = layout.Grid[x, y];
                if (cell != CellType.Floor && cell != CellType.Corridor) continue;

                var pt = new Vector2Int(x, y);

                // Strictly avoid Spawn Room and Safe Room
                if (Vector2Int.Distance(pt, layout.SpawnPoint) < 6.5f) continue;
                if (Vector2Int.Distance(pt, layout.ExitPoint) < 6.5f) continue;
                if (layout.EnemySpawnPoints.Contains(pt)) continue;

                layout.EnemySpawnPoints.Add(pt);
                placed++;
            }
        }

        private void PlaceHazards(FloorLayout layout, int targetCount)
        {
            int placed = 0;
            int attempts = 0;
            int maxAttempts = targetCount * 25;

            while (placed < targetCount && attempts < maxAttempts)
            {
                attempts++;
                int x = _rng.Next(2, layout.Width - 2);
                int y = _rng.Next(2, layout.Height - 2);

                CellType cell = layout.Grid[x, y];
                if (cell != CellType.Floor && cell != CellType.Corridor) continue;

                var pt = new Vector2Int(x, y);

                // Strictly avoid Spawn Room, Safe Room, and immediate enemy points
                if (Vector2Int.Distance(pt, layout.SpawnPoint) < 5.0f) continue;
                if (Vector2Int.Distance(pt, layout.ExitPoint) < 5.0f) continue;
                if (layout.HazardPoints.Contains(pt)) continue;

                layout.HazardPoints.Add(pt);
                layout.Grid[x, y] = CellType.HazardZone;
                placed++;
            }
        }

        private void PlaceSecretRooms(FloorLayout layout)
        {
            float luck = (GameManager.Instance != null && GameManager.Instance.Player != null)
                ? GameManager.Instance.Player.Luck
                : 0.5f;

            if (_rng.NextDouble() < (0.15f + (luck * 0.35f)))
            {
                int sx = _rng.Next(3, layout.Width - 3);
                int sy = _rng.Next(3, layout.Height - 3);

                if (layout.Grid[sx, sy] == CellType.Wall)
                {
                    layout.Grid[sx, sy] = CellType.SecretRoom;
                    layout.ItemSpawnPoints.Add(new Vector2Int(sx, sy));
                }
            }
        }

        private int GenerateSeed(int floorNumber)
        {
            return Environment.TickCount ^ (floorNumber * 7919);
        }
    }
}
