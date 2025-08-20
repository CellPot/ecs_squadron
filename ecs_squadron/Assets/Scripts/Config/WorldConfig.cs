using System.ComponentModel;
using Unity.Entities;

namespace Config
{
    public struct WorldConfig : IComponentData
    {
        public Entity PlayerPrefab;
        public EnemyConfig EnemyConfig;
        public BoidConfig BoidConfig;
    }

    public struct EnemyConfig
    {
        public Entity EnemyPrefab;
        public float ShipSpawnMinRadius;
        public float ShipSpawnMaxRadius;
        public int MaxShipCount;
        public int WaveSize;
        public float WaveCooldown;
    }

    public struct BoidConfig
    {
        public float CellSize;
        public int CellCheckRadius;
        public float SeparationRadius;
        public float AlignmentWeight;
        public float CohesionWeight;
        public float SeparationWeight;
        public float TargetSeekWeight;
        public float MaxSteerForce;
    }
}