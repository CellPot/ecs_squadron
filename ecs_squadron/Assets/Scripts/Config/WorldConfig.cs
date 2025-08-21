using Boids;
using Unity.Entities;

namespace Config
{
    public struct WorldConfig : IComponentData
    {
        public Entity PlayerPrefab;
        public EnemyConfig EnemyConfig;
        public BoidConfig BoidConfig;
        public CombatConfig CombatConfig;
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
    
    public struct CombatConfig
    {
        public float ProjectileSearchCellSize;
        public int ProjectileCellCheckRadius;
        public float WeaponTargetSearchCellSize;
    }
}