// ============================================================================
// FloorConfig.cs — Defines the type and parameters of each floor
// ============================================================================

using System;

namespace DemenciaNoLimite.Core.Data
{
    /// <summary>
    /// Categorizes every floor in the tower.
    /// </summary>
    public enum FloorType
    {
        ProceduralMaze, // Floors 1-8, 10-17, 19-26
        BossRoom,       // Floors 9, 18, 27
        Elevator        // Transition between floors (virtual, not a physical floor)
    }

    /// <summary>
    /// Identifies which "act" (tier) a floor belongs to.
    /// Each act has its own enemy pool, tileset, and difficulty curve.
    /// </summary>
    public enum FloorTier
    {
        Act1,  // Floors 1-9   — The Favela Depths
        Act2,  // Floors 10-18 — The Industrial Rot
        Act3   // Floors 19-27 — The Apex / Babel
    }

    /// <summary>
    /// Immutable configuration for a single floor. Built once by LevelGenerator
    /// during initialization and then consumed by the procedural generation
    /// pipeline and AI Director.
    /// </summary>
    [Serializable]
    public class FloorConfig
    {
        public int       FloorNumber;
        public FloorType Type;
        public FloorTier Tier;

        // ── Difficulty Baseline (before AI Director adjustments) ───────────
        /// <summary>Base difficulty scalar for this floor (0..1 within its tier).</summary>
        public float BaseDifficulty;

        /// <summary>Base number of enemies to spawn on this floor.</summary>
        public int   BaseEnemyCount;

        /// <summary>Base number of environmental hazards.</summary>
        public int   BaseHazardCount;

        /// <summary>Maze complexity factor (0..1). Higher = more dead ends, longer paths.</summary>
        public float MazeComplexity;

        // ── Boss-specific ──────────────────────────────────────────────────
        /// <summary>Boss identifier (only relevant for BossRoom floors).</summary>
        public string BossID;

        // ── Factory ────────────────────────────────────────────────────────

        /// <summary>
        /// Determines the FloorType for a given floor number.
        /// Boss floors: 9, 18, 27. Everything else: ProceduralMaze.
        /// </summary>
        public static FloorType GetFloorType(int floorNumber)
        {
            return (floorNumber == 9 || floorNumber == 18 || floorNumber == 27)
                ? FloorType.BossRoom
                : FloorType.ProceduralMaze;
        }

        /// <summary>
        /// Determines which act/tier a floor belongs to.
        /// </summary>
        public static FloorTier GetFloorTier(int floorNumber)
        {
            if (floorNumber <= 9)  return FloorTier.Act1;
            if (floorNumber <= 18) return FloorTier.Act2;
            return FloorTier.Act3;
        }

        /// <summary>
        /// Builds a FloorConfig with sensible defaults for a given floor number.
        /// The AI Director will modify these values before the floor is generated.
        /// </summary>
        public static FloorConfig Build(int floorNumber)
        {
            var type = GetFloorType(floorNumber);
            var tier = GetFloorTier(floorNumber);

            // Normalized position within the tier (0..1)
            int tierStart = tier == FloorTier.Act1 ? 1
                          : tier == FloorTier.Act2 ? 10
                          : 19;
            float posInTier = (floorNumber - tierStart) / 8f; // 8 floors per tier

            // Tier multiplier: Act1=1, Act2=1.5, Act3=2.2
            float tierMult = tier == FloorTier.Act1 ? 1.0f
                           : tier == FloorTier.Act2 ? 1.5f
                           : 2.2f;

            var config = new FloorConfig
            {
                FloorNumber    = floorNumber,
                Type           = type,
                Tier           = tier,
                BaseDifficulty = posInTier * tierMult,
                MazeComplexity = 0.3f + (posInTier * 0.5f), // 0.3..0.8 within tier
            };

            if (type == FloorType.BossRoom)
            {
                config.BaseEnemyCount  = 0; // Boss rooms have no mobs (just the boss)
                config.BaseHazardCount = 2 + (int)(tier);
                config.BossID          = GetBossID(floorNumber);
            }
            else
            {
                // Maze floors: enemy count scales with floor and tier
                config.BaseEnemyCount  = 4 + (int)(posInTier * 6f * tierMult);
                config.BaseHazardCount = 1 + (int)(posInTier * 3f * tierMult);
            }

            return config;
        }

        /// <summary>
        /// Maps boss floors to their boss identifiers.
        /// These IDs will match ScriptableObject asset names.
        /// </summary>
        private static string GetBossID(int floorNumber)
        {
            switch (floorNumber)
            {
                case 9:  return "BOSS_ACT1_CARNICERO";   // The Butcher
                case 18: return "BOSS_ACT2_MAQUINA";     // The Machine
                case 27: return "BOSS_ACT3_DEMENCIA";    // Dementia Incarnate
                default: return "BOSS_UNKNOWN";
            }
        }
    }
}
