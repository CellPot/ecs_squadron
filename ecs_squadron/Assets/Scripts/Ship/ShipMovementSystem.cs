using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace Ship
{
    [BurstCompile]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct ShipMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            foreach (var (transform, shipMovement, ship) in SystemAPI
                         .Query<RefRW<LocalTransform>,
                             RefRW<ShipMovement>,
                             RefRO<global::Ship.Ship>>())
            {
                transform.ValueRW.Position = transform.ValueRO.Position +
                                             shipMovement.ValueRO.LinearVelocity * ship.ValueRO.MoveSpeed * deltaTime;
            }
        }
    }
}