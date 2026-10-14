// ============================================================================
// LevelBuilder.cs — Physical construction of procedural tower levels
// ============================================================================
// Takes the abstract FloorLayout from LevelGenerator and constructs the
// 3D world:
//   - Solid indoor floors and ceilings for realistic enclosed rooms
//   - Interior partition walls
//   - Exterior perimeter walls and panoramic exterior windows
//   - Places player at spawn chamber and elevator exit gate
//   - Explicitly triggers EnemySpawner ONLY AFTER all geometry is fully instantiated
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;
using DemenciaNoLimite.Player;

namespace DemenciaNoLimite.Level
{
    public class LevelBuilder : MonoBehaviour
    {
        [Header("Grid Scaling")]
        [SerializeField] private float _cellSize = LevelGenerator.StandardCellSize;
        [SerializeField] private float _wallHeight = LevelGenerator.StandardWallHeight;
        [SerializeField] private float _ceilingHeight = LevelGenerator.StandardCeilingHeight;

        public float CellSize => _cellSize;
        public float WallHeight => _wallHeight;
        public float CeilingHeight => _ceilingHeight;

        [Header("Modular Prefabs (Optional Fallbacks to Primitives)")]
        [SerializeField] private GameObject _wallPrefab;
        [SerializeField] private GameObject _floorPrefab;
        [SerializeField] private GameObject _ceilingPrefab;
        [SerializeField] private GameObject _exteriorWallPrefab;
        [SerializeField] private GameObject _windowPrefab;
        [SerializeField] private GameObject _exitDoorPrefab;

        [Header("Materials (Optional)")]
        [SerializeField] private Material _floorMaterial;
        [SerializeField] private Material _ceilingMaterial;
        [SerializeField] private Material _interiorWallMaterial;
        [SerializeField] private Material _exteriorWallMaterial;
        [SerializeField] private Material _windowMaterial;

        [Header("Spawners")]
        [SerializeField] private EnemySpawner _enemySpawner;
        [SerializeField] private HazardSpawner _hazardSpawner;

        private readonly List<GameObject> _instantiatedLevelObjects = new List<GameObject>();
        private Transform _levelContainer;

        private void Awake()
        {
            EnsureContainer();
            ResolveSpawners();
        }

