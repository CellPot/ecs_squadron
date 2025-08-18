using Unity.Entities;
using UnityEngine;

namespace Config
{
    public class WorldConfigAuthoring : MonoBehaviour
    {
        public GameObject PlayerPrefab;
        public GameObject EnemyPrefab;
        public float ShipSpawnMinRadius = 15f;
        public float ShipSpawnMaxRadius = 30f;
        public int MaxShipCount = 30;
        public int WaveSize = 5;
        public float WaveCooldown = 3;

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
                    }
                });
            }
        }
    }
}