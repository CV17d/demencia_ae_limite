// ============================================================================
// LevelBuilder.cs — Physical 3D Construction & Memory Management
// ============================================================================
// Constructs the physical 3D skyscraper geometry from the FloorLayout:
//   - Full Space Construction: Enclosed rooms, straight corridors, structural walls.
//   - Lateral Safe Room & Merchant Feature: Physical dividing wall with counter
//     sill, cutout window, service shelf, overhead warm lighting, and exit elevator.
//   - Clean Memory Management: Dedicated container GameObject that is destroyed
//     completely between floors to eliminate RAM/VRAM leaks.
//   - Safe Player Teleportation: Disables CharacterController during repositioning.
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
                    var containerGo = new GameObject("[Generated_Floor_Geometry]");
                    containerGo.transform.SetParent(transform, false);
                    _levelContainer = containerGo.transform;
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

        public void SetSpawners(EnemySpawner enemySpawner, HazardSpawner hazardSpawner)
        {
            _enemySpawner = enemySpawner;
            _hazardSpawner = hazardSpawner;
        }

        // ════════════════════════════════════════════════════════════════════
        // MEMORY MANAGEMENT: COMPLETE FLOOR CONTAINER DESTRUCTION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Completely destroys the previous floor container and all its children.
        /// Frees meshes, colliders, lights, and entity memory cleanly.
        /// </summary>
        public void TeardownFloorGeometry()
        {
            if (_enemySpawner != null)
            {
                _enemySpawner.ClearEnemies();
            }

            if (_hazardSpawner != null)
            {
                _hazardSpawner.ClearHazards();
            }

            if (_levelContainer != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_levelContainer.gameObject);
                }
                else
                {
                    DestroyImmediate(_levelContainer.gameObject);
                }
                _levelContainer = null;
            }

            _instantiatedLevelObjects.Clear();
            Debug.Log("[LevelBuilder] Previous floor container destroyed. Memory freed successfully.");
        }

        // ════════════════════════════════════════════════════════════════════
        // PHYSICAL LEVEL CONSTRUCTION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Convenience entry point to generate and construct a floor directly.
        /// </summary>
        public void BuildFloor(FloorConfig config)
        {
            var levelGen = FindAnyObjectByType<LevelGenerator>();
            if (levelGen != null)
            {
                FloorLayout layout = levelGen.GenerateFloor(config);
                BuildFloorFromLayout(layout, config);
            }
        }

        /// <summary>
        /// Instantiates the complete 3D environment for the given layout.
        /// </summary>
        public void BuildFloorFromLayout(FloorLayout layout, FloorConfig config)
        {
            EnsureContainer();
            ResolveSpawners();

            if (layout == null)
            {
                Debug.LogError("[LevelBuilder] Layout is null! Cannot build physical floor.");
                return;
            }

            // 1. Instantiate grid geometry (floors, ceilings, walls, windows, merchant counter)
            ConstructGridGeometry(layout);

            // 2. Instantiate Lateral Safe Room Exit Elevator
            PlaceLateralExitElevator(layout.ExitPoint, config != null && config.Type == FloorType.BossRoom);

            // 3. Instantiate Environmental Hazards
            if (_hazardSpawner != null)
            {
                _hazardSpawner.SpawnHazards(layout, config);
            }

            // 4. Instantiate Enemies (only on non-boss floors)
            if (config != null && config.Type != FloorType.BossRoom)
            {
                if (_enemySpawner != null)
                {
                    _enemySpawner.SpawnEnemies(layout, config);
                }
            }

            Debug.Log($"[LevelBuilder] Physical floor {(config != null ? config.FloorNumber : 1)} constructed successfully.");
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
                            SpawnFloor(worldPos, false);
                            SpawnCeiling(worldPos);
                            break;

                        case CellType.SafeRoom:
                            SpawnFloor(worldPos, true); // Distinct safe haven tiles
                            SpawnCeiling(worldPos);
                            break;

                        case CellType.MerchantCounter:
                            SpawnMerchantWindowFeature(worldPos);
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

        private void SpawnFloor(Vector3 position, bool isSafeRoom)
        {
            GameObject floor;
            if (_floorPrefab != null)
            {
                floor = Instantiate(_floorPrefab, position, Quaternion.identity, _levelContainer);
            }
            else
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = isSafeRoom ? "Floor_Tile_SafeRoom" : "Floor_Tile";
                floor.transform.position = position + Vector3.down * 0.1f;
                floor.transform.localScale = new Vector3(_cellSize, 0.2f, _cellSize);
                floor.transform.parent = _levelContainer;

                var rend = floor.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (isSafeRoom && _safeRoomFloorMaterial != null)
                    {
                        rend.material = _safeRoomFloorMaterial;
                    }
                    else if (_floorMaterial != null)
                    {
                        rend.material = _floorMaterial;
                    }
                    else
                    {
                        rend.material.color = isSafeRoom
                            ? new Color(0.24f, 0.26f, 0.23f)  // Warm industrial safe haven floor
                            : new Color(0.14f, 0.15f, 0.17f); // Dark industrial slate
                    }
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

                var rend = ceiling.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_ceilingMaterial != null) rend.material = _ceilingMaterial;
                    else rend.material.color = new Color(0.18f, 0.19f, 0.22f);
                }
            }
            _instantiatedLevelObjects.Add(ceiling);
        }

        // ════════════════════════════════════════════════════════════════════
        // WALLS & WINDOWS
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
                wall.name = "Interior_Dividing_Wall";
                wall.transform.position = position + Vector3.up * (_wallHeight * 0.5f);
                wall.transform.localScale = new Vector3(_cellSize, _wallHeight, _cellSize);
                wall.transform.parent = _levelContainer;

                var rend = wall.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_interiorWallMaterial != null) rend.material = _interiorWallMaterial;
                    else rend.material.color = new Color(0.26f, 0.28f, 0.32f);
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
                extWall.name = "Exterior_Facade_Wall";
                extWall.transform.position = position + Vector3.up * (_wallHeight * 0.5f);
                extWall.transform.localScale = new Vector3(_cellSize, _wallHeight, _cellSize);
                extWall.transform.parent = _levelContainer;

                var rend = extWall.GetComponent<Renderer>();
                if (rend != null)
                {
                    if (_exteriorWallMaterial != null) rend.material = _exteriorWallMaterial;
                    else rend.material.color = new Color(0.12f, 0.13f, 0.15f);
                }
            }
            _instantiatedLevelObjects.Add(extWall);
        }

        private void SpawnExteriorWindow(Vector3 position, int x, int y, int width, int height)
        {
            if (_windowPrefab != null)
            {
                var win = Instantiate(_windowPrefab, position, Quaternion.identity, _levelContainer);
                _instantiatedLevelObjects.Add(win);
                return;
            }

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

            // 1. Lower Sill
            var sill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sill.name = "Window_Sill";
            sill.transform.position = position + Vector3.up * 0.45f;
            sill.transform.localScale = sillScale;
            sill.transform.parent = _levelContainer;
            var rendSill = sill.GetComponent<Renderer>();
            if (rendSill != null) rendSill.material.color = new Color(0.2f, 0.22f, 0.25f);
            _instantiatedLevelObjects.Add(sill);

            // 2. Upper Lintel
            float lintelH = 0.7f;
            var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lintel.name = "Window_Lintel";
            lintel.transform.position = position + Vector3.up * (_wallHeight - (lintelH * 0.5f));
            lintel.transform.localScale = lintelScale;
            lintel.transform.parent = _levelContainer;
            var rendLintel = lintel.GetComponent<Renderer>();
            if (rendLintel != null) rendLintel.material.color = new Color(0.2f, 0.22f, 0.25f);
            _instantiatedLevelObjects.Add(lintel);

            // 3. Panoramic Glass Pane
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "Window_Glass";
            glass.transform.position = position + Vector3.up * (0.9f + (_wallHeight - 1.6f) * 0.5f);
            glass.transform.localScale = glassScale;
            glass.transform.parent = _levelContainer;
            var rendGlass = glass.GetComponent<Renderer>();
            if (rendGlass != null)
            {
                if (_windowMaterial != null) rendGlass.material = _windowMaterial;
                else rendGlass.material.color = new Color(0.35f, 0.65f, 0.85f, 0.45f);
            }
            _instantiatedLevelObjects.Add(glass);
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. LATERAL SAFE ROOM: MERCHANT WINDOW & COUNTER ARCHITECTURE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates the dedicated Merchant architectural counter and cutout window.
        /// Features:
        ///   - Base counter partition wall (0m to 1.0m)
        ///   - Polished interaction desk counter shelf extending forward (height 1.0m)
        ///   - Cutout window opening (1.0m to 2.3m) for dialogue/trading
        ///   - Upper lintel header (2.3m to 3.2m)
        ///   - Framing jambs & warm overhead atmospheric lighting
        /// </summary>
        private void SpawnMerchantWindowFeature(Vector3 position)
        {
            // Spawn base floor and ceiling
            SpawnFloor(position, true);
            SpawnCeiling(position);

            if (_merchantCounterPrefab != null)
            {
                var customCounter = Instantiate(_merchantCounterPrefab, position, Quaternion.identity, _levelContainer);
                _instantiatedLevelObjects.Add(customCounter);
                return;
            }

            var rootGo = new GameObject("Merchant_Service_Window");
            rootGo.transform.position = position;
            rootGo.transform.parent = _levelContainer;

            // 1. Lower Counter Base Wall (0.0m to 1.0m)
            var baseWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseWall.name = "Merchant_Counter_Base";
            baseWall.transform.position = position + Vector3.up * 0.5f;
            baseWall.transform.localScale = new Vector3(_cellSize, 1.0f, 0.45f);
            baseWall.transform.parent = rootGo.transform;
            var rendBase = baseWall.GetComponent<Renderer>();
            if (rendBase != null) rendBase.material.color = new Color(0.24f, 0.20f, 0.17f); // Dark mahogany wood paneling

            // 2. Physical Service Desk Shelf (extends forward into Safe Room)
            var counterDesk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            counterDesk.name = "Merchant_Counter_Shelf";
            counterDesk.transform.position = position + Vector3.up * 1.02f + Vector3.back * 0.18f;
            counterDesk.transform.localScale = new Vector3(_cellSize * 1.02f, 0.08f, 0.85f);
            counterDesk.transform.parent = rootGo.transform;
            var rendDesk = counterDesk.GetComponent<Renderer>();
            if (rendDesk != null)
            {
                if (_merchantCounterMaterial != null) rendDesk.material = _merchantCounterMaterial;
                else rendDesk.material.color = new Color(0.48f, 0.32f, 0.18f); // Polished walnut counter top
            }

            // 3. Cutout Opening Side Pillars (Jambs)
            float jambWidth = 0.35f;
            var jambL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            jambL.name = "Merchant_Window_Jamb_L";
            jambL.transform.position = position + Vector3.up * 1.65f + Vector3.left * ((_cellSize * 0.5f) - (jambWidth * 0.5f));
            jambL.transform.localScale = new Vector3(jambWidth, 1.3f, 0.45f);
            jambL.transform.parent = rootGo.transform;
            var rendJL = jambL.GetComponent<Renderer>();
            if (rendJL != null) rendJL.material.color = new Color(0.28f, 0.24f, 0.20f);

            var jambR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            jambR.name = "Merchant_Window_Jamb_R";
            jambR.transform.position = position + Vector3.up * 1.65f + Vector3.right * ((_cellSize * 0.5f) - (jambWidth * 0.5f));
            jambR.transform.localScale = new Vector3(jambWidth, 1.3f, 0.45f);
            jambR.transform.parent = rootGo.transform;
            var rendJR = jambR.GetComponent<Renderer>();
            if (rendJR != null) rendJR.material.color = new Color(0.28f, 0.24f, 0.20f);

            // 4. Upper Lintel Wall (2.3m to ceiling 3.2m)
            float lintelH = _wallHeight - 2.3f;
            var topLintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topLintel.name = "Merchant_Window_Lintel";
            topLintel.transform.position = position + Vector3.up * (2.3f + (lintelH * 0.5f));
            topLintel.transform.localScale = new Vector3(_cellSize, lintelH, 0.45f);
            topLintel.transform.parent = rootGo.transform;
            var rendLintel = topLintel.GetComponent<Renderer>();
            if (rendLintel != null) rendLintel.material.color = new Color(0.26f, 0.28f, 0.32f);

            // 5. Overhead Warm Amber Illumination
            var lampGo = new GameObject("Merchant_Warm_Spotlight");
            lampGo.transform.position = position + Vector3.up * 2.85f + Vector3.back * 0.3f;
            lampGo.transform.parent = rootGo.transform;
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1.0f, 0.82f, 0.45f); // Warm safe haven amber
            lamp.range = 6.5f;
            lamp.intensity = 2.4f;

            _instantiatedLevelObjects.Add(rootGo);
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. LATERAL EXIT ELEVATOR & SOFTLOCK PREVENTION
        // ════════════════════════════════════════════════════════════════════

        private void PlaceLateralExitElevator(Vector2Int exitGrid, bool isBossFloor)
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

            // 2. Overhead illuminated header sign
            var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sign.name = "Elevator_Header_Sign";
            sign.transform.position = exitPos + Vector3.up * 3.1f;
            sign.transform.localScale = new Vector3(2.8f, 0.5f, 0.4f);
            sign.transform.parent = _levelContainer;
            var rendSign = sign.GetComponent<Renderer>();
            if (rendSign != null) rendSign.material.color = new Color(0.1f, 0.88f, 0.35f);
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
            GameObject cabin;
            if (_elevatorCabinPrefab != null)
            {
                cabin = Instantiate(_elevatorCabinPrefab, exitPos, Quaternion.identity, _levelContainer);
            }
            else
            {
                cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cabin.name = isBossFloor ? "Boss_Exit_Gate" : "Elevator_Cabin";
                cabin.transform.position = exitPos + Vector3.up * 1.5f;
                cabin.transform.localScale = new Vector3(2.4f, 3.0f, 2.0f);
                cabin.transform.parent = _levelContainer;

                var rendCabin = cabin.GetComponent<Renderer>();
                if (rendCabin != null)
                {
                    // Disable renderer on the trigger volume so the illuminated elevator interior is visible
                    rendCabin.enabled = false;
                }

                // Construct authentic 3-walled elevator enclosure (Back, Left, Right) with open front
                SpawnElevatorEnclosure(exitPos);
            }

            var col = cabin.GetComponent<Collider>();
            if (col == null) col = cabin.AddComponent<BoxCollider>();
            col.isTrigger = true;

            var trigger = cabin.GetComponent<ElevatorDoorTrigger>() ?? cabin.AddComponent<ElevatorDoorTrigger>();
            int currentFloorNum = (GameManager.Instance != null && GameManager.Instance.CurrentFloor != null)
                ? GameManager.Instance.CurrentFloor.FloorNumber
                : 1;
            trigger.SetNextFloorNumber(currentFloorNum + 1);

            _instantiatedLevelObjects.Add(cabin);
        }

        private void SpawnElevatorEnclosure(Vector3 exitPos)
        {
            Color steelColor = new Color(0.18f, 0.20f, 0.24f);

            // 1. Back Wall
            var backWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backWall.name = "Elevator_Cabin_Back";
            backWall.transform.position = exitPos + Vector3.up * 1.6f + Vector3.forward * 1.0f;
            backWall.transform.localScale = new Vector3(2.6f, 3.2f, 0.2f);
            backWall.transform.parent = _levelContainer;
            var rendB = backWall.GetComponent<Renderer>();
            if (rendB != null) rendB.material.color = steelColor;
            _instantiatedLevelObjects.Add(backWall);

            // 2. Left Wall
            var leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftWall.name = "Elevator_Cabin_Left";
            leftWall.transform.position = exitPos + Vector3.up * 1.6f + Vector3.left * 1.25f;
            leftWall.transform.localScale = new Vector3(0.2f, 3.2f, 2.0f);
            leftWall.transform.parent = _levelContainer;
            var rendL = leftWall.GetComponent<Renderer>();
            if (rendL != null) rendL.material.color = steelColor;
            _instantiatedLevelObjects.Add(leftWall);

            // 3. Right Wall
            var rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightWall.name = "Elevator_Cabin_Right";
            rightWall.transform.position = exitPos + Vector3.up * 1.6f + Vector3.right * 1.25f;
            rightWall.transform.localScale = new Vector3(0.2f, 3.2f, 2.0f);
            rightWall.transform.parent = _levelContainer;
            var rendR = rightWall.GetComponent<Renderer>();
            if (rendR != null) rendR.material.color = steelColor;
            _instantiatedLevelObjects.Add(rightWall);
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. SAFE PLAYER TELEPORTATION TO START ROOM
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Safely snaps the player to the newly generated Spawn Room.
        /// Temporarily disables CharacterController to prevent Unity physics fighting/jitter.
        /// </summary>
        public void PositionPlayerAtSpawn(Vector2Int spawnGrid)
        {
            Vector3 worldSpawn = new Vector3(spawnGrid.x * _cellSize, 1.0f, spawnGrid.y * _cellSize);

            var playerController = FindAnyObjectByType<PlayerController>();
            if (playerController != null)
            {
                playerController.Teleport(worldSpawn, Quaternion.identity);
                Debug.Log($"[LevelBuilder] PlayerController safely teleported to Spawn Room: {worldSpawn}");
                return;
            }

            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                var cc = playerObj.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                playerObj.transform.position = worldSpawn;
                playerObj.transform.rotation = Quaternion.identity;
                if (cc != null) cc.enabled = true;
                Debug.Log($"[LevelBuilder] Player GameObject safely teleported to Spawn Room: {worldSpawn}");
            }
            else
            {
                Debug.LogWarning("[LevelBuilder] Could not find GameObject with 'Player' tag or PlayerController for teleportation!");
            }
        }
    }
}
