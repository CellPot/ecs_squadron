using Boids;
using Combat.Weapon.Projectile;
using Config;
using Ship;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Weapon
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TargetingSystem : ISystem
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

            EntityQuery weaponQuery = SystemAPI.QueryBuilder()
                .WithAll<ProjectileWeapon, LocalTransform, Faction>()
                .Build();

            EntityQuery targetableQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, Faction>()
                .WithAny<Ship.Ship>()
                .Build();

            int weaponCount = weaponQuery.CalculateEntityCount();
            int targetCount = targetableQuery.CalculateEntityCount();

            if (weaponCount == 0 || targetCount == 0) return;

            WorldUnmanaged world = state.WorldUnmanaged;

            NativeArray<Entity> weaponEntities =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);
            NativeArray<float3> weaponPositions =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);
            NativeArray<float> weaponRanges =
                CollectionHelper.CreateNativeArray<float, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);
            NativeArray<int> weaponFactions =
                CollectionHelper.CreateNativeArray<int, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);

            NativeArray<Entity> targetEntities =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(targetCount, ref world.UpdateAllocator);
            NativeArray<float3> targetPositions =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(targetCount, ref world.UpdateAllocator);
            NativeArray<int> targetFactions =
                CollectionHelper.CreateNativeArray<int, RewindableAllocator>(targetCount, ref world.UpdateAllocator);

            NativeParallelMultiHashMap<int, int> targetSpatialHash =
                new NativeParallelMultiHashMap<int, int>(targetCount, world.UpdateAllocator.ToAllocator);

            NativeArray<Entity> targetResults =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);

            GatherWeaponDataJob gatherWeaponsJob = new GatherWeaponDataJob
            {
                WeaponEntities = weaponEntities,
                WeaponPositions = weaponPositions,
                WeaponRanges = weaponRanges,
                WeaponFactions = weaponFactions
            };
            JobHandle gatherWeaponsHandle = gatherWeaponsJob.ScheduleParallel(weaponQuery, state.Dependency);

            GatherTargetDataJob gatherTargetsJob = new GatherTargetDataJob
            {
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetFactions = targetFactions,
                SpatialHash = targetSpatialHash.AsParallelWriter(),
                CellSize = worldConfig.CombatConfig.WeaponTargetSearchCellSize,
            };
            JobHandle gatherTargetsHandle = gatherTargetsJob.ScheduleParallel(targetableQuery, gatherWeaponsHandle);

            FindTargetsJob findTargetsJob = new FindTargetsJob
            {
                WeaponEntities = weaponEntities,
                WeaponPositions = weaponPositions,
                WeaponRanges = weaponRanges,
                WeaponFactions = weaponFactions,
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetFactions = targetFactions,
                TargetSpatialHash = targetSpatialHash,
                CellSize = worldConfig.CombatConfig.WeaponTargetSearchCellSize,
                TargetResults = targetResults
            };
            JobHandle findTargetsHandle = findTargetsJob.Schedule(weaponCount, 32, gatherTargetsHandle);

            ApplyTargetingResultsJob applyResultsJob = new ApplyTargetingResultsJob
            {
                WeaponEntities = weaponEntities,
                TargetResults = targetResults,
                WeaponLookup = SystemAPI.GetComponentLookup<ProjectileWeapon>(false)
            };
            JobHandle applyResultsHandle = applyResultsJob.Schedule(weaponCount, 64, findTargetsHandle);

            state.Dependency = applyResultsHandle;
        }

        [BurstCompile]
        private partial struct GatherWeaponDataJob : IJobEntity
        {
            public NativeArray<Entity> WeaponEntities;
            public NativeArray<float3> WeaponPositions;
            public NativeArray<float> WeaponRanges;
            public NativeArray<int> WeaponFactions;

            void Execute([EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in ProjectileWeapon weapon,
                in LocalTransform transform,
                in Faction faction)
            {
                WeaponEntities[entityIndexInQuery] = entity;
                WeaponPositions[entityIndexInQuery] = transform.Position;
                WeaponRanges[entityIndexInQuery] = weapon.AttackRange;
                WeaponFactions[entityIndexInQuery] = faction.FactionId;
            }
        }

        [BurstCompile]
        private partial struct GatherTargetDataJob : IJobEntity
        {
            public NativeArray<Entity> TargetEntities;
            public NativeArray<float3> TargetPositions;
            public NativeArray<int> TargetFactions;
            public NativeParallelMultiHashMap<int, int>.ParallelWriter SpatialHash;
            [ReadOnly] public float CellSize;

            void Execute([EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in LocalTransform transform,
                in Faction faction)
            {
                TargetEntities[entityIndexInQuery] = entity;
                TargetPositions[entityIndexInQuery] = transform.Position;
                TargetFactions[entityIndexInQuery] = faction.FactionId;

                int hash = SpatialHashUtils.GetSpatialHash(transform.Position, CellSize);
                SpatialHash.Add(hash, entityIndexInQuery);
            }
        }

        [BurstCompile]
        private struct FindTargetsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> WeaponEntities;
            [ReadOnly] public NativeArray<float3> WeaponPositions;
            [ReadOnly] public NativeArray<float> WeaponRanges;
            [ReadOnly] public NativeArray<int> WeaponFactions;

            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<float3> TargetPositions;
            [ReadOnly] public NativeArray<int> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<int, int> TargetSpatialHash;
            [ReadOnly] public float CellSize;

            [NativeDisableParallelForRestriction] public NativeArray<Entity> TargetResults;

            public void Execute(int weaponIndex)
            {
                Entity weaponEntity = WeaponEntities[weaponIndex];
                float3 weaponPos = WeaponPositions[weaponIndex];
                float weaponRange = WeaponRanges[weaponIndex];
                int weaponFaction = WeaponFactions[weaponIndex];

                Entity closestTarget = Entity.Null;
                float closestDistanceSq = weaponRange * weaponRange;

                NativeList<int> nearbyTargetIndices = new NativeList<int>(Allocator.Temp);

                //Instead of pre-defined cell radius weapon range is used
                int searchRadius = math.max(1, (int)math.ceil(weaponRange / CellSize));
                SpatialHashUtils.AddNeighborIndexes(ref TargetSpatialHash, ref weaponPos, CellSize, searchRadius,
                    ref nearbyTargetIndices);

                for (int i = 0; i < nearbyTargetIndices.Length; i++)
                {
                    int targetIndex = nearbyTargetIndices[i];
                    Entity targetEntity = TargetEntities[targetIndex];

                    if (targetEntity == weaponEntity)
                        continue;

                    if (TargetFactions[targetIndex] == weaponFaction)
                        continue;

                    float3 targetPos = TargetPositions[targetIndex];
                    float distanceSq = math.distancesq(weaponPos, targetPos);

                    if (distanceSq < closestDistanceSq)
                    {
                        closestDistanceSq = distanceSq;
                        closestTarget = targetEntity;
                    }
                }

                nearbyTargetIndices.Dispose();
                TargetResults[weaponIndex] = closestTarget;
            }
        }

        [BurstCompile]
        private struct ApplyTargetingResultsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> WeaponEntities;
            [ReadOnly] public NativeArray<Entity> TargetResults;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileWeapon> WeaponLookup;

            public void Execute(int index)
            {
                Entity weaponEntity = WeaponEntities[index];
                Entity target = TargetResults[index];

                if (WeaponLookup.HasComponent(weaponEntity))
                {
                    RefRW<ProjectileWeapon> weapon = WeaponLookup.GetRefRW(weaponEntity);
                    weapon.ValueRW.Target = target;
                }
            }
        }
    }
}