        private void EnsureContainer()
        {
            if (_levelContainer == null)
            {
                var existing = transform.Find("[Generated_Floor_Geometry]");
                if (existing != null)
                {
                    _levelContainer = existing;
                }
                else
                {
                    _levelContainer = new GameObject("[Generated_Floor_Geometry]").transform;
                    _levelContainer.parent = transform;
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

        private void OnEnable()
        {
            GameEvents.OnFloorLoading += BuildFloor;
        }

        private void OnDisable()
        {
            GameEvents.OnFloorLoading -= BuildFloor;
        }

        public void BuildFloor(FloorConfig config)
        {
            EnsureContainer();
            ClearPreviousFloor();
            ResolveSpawners();

            var levelGen = FindAnyObjectByType<LevelGenerator>();
            if (levelGen == null)
            {
                Debug.LogError("[LevelBuilder] No LevelGenerator found in scene!");
                return;
            }

            FloorLayout layout = levelGen.GenerateFloor(config);
            if (layout == null) return;

            // ── STEP 1: Construct ALL Floor, Ceiling, Wall, and Window Geometry ──
            ConstructGridGeometry(layout);

            // ── STEP 2: Position Player at Spawn Chamber ──
            PositionPlayerAtSpawn(layout.SpawnPoint);

            // ── STEP 3: Place Exit Elevator Station ──
            PlaceExitDoor(layout.ExitPoint, config.Type == FloorType.BossRoom);

            // ── STEP 4: Instantiate Environmental Hazards ──
            if (_hazardSpawner != null)
            {
                _hazardSpawner.SpawnHazards(layout, config);
            }

            // ── STEP 5: Explicitly Trigger Enemy Spawning ONLY AFTER Geometry is Complete ──
            if (config.Type != FloorType.BossRoom)
            {
                if (_enemySpawner != null)
                {
                    _enemySpawner.SpawnEnemies(layout, config);
                }
                else
                {
                    Debug.LogError("[LevelBuilder] Critical: EnemySpawner missing during floor construction!");
                }
            }

            Debug.Log($"[LevelBuilder] Physical floor {config.FloorNumber} constructed successfully with full geometry and enemy deployment.");
        }

        private void ConstructGridGeometry(FloorLayout layout)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    CellType cell = layout.Grid[x, y];
                    Vector3 worldPos = new Vector3(x * _cellSize, 0f, y * _cellSize);

                    switch (cell)
                    {
                        case CellType.Floor:
                        case CellType.Corridor:
                        case CellType.SpawnRoom:
                        case CellType.ExitRoom:
                        case CellType.HazardZone:
                        case CellType.SecretRoom:
                            SpawnFloor(worldPos);
                            SpawnCeiling(worldPos);
                            break;

                        case CellType.ExteriorWall:
                            SpawnExteriorWall(worldPos);
                            break;

                        case CellType.ExteriorWindow:
                            SpawnExteriorWindow(worldPos, x, y, layout.Width, layout.Height);
                            break;

                        case CellType.Wall:
                        default:
                            SpawnInteriorWall(worldPos);
                            break;
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // FLOORS & CEILINGS
        // ════════════════════════════════════════════════════════════════════

        private void SpawnFloor(Vector3 position)
        {
            GameObject floor;
            if (_floorPrefab != null)
            {
                floor = Instantiate(_floorPrefab, position, Quaternion.identity, _levelContainer);
            }
            else
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Floor_Tile";
                floor.transform.position = position + Vector3.down * 0.1f;
                floor.transform.localScale = new Vector3(_cellSize, 0.2f, _cellSize);
                floor.transform.parent = _levelContainer;
                floor.layer = LayerMask.NameToLayer("Default");

                var rend = floor.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_floorMaterial != null) rend.material = _floorMaterial;
                    else rend.material.color = new Color(0.14f, 0.15f, 0.17f); // Dark industrial slate
                }
            }
            _instantiatedLevelObjects.Add(floor);
        }

        private void SpawnCeiling(Vector3 position)
        {
            GameObject ceiling;
            if (_ceilingPrefab != null)
            {
                ceiling = Instantiate(_ceilingPrefab, position + Vector3.up * _ceilingHeight, Quaternion.identity, _levelContainer);
            }
            else
            {
                ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ceiling.name = "Ceiling_Tile";
                ceiling.transform.position = position + Vector3.up * (_ceilingHeight + 0.1f);
                ceiling.transform.localScale = new Vector3(_cellSize, 0.2f, _cellSize);
                ceiling.transform.parent = _levelContainer;
                ceiling.layer = LayerMask.NameToLayer("Default");

                var rend = ceiling.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_ceilingMaterial != null) rend.material = _ceilingMaterial;
                    else rend.material.color = new Color(0.18f, 0.19f, 0.22f); // Industrial acoustic ceiling
                }
            }
            _instantiatedLevelObjects.Add(ceiling);
        }

        // ════════════════════════════════════════════════════════════════════
        // INTERIOR & EXTERIOR WALLS
        // ════════════════════════════════════════════════════════════════════

