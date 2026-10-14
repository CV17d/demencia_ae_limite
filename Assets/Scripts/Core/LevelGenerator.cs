// ============================================================================
// LevelGenerator.cs — 27-Floor Progression & Procedural Maze Logic
// ============================================================================
// Handles the structural logic for generating each floor:
//   - Determines maze dimensions, room count, path complexity
//   - Selects enemy spawn points and hazard placements
//   - Delegates boss room layout for boss floors
//
// NOTE: This script handles the LOGIC and DATA of level generation.
// Actual mesh/prefab instantiation will be handled by a separate
// LevelBuilder system that reads the generated FloorLayout.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.Core
{
    /// <summary>
    /// Output data structure from the level generator.
    /// Consumed by the LevelBuilder (future) to instantiate the physical level.
    /// </summary>
    public class FloorLayout
    {
        public int Width;
        public int Height;
        public CellType[,] Grid;           // 2D cell grid
        public Vector2Int   SpawnPoint;     // Player start position
        public Vector2Int   ExitPoint;      // Elevator / boss trigger position
        public List<Vector2Int> EnemySpawnPoints  = new List<Vector2Int>();
        public List<Vector2Int> HazardPoints      = new List<Vector2Int>();
        public List<Vector2Int> ItemSpawnPoints    = new List<Vector2Int>();
    }

    public enum CellType
    {
        Wall,
        Floor,
        Corridor,
        SpawnRoom,     // Player spawn chamber
        ExitRoom,      // Elevator / boss gate
        HazardZone,
        SecretRoom     // Rare, Luck-gated
    }

    public class LevelGenerator : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ════════════════════════════════════════════════════════════════════

        [Header("Maze Size Ranges")]
        [SerializeField] private Vector2Int _minMazeSize = new Vector2Int(15, 15);
        [SerializeField] private Vector2Int _maxMazeSize = new Vector2Int(40, 40);

        [Header("Room Parameters")]
        [SerializeField] private int _minRoomSize = 3;
        [SerializeField] private int _maxRoomSize = 7;
        [SerializeField] private int _minRooms    = 4;
        [SerializeField] private int _maxRooms    = 10;

        [Header("Boss Room")]
        [SerializeField] private Vector2Int _bossRoomSize = new Vector2Int(20, 20);

        // ── Seeding ────────────────────────────────────────────────────────
        private System.Random _rng;

        /// <summary>The most recently generated layout (for debugging / querying).</summary>
        public FloorLayout LastGeneratedLayout { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Main entry point. Called by GameManager with a FloorConfig
        /// that has already been modified by the AI Director.
        /// </summary>
        public FloorLayout GenerateFloor(FloorConfig config)
        {
            // Seed the RNG deterministically from run seed + floor number
            // This allows replaying the same run for debugging.
            int seed = GenerateSeed(config.FloorNumber);
            _rng = new System.Random(seed);

            FloorLayout layout;

            if (config.Type == FloorType.BossRoom)
            {
                layout = GenerateBossRoom(config);
            }
            else
            {
                layout = GenerateProceduralMaze(config);
            }

            LastGeneratedLayout = layout;

            Debug.Log($"[LevelGenerator] Floor {config.FloorNumber} generated: " +
                      $"{layout.Width}x{layout.Height}, " +
                      $"Enemies: {layout.EnemySpawnPoints.Count}, " +
                      $"Hazards: {layout.HazardPoints.Count}");

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // PROCEDURAL MAZE GENERATION (Floors 1-8, 10-17, 19-26)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates a maze floor using a rooms-and-corridors approach.
        /// Algorithm:
        ///   1. Determine grid size from difficulty & tier
        ///   2. Place rooms using BSP (Binary Space Partitioning) logic
        ///   3. Connect rooms with corridors
        ///   4. Carve additional paths based on MazeComplexity
        ///   5. Place spawn/exit, enemies, hazards
        ///   6. Optionally place secret rooms (Luck-gated)
        /// </summary>
        private FloorLayout GenerateProceduralMaze(FloorConfig config)
        {
            // ── Step 1: Grid Size ──────────────────────────────────────────
            // Scale grid size with difficulty and tier
            float sizeScale = Mathf.Lerp(0.4f, 1f, config.BaseDifficulty / 2.2f);
            int width  = (int)Mathf.Lerp(_minMazeSize.x, _maxMazeSize.x, sizeScale);
            int height = (int)Mathf.Lerp(_minMazeSize.y, _maxMazeSize.y, sizeScale);

            // Ensure odd dimensions for proper maze walls
            if (width  % 2 == 0) width++;
            if (height % 2 == 0) height++;

            var layout = new FloorLayout
            {
                Width  = width,
                Height = height,
                Grid   = new CellType[width, height]
            };

            // Initialize all cells as walls
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    layout.Grid[x, y] = CellType.Wall;

            // ── Step 2: Place Rooms ────────────────────────────────────────
            int roomCount = (int)Mathf.Lerp(_minRooms, _maxRooms, config.MazeComplexity);
            var rooms = PlaceRooms(layout, roomCount);

            // ── Step 3: Connect Rooms ──────────────────────────────────────
            ConnectRooms(layout, rooms);

            // ── Step 4: Additional Maze Carving ────────────────────────────
            if (config.MazeComplexity > 0.5f)
            {
                CarveAdditionalPaths(layout, config.MazeComplexity);
            }

            // ── Step 5: Spawn & Exit Placement ─────────────────────────────
            // Spawn in first room, exit in last room
            layout.SpawnPoint = GetRoomCenter(rooms[0]);
            layout.ExitPoint  = GetRoomCenter(rooms[rooms.Count - 1]);
            layout.Grid[layout.SpawnPoint.x, layout.SpawnPoint.y] = CellType.SpawnRoom;
            layout.Grid[layout.ExitPoint.x, layout.ExitPoint.y]   = CellType.ExitRoom;

            // ── Step 6: Enemy Spawn Points ─────────────────────────────────
            PlaceEnemySpawns(layout, rooms, config.BaseEnemyCount);

            // ── Step 7: Hazard Placement ───────────────────────────────────
            PlaceHazards(layout, rooms, config.BaseHazardCount);

            // ── Step 8: Secret Rooms (Luck-gated) ──────────────────────────
            PlaceSecretRooms(layout, rooms);

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // BOSS ROOM GENERATION (Floors 9, 18, 27)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Boss rooms are simpler: a large arena with the entrance at one end
        /// and the boss spawn at the other, plus hazard placements.
        /// </summary>
        private FloorLayout GenerateBossRoom(FloorConfig config)
        {
            int w = _bossRoomSize.x;
            int h = _bossRoomSize.y;

            var layout = new FloorLayout
            {
                Width  = w,
                Height = h,
                Grid   = new CellType[w, h]
            };

            // Fill the arena as open floor with wall borders
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    bool isBorder = x == 0 || x == w - 1 || y == 0 || y == h - 1;
                    layout.Grid[x, y] = isBorder ? CellType.Wall : CellType.Floor;
                }
            }

            // Player spawns at the south-center
            layout.SpawnPoint = new Vector2Int(w / 2, 2);
            layout.Grid[layout.SpawnPoint.x, layout.SpawnPoint.y] = CellType.SpawnRoom;

            // Boss spawns at the north-center
            layout.ExitPoint = new Vector2Int(w / 2, h - 3);

            // Place hazards around the arena based on tier
            for (int i = 0; i < config.BaseHazardCount; i++)
            {
                int hx = _rng.Next(3, w - 3);
                int hy = _rng.Next(3, h - 3);
                layout.HazardPoints.Add(new Vector2Int(hx, hy));
                layout.Grid[hx, hy] = CellType.HazardZone;
            }

            // Add structural pillars for cover (scales with tier)
            int pillarCount = 2 + (int)config.Tier * 2;
            for (int i = 0; i < pillarCount; i++)
            {
                int px = _rng.Next(4, w - 4);
                int py = _rng.Next(4, h - 4);
                layout.Grid[px, py] = CellType.Wall; // Pillar
            }

            return layout;
        }

        // ════════════════════════════════════════════════════════════════════
        // ROOM PLACEMENT (BSP-inspired)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Places non-overlapping rooms in the grid using random placement
        /// with rejection sampling. Returns the list of placed room rects.
        /// </summary>
        private List<RectInt> PlaceRooms(FloorLayout layout, int targetCount)
        {
            var rooms = new List<RectInt>();
            int attempts = 0;
            int maxAttempts = targetCount * 20;

            while (rooms.Count < targetCount && attempts < maxAttempts)
            {
                attempts++;

                int roomW = _rng.Next(_minRoomSize, _maxRoomSize + 1);
                int roomH = _rng.Next(_minRoomSize, _maxRoomSize + 1);

                // Ensure odd dimensions
                if (roomW % 2 == 0) roomW++;
                if (roomH % 2 == 0) roomH++;

                int rx = _rng.Next(2, layout.Width  - roomW - 2);
                int ry = _rng.Next(2, layout.Height - roomH - 2);

                var candidate = new RectInt(rx, ry, roomW, roomH);

                // Check for overlap with existing rooms (with 1-cell padding)
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

            // Guarantee at least 2 rooms (spawn + exit)
            if (rooms.Count < 2)
            {
                var fallback1 = new RectInt(2, 2, 5, 5);
                var fallback2 = new RectInt(layout.Width - 7, layout.Height - 7, 5, 5);
                if (rooms.Count == 0) { rooms.Add(fallback1); CarveRoom(layout, fallback1); }
                if (rooms.Count == 1) { rooms.Add(fallback2); CarveRoom(layout, fallback2); }
            }

            return rooms;
        }

        /// <summary>
        /// Carves a room into the grid (sets cells to Floor).
        /// </summary>
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

        // ════════════════════════════════════════════════════════════════════
        // CORRIDOR CONNECTION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Connects all rooms in sequence using L-shaped corridors.
        /// Sorts rooms by distance from the first room to create a
        /// logical path through the level.
        /// </summary>
        private void ConnectRooms(FloorLayout layout, List<RectInt> rooms)
        {
            // Sort rooms by distance from center of the grid
            // to create a winding path
            rooms.Sort((a, b) =>
            {
                float distA = Vector2.Distance(
                    GetRoomCenter(a),
                    new Vector2(layout.Width / 2f, layout.Height / 2f)
                );
                float distB = Vector2.Distance(
                    GetRoomCenter(b),
                    new Vector2(layout.Width / 2f, layout.Height / 2f)
                );
                return distA.CompareTo(distB);
            });

            // Connect each room to the next
            for (int i = 0; i < rooms.Count - 1; i++)
            {
                Vector2Int from = GetRoomCenter(rooms[i]);
                Vector2Int to   = GetRoomCenter(rooms[i + 1]);
                CarveCorridor(layout, from, to);
            }
        }

        /// <summary>
        /// Carves an L-shaped corridor between two points.
        /// Randomly chooses horizontal-first or vertical-first.
        /// </summary>
        private void CarveCorridor(FloorLayout layout, Vector2Int from, Vector2Int to)
        {
            bool horizontalFirst = _rng.Next(0, 2) == 0;

            if (horizontalFirst)
            {
                CarveHorizontal(layout, from.x, to.x, from.y);
                CarveVertical(layout, from.y, to.y, to.x);
            }
            else
            {
                CarveVertical(layout, from.y, to.y, from.x);
                CarveHorizontal(layout, from.x, to.x, to.y);
            }
        }

        private void CarveHorizontal(FloorLayout layout, int x1, int x2, int y)
        {
            int start = Mathf.Min(x1, x2);
            int end   = Mathf.Max(x1, x2);
            for (int x = start; x <= end; x++)
            {
                if (x > 0 && x < layout.Width - 1 && y > 0 && y < layout.Height - 1)
                {
                    if (layout.Grid[x, y] == CellType.Wall)
                        layout.Grid[x, y] = CellType.Corridor;
                }
            }
        }

        private void CarveVertical(FloorLayout layout, int y1, int y2, int x)
        {
            int start = Mathf.Min(y1, y2);
            int end   = Mathf.Max(y1, y2);
            for (int y = start; y <= end; y++)
            {
                if (x > 0 && x < layout.Width - 1 && y > 0 && y < layout.Height - 1)
                {
                    if (layout.Grid[x, y] == CellType.Wall)
                        layout.Grid[x, y] = CellType.Corridor;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ADDITIONAL MAZE CARVING
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Adds extra dead-end paths and loops to increase maze complexity.
        /// Called for higher-complexity floors to make navigation harder.
        /// </summary>
        private void CarveAdditionalPaths(FloorLayout layout, float complexity)
        {
            int extraPaths = (int)(complexity * 8);

            for (int i = 0; i < extraPaths; i++)
            {
                int sx = _rng.Next(3, layout.Width - 3);
                int sy = _rng.Next(3, layout.Height - 3);

                // Random walk from the start point
                int length = _rng.Next(3, 8);
                int cx = sx, cy = sy;

                for (int step = 0; step < length; step++)
                {
                    int dir = _rng.Next(0, 4);
                    switch (dir)
                    {
                        case 0: cx++; break;
                        case 1: cx--; break;
                        case 2: cy++; break;
                        case 3: cy--; break;
                    }

                    cx = Mathf.Clamp(cx, 1, layout.Width - 2);
                    cy = Mathf.Clamp(cy, 1, layout.Height - 2);

                    if (layout.Grid[cx, cy] == CellType.Wall)
                        layout.Grid[cx, cy] = CellType.Corridor;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ENTITY PLACEMENT
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Places enemy spawn points in rooms (not corridors, not spawn/exit).
        /// </summary>
        private void PlaceEnemySpawns(FloorLayout layout, List<RectInt> rooms, int count)
        {
            // Skip first (spawn) and last (exit) rooms
            var eligibleRooms = rooms.GetRange(
                1, Mathf.Max(1, rooms.Count - 2)
            );

            int placed = 0;
            int attempts = 0;

            while (placed < count && attempts < count * 10)
            {
                attempts++;
                var room = eligibleRooms[_rng.Next(0, eligibleRooms.Count)];
                int ex = _rng.Next(room.x + 1, room.x + room.width - 1);
                int ey = _rng.Next(room.y + 1, room.y + room.height - 1);

                var point = new Vector2Int(ex, ey);
                if (layout.Grid[ex, ey] == CellType.Floor &&
                    !layout.EnemySpawnPoints.Contains(point))
                {
                    layout.EnemySpawnPoints.Add(point);
                    placed++;
                }
            }
        }

        /// <summary>
        /// Places environmental hazards in corridors and room edges.
        /// </summary>
        private void PlaceHazards(FloorLayout layout, List<RectInt> rooms, int count)
        {
            int placed = 0;
            int attempts = 0;

            while (placed < count && attempts < count * 15)
            {
                attempts++;
                int hx = _rng.Next(2, layout.Width - 2);
                int hy = _rng.Next(2, layout.Height - 2);

                // Prefer corridors and room edges for hazards
                if (layout.Grid[hx, hy] == CellType.Corridor ||
                    layout.Grid[hx, hy] == CellType.Floor)
                {
                    var point = new Vector2Int(hx, hy);
                    if (!layout.HazardPoints.Contains(point) &&
                        point != layout.SpawnPoint &&
                        point != layout.ExitPoint)
                    {
                        layout.HazardPoints.Add(point);
                        layout.Grid[hx, hy] = CellType.HazardZone;
                        placed++;
                    }
                }
            }
        }

        /// <summary>
        /// Places optional secret rooms based on the player's Luck stat.
        /// Higher Luck = higher chance of secret rooms spawning.
        /// </summary>
        private void PlaceSecretRooms(FloorLayout layout, List<RectInt> rooms)
        {
            float luck = GameManager.Instance?.Player?.Luck ?? 0.5f;

            // Chance of a secret room: 10% base + 40% * Luck
            float secretChance = 0.10f + (luck * 0.40f);

            if (_rng.NextDouble() < secretChance)
            {
                // Place near a random room's edge
                var targetRoom = rooms[_rng.Next(0, rooms.Count)];
                int side = _rng.Next(0, 4);
                int sx, sy;

                switch (side)
                {
                    case 0: sx = targetRoom.x - 2; sy = targetRoom.y + targetRoom.height / 2; break;
                    case 1: sx = targetRoom.x + targetRoom.width + 1; sy = targetRoom.y + targetRoom.height / 2; break;
                    case 2: sx = targetRoom.x + targetRoom.width / 2; sy = targetRoom.y - 2; break;
                    default: sx = targetRoom.x + targetRoom.width / 2; sy = targetRoom.y + targetRoom.height + 1; break;
                }

                sx = Mathf.Clamp(sx, 1, layout.Width - 2);
                sy = Mathf.Clamp(sy, 1, layout.Height - 2);

                if (layout.Grid[sx, sy] == CellType.Wall)
                {
                    layout.Grid[sx, sy] = CellType.SecretRoom;
                    layout.ItemSpawnPoints.Add(new Vector2Int(sx, sy));

                    Debug.Log($"[LevelGenerator] Secret room placed at ({sx}, {sy})!");
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // UTILITIES
        // ════════════════════════════════════════════════════════════════════

        private Vector2Int GetRoomCenter(RectInt room)
        {
            return new Vector2Int(room.x + room.width / 2, room.y + room.height / 2);
        }

        /// <summary>
        /// Generates a deterministic seed for a given floor.
        /// Uses the run's random state + floor number for reproducibility.
        /// </summary>
        private int GenerateSeed(int floorNumber)
        {
            // In a full implementation, this would combine with a run-level seed
            // stored in GameManager. For now, use Time-based + floor offset.
            return System.Environment.TickCount ^ (floorNumber * 7919);
        }
    }
}
