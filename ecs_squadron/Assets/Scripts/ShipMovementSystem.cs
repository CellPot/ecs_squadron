using Config;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Jobs;
using UnityEngine;

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
        WorldConfig worldConfig = SystemAPI.GetSingleton<WorldConfig>();
        BoidConfig boidConfig = worldConfig.BoidConfig;

        var aiShipQuery = SystemAPI.QueryBuilder()
            .WithAll<LocalTransform, ShipMovement, Ship>()
            .WithAbsent<PlayerTag>()
            .Build();

        int shipCount = aiShipQuery.CalculateEntityCount();
        if (shipCount == 0) return;

        var world = state.WorldUnmanaged;

        float3 playerPosition = float3.zero;
        bool hasPlayer = false;
        foreach (var playerTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
        {
            playerPosition = playerTransform.ValueRO.Position;
            hasPlayer = true;
            break;
        }

        var shipPositions =
            CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(shipCount, ref world.UpdateAllocator);
        var shipVelocities =
            CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(shipCount, ref world.UpdateAllocator);
        var shipEntities =
            CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(shipCount, ref world.UpdateAllocator);
        var shipMaxSpeeds =
            CollectionHelper.CreateNativeArray<float, RewindableAllocator>(shipCount, ref world.UpdateAllocator);
        var spatialHashMap = new NativeParallelMultiHashMap<int, int>(shipCount, world.UpdateAllocator.ToAllocator);

        var gatherDataJob = new GatherShipDataJob
        {
            Positions = shipPositions,
            Velocities = shipVelocities,
            Entities = shipEntities,
            MaxSpeeds = shipMaxSpeeds,
            SpatialHashMap = spatialHashMap.AsParallelWriter(),
            CellSize = boidConfig.CellSize,
        };
        var gatherHandle = gatherDataJob.ScheduleParallel(aiShipQuery, state.Dependency);

        var boidJob = new CalculateBoidForcesJob
        {
            Positions = shipPositions,
            Velocities = shipVelocities,
            Entities = shipEntities,
            MaxSpeeds = shipMaxSpeeds,
            SpatialHashMap = spatialHashMap,
            BoidConfig = boidConfig,
            TargetPosition = playerPosition,
            HasTarget = hasPlayer,
            MovementLookup = SystemAPI.GetComponentLookup<ShipMovement>(false),
        };
        var boidHandle = boidJob.Schedule(shipCount, 32, gatherHandle);

        state.Dependency = boidHandle;
    }

    [BurstCompile]
    partial struct GatherShipDataJob : IJobEntity
    {
        public NativeArray<float3> Positions;
        public NativeArray<float3> Velocities;
        public NativeArray<Entity> Entities;
        public NativeArray<float> MaxSpeeds;
        public NativeParallelMultiHashMap<int, int>.ParallelWriter SpatialHashMap;
        public float CellSize;

        void Execute([EntityIndexInQuery] int entityIndexInQuery,
            Entity entity,
            in LocalTransform transform,
            in ShipMovement movement,
            in Ship ship)
        {
            Positions[entityIndexInQuery] = transform.Position;
            Velocities[entityIndexInQuery] = movement.LinearVelocity;
            Entities[entityIndexInQuery] = entity;
            MaxSpeeds[entityIndexInQuery] = ship.MoveSpeed;

            var hash = SpatialHashUtils.GetSpatialHash(transform.Position, CellSize);
            SpatialHashMap.Add(hash, entityIndexInQuery);
        }
    }

    [BurstCompile]
    struct CalculateBoidForcesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> Velocities;
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<float> MaxSpeeds;

        [ReadOnly] public NativeParallelMultiHashMap<int, int> SpatialHashMap;
        [ReadOnly] public BoidConfig BoidConfig;

        [ReadOnly] public bool HasTarget;
        [ReadOnly] public float3 TargetPosition;

        //TODO: мб вместо лукапа заполнять вовне? Хотя каждый ship проверяется всегда только одним тредом
        [NativeDisableParallelForRestriction] public ComponentLookup<ShipMovement> MovementLookup;

        public void Execute(int index)
        {
            var currentPosition = Positions[index];
            var currentVelocity = Velocities[index];
            var currentEntity = Entities[index];
            var maxSpeed = MaxSpeeds[index];

            float3 separation = CalculateSeparation(currentPosition, index);
            float3 alignment = CalculateAlignment(currentPosition, currentVelocity, index);
            float3 cohesion = CalculateCohesion(currentPosition, index);
            float3 targetSeek = CalculateTargetSeek(currentPosition);

            var totalForce =
                separation * BoidConfig.SeparationWeight +
                alignment * BoidConfig.AlignmentWeight +
                cohesion * BoidConfig.CohesionWeight +
                targetSeek;

            if (math.lengthsq(totalForce) > BoidConfig.MaxSteerForce * BoidConfig.MaxSteerForce)
            {
                totalForce = math.normalizesafe(totalForce) * BoidConfig.MaxSteerForce;
            }

            var newVelocity = currentVelocity + totalForce;
            if (math.lengthsq(newVelocity) > maxSpeed * maxSpeed)
            {
                newVelocity = math.normalizesafe(newVelocity) * maxSpeed;
            }

            var movement = MovementLookup[currentEntity];
            movement.LinearVelocity = newVelocity;
            MovementLookup[currentEntity] = movement;
        }

        private float3 CalculateTargetSeek(float3 currentPosition)
        {
            float3 targetSeek = float3.zero;
            if (HasTarget)
            {
                var toPlayer = TargetPosition - currentPosition;
                if (math.lengthsq(toPlayer) > 0.001f)
                {
                    targetSeek = BoidConfig.TargetSeekWeight * math.normalizesafe(toPlayer);
                }
            }

            return targetSeek;
        }

        private float3 CalculateSeparation(float3 position, int currentIndex)
        {
            float3 velocity = float3.zero;
            int neighborCount = 0;
            float separationRadiusSq = BoidConfig.SeparationRadius * BoidConfig.SeparationRadius;

            var neighborIndexes = new NativeList<int>(Allocator.Temp);
            SpatialHashUtils.AddNeighborIndexes(ref SpatialHashMap, ref position, BoidConfig.CellSize,
                BoidConfig.CellCheckRadius, ref neighborIndexes);

            for (int i = 0; i < neighborIndexes.Length; i++)
            {
                int neighborIndex = neighborIndexes[i];
                if (neighborIndex == currentIndex) continue;

                float3 diff = position - Positions[neighborIndex];
                float distSq = math.lengthsq(diff);

                if (distSq > 0.001f && distSq < separationRadiusSq)
                {
                    float3 normalized = math.normalizesafe(diff);
                    normalized /= math.sqrt(distSq);
                    velocity += normalized;
                    neighborCount++;
                }
            }

            neighborIndexes.Dispose();

            if (neighborCount > 0)
            {
                velocity /= neighborCount;
                return math.normalizesafe(velocity);
            }

            return float3.zero;
        }

        private float3 CalculateAlignment(float3 position, float3 currentVelocity, int currentIndex)
        {
            float3 velocity = float3.zero;
            int neighborCount = 0;
            float neighborRadiusSq = BoidConfig.CellSize * BoidConfig.CellSize;

            var neighborIndexes = new NativeList<int>(Allocator.Temp);
            SpatialHashUtils.AddNeighborIndexes(ref SpatialHashMap, ref position, BoidConfig.CellSize,
                BoidConfig.CellCheckRadius, ref neighborIndexes);

            for (int i = 0; i < neighborIndexes.Length; i++)
            {
                int neighborIndex = neighborIndexes[i];
                if (neighborIndex == currentIndex) continue;

                float distSq = math.distancesq(position, Positions[neighborIndex]);
                if (distSq < neighborRadiusSq)
                {
                    velocity += Velocities[neighborIndex];
                    neighborCount++;
                }
            }

            neighborIndexes.Dispose();

            if (neighborCount > 0)
            {
                velocity /= neighborCount;
                if (math.lengthsq(velocity) > 0.001f)
                {
                    velocity = math.normalizesafe(velocity);
                }

                return velocity - math.normalizesafe(currentVelocity);
            }

            return float3.zero;
        }

        private float3 CalculateCohesion(float3 position, int currentIndex)
        {
            float3 velocity = float3.zero;
            int neighborCount = 0;
            float neighborRadiusSq = BoidConfig.CellSize * BoidConfig.CellSize;

            var neighborIndexes = new NativeList<int>(Allocator.Temp);
            SpatialHashUtils.AddNeighborIndexes(ref SpatialHashMap, ref position, BoidConfig.CellSize,
                BoidConfig.CellCheckRadius, ref neighborIndexes);

            for (int i = 0; i < neighborIndexes.Length; i++)
            {
                int neighborIndex = neighborIndexes[i];
                if (neighborIndex == currentIndex) continue;

                float distSq = math.distancesq(position, Positions[neighborIndex]);
                if (distSq < neighborRadiusSq)
                {
                    velocity += Positions[neighborIndex];
                    neighborCount++;
                }
            }

            neighborIndexes.Dispose();

            if (neighborCount > 0)
            {
                velocity /= neighborCount;
                float3 desired = velocity - position;
                if (math.lengthsq(desired) > 0.001f)
                {
                    return math.normalizesafe(desired);
                }
            }

            return float3.zero;
        }

        // private NativeList<int> GetNearbyShipsOld(float3 position)
        // {
        //     var nearbyShips = new NativeList<int>(Allocator.Temp);
        //
        //     for (int dx = -CellCheckRadius; dx <= CellCheckRadius; dx++)
        //     {
        //         for (int dy = -CellCheckRadius; dy <= CellCheckRadius; dy++)
        //         {
        //             var offsetPos = position + new float3(dx * CellSize, dy * CellSize, 0);
        //             var hash = SpatialHashUtils.GetSpatialHash(offsetPos, CellSize);
        //
        //             if (SpatialHashMap.TryGetFirstValue(hash, out int shipIndex, out var iterator))
        //             {
        //                 do
        //                 {
        //                     nearbyShips.Add(shipIndex);
        //                 } 
        //                 while (SpatialHashMap.TryGetNextValue(out shipIndex, ref iterator));
        //             }
        //         }
        //     }
        //
        //     return nearbyShips;
        // }

        // private static int GetSpatialHash(float3 position, float cellSize)
        // {
        //     int3 gridPos = new int3(math.floor(position / cellSize));
        //     return GetUniqueHashKey(gridPos);
        //
        //     int GetUniqueHashKey(int3 int3) => 
        //         (int)math.hash(int3);
        // }
    }
}

[BurstCompile]
public static class SpatialHashUtils
{
    [BurstCompile]
    public static int GetSpatialHash(in float3 position, float cellSize)
    {
        int3 gridPos = new int3(math.floor(position / cellSize));
        return GetUniqueHashKey(gridPos);

        int GetUniqueHashKey(int3 int3) =>
            (int)math.hash(int3);
    }

    [BurstCompile]
    public static void AddNeighborIndexes(
        [ReadOnly] ref NativeParallelMultiHashMap<int, int> spatialHashMap,
        [ReadOnly] ref float3 position,
        float cellSize,
        int searchRadius,
        [WriteOnly] ref NativeList<int> neighborIndexes)
    {
        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                var offsetPos = position + new float3(dx * cellSize, dy * cellSize, 0);
                var hash = GetSpatialHash(offsetPos, cellSize);

                if (spatialHashMap.TryGetFirstValue(hash, out int shipIndex, out var iterator))
                {
                    do
                    {
                        neighborIndexes.Add(shipIndex);
                    } while (spatialHashMap.TryGetNextValue(out shipIndex, ref iterator));
                }
            }
        }
    }
}