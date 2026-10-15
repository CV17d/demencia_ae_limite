// ============================================================================
// LevelBuilder.cs — 30-Floor Pre-Generation Tower Builder & Geometry Assembler
// ============================================================================
// Constructs all 30 floors of the tower at Start() with 100m vertical separation:
//   - Floor 1 at Y = 0 (Tutorial Lobby)
//   - Floors 2-9 at Y = 100..800 (Roguelike loot & combat floors)
//   - Floor 10 at Y = 900 (Boss 1: El Carnicero)
//   - Floors 11-19 at Y = 1000..1800 (Act 2 Roguelike floors)
//   - Floor 20 at Y = 1900 (Boss 2: La Maquina)
//   - Floors 21-29 at Y = 2000..2800 (Act 3 Roguelike floors)
//   - Floor 30 at Y = 2900 (Final Boss: Demencia Incarnate)
//
// Void Gap Elimination:
//   - Overlapping floor tiles (_cellSize + 0.12m) to eliminate seam glitches.
//   - Continuous structural sub-floor foundation slab under every procedural floor.
//   - Overlapping wall blocks (_cellSize + 0.08m) sealing all corner gaps.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Level
{
    public class LevelBuilder : MonoBehaviour
    {
        [Header("Grid Metric Scaling")]
        [SerializeField] private float _cellSize = LevelGenerator.StandardCellSize;
        [SerializeField] private float _wallHeight = LevelGenerator.StandardWallHeight;
        [SerializeField] private float _ceilingHeight = LevelGenerator.StandardCeilingHeight;

        public float CellSize => _cellSize;
        public float WallHeight => _wallHeight;
        public float CeilingHeight => _ceilingHeight;

        [Header("Optional Modular Prefabs (Falls back to Primitives)")]
        [SerializeField] private GameObject _wallPrefab;
        [SerializeField] private GameObject _floorPrefab;
        [SerializeField] private GameObject _ceilingPrefab;
        [SerializeField] private GameObject _exteriorWallPrefab;
        [SerializeField] private GameObject _windowPrefab;
        [SerializeField] private GameObject _merchantCounterPrefab;
        [SerializeField] private GameObject _elevatorCabinPrefab;

        [Header("Optional Materials")]
        [SerializeField] private Material _floorMaterial;
        [SerializeField] private Material _safeRoomFloorMaterial;
        [SerializeField] private Material _ceilingMaterial;
        [SerializeField] private Material _interiorWallMaterial;
        [SerializeField] private Material _exteriorWallMaterial;
        [SerializeField] private Material _windowMaterial;
        [SerializeField] private Material _merchantCounterMaterial;

        [Header("Spawners")]
        [SerializeField] private EnemySpawner _enemySpawner;
        [SerializeField] private HazardSpawner _hazardSpawner;

        private Transform _towerContainer;
        private readonly Dictionary<int, Vector3> _floorSpawnPositions = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, GameObject> _floorRoots = new Dictionary<int, GameObject>();
        private bool _isTowerPreGenerated = false;

        public bool IsTowerPreGenerated => _isTowerPreGenerated;

        private void Awake()
        {
            EnsureTowerContainer();
            ResolveSpawners();
        }

        private void Start()
        {
            if (!_isTowerPreGenerated)
            {
                PreGenerateEntireTower();
            }
        }

        private void EnsureTowerContainer()
        {
            if (_towerContainer == null)
            {
                var existing = transform.Find("[30_FLOOR_TOWER]");
                if (existing != null)
                {
                    _towerContainer = existing;
                }
                else
                {
                    var go = new GameObject("[30_FLOOR_TOWER]");
                    go.transform.SetParent(transform, false);
                    _towerContainer = go.transform;
                }
            }
        }

        private void ResolveSpawners()
        {
            if (_enemySpawner == null)
            {
                _enemySpawner = GetComponent<EnemySpawner>() ??
                                GetComponentInChildren<EnemySpawner>() ??
                                FindAnyObjectByType<EnemySpawner>();
            }

            if (_hazardSpawner == null)
            {
                _hazardSpawner = GetComponent<HazardSpawner>() ??
                                 GetComponentInChildren<HazardSpawner>() ??
                                 FindAnyObjectByType<HazardSpawner>();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 30-FLOOR TOWER PRE-GENERATION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Pre-generates all 30 floors of the skyscraper in a single loop at Start().
        /// Each floor is placed with 100 units of vertical spacing (Y = (N-1) * 100).
        /// </summary>
        [ContextMenu("Pre-Generate 30-Floor Tower")]
        public void PreGenerateEntireTower()
        {
            if (_isTowerPreGenerated) return;

            EnsureTowerContainer();
            ResolveSpawners();

            var levelGen = FindAnyObjectByType<LevelGenerator>();
            if (levelGen == null)
            {
                Debug.LogError("[LevelBuilder] Critical: LevelGenerator missing! Cannot pre-generate tower.");
                return;
            }

            Debug.Log("<color=cyan>[LevelBuilder] Pre-generating entire 30-Floor Skyscraper Tower...</color>");

            for (int floorNum = 1; floorNum <= FloorConfig.MAX_TOWER_FLOORS; floorNum++)
            {
                FloorConfig config = FloorConfig.Build(floorNum);
                FloorLayout layout = levelGen.GenerateFloor(config);

                BuildFloorInTowerStack(floorNum, layout, config);
            }

            _isTowerPreGenerated = true;
            Debug.Log("<color=green>[LevelBuilder] All 30 floors successfully pre-generated in vertical space! Zero-void gaps guaranteed.</color>");
        }

        private void BuildFloorInTowerStack(int floorNumber, FloorLayout layout, FloorConfig config)
        {
            float floorElevation = FloorConfig.GetFloorElevation(floorNumber);

            var floorGo = new GameObject($"Floor_{floorNumber:D2}_{(config.Type == FloorType.BossRoom ? "BOSS_ROOM" : "NORMAL")}");
            floorGo.transform.SetParent(_towerContainer, false);
            floorGo.transform.position = new Vector3(0f, floorElevation, 0f);
            _floorRoots[floorNumber] = floorGo;

            Transform floorTransform = floorGo.transform;

            // 1. Continuous Sub-Floor Foundation Slab (Mathematically guarantees player cannot fall into the void)
            CreateContinuousSubFloorFoundation(layout, floorElevation, floorTransform);

            // 2. Grid Geometry with Overlapping Tiles (Zero physical gaps)
            ConstructGridGeometry(layout, floorElevation, floorTransform);

            // 3. Register Predefined Start Room World Position
            Vector3 spawnWorldPos = new Vector3(
                layout.SpawnPoint.x * _cellSize,
                floorElevation + 1.0f,
                layout.SpawnPoint.y * _cellSize
            );
            _floorSpawnPositions[floorNumber] = spawnWorldPos;

            // 4. Place Elevator Teleporter Cabin
            PlaceElevatorCabin(floorNumber, layout.ExitPoint, floorElevation, floorTransform, config.Type == FloorType.BossRoom);

            // 5. Populate Entities & Hazards at Floor Elevation
            if (config.Type != FloorType.BossRoom && floorNumber > 1)
            {
                if (_enemySpawner != null)
                {
                    _enemySpawner.SpawnEnemiesAtElevation(layout, config, floorElevation, floorTransform);
                }
            }

            if (_hazardSpawner != null)
            {
                _hazardSpawner.SpawnHazardsAtElevation(layout, config, floorElevation, floorTransform);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // VOID GAP ELIMINATION & PHYSICAL FOUNDATION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a monolithic sub-floor safety slab beneath the entire floor layout.
        /// Prevents edge-clip or corner-drop void falls.
        /// </summary>
        private void CreateContinuousSubFloorFoundation(FloorLayout layout, float elevation, Transform parent)
        {
            var foundation = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foundation.name = "[Continuous_SubFloor_Foundation]";
            
            float totalWidth = layout.Width * _cellSize + 4.0f;
            float totalLength = layout.Height * _cellSize + 4.0f;
            Vector3 center = new Vector3((layout.Width * _cellSize) * 0.5f, elevation - 0.25f, (layout.Height * _cellSize) * 0.5f);

            foundation.transform.position = center;
            foundation.transform.localScale = new Vector3(totalWidth, 0.4f, totalLength);
            foundation.transform.SetParent(parent, true);

            foundation.layer = LayerMask.NameToLayer("Default");

            var rend = foundation.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material.color = new Color(0.08f, 0.08f, 0.10f);
            }
        }

        private void ConstructGridGeometry(FloorLayout layout, float elevation, Transform parent)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    CellType cell = layout.Grid[x, y];
                    Vector3 worldPos = new Vector3(x * _cellSize, elevation, y * _cellSize);

                    switch (cell)
                    {
                        case CellType.Floor:
                        case CellType.Corridor:
                        case CellType.SpawnRoom:
                        case CellType.ExitRoom:
                        case CellType.HazardZone:
                        case CellType.SecretRoom:
                            SpawnFloorTile(worldPos, parent, false);
                            SpawnCeilingTile(worldPos, elevation, parent);
                            break;

                        case CellType.SafeRoom:
                            SpawnFloorTile(worldPos, parent, true);
                            SpawnCeilingTile(worldPos, elevation, parent);
                            break;

                        case CellType.ExteriorWall:
                            SpawnExteriorWall(worldPos, parent);
                            break;

                        case CellType.ExteriorWindow:
                            SpawnExteriorWindow(worldPos, x, y, layout.Width, layout.Height, parent);
                            break;

                        case CellType.Wall:
                        default:
                            SpawnInteriorWall(worldPos, parent);
                            break;
                    }
                }
            }
        }

        private void SpawnFloorTile(Vector3 position, Transform parent, bool isSafeRoom)
        {
            GameObject floor;
            if (_floorPrefab != null && !isSafeRoom)
            {
                floor = Instantiate(_floorPrefab, position, Quaternion.identity, parent);
            }
            else
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = isSafeRoom ? "SafeRoom_Floor_Tile" : "Floor_Tile";
                // 12cm overlap prevents any floating-point gap between cubes
                floor.transform.position = position + Vector3.down * 0.15f;
                floor.transform.localScale = new Vector3(_cellSize + 0.12f, 0.3f, _cellSize + 0.12f);
                floor.transform.SetParent(parent, true);
                floor.layer = LayerMask.NameToLayer("Default");

                var rend = floor.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (isSafeRoom && _safeRoomFloorMaterial != null) rend.material = _safeRoomFloorMaterial;
                    else if (_floorMaterial != null) rend.material = _floorMaterial;
                    else rend.material.color = isSafeRoom ? new Color(0.20f, 0.22f, 0.25f) : new Color(0.14f, 0.15f, 0.17f);
                }
            }
        }

        private void SpawnCeilingTile(Vector3 position, float elevation, Transform parent)
        {
            GameObject ceiling;
            if (_ceilingPrefab != null)
            {
                ceiling = Instantiate(_ceilingPrefab, position + Vector3.up * _ceilingHeight, Quaternion.identity, parent);
            }
            else
            {
                ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ceiling.name = "Ceiling_Tile";
                ceiling.transform.position = position + Vector3.up * (_ceilingHeight + 0.15f);
                ceiling.transform.localScale = new Vector3(_cellSize + 0.12f, 0.3f, _cellSize + 0.12f);
                ceiling.transform.SetParent(parent, true);
                ceiling.layer = LayerMask.NameToLayer("Default");

                var rend = ceiling.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_ceilingMaterial != null) rend.material = _ceilingMaterial;
                    else rend.material.color = new Color(0.18f, 0.19f, 0.22f);
                }
            }
        }

        private void SpawnInteriorWall(Vector3 position, Transform parent)
        {
            GameObject wall;
            if (_wallPrefab != null)
            {
                wall = Instantiate(_wallPrefab, position + Vector3.up * (_wallHeight * 0.5f), Quaternion.identity, parent);
            }
            else
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Interior_Wall";
                wall.transform.position = position + Vector3.up * (_wallHeight * 0.5f);
                // 8cm overlap seals corners with zero gap
                wall.transform.localScale = new Vector3(_cellSize + 0.08f, _wallHeight, _cellSize + 0.08f);
                wall.transform.SetParent(parent, true);
                wall.layer = LayerMask.NameToLayer("Default");

                var rend = wall.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_interiorWallMaterial != null) rend.material = _interiorWallMaterial;
                    else rend.material.color = new Color(0.26f, 0.28f, 0.32f);
                }
            }
        }

        private void SpawnExteriorWall(Vector3 position, Transform parent)
        {
            GameObject extWall;
            if (_exteriorWallPrefab != null)
            {
                extWall = Instantiate(_exteriorWallPrefab, position + Vector3.up * (_wallHeight * 0.5f), Quaternion.identity, parent);
            }
            else
            {
                extWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                extWall.name = "Exterior_Perimeter_Wall";
                extWall.transform.position = position + Vector3.up * (_wallHeight * 0.5f);
                extWall.transform.localScale = new Vector3(_cellSize + 0.08f, _wallHeight, _cellSize + 0.08f);
                extWall.transform.SetParent(parent, true);
                extWall.layer = LayerMask.NameToLayer("Default");

                var rend = extWall.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_exteriorWallMaterial != null) rend.material = _exteriorWallMaterial;
                    else rend.material.color = new Color(0.12f, 0.13f, 0.15f);
                }
            }
        }

        private void SpawnExteriorWindow(Vector3 position, int x, int y, int width, int height, Transform parent)
        {
            if (_windowPrefab != null)
            {
                var win = Instantiate(_windowPrefab, position, Quaternion.identity, parent);
                return;
            }

            bool isHorizontalEdge = (y == 0 || y == height - 1);
            Vector3 sillScale = isHorizontalEdge
                ? new Vector3(_cellSize + 0.08f, 0.9f, _cellSize * 0.35f)
                : new Vector3(_cellSize * 0.35f, 0.9f, _cellSize + 0.08f);

            Vector3 lintelScale = isHorizontalEdge
                ? new Vector3(_cellSize + 0.08f, 0.7f, _cellSize * 0.35f)
                : new Vector3(_cellSize * 0.35f, 0.7f, _cellSize + 0.08f);

            Vector3 glassScale = isHorizontalEdge
                ? new Vector3(_cellSize, _wallHeight - 1.6f, 0.12f)
                : new Vector3(0.12f, _wallHeight - 1.6f, _cellSize);

            // Lower Wall Sill
            var sill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sill.name = "Window_Sill";
            sill.transform.position = position + Vector3.up * 0.45f;
            sill.transform.localScale = sillScale;
            sill.transform.SetParent(parent, true);
            var rendSill = sill.GetComponent<Renderer>();
            if (rendSill != null) rendSill.material.color = new Color(0.2f, 0.22f, 0.25f);

            // Upper Lintel
            float lintelH = 0.7f;
            float lintelCenterY = _wallHeight - (lintelH * 0.5f);
            var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lintel.name = "Window_Lintel";
            lintel.transform.position = position + Vector3.up * lintelCenterY;
            lintel.transform.localScale = lintelScale;
            lintel.transform.SetParent(parent, true);
            var rendLintel = lintel.GetComponent<Renderer>();
            if (rendLintel != null) rendLintel.material.color = new Color(0.2f, 0.22f, 0.25f);

            // Panoramic Glass Pane
            float glassCenterY = 0.9f + (_wallHeight - 1.6f) * 0.5f;
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "Window_Glass_Exterior";
            glass.transform.position = position + Vector3.up * glassCenterY;
            glass.transform.localScale = glassScale;
            glass.transform.SetParent(parent, true);

            var rendGlass = glass.GetComponent<Renderer>();
            if (rendGlass != null)
            {
                if (_windowMaterial != null) rendGlass.material = _windowMaterial;
                else rendGlass.material.color = new Color(0.35f, 0.65f, 0.85f, 0.45f);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ELEVATOR TELEPORTER CABIN
        // ════════════════════════════════════════════════════════════════════

        private void PlaceElevatorCabin(int currentFloor, Vector2Int exitGrid, float elevation, Transform parent, bool isBossFloor)
        {
            Vector3 exitPos = new Vector3(exitGrid.x * _cellSize, elevation, exitGrid.y * _cellSize);

            // Illuminated Yellow Elevator Posts
            var frameL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frameL.name = "Elevator_Pillar_L";
            frameL.transform.position = exitPos + Vector3.up * 1.6f + Vector3.left * 1.3f;
            frameL.transform.localScale = new Vector3(0.35f, 3.2f, 0.35f);
            frameL.transform.SetParent(parent, true);
            var rendFL = frameL.GetComponent<Renderer>();
            if (rendFL != null) rendFL.material.color = new Color(0.92f, 0.78f, 0.1f);

            var frameR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frameR.name = "Elevator_Pillar_R";
            frameR.transform.position = exitPos + Vector3.up * 1.6f + Vector3.right * 1.3f;
            frameR.transform.localScale = new Vector3(0.35f, 3.2f, 0.35f);
            frameR.transform.SetParent(parent, true);
            var rendFR = frameR.GetComponent<Renderer>();
            if (rendFR != null) rendFR.material.color = new Color(0.92f, 0.78f, 0.1f);

            // Overhead Sign
            var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sign.name = "Elevator_Header_Sign";
            sign.transform.position = exitPos + Vector3.up * 3.2f;
            sign.transform.localScale = new Vector3(2.8f, 0.5f, 0.4f);
            sign.transform.SetParent(parent, true);
            var rendSign = sign.GetComponent<Renderer>();
            if (rendSign != null) rendSign.material.color = new Color(0.1f, 0.85f, 0.3f);

            // Beacon Point Light
            var beaconGo = new GameObject("Elevator_Beacon_Light");
            beaconGo.transform.position = exitPos + Vector3.up * 2.2f;
            beaconGo.transform.SetParent(parent, true);
            var pLight = beaconGo.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.color = new Color(0.2f, 1.0f, 0.4f);
            pLight.range = 8.0f;
            pLight.intensity = 2.0f;

            // Cabin Trigger Volume (Elevator Door Teleporter)
            GameObject cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = isBossFloor ? $"Boss_Exit_Gate_F{currentFloor}" : $"Elevator_Cabin_F{currentFloor}";
            cabin.transform.position = exitPos + Vector3.up * 1.5f;
            cabin.transform.localScale = new Vector3(2.4f, 3.0f, 2.0f);
            cabin.transform.SetParent(parent, true);

            var rendCabin = cabin.GetComponent<Renderer>();
            if (rendCabin != null) rendCabin.material.color = new Color(0.18f, 0.20f, 0.24f);

            var col = cabin.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            var trigger = cabin.AddComponent<ElevatorDoorTrigger>();
            trigger.SetFloorNumbers(currentFloor, currentFloor + 1);
        }

        // ════════════════════════════════════════════════════════════════════
        // SPAWN COORDINATE LOOKUP
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retrieves the pre-calculated world spawn position for any of the 30 floors.
        /// </summary>
        public Vector3 GetFloorSpawnPosition(int floorNumber)
        {
            if (_floorSpawnPositions.TryGetValue(floorNumber, out Vector3 pos))
            {
                return pos;
            }

            // Fallback: 100m elevation calculation
            float elevation = FloorConfig.GetFloorElevation(floorNumber);
            return new Vector3(10f * _cellSize, elevation + 1.0f, 10f * _cellSize);
        }

        public GameObject GetFloorRoot(int floorNumber)
        {
            if (_floorRoots.TryGetValue(floorNumber, out GameObject root))
            {
                return root;
            }
            return null;
        }
    }
}
