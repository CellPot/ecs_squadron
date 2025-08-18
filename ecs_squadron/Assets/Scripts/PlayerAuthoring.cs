using Config;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class PlayerAuthoring : MonoBehaviour
{
    class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<PlayerTag>(entity);
            AddComponent<PlayerInput>(entity);
        }
    }
}

public struct PlayerTag : IComponentData
{
}

public struct PlayerInput : IComponentData
{
    public float3 MoveAxis;
}

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
        var config = SystemAPI.GetSingleton<WorldConfig>();
        var player = state.EntityManager.Instantiate(config.PlayerPrefab);
    }
}

[BurstCompile]
[UpdateBefore(typeof(PlayerSpawnerSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct EnemySpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldConfig>();
        state.RequireForUpdate<PlayerTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;
        
        var config = SystemAPI.GetSingleton<WorldConfig>();
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var random = Random.CreateFromIndex(1234);
        
        float3 playerPos = float3.zero;
        foreach (var playerTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
        {
            playerPos = playerTransform.ValueRO.Position;
            break;
        }

        int enemyCount = 10; 
        float minRadius = 15f; 
        float maxRadius = 30f; 
        
        for (int i = 0; i < enemyCount; i++)
        {
            float angle = random.NextFloat(0, 2 * math.PI);
            float distance = random.NextFloat(minRadius, maxRadius);
            
            float3 spawnPos = new float3(
                playerPos.x + math.cos(angle) * distance,
                playerPos.z + math.sin(angle) * distance,
                0f
            );
            
            Entity enemy = ecb.Instantiate(config.EnemyPrefab);
            ecb.SetComponent(enemy, new LocalTransform
            {
                Position = spawnPos,
                Scale = 1,
                Rotation = quaternion.identity
            });
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}