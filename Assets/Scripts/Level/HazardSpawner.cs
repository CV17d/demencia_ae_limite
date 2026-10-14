// ============================================================================
// HazardSpawner.cs — Environmental trap & hazard instantiation
// ============================================================================
// Spawns toxic miasma, barbed wire, and electrified water pools on HazardPoints.
// Hazards deal damage over time using the DamagePipeline.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using DemenciaNoLimite.Combat;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;

namespace DemenciaNoLimite.Level
{
    public class HazardSpawner : MonoBehaviour
    {
        [Header("Hazard Prefabs")]
        [SerializeField] private GameObject _hazardPrefabDefault;

        private readonly List<GameObject> _activeHazards = new List<GameObject>();

        public void SpawnHazards(FloorLayout layout, FloorConfig config)
        {
            ClearHazards();

            int count = Mathf.Min(config.BaseHazardCount, layout.HazardPoints.Count);

            for (int i = 0; i < count; i++)
            {
                Vector2Int point = layout.HazardPoints[i];
                Vector3 worldPos = new Vector3(point.x * 3.0f, 0.1f, point.y * 3.0f);

                GameObject hazardGo;
                if (_hazardPrefabDefault != null)
                {
                    hazardGo = Instantiate(_hazardPrefabDefault, worldPos, Quaternion.identity, transform);
                }
                else
                {
                    hazardGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    hazardGo.name = "Hazard_Toxic_Spill";
                    hazardGo.transform.position = worldPos;
                    hazardGo.transform.localScale = new Vector3(2.5f, 0.05f, 2.5f);
                    hazardGo.transform.parent = transform;
                    var col = hazardGo.GetComponent<Collider>();
                    if (col != null) col.isTrigger = true;
                }

                if (hazardGo.GetComponent<HazardZoneTrigger>() == null)
                {
                    hazardGo.AddComponent<HazardZoneTrigger>();
                }

                _activeHazards.Add(hazardGo);
            }
        }

        public void ClearHazards()
        {
            foreach (var h in _activeHazards)
            {
                if (h != null) Destroy(h);
            }
            _activeHazards.Clear();
        }
    }

    /// <summary>
    /// Environmental damage trigger zone.
    /// </summary>
    public class HazardZoneTrigger : MonoBehaviour
    {
        [SerializeField] private float _dps = 8.0f;
        [SerializeField] private float _tickInterval = 0.5f;
        private float _lastTickTime;

        private void OnTriggerStay(Collider other)
        {
            if (Time.time - _lastTickTime < _tickInterval) return;

            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable.IsAlive)
            {
                _lastTickTime = Time.time;
                DamageInfo info = new DamageInfo(
                    amount: _dps * _tickInterval,
                    attacker: gameObject,
                    hitPoint: other.transform.position,
                    sourceType: DamageSourceType.HazardEnvironment,
                    ignoreDefense: false
                );
                damageable.TakeDamage(info);
            }
        }
    }
}
