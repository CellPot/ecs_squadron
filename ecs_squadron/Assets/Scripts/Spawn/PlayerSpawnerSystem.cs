using Config;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace Spawn
{
    [BurstCompile]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct PlayerSpawnerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;
            WorldConfig config = SystemAPI.GetSingleton<WorldConfig>();
            Entity player = state.EntityManager.Instantiate(config.PlayerPrefab);
        }
    }
}