using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Combat.Weapon
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct DestroyOnDelaySystem : ISystem
    {        
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DestroyOnDelay>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (destroyComponent, entity) in SystemAPI
                         .Query<RefRW<DestroyOnDelay>>()
                         .WithEntityAccess())
            {
                destroyComponent.ValueRW.TimeToDestroy -= SystemAPI.Time.DeltaTime;
                if (destroyComponent.ValueRW.TimeToDestroy <= 0)
                {
                    ecb.DestroyEntity(entity);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}

public struct DestroyOnDelay : IComponentData
{
    public float TimeToDestroy;
}