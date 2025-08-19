using Config;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
public partial struct ShipMovementSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // state.RequireForUpdate<PlayerTag>();
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
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct AIShipSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        WorldConfig worldConfig = SystemAPI.GetSingleton<WorldConfig>();
        BoidConfig boidConfig = worldConfig.BoidConfig;

        float3 playerPosition = float3.zero;
        bool hasPlayer = false;

        foreach (var playerTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
        {
            playerPosition = playerTransform.ValueRO.Position;
            hasPlayer = true;
            break;
        }

        var aiShips = new NativeList<AIShipData>(Allocator.Temp);

        foreach (var (transform, movement, ship, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<ShipMovement>, RefRO<Ship>>()
                     .WithAbsent<PlayerTag>()
                     .WithEntityAccess())
        {
            aiShips.Add(new AIShipData
            {
                Entity = entity,
                Position = transform.ValueRO.Position,
                Velocity = movement.ValueRO.LinearVelocity,
                MaxSpeed = ship.ValueRO.MoveSpeed
            });
        }

        // Фактически, набор компонентов в системе, к которым можно обратиться по сущности
        ComponentLookup<ShipMovement> movementLookup = SystemAPI.GetComponentLookup<ShipMovement>(false);

        for (int i = 0; i < aiShips.Length; i++)
        {
            AIShipData currentShip = aiShips[i];
            float3 position = currentShip.Position;
            float3 velocity = currentShip.Velocity;
            float maxSpeed = currentShip.MaxSpeed;

            // Calculate boid forces using config parameters
            float3 separationForce = CalculateSeparation(position, i, aiShips, boidConfig.SeparationRadius);
            float3 alignmentForce = CalculateAlignment(velocity, position, i, aiShips, boidConfig.NeighborRadius);
            float3 cohesionForce = CalculateCohesion(position, i, aiShips, boidConfig.NeighborRadius);
            float3 toPlayerDir = float3.zero;

            //TODO: переделать под ближайшего игрока (цель) к каждому из кораблей
            if (hasPlayer)
            {
                float3 toPlayerVec = playerPosition - position;
                if (math.lengthsq(toPlayerVec) > 0.001f)
                {
                    toPlayerDir = math.normalize(toPlayerVec);
                }
            }

            float3 totalForce =
                separationForce * boidConfig.SeparationWeight +
                alignmentForce * boidConfig.AlignmentWeight +
                cohesionForce * boidConfig.CohesionWeight +
                toPlayerDir * boidConfig.TargetSeekWeight;

            if (math.lengthsq(totalForce) > boidConfig.MaxSteerForce * boidConfig.MaxSteerForce)
            {
                totalForce = math.normalize(totalForce) * boidConfig.MaxSteerForce;
            }

            // Вектор силы + сила поворота в нормализованном виде, ограниченная макс. скоростью
            float3 newVelocity = totalForce + velocity; //* deltaTime;
            if (math.lengthsq(newVelocity) > maxSpeed * maxSpeed)
            {
                newVelocity = math.normalize(newVelocity) * maxSpeed;
            }
            
            //TODO: мб напрямую обновлять movement в основном цикле?
            ShipMovement movement = movementLookup[currentShip.Entity];
            movement.LinearVelocity = newVelocity;
            movementLookup[currentShip.Entity] = movement;
        }

        aiShips.Dispose();
    }

    private float3 CalculateSeparation(float3 position, int currentIndex, NativeList<AIShipData> ships, float radius)
    {
        float3 accumulatedVec = float3.zero;
        int inRadiusCnt = 0;
        float radiusSq = radius * radius;

        for (int i = 0; i < ships.Length; i++)
        {
            if (i == currentIndex) continue;

            float3 diff = position - ships[i].Position;
            float distSq = math.lengthsq(diff);

            //Нужна ли проверка на 0? Одна позиция = все равно ошибка
            if (distSq > 0 && distSq < radiusSq)
            {
                float3 awayDir = math.normalize(diff);
                awayDir /= math.sqrt(distSq); 
                accumulatedVec += awayDir;
                inRadiusCnt++;
            }
        }

        if (inRadiusCnt > 0)
        {
            accumulatedVec /= inRadiusCnt;
            if (math.lengthsq(accumulatedVec) > 0.001f)
            {
                accumulatedVec = math.normalize(accumulatedVec);//TODO: normalizesafe?
            }
        }

        return accumulatedVec;
    }

    private float3 CalculateAlignment(float3 currentVelocity, float3 position, int currentIndex,
        NativeList<AIShipData> ships, float radius)
    {
        float3 accumulatedVec = float3.zero;
        int inRadiusCnt = 0;
        float radiusSq = radius * radius;

        for (int i = 0; i < ships.Length; i++)
        {
            if (i == currentIndex) continue;

            float distSq = math.distancesq(position, ships[i].Position);
            if (distSq < radiusSq)
            {
                accumulatedVec += ships[i].Velocity;
                inRadiusCnt++;
            }
        }

        if (inRadiusCnt > 0)
        {
            accumulatedVec /= inRadiusCnt;
            if (math.lengthsq(accumulatedVec) > 0.001f)
            {
                accumulatedVec = math.normalize(accumulatedVec);
            }

            //направление с текущего вектора в желаемый (руление)
            return accumulatedVec - math.normalize(currentVelocity);
        }

        return float3.zero;
    }

    private float3 CalculateCohesion(float3 position, int currentIndex, NativeList<AIShipData> ships, float radius)
    {
        float3 accumulatedVec = float3.zero;
        int inRadiusCnt = 0;
        float radiusSq = radius * radius;

        for (int i = 0; i < ships.Length; i++)
        {
            if (i == currentIndex) continue;

            float distSq = math.distancesq(position, ships[i].Position);
            if (distSq < radiusSq)
            {
                accumulatedVec += ships[i].Position;
                inRadiusCnt++;
            }
        }

        if (inRadiusCnt > 0)
        {
            accumulatedVec /= inRadiusCnt;
            float3 centerDir = accumulatedVec - position;
            if (math.lengthsq(centerDir) > 0.001f)
            {
                return math.normalize(centerDir);
            }
        }

        return float3.zero;
    }

    private struct AIShipData
    {
        public Entity Entity;
        public float3 Position;
        public float3 Velocity;
        public float MaxSpeed;
    }
}