        private void SpawnInteriorWall(Vector3 position)
        {
            GameObject wall;
            if (_wallPrefab != null)
            {
                wall = Instantiate(_wallPrefab, position + Vector3.up * (_wallHeight * 0.5f), Quaternion.identity, _levelContainer);
            }
            else
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Interior_Wall";
                wall.transform.position = position + Vector3.up * (_wallHeight * 0.5f);
                wall.transform.localScale = new Vector3(_cellSize, _wallHeight, _cellSize);
                wall.transform.parent = _levelContainer;
                wall.layer = LayerMask.NameToLayer("Default");

                var rend = wall.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_interiorWallMaterial != null) rend.material = _interiorWallMaterial;
                    else rend.material.color = new Color(0.26f, 0.28f, 0.32f); // Dark interior partition concrete
                }
            }
            _instantiatedLevelObjects.Add(wall);
        }

        private void SpawnExteriorWall(Vector3 position)
        {
            GameObject extWall;
            if (_exteriorWallPrefab != null)
            {
                extWall = Instantiate(_exteriorWallPrefab, position + Vector3.up * (_wallHeight * 0.5f), Quaternion.identity, _levelContainer);
            }
            else
            {
                extWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                extWall.name = "Exterior_Perimeter_Wall";
                extWall.transform.position = position + Vector3.up * (_wallHeight * 0.5f);
                extWall.transform.localScale = new Vector3(_cellSize, _wallHeight, _cellSize);
                extWall.transform.parent = _levelContainer;
                extWall.layer = LayerMask.NameToLayer("Default");

                var rend = extWall.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_exteriorWallMaterial != null) rend.material = _exteriorWallMaterial;
                    else rend.material.color = new Color(0.12f, 0.13f, 0.15f); // Heavy reinforced facade
                }
            }
            _instantiatedLevelObjects.Add(extWall);
        }

        // ════════════════════════════════════════════════════════════════════
        // EXTERIOR WINDOWS (REALISTIC BUILDING OPENINGS)
        // ════════════════════════════════════════════════════════════════════

        private void SpawnExteriorWindow(Vector3 position, int x, int y, int width, int height)
        {
            if (_windowPrefab != null)
            {
                var win = Instantiate(_windowPrefab, position, Quaternion.identity, _levelContainer);
                _instantiatedLevelObjects.Add(win);
                return;
            }

            // Determine wall orientation (East-West vs North-South)
            bool isHorizontalEdge = (y == 0 || y == height - 1);
            Vector3 sillScale = isHorizontalEdge
                ? new Vector3(_cellSize, 0.9f, _cellSize * 0.35f)
                : new Vector3(_cellSize * 0.35f, 0.9f, _cellSize);

            Vector3 lintelScale = isHorizontalEdge
                ? new Vector3(_cellSize, 0.7f, _cellSize * 0.35f)
                : new Vector3(_cellSize * 0.35f, 0.7f, _cellSize);

            Vector3 glassScale = isHorizontalEdge
                ? new Vector3(_cellSize * 0.98f, _wallHeight - 1.6f, 0.12f)
                : new Vector3(0.12f, _wallHeight - 1.6f, _cellSize * 0.98f);

            // 1. Lower Wall Sill (Solid bottom from 0m to 0.9m)
            var sill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sill.name = "Window_Sill";
            sill.transform.position = position + Vector3.up * 0.45f;
            sill.transform.localScale = sillScale;
            sill.transform.parent = _levelContainer;
            var rendSill = sill.GetComponent<Renderer>();
            if (rendSill != null) rendSill.material.color = new Color(0.2f, 0.22f, 0.25f);
            _instantiatedLevelObjects.Add(sill);

            // 2. Upper Lintel (Solid header from 2.5m to ceiling)
            float lintelH = 0.7f;
            float lintelCenterY = _wallHeight - (lintelH * 0.5f);
            var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lintel.name = "Window_Lintel";
            lintel.transform.position = position + Vector3.up * lintelCenterY;
            lintel.transform.localScale = lintelScale;
            lintel.transform.parent = _levelContainer;
            var rendLintel = lintel.GetComponent<Renderer>();
            if (rendLintel != null) rendLintel.material.color = new Color(0.2f, 0.22f, 0.25f);
            _instantiatedLevelObjects.Add(lintel);

            // 3. Panoramic Glass Pane (Permits looking outside the skyscraper into the void)
            float glassCenterY = 0.9f + (_wallHeight - 1.6f) * 0.5f;
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "Window_Glass_Exterior";
            glass.transform.position = position + Vector3.up * glassCenterY;
            glass.transform.localScale = glassScale;
            glass.transform.parent = _levelContainer;

            var rendGlass = glass.GetComponent<Renderer>();
            if (rendGlass != null)
            {
                if (_windowMaterial != null)
                {
                    rendGlass.material = _windowMaterial;
                }
                else
                {
                    // Clean tinted glass fallback
                    rendGlass.material.color = new Color(0.35f, 0.65f, 0.85f, 0.45f);
                }
            }
            _instantiatedLevelObjects.Add(glass);
        }

        // ════════════════════════════════════════════════════════════════════
        // SPAWN POINT & EXIT ELEVATOR
        // ════════════════════════════════════════════════════════════════════

        private void PositionPlayerAtSpawn(Vector2Int spawnGrid)
        {
            Vector3 worldSpawn = new Vector3(spawnGrid.x * _cellSize, 1.0f, spawnGrid.y * _cellSize);
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                player.Teleport(worldSpawn, Quaternion.identity);
            }
        }

        private void PlaceExitDoor(Vector2Int exitGrid, bool isBossFloor)
        {
            Vector3 exitPos = new Vector3(exitGrid.x * _cellSize, 0f, exitGrid.y * _cellSize);

            // 1. Elevator frame pillars (Hazard Yellow)
            var frameL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frameL.name = "Elevator_Pillar_L";
            frameL.transform.position = exitPos + Vector3.up * 1.6f + Vector3.left * 1.3f;
            frameL.transform.localScale = new Vector3(0.35f, 3.2f, 0.35f);
            frameL.transform.parent = _levelContainer;
            var rendFL = frameL.GetComponent<Renderer>();
            if (rendFL != null) rendFL.material.color = new Color(0.92f, 0.78f, 0.1f);
            _instantiatedLevelObjects.Add(frameL);

            var frameR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frameR.name = "Elevator_Pillar_R";
            frameR.transform.position = exitPos + Vector3.up * 1.6f + Vector3.right * 1.3f;
            frameR.transform.localScale = new Vector3(0.35f, 3.2f, 0.35f);
            frameR.transform.parent = _levelContainer;
            var rendFR = frameR.GetComponent<Renderer>();
            if (rendFR != null) rendFR.material.color = new Color(0.92f, 0.78f, 0.1f);
            _instantiatedLevelObjects.Add(frameR);

            // 2. Overhead illuminated sign
            var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sign.name = "Elevator_Header_Sign";
            sign.transform.position = exitPos + Vector3.up * 3.2f;
            sign.transform.localScale = new Vector3(2.8f, 0.5f, 0.4f);
            sign.transform.parent = _levelContainer;
            var rendSign = sign.GetComponent<Renderer>();
            if (rendSign != null) rendSign.material.color = new Color(0.1f, 0.85f, 0.3f);
            _instantiatedLevelObjects.Add(sign);

            // 3. Cabin interior beacon light
            var beaconGo = new GameObject("Elevator_Beacon_Light");
            beaconGo.transform.position = exitPos + Vector3.up * 2.2f;
            beaconGo.transform.parent = _levelContainer;
            var pLight = beaconGo.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.color = new Color(0.2f, 1.0f, 0.4f);
            pLight.range = 10.0f;
            pLight.intensity = 2.5f;
            _instantiatedLevelObjects.Add(beaconGo);

            // 4. Cabin trigger volume
            GameObject cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = isBossFloor ? "Boss_Exit_Gate" : "Elevator_Cabin";
            cabin.transform.position = exitPos + Vector3.up * 1.5f;
            cabin.transform.localScale = new Vector3(2.4f, 3.0f, 2.0f);
            cabin.transform.parent = _levelContainer;

            var rendCabin = cabin.GetComponent<Renderer>();
            if (rendCabin != null) rendCabin.material.color = new Color(0.2f, 0.22f, 0.28f);

            var col = cabin.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            var trigger = cabin.AddComponent<ElevatorDoorTrigger>();
            int nextFloor = (GameManager.Instance != null && GameManager.Instance.CurrentFloor != null)
                ? GameManager.Instance.CurrentFloor.FloorNumber + 1
                : 2;
            trigger.SetNextFloorNumber(nextFloor);

            _instantiatedLevelObjects.Add(cabin);
        }

        private void ClearPreviousFloor()
        {
            foreach (var go in _instantiatedLevelObjects)
            {
                if (go != null) Destroy(go);
            }
            _instantiatedLevelObjects.Clear();
        }
    }
}
