using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Weapon.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ProjectileMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Projectile>();
        }


        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (transform, projectile) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRO<Projectile>>())
            {
                float3 velocity = projectile.ValueRO.Direction * projectile.ValueRO.Speed * deltaTime;
                transform.ValueRW.Position += velocity;
            }
        }
    }
}