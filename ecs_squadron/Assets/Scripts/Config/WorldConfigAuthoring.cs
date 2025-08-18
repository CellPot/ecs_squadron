using Unity.Entities;
using UnityEngine;

namespace Config
{
    public class WorldConfigAuthoring : MonoBehaviour
    {
        public GameObject PlayerPrefab;
        public GameObject EnemyPrefab;

        class Baker : Baker<WorldConfigAuthoring>
        {
            public override void Bake(WorldConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new WorldConfig
                {
                    PlayerPrefab = GetEntity(authoring.PlayerPrefab, TransformUsageFlags.Dynamic),
                    EnemyPrefab = GetEntity(authoring.EnemyPrefab, TransformUsageFlags.Dynamic),
                });
            }
        }
    }
}