// ============================================================================
// FloorConfig.cs — Defines the type and parameters of each floor in the 30-floor tower
// ============================================================================

using System;
using UnityEngine;

namespace DemenciaNoLimite.Core.Data
{
    /// <summary>
    /// Categorizes every floor in the 30-floor tower.
    /// </summary>
    public enum FloorType
    {
        TutorialLobby,  // Floor 1 — Safe starter lobby & weapons tutorial
        ProceduralMaze, // Floors 2-9, 11-19, 21-29 — Roguelike loot & combat floors
        BossRoom,       // Floors 10, 20, 30 — Arena boss encounters
        Elevator        // Transition elevator cabin
    }

    /// <summary>
    /// Identifies which "act" (tier) a floor belongs to.
    /// Each act has its own enemy pool, tileset, and difficulty curve.
    /// </summary>
    public enum FloorTier
    {
        Act1,  // Floors 1-10  — The Favela Depths (Boss at Floor 10)
        Act2,  // Floors 11-20 — The Industrial Rot (Boss at Floor 20)
        Act3   // Floors 21-30 — The Apex / Babel (Final Boss at Floor 30)
    }

    /// <summary>
    /// Configuration for a single floor in the 30-floor pre-generated tower.
    /// </summary>
    [Serializable]
    public class FloorConfig
    {
        public const int MAX_TOWER_FLOORS = 30;

        public int       FloorNumber;
        public FloorType Type;
        public FloorTier Tier;

        // ── Difficulty Baseline ─────────────────────────────────────────────
        public float BaseDifficulty;
        public int   BaseEnemyCount;
        public int   BaseHazardCount;
        public float MazeComplexity;

        // ── Boss-specific ──────────────────────────────────────────────────
        public string BossID;

        // ── Spatial Separation ─────────────────────────────────────────────
        /// <summary>Vertical Y-offset per floor to prevent physical overlapping.</summary>
        public const float VerticalFloorSpacing = 100.0f;

        public static float GetFloorElevation(int floorNumber)
        {
            return (floorNumber - 1) * VerticalFloorSpacing;
        }

        // ── Factory ────────────────────────────────────────────────────────

        public static FloorType GetFloorType(int floorNumber)
        {
            if (floorNumber == 1) return FloorType.TutorialLobby;
            if (floorNumber == 10 || floorNumber == 20 || floorNumber == 30) return FloorType.BossRoom;
            return FloorType.ProceduralMaze;
        }

        public static FloorTier GetFloorTier(int floorNumber)
        {
            if (floorNumber <= 10) return FloorTier.Act1;
            if (floorNumber <= 20) return FloorTier.Act2;
            return FloorTier.Act3;
        }

        public static FloorConfig Build(int floorNumber)
        {
            var type = GetFloorType(floorNumber);
            var tier = GetFloorTier(floorNumber);

            int tierStart = tier == FloorTier.Act1 ? 1
                          : tier == FloorTier.Act2 ? 11
                          : 21;
            float posInTier = Mathf.Clamp01((floorNumber - tierStart) / 9f);

            float tierMult = tier == FloorTier.Act1 ? 1.0f
                           : tier == FloorTier.Act2 ? 1.5f
                           : 2.2f;

            var config = new FloorConfig
            {
                FloorNumber    = floorNumber,
                Type           = type,
                Tier           = tier,
                BaseDifficulty = 1.0f + (posInTier * tierMult),
                MazeComplexity = Mathf.Clamp01(0.40f + (posInTier * 0.40f) + ((int)tier * 0.15f)),
            };

            if (type == FloorType.TutorialLobby)
            {
                config.BaseEnemyCount  = 0;
                config.BaseHazardCount = 0;
                config.MazeComplexity  = 0.1f;
            }
            else if (type == FloorType.BossRoom)
            {
                config.BaseEnemyCount  = 0;
                config.BaseHazardCount = 2 + (int)tier;
                config.BossID          = GetBossID(floorNumber);
            }
            else
            {
                // Procedural maze floors: scale enemies and hazards proportionally with larger labyrinth wings
                config.BaseEnemyCount  = 5 + (int)(floorNumber * 0.65f);
                config.BaseHazardCount = 2 + (int)(floorNumber * 0.25f);
            }

            return config;
        }

        public static string GetBossID(int floorNumber)
        {
            switch (floorNumber)
            {
                case 10: return "BOSS_ACT1_CARNICERO"; // The Butcher (Act 1 Climax)
                case 20: return "BOSS_ACT2_MAQUINA";   // The Machine (Act 2 Climax)
                case 30: return "BOSS_ACT3_DEMENCIA";  // Dementia Incarnate (Tower Pinnacle)
                default: return "BOSS_UNKNOWN";
            }
        }
    }
}
