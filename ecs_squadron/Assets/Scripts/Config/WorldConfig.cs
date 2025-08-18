using Unity.Entities;

namespace Config
{
    public struct WorldConfig : IComponentData
    {
        public Entity PlayerPrefab;
        public EnemyConfig EnemyConfig;
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
}