using Config;
using Player;
using Ship;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Boids
{
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct BoidsSimulationSystem : ISystem
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

            EntityQuery aiShipQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, ShipMovement, Ship.Ship>()
                .WithAbsent<PlayerTag>()
                .Build();

            int shipCount = aiShipQuery.CalculateEntityCount();
            if (shipCount == 0) return;

            WorldUnmanaged world = state.WorldUnmanaged;

            float3 playerPosition = float3.zero;
            bool hasPlayer = false;
            foreach (RefRO<LocalTransform> playerTransform in SystemAPI.Query<RefRO<LocalTransform>>()
                         .WithAll<PlayerTag>())
            {
                playerPosition = playerTransform.ValueRO.Position;
                hasPlayer = true;
                break;
            }

            NativeArray<float3> shipPositions =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(shipCount, ref world.UpdateAllocator);
            NativeArray<float3> shipVelocities =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(shipCount, ref world.UpdateAllocator);
            NativeArray<Entity> shipEntities =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(shipCount, ref world.UpdateAllocator);
            NativeArray<float> shipMaxSpeeds =
                CollectionHelper.CreateNativeArray<float, RewindableAllocator>(shipCount, ref world.UpdateAllocator);

            NativeArray<float3> shipNewVelocities =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(shipCount, ref world.UpdateAllocator);

            NativeParallelMultiHashMap<int, int> spatialHashMap =
                new NativeParallelMultiHashMap<int, int>(shipCount, world.UpdateAllocator.ToAllocator);

            GatherShipDataJob gatherDataJob = new GatherShipDataJob
            {
                Positions = shipPositions,
                Velocities = shipVelocities,
                Entities = shipEntities,
                MaxSpeeds = shipMaxSpeeds,
                SpatialHashMap = spatialHashMap.AsParallelWriter(),
                CellSize = boidConfig.CellSize,
            };
            JobHandle gatherHandle = gatherDataJob.ScheduleParallel(aiShipQuery, state.Dependency);

            CalculateBoidForcesJob boidJob = new CalculateBoidForcesJob
            {
                Positions = shipPositions,
                InputVelocities = shipVelocities,
                MaxSpeeds = shipMaxSpeeds,
                SpatialHashMap = spatialHashMap,
                BoidConfig = boidConfig,
                TargetPosition = playerPosition,
                HasTarget = hasPlayer,
                OutputVelocities = shipNewVelocities,
            };
            JobHandle boidHandle = boidJob.Schedule(shipCount, 32, gatherHandle);

            ApplyNewVelocitiesJob applyJob = new ApplyNewVelocitiesJob
            {
                NewVelocities = shipNewVelocities,
                Entities = shipEntities,
                MovementLookup = SystemAPI.GetComponentLookup<ShipMovement>(false),
            };

            JobHandle applyHandle = applyJob.Schedule(shipCount, 32, boidHandle);

            state.Dependency = applyHandle;
        }

        [BurstCompile]
        private partial struct GatherShipDataJob : IJobEntity
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
                in Ship.Ship ship)
            {
                Positions[entityIndexInQuery] = transform.Position;
                Velocities[entityIndexInQuery] = movement.LinearVelocity;
                Entities[entityIndexInQuery] = entity;
                MaxSpeeds[entityIndexInQuery] = ship.MoveSpeed;

                int hash = SpatialHashUtils.GetSpatialHash(transform.Position, CellSize);
                SpatialHashMap.Add(hash, entityIndexInQuery);
            }
        }

        [BurstCompile]
        private struct CalculateBoidForcesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Positions;
            [ReadOnly] public NativeArray<float3> InputVelocities;
            [ReadOnly] public NativeArray<float> MaxSpeeds;

            [ReadOnly] public NativeParallelMultiHashMap<int, int> SpatialHashMap;
            [ReadOnly] public BoidConfig BoidConfig;

            [ReadOnly] public bool HasTarget;
            [ReadOnly] public float3 TargetPosition;

            [WriteOnly] public NativeArray<float3> OutputVelocities;

            public void Execute(int index)
            {
                float3 currentPosition = Positions[index];
                float3 currentVelocity = InputVelocities[index];
                float maxSpeed = MaxSpeeds[index];

                float3 separation = CalculateSeparation(currentPosition, index);
                float3 alignment = CalculateAlignment(currentPosition, currentVelocity, index);
                float3 cohesion = CalculateCohesion(currentPosition, index);
                float3 targetSeek = CalculateTargetSeek(currentPosition);

                float3 totalForce =
                    separation * BoidConfig.SeparationWeight +
                    alignment * BoidConfig.AlignmentWeight +
                    cohesion * BoidConfig.CohesionWeight +
                    targetSeek;

                if (math.lengthsq(totalForce) > BoidConfig.MaxSteerForce * BoidConfig.MaxSteerForce)
                {
                    totalForce = math.normalizesafe(totalForce) * BoidConfig.MaxSteerForce;
                }

                float3 newVelocity = currentVelocity + totalForce;
                if (math.lengthsq(newVelocity) > maxSpeed * maxSpeed)
                {
                    newVelocity = math.normalizesafe(newVelocity) * maxSpeed;
                }

                OutputVelocities[index] = newVelocity;
            }

            private float3 CalculateTargetSeek(float3 currentPosition)
            {
                float3 targetSeek = float3.zero;
                if (HasTarget)
                {
                    float3 toTarget = TargetPosition - currentPosition;
                    float distanceSq = math.lengthsq(toTarget);

                    float stopRadiusSq = BoidConfig.TargetStopRadius * BoidConfig.TargetStopRadius;
                    float slowRadiusSq = BoidConfig.TargetSlowRadius * BoidConfig.TargetSlowRadius;

                    if (distanceSq > slowRadiusSq)
                    {
                        targetSeek = math.normalizesafe(toTarget) * BoidConfig.TargetSeekWeight;
                    }
                    else if (distanceSq > stopRadiusSq)
                    {
                        float distance = math.sqrt(distanceSq);
                        float slowFactor = (distance - BoidConfig.TargetStopRadius) /
                                           (BoidConfig.TargetSlowRadius - BoidConfig.TargetStopRadius);
                        targetSeek = math.normalizesafe(toTarget) * BoidConfig.TargetSeekWeight * slowFactor;
                    }
                    else if (distanceSq > 0.000001f)
                    {
                        targetSeek = -math.normalizesafe(toTarget) * BoidConfig.ObstacleAvoidanceWeight;
                    }
                }

                return targetSeek;
            }

            private float3 CalculateSeparation(float3 position, int currentIndex)
            {
                float3 velocity = float3.zero;
                int neighborCount = 0;
                float separationRadiusSq = BoidConfig.SeparationRadius * BoidConfig.SeparationRadius;

                NativeList<int> neighborIndexes = new NativeList<int>(Allocator.Temp);
                SpatialHashUtils.AddNeighborIndexes(ref SpatialHashMap, ref position, BoidConfig.CellSize,
                    BoidConfig.CellCheckRadius, ref neighborIndexes);

                for (int i = 0; i < neighborIndexes.Length; i++)
                {
                    int neighborIndex = neighborIndexes[i];

                    if (neighborIndex == currentIndex)
                        continue;

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

                NativeList<int> neighborIndexes = new NativeList<int>(Allocator.Temp);
                SpatialHashUtils.AddNeighborIndexes(ref SpatialHashMap, ref position, BoidConfig.CellSize,
                    BoidConfig.CellCheckRadius, ref neighborIndexes);

                for (int i = 0; i < neighborIndexes.Length; i++)
                {
                    int neighborIndex = neighborIndexes[i];

                    if (neighborIndex == currentIndex)
                        continue;

                    float distSq = math.distancesq(position, Positions[neighborIndex]);
                    if (distSq < neighborRadiusSq)
                    {
                        velocity += InputVelocities[neighborIndex];
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

                NativeList<int> neighborIndexes = new NativeList<int>(Allocator.Temp);
                SpatialHashUtils.AddNeighborIndexes(ref SpatialHashMap, ref position, BoidConfig.CellSize,
                    BoidConfig.CellCheckRadius, ref neighborIndexes);

                for (int i = 0; i < neighborIndexes.Length; i++)
                {
                    int neighborIndex = neighborIndexes[i];

                    if (neighborIndex == currentIndex)
                        continue;

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
        }

        [BurstCompile]
        private struct ApplyNewVelocitiesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<float3> NewVelocities;

            //Only accessing by the current index, so it's ok for parallel
            [NativeDisableParallelForRestriction] 
            public ComponentLookup<ShipMovement> MovementLookup;

            public void Execute(int index)
            {
                Entity entity = Entities[index];

                if (MovementLookup.HasComponent(entity))
                {
                    RefRW<ShipMovement> movement = MovementLookup.GetRefRW(entity);
                    movement.ValueRW.LinearVelocity = NewVelocities[index];
                }
            }
        }
    }
}