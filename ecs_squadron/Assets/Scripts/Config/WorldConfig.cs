using Unity.Entities;

namespace Config
{
    public struct WorldConfig: IComponentData
    {
        public Entity PlayerPrefab;
        public Entity EnemyPrefab;
    }
}
