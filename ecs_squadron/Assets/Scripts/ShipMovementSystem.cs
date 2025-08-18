using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct ShipMovementSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (transform, shipMovement, ship) in SystemAPI
                     .Query<RefRW<LocalTransform>,
                         RefRW<ShipMovement>,
                         RefRO<Ship>>())
        {
            // transform.ValueRW = transform.ValueRO.RotateZ(
            //     shipMovement.ValueRO.AngularVelocity * deltaTime * ship.ValueRO.RotationSpeed
            // );
            transform.ValueRW.Position = transform.ValueRO.Position +
                                         shipMovement.ValueRO.LinearVelocity * ship.ValueRO.MoveSpeed * deltaTime;
        }
    }
}

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
        foreach (var (input, shipMovement, ship) in
                 SystemAPI.Query<RefRW<PlayerInput>, RefRW<ShipMovement>, RefRO<Ship>>()
                     .WithAll<PlayerTag>())
        {
            float3 rawInput = new float3(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"),
                0
            );

            input.ValueRW = new PlayerInput
            {
                MoveAxis = rawInput,
            };

            float3 inputDirection = rawInput;
            if (math.lengthsq(inputDirection) > 0.001f)
            {
                inputDirection = math.normalize(inputDirection);
            }

            shipMovement.ValueRW.LinearVelocity = inputDirection;
            // shipMovement.ValueRW.AngularVelocity = input.ValueRO.RotateAxis;
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct AIShipSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, movement, ship) in
                 SystemAPI.Query<RefRW<LocalTransform>,
                         RefRW<ShipMovement>,
                         RefRO<Ship>>()
                     .WithAbsent<PlayerTag>())
        {
            float3 aiPosition = transform.ValueRO.Position;
            float3 closestPlayerPosition = float3.zero;
            float closestDistance = float.MaxValue;
            bool isPlayerPresent = false;

            foreach (var playerTransform in SystemAPI.Query<RefRO<LocalTransform>>()
                         .WithAll<PlayerTag>())
            {
                float3 playerPosition = playerTransform.ValueRO.Position;
                float distance = math.distancesq(aiPosition, playerPosition);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPlayerPosition = playerPosition;
                    isPlayerPresent = true;
                }
            }

            if (isPlayerPresent)
            {
                float3 directionToPlayer = closestPlayerPosition - aiPosition;
                float distanceToPlayer = math.length(directionToPlayer);

                if (distanceToPlayer > 0.001f)
                {
                    float3 moveDirection = math.normalize(directionToPlayer);
                    movement.ValueRW.LinearVelocity = moveDirection;
                }
                else
                {
                    movement.ValueRW.LinearVelocity = float3.zero;
                }
            }
            else
            {
                movement.ValueRW.LinearVelocity = float3.zero;
            }
        }
    }
}