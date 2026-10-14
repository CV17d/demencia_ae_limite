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

            GameObject door;
            if (_exitDoorPrefab != null)
            {
                door = Instantiate(_exitDoorPrefab, exitPos, Quaternion.identity, _levelContainer);
            }
            else
            {
                door = GameObject.CreatePrimitive(PrimitiveType.Cube);
                door.name = isBossFloor ? "Boss_Exit_Gate" : "Elevator_Door_Trigger";
                door.transform.position = exitPos + Vector3.up * 1.5f;
                door.transform.localScale = new Vector3(_cellSize * 0.8f, 3f, 0.4f);
                door.transform.parent = _levelContainer;
                var col = door.GetComponent<Collider>();
                if (col != null) col.isTrigger = true;
            }

            if (door.GetComponent<ElevatorDoorTrigger>() == null)
            {
                door.AddComponent<ElevatorDoorTrigger>();
            }

            _instantiatedLevelObjects.Add(door);
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
