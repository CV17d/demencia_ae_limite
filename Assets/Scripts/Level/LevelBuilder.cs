// ============================================================================
// LevelBuilder.cs — Physical construction of procedural tower levels
// ============================================================================
// Takes the abstract FloorLayout from LevelGenerator and constructs the
// 3D world (Floors, Walls, Corridors, Pillars).
// Places the player at SpawnPoint, instantiates the Exit Elevator Trigger,
// and invokes EnemySpawner and HazardSpawner.
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
        [SerializeField] private float _cellSize = 3.0f;
        [SerializeField] private float _wallHeight = 3.5f;

        [Header("Modular Prefabs (Optional - generates clean primitives if null)")]
        [SerializeField] private GameObject _wallPrefab;
        [SerializeField] private GameObject _floorPrefab;
        [SerializeField] private GameObject _exitDoorPrefab;

        [Header("Spawners")]
        [SerializeField] private EnemySpawner _enemySpawner;
        [SerializeField] private HazardSpawner _hazardSpawner;

        private readonly List<GameObject> _instantiatedLevelObjects = new List<GameObject>();
        private Transform _levelContainer;

        private void Awake()
        {
            _levelContainer = new GameObject("[Generated_Floor_Geometry]").transform;
            _levelContainer.parent = transform;

            if (_enemySpawner == null) _enemySpawner = GetComponentInChildren<EnemySpawner>();
            if (_hazardSpawner == null) _hazardSpawner = GetComponentInChildren<HazardSpawner>();
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
            ClearPreviousFloor();

            var levelGen = FindAnyObjectByType<LevelGenerator>();
            if (levelGen == null)
            {
                Debug.LogError("[LevelBuilder] No LevelGenerator found in scene!");
                return;
            }

            FloorLayout layout = levelGen.GenerateFloor(config);
            if (layout == null) return;

            // 1. Build Geometry
            ConstructGridGeometry(layout);

            // 2. Position Player
            PositionPlayerAtSpawn(layout.SpawnPoint);

            // 3. Place Exit Trigger
            PlaceExitDoor(layout.ExitPoint, config.Type == FloorType.BossRoom);

            // 4. Spawn Entities
            if (_enemySpawner != null && config.Type != FloorType.BossRoom)
            {
                _enemySpawner.SpawnEnemies(layout, config);
            }

            if (_hazardSpawner != null)
            {
                _hazardSpawner.SpawnHazards(layout, config);
            }

            Debug.Log($"[LevelBuilder] Physical floor {config.FloorNumber} constructed successfully.");
        }

        private void ConstructGridGeometry(FloorLayout layout)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    CellType cell = layout.Grid[x, y];
                    Vector3 worldPos = new Vector3(x * _cellSize, 0f, y * _cellSize);

                    if (cell == CellType.Wall)
                    {
                        SpawnWall(worldPos);
                    }
                    else
                    {
                        SpawnFloor(worldPos);
                    }
                }
            }
        }

        private void SpawnWall(Vector3 position)
        {
            GameObject wall;
            if (_wallPrefab != null)
            {
                wall = Instantiate(_wallPrefab, position + Vector3.up * (_wallHeight * 0.5f), Quaternion.identity, _levelContainer);
            }
            else
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall_Block";
                wall.transform.position = position + Vector3.up * (_wallHeight * 0.5f);
                wall.transform.localScale = new Vector3(_cellSize, _wallHeight, _cellSize);
                wall.transform.parent = _levelContainer;
                wall.layer = LayerMask.NameToLayer("Default");

                var rend = wall.GetComponent<Renderer>();
                if (rend != null) rend.material.color = new Color(0.24f, 0.27f, 0.30f); // Dark industrial concrete
            }
            _instantiatedLevelObjects.Add(wall);
        }

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
                if (rend != null) rend.material.color = new Color(0.12f, 0.13f, 0.15f); // Asphalt floor
            }
            _instantiatedLevelObjects.Add(floor);
        }

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
            if (rendSign != null) rendSign.material.color = new Color(0.1f, 0.85f, 0.3f); // Glowing Green
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
