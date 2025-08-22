using Ship;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct PlayerInputSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var shipMovement in
                     SystemAPI.Query<RefRW<ShipMovement>>()
                         .WithAll<PlayerTag>())
            {
                float3 rawInput = new float3(
                    Input.GetAxisRaw("Horizontal"),
                    Input.GetAxisRaw("Vertical"),
                    0
                );

                float3 inputDirection = rawInput;
                if (math.lengthsq(inputDirection) > 0.001f)
                {
                    inputDirection = math.normalize(inputDirection);
                }

                shipMovement.ValueRW.LinearVelocity = inputDirection;
            }
        }
    }
}