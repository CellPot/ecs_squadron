using Boids;
using Unity.Entities;
using UnityEngine;

namespace Config
{
    public class WorldConfigAuthoring : MonoBehaviour
    {
        [Header("Prefabs")] public GameObject PlayerPrefab;
        public GameObject EnemyPrefab;

        [Header("Enemy Spawning")] public float ShipSpawnMinRadius = 15f;
        public float ShipSpawnMaxRadius = 30f;
        public int MaxShipCount = 30;
        public int WaveSize = 5;
        public float WaveCooldown = 3;

        [Header("Boid Behavior")]
        [Tooltip("The size of cell, in which ships search neighbors for alignment and cohesion")]
        public float CellSize = 6f;

        [Tooltip("How many cells at each side is checked around current cell for neighbours: 1 = search grid [3x3]")]
        public int CellCheckRadius = 1;

        [Tooltip("How close is too close for separation behavior")]
        public float SeparationRadius = 3f;

        [Tooltip("How strongly ships align with neighbors")]
        public float AlignmentWeight = 1.2f;

        [Tooltip("How strongly ships move toward the group center")]
        public float CohesionWeight = 1.0f;

        [Tooltip("How strongly ships avoid crowding")]
        public float SeparationWeight = 2.0f;

        [Tooltip("How strongly ships are attracted to the player")]
        public float TargetSeekWeight = 1.5f;

        [Tooltip("Maximum steering force to prevent erratic movement")]
        public float MaxSteerForce = 5f;

        [Tooltip("Distance at which target seeking velocity will start diminishing")]
        public float TargetSlowRadius = 6f;

        [Tooltip("Distance at which target seeking velocity will be reverted")]
        public float TargetStopRadius = 4f;

        [Tooltip("How much ships will try to avoid player")]
        public float ObstacleAvoidanceWeight = 4f;

        [Header("Combat")]
        public float ProjectileSearchCellSize = 10f;
        public int ProjectileCellCheckRadius = 1;
        public float WeaponTargetSearchCellSize = 15f;

        class Baker : Baker<WorldConfigAuthoring>
        {
            public override void Bake(WorldConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new WorldConfig
                {
                    PlayerPrefab = GetEntity(authoring.PlayerPrefab, TransformUsageFlags.Dynamic),
                    EnemyConfig = new EnemyConfig()
                    {
                        EnemyPrefab = GetEntity(authoring.EnemyPrefab, TransformUsageFlags.Dynamic),
                        ShipSpawnMinRadius = authoring.ShipSpawnMinRadius,
                        ShipSpawnMaxRadius = authoring.ShipSpawnMaxRadius,
                        MaxShipCount = authoring.MaxShipCount,
                        WaveSize = authoring.WaveSize,
                        WaveCooldown = authoring.WaveCooldown,
                    },
                    BoidConfig = new BoidConfig()
                    {
                        CellSize = authoring.CellSize,
                        CellCheckRadius = authoring.CellCheckRadius,
                        SeparationRadius = authoring.SeparationRadius,
                        AlignmentWeight = authoring.AlignmentWeight,
                        CohesionWeight = authoring.CohesionWeight,
                        SeparationWeight = authoring.SeparationWeight,
                        TargetSeekWeight = authoring.TargetSeekWeight,
                        MaxSteerForce = authoring.MaxSteerForce,
                        TargetSlowRadius = authoring.TargetSlowRadius,
                        TargetStopRadius = authoring.TargetStopRadius,
                        ObstacleAvoidanceWeight = authoring.ObstacleAvoidanceWeight,
                    },
                    CombatConfig = new CombatConfig()
                    {
                        ProjectileCellCheckRadius = authoring.ProjectileCellCheckRadius,
                        ProjectileSearchCellSize = authoring.ProjectileSearchCellSize,
                        WeaponTargetSearchCellSize = authoring.WeaponTargetSearchCellSize,
                    }
                });
            }
        }
    }
}